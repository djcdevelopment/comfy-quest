using ComfyQuestContracts;
using Newtonsoft.Json;

namespace Comfy.Quest.Studio;

public sealed record StudioRunResetRequest(string RunId, string? PreviewToken = null, bool ConfirmReset = false);
public sealed record StudioSelectExperienceRequest(string? ExperienceId);
public sealed record StudioBindExperienceRequest(string? ExperienceId, string? BindingZdo);
public sealed record StudioRestoreBindingRequest(string? BindingZdo, string? BindingChangeId);
public sealed record StudioRunStatusView(
    int SchemaVersion,
    bool Available,
    bool Connected,
    string? Error,
    string? Machine,
    string? WorldUid,
    IReadOnlyList<RuntimeRunStatusEntry> Runs);
public sealed record StudioRunControlResult(
    bool Ok,
    bool Queued,
    string? Error,
    RuntimeRunControlReceipt? Receipt,
    string? RequestId = null);

internal sealed class QuestStudioRunControl
{
    const int MaxStatusBytes = RuntimeRunStatusStore.MaxStatusBytes;
    const int MaxReceiptBytes = 1024 * 1024;
    readonly IQuestStudioHost _host;
    readonly QuestStudioWorkspace _workspace;

    public QuestStudioRunControl(IQuestStudioHost host, QuestStudioWorkspace workspace)
    {
        _host = host;
        _workspace = workspace;
    }

    public StudioRunStatusView Status(string projectId)
    {
        var project = _workspace.ReadProject(projectId);
        if (project is null) return new(1, false, false, "project_missing", null, null, Array.Empty<RuntimeRunStatusEntry>());
        var root = RuntimeRoot();
        if (root is null) return new(1, false, false, "valheim_not_found", null, null, Array.Empty<RuntimeRunStatusEntry>());
        var path = Path.Combine(root, "status", "runs.json");
        RuntimeRunStatusDocument? status;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaxStatusBytes)
                return new(1, true, false, "runtime_run_status_missing", null, null, Array.Empty<RuntimeRunStatusEntry>());
            // Runtime replaces this heartbeat atomically while Studio polls it. Read with delete
            // sharing so a dashboard refresh cannot make Runtime's next File.Replace fail on Windows.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            status = JsonConvert.DeserializeObject<RuntimeRunStatusDocument>(reader.ReadToEnd());
        }
        catch { return new(1, true, false, "runtime_run_status_unreadable", null, null, Array.Empty<RuntimeRunStatusEntry>()); }
        if (status?.Schema != "comfy-quest-runtime-run-status/v1")
            return new(1, true, false, "runtime_run_status_schema_invalid", null, null, Array.Empty<RuntimeRunStatusEntry>());
        var reported = status.Runs ?? Array.Empty<RuntimeRunStatusEntry>();
        if (reported.Count > 64 || reported.Any(value => value is null
                || string.IsNullOrWhiteSpace(value.RunId)
                || string.IsNullOrWhiteSpace(value.ScopeId)
                || string.IsNullOrWhiteSpace(value.ExperienceId)))
            return new(1, true, false, "runtime_run_status_invalid", status.Machine, status.WorldUid, Array.Empty<RuntimeRunStatusEntry>());
        var runs = reported.Where(value => string.Equals(value.ExperienceId, project.ExperienceId, StringComparison.Ordinal)).ToArray();
        if (runs.GroupBy(value => value.RunId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            return new(1, true, false, "runtime_run_status_ambiguous", status.Machine, status.WorldUid, Array.Empty<RuntimeRunStatusEntry>());
        var fresh = status.ObservedUtc >= DateTimeOffset.UtcNow.AddSeconds(-3) && status.ObservedUtc <= DateTimeOffset.UtcNow.AddSeconds(1);
        var machineKnown = !string.IsNullOrWhiteSpace(status.Machine) && status.Machine.Length <= 80;
        var worldLoaded = long.TryParse(status.WorldUid, out var worldUid) && worldUid != 0;
        var connected = fresh && machineKnown && worldLoaded;
        var error = !fresh ? "runtime_run_status_stale"
            : !machineKnown ? "runtime_machine_identity_invalid"
            : !worldLoaded ? "runtime_world_not_loaded"
            : null;
        return new(1, true, connected, error, status.Machine, status.WorldUid, runs);
    }

    public Task<StudioRunControlResult> PreviewAsync(string projectId, StudioRunResetRequest? request, CancellationToken cancellationToken) =>
        SendAsync(projectId, request, "preview_reset", cancellationToken);

    public Task<StudioRunControlResult> ApplyAsync(string projectId, StudioRunResetRequest? request, CancellationToken cancellationToken) =>
        SendAsync(projectId, request, "apply_reset", cancellationToken);

    /// <summary>Bind one experience of the activated pack. A guild ships several experiences in one
    /// pack and Runtime will not guess between them, so this is how the creator says which one is
    /// being played without republishing. It addresses the pack rather than a run, and so carries
    /// no run scope; machine, world, active content, and exact experience remain pinned.</summary>
    public Task<StudioRunControlResult> SelectExperienceAsync(string projectId, StudioSelectExperienceRequest? request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.ExperienceId))
            return Task.FromResult(new StudioRunControlResult(false, false, "experience_selection_required", null));
        var status = Status(projectId);
        if (!status.Available || !status.Connected)
            return Task.FromResult(new StudioRunControlResult(false, false, status.Error ?? "runtime_disconnected", null));
        var now = DateTimeOffset.UtcNow;
        return DispatchAsync(RuntimeRoot()!, new RuntimeRunControlRequest
        {
            RequestId = RequestId("select_experience", now),
            Operation = "select_experience",
            CreatedUtc = now.ToString("O"),
            ExpiresUtc = now.AddMinutes(2).ToString("O"),
            ExpectedMachine = status.Machine!,
            ExpectedWorldUid = status.WorldUid!,
            ExperienceId = request.ExperienceId,
        }, cancellationToken);
    }

    public Task<StudioRunControlResult> BindingCandidatesAsync(string projectId, CancellationToken cancellationToken) =>
        SendPackAsync(projectId, "list_binding_candidates", null, null, null, cancellationToken);

    public Task<StudioRunControlResult> BindExperienceAsync(string projectId, StudioBindExperienceRequest? request, CancellationToken cancellationToken)
    {
        var project = _workspace.ReadProject(projectId);
        if (project is null) return Task.FromResult(new StudioRunControlResult(false, false, "project_missing", null));
        if (request is null || !string.Equals(request.ExperienceId, project.ExperienceId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(request.BindingZdo))
            return Task.FromResult(new StudioRunControlResult(false, false, "binding_selection_required", null));
        return SendPackAsync(projectId, "bind_selected_experience", request.ExperienceId, request.BindingZdo, null, cancellationToken);
    }

    public Task<StudioRunControlResult> RestoreBindingAsync(string projectId, StudioRestoreBindingRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.BindingZdo) || string.IsNullOrWhiteSpace(request.BindingChangeId))
            return Task.FromResult(new StudioRunControlResult(false, false, "binding_restore_identity_required", null));
        return SendPackAsync(projectId, "restore_binding", null, request.BindingZdo, request.BindingChangeId, cancellationToken);
    }

    Task<StudioRunControlResult> SendPackAsync(string projectId, string operation, string? experienceId,
        string? bindingZdo, string? bindingChangeId, CancellationToken cancellationToken)
    {
        if (_workspace.ReadProject(projectId) is null)
            return Task.FromResult(new StudioRunControlResult(false, false, "project_missing", null));
        var status = Status(projectId);
        if (!status.Available || !status.Connected)
            return Task.FromResult(new StudioRunControlResult(false, false, status.Error ?? "runtime_disconnected", null));
        var now = DateTimeOffset.UtcNow;
        return DispatchAsync(RuntimeRoot()!, new RuntimeRunControlRequest
        {
            RequestId = RequestId(operation, now), Operation = operation,
            CreatedUtc = now.ToString("O"), ExpiresUtc = now.AddMinutes(2).ToString("O"),
            ExpectedMachine = status.Machine!, ExpectedWorldUid = status.WorldUid!,
            ExperienceId = experienceId, BindingZdo = bindingZdo, BindingChangeId = bindingChangeId,
        }, cancellationToken);
    }

    public StudioRunControlResult Receipt(string projectId, string? requestId, string? runId)
    {
        if (_workspace.ReadProject(projectId) is null) return new(false, false, "project_missing", null);
        var identity = new RuntimeRunControlRequest { RequestId = requestId };
        if (!RuntimeRunControlRequestPolicy.CanAddressReceipt(identity))
            return new(false, false, "run_control_identity_invalid", null);
        var root = RuntimeRoot();
        if (root is null) return new(false, false, "valheim_not_found", null, requestId);
        if (!TryReadScopedReceipt(root, runId, requestId!, out var receipt)) return new(true, true, null, null, requestId);
        var receiptRun = receipt!.Preview?.RunId ?? receipt.Result?.PriorRunId;
        if (receiptRun is not null && receiptRun != runId) return new(false, false, "run_control_scope_mismatch", null, requestId);
        var ok = receipt.State is "previewed" or "completed";
        return new(ok, false, ok ? null : receipt.Detail ?? receipt.State, receipt, requestId);
    }

    async Task<StudioRunControlResult> SendAsync(string projectId, StudioRunResetRequest? request, string operation, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RunId)) return new(false, false, "run_required", null);
        if (operation == "apply_reset" && (!request.ConfirmReset || string.IsNullOrWhiteSpace(request.PreviewToken)))
            return new(false, false, "reset_confirmation_required", null);
        var status = Status(projectId);
        if (!status.Available || !status.Connected) return new(false, false, status.Error ?? "runtime_disconnected", null);
        var run = status.Runs.SingleOrDefault(value => value.RunId == request.RunId);
        if (run is null) return new(false, false, "run_scope_not_loaded", null);
        var root = RuntimeRoot()!;
        var now = DateTimeOffset.UtcNow;
        return await DispatchAsync(root, new RuntimeRunControlRequest
        {
            RequestId = RequestId(operation, now),
            Operation = operation,
            CreatedUtc = now.ToString("O"),
            ExpiresUtc = now.AddMinutes(2).ToString("O"),
            ExpectedMachine = status.Machine!,
            ExpectedWorldUid = status.WorldUid!,
            RunId = request.RunId,
            PreviewToken = request.PreviewToken,
            ConfirmReset = request.ConfirmReset,
        }, cancellationToken);
    }

    static string RequestId(string operation, DateTimeOffset now) =>
        "studio-" + operation.Replace('_', '-') + "-"
        + now.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'") + "-"
        + Guid.NewGuid().ToString("N")[..8];

    /// <summary>One bounded request through the single-slot mailbox, then wait for its receipt.</summary>
    async Task<StudioRunControlResult> DispatchAsync(string root, RuntimeRunControlRequest body, CancellationToken cancellationToken)
    {
        var requestId = body.RequestId!;
        var mailbox = Path.Combine(root, "requests", "run-control.json");
        if (File.Exists(mailbox)) return new(false, false, "runtime_run_control_busy", null);
        Directory.CreateDirectory(Path.GetDirectoryName(mailbox)!);
        var temporary = mailbox + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonConvert.SerializeObject(body, Formatting.Indented));
            File.Move(temporary, mailbox);
        }
        catch (IOException) when (File.Exists(mailbox))
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            return new(false, false, "runtime_run_control_busy", null);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadScopedReceipt(root, body.RunId, requestId, out var receipt))
                return new(receipt!.State is "previewed" or "completed", false,
                    receipt.State is "previewed" or "completed" ? null : receipt.Detail ?? receipt.State,
                    receipt, requestId);
            await Task.Delay(100, cancellationToken);
        }
        return new(true, true, null, null, requestId);
    }

    /// <summary>Receipts are partitioned by run now (audit C3), so a reader has to know the scope.
    /// Studio always does — it is in the request it sent. Retention moves old bytes to the same
    /// partition under archive, and the legacy flat path remains readable for pre-partition installs.</summary>
    static bool TryReadScopedReceipt(string root, string? runId, string requestId, out RuntimeRunControlReceipt? receipt) =>
        TryReadReceipt(RuntimeRunControlReceipts.ReceiptPath(root, RuntimeRunControlReceipts.Scope(runId!), requestId), requestId, out receipt)
        || TryReadReceipt(RuntimeRunControlReceipts.ArchivedReceiptPath(root, RuntimeRunControlReceipts.Scope(runId!), requestId), requestId, out receipt)
        || TryReadReceipt(RuntimeRunControlReceipts.LegacyPath(root, requestId), requestId, out receipt);

    static bool TryReadReceipt(string path, string requestId, out RuntimeRunControlReceipt? receipt)
    {
        receipt = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaxReceiptBytes) return false;
            receipt = JsonConvert.DeserializeObject<RuntimeRunControlReceipt>(File.ReadAllText(path));
            return receipt?.Schema == "comfy-quest-runtime-run-control-receipt/v1" && receipt.RequestId == requestId;
        }
        catch { return false; }
    }

    string? RuntimeRoot()
    {
        var valheim = _host.FindValheim();
        return valheim is null ? null : Path.Combine(Path.GetFullPath(valheim), "BepInEx", "config", "comfy-quest-runtime");
    }
}

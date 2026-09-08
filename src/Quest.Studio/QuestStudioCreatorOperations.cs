using System.Security.Cryptography;
using System.Text.Json;
using ComfyQuestContracts;

namespace Comfy.Quest.Studio;

public sealed partial class QuestStudioService
{
    readonly CreatorOperationJournal _creatorOperations;
    readonly SemaphoreSlim _creatorControlGate = new(1, 1);
    readonly object _creatorEvidenceGate = new();

    async Task<T> SerializeCreatorControl<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _creatorControlGate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { _creatorControlGate.Release(); }
    }

    public StudioCreatorOperationAcceptance SubmitCreatorOperation(StudioCreatorOperationRequest? request) =>
        _creatorOperations.Submit(request, ValidateCreatorOperation, ExecuteCreatorOperationAsync);

    public StudioCreatorOperationRecord? CreatorOperation(string id)
    {
        var record = _creatorOperations.Get(id);
        if (record is not { State: "awaiting_runtime", Outcome.RequestId: not null }) return record;
        var operation = record.Request.Operation == "reset_preview" ? "preview_reset" : "apply_reset";
        var request = record.Request;
        var result = _runControl.ReceiptPinned(request.ProjectId, record.Outcome.RequestId, request.RunId,
            new(request.ExpectedMachine, request.ExpectedWorldUid, request.CreatorSessionId), operation);
        if (result.Receipt is not null)
            _creatorOperations.CompletePending(id, ControlOutcome(result));
        return _creatorOperations.Get(id);
    }

    public object? CreatorContext(string projectId)
    {
        if (!CreatorOperationJournal.Safe(projectId)) return null;
        var project = _workspace.ReadProject(projectId);
        if (project is null) return null;
        var target = _creator.LatestTarget(projectId, project.Revision);
        var scene = target is null ? null : _creator.LatestScene(target.WorldLink.SnapshotFileSha256);
        var runs = _runControl.Status(projectId);
        var session = CreatorSessionIdentity();
        var operations = _creatorOperations.ForProject(projectId)
            .TakeLast(32).Select(value => CreatorOperation(value.OperationId)).ToArray();
        return new
        {
            schema = "comfy-quest-creator-context/v1", ok = true,
            project = new { project.ProjectId, project.Title, project.Revision,
                project.ExperienceId, project.PackId, project.Version },
            studio_path = "/quest-studio?project=" + Uri.EscapeDataString(projectId),
            session,
            target = target is null ? null : new { target.TargetId, target.SceneId,
                target.RouteId, target.BindingZdo, target.AnchorSha256, target.WorldLink },
            scene,
            scene_path = scene is null ? null : "/api/v2/quest-studio/creator/scenes/" + scene.SceneId,
            runtime = RuntimeStatusView(projectId), runs,
            steward_sync = _creator.SpatialSyncStatus(projectId),
            cast = CreatorCastStatus(projectId) with { Project = null }, operations,
            supported_operations = CreatorOperationJournal.Operations,
            proof_boundary = "Operation completion is not gameplay completion. Runtime receipts remain authoritative.",
        };
    }

    public StudioCreatorSceneResult RetainedCreatorScene(string sceneId) => _creator.RetainedScene(sceneId);

    public Task<object> SyncCreatorSpatialEvidenceAsync(string projectId, CancellationToken token) =>
        _creator.SyncSpatialEvidenceAsync(projectId, DownloadSpatialEvidence(projectId), token);

    StudioRuntimeIdentity? CreatorSessionIdentity()
    {
        var valheim = _host.FindValheim();
        if (valheim is null) return null;
        try
        {
            var path = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-creator", "session.json");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > 1024 * 1024) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.GetProperty("schema").GetString() != "comfy-quest-creator-session/v1"
                || root.GetProperty("state").GetString() != "active") return null;
            var identity = new StudioRuntimeIdentity(root.GetProperty("expected_machine").GetString()!,
                root.GetProperty("world_uid").GetString()!, root.GetProperty("session_id").GetString()!);
            return CreatorOperationJournal.Safe(identity.Machine) && CreatorOperationJournal.Safe(identity.CreatorSessionId)
                && long.TryParse(identity.WorldUid, out var world) && world != 0 ? identity : null;
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return null; }
    }

    string? ValidateCreatorOperation(StudioCreatorOperationRequest request)
    {
        var project = _workspace.ReadProject(request.ProjectId);
        if (project is null) return "project_missing";
        if (project.Revision != request.ExpectedRevision) return "revision_conflict";
        if (request.Operation.StartsWith("campaign_", StringComparison.Ordinal))
        {
            var campaignError = ValidateCreatorCampaignOperation(request);
            if (campaignError is not null) return campaignError;
        }
        var session = CreatorSessionIdentity();
        if (session is null) return "creator_session_unavailable";
        if (!string.Equals(session.Machine, request.ExpectedMachine, StringComparison.OrdinalIgnoreCase)
            || session.WorldUid != request.ExpectedWorldUid || session.CreatorSessionId != request.CreatorSessionId)
            return "runtime_identity_changed";
        if (request.Operation == "campaign_play") return null; // The packaged prerequisite runner may launch the leased world.
        var runs = _runControl.Status(request.ProjectId);
        if (!runs.Connected || !string.Equals(runs.Machine, request.ExpectedMachine, StringComparison.OrdinalIgnoreCase)
            || runs.WorldUid != request.ExpectedWorldUid) return runs.Error ?? "runtime_identity_changed";
        if (request.Operation is "activate" or "quick_cast")
        {
            var target = _creator.ReadTarget(request.ProjectId, request.TargetId);
            if (target is null) return "creator_target_receipt_missing";
            if (target.ProjectRevision != request.ExpectedRevision) return "creator_target_revision_stale";
            if (target.WorldLink.CreatorSessionId != request.CreatorSessionId
                || target.WorldLink.WorldUid != request.ExpectedWorldUid
                || !string.Equals(target.WorldLink.Machine, request.ExpectedMachine, StringComparison.OrdinalIgnoreCase))
                return "creator_target_identity_changed";
        }
        if (request.Operation is "reset_preview" or "reset" && !runs.Runs.Any(value => value.RunId == request.RunId))
            return "run_scope_not_loaded";
        return null;
    }

    async Task<StudioCreatorOperationOutcome> ExecuteCreatorOperationAsync(StudioCreatorOperationRequest request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        if (request.Operation.StartsWith("campaign_", StringComparison.Ordinal))
            return await SerializeCreatorControl(() => ExecuteCreatorCampaignOperationAsync(request, token), token);
        if (request.Operation is "quick_cast" or "activate")
        {
            var result = await CreatorCastAsync(request.ProjectId,
                new(request.ExpectedRevision, request.TargetId, request.Operation), token);
            return new(result.Ok, result.Error, result.State, result.CastId, result.RunId,
                RecoveryRequired: !result.Ok && result.CastId is not null);
        }
        if (request.Operation == "undo_cast")
        {
            var result = await UndoCreatorCastAsync(request.ProjectId, new(request.CastId), token);
            return new(result.Ok, result.Error, result.State, result.CastId, result.RunId,
                RecoveryRequired: !result.Ok && result.CastId is not null);
        }
        var reset = new StudioRunResetRequest(request.RunId!, request.PreviewToken, request.Confirm,
            request.ExpectedMachine, request.ExpectedWorldUid, request.CreatorSessionId);
        var control = await SerializeCreatorControl(() => request.Operation == "reset_preview"
            ? _runControl.PreviewAsync(request.ProjectId, reset, token)
            : _runControl.ApplyAsync(request.ProjectId, reset, token), token);
        return ControlOutcome(control);
    }

    StudioCreatorOperationOutcome ControlOutcome(StudioRunControlResult result) => new(
        result.Ok, result.Error, result.Receipt?.State, RunId: result.Receipt?.Result?.NewRunId,
        RequestId: result.RequestId,
        Receipt: result.Receipt is null ? null : JsonSerializer.SerializeToElement(result.Receipt, _host.Json),
        Pending: result.Queued);

    /// <summary>Install-local evidence sequence: source order is retained, never reconstructed by timestamps.</summary>
    public object? CreatorEvidence(string projectId, long after)
    {
        if (!CreatorOperationJournal.Safe(projectId) || after < 0 || _workspace.ReadProject(projectId) is null) return null;
        lock (_creatorEvidenceGate)
        {
            const int maxEntries = 1024;
            const int maxBytes = 8 * 1024 * 1024;
            var path = Path.Combine(_host.StateDirectory, "quest-studio", "creator", "evidence", projectId + ".json");
            var entries = new List<StudioCreatorEvidenceEntry>();
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > maxBytes) throw new InvalidDataException("creator_evidence_size_invalid");
                entries = JsonSerializer.Deserialize<List<StudioCreatorEvidenceEntry>>(File.ReadAllText(path), _host.Json)
                    ?? throw new InvalidDataException("creator_evidence_invalid");
                if (entries.Count > maxEntries || entries.Where((value, index) => value.Sequence != index + 1).Any())
                    throw new InvalidDataException("creator_evidence_sequence_invalid");
            }
            var hashes = entries.Select(value => value.SourceSha256).ToHashSet(StringComparer.Ordinal);
            var changed = false;
            var saturated = false;
            foreach (var receipt in RuntimeStatus(projectId).ExactReceipts.Reverse())
            {
                // The display view deliberately omits schema, target and spatial traces.
                // Export the actual Runtime contract, including its native JSON field names.
                var bytes = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(receipt));
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (hashes.Contains(hash)) continue;
                if (entries.Count >= maxEntries) { saturated = true; break; }
                var entry = new StudioCreatorEvidenceEntry(entries.Count + 1, hash,
                    Convert.ToBase64String(bytes), JsonDocument.Parse(bytes).RootElement.Clone());
                entries.Add(entry);
                if (JsonSerializer.SerializeToUtf8Bytes(entries, _host.Json).Length > maxBytes)
                { entries.RemoveAt(entries.Count - 1); saturated = true; break; }
                hashes.Add(hash);
                changed = true;
            }
            if (changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
                try { File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(entries, _host.Json)); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            var page = entries.Where(value => value.Sequence > after).Take(128).ToArray();
            return new { schema = "comfy-quest-creator-evidence/v1", project_id = projectId,
                cursor = page.LastOrDefault()?.Sequence ?? after, latest_cursor = entries.Count,
                cursor_invalid = after > entries.Count, entries = page,
                has_more = entries.Count - after > 128, saturated,
                source_history = "bounded_runtime_receipt_window", full_history_proven = false };
        }
    }
}

public sealed record StudioCreatorEvidenceEntry(long Sequence, string SourceSha256, string SourceBytesBase64, JsonElement Receipt);

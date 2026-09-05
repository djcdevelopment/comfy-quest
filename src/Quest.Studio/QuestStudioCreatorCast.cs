using System.Text;
using System.Text.Json;
using ComfyQuestContracts;
using Newtonsoft.Json;

namespace Comfy.Quest.Studio;

public sealed record StudioCreatorCastRequest(int ExpectedRevision, string? TargetId, string? Mode);
public sealed record StudioCreatorUndoCastRequest(string? CastId);

public sealed record StudioCreatorCastResult(
    bool Ok,
    bool Conflict,
    string? State,
    string? Error,
    string? CastId,
    string? RunId,
    string? ExperienceId,
    StudioProjectDocument? Project)
{
    public static StudioCreatorCastResult Fail(string error, bool conflict = false,
        StudioProjectDocument? project = null) =>
        new(false, conflict, "failed", error, null, null, null, project);
}

internal sealed class StudioCreatorCastReceipt
{
    public string Schema { get; set; } = "comfy-quest-studio-creator-cast/v1";
    public string CastId { get; set; } = string.Empty;
    public string State { get; set; } = "pending";
    public string Mode { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public int ProjectRevision { get; set; }
    public string TargetId { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;
    public string BindingZdo { get; set; } = string.Empty;
    public string ExperienceId { get; set; } = string.Empty;
    public string PackId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string CreatorSessionId { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
    public string WorldUid { get; set; } = string.Empty;
    public string? PackageSha256 { get; set; }
    public string? ActivationId { get; set; }
    public string? BindRequestId { get; set; }
    public string? BindingChangeId { get; set; }
    public string? RunId { get; set; }
    public string? RetirePreviewRequestId { get; set; }
    public string? RetirePreviewToken { get; set; }
    public string? RetireRequestId { get; set; }
    public string? RestoreRequestId { get; set; }
    public string? Error { get; set; }
}

internal sealed class QuestStudioCreatorCast
{
    const int MaxCasts = 128;
    readonly object _gate = new();
    readonly IQuestStudioHost _host;
    readonly QuestPackPublisher _publisher;
    readonly QuestStudioWorkspace _workspace;
    readonly QuestStudioRunControl _runControl;
    readonly QuestStudioCreator _creator;
    readonly string _castRoot;

    public QuestStudioCreatorCast(IQuestStudioHost host, QuestPackPublisher publisher,
        QuestStudioWorkspace workspace, QuestStudioRunControl runControl,
        QuestStudioCreator creator)
    {
        _host = host;
        _publisher = publisher;
        _workspace = workspace;
        _runControl = runControl;
        _creator = creator;
        _castRoot = Path.Combine(host.StateDirectory, "quest-studio", "creator", "casts");
    }

    public async Task<StudioCreatorCastResult> CastAsync(string projectId,
        StudioCreatorCastRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.Mode is not ("activate" or "quick_cast"))
            return StudioCreatorCastResult.Fail("creator_cast_request_invalid");
        var project = _workspace.ReadProject(projectId);
        if (project is null) return StudioCreatorCastResult.Fail("project_missing");
        if (project.Revision != request.ExpectedRevision)
            return StudioCreatorCastResult.Fail("revision_conflict", true, project);
        if (request.Mode == "quick_cast" && ActiveQuickCast(projectId) is { } activeCast)
            return new(false, false, activeCast.State, "quick_cast_already_held",
                activeCast.CastId, activeCast.RunId, activeCast.ExperienceId, project);
        var target = _creator.ReadTarget(projectId, request.TargetId);
        if (target is null) return StudioCreatorCastResult.Fail("creator_target_receipt_missing");
        if (target.ProjectRevision != project.Revision)
            return StudioCreatorCastResult.Fail("creator_target_revision_stale");

        var compiled = StudioGraphCompiler.Compile(project);
        if (!compiled.Ok) return StudioCreatorCastResult.Fail(compiled.Error ?? "graph_invalid");
        var route = compiled.Document!.Stages.SelectMany(value => value.Transitions ?? new())
            .SingleOrDefault(value => value.Id == target.RouteId);
        if (route is null) return StudioCreatorCastResult.Fail("creator_target_route_missing");
        if (request.Mode == "quick_cast" && route.Actions.Any(value => value.Type == "grant_item"))
            return StudioCreatorCastResult.Fail("quick_cast_nonreversible_action");

        var quickId = QuickExperienceId(project, target.RouteId);
        var quick = BuildQuickExperience(project, route, quickId);
        var quickJson = JsonConvert.SerializeObject(quick, Formatting.Indented);
        var quickContract = ExperienceCompiler.CompileProductionJson(quickJson);
        if (!quickContract.IsValid) return StudioCreatorCastResult.Fail("quick_cast_graph_invalid");
        var entries = new[]
        {
            new KeyValuePair<string, string>(project.ExperienceId, compiled.ExperienceJson!),
            new KeyValuePair<string, string>(quickId, quickJson),
        };
        var contentHash = QuestPackContent.ComputeHash(entries.Select(value =>
            new KeyValuePair<string, byte[]>($"experiences/{value.Key}.json",
                Encoding.UTF8.GetBytes(value.Value))));
        var bytes = StudioGraphCompiler.BuildPack(project.PackId, project.Version, entries, contentHash);
        var selectedExperience = request.Mode == "activate" ? project.ExperienceId : quickId;
        var identity = new StudioRuntimeIdentity(target.WorldLink.Machine,
            target.WorldLink.WorldUid, target.WorldLink.CreatorSessionId);
        var status = _runControl.StatusForExperience(projectId, selectedExperience);
        if (!status.Available || !status.Connected
            || !string.Equals(status.Machine, identity.Machine, StringComparison.OrdinalIgnoreCase)
            || status.WorldUid != identity.WorldUid)
            return StudioCreatorCastResult.Fail(status.Error ?? "runtime_identity_changed");
        var valheim = _host.FindValheim();
        if (valheim is null) return StudioCreatorCastResult.Fail("valheim_not_found");
        var runtimeRoot = Path.Combine(Path.GetFullPath(valheim), "BepInEx", "config",
            "comfy-quest-runtime");
        var dev = await StudioDevChannelConnection.WaitForConnectedAsync(runtimeRoot, cancellationToken);
        if (dev is null) return StudioCreatorCastResult.Fail("dev_channel_disconnected");
        if (dev.Armed != true) return StudioCreatorCastResult.Fail("dev_channel_not_armed");

        var cast = new StudioCreatorCastReceipt
        {
            CastId = "cast-" + DateTimeOffset.UtcNow.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'")
                + "-" + Guid.NewGuid().ToString("N")[..8],
            StartedUtc = DateTimeOffset.UtcNow, Mode = request.Mode,
            ProjectId = projectId, ProjectRevision = project.Revision,
            TargetId = target.TargetId, RouteId = target.RouteId,
            BindingZdo = target.BindingZdo, ExperienceId = selectedExperience,
            PackId = project.PackId, Version = project.Version,
            ContentHash = contentHash, CreatorSessionId = identity.CreatorSessionId,
            Machine = identity.Machine, WorldUid = identity.WorldUid,
        };
        Store(cast);
        try
        {
            await using var stream = new MemoryStream(bytes, writable: false);
            var filename = $"creator-{project.ProjectId}-r{project.Revision}-{contentHash[..12]}-{cast.CastId[^8..]}.questpack";
            var publication = await _publisher.PublishDevAsync(stream, filename, cancellationToken);
            if (!publication.Ok) return Fail(cast, publication.Error ?? "publication_failed", project);
            cast.PackageSha256 = publication.PackageSha256;
            Store(cast);
            var activation = await WaitForExactActivationAsync(runtimeRoot, publication,
                contentHash, cancellationToken);
            if (activation.Active is null)
                return Fail(cast, activation.Error ?? "creator_activation_timeout", project);
            cast.ActivationId = activation.Active.ActivationId;
            Store(cast);

            var bindInitial = await _runControl.BindExperiencePinnedAsync(projectId,
                new(selectedExperience, target.BindingZdo), selectedExperience, identity,
                cancellationToken);
            cast.BindRequestId = bindInitial.RequestId;
            if (!string.IsNullOrWhiteSpace(cast.BindRequestId)) cast.State = "binding_pending";
            Store(cast);
            var bound = await CompleteAsync(projectId, bindInitial, identity,
                "bind_selected_experience", null, cancellationToken);
            if (!bound.Ok) return Fail(cast, bound.Error ?? "creator_bind_failed", project);
            var change = bound.Receipt?.BindingChange;
            if (!ExactAppliedBinding(change, cast))
                return Fail(cast, "creator_bind_evidence_mismatch", project);
            cast.BindingChangeId = change!.ChangeId;
            cast.State = "bound";
            Store(cast);
            var run = await WaitForRunAsync(projectId, selectedExperience, target.BindingZdo,
                contentHash, identity, cancellationToken);
            if (run is null) return Fail(cast, "creator_run_start_timeout", project);
            cast.RunId = run.RunId;
            cast.State = request.Mode == "activate" ? "activated" : "held";
            cast.CompletedUtc = DateTimeOffset.UtcNow;
            Store(cast);
            return new(true, false, cast.State, null, cast.CastId, cast.RunId,
                cast.ExperienceId, project);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return Fail(cast, "creator_cast_failed:" + exception.Message, project); }
    }

    public async Task<StudioCreatorCastResult> UndoAsync(string projectId,
        StudioCreatorUndoCastRequest? request, CancellationToken cancellationToken)
    {
        var cast = Read(projectId, request?.CastId);
        if (cast is null) return StudioCreatorCastResult.Fail("creator_cast_receipt_missing");
        if (cast.Mode != "quick_cast") return StudioCreatorCastResult.Fail("creator_cast_not_undoable");
        var project = _workspace.ReadProject(projectId);
        if (project is null) return StudioCreatorCastResult.Fail("project_missing");
        if (cast.State == "undone") return Success(cast, project);
        var identity = new StudioRuntimeIdentity(cast.Machine, cast.WorldUid,
            cast.CreatorSessionId);
        try
        {
            if (string.IsNullOrWhiteSpace(cast.BindingChangeId)
                && cast.State == "binding_pending" && !string.IsNullOrWhiteSpace(cast.BindRequestId))
            {
                var bound = await CompleteAsync(projectId,
                    _runControl.ReceiptPinned(projectId, cast.BindRequestId, null, identity,
                        "bind_selected_experience"), identity, "bind_selected_experience", null,
                    cancellationToken);
                if (!bound.Ok) return Fail(cast, bound.Error ?? "creator_bind_failed", project);
                var change = bound.Receipt?.BindingChange;
                if (!ExactAppliedBinding(change, cast))
                    return Fail(cast, "creator_bind_evidence_mismatch", project);
                cast.BindingChangeId = change!.ChangeId;
                cast.State = "bound";
                cast.Error = null;
                Store(cast);
            }
            if (string.IsNullOrWhiteSpace(cast.BindingChangeId))
                return Fail(cast, "creator_cast_incomplete", project);
            if (string.IsNullOrWhiteSpace(cast.RunId) && cast.State == "bound")
            {
                var run = await WaitForRunAsync(projectId, cast.ExperienceId, cast.BindingZdo,
                    cast.ContentHash, identity, cancellationToken);
                if (run is null) return Fail(cast, "creator_run_start_timeout", project);
                cast.RunId = run.RunId;
                cast.State = "held";
                cast.Error = null;
                Store(cast);
            }
            if (string.IsNullOrWhiteSpace(cast.RunId))
                return Fail(cast, "creator_cast_incomplete", project);
            if (cast.State == "held")
            {
                if (string.IsNullOrWhiteSpace(cast.RetirePreviewToken))
                {
                    var previewInitial = string.IsNullOrWhiteSpace(cast.RetirePreviewRequestId)
                        ? await _runControl.PreviewRetirePinnedAsync(projectId, cast.RunId,
                            cast.ExperienceId, identity, cancellationToken)
                        : _runControl.ReceiptPinned(projectId, cast.RetirePreviewRequestId,
                            cast.RunId, identity, "preview_retire");
                    cast.RetirePreviewRequestId = previewInitial.RequestId;
                    Store(cast);
                    var preview = await CompleteAsync(projectId, previewInitial, identity,
                        "preview_retire", cast.RunId, cancellationToken);
                    cast.RetirePreviewToken = preview.Receipt?.RetirePreview?.PreviewToken;
                    if (!preview.Ok || string.IsNullOrWhiteSpace(cast.RetirePreviewToken))
                        return Fail(cast, preview.Error ?? "creator_retire_preview_failed", project);
                    Store(cast);
                }
                var retireInitial = string.IsNullOrWhiteSpace(cast.RetireRequestId)
                    ? await _runControl.ApplyRetirePinnedAsync(projectId, cast.RunId,
                        cast.RetirePreviewToken, cast.ExperienceId, identity, cancellationToken)
                    : _runControl.ReceiptPinned(projectId, cast.RetireRequestId, cast.RunId,
                        identity, "apply_retire");
                cast.RetireRequestId = retireInitial.RequestId;
                Store(cast);
                var retired = await CompleteAsync(projectId, retireInitial, identity,
                    "apply_retire", cast.RunId, cancellationToken);
                if (!retired.Ok && retired.Receipt?.RetireResult?.State == "cleanup_incomplete")
                {
                    cast.RetireRequestId = null;
                    Store(cast);
                }
                if (!retired.Ok || retired.Receipt?.RetireResult?.State != "completed")
                    return Fail(cast, retired.Error ?? "creator_retire_failed", project);
                cast.State = "retired";
                Store(cast);
            }
            if (cast.State == "retired")
            {
                var restoreInitial = string.IsNullOrWhiteSpace(cast.RestoreRequestId)
                    ? await _runControl.RestoreBindingPinnedAsync(projectId,
                        new(cast.BindingZdo, cast.BindingChangeId), identity, cancellationToken)
                    : _runControl.ReceiptPinned(projectId, cast.RestoreRequestId, null,
                        identity, "restore_binding");
                cast.RestoreRequestId = restoreInitial.RequestId;
                Store(cast);
                var restored = await CompleteAsync(projectId, restoreInitial, identity,
                    "restore_binding", null, cancellationToken);
                if (!restored.Ok || restored.Receipt?.BindingChange?.State != "restored")
                    return Fail(cast, restored.Error ?? "creator_binding_restore_failed", project);
                cast.State = "undone";
                cast.CompletedUtc = DateTimeOffset.UtcNow;
                cast.Error = null;
                Store(cast);
            }
            return Success(cast, project);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return Fail(cast, "creator_undo_failed:" + exception.Message, project); }
    }

    public StudioCreatorCastResult Status(string projectId)
    {
        var project = _workspace.ReadProject(projectId);
        if (project is null) return StudioCreatorCastResult.Fail("project_missing");
        var cast = ActiveQuickCast(projectId);
        return cast is null
            ? new(true, false, "idle", null, null, null, null, project)
            : Success(cast, project);
    }

    internal static ExperienceDocument BuildQuickExperience(StudioProjectDocument project,
        ExperienceTransition route, string quickId) => new()
    {
        Schema = ExperienceSchema.Id,
        Id = quickId,
        Title = project.Title + " (Quick Cast)",
        EntryStage = "cast",
        Bindings = new()
        {
            new ExperienceBinding
            {
                Id = "default", ExperienceId = quickId,
                TargetKinds = !string.IsNullOrWhiteSpace(project.BindingTargetKind)
                    ? new() { project.BindingTargetKind }
                    : (project.BindingTargetKinds ?? new()).Distinct(StringComparer.Ordinal).ToList(),
            },
        },
        Stages = new()
        {
            new ExperienceStage
            {
                Id = "cast", EntryActions = new(),
                Transitions = new()
                {
                    new ExperienceTransition
                    {
                        Id = "invoke", Priority = 1,
                        When = new TriggerExpression
                        {
                            Op = "EVENT", Event = ExperienceSchema.ExperienceStartedEvent,
                            Where = new(), Children = new(),
                        },
                        Actions = JsonConvert.DeserializeObject<List<ExperienceAction>>(
                            JsonConvert.SerializeObject(route.Actions)) ?? new(),
                        NextStage = "held",
                    },
                },
            },
            new ExperienceStage { Id = "held", EntryActions = new(), Transitions = new() },
        },
    };

    internal static string QuickExperienceId(StudioProjectDocument project, string routeId)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(project.ProjectId + "\n" + routeId))).ToLowerInvariant();
        return "creator-cast-" + hash[..20];
    }

    static bool ExactAppliedBinding(RuntimeBindingChange? change,
        StudioCreatorCastReceipt cast) => change is not null
        && change.Schema == RuntimeBindingChange.CurrentSchema && change.State == "applied"
        && change.BindingZdo == cast.BindingZdo
        && (string.IsNullOrWhiteSpace(change.ResolvedBindingZdo)
            || change.ResolvedBindingZdo == cast.BindingZdo)
        && change.WorldId == cast.WorldUid && change.Applied is not null
        && change.Applied.PackId == cast.PackId
        && change.Applied.Version == cast.Version
        && change.Applied.ExperienceId == cast.ExperienceId
        && change.Applied.BindingId == "default"
        && string.Equals(change.Applied.ContentHash, cast.ContentHash, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(change.Applied.BindingInstanceId);

    async Task<(ActiveSet? Active, string? Error)> WaitForExactActivationAsync(
        string runtimeRoot, QuestPackPublishReceipt publication, string contentHash,
        CancellationToken cancellationToken)
    {
        var store = new QuestPackStore(runtimeRoot);
        var statusStore = new RuntimeDevChannelStatusStore(runtimeRoot);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        string? rejection = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = statusStore.Read();
            rejection = status?.LastRejection ?? rejection;
            ActiveSet? active = null;
            try { active = store.ReadActive(); } catch { }
            if (StudioDevChannelConnection.IsConnected(status, DateTimeOffset.UtcNow)
                && status?.Armed == true && active?.SourceChannel == "dev"
                && active.Source == publication.Filename && active.PackId == publication.PackId
                && active.Version == publication.Version
                && string.Equals(active.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
                && string.Equals(active.PackageSha256, publication.PackageSha256,
                    StringComparison.OrdinalIgnoreCase)
                && status.ActiveActivationId == active.ActivationId)
                return (active, null);
            await Task.Delay(250, cancellationToken);
        }
        return (null, string.IsNullOrWhiteSpace(rejection)
            ? "creator_activation_timeout" : "creator_activation_rejected:" + rejection);
    }

    async Task<RuntimeRunStatusEntry?> WaitForRunAsync(string projectId, string experienceId,
        string bindingZdo, string contentHash, StudioRuntimeIdentity identity,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = _runControl.StatusForExperience(projectId, experienceId);
            if (status.Connected && string.Equals(status.Machine, identity.Machine,
                    StringComparison.OrdinalIgnoreCase) && status.WorldUid == identity.WorldUid)
            {
                var matches = status.Runs.Where(value => value.BindingZdo == bindingZdo
                    && string.Equals(value.ContentHash, contentHash,
                        StringComparison.OrdinalIgnoreCase) && value.Outcome is null).ToArray();
                if (matches.Length == 1) return matches[0];
                if (matches.Length > 1) return null;
            }
            await Task.Delay(200, cancellationToken);
        }
        return null;
    }

    async Task<StudioRunControlResult> CompleteAsync(string projectId,
        StudioRunControlResult initial, StudioRuntimeIdentity identity, string operation,
        string? runId, CancellationToken cancellationToken)
    {
        if (!initial.Ok || !initial.Queued || string.IsNullOrWhiteSpace(initial.RequestId)) return initial;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(200, cancellationToken);
            var result = _runControl.ReceiptPinned(projectId, initial.RequestId, runId,
                identity, operation);
            if (!result.Queued) return result;
        }
        return new(false, false, "run_control_receipt_timeout", null, initial.RequestId);
    }

    StudioCreatorCastResult Fail(StudioCreatorCastReceipt cast, string error,
        StudioProjectDocument project)
    {
        cast.Error = error;
        if (cast.State == "pending") cast.State = "failed";
        cast.CompletedUtc = DateTimeOffset.UtcNow;
        Store(cast);
        return new(false, false, cast.State, error, cast.CastId, cast.RunId,
            cast.ExperienceId, project);
    }

    static StudioCreatorCastResult Success(StudioCreatorCastReceipt cast,
        StudioProjectDocument project) => new(true, false, cast.State, null,
        cast.CastId, cast.RunId, cast.ExperienceId, project);

    void Store(StudioCreatorCastReceipt cast)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_castRoot);
            var path = Path.Combine(_castRoot, cast.CastId + ".json");
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(cast, _host.Json));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            foreach (var file in new DirectoryInfo(_castRoot).GetFiles("*.json")
                         .OrderByDescending(value => value.LastWriteTimeUtc)
                         .ThenBy(value => value.Name, StringComparer.Ordinal).Skip(MaxCasts))
                file.Delete();
        }
    }

    StudioCreatorCastReceipt? Read(string projectId, string? castId)
    {
        if (string.IsNullOrWhiteSpace(castId) || castId.Length > 80
            || castId.Any(value => !char.IsLetterOrDigit(value) && value is not ('-' or '_' or '.')))
            return null;
        try
        {
            var path = Path.Combine(_castRoot, castId + ".json");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > 1024 * 1024) return null;
            var cast = System.Text.Json.JsonSerializer.Deserialize<StudioCreatorCastReceipt>(
                File.ReadAllText(path), _host.Json);
            return cast?.Schema == "comfy-quest-studio-creator-cast/v1"
                && cast.CastId == castId && cast.ProjectId == projectId ? cast : null;
        }
        catch { return null; }
    }

    StudioCreatorCastReceipt? ActiveQuickCast(string projectId)
    {
        try
        {
            if (!Directory.Exists(_castRoot)) return null;
            return new DirectoryInfo(_castRoot).GetFiles("*.json")
                .OrderByDescending(value => value.LastWriteTimeUtc)
                .ThenBy(value => value.Name, StringComparer.Ordinal)
                .Select(value => Read(projectId, Path.GetFileNameWithoutExtension(value.Name)))
                .FirstOrDefault(value => value is { Mode: "quick_cast" }
                    && (!string.IsNullOrWhiteSpace(value.BindingChangeId)
                        || !string.IsNullOrWhiteSpace(value.BindRequestId))
                    && value.State is "binding_pending" or "bound" or "held" or "retired");
        }
        catch { return null; }
    }
}

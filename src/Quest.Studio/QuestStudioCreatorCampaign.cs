using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ComfyQuestContracts;

namespace Comfy.Quest.Studio;

public sealed record StudioCreatorCampaignProject(string ProjectId, string ExperienceId, string Title, int Revision);
public sealed record StudioCreatorCampaignSceneRequest(string AttemptId, string SceneId);

public sealed class StudioCreatorCampaignAttempt
{
    public string Schema { get; set; } = "comfy-quest-creator-campaign-attempt/v1";
    public string AttemptId { get; set; } = "";
    public string? PriorAttemptId { get; set; }
    public string GuildId { get; set; } = "";
    public string CampaignId { get; set; } = "";
    public string State { get; set; } = "starting";
    public string? PendingStep { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public StudioCampaignPlayReceipt? Play { get; set; }
    public string? MeasuredSceneId { get; set; }
    public List<StudioCreatorCampaignProject> Projects { get; set; } = new();
    public List<RuntimeRunStatusEntry> Runs { get; set; } = new();
    public List<RuntimeContinuationRecord> Continuations { get; set; } = new();
    public List<JsonElement> ResetReceipts { get; set; } = new();
}

public sealed record StudioCreatorCampaignRetirement(string ProjectId, string ExperienceId, RuntimeRetirePreview Preview);
public sealed record StudioCreatorCampaignResetPreview(string Schema, string PreviewToken, string AttemptId,
    DateTimeOffset ExpiresUtc, string NextContentHash, int NextCampaignRevision,
    string PreparationId, string ScopeHash, IReadOnlyList<StudioCreatorCampaignRetirement> Retirements,
    IReadOnlyList<string> BindingChangeIds, string PreviousRewards = "retained");

public sealed partial class QuestStudioService
{
    readonly object _creatorCampaignGate = new();

    string CampaignFile(string id, bool preview = false)
    {
        if (!CreatorOperationJournal.Safe(id)) throw new ArgumentException("campaign_identity_invalid");
        return Path.Combine(_host.StateDirectory, "quest-studio", "creator",
            preview ? "campaign-reset-previews" : "campaign-attempts", id + ".json");
    }

    T? ReadCampaignFile<T>(string id, bool preview = false)
    {
        var path = CampaignFile(id, preview);
        if (!File.Exists(path)) return default;
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("campaign_record_too_large");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), _host.Json);
    }

    void WriteCampaignFile<T>(string id, T value, bool preview = false)
    {
        lock (_creatorCampaignGate)
        {
            var path = CampaignFile(id, preview);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _host.Json);
            if (bytes.Length > 2 * 1024 * 1024) throw new InvalidDataException("campaign_record_too_large");
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    StudioCreatorCampaignAttempt[] CampaignAttempts(string guildId, string campaignId)
    {
        var directory = Path.GetDirectoryName(CampaignFile("index"))!;
        if (!Directory.Exists(directory)) return [];
        var files = Directory.GetFiles(directory, "*.json");
        if (files.Length > 128) throw new InvalidDataException("campaign_attempt_limit");
        return files.Select(path => ReadCampaignFile<StudioCreatorCampaignAttempt>(Path.GetFileNameWithoutExtension(path)))
            .Where(value => value is not null && value.GuildId == guildId && value.CampaignId == campaignId)
            .Cast<StudioCreatorCampaignAttempt>().OrderBy(value => value.CreatedUtc).ToArray();
    }

    public object CreatorCampaigns() => new
    {
        schema = "comfy-quest-creator-campaigns/v1",
        campaigns = ListGuilds().Select(summary => ReadGuild(summary.GuildId)).Where(guild => guild is not null)
            .SelectMany(guild => guild!.Campaigns.Where(campaign => IsSignatureHuntCampaign(guild, campaign))
                .Select(campaign => new { guild_id = guild.GuildId, campaign.CampaignId, campaign.Title, campaign.Revision })).ToArray()
    };

    public object? CreatorCampaignContext(string guildId, string campaignId)
    {
        if (!CreatorOperationJournal.Safe(guildId) || !CreatorOperationJournal.Safe(campaignId)) return null;
        var guild = ReadGuild(guildId);
        var campaign = guild?.Campaigns.SingleOrDefault(value => value.CampaignId == campaignId);
        if (guild is null || campaign is null || !IsSignatureHuntCampaign(guild, campaign)) return null;
        var root = CampaignArtifactsInAuthoredOrder(campaign).SingleOrDefault(value => value.PrerequisiteProjectIds.Count == 0);
        if (root is null) return null;
        var context = JsonSerializer.SerializeToNode(CreatorContext(root.ProjectId), _host.Json)!.AsObject();
        var attempts = CampaignAttempts(guildId, campaignId);
        var attempt = attempts.LastOrDefault();
        if (attempt is not null) attempt = ObserveCampaignAttempt(attempt.AttemptId);
        if (attempt?.MeasuredSceneId is not null)
        {
            var measured = _creator.LinkedScene(attempt.MeasuredSceneId, _runControl.Status(root.ProjectId));
            context["scene"] = JsonSerializer.SerializeToNode(measured, _host.Json);
            context["scene_path"] = measured is null ? null : JsonValue.Create("/api/v2/quest-studio/creator/scenes/" + measured.SceneId);
        }
        var projects = CampaignArtifactsInAuthoredOrder(campaign).Select(value => _workspace.ReadProject(value.ProjectId)!).ToArray();
        context["campaign"] = JsonSerializer.SerializeToNode(new
        {
            schema = "comfy-quest-creator-campaign-context/v1", guild_id = guildId,
            campaign.CampaignId, campaign.Title, campaign.Revision, campaign.Version,
            projects = CampaignArtifactsInAuthoredOrder(campaign).Select(value => _workspace.ReadProject(value.ProjectId))
                .Where(value => value is not null).Select(value => new { value!.ProjectId, value.ExperienceId, value.Title, value.Revision }),
            attempt,
            history = attempts.Select(value => new { value.AttemptId, value.State, value.CreatedUtc, value.PriorAttemptId }),
            authoring_path = "/quest-studio?guild=" + Uri.EscapeDataString(guildId),
            has_spatial_conditions = projects.Any(project => project.Nodes.Any(node => node.Routes.Any(route => route.SpatialConditions.Count > 0))),
            evidence = CampaignEvidence(guildId, campaignId),
        }, _host.Json);
        return context;
    }

    public object LinkCreatorCampaignScene(string guildId, string campaignId, StudioCreatorCampaignSceneRequest request)
    {
        if (!CreatorOperationJournal.Safe(guildId) || !CreatorOperationJournal.Safe(campaignId)
            || !CreatorOperationJournal.Safe(request.AttemptId)) return new { ok = false, error = "campaign_identity_invalid" };
        lock (_creatorCampaignGate)
        {
            var attempt = CampaignAttempts(guildId, campaignId).LastOrDefault();
            if (attempt?.AttemptId != request.AttemptId || attempt.Play is null || attempt.State is not ("started" or "complete"))
                return new { ok = false, error = "campaign_attempt_changed" };
            var status = _runControl.Status(attempt.Play.FirstProjectId);
            if (!status.Connected || status.WorldUid != attempt.Play.WorldUid || status.Machine != attempt.Play.Machine)
                return new { ok = false, error = "campaign_runtime_identity_changed" };
            var scene = _creator.LinkedScene(request.SceneId, status);
            if (scene is null) return new { ok = false, error = "campaign_scene_snapshot_mismatch" };
            attempt.MeasuredSceneId = scene.SceneId;
            WriteCampaignFile(attempt.AttemptId, attempt);
            return new { ok = true, attempt_id = attempt.AttemptId, scene };
        }
    }

    StudioCreatorCampaignAttempt ObserveCampaignAttempt(string id)
    {
        lock (_creatorCampaignGate)
        {
            var attempt = ReadCampaignFile<StudioCreatorCampaignAttempt>(id) ?? throw new InvalidDataException("campaign_attempt_missing");
            if (attempt.Play is null || attempt.State is "retired" or "resetting" or "recovery_required") return attempt;
            var play = attempt.Play;
            var statuses = attempt.Projects.Select(project => _runControl.StatusForExperience(project.ProjectId, project.ExperienceId)).ToArray();
            if (statuses.Any(status => !status.Connected || status.Machine != play.Machine || status.WorldUid != play.WorldUid)) return attempt;
            var before = JsonSerializer.Serialize(attempt, _host.Json);
            var runs = statuses.SelectMany(status => status.Runs).Where(run =>
                run.BindingInstanceId == play.BindingInstanceId && run.ContentHash == play.ContentHash).ToArray();
            if (runs.GroupBy(run => run.ExperienceId).Any(group => group.Count() > 1))
                throw new InvalidDataException("campaign_attempt_scope_ambiguous");
            foreach (var run in runs)
            {
                attempt.Runs.RemoveAll(value => value.RunId == run.RunId);
                attempt.Runs.Add(run);
            }
            var store = new RuntimeContinuationStore(Path.Combine(_host.FindValheim()!, "BepInEx", "config", "comfy-quest-runtime"));
            attempt.Continuations = attempt.Runs.Select(run => store.FindBySourceRun(run.RunId))
                .Where(value => value is not null && value.BindingInstanceId == play.BindingInstanceId
                    && value.ContentHash == play.ContentHash && value.WorldId == play.WorldUid).ToList()!;
            if (attempt.State == "started" && attempt.Projects.All(project =>
                    attempt.Runs.Any(run => run.ExperienceId == project.ExperienceId && run.Outcome == "complete"))
                && attempt.Continuations.Count == attempt.Projects.Count - 1
                && attempt.Continuations.All(value => value.State == "completed" && attempt.Runs.Any(run => run.RunId == value.SuccessorRunId)))
                attempt.State = "complete";
            if (before != JsonSerializer.Serialize(attempt, _host.Json)) WriteCampaignFile(id, attempt);
            return attempt;
        }
    }

    string? ValidateCreatorCampaignOperation(StudioCreatorOperationRequest request)
    {
        var guild = ReadGuild(request.GuildId!);
        var campaign = guild?.Campaigns.SingleOrDefault(value => value.CampaignId == request.CampaignId);
        if (guild is null || campaign is null || !IsSignatureHuntCampaign(guild, campaign)) return "creator_campaign_missing";
        if (campaign.Revision != request.ExpectedCampaignRevision) return "campaign_revision_conflict";
        var roots = CampaignArtifactsInAuthoredOrder(campaign).Where(value => value.PrerequisiteProjectIds.Count == 0).ToArray();
        if (roots.Length != 1 || roots[0].ProjectId != request.ProjectId) return "campaign_root_mismatch";
        var latest = CampaignAttempts(guild.GuildId, campaign.CampaignId).LastOrDefault();
        if (request.Operation == "campaign_play")
            return latest is not null && latest.State != "retired" ? "campaign_reset_required" : null;
        if (latest is null || latest.AttemptId != request.AttemptId || latest.Play is null) return "campaign_attempt_changed";
        if (latest.State is not ("started" or "complete")) return "campaign_attempt_requires_recovery";
        if (latest.Play.CreatorSessionId != request.CreatorSessionId || latest.Play.WorldUid != request.ExpectedWorldUid
            || latest.Play.Machine != request.ExpectedMachine) return "campaign_attempt_identity_changed";
        return null;
    }

    async Task<StudioCreatorOperationOutcome> ExecuteCreatorCampaignOperationAsync(StudioCreatorOperationRequest request, CancellationToken token)
    {
        var stale = ValidateCreatorOperation(request); // The shared Studio control gate may have been occupied.
        if (stale is not null) return new(false, stale);
        var guild = ReadGuild(request.GuildId!)!;
        var campaign = guild.Campaigns.Single(value => value.CampaignId == request.CampaignId);
        if (request.Operation == "campaign_play") return await StartCreatorCampaignAsync(request, guild, campaign, null, token);
        var attempt = ObserveCampaignAttempt(request.AttemptId!);
        var identity = new StudioRuntimeIdentity(request.ExpectedMachine, request.ExpectedWorldUid, request.CreatorSessionId);
        var compiled = CompileCampaign(guild, campaign);
        if (!compiled.Ok) return new(false, compiled.Error);
        if (attempt.Runs.Count == 0 || attempt.Continuations.Any(value => value.State != "completed"))
            return new(false, "campaign_runs_missing_or_continuation_pending");
        if (request.Operation == "campaign_reset_preview")
        {
            var retirements = new List<StudioCreatorCampaignRetirement>();
            foreach (var project in attempt.Projects.AsEnumerable().Reverse())
                foreach (var run in attempt.Runs.Where(value => value.ExperienceId == project.ExperienceId))
                {
                    var result = await CompleteCampaignRunControlAsync(project.ProjectId, run.RunId,
                        await _runControl.PreviewRetirePinnedAsync(project.ProjectId, run.RunId, project.ExperienceId, identity, token),
                        identity, "preview_retire", token);
                    if (!result.Ok || result.Receipt?.RetirePreview is null) return new(false, result.Error ?? "campaign_retire_preview_missing");
                    retirements.Add(new(project.ProjectId, project.ExperienceId, result.Receipt.RetirePreview));
                }
            var changes = attempt.Continuations.AsEnumerable().Reverse().Select(value => value.BindingChangeId)
                .Append(attempt.Play!.BindingChangeId).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
            foreach (var change in changes) ReadCampaignBinding(attempt, change);
            var preview = new StudioCreatorCampaignResetPreview("comfy-quest-creator-campaign-reset-preview/v1",
                "campaign-reset-" + Guid.NewGuid().ToString("N"), attempt.AttemptId, DateTimeOffset.UtcNow.AddMinutes(4),
                compiled.ContentHash!, campaign.Revision, attempt.Play.FixturePreparationId, CampaignScopeHash(attempt), retirements, changes);
            WriteCampaignFile(preview.PreviewToken, preview, true);
            return new(true, null, "previewed", Receipt: JsonSerializer.SerializeToElement(new { preview }, _host.Json));
        }
        var accepted = ReadCampaignFile<StudioCreatorCampaignResetPreview>(request.PreviewToken!, true);
        if (accepted is null || accepted.AttemptId != attempt.AttemptId || accepted.ExpiresUtc <= DateTimeOffset.UtcNow
            || accepted.NextContentHash != compiled.ContentHash || accepted.NextCampaignRevision != campaign.Revision
            || accepted.ScopeHash != CampaignScopeHash(attempt)) return new(false, "campaign_reset_preview_stale");
        var bindings = accepted.BindingChangeIds.Select(change => ReadCampaignBinding(attempt, change)).ToArray();
        attempt.State = "resetting";
        WriteCampaignFile(attempt.AttemptId, attempt);
        try
        {
            foreach (var retirement in accepted.Retirements)
            {
                attempt.PendingStep = "retire:" + retirement.Preview.RunId;
                WriteCampaignFile(attempt.AttemptId, attempt);
                var result = await CompleteCampaignRunControlAsync(retirement.ProjectId, retirement.Preview.RunId,
                    await _runControl.ApplyRetirePinnedAsync(retirement.ProjectId, retirement.Preview.RunId,
                        retirement.Preview.PreviewToken, retirement.ExperienceId, identity, token), identity, "apply_retire", token);
                if (!result.Ok || result.Receipt?.RetireResult?.State != "completed")
                    throw new InvalidOperationException(result.Error ?? "campaign_retirement_incomplete");
                attempt.ResetReceipts.Add(JsonSerializer.SerializeToElement(result.Receipt, _host.Json));
            }
            foreach (var change in bindings)
            {
                attempt.PendingStep = "restore_binding:" + change.ChangeId;
                WriteCampaignFile(attempt.AttemptId, attempt);
                var result = await CompletePackControlAsync(request.ProjectId,
                    await _runControl.RestoreBindingPinnedAsync(request.ProjectId,
                        new(change.BindingZdo, change.ChangeId), identity, token), identity, "restore_binding", token);
                if (!result.Ok) throw new InvalidOperationException(result.Error ?? "campaign_binding_restore_incomplete");
                attempt.ResetReceipts.Add(JsonSerializer.SerializeToElement(result.Receipt, _host.Json));
            }
            attempt.PendingStep = "clear_fixture:" + accepted.PreparationId;
            WriteCampaignFile(attempt.AttemptId, attempt);
            var clearError = await ClearCreatorCampaignFixtureAsync(request.CommandId, accepted.PreparationId, token);
            if (clearError is not null) throw new InvalidOperationException(clearError);
            attempt.State = "retired";
            attempt.PendingStep = null;
            WriteCampaignFile(attempt.AttemptId, attempt);
            return await StartCreatorCampaignAsync(request, guild, campaign, attempt.AttemptId, token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            attempt.State = "recovery_required";
            WriteCampaignFile(attempt.AttemptId, attempt);
            return new(false, error.Message, attempt.PendingStep, Receipt: JsonSerializer.SerializeToElement(attempt, _host.Json), RecoveryRequired: true);
        }
    }

    async Task<StudioCreatorOperationOutcome> StartCreatorCampaignAsync(StudioCreatorOperationRequest request,
        StudioGuildDocument guild, StudioCampaignDocument campaign, string? priorAttempt, CancellationToken token)
    {
        var attempt = new StudioCreatorCampaignAttempt { AttemptId = request.CommandId, PriorAttemptId = priorAttempt,
            GuildId = guild.GuildId, CampaignId = campaign.CampaignId,
            Projects = CampaignArtifactsInAuthoredOrder(campaign).Select(value => _workspace.ReadProject(value.ProjectId)!)
                .Select(value => new StudioCreatorCampaignProject(value.ProjectId, value.ExperienceId, value.Title, value.Revision)).ToList() };
        WriteCampaignFile(attempt.AttemptId, attempt);
        var result = await PlaySignatureHuntCampaignAsync(guild, campaign, new(campaign.Revision), token);
        attempt.Play = result.PlayReceipt;
        attempt.State = result.Ok ? "started" : "recovery_required";
        attempt.PendingStep = result.Ok ? null : result.PlayReceipt?.FailedStage;
        WriteCampaignFile(attempt.AttemptId, attempt);
        return new(result.Ok, result.Error, result.Status, Receipt: JsonSerializer.SerializeToElement(attempt, _host.Json), RecoveryRequired: !result.Ok);
    }

    static string CampaignScopeHash(StudioCreatorCampaignAttempt attempt) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { attempt.AttemptId, attempt.Play!.BindingInstanceId,
            runs = attempt.Runs.OrderBy(value => value.RunId).Select(value => new { value.RunId, value.ScopeId, value.Outcome }),
            handoffs = attempt.Continuations.OrderBy(value => value.HandoffId).Select(value => new { value.HandoffId, value.State, value.BindingChangeId }) })));

    RuntimeBindingChange ReadCampaignBinding(StudioCreatorCampaignAttempt attempt, string id)
    {
        if (!CreatorOperationJournal.Safe(id)) throw new InvalidDataException("campaign_binding_identity_invalid");
        var path = Path.Combine(_host.FindValheim()!, "BepInEx", "config", "comfy-quest-runtime", "state", "binding-changes", id + ".json");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > RuntimeBindingCoordinator.MaxChangeBytes)
            throw new InvalidDataException("campaign_binding_receipt_missing");
        var change = Newtonsoft.Json.JsonConvert.DeserializeObject<RuntimeBindingChange>(File.ReadAllText(path));
        if (change is null || change.Schema != RuntimeBindingChange.CurrentSchema || change.Applied is null
            || change.ChangeId != id || change.WorldId != attempt.Play!.WorldUid
            || change.Applied.BindingInstanceId != attempt.Play.BindingInstanceId
            || change.Applied.ContentHash != attempt.Play.ContentHash || change.State != "applied")
            throw new InvalidDataException("campaign_binding_scope_changed");
        // A continuation may have been bound after a reload assigned a new ZDO ID.
        // Restore addresses each original change; Runtime resolves its durable marker.
        return change;
    }

    async Task<StudioRunControlResult> CompleteCampaignRunControlAsync(string project, string run,
        StudioRunControlResult result, StudioRuntimeIdentity identity, string operation, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(35);
        while (result.Queued && result.RequestId is not null && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(200, token);
            result = _runControl.ReceiptPinned(project, result.RequestId, run, identity, operation);
        }
        return result.Queued ? new(false, true, "campaign_run_control_pending", null, result.RequestId) : result;
    }

    async Task<string?> ClearCreatorCampaignFixtureAsync(string command, string preparation, CancellationToken token)
    {
        if (OperatingSystem.IsWindows()) return "campaign_fixture_clear_requires_linux";
        var script = Path.Combine(AppContext.BaseDirectory, "campaign", "campaign_play_prerequisites.py");
        var output = Path.Combine(_host.StateDirectory, "captures", "campaign-reset", command);
        var start = new ProcessStartInfo("python3") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { script, "--operation-id", command, "--valheim-root", _host.FindValheim()!,
            "--output", output, "--clear-preparation", preparation }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch { try { process.Kill(true); } catch { } throw; }
        var text = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) return "campaign_fixture_clear_failed:" + error.Trim()[..Math.Min(500, error.Trim().Length)];
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("state").GetString() == "cleared"
            && doc.RootElement.GetProperty("preparation_id").GetString() == preparation ? null : "campaign_fixture_clear_receipt_invalid";
    }
}

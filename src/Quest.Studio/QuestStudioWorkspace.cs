using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ComfyQuestContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Comfy.Quest.Studio;

internal sealed class QuestStudioWorkspace
{
    const int MaxDraftBytes = 1024 * 1024;
    readonly object _gate = new();
    readonly string _root;
    readonly string _projectsRoot;
    readonly string _legacyProjectPath;
    readonly string _legacyHistoryPath;
    readonly IQuestStudioHost _host;
    readonly QuestPackPublisher _publisher;

    public QuestStudioWorkspace(IQuestStudioHost host, QuestPackPublisher publisher)
    {
        _host = host;
        _publisher = publisher;
        _root = Path.Combine(host.StateDirectory, "quest-studio");
        _projectsRoot = Path.Combine(_root, "projects");
        _legacyProjectPath = Path.Combine(_root, "project.json");
        _legacyHistoryPath = Path.Combine(_root, "history");
        Directory.CreateDirectory(_projectsRoot);
        EnsureLegacyMigration();
    }

    public object Catalog()
    {
        var effects = CatalogEffects();
        return new
        {
        schema_version = 3,
        adaptive_measures = AdaptiveMeasureCatalog.All.Select(measure => new
        {
            name = measure.Name,
            label = measure.Label,
            unit = measure.Unit,
            unit_singular = measure.UnitSingular,
            minimum = measure.Minimum,
            maximum = measure.Maximum,
            default_value = measure.DefaultValue,
            requires_spawn_action = measure.RequiresSpawnAction,
            palette = measure.Palette,
            comparison = "gte"
        }).ToArray(),
        spatial_predicates = SpatialPredicateCatalog.All.Select(predicate => new
        {
            name = predicate.Name,
            label = predicate.Label,
            requires_value = predicate.RequiresValue,
            value_minimum = predicate.ValueMinimum,
            value_maximum = predicate.ValueMaximum,
            value_unit = predicate.ValueUnit,
            allows_player_anchor = predicate.AllowsPlayerAnchor,
            palette = predicate.Palette,
            radius_minimum = SpatialPredicateCatalog.RadiusMinimum,
            radius_maximum = SpatialPredicateCatalog.RadiusMaximum,
            coordinate_bound = SpatialPredicateCatalog.MaxWorldCoordinate
        }).ToArray(),
        quick_presets = CreatorSignalCatalog.All.Select(signal => new
        {
            id = signal.Id,
            label = signal.Label,
            instruction = signal.Instruction,
            event_name = signal.EventName,
            target = signal.Target,
            target_policy = signal.TargetPolicy,
            privacy = signal.Privacy,
            runtime_adapter = signal.RuntimeAdapter
        }).ToArray(),
        creator_events = CreatorEventCatalog.All.Select(definition =>
        {
            var production = RuntimeProductionEventCatalog.TryGet(definition.Name, out var runtime);
            var authorFields = definition.Fields
                .Where(field => !production || RuntimeProductionEventCatalog.IsAllowedWhere(definition.Name, field.Name))
                .Select(field => new
                {
                    name = field.Name,
                    label = field.Label,
                    description = field.Description,
                    example = field.Example,
                    draft_by_default = field.DraftByDefault,
                    production_emitted = production && RuntimeProductionEventCatalog.IsAllowedWhere(definition.Name, field.Name)
                }).ToArray();
            return new
            {
                id = definition.Name,
                name = definition.Name,
                event_name = definition.Name,
                label = definition.Label,
                instruction = definition.Instruction,
                category = definition.Category,
                school = definition.Category,
                profile = definition.Profile,
                target = new { kind = definition.TargetKind, description = definition.TargetDescription, example = definition.ExampleTarget },
                target_kind = definition.TargetKind,
                target_description = definition.TargetDescription,
                example_target = definition.ExampleTarget,
                target_policy = production ? runtime.TargetPolicy : "research-only",
                fixed_target = production ? runtime.FixedTarget : null,
                targets = production && runtime.AllowedTargets.Count > 0
                    ? runtime.AllowedTargets.ToArray()
                    : CreatorSignalCatalog.All
                        .Where(signal => signal.EventName == definition.Name && signal.Target is not null)
                        .Select(signal => signal.Target!)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                actor_roles = production && runtime.FixedWhere.TryGetValue("actor_role", out var actorRole)
                    ? new[] { actorRole }
                    : Array.Empty<string>(),
                fixed_where = production
                    ? runtime.FixedWhere.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal),
                supports_weapon_skill = production && runtime.EmitsWeaponSkill,
                supports_projectile = production && runtime.EmitsProjectile,
                research_supports_weapon_skill = definition.SupportsWeaponSkill,
                research_supports_projectile = definition.SupportsProjectile,
                where_fields = authorFields,
                research_where_fields = definition.Fields.Select(field => new
                {
                    name = field.Name, label = field.Label, description = field.Description,
                    example = field.Example, draft_by_default = field.DraftByDefault
                }).ToArray(),
                privacy = definition.Privacy,
                production_available = production,
                addable = production,
                availability = new
                {
                    production_available = production,
                    runtime_adapter = production ? runtime.RuntimeAdapter : null,
                    evidence_state = production ? runtime.EvidenceState : definition.EvidenceState,
                    evidence_revision = production ? runtime.EvidenceRevision : definition.EvidenceRevision
                }
            };
        }).ToArray(),
        engine_events = RuntimeProductionEventCatalog.EngineEvents.Select(definition => new
        {
            id = definition.Name,
            name = definition.Name,
            event_name = definition.Name,
            label = definition.Label,
            instruction = definition.Instruction,
            production_available = true,
            addable = true,
            runtime_adapter = definition.RuntimeAdapter,
            target_policy = definition.TargetPolicy,
            fixed_target = definition.FixedTarget,
            targets = definition.AllowedTargets,
            actor_roles = definition.Name == "chat_received" ? new[] { "peer", "listen_host" } : Array.Empty<string>(),
            fixed_where = new Dictionary<string, string>(StringComparer.Ordinal),
            required_where_fields = definition.RequiredWhereFields,
            where_fields = definition.AllowedWhereFields.Select(field => new
            {
                name = field,
                label = Humanize(field),
                description = field == "timer_id" ? "The exact quest-owned timer identifier." : "The observed actor role.",
                example = field == "timer_id" ? "timer-1" : "peer",
                draft_by_default = true,
                production_emitted = true
            }).ToArray(),
            privacy = definition.Privacy
        }).ToArray(),
        effects,
        events = CatalogEvents(),
        actions = effects,
        templates = new object[]
        {
            new { id = "blank", label = "Blank local quest", note = "One low-friction local beat." },
            new { id = "signal-circuit", label = "R&D Signal Circuit", note = "One compact lap across chat, timing, inventory, healing, and reward." },
            new { id = "cooperative-ritual", label = "Two Voices, One Rune", note = "Captured 1.6 peer Shout then listen-host sign placement." },
            new { id = "reward-cleanup", label = "Reward, spawn, and cleanup", note = "Proven 1.5 grant, marked spawn, timer, and cleanup." }
        },
        scenarios = Scenarios(),
        simple_triggers = CreatorSignalCatalog.All.Select(signal => new
        {
            id = signal.Id,
            label = signal.Label,
            instruction = signal.Instruction,
            event_name = signal.EventName,
            target = signal.Target,
            target_policy = signal.TargetPolicy,
            privacy = signal.Privacy,
            lab_profile = signal.LabProfile,
            lab_route = signal.LabRoute,
            runtime_adapter = signal.RuntimeAdapter
        }).ToArray(),
        charm_target_kinds = AllCharmTargetKinds
        };
    }

    static object[] CatalogEffects() => new object[]
    {
        new { id = "message", label = "Show message" },
        new { id = "timer_start", label = "Start timer", seconds_min = 1, seconds_max = 86400 },
        new { id = "timer_cancel", label = "Cancel timer" },
        new { id = "grant_item", label = "Grant item", items = new Dictionary<string,int> { ["Wood"] = 50, ["Stone"] = 50, ["Resin"] = 50, ["Coins"] = 100 } },
        new { id = "spawn", label = "Spawn", count_min = 1, count_max = 16, radius_min = 0, radius_max = 30,
            prefabs = new Dictionary<string,string[]> { ["creature"] = new[] { "Greyling", "Boar" }, ["item"] = new[] { "Wood", "Stone", "Resin" }, ["piece"] = new[] { "sign", "wood_floor" } } },
        new { id = "clear_spawned", label = "Clear a prior spawn" }
    };

    static object[] CatalogEvents()
    {
        var creator = CreatorEventCatalog.All
            .Where(definition => RuntimeProductionEventCatalog.Contains(definition.Name))
            .Select(definition => (object)new
            {
                id = definition.Name,
                label = definition.Label,
                targets = CreatorSignalCatalog.All.Where(signal => signal.EventName == definition.Name && signal.Target is not null).Select(signal => signal.Target!).ToArray(),
                actor_roles = definition.Name == "piece_placed" ? new[] { "listen_host" } : Array.Empty<string>(),
                note = definition.Privacy
            });
        var engine = RuntimeProductionEventCatalog.EngineEvents.Select(definition => (object)new
        {
            id = definition.Name,
            label = definition.Label,
            targets = definition.Name == "chat_received" ? new[] { "shout", "normal" } : Array.Empty<string>(),
            actor_roles = definition.Name == "chat_received" ? new[] { "peer", "listen_host" } : Array.Empty<string>(),
            note = definition.Privacy
        });
        return creator.Concat(engine).ToArray();
    }

    static readonly string[] AllCharmTargetKinds =
        { "sign", "player_built_piece", "item_stand", "dedicated_charm" };

    public IReadOnlyList<StudioProjectSummary> ListProjects()
    {
        lock (_gate)
        {
            return Directory.GetDirectories(_projectsRoot)
                .Select(path => ReadDocument(Path.Combine(path, "draft.json")))
                .Where(project => project is not null)
                .Cast<StudioProjectDocument>()
                .OrderByDescending(project => project.UpdatedUtc)
                .Select(Summary)
                .ToArray();
        }
    }

    public StudioProjectDocument? ReadProject(string projectId)
    {
        if (!SafeLocalId(projectId)) return null;
        lock (_gate) return ReadDocument(DraftPath(projectId));
    }

    public StudioProjectDocument CreateProject(string? templateId)
    {
        lock (_gate)
        {
            var projectId = "project-" + Guid.NewGuid().ToString("N")[..12];
            var suffix = projectId[^6..];
            var project = (templateId ?? "blank") switch
            {
                "signal-circuit" => SignalCircuitTemplate(projectId, suffix),
                "cooperative-ritual" => CooperativeTemplate(projectId, suffix),
                "reward-cleanup" => RewardTemplate(projectId, suffix),
                _ => BlankTemplate(projectId, suffix)
            };
            WriteProject(project, create: true);
            return project;
        }
    }

    public StudioProjectDocument? Duplicate(string projectId)
    {
        lock (_gate)
        {
            var source = ReadDocument(DraftPath(projectId));
            if (source is null) return null;
            var clone = Clone(source);
            clone.ProjectId = "project-" + Guid.NewGuid().ToString("N")[..12];
            clone.Revision = 1;
            clone.PackId = BoundedId(source.PackId + "-copy");
            clone.ExperienceId = BoundedId(source.ExperienceId + "-copy");
            clone.Title = source.Title + " (copy)";
            clone.Version = "1.0.0";
            clone.UpdatedUtc = DateTimeOffset.UtcNow;
            WriteProject(clone, create: true);
            return clone;
        }
    }

    public StudioSaveResult SaveDraft(string projectId, StudioSaveRequest? request)
    {
        if (!SafeLocalId(projectId) || request?.Project is null || request.Project.ProjectId != projectId)
            return StudioSaveResult.Fail("project_identity_invalid");
        if (!NormalizeDocument(request.Project)) return StudioSaveResult.Fail("project_schema_or_where_invalid");
        var envelopeError = ValidateDraftEnvelope(request.Project);
        if (envelopeError is not null) return StudioSaveResult.Fail(envelopeError);
        lock (_gate)
        {
            var current = ReadDocument(DraftPath(projectId));
            if (current is null) return StudioSaveResult.Fail("project_missing");
            if (request.ExpectedRevision != current.Revision)
                return StudioSaveResult.RevisionConflict(current);
            var saved = Clone(request.Project);
            saved.Revision = current.Revision + 1;
            saved.UpdatedUtc = DateTimeOffset.UtcNow;
            WriteProject(saved, create: false);
            return StudioSaveResult.Success(saved);
        }
    }

    public StudioSaveResult BumpPatch(string projectId, int expectedRevision)
    {
        lock (_gate)
        {
            var current = ReadDocument(DraftPath(projectId));
            if (current is null) return StudioSaveResult.Fail("project_missing");
            if (current.Revision != expectedRevision) return StudioSaveResult.RevisionConflict(current);
            if (!SemanticVersion.TryParse(current.Version, out var version)) return StudioSaveResult.Fail("version_invalid");
            current.Version = $"{version.Major}.{version.Minor}.{version.Patch + 1}";
            current.Revision++;
            current.UpdatedUtc = DateTimeOffset.UtcNow;
            WriteProject(current, create: false);
            return StudioSaveResult.Success(current);
        }
    }

    public StudioCertificationResult Validate(string projectId) => Certify(projectId, storeSnapshot: false);
    public StudioCertificationResult Certify(string projectId) => Certify(projectId, storeSnapshot: true);

    StudioCertificationResult Certify(string projectId, bool storeSnapshot)
    {
        var project = ReadProject(projectId);
        if (project is null) return StudioCertificationResult.Fail("project_missing");
        var compiled = StudioGraphCompiler.Compile(project);
        if (!compiled.Ok) return compiled;
        if (storeSnapshot) StoreSnapshot(project, compiled.ContentHash!);
        return compiled;
    }

    public async Task<StudioPublishResult> PublishAsync(string projectId, int expectedRevision, CancellationToken cancellationToken)
    {
        StudioProjectDocument project;
        lock (_gate)
        {
            project = ReadDocument(DraftPath(projectId))!;
            if (project is null) return StudioPublishResult.Fail("project_missing");
            if (project.Revision != expectedRevision) return StudioPublishResult.RevisionConflict(project);
        }
        var compiled = StudioGraphCompiler.Compile(project);
        if (!compiled.Ok) return StudioPublishResult.Fail(compiled.Error!, compiled.Diagnostics);
        StoreSnapshot(project, compiled.ContentHash!);
        var bytes = StudioGraphCompiler.BuildPack(project, compiled.ExperienceJson!, compiled.ContentHash!);
        await using var stream = new MemoryStream(bytes, writable: false);
        var receipt = await _publisher.PublishAsync(stream, $"{project.PackId}-{project.Version}.questpack", cancellationToken);
        return receipt.Ok
            ? StudioPublishResult.Success(receipt.Status, receipt, project, compiled.Diagnostics)
            : StudioPublishResult.Fail(receipt.Error!, receipt.Diagnostics ?? Array.Empty<ContractDiagnostic>(), receipt);
    }

    public async Task<StudioPublishResult> PlayRevisionAsync(string projectId, int expectedRevision, CancellationToken cancellationToken)
    {
        StudioProjectDocument project;
        lock (_gate)
        {
            project = ReadDocument(DraftPath(projectId))!;
            if (project is null) return StudioPublishResult.Fail("project_missing");
            if (project.Revision != expectedRevision) return StudioPublishResult.RevisionConflict(project);
        }
        var valheim = _host.FindValheim();
        if (valheim is null) return StudioPublishResult.Fail("valheim_not_found");
        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        var devStatus = new RuntimeDevChannelStatusStore(runtimeRoot).Read();
        var now = DateTimeOffset.UtcNow;
        var devConnected = devStatus is not null && devStatus.ObservedUtc <= now.AddSeconds(1)
            && devStatus.ObservedUtc >= now.AddSeconds(-3);
        if (!devConnected) return StudioPublishResult.Fail("dev_channel_disconnected");
        if (devStatus!.Armed != true) return StudioPublishResult.Fail("dev_channel_not_armed");
        var compiled = StudioGraphCompiler.Compile(project);
        if (!compiled.Ok) return StudioPublishResult.Fail(compiled.Error!, compiled.Diagnostics);
        var bytes = StudioGraphCompiler.BuildPack(project, compiled.ExperienceJson!, compiled.ContentHash!);
        await using var stream = new MemoryStream(bytes, writable: false);
        var shortHash = compiled.ContentHash!.Substring(0, 12);
        var filename = $"{project.PackId}-{project.Version}-r{project.Revision}-{shortHash}.questpack";
        var receipt = await _publisher.PublishDevAsync(stream, filename, cancellationToken);
        return receipt.Ok
            ? StudioPublishResult.Success(receipt.Status, receipt, project, compiled.Diagnostics)
            : StudioPublishResult.Fail(receipt.Error!, receipt.Diagnostics ?? Array.Empty<ContractDiagnostic>(), receipt);
    }

    public object History(string projectId)
    {
        if (!SafeLocalId(projectId)) return new { schema_version = 3, versions = Array.Empty<StudioProjectSnapshot>() };
        lock (_gate)
        {
            var root = HistoryPath(projectId);
            if (!Directory.Exists(root)) return new { schema_version = 3, versions = Array.Empty<StudioProjectSnapshot>() };
            var values = Directory.GetFiles(root, "*.json")
                .Select(ReadSnapshot).Where(value => value is not null).Cast<StudioProjectSnapshot>()
                .OrderByDescending(value => value.SavedUtc).Take(100).ToArray();
            return new { schema_version = 3, versions = values };
        }
    }

    public StudioRehearsalResult Rehearse(string projectId, StudioRehearsalRequest? request)
    {
        var project = ReadProject(projectId);
        if (project is null) return StudioRehearsalResult.Fail("project_missing");
        var compiled = StudioGraphCompiler.Compile(project);
        if (!compiled.Ok) return StudioRehearsalResult.Fail(compiled.Error!, compiled.Diagnostics);
        return StudioRehearsal.Run(project, compiled.Document!, request ?? new());
    }

    public StudioRuntimeStatus RuntimeStatus(string projectId)
    {
        var project = ReadProject(projectId);
        if (project is null) return StudioRuntimeStatus.Unavailable("project_missing");
        var compiled = StudioGraphCompiler.Compile(project);
        if (!compiled.Ok) return StudioRuntimeStatus.Unavailable("draft_invalid", compiled.Diagnostics);
        var valheim = _host.FindValheim();
        if (valheim is null) return StudioRuntimeStatus.Unavailable("valheim_not_found");
        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        var store = new QuestPackStore(runtimeRoot);
        var published = store.CheckInbox(RuntimeProductionEventCatalog.CreateSet()).FirstOrDefault(candidate => candidate.IsValid
            && candidate.Manifest.PackId == project.PackId && candidate.Manifest.Version == project.Version
            && string.Equals(candidate.ContentHash, compiled.ContentHash, StringComparison.OrdinalIgnoreCase));
        var devPublished = store.CheckDevInbox(RuntimeProductionEventCatalog.CreateSet()).FirstOrDefault(candidate => candidate.IsValid
            && candidate.Manifest.PackId == project.PackId && candidate.Manifest.Version == project.Version
            && string.Equals(candidate.ContentHash, compiled.ContentHash, StringComparison.OrdinalIgnoreCase));
        var devStatus = new RuntimeDevChannelStatusStore(runtimeRoot).Read();
        var devConnected = devStatus is not null && devStatus.ObservedUtc <= DateTimeOffset.UtcNow.AddSeconds(1)
            && devStatus.ObservedUtc >= DateTimeOffset.UtcNow.AddSeconds(-3);
        ComfyQuestContracts.ActiveSet? active = null;
        try
        {
            var activePath = Path.Combine(runtimeRoot, "active", "active-set.json");
            var activeInfo = new FileInfo(activePath);
            if (activeInfo.Exists && activeInfo.Length is >= 0 and <= 128 * 1024)
                active = JsonConvert.DeserializeObject<ComfyQuestContracts.ActiveSet>(File.ReadAllText(activePath));
        }
        catch { /* surfaced as not active */ }
        var receipts = new List<RuntimeReceipt>();
        foreach (var path in new RuntimeReceiptStore(runtimeRoot).List(100))
        {
            try
            {
                var receiptInfo = new FileInfo(path);
                if (!receiptInfo.Exists || receiptInfo.Length is < 0 or > 128 * 1024) continue;
                var receipt = JsonConvert.DeserializeObject<RuntimeReceipt>(File.ReadAllText(path));
                var matchesProject = receipt is not null && receipt.PackId == project.PackId && receipt.Version == project.Version
                    && !string.IsNullOrWhiteSpace(receipt.ContentHash)
                    && string.Equals(receipt.ContentHash, compiled.ContentHash, StringComparison.OrdinalIgnoreCase);
                if (matchesProject)
                    receipts.Add(receipt!);
            }
            catch { }
        }
        var orderedReceipts = receipts
            .OrderByDescending(value => value.AtUtc)
            .ThenByDescending(value => value.Operation == "transition")
            .ToArray();
        var isActive = active?.PackId == project.PackId && active.Version == project.Version
            && string.Equals(active.ContentHash, compiled.ContentHash, StringComparison.OrdinalIgnoreCase);
        var activeRelation = active is null ? "none" : isActive ? "current" : "other_version";
        var activeReceipts = !isActive ? Array.Empty<RuntimeReceipt>()
            : string.IsNullOrWhiteSpace(active?.ActivationId) ? orderedReceipts
            : orderedReceipts.Where(value => value.ActivationId == active.ActivationId).ToArray();
        var checkedOk = orderedReceipts.Any(value => value.Operation == "check" && value.Status == "accepted");
        var bound = activeReceipts.Any(value => (value.Operation == "bind" && value.Status is "inscribed" or "accepted")
            || (value.Operation == "dev_rebind" && (value.Status == "rebound" || value.Error == "already_current")));
        var completed = activeReceipts.FirstOrDefault(value => value.Operation == "transition" && value.Status is "complete" or "fail");
        var liveReceipt = activeReceipts.FirstOrDefault(value =>
            (value.Operation == "transition" && value.Status == "advanced")
            || (value.Operation == "event" && value.Status is "matched" or "ignored"));
        var currentStageId = liveReceipt?.Operation == "transition"
            ? liveReceipt.NextStageId ?? liveReceipt.CurrentStageId ?? liveReceipt.StageId
            : liveReceipt?.CurrentStageId ?? liveReceipt?.StageId;
        currentStageId ??= devConnected ? devStatus?.CurrentStageId : null;
        if (bound && string.IsNullOrWhiteSpace(currentStageId)) currentStageId = compiled.Document!.EntryStage;
        var currentCount = liveReceipt?.Operation == "event" ? liveReceipt.CurrentCount : null;
        var requiredCount = liveReceipt?.Operation == "event" ? liveReceipt.RequiredCount : null;
        var phase = completed is not null ? completed.Status : bound ? "bound" : isActive ? "active"
            : devPublished is not null && active is not null ? "other_active"
            : devPublished is not null ? !devConnected ? "dev_disconnected" : devStatus!.Armed ? "dev_queued" : "dev_waiting_for_arm"
            : checkedOk ? "checked" : published is not null ? "published" : "certified";
        var currentStage = compiled.Document!.Stages.FirstOrDefault(value => value.Id == currentStageId);
        var currentRoute = currentStage?.Transitions?
            .OrderByDescending(value => value.Priority)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        var liveInstruction = DescribeLiveTrigger(currentRoute?.When);
        if (requiredCount > 1)
            liveInstruction += $" ({currentCount.GetValueOrDefault()}/{requiredCount})";
        var instruction = phase switch
        {
            "certified" => !devConnected
                ? "Start the local Runtime; Studio is waiting for its read-only heartbeat."
                : devStatus?.Armed != true
                    ? "Open F9 once and arm the dev channel for this game session."
                    : "Runtime is armed. Play this revision when ready, or publish it independently.",
            "dev_disconnected" => "Start the local Runtime; Studio is waiting for its read-only heartbeat.",
            "dev_waiting_for_arm" => "Open F9 once and arm the dev channel for this game session.",
            "dev_queued" => "Revision transferred; the armed Runtime is validating and activating it.",
            "other_active" => "A different revision is active. Play this revision to return to the current draft.",
            "published" => "In Valheim, press F10 to check the published update.",
            "checked" => "In Valheim, press F11 to load the validated update.",
            "active" => "Open F9, aim at the Charm target, then press backtick twice for CHECK and CAST.",
            "bound" => liveInstruction,
            "complete" => "The live Runtime reports this quest complete.",
            "fail" => "The live Runtime reports the fail outcome.",
            _ => "Inspect the latest Runtime receipt."
        };
        return new(2, true, phase, instruction, compiled.ContentHash, devPublished?.Sha256 ?? published?.Sha256, active,
            currentStageId, currentCount, requiredCount, orderedReceipts.Take(20).ToArray(), compiled.Diagnostics)
        {
            ActiveRelation = activeRelation,
            RouteLabels = RuntimeRouteLabels(compiled.Document!),
            EffectLabels = RuntimeEffectLabels(compiled.Document!),
            DevConnected = devConnected,
            DevPublished = devPublished is not null,
            DevStatus = devStatus
        };
    }

    static string DescribeLiveTrigger(TriggerExpression? trigger)
    {
        if (trigger is null) return "Perform the current quest beat.";
        var op = (trigger.Op ?? string.Empty).ToUpperInvariant();
        if (op == "THRESHOLD") return StudioRehearsal.AdaptiveLiveInstruction(trigger);
        if (op == "SPATIAL") return StudioRehearsal.SpatialLiveInstruction(trigger);
        if (op == "COUNT" && trigger.Children?.FirstOrDefault() is { } counted)
            return DescribeLiveTrigger(counted);
        if (op is "ANY" or "ALL" or "SEQUENCE" && trigger.Children?.Count > 0)
        {
            var separator = op == "SEQUENCE" ? "; then " : op == "ALL" ? "; and " : "; or ";
            var prefix = op == "ALL" ? "Complete all: " : op == "SEQUENCE" ? "In order: " : "Complete either: ";
            return prefix + string.Join(separator, trigger.Children.Select(DescribeLiveTrigger));
        }
        if (CreatorSignalCatalog.TryDescribe(trigger.Event, trigger.Target, out var signal))
            return signal.Instruction;
        if (CreatorEventCatalog.TryGet(trigger.Event, out var definition))
            return definition.Instruction;
        if (RuntimeProductionEventCatalog.TryGetEngine(trigger.Event, out var engine))
            return engine.Instruction;
        return string.IsNullOrWhiteSpace(trigger.Event)
            ? "Perform the current quest beat."
            : "Perform: " + trigger.Event.Replace('_', ' ');
    }

    static IReadOnlyDictionary<string, string> RuntimeRouteLabels(ExperienceDocument document) =>
        (document.Stages ?? new()).SelectMany(stage => stage.Transitions ?? new())
            .Where(transition => !string.IsNullOrWhiteSpace(transition.Id))
            .ToDictionary(transition => transition.Id, transition => DescribeLiveTrigger(transition.When), StringComparer.Ordinal);

    static IReadOnlyDictionary<string, string> RuntimeEffectLabels(ExperienceDocument document) =>
        (document.Stages ?? new()).SelectMany(stage =>
                (stage.EntryActions ?? new()).Concat((stage.Transitions ?? new()).SelectMany(transition => transition.Actions ?? new())))
            .Where(action => !string.IsNullOrWhiteSpace(action.Id))
            .ToDictionary(action => action.Id, DescribeRuntimeEffect, StringComparer.Ordinal);

    static string DescribeRuntimeEffect(ExperienceAction action)
    {
        string Value(string name) => action.Parameters is not null && action.Parameters.TryGetValue(name, out var value)
            ? value.ToString() : string.Empty;
        return action.Type switch
        {
            "message" => "Show message",
            "timer_start" => $"Start timer {Value("timer_id")} for {Value("seconds")} seconds",
            "timer_cancel" => $"Cancel timer {Value("timer_id")}",
            "grant_item" => $"Give {Value("quantity")} {Value("item")}",
            "spawn" => $"Create {Value("count")} {Humanize(Value("prefab"))}",
            "clear_spawned" => "Clear staged objects",
            _ => Humanize(action.Type ?? action.Id)
        };
    }

    void EnsureLegacyMigration()
    {
        lock (_gate)
        {
            if (Directory.GetFiles(_projectsRoot, "draft.json", SearchOption.AllDirectories).Length > 0 || !File.Exists(_legacyProjectPath)) return;
            try
            {
                var legacy = System.Text.Json.JsonSerializer.Deserialize<QuestStudioProject>(File.ReadAllText(_legacyProjectPath), _host.Json);
                if (legacy is null) return;
                var projectId = SafeLocalId(legacy.PackId) ? legacy.PackId : "migrated-project";
                if (Directory.Exists(ProjectPath(projectId))) projectId += "-" + Guid.NewGuid().ToString("N")[..6];
                var migrated = FromLegacy(projectId, legacy);
                WriteProject(migrated, create: true);
                if (!Directory.Exists(_legacyHistoryPath)) return;
                foreach (var path in Directory.GetFiles(_legacyHistoryPath, "*.json"))
                {
                    try
                    {
                        var snapshot = System.Text.Json.JsonSerializer.Deserialize<QuestStudioSnapshot>(File.ReadAllText(path), _host.Json);
                        if (snapshot?.Project is null) continue;
                        StoreSnapshot(FromLegacy(projectId, snapshot.Project), snapshot.ContentHash, snapshot.SavedUtc);
                    }
                    catch { }
                }
            }
            catch { /* legacy state remains untouched and can be retried after repair */ }
        }
    }

    static StudioProjectDocument FromLegacy(string projectId, QuestStudioProject legacy)
    {
        var sourceStages = legacy.Stages is { Count: > 0 }
            ? legacy.Stages
            : new[] { new QuestStudioStage("start", legacy.Event, legacy.Target, null, legacy.Message) };
        var nodes = new List<StudioNode>();
        for (var index = 0; index < sourceStages.Count; index++)
        {
            var stage = sourceStages[index];
            nodes.Add(new StudioNode
            {
                Id = stage.Id,
                Label = Humanize(stage.Id),
                X = 80 + index * 300,
                Y = 120 + (index % 2) * 60,
                Routes = new()
                {
                    new StudioRoute
                    {
                        Id = $"transition-{index + 1:00}", Priority = 100,
                        Event = stage.Event, Target = stage.Target, ActorRole = stage.ActorRole,
                        DestinationNodeId = index + 1 < sourceStages.Count ? sourceStages[index + 1].Id : null,
                        Outcome = index + 1 == sourceStages.Count ? "complete" : null,
                        Actions = new() { new StudioAction { Id = $"message-{index + 1:00}", Type = "message", Text = stage.Message } }
                    }
                }
            });
        }
        return new StudioProjectDocument
        {
            ProjectId = projectId, Revision = 1, UpdatedUtc = DateTimeOffset.UtcNow,
            PackId = legacy.PackId, Version = legacy.Version, ExperienceId = legacy.ExperienceId,
            Title = legacy.Title, BindingTargetKind = legacy.BindingTargetKind ?? "sign",
            EntryNodeId = nodes[0].Id, Nodes = nodes
        };
    }

    static StudioProjectDocument BlankTemplate(string projectId, string suffix) => new()
    {
        ProjectId = projectId, Revision = 1, UpdatedUtc = DateTimeOffset.UtcNow,
        PackId = "quest-" + suffix, Version = "1.0.0", ExperienceId = "quest-" + suffix,
        Title = "New Quest", BindingTargetKind = null, BindingTargetKinds = AllCharmTargetKinds.ToList(), EntryNodeId = "start",
        Nodes = new()
        {
            new StudioNode { Id = "start", Label = "First step", X = 120, Y = 160, Routes = new()
            {
                new StudioRoute { Id = "finish", Priority = 100, Event = "chat_sent", Target = "normal", Outcome = "complete",
                    Actions = new() { new StudioAction { Id = "message-finish", Type = "message", Text = "The Charm answers." } } }
            } }
        }
    };

    static StudioProjectDocument CooperativeTemplate(string projectId, string suffix)
    {
        var legacy = QuestStudioProject.Starter() with { PackId = "two-voices-" + suffix, ExperienceId = "two-voices-" + suffix };
        var project = FromLegacy(projectId, legacy);
        project.BindingTargetKind = null;
        project.BindingTargetKinds = AllCharmTargetKinds.ToList();
        return project;
    }

    static StudioProjectDocument RewardTemplate(string projectId, string suffix) => new()
    {
        ProjectId = projectId, Revision = 1, UpdatedUtc = DateTimeOffset.UtcNow,
        PackId = "reward-cleanup-" + suffix, Version = "1.0.0", ExperienceId = "reward-cleanup-" + suffix,
        Title = "Reward and Marked Spawn", BindingTargetKind = null, BindingTargetKinds = AllCharmTargetKinds.ToList(), EntryNodeId = "ready",
        Nodes = new()
        {
            new StudioNode { Id = "ready", Label = "Cast the reward", X = 100, Y = 150, Routes = new()
            {
                new StudioRoute { Id = "cast", Priority = 100, Event = "sign_written", Target = "sign", DestinationNodeId = "cleanup", Actions = new()
                {
                    new StudioAction { Id = "message-cast", Type = "message", Text = "The Charm grants one wood and raises a marked floor." },
                    new StudioAction { Id = "grant-wood", Type = "grant_item", Item = "Wood", Quantity = 1 },
                    new StudioAction { Id = "raise-floor", Type = "spawn", Kind = "piece", Prefab = "wood_floor", Count = 1, Radius = 3 },
                    new StudioAction { Id = "cleanup-timer", Type = "timer_start", TimerId = "cleanup", Seconds = 5 }
                } }
            } },
            new StudioNode { Id = "cleanup", Label = "Clear the mark", X = 430, Y = 150, Routes = new()
            {
                new StudioRoute { Id = "clear-marked-floor", Priority = 100, Event = "timer_elapsed", TimerId = "cleanup", Outcome = "complete", Actions = new()
                {
                    new StudioAction { Id = "clear-floor", Type = "clear_spawned", ActionId = "raise-floor" },
                    new StudioAction { Id = "message-cleared", Type = "message", Text = "The marked floor fades; everything else remains." }
                } }
            } }
        }
    };

    static StudioProjectDocument SignalCircuitTemplate(string projectId, string suffix) => new()
    {
        ProjectId = projectId, Revision = 1, UpdatedUtc = DateTimeOffset.UtcNow,
        PackId = "signal-circuit-" + suffix, Version = "1.0.0", ExperienceId = "signal-circuit-" + suffix,
        Title = "R&D Signal Circuit", BindingTargetKind = null, BindingTargetKinds = AllCharmTargetKinds.ToList(), EntryNodeId = "say",
        Nodes = new()
        {
            FastBeat("say", "say", "wait", actions: new()
            {
                new StudioAction { Id = "message-start", Type = "message", Text = "The circuit wakes. Hold the rhythm." },
                new StudioAction { Id = "start-wait", Type = "timer_start", TimerId = "circuit-wait", Seconds = 5 }
            }),
            FastBeat("wait", "wait", "shout", timerId: "circuit-wait"),
            FastBeat("shout", "shout", "drop-twice"),
            FastBeat("drop-twice", "drop", "pickup", repeatCount: 2, withinSeconds: 30,
                actions: new() { new StudioAction { Id = "message-drop", Type = "message", Text = "Two offerings heard." } }),
            FastBeat("pickup", "pickup", "equip"),
            FastBeat("equip", "equip", "consume"),
            FastBeat("consume", "consume", "heal"),
            FastBeat("heal", "heal", null, actions: new()
            {
                new StudioAction { Id = "message-complete", Type = "message", Text = "The signal circuit is complete." },
                new StudioAction { Id = "reward-wood", Type = "grant_item", Item = "Wood", Quantity = 5 }
            })
        }
    };

    static StudioNode FastBeat(string nodeId, string signalId, string? nextNodeId,
        int repeatCount = 1, int? withinSeconds = null, string? timerId = null,
        List<StudioAction>? actions = null)
    {
        if (!CreatorSignalCatalog.TryGet(signalId, out var signal))
            throw new InvalidOperationException("Generated signal catalog is missing " + signalId);
        return new StudioNode
        {
            Id = nodeId, Label = signal.Label, X = 100, Y = 100,
            Routes = new()
            {
                new StudioRoute
                {
                    Id = "advance-" + nodeId, Priority = 100, Event = signal.EventName,
                    Target = signal.Target, TimerId = timerId, RepeatCount = repeatCount,
                    WithinSeconds = withinSeconds, DestinationNodeId = nextNodeId,
                    Outcome = nextNodeId is null ? "complete" : null, Actions = actions ?? new()
                }
            }
        };
    }

    static object[] Scenarios() => new object[]
    {
        new { id = "captured-1.6", label = "Captured 1.6 multiplayer contract", proof_level = "captured_contract_fixture", steps = new object[]
        {
            new { kind = "event", event_name = "chat_received", target = "shout", actor_role = "peer", timer_id = (string?)null, seconds = 0 },
            new { kind = "event", event_name = "piece_placed", target = "sign", actor_role = "listen_host", timer_id = (string?)null, seconds = 0 }
        } },
        new { id = "reward-cleanup", label = "Reward and cleanup", proof_level = "rehearsal", steps = new object[]
        {
            new { kind = "event", event_name = "sign_written", target = "sign", actor_role = (string?)null, timer_id = (string?)null, seconds = 0 },
            new { kind = "advance", event_name = (string?)null, target = (string?)null, actor_role = (string?)null, timer_id = (string?)null, seconds = 5 }
        } },
        new { id = "signal-circuit", label = "R&D Signal Circuit", proof_level = "rehearsal", steps = new object[]
        {
            new { kind = "event", event_name = "chat_sent", target = "normal", actor_role = (string?)null, timer_id = (string?)null, seconds = 0 },
            new { kind = "advance", event_name = (string?)null, target = (string?)null, actor_role = (string?)null, timer_id = (string?)null, seconds = 5 },
            new { kind = "event", event_name = "chat_sent", target = "shout", actor_role = (string?)null, timer_id = (string?)null, seconds = 0 },
            new { kind = "event", event_name = "item_dropped", target = "Wood", actor_role = (string?)null, timer_id = (string?)null, seconds = 0 },
            new { kind = "event", event_name = "item_picked_up", target = "Wood", actor_role = (string?)null, timer_id = (string?)null, seconds = 0 },
            new { kind = "event", event_name = "item_dropped", target = "Wood", actor_role = (string?)null, timer_id = (string?)null, seconds = 0 },
            new { kind = "event", event_name = "item_picked_up", target = "Wood", actor_role = (string?)null, timer_id = (string?)null, seconds = 0 },
            new { kind = "event", event_name = "item_equipped", target = "Hammer", actor_role = (string?)null, timer_id = (string?)null, seconds = 0 },
            new { kind = "event", event_name = "item_consumed", target = "CookedMeat", actor_role = (string?)null, timer_id = (string?)null, seconds = 0 },
            new { kind = "event", event_name = "character_healed", target = "you", actor_role = (string?)null, timer_id = (string?)null, seconds = 0 }
        } }
    };

    string? ValidateDraftEnvelope(StudioProjectDocument project)
    {
        var bytes = Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(project, _host.Json));
        if (bytes > MaxDraftBytes) return "draft_too_large";
        if (project.Nodes is null || project.Nodes.Count > ExperienceSchema.MaxStages) return "draft_node_bounds";
        if (project.Nodes.Any(node => node is null || node.Id?.Length > 64 || node.Label?.Length > 120 || !double.IsFinite(node.X) || !double.IsFinite(node.Y)
            || node.Routes is null || node.Routes.Count > 64 || node.Routes.Any(route => route is null || route.Id?.Length > 64 || route.Target?.Length > 120
                || route.Where is null || route.Where.Count > 16 || route.Where.Any(pair => pair.Key.Length > 64 || pair.Value?.Length is null or > 120)
                || route.Actions is null || route.Actions.Count > 64 || route.Actions.Any(action => action is null || action.Id?.Length > 64 || action.Text?.Length > 500))))
            return "draft_field_bounds";
        return null;
    }

    void StoreSnapshot(StudioProjectDocument project, string contentHash, DateTimeOffset? savedUtc = null)
    {
        lock (_gate)
        {
            var root = HistoryPath(project.ProjectId);
            Directory.CreateDirectory(root);
            foreach (var existingPath in Directory.GetFiles(root, "*.json"))
            {
                var existing = ReadSnapshot(existingPath);
                if (existing is not null
                    && existing.Project.ProjectId == project.ProjectId
                    && existing.Project.PackId == project.PackId
                    && existing.Project.Version == project.Version
                    && string.Equals(existing.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            var identityBytes = Encoding.UTF8.GetBytes($"{project.ProjectId}\n{project.PackId}\n{project.Version}\n{contentHash}");
            var identityHash = Convert.ToHexString(SHA256.HashData(identityBytes)).ToLowerInvariant();
            var target = Path.Combine(root, $"{identityHash}-{contentHash}.json");
            if (File.Exists(target)) return;
            AtomicWrite(target, System.Text.Json.JsonSerializer.Serialize(new StudioProjectSnapshot(3, contentHash, savedUtc ?? DateTimeOffset.UtcNow, project), _host.Json), create: true);
        }
    }

    StudioProjectSnapshot? ReadSnapshot(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxDraftBytes) return null;
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<StudioProjectSnapshot>(File.ReadAllText(path), _host.Json);
            return snapshot is not null && NormalizeDocument(snapshot.Project) ? snapshot : null;
        }
        catch { return null; }
    }

    StudioProjectDocument? ReadDocument(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxDraftBytes) return null;
            var project = System.Text.Json.JsonSerializer.Deserialize<StudioProjectDocument>(File.ReadAllText(path), _host.Json);
            return project is not null && NormalizeDocument(project) ? project : null;
        }
        catch { return null; }
    }

    internal static bool NormalizeDocument(StudioProjectDocument project)
    {
        if (project.SchemaVersion is < 2 or > StudioProjectDocument.CurrentSchemaVersion) return false;
        foreach (var route in (project.Nodes ?? new()).SelectMany(node => node?.Routes ?? new()))
        {
            route.Where ??= new Dictionary<string, string>(StringComparer.Ordinal);
            if (!MergeLegacy("actor_role", route.LegacyActorRole) || !MergeLegacy("timer_id", route.LegacyTimerId)) return false;
            var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in route.Where.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var key = pair.Key?.Trim().ToLowerInvariant();
                var value = pair.Value?.Trim();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) || !normalized.TryAdd(key, value)) return false;
            }
            route.Where = normalized;
            route.LegacyActorRole = null;
            route.LegacyTimerId = null;

            bool MergeLegacy(string key, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return true;
                if (route.Where.TryGetValue(key, out var current)) return string.Equals(current, value, StringComparison.Ordinal);
                route.Where[key] = value;
                return true;
            }
        }
        project.SchemaVersion = StudioProjectDocument.CurrentSchemaVersion;
        return true;
    }

    void WriteProject(StudioProjectDocument project, bool create)
    {
        Directory.CreateDirectory(ProjectPath(project.ProjectId));
        AtomicWrite(DraftPath(project.ProjectId), System.Text.Json.JsonSerializer.Serialize(project, _host.Json), create);
    }

    static void AtomicWrite(string target, string content, bool create)
    {
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        try
        {
            if (create && !File.Exists(target)) File.Move(temporary, target);
            else File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    string ProjectPath(string projectId) => Path.Combine(_projectsRoot, projectId);
    string DraftPath(string projectId) => Path.Combine(ProjectPath(projectId), "draft.json");
    string HistoryPath(string projectId) => Path.Combine(ProjectPath(projectId), "history");
    static StudioProjectSummary Summary(StudioProjectDocument project) => new(project.ProjectId, project.PackId, project.Version, project.Title, project.Revision, project.UpdatedUtc, project.Nodes?.Count ?? 0);
    StudioProjectDocument Clone(StudioProjectDocument project) => System.Text.Json.JsonSerializer.Deserialize<StudioProjectDocument>(System.Text.Json.JsonSerializer.Serialize(project, _host.Json), _host.Json)!;
    static bool SafeLocalId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
    static string BoundedId(string value) => value.Length <= 64 ? value : value[..64];
    static string Humanize(string value) => string.Join(' ', (value ?? string.Empty).Split('-', '_').Where(part => part.Length > 0).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}

internal static class StudioGraphCompiler
{
    static readonly string[] ReviewedCharmTargets =
        { "sign", "player_built_piece", "item_stand", "dedicated_charm" };
    static readonly HashSet<string> Events = new(
        RuntimeProductionEventCatalog.All.Select(definition => definition.Name)
            .Concat(RuntimeProductionEventCatalog.EngineEvents.Select(definition => definition.Name)),
        StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> Actions = new(StringComparer.Ordinal) { "message", "timer_start", "timer_cancel", "grant_item", "spawn", "clear_spawned" };

    public static StudioCertificationResult Compile(StudioProjectDocument project)
    {
        var diagnostics = new List<ContractDiagnostic>();
        if (!SafeId(project.PackId) || !SafeId(project.ExperienceId)) Add("stable_id_invalid", "$", "Pack and experience IDs must be stable identifiers.");
        if (!SemanticVersion.TryParse(project.Version, out _)) Add("version_invalid", "$.version", "Version must be major.minor.patch.");
        if (string.IsNullOrWhiteSpace(project.Title) || project.Title.Length > 120) Add("title_invalid", "$.title", "Title is required and limited to 120 characters.");
        var targetKinds = EffectiveTargetKinds(project);
        if (targetKinds.Count == 0 || targetKinds.Any(value => !ReviewedCharmTargets.Contains(value, StringComparer.Ordinal))) Add("binding_target_kind_invalid", "$.binding_target_kinds", "Choose only supported Charm targets.");
        var nodes = project.Nodes ?? new();
        if (nodes.Count is < 1 or > ExperienceSchema.MaxStages) Add("node_count_invalid", "$.nodes", "A quest needs 1..64 nodes.");
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var globalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!SafeId(node.Id) || !nodeIds.Add(node.Id ?? string.Empty)) Add("node_id_invalid", "$.nodes", "Node IDs must be unique stable identifiers.");
            globalIds.Add(node.Id ?? string.Empty);
        }
        if (!nodeIds.Contains(project.EntryNodeId)) Add("entry_node_missing", "$.entry_node_id", "Choose an existing start node.");
        var spawnIds = nodes.SelectMany(node => node.Routes ?? new()).SelectMany(route => route.Actions ?? new()).Where(action => action.Type == "spawn").Select(action => action.Id).ToHashSet(StringComparer.Ordinal);
        var stages = new List<ExperienceStage>();
        foreach (var node in nodes)
        {
            var transitions = new List<ExperienceTransition>();
            foreach (var route in node.Routes ?? new())
            {
                var path = $"$.nodes.{node.Id}.routes.{route.Id}";
                if (!SafeId(route.Id) || !globalIds.Add(route.Id ?? string.Empty)) Add("route_id_invalid", path, "Route IDs must be globally unique stable identifiers.");
                if (!Events.Contains(route.Event ?? string.Empty)) Add("event_unknown", path + ".event", "The selected event is not in the evidence-gated Runtime production catalog.");
                if (route.Priority is < -10000 or > 10000) Add("priority_invalid", path + ".priority", "Priority must be -10000..10000.");
                if (route.Event == "chat_received" && route.ActorRole is not ("peer" or "listen_host")) Add("chat_actor_role_required", path + ".actor_role", "Chat requires peer or listen_host.");
                if (route.Event == "piece_placed" && route.ActorRole != "listen_host") Add("piece_placed_listen_host_required", path + ".actor_role", "Placement is currently a listen-host event.");
                if (route.Event == "timer_elapsed" && !SafeId(route.TimerId)) Add("timer_id_required", path + ".timer_id", "Timer events require a stable timer ID.");
                if (string.Equals(route.Target?.Trim(), "any", StringComparison.OrdinalIgnoreCase))
                    Add("target_any_literal", path + ".target", "Leave the target empty for a wildcard; the literal value 'any' is an exact target.");
                var where = NormalizeWhere(route, path, Add);
                if (route.RepeatCount is < 1 or > 16) Add("repeat_count_invalid", path + ".repeat_count", "Fast-lane repeats must be 1..16.");
                if (route.Event == "timer_elapsed" && route.RepeatCount != 1) Add("timer_repeat_unsupported", path + ".repeat_count", "Wait beats run once per started timer.");
                if (route.WithinSeconds.HasValue && (route.RepeatCount == 1 || route.WithinSeconds is < 1 or > 86400))
                    Add("repeat_window_invalid", path + ".within_seconds", "A time window requires a repeated beat and must be 1..86400 seconds.");
                var terminal = route.Outcome is "complete" or "fail";
                if ((!string.IsNullOrWhiteSpace(route.DestinationNodeId) ? 1 : 0) + (terminal ? 1 : 0) != 1) Add("route_destination_invalid", path, "Choose exactly one next node or terminal outcome.");
                if (!string.IsNullOrWhiteSpace(route.DestinationNodeId) && !nodeIds.Contains(route.DestinationNodeId)) Add("route_destination_missing", path + ".destination_node_id", "The destination node does not exist.");
                var compiledActions = new List<ExperienceAction>();
                foreach (var action in route.Actions ?? new())
                {
                    var actionPath = path + ".actions." + action.Id;
                    if (!SafeId(action.Id) || !globalIds.Add(action.Id ?? string.Empty)) Add("action_id_invalid", actionPath, "Action IDs must be globally unique stable identifiers.");
                    if (!Actions.Contains(action.Type ?? string.Empty)) { Add("action_unknown", actionPath, "The action is not implemented by Runtime."); continue; }
                    if (action.Type == "clear_spawned" && (!SafeId(action.ActionId) || !spawnIds.Contains(action.ActionId ?? string.Empty))) Add("clear_spawn_reference_invalid", actionPath + ".action_id", "Cleanup must reference a spawn action in this quest.");
                    compiledActions.Add(ToContract(action));
                }
                var eventTrigger = new TriggerExpression { Op = "EVENT", Event = route.Event, Target = NullIfWhite(route.Target), Where = where.Count == 0 ? null : where };
                var eventClause = route.RepeatCount > 1
                    ? new TriggerExpression { Op = "COUNT", Count = route.RepeatCount, WithinSeconds = (route.AdaptiveConditions?.Count ?? 0) == 0 && (route.SpatialConditions?.Count ?? 0) == 0 ? route.WithinSeconds : null, Children = new() { eventTrigger } }
                    : eventTrigger;
                var adaptive = new List<TriggerExpression>();
                var seenMeasures = new HashSet<string>(StringComparer.Ordinal);
                var conditionIndex = 0;
                foreach (var condition in route.AdaptiveConditions ?? new())
                {
                    var conditionPath = path + ".adaptive_conditions." + conditionIndex++;
                    if (condition is null)
                    {
                        Add("adaptive_condition_required", conditionPath, "Adaptive conditions cannot be null.");
                        continue;
                    }
                    if (!AdaptiveMeasureCatalog.TryGet(condition.Measure, out var measure)) Add("adaptive_measure_invalid", conditionPath + ".measure", "Choose a measure from the advanced adaptive registry.");
                    else if (!seenMeasures.Add(condition.Measure)) Add("adaptive_measure_duplicate", conditionPath + ".measure", "Each adaptive measure may appear once per route.");
                    if (!string.Equals(condition.Comparison, "gte", StringComparison.Ordinal)) Add("adaptive_comparison_invalid", conditionPath + ".comparison", "Only at least (gte) is currently supported.");
                    if (condition.Value < (measure?.Minimum ?? 1) || condition.Value > (measure?.Maximum ?? 86400)) Add("adaptive_value_invalid", conditionPath + ".value", "Adaptive value is outside the measure's reviewed bounds.");
                    var countsSpawn = measure?.RequiresSpawnAction == true;
                    if (countsSpawn && !spawnIds.Contains(condition.ActionId ?? string.Empty)) Add("adaptive_action_invalid", conditionPath + ".action_id", "Name a spawn effect from this quest so the count means one staged wave.");
                    if (!countsSpawn && measure is not null && !string.IsNullOrWhiteSpace(condition.ActionId)) Add("adaptive_action_unsupported", conditionPath + ".action_id", "This measure does not count a staged wave.");
                    adaptive.Add(new TriggerExpression { Op = "THRESHOLD", Measure = condition.Measure, Comparison = condition.Comparison, Value = condition.Value, ActionId = countsSpawn ? NullIfWhite(condition.ActionId) : null });
                }
                var spatial = new List<TriggerExpression>();
                var seenPredicates = new HashSet<string>(StringComparer.Ordinal);
                var spatialIndex = 0;
                foreach (var condition in route.SpatialConditions ?? new())
                {
                    var conditionPath = path + ".spatial_conditions." + spatialIndex++;
                    if (condition is null)
                    {
                        Add("spatial_condition_required", conditionPath, "Spatial conditions cannot be null.");
                        continue;
                    }
                    if (!SpatialPredicateCatalog.TryGet(condition.Predicate, out var predicate)) Add("spatial_predicate_invalid", conditionPath + ".predicate", "Choose a predicate from the advanced spatial registry.");
                    else if (!seenPredicates.Add(condition.Predicate)) Add("spatial_predicate_duplicate", conditionPath + ".predicate", "Each spatial predicate may appear once per route.");
                    var kind = condition.AnchorKind ?? string.Empty;
                    AreaAnchor? anchor = null;
                    if (kind is "binding" or "player") anchor = new AreaAnchor { Kind = kind };
                    else if (kind == "coordinates")
                    {
                        if (!(BoundedCoordinate(condition.X) && BoundedCoordinate(condition.Y) && BoundedCoordinate(condition.Z))) Add("spatial_coordinates_invalid", conditionPath, "Coordinates must be complete, finite, and within the reviewed world bounds.");
                        else anchor = new AreaAnchor { Kind = "coordinates", X = condition.X, Y = condition.Y, Z = condition.Z };
                    }
                    else Add("spatial_anchor_invalid", conditionPath + ".anchor_kind", "Choose the bound Charm, the player, or explicit coordinates.");
                    if (kind == "player" && predicate is not null && !predicate.AllowsPlayerAnchor) Add("spatial_anchor_player_invalid", conditionPath + ".anchor_kind", "The player anchor applies only to counting objects near the player.");
                    if (condition.Radius is < SpatialPredicateCatalog.RadiusMinimum or > SpatialPredicateCatalog.RadiusMaximum) Add("spatial_radius_invalid", conditionPath + ".radius", "Radius must be 1..100 whole meters.");
                    if (predicate is not null && predicate.RequiresValue && (condition.Value is null || condition.Value < predicate.ValueMinimum || condition.Value > predicate.ValueMaximum)) Add("spatial_value_invalid", conditionPath + ".value", "Value is outside the predicate's reviewed bounds.");
                    if (predicate is not null && !predicate.RequiresValue && condition.Value is not null) Add("spatial_value_invalid", conditionPath + ".value", "This predicate does not take a value.");
                    if (anchor is not null && predicate is not null)
                        spatial.Add(new TriggerExpression { Op = "SPATIAL", Spatial = condition.Predicate, Anchor = anchor, Radius = condition.Radius, Value = condition.Value });
                }
                var trigger = adaptive.Count == 0 && spatial.Count == 0
                    ? eventClause
                    : new TriggerExpression { Op = "ALL", WithinSeconds = route.WithinSeconds, Children = new List<TriggerExpression> { eventClause }.Concat(adaptive).Concat(spatial).ToList() };
                transitions.Add(new ExperienceTransition
                {
                    Id = route.Id, Priority = route.Priority,
                    When = trigger,
                    Actions = compiledActions, NextStage = NullIfWhite(route.DestinationNodeId), Outcome = terminal ? route.Outcome : null
                });
            }
            stages.Add(new ExperienceStage { Id = node.Id, EntryActions = new(), Transitions = transitions });
        }
        var document = new ExperienceDocument
        {
            Schema = ExperienceSchema.Id, Id = project.ExperienceId, Title = project.Title, EntryStage = project.EntryNodeId,
            Stages = stages, Bindings = new() { new ExperienceBinding { Id = "default", ExperienceId = project.ExperienceId, TargetKinds = EffectiveTargetKinds(project) } }
        };
        var json = JsonConvert.SerializeObject(document, Formatting.Indented);
        var contract = ExperienceCompiler.CompileProductionJson(json);
        diagnostics.AddRange(contract.Diagnostics);
        if (diagnostics.Count > 0) return StudioCertificationResult.Fail("graph_invalid", diagnostics);
        var hash = QuestPackContent.ComputeHash(new[] { new KeyValuePair<string, byte[]>($"experiences/{project.ExperienceId}.json", Encoding.UTF8.GetBytes(json)) });
        return StudioCertificationResult.Success(json, hash, document);

        void Add(string code, string path, string message) => diagnostics.Add(new(code, path, message));
    }

    static Dictionary<string, string> NormalizeWhere(StudioRoute route, string path, Action<string, string, string> add)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in (route.Where ?? new()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var key = pair.Key?.Trim().ToLowerInvariant();
            var value = pair.Value?.Trim();
            var fieldPath = path + ".where." + (key ?? "field");
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) || value.Length > 120)
            {
                add("where_value_invalid", fieldPath, "Event filters require a non-empty value no longer than 120 characters.");
                continue;
            }
            if (!RuntimeProductionEventCatalog.IsAllowedWhere(route.Event, key))
            {
                add("where_field_unsupported", fieldPath, "This Runtime adapter does not emit that filter field.");
                continue;
            }
            if (string.Equals(value, "any", StringComparison.OrdinalIgnoreCase))
            {
                add("where_any_literal", fieldPath, "Remove this filter to match any value; 'any' is not a wildcard token.");
                continue;
            }
            value = NormalizeFieldValue(key, value, fieldPath, add);
            if (!string.IsNullOrWhiteSpace(value) && !result.TryAdd(key, value))
                add("where_field_duplicate", fieldPath, "Each event filter may appear only once.");
        }
        if (RuntimeProductionEventCatalog.TryGetEngine(route.Event, out var engine))
        {
            foreach (var required in engine.RequiredWhereFields)
                if (!result.ContainsKey(required)) add("where_field_required", path + ".where." + required, "This engine event requires the filter.");
        }
        if (RuntimeProductionEventCatalog.TryGet(route.Event, out var runtime))
        {
            foreach (var fixedField in runtime.FixedWhere)
            {
                if (!result.TryGetValue(fixedField.Key, out var actual))
                    add("where_field_required", path + ".where." + fixedField.Key, "This Runtime adapter emits one required fixed filter value.");
                else if (!string.Equals(actual, fixedField.Value, StringComparison.Ordinal))
                    add("where_field_fixed", path + ".where." + fixedField.Key, "This Runtime adapter emits one fixed value for this filter.");
            }
        }
        return result;
    }

    static string? NormalizeFieldValue(string key, string value, string path, Action<string, string, string> add)
    {
        switch (key)
        {
            case "actor_role":
                if (value is not ("peer" or "listen_host"))
                {
                    add("actor_role_invalid", path, "Actor role must be peer or listen_host.");
                    return null;
                }
                return value;
            case "timer_id":
                if (!SafeId(value))
                {
                    add("timer_id_invalid", path, "Timer IDs must be stable identifiers.");
                    return null;
                }
                return value;
            case "projectile":
                if (!string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    add("projectile_filter_invalid", path, "Projectile filtering is opt-in; use true or remove the filter.");
                    return null;
                }
                return "true";
            case "quantity":
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity is < 1 or > 1_000_000)
                {
                    add("quantity_filter_invalid", path, "Exact event quantity must be an integer from 1 to 1000000.");
                    return null;
                }
                return quantity.ToString(CultureInfo.InvariantCulture);
            case "amount":
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || !double.IsFinite(amount))
                {
                    add("amount_filter_invalid", path, "Exact event amount must be a finite invariant number.");
                    return null;
                }
                return amount.ToString("R", CultureInfo.InvariantCulture);
            default:
                return value;
        }
    }

    static List<string> EffectiveTargetKinds(StudioProjectDocument project) =>
        !string.IsNullOrWhiteSpace(project.BindingTargetKind)
            ? new() { project.BindingTargetKind }
            : (project.BindingTargetKinds ?? new()).Distinct(StringComparer.Ordinal).ToList();

    public static byte[] BuildPack(StudioProjectDocument project, string experienceJson, string contentHash)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Write(archive, "manifest.json", JsonConvert.SerializeObject(new QuestPackManifest { PackId = project.PackId, Version = project.Version, ContentHash = contentHash }, Formatting.Indented));
            Write(archive, $"experiences/{project.ExperienceId}.json", experienceJson);
        }
        return output.ToArray();
    }

    static ExperienceAction ToContract(StudioAction action)
    {
        var values = new Dictionary<string, JToken>();
        switch (action.Type)
        {
            case "message": values["text"] = action.Text ?? string.Empty; break;
            case "timer_start": values["timer_id"] = action.TimerId ?? string.Empty; values["seconds"] = action.Seconds; break;
            case "timer_cancel": values["timer_id"] = action.TimerId ?? string.Empty; break;
            case "grant_item": values["item"] = action.Item ?? string.Empty; values["quantity"] = action.Quantity; break;
            case "spawn": values["kind"] = action.Kind ?? string.Empty; values["prefab"] = action.Prefab ?? string.Empty; values["count"] = action.Count; values["radius"] = action.Radius; break;
            case "clear_spawned": values["action_id"] = action.ActionId ?? string.Empty; break;
        }
        return new ExperienceAction { Id = action.Id, Type = action.Type, Parameters = values };
    }

    static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
    static bool SafeId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '$');
    static bool BoundedCoordinate(double? value) => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) && Math.Abs(value.Value) <= SpatialPredicateCatalog.MaxWorldCoordinate;
    static string? NullIfWhite(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

internal static class StudioRehearsal
{
    public static StudioRehearsalResult Run(StudioProjectDocument project, ExperienceDocument document, StudioRehearsalRequest request)
    {
        var now = DateTimeOffset.UnixEpoch;
        var stageId = document.EntryStage;
        var stageEnteredUtc = now;
        var lastProgressUtc = now;
        string? outcome = null;
        var history = new List<RuntimeEvent>();
        var timers = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var inventory = new Dictionary<string, int>(StringComparer.Ordinal);
        var spawns = new Dictionary<string, int>(StringComparer.Ordinal);
        // Rehearsal has no ZDOs, so "gone" is whatever the transcript asked to remove.
        var despawned = new Dictionary<string, int>(StringComparer.Ordinal);
        var deathsInStage = 0;
        var transcript = new List<string>();
        var trace = new List<StudioRehearsalTrace>();
        var guided = string.Equals(request.Mode, "guided", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(request.Mode) && (request.Steps?.Count ?? 0) == 0);
        List<string> limitations;
        List<string> availablePaths;
        List<StudioRehearsalInput> steps;
        if (guided) steps = BuildGuidedSteps(project, document, out limitations, out availablePaths);
        else
        {
            steps = request.Steps ?? new List<StudioRehearsalInput>();
            limitations = new List<string>();
            availablePaths = new List<string>();
        }
        foreach (var input in steps)
        {
            if (outcome is not null) break;
            if (input.Kind == "advance")
            {
                now = now.AddSeconds(Math.Clamp(input.Seconds, 0, 86400));
                while (outcome is null)
                {
                    var due = timers.Where(pair => pair.Value <= now).OrderBy(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(due.Key)) break;
                    timers.Remove(due.Key);
                    Emit(new RuntimeEvent { Name = ExperienceSchema.TimerElapsedEvent, At = due.Value, Fields = new Dictionary<string, string> { ["timer_id"] = due.Key } });
                }
                continue;
            }
            if (input.Kind == "despawn")
            {
                var target = input.ActionId ?? string.Empty;
                if (!spawns.ContainsKey(target)) return StudioRehearsalResult.Fail("rehearsal_despawn_unknown");
                despawned[target] = Math.Min(spawns[target], despawned.GetValueOrDefault(target) + Math.Max(1, input.Count));
                continue;
            }
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in (input.Fields ?? new()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var key = pair.Key?.Trim().ToLowerInvariant();
                var value = pair.Value?.Trim();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) || !fields.TryAdd(key, value))
                    return StudioRehearsalResult.Fail("rehearsal_fields_invalid");
            }
            if (!MergeLegacy("actor_role", input.ActorRole) || !MergeLegacy("timer_id", input.TimerId))
                return StudioRehearsalResult.Fail("rehearsal_fields_conflict");
            Emit(new RuntimeEvent { Name = input.EventName, Target = input.Target, At = now, Fields = fields, PosX = input.X, PosY = input.Y, PosZ = input.Z });

            bool MergeLegacy(string key, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return true;
                if (fields.TryGetValue(key, out var current)) return string.Equals(current, value, StringComparison.Ordinal);
                fields[key] = value;
                return true;
            }
        }
        var proofLevel = !guided && request.ScenarioId == "captured-1.6" && IsCapturedContractFixture(steps)
            ? "captured_contract_fixture"
            : "rehearsal";
        return new(3, true, null, proofLevel, "Synthetic rehearsal only; this does not prove a Valheim adapter or live mutation.", stageId, outcome, trace, transcript, inventory, spawns,
            timers.ToDictionary(pair => pair.Key, pair => (int)Math.Max(0, (pair.Value - now).TotalSeconds)), guided ? steps : Array.Empty<StudioRehearsalInput>(), limitations, availablePaths);

        int Live(string action) => Math.Max(0, spawns.GetValueOrDefault(action) - despawned.GetValueOrDefault(action));

        static bool IsCapturedContractFixture(IReadOnlyList<StudioRehearsalInput> values) => values.Count == 2
            && values[0].Kind == "event" && values[0].EventName == "chat_received" && values[0].Target == "shout"
            && (values[0].Fields?.GetValueOrDefault("actor_role") ?? values[0].ActorRole) == "peer"
            && values[1].Kind == "event" && values[1].EventName == "piece_placed" && values[1].Target == "sign"
            && (values[1].Fields?.GetValueOrDefault("actor_role") ?? values[1].ActorRole) == "listen_host";

        void Emit(RuntimeEvent evt)
        {
            if (string.IsNullOrWhiteSpace(evt.Name) || outcome is not null) return;
            var stage = document.Stages.FirstOrDefault(value => value.Id == stageId);
            if (stage is null) return;
            var ordered = (stage.Transitions ?? new()).OrderByDescending(value => value.Priority).ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
            // The synthetic binding rehearses at the origin; this path's spawned objects rehearse
            // beside it so area predicates can be exercised without a live scene.
            if (string.Equals(evt.Name, ExperienceSchema.PlayerDiedEvent, StringComparison.OrdinalIgnoreCase)) deathsInStage++;
            var context = new TriggerEvaluationContext
            {
                At = evt.At, StageEnteredUtc = stageEnteredUtc, LastProgressUtc = lastProgressUtc,
                BindingPosition = new SpatialPoint(0, 0, 0),
                SpawnedPositions = spawns.Keys.SelectMany(action => Enumerable.Repeat(new SpatialPoint(0, 0, 0), Live(action))).ToArray(),
                AuthoredAnchors = SpatialEvaluator.AnchorMap(document),
                DeathsInStage = deathsInStage,
                SpawnsByAction = spawns.ToDictionary(pair => pair.Key, pair => new SpawnTally(pair.Value, Live(pair.Key)), StringComparer.Ordinal)
            };
            var progressBefore = ordered.Sum(value => TriggerEvaluator.EventProgress(value.When, history));
            history.Add(evt);
            var madeProgress = ordered.Sum(value => TriggerEvaluator.EventProgress(value.When, history)) > progressBefore;
            var transition = ordered.FirstOrDefault(value => TriggerEvaluator.Matches(value.When, history, context));
            if (transition is null)
            {
                if (madeProgress) lastProgressUtc = evt.At;
                var candidate = ordered.FirstOrDefault();
                var partial = TriggerEvaluator.Measure(candidate?.When, history, context);
                trace.Add(new(trace.Count + 1, evt.Name, evt.Target, stageId, null, Array.Empty<string>(), stageId, null,
                    "ignored", partial.Current, partial.Required, Describe(candidate?.When)));
                return;
            }
            var progress = TriggerEvaluator.Measure(transition.When, history, context);
            var effects = new List<string>();
            foreach (var action in transition.Actions ?? new())
            {
                string P(string key) => action.Parameters is not null && action.Parameters.TryGetValue(key, out var value) ? value.ToString() : string.Empty;
                int I(string key) => action.Parameters is not null && action.Parameters.TryGetValue(key, out var value) ? value.ToObject<int>() : 0;
                switch (action.Type)
                {
                    case "message": transcript.Add(P("text")); effects.Add("message: " + P("text")); break;
                    case "timer_start": timers[P("timer_id")] = now.AddSeconds(I("seconds")); effects.Add($"timer {P("timer_id")} +{I("seconds")}s"); break;
                    case "timer_cancel": timers.Remove(P("timer_id")); effects.Add("cancel timer " + P("timer_id")); break;
                    case "grant_item": inventory[P("item")] = inventory.GetValueOrDefault(P("item")) + I("quantity"); effects.Add($"grant {I("quantity")} {P("item")}"); break;
                    case "spawn": spawns[action.Id] = spawns.GetValueOrDefault(action.Id) + I("count"); effects.Add($"spawn {I("count")} {P("prefab")}"); break;
                    case "clear_spawned": var removed = spawns.GetValueOrDefault(P("action_id")); spawns.Remove(P("action_id")); despawned.Remove(P("action_id")); effects.Add($"clear {removed} from {P("action_id")}"); break;
                }
            }
            var from = stageId;
            if (!string.IsNullOrWhiteSpace(transition.NextStage))
            {
                stageId = transition.NextStage;
                stageEnteredUtc = evt.At;
                lastProgressUtc = evt.At;
                deathsInStage = 0;
            }
            else if (madeProgress) lastProgressUtc = evt.At;
            outcome = string.IsNullOrWhiteSpace(transition.Outcome) ? null : transition.Outcome;
            history.Clear();
            trace.Add(new(trace.Count + 1, evt.Name, evt.Target, from, transition.Id, effects, stageId, outcome,
                "matched", progress.Current, progress.Required, outcome is null ? Describe(document.Stages.FirstOrDefault(value => value.Id == stageId)?.Transitions?.OrderByDescending(value => value.Priority).FirstOrDefault()?.When) : null));
        }
    }

    static List<StudioRehearsalInput> BuildGuidedSteps(StudioProjectDocument project, ExperienceDocument document,
        out List<string> limitations, out List<string> availablePaths)
    {
        var steps = new List<StudioRehearsalInput>();
        limitations = new List<string>();
        availablePaths = new List<string>();
        var nodes = (project.Nodes ?? new()).Where(node => node is not null).ToDictionary(node => node.Id, StringComparer.Ordinal);
        var timerSeconds = new Dictionary<string, int>(StringComparer.Ordinal);
        var spawnedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var nodeId = document.EntryStage;
        while (!string.IsNullOrWhiteSpace(nodeId) && nodes.TryGetValue(nodeId, out var node) && visited.Add(nodeId))
        {
            var routes = (node.Routes ?? new()).OrderByDescending(route => route.Priority).ThenBy(route => route.Id, StringComparer.Ordinal).ToArray();
            if (routes.Length == 0) break;
            var route = routes[0];
            var adaptive = (route.AdaptiveConditions ?? new()).Where(value => value is not null).ToArray();
            // Only measures counted in seconds may set the synthetic wait; a count would poison it.
            var adaptiveSeconds = adaptive.Where(value => AdaptiveMeasureCatalog.TryGet(value.Measure, out var measure) && measure.Unit == "seconds")
                .Select(value => value.Value).DefaultIfEmpty(0).Max();
            var noProgressSeconds = adaptive.Where(value => value.Measure == "time_since_progress").Select(value => value.Value).DefaultIfEmpty(0).Max();
            var deathsRequired = adaptive.Where(value => value.Measure == AdaptiveMeasureCatalog.DeathsMeasure).Select(value => value.Value).DefaultIfEmpty(0).Max();
            var spatialConditions = (route.SpatialConditions ?? new()).Where(value => value is not null).ToArray();
            var remainedSeconds = spatialConditions.Where(value => value.Predicate == "remained").Select(value => value.Value ?? 0).DefaultIfEmpty(0).Max();
            var positioned = spatialConditions.Length > 0;
            var coordinates = spatialConditions.FirstOrDefault(value => value.AnchorKind == "coordinates" && value.X.HasValue && value.Y.HasValue && value.Z.HasValue);
            var insideX = coordinates?.X ?? 0d; var insideY = coordinates?.Y ?? 0d; var insideZ = coordinates?.Z ?? 0d;
            var maxRadius = spatialConditions.Select(value => value.Radius).DefaultIfEmpty(0).Max();
            var outsideX = insideX + maxRadius + 25 <= SpatialPredicateCatalog.MaxWorldCoordinate ? insideX + maxRadius + 25 : insideX - maxRadius - 25;
            var needsEnter = spatialConditions.Any(value => value.Predicate == "entered");
            var needsLeave = spatialConditions.Any(value => value.Predicate == "left");
            var finalInside = !needsLeave;
            if (spatialConditions.Any(value => value.Predicate == "count_in_area"))
                limitations.Add($"Route {route.Id} counts objects in an area; guided rehearsal places this path's spawned objects at the synthetic binding origin.");
            if (routes.Length > 1)
            {
                availablePaths.AddRange(routes.Select(candidate => node.Id + ":" + candidate.Id));
                limitations.Add($"Stage {node.Id} has {routes.Length} routes; this run followed highest priority route {route.Id}.");
            }
            foreach (var condition in adaptive.Where(value => AdaptiveMeasureCatalog.TryGet(value.Measure, out var measure) && measure.RequiresSpawnAction))
            {
                var staged = spawnedCounts.GetValueOrDefault(condition.ActionId ?? string.Empty);
                if (staged == 0)
                {
                    limitations.Add($"Route {route.Id} counts staged objects from {condition.ActionId}, which this path never spawned, so that condition was not satisfied.");
                    continue;
                }
                if (condition.Measure == AdaptiveMeasureCatalog.ClearedMeasure)
                {
                    steps.Add(new StudioRehearsalInput { Kind = "despawn", ActionId = condition.ActionId, Count = condition.Value });
                    limitations.Add($"Route {route.Id} waits for staged objects to be cleared; rehearsal removes {condition.Value} of them on request, while play removes one when the object itself is gone.");
                }
                else if (staged < condition.Value)
                    limitations.Add($"Route {route.Id} needs {condition.Value} staged objects still standing from {condition.ActionId}, but this path staged only {staged}.");
            }
            for (var count = 0; count < Math.Max(0, deathsRequired - (string.Equals(route.Event, ExperienceSchema.PlayerDiedEvent, StringComparison.Ordinal) ? 1 : 0)); count++)
                steps.Add(new StudioRehearsalInput { Kind = "event", EventName = ExperienceSchema.PlayerDiedEvent, Target = "you" });
            if (string.Equals(route.Event, ExperienceSchema.TimerElapsedEvent, StringComparison.Ordinal))
            {
                if (!timerSeconds.TryGetValue(route.TimerId ?? string.Empty, out var seconds))
                {
                    seconds = 1;
                    limitations.Add($"Timer {route.TimerId} was not started on the selected path, so no timer event was injected.");
                }
                if (positioned)
                    limitations.Add($"Route {route.Id} pairs a timer beat with spatial conditions; synthetic timer events carry no position, so those conditions were not satisfied.");
                steps.Add(new StudioRehearsalInput { Kind = "advance", Seconds = Math.Max(seconds, adaptiveSeconds) });
            }
            else
            {
                var target = string.IsNullOrWhiteSpace(route.Target) ? ExampleTarget(route.Event) : route.Target;
                var repeats = Math.Max(1, route.RepeatCount);
                StudioRehearsalInput Positioned(bool inside) => new()
                {
                    Kind = "event", EventName = route.Event, Target = target, Fields = SyntheticFields(route),
                    X = positioned ? (inside ? insideX : outsideX) : null,
                    Y = positioned ? insideY : null,
                    Z = positioned ? insideZ : null
                };
                if (needsEnter) steps.Add(Positioned(false));
                if (remainedSeconds > 0 || needsLeave) steps.Add(Positioned(true));
                var beforeWait = noProgressSeconds > 0 ? Math.Max(0, repeats - 1) : 0;
                for (var count = 0; count < beforeWait; count++) steps.Add(Positioned(finalInside));
                var waitSeconds = Math.Max(adaptiveSeconds, remainedSeconds);
                if (waitSeconds > 0) steps.Add(new StudioRehearsalInput { Kind = "advance", Seconds = waitSeconds });
                var afterWait = noProgressSeconds > 0 && route.WithinSeconds.HasValue && route.WithinSeconds.Value < adaptiveSeconds
                    ? repeats
                    : repeats - beforeWait;
                for (var count = 0; count < afterWait; count++) steps.Add(Positioned(finalInside));
            }
            foreach (var action in route.Actions ?? new())
            {
                if (action.Type == "timer_start" && !string.IsNullOrWhiteSpace(action.TimerId))
                    timerSeconds[action.TimerId] = Math.Clamp(action.Seconds, 1, 86400);
                else if (action.Type == "timer_cancel" && !string.IsNullOrWhiteSpace(action.TimerId))
                    timerSeconds.Remove(action.TimerId);
                else if (action.Type == "spawn" && !string.IsNullOrWhiteSpace(action.Id))
                    spawnedCounts[action.Id] = spawnedCounts.GetValueOrDefault(action.Id) + Math.Max(0, action.Count);
                else if (action.Type == "clear_spawned" && !string.IsNullOrWhiteSpace(action.ActionId))
                    spawnedCounts.Remove(action.ActionId);
            }
            nodeId = route.DestinationNodeId;
            if (route.Outcome is "complete" or "fail") break;
        }
        if (!string.IsNullOrWhiteSpace(nodeId) && !visited.Add(nodeId))
            limitations.Add("The selected route revisited a stage; guided rehearsal stopped at the cycle boundary.");
        return steps;
    }

    static Dictionary<string, string> SyntheticFields(StudioRoute route)
    {
        var fields = new Dictionary<string, string>(route.Where ?? new(), StringComparer.Ordinal);
        if (!CreatorEventCatalog.TryGet(route.Event, out var definition)) return fields;
        foreach (var field in definition.Fields)
            if (RuntimeProductionEventCatalog.IsAllowedWhere(route.Event, field.Name) && !fields.ContainsKey(field.Name))
                fields[field.Name] = field.Example;
        if (RuntimeProductionEventCatalog.TryGet(route.Event, out var runtime))
        {
            if (runtime.EmitsWeaponSkill && !fields.ContainsKey("weapon_skill")) fields["weapon_skill"] = "Axes";
            if (runtime.EmitsProjectile && !fields.ContainsKey("projectile")) fields["projectile"] = "true";
        }
        return fields;
    }

    static string? ExampleTarget(string? eventName)
    {
        if (RuntimeProductionEventCatalog.TryGet(eventName, out var runtime))
        {
            if (string.Equals(runtime.TargetPolicy, "none", StringComparison.Ordinal)) return null;
            if (string.Equals(runtime.TargetPolicy, "fixed-output", StringComparison.Ordinal)) return runtime.FixedTarget;
        }
        if (string.Equals(eventName, "chat_sent", StringComparison.OrdinalIgnoreCase)) return "normal";
        if (string.Equals(eventName, "chat_received", StringComparison.OrdinalIgnoreCase)) return "shout";
        return CreatorEventCatalog.TryGet(eventName, out var definition) && !string.Equals(definition.ExampleTarget, "any", StringComparison.OrdinalIgnoreCase)
            ? definition.ExampleTarget
            : null;
    }

    static string? Describe(TriggerExpression? trigger)
    {
        var leaf = EventLeaf(trigger);
        var instruction = leaf is not null && CreatorSignalCatalog.TryDescribe(leaf.Event, leaf.Target, out var signal) ? signal.Instruction
            : leaf is not null && CreatorEventCatalog.TryGet(leaf.Event, out var definition) ? definition.Instruction
            : leaf is not null && RuntimeProductionEventCatalog.TryGetEngine(leaf.Event, out var engine) ? engine.Instruction : null;
        var conditions = Thresholds(trigger).Select(AdaptiveConditionPhrase)
            .Concat(SpatialNodes(trigger).Select(SpatialConditionPhrase)).ToArray();
        return instruction is null || conditions.Length == 0 ? instruction : instruction.TrimEnd('.') + ", " + string.Join(" and ", conditions) + ".";
    }

    static TriggerExpression? EventLeaf(TriggerExpression? trigger)
    {
        if (trigger is null) return null;
        if (string.Equals(trigger.Op, "EVENT", StringComparison.OrdinalIgnoreCase)) return trigger;
        return (trigger.Children ?? new()).Select(EventLeaf).FirstOrDefault(value => value is not null);
    }

    static IEnumerable<TriggerExpression> Thresholds(TriggerExpression? trigger)
    {
        if (trigger is null) yield break;
        if (string.Equals(trigger.Op, "THRESHOLD", StringComparison.OrdinalIgnoreCase)) yield return trigger;
        foreach (var child in trigger.Children ?? new()) foreach (var threshold in Thresholds(child)) yield return threshold;
    }

    static IEnumerable<TriggerExpression> SpatialNodes(TriggerExpression? trigger)
    {
        if (trigger is null) yield break;
        if (string.Equals(trigger.Op, "SPATIAL", StringComparison.OrdinalIgnoreCase)) yield return trigger;
        foreach (var child in trigger.Children ?? new()) foreach (var node in SpatialNodes(child)) yield return node;
    }

    internal static string AdaptiveLiveInstruction(TriggerExpression trigger)
    {
        var amount = trigger.Value.GetValueOrDefault();
        AdaptiveMeasureCatalog.TryGet(trigger.Measure, out var measure);
        var unit = measure is null ? string.Empty : measure.UnitFor(amount);
        return trigger.Measure switch
        {
            "time_since_stage_entered" => $"Wait until this stage has lasted {amount} seconds.",
            "time_since_progress" => $"Wait until {amount} seconds have passed without quest progress.",
            AdaptiveMeasureCatalog.DeathsMeasure => $"Fall in this stage {amount} {unit}.",
            AdaptiveMeasureCatalog.ClearedMeasure => $"Clear {amount} staged {unit}.",
            AdaptiveMeasureCatalog.RemainingMeasure => $"Leave {amount} staged {unit} still standing.",
            _ => $"Wait until {measure?.Label ?? trigger.Measure} reaches {amount}."
        };
    }

    static string AdaptiveConditionPhrase(TriggerExpression trigger)
    {
        var amount = trigger.Value.GetValueOrDefault();
        AdaptiveMeasureCatalog.TryGet(trigger.Measure, out var measure);
        var unit = measure is null ? string.Empty : measure.UnitFor(amount);
        return trigger.Measure switch
        {
            "time_since_stage_entered" => $"after {amount} seconds in this stage",
            "time_since_progress" => $"after {amount} seconds without quest progress",
            AdaptiveMeasureCatalog.DeathsMeasure => $"after {amount} {unit} in this stage",
            AdaptiveMeasureCatalog.ClearedMeasure => $"once {amount} staged {unit} are cleared",
            AdaptiveMeasureCatalog.RemainingMeasure => $"with {amount} staged {unit} still standing",
            _ => $"once {measure?.Label ?? trigger.Measure} reaches {amount}"
        };
    }

    internal static string SpatialLiveInstruction(TriggerExpression trigger)
    {
        var label = SpatialEvaluator.Label(trigger.Anchor);
        var radius = trigger.Radius.GetValueOrDefault();
        return trigger.Spatial switch
        {
            "entered" => $"Enter the area {radius} m around {label}.",
            "left" => $"Leave the area {radius} m around {label}.",
            "remained" => $"Remain in the area {radius} m around {label} for {trigger.Value.GetValueOrDefault()} seconds.",
            "count_in_area" => $"Keep {trigger.Value.GetValueOrDefault()} objects within {radius} m of {label}.",
            _ => $"Stay within {radius} m of {label}."
        };
    }

    static string SpatialConditionPhrase(TriggerExpression trigger)
    {
        var label = SpatialEvaluator.Label(trigger.Anchor);
        var radius = trigger.Radius.GetValueOrDefault();
        return trigger.Spatial switch
        {
            "entered" => $"after entering the area {radius} m around {label}",
            "left" => $"after leaving the area {radius} m around {label}",
            "remained" => $"after {trigger.Value.GetValueOrDefault()} seconds in the area {radius} m around {label}",
            "count_in_area" => $"with {trigger.Value.GetValueOrDefault()} objects within {radius} m of {label}",
            _ => $"while within {radius} m of {label}"
        };
    }
}

public sealed class StudioProjectDocument
{
    public const int CurrentSchemaVersion = 3;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string ProjectId { get; set; } = string.Empty;
    public int Revision { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public string PackId { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string ExperienceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? BindingTargetKind { get; set; }
    public List<string> BindingTargetKinds { get; set; } = new();
    public string EntryNodeId { get; set; } = string.Empty;
    public List<StudioNode> Nodes { get; set; } = new();
}

public sealed class StudioNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public List<StudioRoute> Routes { get; set; } = new();
}

public sealed class StudioRoute
{
    public string Id { get; set; } = string.Empty;
    public int Priority { get; set; } = 100;
    public string Event { get; set; } = "sign_written";
    public string? Target { get; set; }
    public Dictionary<string, string> Where { get; set; } = new(StringComparer.Ordinal);
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ActorRole
    {
        get => Where is not null && Where.TryGetValue("actor_role", out var value) ? value : LegacyActorRole;
        set => SetWhere("actor_role", value);
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? TimerId
    {
        get => Where is not null && Where.TryGetValue("timer_id", out var value) ? value : LegacyTimerId;
        set => SetWhere("timer_id", value);
    }
    [System.Text.Json.Serialization.JsonPropertyName("actor_role"), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyActorRole { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("timer_id"), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyTimerId { get; set; }
    public int RepeatCount { get; set; } = 1;
    public int? WithinSeconds { get; set; }
    public string? DestinationNodeId { get; set; }
    public string? Outcome { get; set; }
    public List<StudioAction> Actions { get; set; } = new();
    public List<StudioAdaptiveCondition> AdaptiveConditions { get; set; } = new();
    public List<StudioSpatialCondition> SpatialConditions { get; set; } = new();

    void SetWhere(string key, string? value)
    {
        Where ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) Where.Remove(key);
        else Where[key] = value;
    }
}

public sealed class StudioAdaptiveCondition
{
    public string Measure { get; set; } = "time_since_stage_entered";
    public string Comparison { get; set; } = "gte";
    public int Value { get; set; } = 30;
    /// <summary>The spawn effect whose staged objects this measure counts, when it counts one.</summary>
    public string? ActionId { get; set; }
}

public sealed class StudioSpatialCondition
{
    public string Predicate { get; set; } = "within_radius";
    public string AnchorKind { get; set; } = "binding";
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Z { get; set; }
    public int Radius { get; set; } = 20;
    public int? Value { get; set; }
}

public sealed class StudioAction
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "message";
    public string? Text { get; set; }
    public string? TimerId { get; set; }
    public int Seconds { get; set; } = 5;
    public string? Item { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Kind { get; set; }
    public string? Prefab { get; set; }
    public int Count { get; set; } = 1;
    public int Radius { get; set; } = 3;
    public string? ActionId { get; set; }
}

public sealed record StudioProjectSummary(string ProjectId, string PackId, string Version, string Title, int Revision, DateTimeOffset UpdatedUtc, int NodeCount);
public sealed record StudioProjectSnapshot(int SchemaVersion, string ContentHash, DateTimeOffset SavedUtc, StudioProjectDocument Project);
public sealed record StudioSaveRequest(int ExpectedRevision, StudioProjectDocument Project);
public sealed record StudioCreateRequest(string? TemplateId);
public sealed record StudioBumpRequest(int ExpectedRevision);
public sealed record StudioPublishRequest(int ExpectedRevision);
public sealed record StudioSaveResult(bool Ok, bool Conflict, string? Error, StudioProjectDocument? Project)
{
    public static StudioSaveResult Success(StudioProjectDocument project) => new(true, false, null, project);
    public static StudioSaveResult RevisionConflict(StudioProjectDocument project) => new(false, true, "revision_conflict", project);
    public static StudioSaveResult Fail(string error) => new(false, false, error, null);
}

public sealed record StudioCertificationResult(bool Ok, string Status, string? Error, string? ExperienceJson, string? ContentHash, ExperienceDocument? Document, IReadOnlyList<ContractDiagnostic> Diagnostics)
{
    public static StudioCertificationResult Success(string json, string hash, ExperienceDocument document) => new(true, "certified", null, json, hash, document, Array.Empty<ContractDiagnostic>());
    public static StudioCertificationResult Fail(string error, IReadOnlyList<ContractDiagnostic>? diagnostics = null) => new(false, "rejected", error, null, null, null, diagnostics ?? Array.Empty<ContractDiagnostic>());
}

public sealed record StudioPublishResult(bool Ok, bool Conflict, string Status, string? Error, QuestPackPublishReceipt? Receipt, StudioProjectDocument? Project, IReadOnlyList<ContractDiagnostic> Diagnostics)
{
    public static StudioPublishResult Success(string status, QuestPackPublishReceipt receipt, StudioProjectDocument project, IReadOnlyList<ContractDiagnostic> diagnostics) =>
        new(true, false, status, null, receipt, project, diagnostics);
    public static StudioPublishResult RevisionConflict(StudioProjectDocument project) =>
        new(false, true, "conflict", "revision_conflict", null, project, Array.Empty<ContractDiagnostic>());
    public static StudioPublishResult Fail(string error, IReadOnlyList<ContractDiagnostic>? diagnostics = null, QuestPackPublishReceipt? receipt = null) =>
        new(false, false, "rejected", error, receipt, null, diagnostics ?? Array.Empty<ContractDiagnostic>());
}

public sealed class StudioRehearsalRequest
{
    public string? Mode { get; set; }
    public string? ScenarioId { get; set; }
    public List<StudioRehearsalInput> Steps { get; set; } = new();
}

public sealed class StudioRehearsalInput
{
    public string Kind { get; set; } = "event";
    public string? EventName { get; set; }
    public string? Target { get; set; }
    public string? ActorRole { get; set; }
    public string? TimerId { get; set; }
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);
    public int Seconds { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Z { get; set; }
    /// <summary>Spawn effect a "despawn" step removes staged objects from.</summary>
    public string? ActionId { get; set; }
    public int Count { get; set; }
}

public sealed record StudioRehearsalTrace(int Step, string EventName, string? Target, string FromNodeId, string? RouteId, IReadOnlyList<string> Effects, string CurrentNodeId, string? Outcome, string Status, int CurrentCount, int RequiredCount, string? NextInstruction);
public sealed record StudioRehearsalResult(int SchemaVersion, bool Ok, string? Error, string ProofLevel, string Disclaimer, string? CurrentNodeId, string? Outcome, IReadOnlyList<StudioRehearsalTrace> Trace, IReadOnlyList<string> Transcript, IReadOnlyDictionary<string,int> Inventory, IReadOnlyDictionary<string,int> Spawns, IReadOnlyDictionary<string,int> Timers,
    IReadOnlyList<StudioRehearsalInput> GeneratedSteps, IReadOnlyList<string> Limitations, IReadOnlyList<string> AvailablePaths)
{
    public static StudioRehearsalResult Fail(string error, IReadOnlyList<ContractDiagnostic>? diagnostics = null) => new(3, false, error, "rehearsal", "Synthetic rehearsal only; this does not prove a Valheim adapter or live mutation.", null, null, Array.Empty<StudioRehearsalTrace>(), Array.Empty<string>(), new Dictionary<string,int>(), new Dictionary<string,int>(), new Dictionary<string,int>(), Array.Empty<StudioRehearsalInput>(), Array.Empty<string>(), Array.Empty<string>());
}

public sealed record StudioRuntimeStatus(int SchemaVersion, bool Available, string Phase, string NextInstruction,
    string? ContentHash, string? PackageSha256, ComfyQuestContracts.ActiveSet? ActiveSet,
    string? CurrentStageId, int? CurrentCount, int? RequiredCount,
    IReadOnlyList<RuntimeReceipt> Receipts, IReadOnlyList<ContractDiagnostic> Diagnostics)
{
    public string ActiveRelation { get; init; } = "none";
    public bool DevConnected { get; init; }
    public bool DevPublished { get; init; }
    public RuntimeDevChannelStatus? DevStatus { get; init; }
    public IReadOnlyDictionary<string, string> RouteLabels { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> EffectLabels { get; init; } = new Dictionary<string, string>();

    public static StudioRuntimeStatus Unavailable(string phase, IReadOnlyList<ContractDiagnostic>? diagnostics = null) =>
        new(2, false, phase,
            phase == "draft_invalid" ? "Resolve the graph diagnostics before publishing." : "Runtime state is unavailable on this machine.",
            null, null, null, null, null, null, Array.Empty<RuntimeReceipt>(), diagnostics ?? Array.Empty<ContractDiagnostic>());
}

public sealed record StudioRuntimeReceiptSummary(
    string? Operation, string? Status, string? StageId, string? EventName,
    int? CurrentCount, int? RequiredCount, DateTimeOffset AtUtc,
    string? TransitionId, string? ActionId, string? ActivationId, string? CorrelationId, string? Error,
    string? RouteLabel, string? EffectLabel);

public sealed record StudioRuntimePassLine(string Kind, string Status, string Message,
    string? ActivationId, string? CorrelationId, DateTimeOffset? AtUtc);

public sealed record StudioRuntimeStatusView(int SchemaVersion, bool Available, string Phase, string NextInstruction,
    string? ContentHash, string? PackageSha256,
    string? ActivePackId, string? ActiveVersion, string? ActiveContentHash, string? ActiveActivationId,
    DateTimeOffset? ActivatedUtc, string ActiveRelation,
    string? CurrentStageId, int? CurrentCount, int? RequiredCount,
    IReadOnlyList<StudioRuntimeReceiptSummary> Receipts, IReadOnlyList<ContractDiagnostic> Diagnostics)
{
    public bool DevConnected { get; init; }
    public bool DevArmed { get; init; }
    public bool DevPublished { get; init; }
    public string? DevState { get; init; }
    public string? DevSessionId { get; init; }
    public string? LastRejection { get; init; }
    public IReadOnlyList<StudioRuntimePassLine> PassLines { get; init; } = Array.Empty<StudioRuntimePassLine>();
}

using System.IO.Compression;
using System.Text;
using ComfyQuestContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Comfy.Quest.Studio;

internal sealed class QuestStudioWorkspace
{
    const int MaxDraftBytes = 1024 * 1024;
    static readonly HashSet<string> SupportedEvents = new(
        CreatorSignalCatalog.All.Select(signal => signal.EventName).Concat(
            new[] { "chat_received", "kill", "piece_damaged", "piece_placed", "sign_written" }),
        StringComparer.Ordinal);
    static readonly string[] SupportedActions =
        { "message", "timer_start", "timer_cancel", "grant_item", "spawn", "clear_spawned" };

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

    public object Catalog() => new
    {
        schema_version = 2,
        events = CatalogEvents(),
        actions = new object[]
        {
            new { id = "message", label = "Show message" },
            new { id = "timer_start", label = "Start timer", seconds_min = 1, seconds_max = 86400 },
            new { id = "timer_cancel", label = "Cancel timer" },
            new { id = "grant_item", label = "Grant item", items = new Dictionary<string,int> { ["Wood"] = 50, ["Stone"] = 50, ["Resin"] = 50, ["Coins"] = 100 } },
            new { id = "spawn", label = "Spawn", prefabs = new Dictionary<string,string[]> { ["creature"] = new[] { "Greyling", "Boar" }, ["item"] = new[] { "Wood", "Stone", "Resin" }, ["piece"] = new[] { "sign", "wood_floor" } } },
            new { id = "clear_spawned", label = "Clear a prior spawn" }
        },
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

    static object[] CatalogEvents()
    {
        var fast = CreatorSignalCatalog.All
            .GroupBy(signal => signal.EventName, StringComparer.Ordinal)
            .Select(group => (object)new
            {
                id = group.Key,
                label = group.First().Label,
                targets = group.Where(signal => signal.Target is not null).Select(signal => signal.Target!).ToArray(),
                actor_roles = Array.Empty<string>(),
                note = string.Join(" ", group.Select(signal => signal.Privacy).Distinct(StringComparer.Ordinal))
            });
        return fast.Concat(new object[]
        {
            new { id = "chat_received", label = "Chat received", targets = new[] { "shout", "normal" }, actor_roles = new[] { "peer", "listen_host" }, note = "Listen-host observation; message text is never persisted." },
            new { id = "kill", label = "Creature killed", targets = Array.Empty<string>(), actor_roles = Array.Empty<string>(), note = "A creature killed by the local authoritative player." },
            new { id = "piece_damaged", label = "Bound piece damaged", targets = Array.Empty<string>(), actor_roles = Array.Empty<string>(), note = "Local-player damage to the exact bound piece." },
            new { id = "piece_placed", label = "Piece placed", targets = new[] { "sign", "wood_floor" }, actor_roles = new[] { "listen_host" }, note = "A piece placed by the listen host." },
            new { id = "sign_written", label = "Sign written", targets = new[] { "sign" }, actor_roles = Array.Empty<string>(), note = "A local sign edit; text remains private." }
        }).ToArray();
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

    public async Task<StudioPublishResult> PublishAsync(string projectId, CancellationToken cancellationToken)
    {
        var project = ReadProject(projectId);
        if (project is null) return StudioPublishResult.Fail("project_missing");
        var compiled = StudioGraphCompiler.Compile(project);
        if (!compiled.Ok) return StudioPublishResult.Fail(compiled.Error!, compiled.Diagnostics);
        StoreSnapshot(project, compiled.ContentHash!);
        var bytes = StudioGraphCompiler.BuildPack(project, compiled.ExperienceJson!, compiled.ContentHash!);
        await using var stream = new MemoryStream(bytes, writable: false);
        var receipt = await _publisher.PublishAsync(stream, $"{project.PackId}-{project.Version}.questpack", cancellationToken);
        return receipt.Ok
            ? new(true, receipt.Status, null, receipt, compiled.Diagnostics)
            : StudioPublishResult.Fail(receipt.Error!, receipt.Diagnostics ?? Array.Empty<ContractDiagnostic>(), receipt);
    }

    public object History(string projectId)
    {
        if (!SafeLocalId(projectId)) return new { schema_version = 2, versions = Array.Empty<StudioProjectSnapshot>() };
        lock (_gate)
        {
            var root = HistoryPath(projectId);
            if (!Directory.Exists(root)) return new { schema_version = 2, versions = Array.Empty<StudioProjectSnapshot>() };
            var values = Directory.GetFiles(root, "*.json")
                .Select(ReadSnapshot).Where(value => value is not null).Cast<StudioProjectSnapshot>()
                .OrderByDescending(value => value.SavedUtc).Take(100).ToArray();
            return new { schema_version = 2, versions = values };
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
        var published = store.CheckInbox().FirstOrDefault(candidate => candidate.IsValid
            && candidate.Manifest.PackId == project.PackId && candidate.Manifest.Version == project.Version
            && string.Equals(candidate.ContentHash, compiled.ContentHash, StringComparison.OrdinalIgnoreCase));
        var publishedAt = published is null ? DateTimeOffset.MaxValue : File.GetLastWriteTimeUtc(published.Path);
        ComfyQuestContracts.ActiveSet? active = null;
        try
        {
            var activePath = Path.Combine(runtimeRoot, "active", "active-set.json");
            if (File.Exists(activePath)) active = JsonConvert.DeserializeObject<ComfyQuestContracts.ActiveSet>(File.ReadAllText(activePath));
        }
        catch { /* surfaced as not active */ }
        var receipts = new List<RuntimeReceipt>();
        foreach (var path in new RuntimeReceiptStore(runtimeRoot).List(100))
        {
            try
            {
                var receipt = JsonConvert.DeserializeObject<RuntimeReceipt>(File.ReadAllText(path));
                var matchesProject = receipt is not null && receipt.PackId == project.PackId && receipt.Version == project.Version
                    && (string.IsNullOrWhiteSpace(receipt.ContentHash) || string.Equals(receipt.ContentHash, compiled.ContentHash, StringComparison.OrdinalIgnoreCase));
                var isPostPublishCheck = receipt is not null && published is not null && receipt.Operation == "check" && receipt.AtUtc >= publishedAt;
                if (matchesProject || isPostPublishCheck)
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
        var checkedOk = orderedReceipts.Any(value => value.Operation == "check" && value.Status == "accepted");
        var bound = orderedReceipts.Any(value => value.Operation == "bind" && value.Status is "inscribed" or "accepted");
        var completed = orderedReceipts.FirstOrDefault(value => value.Operation == "transition" && value.Status is "complete" or "fail");
        var liveReceipt = orderedReceipts.FirstOrDefault(value =>
            (value.Operation == "transition" && value.Status == "advanced")
            || (value.Operation == "event" && value.Status is "matched" or "ignored"));
        var currentStageId = liveReceipt?.Operation == "transition"
            ? liveReceipt.NextStageId ?? liveReceipt.CurrentStageId ?? liveReceipt.StageId
            : liveReceipt?.CurrentStageId ?? liveReceipt?.StageId;
        if (bound && string.IsNullOrWhiteSpace(currentStageId)) currentStageId = compiled.Document!.EntryStage;
        var currentCount = liveReceipt?.Operation == "event" ? liveReceipt.CurrentCount : null;
        var requiredCount = liveReceipt?.Operation == "event" ? liveReceipt.RequiredCount : null;
        var phase = completed is not null ? completed.Status : bound ? "bound" : isActive ? "active" : checkedOk ? "checked" : published is not null ? "published" : "certified";
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
            "certified" => "Publish this version to the Runtime inbox.",
            "published" => "In Valheim, press F10 to check the published update.",
            "checked" => "In Valheim, press F11 to load the validated update.",
            "active" => "Open F9, aim at the Charm target, then press backtick twice for CHECK and CAST.",
            "bound" => liveInstruction,
            "complete" => "The live Runtime reports this quest complete.",
            "fail" => "The live Runtime reports the fail outcome.",
            _ => "Inspect the latest Runtime receipt."
        };
        return new(2, true, phase, instruction, compiled.ContentHash, published?.Sha256, active,
            currentStageId, currentCount, requiredCount, orderedReceipts.Take(20).ToArray(), compiled.Diagnostics);
    }

    static string DescribeLiveTrigger(TriggerExpression? trigger)
    {
        var leaf = string.Equals(trigger?.Op, "COUNT", StringComparison.OrdinalIgnoreCase)
            ? trigger?.Children?.FirstOrDefault()
            : trigger;
        if (leaf is not null && CreatorSignalCatalog.TryDescribe(leaf.Event, leaf.Target, out var signal))
            return signal.Instruction;
        return string.IsNullOrWhiteSpace(leaf?.Event)
            ? "Perform the current quest beat."
            : "Perform: " + leaf.Event.Replace('_', ' ');
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
            var target = Path.Combine(root, contentHash + ".json");
            if (File.Exists(target)) return;
            AtomicWrite(target, System.Text.Json.JsonSerializer.Serialize(new StudioProjectSnapshot(2, contentHash, savedUtc ?? DateTimeOffset.UtcNow, project), _host.Json), create: true);
        }
    }

    StudioProjectSnapshot? ReadSnapshot(string path)
    {
        try { return new FileInfo(path).Length <= MaxDraftBytes ? System.Text.Json.JsonSerializer.Deserialize<StudioProjectSnapshot>(File.ReadAllText(path), _host.Json) : null; }
        catch { return null; }
    }

    StudioProjectDocument? ReadDocument(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length <= MaxDraftBytes ? System.Text.Json.JsonSerializer.Deserialize<StudioProjectDocument>(File.ReadAllText(path), _host.Json) : null; }
        catch { return null; }
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
        CreatorSignalCatalog.All.Select(signal => signal.EventName).Concat(
            new[] { "chat_received", "kill", "piece_damaged", "piece_placed", "sign_written" }),
        StringComparer.Ordinal);
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
                if (!Events.Contains(route.Event ?? string.Empty)) Add("event_unknown", path + ".event", "The selected event has no Runtime adapter.");
                if (route.Priority is < -10000 or > 10000) Add("priority_invalid", path + ".priority", "Priority must be -10000..10000.");
                if (route.Event == "chat_received" && route.ActorRole is not ("peer" or "listen_host")) Add("chat_actor_role_required", path + ".actor_role", "Chat requires peer or listen_host.");
                if (route.Event == "piece_placed" && route.ActorRole != "listen_host") Add("piece_placed_listen_host_required", path + ".actor_role", "Placement is currently a listen-host event.");
                if (route.Event is "kill" or "piece_damaged" or "sign_written" or "timer_elapsed" && !string.IsNullOrWhiteSpace(route.ActorRole)) Add("actor_role_not_supported", path + ".actor_role", "This event does not accept an actor role.");
                if (route.Event == "timer_elapsed" && !SafeId(route.TimerId)) Add("timer_id_required", path + ".timer_id", "Timer events require a stable timer ID.");
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
                var where = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(route.ActorRole)) where["actor_role"] = route.ActorRole;
                if (!string.IsNullOrWhiteSpace(route.TimerId)) where["timer_id"] = route.TimerId;
                var eventTrigger = new TriggerExpression { Op = "EVENT", Event = route.Event, Target = NullIfWhite(route.Target), Where = where.Count == 0 ? null : where };
                var trigger = route.RepeatCount > 1
                    ? new TriggerExpression { Op = "COUNT", Count = route.RepeatCount, WithinSeconds = route.WithinSeconds, Children = new() { eventTrigger } }
                    : eventTrigger;
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
        var contract = ExperienceCompiler.CompileJson(json, CanonicalEventCatalog.CreateSet());
        diagnostics.AddRange(contract.Diagnostics);
        if (diagnostics.Count > 0) return StudioCertificationResult.Fail("graph_invalid", diagnostics);
        var hash = QuestPackContent.ComputeHash(new[] { new KeyValuePair<string, byte[]>($"experiences/{project.ExperienceId}.json", Encoding.UTF8.GetBytes(json)) });
        return StudioCertificationResult.Success(json, hash, document);

        void Add(string code, string path, string message) => diagnostics.Add(new(code, path, message));
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

    static void Write(ZipArchive archive, string name, string content) { using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)); writer.Write(content); }
    static bool SafeId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '$');
    static string? NullIfWhite(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

internal static class StudioRehearsal
{
    public static StudioRehearsalResult Run(StudioProjectDocument project, ExperienceDocument document, StudioRehearsalRequest request)
    {
        var now = DateTimeOffset.UnixEpoch;
        var stageId = document.EntryStage;
        string? outcome = null;
        var history = new List<RuntimeEvent>();
        var timers = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var inventory = new Dictionary<string, int>(StringComparer.Ordinal);
        var spawns = new Dictionary<string, int>(StringComparer.Ordinal);
        var transcript = new List<string>();
        var trace = new List<StudioRehearsalTrace>();
        foreach (var input in request.Steps ?? new())
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
            var fields = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(input.ActorRole)) fields["actor_role"] = input.ActorRole;
            if (!string.IsNullOrWhiteSpace(input.TimerId)) fields["timer_id"] = input.TimerId;
            Emit(new RuntimeEvent { Name = input.EventName, Target = input.Target, At = now, Fields = fields });
        }
        var proofLevel = request.ScenarioId == "captured-1.6" ? "captured_contract_fixture" : "rehearsal";
        return new(2, true, null, proofLevel, "Browser rehearsal only; this does not prove a Valheim adapter or live mutation.", stageId, outcome, trace, transcript, inventory, spawns, timers.ToDictionary(pair => pair.Key, pair => (int)Math.Max(0, (pair.Value - now).TotalSeconds)));

        void Emit(RuntimeEvent evt)
        {
            if (string.IsNullOrWhiteSpace(evt.Name) || outcome is not null) return;
            var stage = document.Stages.FirstOrDefault(value => value.Id == stageId);
            if (stage is null) return;
            history.Add(evt);
            var ordered = (stage.Transitions ?? new()).OrderByDescending(value => value.Priority).ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
            var transition = ordered.FirstOrDefault(value => TriggerEvaluator.Matches(value.When, history));
            if (transition is null)
            {
                var candidate = ordered.FirstOrDefault();
                var partial = TriggerEvaluator.Measure(candidate?.When, history);
                trace.Add(new(trace.Count + 1, evt.Name, evt.Target, stageId, null, Array.Empty<string>(), stageId, null,
                    "ignored", partial.Current, partial.Required, Describe(candidate?.When)));
                return;
            }
            var progress = TriggerEvaluator.Measure(transition.When, history);
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
                    case "clear_spawned": var removed = spawns.GetValueOrDefault(P("action_id")); spawns.Remove(P("action_id")); effects.Add($"clear {removed} from {P("action_id")}"); break;
                }
            }
            var from = stageId;
            if (!string.IsNullOrWhiteSpace(transition.NextStage)) stageId = transition.NextStage;
            outcome = string.IsNullOrWhiteSpace(transition.Outcome) ? null : transition.Outcome;
            history.Clear();
            trace.Add(new(trace.Count + 1, evt.Name, evt.Target, from, transition.Id, effects, stageId, outcome,
                "matched", progress.Current, progress.Required, outcome is null ? Describe(document.Stages.FirstOrDefault(value => value.Id == stageId)?.Transitions?.OrderByDescending(value => value.Priority).FirstOrDefault()?.When) : null));
        }
    }

    static string? Describe(TriggerExpression? trigger)
    {
        var leaf = string.Equals(trigger?.Op, "COUNT", StringComparison.OrdinalIgnoreCase)
            ? trigger?.Children?.FirstOrDefault()
            : trigger;
        return leaf is not null && CreatorSignalCatalog.TryDescribe(leaf.Event, leaf.Target, out var signal)
            ? signal.Instruction
            : null;
    }
}

public sealed class StudioProjectDocument
{
    public int SchemaVersion { get; set; } = 2;
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
    public string? ActorRole { get; set; }
    public string? TimerId { get; set; }
    public int RepeatCount { get; set; } = 1;
    public int? WithinSeconds { get; set; }
    public string? DestinationNodeId { get; set; }
    public string? Outcome { get; set; }
    public List<StudioAction> Actions { get; set; } = new();
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

public sealed record StudioPublishResult(bool Ok, string Status, string? Error, QuestPackPublishReceipt? Receipt, IReadOnlyList<ContractDiagnostic> Diagnostics)
{
    public static StudioPublishResult Fail(string error, IReadOnlyList<ContractDiagnostic>? diagnostics = null, QuestPackPublishReceipt? receipt = null) => new(false, "rejected", error, receipt, diagnostics ?? Array.Empty<ContractDiagnostic>());
}

public sealed class StudioRehearsalRequest
{
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
    public int Seconds { get; set; }
}

public sealed record StudioRehearsalTrace(int Step, string EventName, string? Target, string FromNodeId, string? RouteId, IReadOnlyList<string> Effects, string CurrentNodeId, string? Outcome, string Status, int CurrentCount, int RequiredCount, string? NextInstruction);
public sealed record StudioRehearsalResult(int SchemaVersion, bool Ok, string? Error, string ProofLevel, string Disclaimer, string? CurrentNodeId, string? Outcome, IReadOnlyList<StudioRehearsalTrace> Trace, IReadOnlyList<string> Transcript, IReadOnlyDictionary<string,int> Inventory, IReadOnlyDictionary<string,int> Spawns, IReadOnlyDictionary<string,int> Timers)
{
    public static StudioRehearsalResult Fail(string error, IReadOnlyList<ContractDiagnostic>? diagnostics = null) => new(2, false, error, "rehearsal", "Browser rehearsal only; this does not prove a Valheim adapter or live mutation.", null, null, Array.Empty<StudioRehearsalTrace>(), Array.Empty<string>(), new Dictionary<string,int>(), new Dictionary<string,int>(), new Dictionary<string,int>());
}

public sealed record StudioRuntimeStatus(int SchemaVersion, bool Available, string Phase, string NextInstruction,
    string? ContentHash, string? PackageSha256, ComfyQuestContracts.ActiveSet? ActiveSet,
    string? CurrentStageId, int? CurrentCount, int? RequiredCount,
    IReadOnlyList<RuntimeReceipt> Receipts, IReadOnlyList<ContractDiagnostic> Diagnostics)
{
    public static StudioRuntimeStatus Unavailable(string phase, IReadOnlyList<ContractDiagnostic>? diagnostics = null) =>
        new(2, false, phase,
            phase == "draft_invalid" ? "Resolve the graph diagnostics before publishing." : "Runtime state is unavailable on this machine.",
            null, null, null, null, null, null, Array.Empty<RuntimeReceipt>(), diagnostics ?? Array.Empty<ContractDiagnostic>());
}

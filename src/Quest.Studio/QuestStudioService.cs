using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ComfyQuestContracts;
using Newtonsoft.Json;

namespace Comfy.Quest.Studio;

public sealed class QuestStudioService
{
    static readonly string[] RuntimeEvents = CreatorSignalCatalog.All
        .Where(signal => signal.EventName != ExperienceSchema.TimerElapsedEvent)
        .Select(signal => signal.EventName)
        .Concat(new[] { "chat_received", "kill", "piece_damaged", "piece_placed", "sign_written" })
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    readonly object _lock = new();
    readonly string _projectPath;
    readonly string _historyPath;
    readonly QuestPackPublisher _publisher;
    readonly IQuestStudioHost _host;
    readonly QuestStudioWorkspace _workspace;

    public QuestStudioService(IQuestStudioHost host, QuestPackPublisher publisher)
    {
        var root = Path.Combine(host.StateDirectory, "quest-studio");
        Directory.CreateDirectory(root);
        _projectPath = Path.Combine(root, "project.json");
        _historyPath = Path.Combine(root, "history");
        _publisher = publisher;
        _host = host;
        _workspace = new QuestStudioWorkspace(host, publisher);
    }

    public object WorkspaceCatalog() => _workspace.Catalog();
    public IReadOnlyList<StudioProjectSummary> ListProjects() => _workspace.ListProjects();
    public StudioProjectDocument? ReadProject(string projectId) => _workspace.ReadProject(projectId);
    public StudioProjectDocument CreateProject(string? templateId) => _workspace.CreateProject(templateId);
    public StudioProjectDocument? DuplicateProject(string projectId) => _workspace.Duplicate(projectId);
    public StudioSaveResult SaveDraft(string projectId, StudioSaveRequest? request) => _workspace.SaveDraft(projectId, request);
    public StudioSaveResult BumpPatch(string projectId, int expectedRevision) => _workspace.BumpPatch(projectId, expectedRevision);
    public StudioCertificationResult ValidateGraph(string projectId) => _workspace.Validate(projectId);
    public StudioCertificationResult CertifyGraph(string projectId) => _workspace.Certify(projectId);
    public Task<StudioPublishResult> PublishGraphAsync(string projectId, CancellationToken cancellationToken) => _workspace.PublishAsync(projectId, cancellationToken);
    public StudioRehearsalResult Rehearse(string projectId, StudioRehearsalRequest? request) => _workspace.Rehearse(projectId, request);
    public StudioRuntimeStatus RuntimeStatus(string projectId) => _workspace.RuntimeStatus(projectId);
    public object ProjectHistory(string projectId) => _workspace.History(projectId);

    public QuestStudioProject Read()
    {
        lock (_lock)
        {
            if (!File.Exists(_projectPath)) return QuestStudioProject.Starter();
            try { return System.Text.Json.JsonSerializer.Deserialize<QuestStudioProject>(File.ReadAllText(_projectPath), _host.Json) ?? QuestStudioProject.Starter(); }
            catch { return QuestStudioProject.Starter() with { LastError = "project_state_unreadable" }; }
        }
    }

    public object Events() => new
    {
        schema_version = 2,
        events = RuntimeEvents,
        event_definitions = new object[]
        {
            new { id = "chat_received", actor_roles = new[] { "peer", "listen_host" }, note = "Listen-host observed chat; message text is never persisted." },
            new { id = "piece_placed", actor_roles = new[] { "listen_host" }, note = "A piece placed locally by the authoritative listen host." },
            new { id = "kill", actor_roles = Array.Empty<string>(), note = "A local-player-caused creature kill." },
            new { id = "piece_damaged", actor_roles = Array.Empty<string>(), note = "Damage to the bound player-built piece by the local player." },
            new { id = "sign_written", actor_roles = Array.Empty<string>(), note = "A local sign text update; Studio never captures the text." }
        }
    };

    public object Receipts()
    {
        var valheim = _host.FindValheim();
        if (valheim is null) return new { schema_version = 1, available = false, receipts = Array.Empty<JsonElement>() };
        var root = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        var store = new RuntimeReceiptStore(root);
        var values = new List<JsonElement>();
        foreach (var path in store.List(50))
        {
            try { if (new FileInfo(path).Length <= 128 * 1024) values.Add(JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone()); }
            catch { /* a partial or malformed receipt is ignored, never trusted as runtime evidence */ }
        }
        return new { schema_version = 1, available = true, receipts = values };
    }

    public QuestStudioResult Save(QuestStudioProject? project)
    {
        var validation = ValidateProject(project);
        if (validation is not null) return QuestStudioResult.Fail(validation);
        lock (_lock)
        {
            var temporary = _projectPath + ".tmp";
            File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(project, _host.Json));
            File.Move(temporary, _projectPath, true);
        }
        var certified = Certify(project!);
        if (certified.Ok) StoreSnapshot(project!, certified.ContentHash!);
        return certified with { Status = "saved" };
    }

    public object History()
    {
        lock (_lock)
        {
            if (!Directory.Exists(_historyPath)) return new { schema_version = 1, versions = Array.Empty<QuestStudioSnapshot>() };
            var snapshots = Directory.GetFiles(_historyPath, "*.json").Select(ReadSnapshot).Where(x => x is not null)
                .Cast<QuestStudioSnapshot>().OrderByDescending(x => x.SavedUtc).Take(100).ToArray();
            return new { schema_version = 1, versions = snapshots };
        }
    }

    public QuestStudioDiff Diff(string? from, string? to)
    {
        if (!SafeHash(from) || !SafeHash(to)) return QuestStudioDiff.Fail("history_hash_invalid");
        lock (_lock)
        {
            var left = ReadSnapshot(Path.Combine(_historyPath, from + ".json"));
            var right = ReadSnapshot(Path.Combine(_historyPath, to + ".json"));
            if (left is null || right is null) return QuestStudioDiff.Fail("history_version_missing");
            var changes = new List<QuestStudioFieldChange>();
            Add("pack_id", left.Project.PackId, right.Project.PackId, changes);
            Add("version", left.Project.Version, right.Project.Version, changes);
            Add("experience_id", left.Project.ExperienceId, right.Project.ExperienceId, changes);
            Add("title", left.Project.Title, right.Project.Title, changes);
            Add("event", left.Project.Event, right.Project.Event, changes);
            Add("target", left.Project.Target, right.Project.Target, changes);
            Add("message", left.Project.Message, right.Project.Message, changes);
            Add("binding_target_kind", left.Project.BindingTargetKind, right.Project.BindingTargetKind, changes);
            Add("stages", StageFingerprint(left.Project), StageFingerprint(right.Project), changes);
            return new(true, null, left, right, changes);
        }
    }

    public QuestStudioResult Certify(QuestStudioProject? project)
    {
        var validation = ValidateProject(project);
        if (validation is not null) return QuestStudioResult.Fail(validation);
        var json = BuildExperienceJson(project!);
        var compiled = ExperienceCompiler.CompileJson(json, CanonicalEventCatalog.CreateSet());
        return compiled.IsValid
            ? QuestStudioResult.Success("certified", json, QuestPackContent.ComputeHash(new[] { new KeyValuePair<string, byte[]>($"experiences/{project!.ExperienceId}.json", Encoding.UTF8.GetBytes(json)) }))
            : QuestStudioResult.Fail("experience_invalid", compiled.Diagnostics);
    }

    public async Task<QuestStudioPublishResult> PublishAsync(QuestStudioProject? project, CancellationToken cancellationToken)
    {
        var certified = Certify(project);
        if (!certified.Ok) return QuestStudioPublishResult.Fail(certified.Error!, certified.Diagnostics);
        var saved = Save(project);
        if (!saved.Ok) return QuestStudioPublishResult.Fail(saved.Error!, saved.Diagnostics);
        var bytes = BuildPack(project!, certified.ExperienceJson!, certified.ContentHash!);
        await using var stream = new MemoryStream(bytes, writable: false);
        var filename = $"{project!.PackId}-{project.Version}.questpack";
        var receipt = await _publisher.PublishAsync(stream, filename, cancellationToken);
        return new(receipt.Ok, receipt.Ok ? "published" : "rejected", receipt.Error, receipt, certified.Diagnostics);
    }

    static string? ValidateProject(QuestStudioProject? p)
    {
        if (p is null) return "project_required";
        if (!SafeId(p.PackId) || !SafeId(p.ExperienceId)) return "stable_id_invalid";
        if (!SemanticVersion.TryParse(p.Version, out _)) return "version_invalid";
        if (p.Title?.Length is < 1 or > 120) return "field_bounds_invalid";
        if (p.BindingTargetKind is not null && p.BindingTargetKind is not ("sign" or "player_built_piece" or "item_stand" or "dedicated_charm")) return "binding_target_kind_invalid";
        var stages = EffectiveStages(p);
        if (stages.Count is < 1 or > ExperienceSchema.MaxStages) return "stage_count_invalid";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stage in stages)
        {
            if (!SafeId(stage.Id) || !ids.Add(stage.Id) || stage.Id.StartsWith("transition-", StringComparison.Ordinal) || stage.Id.StartsWith("message-", StringComparison.Ordinal) || stage.Id == "default") return "stage_id_invalid";
            if (!KnownStudioEvent(stage.Event)) return "event_unknown";
            if (stage.Target?.Length > 120 || stage.Message?.Length is < 1 or > 500) return "field_bounds_invalid";
            if (stage.ActorRole is not null && stage.ActorRole is not (CooperativeEventContract.PeerRole or CooperativeEventContract.ListenHostRole)) return "actor_role_invalid";
            if (stage.Event == ExperienceSchema.ChatReceivedEvent && stage.ActorRole is null) return "chat_actor_role_required";
            if (stage.Event == "piece_placed" && stage.ActorRole != CooperativeEventContract.ListenHostRole) return "piece_placed_listen_host_required";
            if (stage.Event is "kill" or "piece_damaged" or "sign_written" && stage.ActorRole is not null) return "actor_role_not_supported";
        }
        return null;
    }

    static bool SafeId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
    static bool SafeHash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    static bool KnownStudioEvent(string? value) => !string.IsNullOrWhiteSpace(value) && RuntimeEvents.Contains(value, StringComparer.Ordinal);

    void StoreSnapshot(QuestStudioProject project, string contentHash)
    {
        Directory.CreateDirectory(_historyPath);
        var target = Path.Combine(_historyPath, contentHash + ".json");
        if (File.Exists(target)) return;
        var temporary = target + ".tmp";
        var snapshot = new QuestStudioSnapshot(1, contentHash, DateTimeOffset.UtcNow, project with { LastError = null });
        File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(snapshot, _host.Json));
        try { File.Move(temporary, target); } catch (IOException) when (File.Exists(target)) { File.Delete(temporary); }
    }

    QuestStudioSnapshot? ReadSnapshot(string path)
    {
        try { return new FileInfo(path).Length <= 1024 * 1024 ? System.Text.Json.JsonSerializer.Deserialize<QuestStudioSnapshot>(File.ReadAllText(path), _host.Json) : null; }
        catch { return null; }
    }
    static void Add(string field, string? from, string? to, List<QuestStudioFieldChange> changes) { if (!string.Equals(from, to, StringComparison.Ordinal)) changes.Add(new(field, from, to)); }

    static string BuildExperienceJson(QuestStudioProject p)
    {
        var authored = EffectiveStages(p);
        var stages = new List<ExperienceStage>();
        for (var index = 0; index < authored.Count; index++)
        {
            var stage = authored[index];
            var where = string.IsNullOrWhiteSpace(stage.ActorRole)
                ? null
                : new Dictionary<string, string> { ["actor_role"] = stage.ActorRole };
            var trigger = new TriggerExpression { Op = "EVENT", Event = stage.Event, Target = string.IsNullOrWhiteSpace(stage.Target) ? null : stage.Target, Where = where };
            var action = new ExperienceAction { Id = $"message-{index + 1:00}", Type = "message", Parameters = new Dictionary<string, Newtonsoft.Json.Linq.JToken> { ["text"] = stage.Message } };
            stages.Add(new ExperienceStage
            {
                Id = stage.Id,
                EntryActions = new(),
                Transitions = new()
                {
                    new ExperienceTransition
                    {
                        Id = $"transition-{index + 1:00}",
                        Priority = 100,
                        When = trigger,
                        Actions = new() { action },
                        NextStage = index + 1 < authored.Count ? authored[index + 1].Id : null,
                        Outcome = index + 1 == authored.Count ? "complete" : null
                    }
                }
            });
        }
        var document = new ExperienceDocument {
            Schema = ExperienceSchema.Id, Id = p.ExperienceId, Title = p.Title, EntryStage = authored[0].Id,
            Stages = stages,
            Bindings = new() { new ExperienceBinding { Id = "default", ExperienceId = p.ExperienceId, TargetKinds = string.IsNullOrWhiteSpace(p.BindingTargetKind) ? null : new() { p.BindingTargetKind } } }
        };
        return JsonConvert.SerializeObject(document, Formatting.Indented);
    }

    static IReadOnlyList<QuestStudioStage> EffectiveStages(QuestStudioProject p) => p.Stages is { Count: > 0 }
        ? p.Stages
        : new[] { new QuestStudioStage("start", p.Event, p.Target, null, p.Message) };

    static string StageFingerprint(QuestStudioProject p) => System.Text.Json.JsonSerializer.Serialize(EffectiveStages(p));

    static byte[] BuildPack(QuestStudioProject p, string experienceJson, string contentHash)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Write(archive, "manifest.json", JsonConvert.SerializeObject(new QuestPackManifest { PackId = p.PackId, Version = p.Version, ContentHash = contentHash }, Formatting.Indented));
            Write(archive, $"experiences/{p.ExperienceId}.json", experienceJson);
        }
        return output.ToArray();
    }

    static void Write(ZipArchive archive, string name, string content) { using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)); writer.Write(content); }
}

public sealed record QuestStudioProject(
    string PackId,
    string Version,
    string ExperienceId,
    string Title,
    string Event,
    string? Target,
    string Message,
    string? LastError = null,
    string? BindingTargetKind = null,
    IReadOnlyList<QuestStudioStage>? Stages = null)
{
    public static QuestStudioProject Starter() => new(
        "studio-two-voices-one-rune",
        "1.0.0",
        "two-voices-one-rune",
        "Two Voices, One Rune",
        ExperienceSchema.ChatReceivedEvent,
        "shout",
        "A distant voice wakes the Charm.",
        BindingTargetKind: "sign",
        Stages: new[]
        {
            new QuestStudioStage("await-peer-shout", ExperienceSchema.ChatReceivedEvent, "shout", CooperativeEventContract.PeerRole, "A distant voice wakes the Charm."),
            new QuestStudioStage("await-host-sign", "piece_placed", "sign", CooperativeEventContract.ListenHostRole, "The host raises the answering rune. The ritual is complete.")
        });
}
public sealed record QuestStudioStage(string Id, string Event, string? Target, string? ActorRole, string Message);
public sealed record QuestStudioResult(bool Ok, string Status, string? Error, string? ExperienceJson, string? ContentHash, IReadOnlyList<ContractDiagnostic> Diagnostics)
{
    public static QuestStudioResult Success(string status, string json, string hash) => new(true, status, null, json, hash, Array.Empty<ContractDiagnostic>());
    public static QuestStudioResult Fail(string error, IReadOnlyList<ContractDiagnostic>? diagnostics = null) => new(false, "rejected", error, null, null, diagnostics ?? Array.Empty<ContractDiagnostic>());
}
public sealed record QuestStudioPublishResult(bool Ok, string Status, string? Error, QuestPackPublishReceipt? Receipt, IReadOnlyList<ContractDiagnostic> Diagnostics)
{
    public static QuestStudioPublishResult Fail(string error, IReadOnlyList<ContractDiagnostic>? diagnostics = null) => new(false, "rejected", error, null, diagnostics ?? Array.Empty<ContractDiagnostic>());
}
public sealed record QuestStudioSnapshot(int SchemaVersion, string ContentHash, DateTimeOffset SavedUtc, QuestStudioProject Project);
public sealed record QuestStudioFieldChange(string Field, string? From, string? To);
public sealed record QuestStudioDiff(bool Ok, string? Error, QuestStudioSnapshot? From, QuestStudioSnapshot? To, IReadOnlyList<QuestStudioFieldChange> Changes)
{
    public static QuestStudioDiff Fail(string error) => new(false, error, null, null, Array.Empty<QuestStudioFieldChange>());
}

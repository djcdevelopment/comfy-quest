using System.Globalization;
using System.Text;
using System.Text.Json;
using ComfyQuestContracts;

namespace Comfy.Quest.Studio;

/// <summary>
/// Local-only, privacy-minimal aggregate usage observations. This type has no transport
/// dependency and accepts only fixed semantic categories; arbitrary browser telemetry,
/// project identity, targets, and authored text cannot enter the store.
/// </summary>
internal sealed class QuestStudioUsageInsights
{
    const int RetentionWeeks = 13;
    static readonly HashSet<string> Templates = new(StringComparer.Ordinal)
        { "blank", "demo-world-first-portal", "signal-circuit", "cooperative-ritual", "reward-cleanup", "desperate-defense", "duplicate" };
    static readonly HashSet<string> Events = new(
        CreatorEventCatalog.All.Select(value => value.Name)
            .Concat(RuntimeProductionEventCatalog.EngineEvents.Select(value => value.Name)),
        StringComparer.Ordinal);
    static readonly HashSet<string> Actions = new(StringComparer.Ordinal)
        { "message", "timer_start", "timer_cancel", "grant_item", "spawn", "clear_spawned" };
    static readonly HashSet<string> GrantItems = new(StringComparer.Ordinal) { "Wood", "Stone", "Resin", "Coins" };
    static readonly HashSet<string> SpawnKinds = new(StringComparer.Ordinal) { "creature", "item", "piece" };
    static readonly HashSet<string> SpawnPrefabs = new(StringComparer.Ordinal) { "Greyling", "Boar", "Wood", "Stone", "Resin", "sign", "wood_floor" };
    static readonly HashSet<string> BindingTargets = new(StringComparer.Ordinal) { "sign", "player_built_piece", "item_stand", "dedicated_charm" };
    static readonly HashSet<string> Operations = new(StringComparer.Ordinal)
        { "create", "import", "duplicate", "save", "bump_patch", "validate", "certify", "rehearse", "publish", "bundle_export", "questpack_download" };
    static readonly HashSet<string> Outcomes = new(StringComparer.Ordinal)
        { "accepted", "rejected", "conflict", "missing" };

    readonly object _gate = new();
    readonly string _root;
    readonly string _settingsPath;
    readonly string _aggregatePath;
    readonly JsonSerializerOptions _json;
    readonly Func<DateTimeOffset> _clock;
    UsageSettings _settings;
    UsageAggregate _aggregate;
    bool _settingsAvailable = true;
    bool _aggregateAvailable = true;
    bool Available => _settingsAvailable && _aggregateAvailable;

    public QuestStudioUsageInsights(IQuestStudioHost host, Func<DateTimeOffset>? clock = null)
    {
        _root = Path.Combine(host.StateDirectory, "quest-studio", "usage");
        _settingsPath = Path.Combine(_root, "settings.json");
        _aggregatePath = Path.Combine(_root, "aggregate.json");
        _json = host.Json;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        var settings = Read<UsageSettings>(_settingsPath);
        var aggregate = Read<UsageAggregate>(_aggregatePath);
        _settingsAvailable = !File.Exists(_settingsPath) || settings?.SchemaVersion == 1;
        _aggregateAvailable = !File.Exists(_aggregatePath) || aggregate?.SchemaVersion == 1;
        _settings = settings ?? new();
        _aggregate = aggregate ?? new();
        var pruned = Normalize();
        if (pruned && _aggregateAvailable) _aggregateAvailable = TryWrite(_aggregatePath, _aggregate);
    }

    public StudioUsageReport Report()
    {
        lock (_gate)
        {
            var pruned = Normalize();
            if (pruned && _aggregateAvailable) _aggregateAvailable = TryWrite(_aggregatePath, _aggregate);
            return Snapshot();
        }
    }

    public StudioUsageReport SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            // A newer schema is not corruption: an older Studio must not overwrite it.
            // A parse failure falls back to the current schema and can be repaired by this
            // explicit user control.
            if (_settings.SchemaVersion != 1) return Snapshot();
            var previous = _settings.Enabled;
            _settings.Enabled = enabled;
            _settingsAvailable = TryWrite(_settingsPath, _settings);
            if (!_settingsAvailable) _settings.Enabled = previous;
            Normalize();
            return Snapshot();
        }
    }

    public StudioUsageResetResult Reset(bool confirmed)
    {
        lock (_gate)
        {
            if (!confirmed) return new(false, "reset_confirmation_required", Snapshot());
            var previous = _aggregate;
            _aggregate = new();
            _aggregateAvailable = TryWrite(_aggregatePath, _aggregate);
            if (!_aggregateAvailable) _aggregate = previous;
            return new(_aggregateAvailable, _aggregateAvailable ? null : "usage_storage_unavailable", Snapshot());
        }
    }

    public StudioDownloadResult Export()
    {
        var report = Report();
        if (!report.Available) return StudioDownloadResult.Fail("usage_storage_unavailable");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, _json);
        return StudioDownloadResult.Success("quest-studio-usage.json", "application/json", bytes);
    }

    public void RecordProject(string operation, string outcome, StudioProjectDocument? project, string? templateId = null)
    {
        if (!Operations.Contains(operation) || !Outcomes.Contains(outcome)) return;
        Mutate(week =>
        {
            Increment(week.Outcomes, operation + "." + outcome);
            if (operation == "create" && Templates.Contains(templateId ?? string.Empty))
                Increment(week.Templates, templateId!);
            if (operation == "duplicate") Increment(week.Templates, "duplicate");
            if (project is null || outcome != "accepted") return;
            AddProjectShape(week, project, includeSelections: operation == "create");
        });
    }

    public void RecordOutcome(string operation, string outcome) => RecordProject(operation, outcome, null);

    public void RecordSave(string outcome, StudioProjectDocument? before, StudioProjectDocument? after)
    {
        if (!Outcomes.Contains(outcome)) return;
        Mutate(week =>
        {
            Increment(week.Outcomes, "save." + outcome);
            if (outcome != "accepted" || after is null) return;
            AddProjectShape(week, after, includeSelections: false);
            var prior = ChoiceCounts(before);
            foreach (var pair in ChoiceCounts(after))
            {
                var added = pair.Value - prior.GetValueOrDefault(pair.Key);
                if (added <= 0) continue;
                var split = pair.Key.IndexOf(':');
                var kind = pair.Key[..split];
                var value = pair.Key[(split + 1)..];
                if (kind == "event") Increment(week.Events, value, added);
                else if (kind == "action") Increment(week.Actions, value, added);
                else if (kind == "selection") Increment(week.Selections, value, added);
            }
        });
    }

    public void RecordCheckpoint(string operation, string outcome, StudioProjectDocument? project)
    {
        if (!Operations.Contains(operation) || !Outcomes.Contains(outcome)) return;
        Mutate(week =>
        {
            Increment(week.Outcomes, operation + "." + outcome);
            if (outcome == "accepted" && project is not null) AddProjectShape(week, project, includeSelections: false);
        });
    }

    void AddProjectShape(UsageWeek week, StudioProjectDocument project, bool includeSelections = true)
    {
        var nodes = project.Nodes ?? new();
        Increment(week.Distributions, "node_count." + BucketCount(nodes.Count));
        var routes = nodes.SelectMany(node => node.Routes ?? new()).ToArray();
        Increment(week.Distributions, "route_count." + BucketCount(routes.Length));
        foreach (var route in routes)
        {
            if (includeSelections && Events.Contains(route.Event)) Increment(week.Events, route.Event);
            Increment(week.Distributions, "repeat_count." + BucketRepeat(route.RepeatCount));
            Increment(week.Distributions, "window_seconds." + BucketDuration(route.WithinSeconds));
            var fieldCount = route.Where?.Count ?? 0;
            if (!(route.Where?.ContainsKey("actor_role") ?? false) && !string.IsNullOrWhiteSpace(route.ActorRole)) fieldCount++;
            if (!(route.Where?.ContainsKey("timer_id") ?? false) && !string.IsNullOrWhiteSpace(route.TimerId)) fieldCount++;
            Increment(week.Distributions, "field_count." + BucketFields(fieldCount));
            foreach (var action in route.Actions ?? new())
            {
                if (!Actions.Contains(action.Type)) continue;
                if (includeSelections) Increment(week.Actions, action.Type);
                if (includeSelections) AddClosedSelections(week.Selections, action);
                if (action.Type == "grant_item") Increment(week.Distributions, "quantity." + BucketQuantity(action.Quantity));
                if (action.Type == "spawn")
                {
                    Increment(week.Distributions, "quantity." + BucketQuantity(action.Count));
                    Increment(week.Distributions, "radius." + BucketRadius(action.Radius));
                }
            }
        }
        if (includeSelections)
        {
            if (BindingTargets.Contains(project.BindingTargetKind ?? string.Empty))
                Increment(week.Selections, "binding_target." + project.BindingTargetKind);
            foreach (var target in (project.BindingTargetKinds ?? new()).Where(BindingTargets.Contains).Distinct(StringComparer.Ordinal))
                Increment(week.Selections, "binding_target." + target);
        }
    }

    static Dictionary<string, int> ChoiceCounts(StudioProjectDocument? project)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        if (project is null) return values;
        void Add(string kind, string value)
        {
            var key = kind + ":" + value;
            values[key] = values.GetValueOrDefault(key) + 1;
        }
        foreach (var route in (project.Nodes ?? new()).SelectMany(node => node.Routes ?? new()))
        {
            if (Events.Contains(route.Event)) Add("event", route.Event);
            foreach (var action in route.Actions ?? new())
            {
                if (!Actions.Contains(action.Type)) continue;
                Add("action", action.Type);
                if (action.Type == "grant_item" && GrantItems.Contains(action.Item ?? string.Empty)) Add("selection", "grant_item." + action.Item);
                if (action.Type == "spawn")
                {
                    if (SpawnKinds.Contains(action.Kind ?? string.Empty)) Add("selection", "spawn_kind." + action.Kind);
                    if (SpawnPrefabs.Contains(action.Prefab ?? string.Empty)) Add("selection", "spawn_prefab." + action.Prefab);
                }
            }
        }
        if (BindingTargets.Contains(project.BindingTargetKind ?? string.Empty)) Add("selection", "binding_target." + project.BindingTargetKind);
        foreach (var target in (project.BindingTargetKinds ?? new()).Where(BindingTargets.Contains).Distinct(StringComparer.Ordinal))
            Add("selection", "binding_target." + target);
        return values;
    }

    static void AddClosedSelections(Dictionary<string, long> values, StudioAction action)
    {
        if (action.Type == "grant_item" && GrantItems.Contains(action.Item ?? string.Empty)) Increment(values, "grant_item." + action.Item);
        if (action.Type != "spawn") return;
        if (SpawnKinds.Contains(action.Kind ?? string.Empty)) Increment(values, "spawn_kind." + action.Kind);
        if (SpawnPrefabs.Contains(action.Prefab ?? string.Empty)) Increment(values, "spawn_prefab." + action.Prefab);
    }

    void Mutate(Action<UsageWeek> mutation)
    {
        lock (_gate)
        {
            if (!_settings.Enabled || !Available) return;
            try
            {
                Normalize();
                var key = WeekKey(_clock());
                if (!_aggregate.Weeks.TryGetValue(key, out var week))
                    _aggregate.Weeks[key] = week = new();
                mutation(week);
                Prune();
                _aggregateAvailable = TryWrite(_aggregatePath, _aggregate);
            }
            catch { /* insights must never interrupt creator work */ }
        }
    }

    bool Normalize()
    {
        var changed = _aggregate.Weeks is null;
        var keysBefore = (_aggregate.Weeks ?? new()).Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        _aggregate.Weeks ??= new(StringComparer.Ordinal);
        foreach (var week in _aggregate.Weeks.Values)
        {
            changed |= week.Outcomes is null || week.Templates is null || week.Events is null || week.Actions is null
                || week.Selections is null || week.Distributions is null;
            week.Outcomes ??= new(StringComparer.Ordinal);
            week.Templates ??= new(StringComparer.Ordinal);
            week.Events ??= new(StringComparer.Ordinal);
            week.Actions ??= new(StringComparer.Ordinal);
            week.Selections ??= new(StringComparer.Ordinal);
            week.Distributions ??= new(StringComparer.Ordinal);
            changed |= Filter(week.Outcomes, value => IsDottedPair(value, Operations, Outcomes));
            changed |= Filter(week.Templates, Templates.Contains);
            changed |= Filter(week.Events, Events.Contains);
            changed |= Filter(week.Actions, Actions.Contains);
            changed |= Filter(week.Selections, IsSelectionKey);
            changed |= Filter(week.Distributions, IsDistributionKey);
        }
        Prune();
        return changed || !keysBefore.SequenceEqual(_aggregate.Weeks.Keys.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    void Prune()
    {
        var currentMonday = WeekMonday(_clock());
        foreach (var key in _aggregate.Weeks.Keys.ToArray())
        {
            if (!TryWeekMonday(key, out var monday) || monday < currentMonday.AddDays(-7 * (RetentionWeeks - 1)) || monday > currentMonday)
                _aggregate.Weeks.Remove(key);
        }
    }

    StudioUsageReport Snapshot()
    {
        var weeks = _aggregate.Weeks.OrderBy(value => value.Key, StringComparer.Ordinal)
            .ToDictionary(value => value.Key, value => new StudioUsageWeek(
                Copy(value.Value.Outcomes), Copy(value.Value.Templates), Copy(value.Value.Events),
                Copy(value.Value.Actions), Copy(value.Value.Selections), Copy(value.Value.Distributions)), StringComparer.Ordinal);
        return new(1, Available, _settingsAvailable, _aggregateAvailable, _settings.Enabled, "local_only", RetentionWeeks,
            "No authored text, targets, project/player/world identity, paths, exact timestamps, or exact numeric selections are collected.",
            weeks);
    }

    static IReadOnlyDictionary<string, long> Copy(Dictionary<string, long> source) =>
        source.OrderBy(value => value.Key, StringComparer.Ordinal).ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
    static void Increment(Dictionary<string, long> values, string key, int amount = 1)
    {
        var current = values.GetValueOrDefault(key);
        values[key] = current >= long.MaxValue - amount ? long.MaxValue : current + amount;
    }
    static bool Filter(Dictionary<string, long> values, Func<string, bool> predicate)
    {
        var rejected = values.Keys.Where(key => !predicate(key) || values[key] < 0).ToArray();
        foreach (var key in rejected) values.Remove(key);
        return rejected.Length > 0;
    }

    T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), _json);
        }
        catch { return null; }
    }

    bool TryWrite<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(_root);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(value, _json), new UTF8Encoding(false));
                File.Move(temporary, path, true);
                return true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch { return false; /* insights persistence is deliberately fail-soft */ }
    }

    static string WeekKey(DateTimeOffset value)
    {
        var date = value.UtcDateTime.Date;
        return $"{ISOWeek.GetYear(date):0000}-W{ISOWeek.GetWeekOfYear(date):00}";
    }
    static DateTime WeekMonday(DateTimeOffset value)
    {
        var date = value.UtcDateTime.Date;
        return ISOWeek.ToDateTime(ISOWeek.GetYear(date), ISOWeek.GetWeekOfYear(date), DayOfWeek.Monday);
    }
    static bool TryWeekMonday(string key, out DateTime monday)
    {
        monday = default;
        if (key.Length != 8 || key[4..6] != "-W" || !int.TryParse(key[..4], out var year) || !int.TryParse(key[6..], out var week)) return false;
        try { monday = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday); return true; }
        catch { return false; }
    }

    static bool IsDottedPair(string value, HashSet<string> left, HashSet<string> right)
    {
        var index = value.IndexOf('.');
        return index > 0 && left.Contains(value[..index]) && right.Contains(value[(index + 1)..]);
    }
    static bool IsDistributionKey(string value)
    {
        var index = value.IndexOf('.');
        if (index <= 0) return false;
        var metric = value[..index];
        var bucket = value[(index + 1)..];
        return metric switch
        {
            "node_count" or "route_count" => CountBuckets.Contains(bucket),
            "repeat_count" => RepeatBuckets.Contains(bucket),
            "window_seconds" => DurationBuckets.Contains(bucket),
            "quantity" => QuantityBuckets.Contains(bucket),
            "radius" => RadiusBuckets.Contains(bucket),
            "field_count" => FieldBuckets.Contains(bucket),
            _ => false
        };
    }

    static bool IsSelectionKey(string value)
    {
        var index = value.IndexOf('.');
        if (index <= 0) return false;
        var control = value[..index];
        var option = value[(index + 1)..];
        return control switch
        {
            "grant_item" => GrantItems.Contains(option),
            "spawn_kind" => SpawnKinds.Contains(option),
            "spawn_prefab" => SpawnPrefabs.Contains(option),
            "binding_target" => BindingTargets.Contains(option),
            _ => false
        };
    }

    static readonly string[] CountBuckets = { "0", "1", "2-4", "5-8", "9-16", "17-32", "33-64", "65+" };
    static readonly string[] RepeatBuckets = { "1", "2", "3-4", "5-8", "9-16", "17+" };
    static readonly string[] DurationBuckets = { "none", "1-10", "11-30", "31-60", "61-300", "301-1800", "1801+" };
    static readonly string[] QuantityBuckets = { "1", "2-4", "5-10", "11-25", "26-50", "51-100", "101+" };
    static readonly string[] RadiusBuckets = { "0-1", "2-3", "4-5", "6-10", "11+" };
    static readonly string[] FieldBuckets = { "0", "1", "2", "3-4", "5+" };
    static string BucketCount(int value) => value switch { <= 0 => "0", 1 => "1", <= 4 => "2-4", <= 8 => "5-8", <= 16 => "9-16", <= 32 => "17-32", <= 64 => "33-64", _ => "65+" };
    static string BucketRepeat(int value) => value switch { <= 1 => "1", 2 => "2", <= 4 => "3-4", <= 8 => "5-8", <= 16 => "9-16", _ => "17+" };
    static string BucketDuration(int? value) => value switch { null => "none", <= 10 => "1-10", <= 30 => "11-30", <= 60 => "31-60", <= 300 => "61-300", <= 1800 => "301-1800", _ => "1801+" };
    static string BucketQuantity(int value) => value switch { <= 1 => "1", <= 4 => "2-4", <= 10 => "5-10", <= 25 => "11-25", <= 50 => "26-50", <= 100 => "51-100", _ => "101+" };
    static string BucketRadius(int value) => value switch { <= 1 => "0-1", <= 3 => "2-3", <= 5 => "4-5", <= 10 => "6-10", _ => "11+" };
    static string BucketFields(int value) => value switch { <= 0 => "0", 1 => "1", 2 => "2", <= 4 => "3-4", _ => "5+" };

    sealed class UsageSettings { public int SchemaVersion { get; set; } = 1; public bool Enabled { get; set; } = true; }
    sealed class UsageAggregate { public int SchemaVersion { get; set; } = 1; public Dictionary<string, UsageWeek> Weeks { get; set; } = new(StringComparer.Ordinal); }
    sealed class UsageWeek
    {
        public Dictionary<string, long> Outcomes { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> Templates { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> Events { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> Actions { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> Selections { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> Distributions { get; set; } = new(StringComparer.Ordinal);
    }
}

public sealed record StudioUsageSettingsRequest(bool Enabled);
public sealed record StudioUsageResetRequest(bool Confirm);
public sealed record StudioUsageResetResult(bool Ok, string? Error, StudioUsageReport Report);
public sealed record StudioUsageReport(int SchemaVersion, bool Available, bool SettingsAvailable, bool AggregateAvailable,
    bool Enabled, string Storage, int RetentionWeeks, string Privacy, IReadOnlyDictionary<string, StudioUsageWeek> Weeks);
public sealed record StudioUsageWeek(
    IReadOnlyDictionary<string, long> Outcomes,
    IReadOnlyDictionary<string, long> Templates,
    IReadOnlyDictionary<string, long> Events,
    IReadOnlyDictionary<string, long> Actions,
    IReadOnlyDictionary<string, long> Selections,
    IReadOnlyDictionary<string, long> Distributions);

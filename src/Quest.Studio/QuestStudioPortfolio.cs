using ComfyQuestContracts;

namespace Comfy.Quest.Studio;

/// <summary>
/// The portfolio is the creator's local library identity. Projects remain independent
/// schema-3 drafts; guild documents arrange them without rewriting or flattening them.
/// </summary>
public sealed class StudioPortfolioDocument
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string PortfolioId { get; set; } = string.Empty;
    public string Title { get; set; } = "My Quest Portfolio";
    public int Revision { get; set; } = 1;
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class StudioGuildDocument
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string GuildId { get; set; } = string.Empty;
    public int Revision { get; set; } = 1;
    public DateTimeOffset UpdatedUtc { get; set; }
    public string Version { get; set; } = "0.1.0";
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProgressionScope { get; set; } = string.Empty;
    public List<string> Compatibility { get; set; } = new();
    public StudioGuildProvenance Provenance { get; set; } = new();
    public string ReleaseState { get; set; } = "draft";
    public bool Archived { get; set; }
    public List<StudioProgressionBand> ProgressionBands { get; set; } = new();
    public List<StudioQuestline> Questlines { get; set; } = new();
    public List<StudioGuildArtifact> StandaloneQuests { get; set; } = new();
    public List<StudioGuildArtifact> Events { get; set; } = new();
}

public sealed class StudioGuildProvenance
{
    public string Origin { get; set; } = "local";
    public string? SourceGuildId { get; set; }
    public string? SourceVersion { get; set; }
}

public sealed class StudioProgressionBand
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class StudioQuestline
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<StudioGuildArtifact> Quests { get; set; } = new();
}

public sealed class StudioGuildArtifact
{
    public string ProjectId { get; set; } = string.Empty;
    public string Kind { get; set; } = "quest";
    public string? ProgressionBandId { get; set; }
    /// <summary>AND prerequisites expressed in stable project identity. Guild publication maps
    /// these to the corresponding experience ids in the one canonical pack.</summary>
    public List<string> PrerequisiteProjectIds { get; set; } = new();
    /// <summary>Phase 4A reset semantics: a successor run receives fresh action claims.</summary>
    public string RewardPolicy { get; set; } = "per_run";
}

public sealed record StudioPortfolioSaveRequest(int ExpectedRevision, StudioPortfolioDocument Portfolio);
public sealed record StudioGuildSaveRequest(int ExpectedRevision, StudioGuildDocument Guild);
public sealed record StudioGuildCreateRequest(string? Title, string? Author);
public sealed record StudioGuildArchiveRequest(int ExpectedRevision, bool Archived);
public sealed record StudioGuildPlacementRequest(
    int ExpectedRevision,
    string ProjectId,
    string Kind,
    string ContainerKind,
    string? QuestlineId,
    string? ProgressionBandId);
public sealed record StudioGuildDuplicateRequest(int ExpectedRevision);
public sealed record StudioGuildImportRequest(StudioGuildBundleDocument? Bundle);
public sealed record StudioPortfolioSaveResult(bool Ok, bool Conflict, string? Error, StudioPortfolioDocument? Portfolio);
public sealed record StudioGuildSaveResult(bool Ok, bool Conflict, string? Error, StudioGuildDocument? Guild);
public sealed record StudioGuildSummary(
    string GuildId,
    string Version,
    string Title,
    string Author,
    string ReleaseState,
    bool Archived,
    int QuestlineCount,
    int QuestCount,
    int EventCount,
    DateTimeOffset UpdatedUtc);
public sealed record StudioProjectPlacement(
    string ProjectId,
    string GuildId,
    string ContainerKind,
    string? QuestlineId,
    string Kind,
    string? ProgressionBandId,
    string RewardPolicy);
public sealed record StudioGuildBundleDocument(
    int SchemaVersion,
    StudioGuildDocument Guild,
    IReadOnlyList<StudioProjectDocument> Projects);
public sealed record StudioGuildImportResult(
    bool Ok,
    string? Error,
    StudioGuildDocument? Guild,
    IReadOnlyList<StudioProjectDocument> Projects);

internal sealed class QuestStudioPortfolioStore
{
    internal const int MaxGuilds = 64;
    internal const int MaxProjects = 512;
    internal const int MaxArtifactsPerGuild = 128;
    internal const int MaxQuestlinesPerGuild = 32;
    internal const int MaxBandsPerGuild = 64;
    const int MaxDocumentBytes = 1024 * 1024;

    readonly object _gate = new();
    readonly string _portfolioPath;
    readonly string _guildsRoot;
    readonly string _transactionsRoot;
    readonly IQuestStudioHost _host;
    bool _recovering;

    public QuestStudioPortfolioStore(IQuestStudioHost host)
    {
        _host = host;
        var root = Path.Combine(host.StateDirectory, "quest-studio");
        _portfolioPath = Path.Combine(root, "portfolio.json");
        _guildsRoot = Path.Combine(root, "guilds");
        _transactionsRoot = Path.Combine(root, "portfolio-transactions");
        Directory.CreateDirectory(_guildsRoot);
        Directory.CreateDirectory(_transactionsRoot);
    }

    public StudioPortfolioDocument ReadPortfolio()
    {
        lock (_gate)
        {
            RecoverPlacementTransactions();
            var current = Read<StudioPortfolioDocument>(_portfolioPath);
            if (current is not null && ValidatePortfolio(current) is null) return current;
            if (File.Exists(_portfolioPath))
            {
                var previous = Read<StudioPortfolioDocument>(_portfolioPath + ".previous");
                if (previous is not null && ValidatePortfolio(previous) is null) return previous;
                throw new InvalidDataException("portfolio_unreadable");
            }
            var created = new StudioPortfolioDocument
            {
                PortfolioId = "portfolio-" + Guid.NewGuid().ToString("N")[..12],
                UpdatedUtc = DateTimeOffset.UtcNow,
            };
            AtomicWrite(_portfolioPath, created, create: !File.Exists(_portfolioPath));
            return created;
        }
    }

    public StudioPortfolioSaveResult SavePortfolio(StudioPortfolioSaveRequest? request)
    {
        if (request?.Portfolio is null) return new(false, false, "portfolio_required", null);
        lock (_gate)
        {
            var current = ReadPortfolio();
            if (request.ExpectedRevision != current.Revision)
                return new(false, true, "revision_conflict", current);
            var candidate = Clone(request.Portfolio);
            if (!string.Equals(candidate.PortfolioId, current.PortfolioId, StringComparison.Ordinal))
                return new(false, false, "portfolio_identity_immutable", null);
            candidate.Revision = current.Revision + 1;
            candidate.UpdatedUtc = DateTimeOffset.UtcNow;
            var error = ValidatePortfolio(candidate);
            if (error is not null) return new(false, false, error, null);
            AtomicWrite(_portfolioPath, candidate, create: false);
            return new(true, false, null, candidate);
        }
    }

    public IReadOnlyList<StudioGuildSummary> ListGuilds()
    {
        lock (_gate)
        {
            RecoverPlacementTransactions();
            var guilds = new List<StudioGuildDocument>();
            foreach (var directory in Directory.GetDirectories(_guildsRoot))
            {
                var id = Path.GetFileName(directory);
                if (!SafeId(id)) throw new InvalidDataException("guild_directory_invalid");
                var guild = ReadGuild(id) ?? throw new InvalidDataException("guild_unreadable");
                guilds.Add(guild);
            }
            return guilds
                .OrderBy(value => value.Archived)
                .ThenByDescending(value => value.UpdatedUtc)
                .Select(Summary)
                .ToArray();
        }
    }

    public StudioGuildDocument? ReadGuild(string? guildId)
    {
        if (!SafeId(guildId)) return null;
        lock (_gate)
        {
            RecoverPlacementTransactions();
            var path = GuildPath(guildId!);
            var value = Read<StudioGuildDocument>(path);
            if (value is not null && ValidateGuild(value, checkGlobalMembership: false) is null) return value;
            if (!File.Exists(path)) return null;
            var previous = Read<StudioGuildDocument>(path + ".previous");
            if (previous is not null && ValidateGuild(previous, checkGlobalMembership: false) is null) return previous;
            throw new InvalidDataException("guild_unreadable");
        }
    }

    public StudioGuildDocument CreateGuild(StudioGuildCreateRequest? request)
    {
        lock (_gate)
        {
            if (ListGuilds().Count >= MaxGuilds) throw new InvalidOperationException("portfolio_guild_limit");
            var id = "guild-" + Guid.NewGuid().ToString("N")[..12];
            var guild = new StudioGuildDocument
            {
                GuildId = id,
                Title = Bounded(request?.Title, "New guild", 120),
                Author = Bounded(request?.Author, Environment.UserName, 120),
                UpdatedUtc = DateTimeOffset.UtcNow,
                Questlines = new List<StudioQuestline>
                {
                    new() { Id = "main", Title = "Main questline" },
                },
            };
            AtomicWrite(GuildPath(id), guild, create: true);
            return guild;
        }
    }

    public StudioGuildSaveResult SaveGuild(string? guildId, StudioGuildSaveRequest? request)
    {
        if (!SafeId(guildId) || request?.Guild is null)
            return new(false, false, "guild_required", null);
        lock (_gate)
        {
            var current = ReadGuild(guildId);
            if (current is null) return new(false, false, "guild_missing", null);
            if (request.ExpectedRevision != current.Revision)
                return new(false, true, "revision_conflict", current);
            var candidate = Clone(request.Guild);
            if (!string.Equals(candidate.GuildId, current.GuildId, StringComparison.Ordinal))
                return new(false, false, "guild_identity_immutable", null);
            candidate.Revision = current.Revision + 1;
            candidate.UpdatedUtc = DateTimeOffset.UtcNow;
            var error = ValidateGuild(candidate, checkGlobalMembership: true);
            if (error is not null) return new(false, false, error, null);
            AtomicWrite(GuildPath(guildId!), candidate, create: false);
            return new(true, false, null, candidate);
        }
    }

    public StudioGuildSaveResult SetArchived(string? guildId, StudioGuildArchiveRequest? request)
    {
        var current = ReadGuild(guildId);
        if (current is null || request is null) return new(false, false, "guild_missing", null);
        current.Archived = request.Archived;
        current.ReleaseState = request.Archived ? "archived" : current.ReleaseState == "archived" ? "draft" : current.ReleaseState;
        return SaveGuild(guildId, new StudioGuildSaveRequest(request.ExpectedRevision, current));
    }

    public StudioGuildSaveResult Place(string? guildId, StudioGuildPlacementRequest? request, Func<string, bool> projectExists)
    {
        if (!SafeId(guildId) || request is null) return new(false, false, "guild_missing", null);
        lock (_gate)
        {
            RecoverPlacementTransactions();
            var target = ReadGuild(guildId);
            if (target is null) return new(false, false, "guild_missing", null);
            if (request.ExpectedRevision != target.Revision) return new(false, true, "revision_conflict", target);
            if (!projectExists(request.ProjectId)) return new(false, false, "project_missing", null);
            var existing = Placements().Where(value => value.ProjectId == request.ProjectId).ToArray();
            if (existing.Length > 1) return new(false, false, "portfolio_membership_ambiguous", null);
            var sourceId = existing.SingleOrDefault()?.GuildId;
            if (request.ContainerKind == "unfile" && sourceId is null) return new(true, false, null, target);
            if (request.ContainerKind == "unfile" && sourceId != guildId) return new(false, false, "placement_source_mismatch", null);

            var targetAfter = Clone(target);
            RemoveProject(targetAfter, request.ProjectId);
            if (request.ContainerKind != "unfile")
            {
                var artifact = new StudioGuildArtifact
                {
                    ProjectId = request.ProjectId,
                    Kind = request.Kind,
                    ProgressionBandId = NullIfWhite(request.ProgressionBandId),
                    RewardPolicy = "per_run",
                };
                if (request.ContainerKind == "event") targetAfter.Events.Add(artifact);
                else if (request.ContainerKind == "standalone") targetAfter.StandaloneQuests.Add(artifact);
                else if (request.ContainerKind == "questline")
                {
                    var line = targetAfter.Questlines.FirstOrDefault(value => value.Id == request.QuestlineId);
                    if (line is null) return new(false, false, "questline_missing", null);
                    line.Quests.Add(artifact);
                }
                else return new(false, false, "container_kind_invalid", null);
            }

            StudioGuildDocument? sourceAfter = null;
            if (sourceId is not null && sourceId != guildId)
            {
                var source = ReadGuild(sourceId);
                if (source is null) return new(false, false, "placement_source_missing", null);
                sourceAfter = Clone(source);
                RemoveProject(sourceAfter, request.ProjectId);
                Advance(sourceAfter);
            }
            Advance(targetAfter);
            var targetError = ValidateGuild(targetAfter, checkGlobalMembership: false);
            var sourceError = sourceAfter is null ? null : ValidateGuild(sourceAfter, checkGlobalMembership: false);
            if (targetError is not null || sourceError is not null) return new(false, false, targetError ?? sourceError, null);

            var transaction = new StudioPlacementTransaction
            {
                TransactionId = "placement-" + Guid.NewGuid().ToString("N"),
                CreatedUtc = DateTimeOffset.UtcNow,
                SourceAfter = sourceAfter,
                TargetAfter = targetAfter,
            };
            var transactionPath = Path.Combine(_transactionsRoot, transaction.TransactionId + ".json");
            AtomicWrite(transactionPath, transaction, create: true);
            if (sourceAfter is not null) AtomicWrite(GuildPath(sourceAfter.GuildId), sourceAfter, create: false);
            AtomicWrite(GuildPath(targetAfter.GuildId), targetAfter, create: false);
            File.Delete(transactionPath);
            return new(true, false, null, targetAfter);
        }
    }

    public IReadOnlyList<StudioProjectPlacement> Placements()
    {
        lock (_gate)
        {
            RecoverPlacementTransactions();
            var values = new List<StudioProjectPlacement>();
            foreach (var summary in ListGuilds())
            {
                var guild = ReadGuild(summary.GuildId);
                if (guild is null) continue;
                values.AddRange(guild.StandaloneQuests.Select(value => Placement(guild.GuildId, "standalone", null, value)));
                values.AddRange(guild.Events.Select(value => Placement(guild.GuildId, "event", null, value)));
                foreach (var line in guild.Questlines)
                    values.AddRange(line.Quests.Select(value => Placement(guild.GuildId, "questline", line.Id, value)));
            }
            return values;
        }
    }

    public StudioGuildDocument CreateFork(StudioGuildDocument source, IReadOnlyDictionary<string, string> projectIds, string origin)
    {
        lock (_gate)
        {
            if (ListGuilds().Count >= MaxGuilds) throw new InvalidOperationException("portfolio_guild_limit");
            var clone = Clone(source);
            clone.GuildId = "guild-" + Guid.NewGuid().ToString("N")[..12];
            clone.Revision = 1;
            clone.Version = "0.1.0";
            clone.Title = Bounded(source.Title + " (copy)", "Guild copy", 120);
            clone.Archived = false;
            clone.ReleaseState = "draft";
            clone.UpdatedUtc = DateTimeOffset.UtcNow;
            clone.Provenance = new StudioGuildProvenance
            {
                Origin = origin,
                SourceGuildId = source.GuildId,
                SourceVersion = source.Version,
            };
            foreach (var artifact in Artifacts(clone))
            {
                if (projectIds.TryGetValue(artifact.ProjectId, out var replacement)) artifact.ProjectId = replacement;
                artifact.PrerequisiteProjectIds = (artifact.PrerequisiteProjectIds ?? new())
                    .Select(value => projectIds.TryGetValue(value, out var mapped) ? mapped : value)
                    .ToList();
            }
            var error = ValidateGuild(clone, checkGlobalMembership: true);
            if (error is not null) throw new InvalidDataException(error);
            AtomicWrite(GuildPath(clone.GuildId), clone, create: true);
            return clone;
        }
    }

    public string? ValidateForkSource(StudioGuildDocument? source)
    {
        if (source is null) return "guild_bundle_invalid";
        lock (_gate)
        {
            RecoverPlacementTransactions();
            return ValidateGuild(Clone(source), checkGlobalMembership: false);
        }
    }

    static StudioProjectPlacement Placement(string guildId, string container, string? line, StudioGuildArtifact value) =>
        new(value.ProjectId, guildId, container, line, value.Kind, value.ProgressionBandId, value.RewardPolicy);

    static StudioGuildSummary Summary(StudioGuildDocument guild) => new(
        guild.GuildId, guild.Version, guild.Title, guild.Author, guild.ReleaseState, guild.Archived,
        guild.Questlines.Count,
        guild.StandaloneQuests.Count + guild.Questlines.Sum(value => value.Quests.Count),
        guild.Events.Count,
        guild.UpdatedUtc);

    string? ValidatePortfolio(StudioPortfolioDocument value)
    {
        if (value.SchemaVersion != StudioPortfolioDocument.CurrentSchemaVersion) return "portfolio_schema_unsupported";
        if (!SafeId(value.PortfolioId)) return "portfolio_id_invalid";
        if (string.IsNullOrWhiteSpace(value.Title) || value.Title.Length > 120) return "portfolio_title_invalid";
        if (value.Revision < 1) return "portfolio_revision_invalid";
        return null;
    }

    string? ValidateGuild(StudioGuildDocument value, bool checkGlobalMembership)
    {
        if (value.SchemaVersion != StudioGuildDocument.CurrentSchemaVersion) return "guild_schema_unsupported";
        if (!SafeId(value.GuildId)) return "guild_id_invalid";
        if (!SemanticVersion.TryParse(value.Version, out _)) return "guild_version_invalid";
        if (string.IsNullOrWhiteSpace(value.Title) || value.Title.Length > 120) return "guild_title_invalid";
        if (value.Revision < 1) return "guild_revision_invalid";
        if (value.Author?.Length > 120 || value.Description?.Length > 4000 || value.ProgressionScope?.Length > 1000) return "guild_field_bounds_invalid";
        if (value.ReleaseState is not ("draft" or "released" or "archived")) return "guild_release_state_invalid";
        if (value.Archived != (value.ReleaseState == "archived")) return "guild_archive_state_invalid";
        if (value.Compatibility is null || value.Provenance is null || value.ProgressionBands is null
            || value.Questlines is null || value.StandaloneQuests is null || value.Events is null)
            return "guild_structure_invalid";
        if (value.Compatibility.Count > 64 || value.ProgressionBands.Count > MaxBandsPerGuild || value.Questlines.Count > MaxQuestlinesPerGuild) return "guild_limit_exceeded";
        if (value.Compatibility.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 120 || tag.Any(char.IsControl))
            || value.Compatibility.Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Compatibility.Count)
            return "guild_compatibility_invalid";
        if (value.Provenance.Origin is not ("local" or "duplicate" or "import")
            || value.Provenance.SourceGuildId is not null && !SafeId(value.Provenance.SourceGuildId)
            || value.Provenance.SourceVersion is not null && !SemanticVersion.TryParse(value.Provenance.SourceVersion, out _))
            return "guild_provenance_invalid";
        var bands = new HashSet<string>(StringComparer.Ordinal);
        foreach (var band in value.ProgressionBands)
            if (band is null || !SafeId(band.Id) || !bands.Add(band.Id) || string.IsNullOrWhiteSpace(band.Title) || band.Title.Length > 120 || band.Description?.Length > 1000) return "progression_band_invalid";
        var lines = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in value.Questlines)
        {
            if (line is null || !SafeId(line.Id) || !lines.Add(line.Id) || string.IsNullOrWhiteSpace(line.Title)
                || line.Title.Length > 120 || line.Quests is null) return "questline_invalid";
        }
        var artifacts = Artifacts(value).ToArray();
        if (artifacts.Length > MaxArtifactsPerGuild) return "guild_artifact_limit";
        var projects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in artifacts)
        {
            if (artifact is null || !SafeId(artifact.ProjectId) || !projects.Add(artifact.ProjectId)) return "guild_artifact_duplicate";
            if (artifact.Kind is not ("quest" or "event") || artifact.RewardPolicy != "per_run") return "guild_artifact_policy_invalid";
            if (artifact.ProgressionBandId is not null && !bands.Contains(artifact.ProgressionBandId)) return "progression_band_missing";
            if (artifact.PrerequisiteProjectIds is null || artifact.PrerequisiteProjectIds.Count > ExperienceSchema.MaxPrerequisites
                || artifact.PrerequisiteProjectIds.Any(value => !SafeId(value))
                || artifact.PrerequisiteProjectIds.Distinct(StringComparer.Ordinal).Count() != artifact.PrerequisiteProjectIds.Count
                || artifact.PrerequisiteProjectIds.Contains(artifact.ProjectId, StringComparer.Ordinal))
                return "guild_prerequisite_invalid";
        }
        if (artifacts.SelectMany(value => value.PrerequisiteProjectIds).Any(value => !projects.Contains(value))) return "guild_prerequisite_missing";
        var dependencies = artifacts.ToDictionary(value => value.ProjectId, value => value.PrerequisiteProjectIds, StringComparer.Ordinal);
        var dependencyState = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var projectId in projects.OrderBy(value => value, StringComparer.Ordinal))
            if (Visit(projectId)) return "guild_prerequisite_cycle";
        if (value.Events.Any(item => item.Kind != "event") || value.StandaloneQuests.Any(item => item.Kind != "quest") || value.Questlines.Any(line => line.Quests.Any(item => item.Kind != "quest"))) return "guild_artifact_kind_invalid";
        if (checkGlobalMembership)
        {
            var ownedElsewhere = Placements().Where(item => item.GuildId != value.GuildId).Select(item => item.ProjectId).ToHashSet(StringComparer.Ordinal);
            if (artifacts.Any(item => ownedElsewhere.Contains(item.ProjectId))) return "project_already_assigned";
        }
        return null;

        bool Visit(string projectId)
        {
            if (dependencyState.TryGetValue(projectId, out var known)) return known == 1;
            dependencyState[projectId] = 1;
            foreach (var prerequisite in dependencies[projectId]) if (Visit(prerequisite)) return true;
            dependencyState[projectId] = 2;
            return false;
        }
    }

    static IEnumerable<StudioGuildArtifact> Artifacts(StudioGuildDocument guild) =>
        guild.StandaloneQuests.Concat(guild.Events).Concat(guild.Questlines.SelectMany(value => value.Quests));

    static void RemoveProject(StudioGuildDocument guild, string projectId)
    {
        guild.StandaloneQuests.RemoveAll(value => value.ProjectId == projectId);
        guild.Events.RemoveAll(value => value.ProjectId == projectId);
        foreach (var line in guild.Questlines) line.Quests.RemoveAll(value => value.ProjectId == projectId);
    }

    static void Advance(StudioGuildDocument guild)
    {
        guild.Revision++;
        guild.UpdatedUtc = DateTimeOffset.UtcNow;
    }

    void RecoverPlacementTransactions()
    {
        if (_recovering || !Directory.Exists(_transactionsRoot)) return;
        _recovering = true;
        try
        {
            foreach (var path in Directory.GetFiles(_transactionsRoot, "*.json").OrderBy(value => value, StringComparer.Ordinal))
            {
                var transaction = Read<StudioPlacementTransaction>(path);
                if (transaction is null || transaction.SchemaVersion != 1 || !SafeId(transaction.TransactionId)
                    || transaction.TargetAfter is null || !SafeId(transaction.TargetAfter.GuildId)
                    || transaction.SourceAfter is not null && !SafeId(transaction.SourceAfter.GuildId))
                    throw new InvalidDataException("portfolio_transaction_unreadable");
                if (ValidateGuild(transaction.TargetAfter, checkGlobalMembership: false) is not null
                    || transaction.SourceAfter is not null && ValidateGuild(transaction.SourceAfter, checkGlobalMembership: false) is not null)
                    throw new InvalidDataException("portfolio_transaction_invalid");
                if (transaction.SourceAfter is not null)
                    AtomicWrite(GuildPath(transaction.SourceAfter.GuildId), transaction.SourceAfter, create: false);
                AtomicWrite(GuildPath(transaction.TargetAfter.GuildId), transaction.TargetAfter, create: false);
                File.Delete(path);
            }
        }
        finally { _recovering = false; }
    }

    T? Read<T>(string path) where T : class
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length is > 0 and <= MaxDocumentBytes
                ? System.Text.Json.JsonSerializer.Deserialize<T>(File.ReadAllText(path), _host.Json)
                : null;
        }
        catch { return null; }
    }

    T Clone<T>(T value) => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value, _host.Json), _host.Json)!;

    void AtomicWrite<T>(string target, T value, bool create)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        var json = System.Text.Json.JsonSerializer.Serialize(value, _host.Json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes) throw new InvalidDataException("portfolio_document_too_large");
        File.WriteAllText(temporary, json);
        try
        {
            if (create) File.Move(temporary, target);
            else if (File.Exists(target)) File.Replace(temporary, target, target + ".previous", ignoreMetadataErrors: true);
            else File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    string GuildDirectory(string guildId) => Path.Combine(_guildsRoot, guildId);
    string GuildPath(string guildId) => Path.Combine(GuildDirectory(guildId), "guild.json");
    static bool SafeId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
    static string Bounded(string? value, string fallback, int maximum)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return result.Length <= maximum ? result : result[..maximum];
    }
    static string? NullIfWhite(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    sealed class StudioPlacementTransaction
    {
        public int SchemaVersion { get; set; } = 1;
        public string TransactionId { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }
        public StudioGuildDocument? SourceAfter { get; set; }
        public StudioGuildDocument TargetAfter { get; set; } = new();
    }
}

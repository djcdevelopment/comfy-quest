using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ComfyQuestContracts;
using Newtonsoft.Json;

namespace Comfy.Quest.Studio;

public sealed partial class QuestStudioService
{
    static readonly string[] RuntimeEvents = RuntimeProductionEventCatalog.All
        .Select(definition => definition.Name)
        .Concat(RuntimeProductionEventCatalog.EngineEvents.Select(definition => definition.Name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    readonly object _lock = new();
    readonly string _projectPath;
    readonly string _historyPath;
    readonly QuestPackPublisher _publisher;
    readonly IQuestStudioHost _host;
    readonly QuestStudioWorkspace _workspace;
    readonly QuestStudioPortfolioStore _portfolio;
    readonly QuestStudioRunControl _runControl;
    readonly QuestStudioCreator _creator;
    readonly QuestStudioCreatorCast _creatorCast;
    readonly QuestStudioDataExport _dataExport;
    readonly QuestStudioUsageInsights _usage;
    readonly string _creativeEvidenceRoot;
    readonly IStudioCampaignPlayPrerequisiteRunner _campaignPlayPrerequisites;

    public QuestStudioService(IQuestStudioHost host, QuestPackPublisher publisher)
        : this(host, publisher, new StudioCampaignPlayPrerequisiteRunner(host))
    {
    }

    internal QuestStudioService(IQuestStudioHost host, QuestPackPublisher publisher,
        IStudioCampaignPlayPrerequisiteRunner campaignPlayPrerequisites)
    {
        var root = Path.Combine(host.StateDirectory, "quest-studio");
        Directory.CreateDirectory(root);
        _projectPath = Path.Combine(root, "project.json");
        _historyPath = Path.Combine(root, "history");
        _publisher = publisher;
        _host = host;
        _workspace = new QuestStudioWorkspace(host, publisher);
        _portfolio = new QuestStudioPortfolioStore(host);
        _runControl = new QuestStudioRunControl(host, _workspace);
        _creator = new QuestStudioCreator(host, _workspace, _runControl);
        _creatorCast = new QuestStudioCreatorCast(host, publisher, _workspace, _runControl, _creator);
        _dataExport = new QuestStudioDataExport(host);
        _usage = new QuestStudioUsageInsights(host);
        _creativeEvidenceRoot = Path.Combine(root, "creative-evidence");
        _campaignPlayPrerequisites = campaignPlayPrerequisites;
        Directory.CreateDirectory(_creativeEvidenceRoot);
        _creatorOperations = new CreatorOperationJournal(host.StateDirectory);
    }

    public object WorkspaceCatalog() => _workspace.Catalog();
    public Task<StudioCreatorSceneResult> FetchCreatorSceneAsync(
        StudioCreatorSceneRequest? request, CancellationToken cancellationToken) =>
        _creator.FetchSceneAsync(request, cancellationToken);
    public Task<StudioCreatorTargetResult> SelectCreatorTargetAsync(string projectId,
        StudioCreatorTargetRequest? request, CancellationToken cancellationToken) =>
        _creator.SelectTargetAsync(projectId, request, cancellationToken);
    public Task<StudioCreatorCastResult> CreatorCastAsync(string projectId,
        StudioCreatorCastRequest? request, CancellationToken cancellationToken) =>
        SerializeCreatorControl(() => _creatorCast.CastAsync(projectId, request, cancellationToken), cancellationToken);
    public StudioCreatorCastResult CreatorCastStatus(string projectId) =>
        _creatorCast.Status(projectId);
    public Task<StudioCreatorCastResult> UndoCreatorCastAsync(string projectId,
        StudioCreatorUndoCastRequest? request, CancellationToken cancellationToken) =>
        SerializeCreatorControl(() => _creatorCast.UndoAsync(projectId, request, cancellationToken), cancellationToken);
    public StudioPortfolioDocument Portfolio() => _portfolio.ReadPortfolio();
    public StudioPortfolioSaveResult SavePortfolio(StudioPortfolioSaveRequest? request) => _portfolio.SavePortfolio(request);
    public IReadOnlyList<StudioGuildSummary> ListGuilds() => _portfolio.ListGuilds();
    public StudioGuildDocument? ReadGuild(string guildId) => _portfolio.ReadGuild(guildId);
    public StudioGuildDocument CreateGuild(StudioGuildCreateRequest? request) => _portfolio.CreateGuild(request);
    public StudioGuildSaveResult SaveGuild(string guildId, StudioGuildSaveRequest? request) => _portfolio.SaveGuild(guildId, request);
    public StudioGuildSaveResult ArchiveGuild(string guildId, StudioGuildArchiveRequest? request) => _portfolio.SetArchived(guildId, request);
    public StudioGuildSaveResult PlaceProject(string guildId, StudioGuildPlacementRequest? request) =>
        _portfolio.Place(guildId, request, projectId => _workspace.ReadProject(projectId) is not null);
    public object PortfolioView()
    {
        var projects = _workspace.ListProjects();
        var placements = _portfolio.Placements();
        var assigned = placements.Select(value => value.ProjectId).ToHashSet(StringComparer.Ordinal);
        return new
        {
            schema_version = 1,
            portfolio = _portfolio.ReadPortfolio(),
            guilds = _portfolio.ListGuilds(),
            projects,
            placements,
            unfiled = projects.Where(value => !assigned.Contains(value.ProjectId)).ToArray(),
            limits = new
            {
                guilds = QuestStudioPortfolioStore.MaxGuilds,
                projects = QuestStudioPortfolioStore.MaxProjects,
                artifacts_per_guild = QuestStudioPortfolioStore.MaxArtifactsPerGuild,
                questlines_per_guild = QuestStudioPortfolioStore.MaxQuestlinesPerGuild,
                progression_bands_per_guild = QuestStudioPortfolioStore.MaxBandsPerGuild,
            },
        };
    }

    public StudioSourceImportResult ImportSource(string guildId, StudioSourceImportRequest? request)
    {
        var guild = _portfolio.ReadGuild(guildId);
        if (guild is null) return new(false, false, "guild_missing", null, null);
        if (request is null || request.ExpectedRevision != guild.Revision)
            return new(false, true, "revision_conflict", guild, null);
        var snapshotId = SafeId(request.SourceSnapshotId) ? request.SourceSnapshotId! : string.Empty;
        if (snapshotId.Length == 0 || string.IsNullOrWhiteSpace(request.CatalogJson)
            || string.IsNullOrWhiteSpace(request.ProvenanceJson) || request.AnomaliesText is null)
            return new(false, false, "source_snapshot_required", guild, null);
        if (Encoding.UTF8.GetByteCount(request.CatalogJson) > 4 * 1024 * 1024
            || Encoding.UTF8.GetByteCount(request.ProvenanceJson) > 4 * 1024 * 1024
            || Encoding.UTF8.GetByteCount(request.AnomaliesText) > 1024 * 1024)
            return new(false, false, "source_snapshot_too_large", guild, null);
        try
        {
            using var catalog = JsonDocument.Parse(request.CatalogJson);
            using var provenance = JsonDocument.Parse(request.ProvenanceJson);
            var root = catalog.RootElement;
            var proof = provenance.RootElement;
            if (!root.TryGetProperty("schema_version", out var schema) || schema.GetInt32() != 1
                || !root.TryGetProperty("guild", out var guildName) || string.IsNullOrWhiteSpace(guildName.GetString())
                || !root.TryGetProperty("era", out var eraValue) || !eraValue.TryGetInt32(out var era) || era < 1
                || !root.TryGetProperty("quests", out var quests) || quests.ValueKind != JsonValueKind.Array
                || !proof.TryGetProperty("schema_version", out var proofSchema) || proofSchema.GetInt32() != 1
                || !proof.TryGetProperty("anomalies", out var anomalies) || anomalies.ValueKind != JsonValueKind.Array)
                return new(false, false, "source_snapshot_schema_invalid", guild, null);
            var questIds = quests.EnumerateArray().Select(value => value.TryGetProperty("quest_id", out var id) ? id.GetString() : null).ToArray();
            if (questIds.Any(value => !SafeId(value)) || questIds.Distinct(StringComparer.Ordinal).Count() != questIds.Length)
                return new(false, false, "source_snapshot_quest_ids_invalid", guild, null);
            var existing = guild.SourceSnapshots.FirstOrDefault(value => value.SourceSnapshotId == snapshotId);
            var snapshot = new StudioGuildSourceSnapshot
            {
                SourceSnapshotId = snapshotId,
                Title = Bounded(request.Title, guildName.GetString() + " source", 120),
                Guild = guildName.GetString()!,
                Era = era,
                SourceKind = root.TryGetProperty("source", out var source) && source.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "catalog" : "catalog",
                CatalogJson = request.CatalogJson,
                CatalogSha256 = StudioCreativeHash.Text(request.CatalogJson),
                ProvenanceJson = request.ProvenanceJson,
                ProvenanceSha256 = StudioCreativeHash.Text(request.ProvenanceJson),
                AnomaliesText = request.AnomaliesText,
                AnomaliesSha256 = StudioCreativeHash.Text(request.AnomaliesText),
                SnapshotHash = StudioCreativeHash.Parts(request.CatalogJson, request.ProvenanceJson, request.AnomaliesText),
                EntryCount = questIds.Length,
                AnomalyCount = anomalies.GetArrayLength(),
                ImportedUtc = DateTimeOffset.UtcNow,
            };
            if (existing is not null)
                return existing.SnapshotHash == snapshot.SnapshotHash
                    ? new(true, false, null, guild, existing)
                    : new(false, false, "source_snapshot_immutable", guild, existing);
            var saved = _portfolio.MutateCreativeConfig(guildId, request.ExpectedRevision, value => value.SourceSnapshots.Add(snapshot));
            return new(saved.Ok, saved.Conflict, saved.Error, saved.Guild, saved.Ok ? snapshot : null);
        }
        catch (System.Text.Json.JsonException)
        {
            return new(false, false, "source_snapshot_schema_invalid", guild, null);
        }
        catch (InvalidOperationException)
        {
            return new(false, false, "source_snapshot_schema_invalid", guild, null);
        }
    }

    public StudioAbstractionMutationResult PromoteAbstraction(string guildId, StudioAbstractionPromoteRequest? request)
    {
        var guild = _portfolio.ReadGuild(guildId);
        if (guild is null) return new(false, false, "guild_missing", null, null);
        if (request is null || request.ExpectedRevision != guild.Revision)
            return new(false, true, "revision_conflict", guild, null);
        if (!SafeId(request.AbstractionId) || !SafeId(request.SourceSnapshotId) || !SafeId(request.ProjectId)
            || !SafeId(request.RouteId) || request.SourceQuestIds is not { Count: > 0 }
            || request.TargetChoices is not { Count: > 0 })
            return new(false, false, "abstraction_required", guild, null);
        if (guild.Abstractions.Any(value => value.AbstractionId == request.AbstractionId))
            return new(false, false, "abstraction_identity_exists", guild, null);
        var snapshot = guild.SourceSnapshots.FirstOrDefault(value => value.SourceSnapshotId == request.SourceSnapshotId);
        if (snapshot is null) return new(false, false, "source_snapshot_missing", guild, null);
        if (!CatalogContains(snapshot.CatalogJson, request.SourceQuestIds))
            return new(false, false, "abstraction_source_citation_missing", guild, null);
        var project = _workspace.ReadProject(request.ProjectId!);
        if (project is null) return new(false, false, "project_missing", guild, null);
        var projectError = _workspace.ValidateForkSource(project);
        if (projectError is not null) return new(false, false, projectError, guild, null);
        var node = project.Nodes.FirstOrDefault(value => value.Routes.Any(route => route.Id == request.RouteId));
        var route = node?.Routes.FirstOrDefault(value => value.Id == request.RouteId);
        if (node is null || route is null || route.Event != "kill"
            || route.Where is null || !route.Where.TryGetValue("weapon_skill", out var skill) || skill != "Spears"
            || !route.Where.TryGetValue("projectile", out var projectile) || !string.Equals(projectile, "true", StringComparison.OrdinalIgnoreCase))
            return new(false, false, "abstraction_signature_hunt_invariant_missing", guild, null);
        var actionId = string.IsNullOrWhiteSpace(request.CompletionActionId)
            ? route.Actions.FirstOrDefault(value => value.Type == "message")?.Id
            : request.CompletionActionId;
        if (actionId is not null && !route.Actions.Any(value => value.Id == actionId && value.Type == "message"))
            return new(false, false, "abstraction_completion_action_missing", guild, null);
        if (request.TargetChoices.Any(choice => choice is null || !SafeId(choice.Id) || string.IsNullOrWhiteSpace(choice.RuntimeTarget)
                || !request.SourceQuestIds.Contains(choice.SourceQuestId, StringComparer.Ordinal)))
            return new(false, false, "abstraction_target_choice_invalid", guild, null);
        var canonicalJson = System.Text.Json.JsonSerializer.Serialize(project, _host.Json);
        var abstraction = new StudioGuildAbstractionDocument
        {
            AbstractionId = request.AbstractionId!,
            Revision = 1,
            Title = Bounded(request.Title, "Creative abstraction", 120),
            Explanation = Bounded(request.Explanation, string.Empty, 2000),
            SourceSnapshotId = snapshot.SourceSnapshotId,
            SourceSnapshotHash = snapshot.SnapshotHash,
            SourceQuestIds = request.SourceQuestIds.Distinct(StringComparer.Ordinal).ToList(),
            Attribution = Bounded(request.Attribution, snapshot.Guild, 500),
            EvidencePolicy = EvidencePolicy(request.EvidencePolicy),
            EvidenceExplanation = Bounded(request.EvidenceExplanation, string.Empty, 2000),
            CanonicalProjectJson = canonicalJson,
            CanonicalProjectHash = StudioCreativeHash.Text(canonicalJson),
            EntryNodeId = project.EntryNodeId,
            RouteId = route.Id,
            CompletionActionId = actionId,
            TargetChoices = request.TargetChoices.Select(CloneTarget).ToList(),
            PublishedUtc = DateTimeOffset.UtcNow,
        };
        abstraction.InvariantHash = InvariantHash(project, abstraction);
        abstraction.ContentHash = AbstractionHash(abstraction);
        var saved = _portfolio.MutateCreativeConfig(guildId, request.ExpectedRevision, value =>
        {
            value.Abstractions.Add(abstraction);
            value.Palette.Add(new StudioGuildPaletteEntry
            {
                AbstractionId = abstraction.AbstractionId,
                Revision = abstraction.Revision,
                ContentHash = abstraction.ContentHash,
            });
        });
        return new(saved.Ok, saved.Conflict, saved.Error, saved.Guild, saved.Ok ? abstraction : null);
    }

    public StudioAbstractionMutationResult ReviseAbstraction(string guildId, string abstractionId, StudioAbstractionReviseRequest? request)
    {
        var guild = _portfolio.ReadGuild(guildId);
        if (guild is null) return new(false, false, "guild_missing", null, null);
        if (request is null || request.ExpectedRevision != guild.Revision)
            return new(false, true, "revision_conflict", guild, null);
        var prior = guild.Abstractions.Where(value => value.AbstractionId == abstractionId)
            .OrderByDescending(value => value.Revision).FirstOrDefault();
        if (prior is null) return new(false, false, "abstraction_missing", guild, null);
        var revised = Clone(prior);
        revised.Revision++;
        revised.EvidencePolicy = EvidencePolicy(request.EvidencePolicy ?? prior.EvidencePolicy);
        revised.EvidenceExplanation = Bounded(request.EvidenceExplanation, prior.EvidenceExplanation, 2000);
        if (request.ConfigurablePractice || request.PracticeTargets?.Count > 0)
        {
            if (abstractionId != "slayers-signature-hunt" || !request.StageOwnedTarget)
                return new(false, false, "abstraction_practice_target_unsupported", guild, null);
            revised.ConfigurablePractice |= request.ConfigurablePractice;
            foreach (var id in request.PracticeTargets ?? [])
            {
                if (id is not ("draugr" or "greyling" or "boar" or "lox"))
                    return new(false, false, "abstraction_practice_target_unsupported", guild, null);
                if (revised.TargetChoices.All(choice => choice.Id != id))
                    revised.TargetChoices.Add(new StudioAbstractionTargetChoice
                        { Id = id, Label = char.ToUpperInvariant(id[0]) + id[1..], RuntimeTarget = "$enemy_" + id,
                            PracticeAttribution = "Configurable R&D encounter; outside the frozen Slayers quest source." });
            }
        }
        if (request.IncludePracticeLox)
        {
            if (abstractionId != "slayers-signature-hunt" || !request.StageOwnedTarget)
                return new(false, false, "abstraction_practice_target_unsupported", guild, null);
            if (revised.TargetChoices.All(choice => choice.Id != "lox"))
                revised.TargetChoices.Add(new StudioAbstractionTargetChoice
                    { Id = "lox", Label = "Lox (practice)", RuntimeTarget = "$enemy_lox",
                        PracticeAttribution = "Derek-requested R&D practice target; outside the frozen Slayers quest source." });
        }
        if (request.IncludePracticeDraugr)
        {
            if (abstractionId != "slayers-signature-hunt" || !request.StageOwnedTarget)
                return new(false, false, "abstraction_practice_target_unsupported", guild, null);
            if (revised.TargetChoices.All(choice => choice.Id != "draugr"))
                revised.TargetChoices.Add(new StudioAbstractionTargetChoice
                    { Id = "draugr", Label = "Draugr (practice)", RuntimeTarget = "$enemy_draugr",
                        PracticeAttribution = "Derek-requested R&D practice target; outside the frozen Slayers quest source." });
        }
        if (request.StageOwnedTarget)
        {
            if (abstractionId != "slayers-signature-hunt"
                || prior.TargetChoices.Any(choice => SignatureHuntPrefab(choice.RuntimeTarget) is null))
                return new(false, false, "abstraction_stage_target_unsupported", guild, null);
            var canonical = System.Text.Json.JsonSerializer.Deserialize<StudioProjectDocument>(prior.CanonicalProjectJson, _host.Json)!;
            if (!QuestStudioWorkspace.NormalizeDocument(canonical))
                return new(false, false, "abstraction_project_schema_invalid", guild, null);
            var entry = canonical.Nodes.Single(value => value.Id == prior.EntryNodeId);
            var route = canonical.Nodes.SelectMany(value => value.Routes).Single(value => value.Id == prior.RouteId);
            if (SignatureHuntPrefab(route.Target) is not { } targetPrefab)
                return new(false, false, "abstraction_stage_target_unsupported", guild, null);
            revised.TargetSpawnActionId = prior.TargetSpawnActionId ?? "signature-hunt-target";
            entry.EntryActions ??= new();
            var spawn = entry.EntryActions.SingleOrDefault(value => value.Id == revised.TargetSpawnActionId);
            if (spawn is null)
            {
                spawn = new StudioAction { Id = revised.TargetSpawnActionId, Type = "spawn", Kind = "creature", Count = 1, Radius = 12 };
                entry.EntryActions.Add(spawn);
            }
            spawn.Prefab = targetPrefab;
            revised.CanonicalProjectJson = System.Text.Json.JsonSerializer.Serialize(canonical, _host.Json);
            revised.CanonicalProjectHash = StudioCreativeHash.Text(revised.CanonicalProjectJson);
            revised.InvariantHash = InvariantHash(canonical, revised);
        }
        revised.PublishedUtc = DateTimeOffset.UtcNow;
        revised.ContentHash = AbstractionHash(revised);
        var saved = _portfolio.MutateCreativeConfig(guildId, request.ExpectedRevision, value =>
        {
            value.Abstractions.Add(revised);
            var palette = value.Palette.Single(item => item.AbstractionId == abstractionId);
            palette.Revision = revised.Revision;
            palette.ContentHash = revised.ContentHash;
        });
        return new(saved.Ok, saved.Conflict, saved.Error, saved.Guild, saved.Ok ? revised : null);
    }

    public StudioAbstractionInstantiateResult InstantiateAbstraction(string guildId, string abstractionId, StudioAbstractionInstantiateRequest? request)
    {
        lock (_lock)
        {
            var guild = _portfolio.ReadGuild(guildId);
            if (guild is null) return new(false, false, "guild_missing", null, null);
            if (request is null || request.ExpectedGuildRevision != guild.Revision)
                return new(false, true, "revision_conflict", guild, null);
            var abstraction = guild.Abstractions.SingleOrDefault(value => value.AbstractionId == abstractionId && value.Revision == request.AbstractionRevision);
            if (abstraction is null) return new(false, false, "abstraction_revision_missing", guild, null);
            var palette = guild.Palette.SingleOrDefault(value => value.AbstractionId == abstractionId);
            if (palette is null || palette.Revision != abstraction.Revision || palette.ContentHash != abstraction.ContentHash)
                return new(false, false, "abstraction_not_in_palette", guild, null);
            var choice = abstraction.TargetChoices.SingleOrDefault(value => value.Id == request.TargetChoiceId);
            if (choice is null) return new(false, false, "abstraction_target_choice_missing", guild, null);
            var mechanic = request.Mechanic ?? "thrown_spear";
            if (!HuntMechanicAllowed(abstraction, mechanic))
                return new(false, false, "hunt_mechanic_not_available", guild, null);
            using var json = JsonDocument.Parse(abstraction.CanonicalProjectJson);
            var imported = _workspace.Import(new StudioImportRequest(json.RootElement.Clone()));
            if (!imported.Ok || imported.Project is null)
                return new(false, false, imported.Error ?? "abstraction_project_import_failed", guild, null);
            var project = imported.Project;
            project.Title = Bounded(request.Title, choice.Label, 120);
            var entry = project.Nodes.Single(value => value.Id == abstraction.EntryNodeId);
            entry.Label = Bounded(request.Instructions, entry.Label, 500);
            var route = project.Nodes.SelectMany(value => value.Routes).Single(value => value.Id == abstraction.RouteId);
            route.Target = choice.RuntimeTarget;
            ApplyHuntMechanic(route, mechanic);
            if (abstraction.TargetSpawnActionId is not null)
                entry.EntryActions!.Single(value => value.Id == abstraction.TargetSpawnActionId).Prefab = SignatureHuntPrefab(choice.RuntimeTarget);
            var completion = abstraction.CompletionActionId is null ? null : route.Actions.Single(value => value.Id == abstraction.CompletionActionId);
            if (completion is not null) completion.Text = Bounded(request.CompletionMessage, completion.Text ?? string.Empty, 500);
            var configurationHash = HuntConfigurationHash(abstraction, project.Title, choice, entry.Label, completion?.Text, mechanic);
            project.Derivation = new StudioProjectDerivation
            {
                GuildId = guild.GuildId,
                AbstractionId = abstraction.AbstractionId,
                AbstractionRevision = abstraction.Revision,
                AbstractionHash = abstraction.ContentHash,
                SourceSnapshotId = abstraction.SourceSnapshotId,
                SourceSnapshotHash = abstraction.SourceSnapshotHash,
                ConfigurationHash = configurationHash,
                TargetChoiceId = choice.Id,
                TargetRuntimeValue = choice.RuntimeTarget,
                Instructions = entry.Label,
                CompletionMessage = completion?.Text ?? string.Empty,
                Mechanic = abstraction.ConfigurablePractice ? mechanic : null,
                InstantiatedUtc = DateTimeOffset.UtcNow,
            };
            if (InvariantHash(project, abstraction) != abstraction.InvariantHash)
                return new(false, false, "abstraction_invariant_expansion_failed", guild, null);
            var save = _workspace.SaveDraft(project.ProjectId, new StudioSaveRequest(project.Revision, project));
            if (!save.Ok || save.Project is null)
                return new(false, save.Conflict, save.Error ?? "abstraction_project_save_failed", guild, null);
            project = save.Project;
            var saved = _portfolio.MutateCreativeConfig(guildId, request.ExpectedGuildRevision, value => value.Artifacts.Add(new StudioGuildArtifactMembership
            {
                ProjectId = project.ProjectId,
                Kind = "quest",
                Creator = Bounded(request.Creator, value.Steward, 120),
                AbstractionId = abstraction.AbstractionId,
                AbstractionRevision = abstraction.Revision,
                RequiresGuildCompliance = true,
            }));
            return new(saved.Ok, saved.Conflict, saved.Error, saved.Guild, saved.Ok ? project : null);
        }
    }

    public StudioSaveResult ConfigureHunt(string projectId, StudioHuntConfigureRequest? request)
    {
        lock (_lock)
        {
            var project = _workspace.ReadProject(projectId);
            if (project?.Derivation is not { Detached: false } derivation)
                return StudioSaveResult.Fail("hunt_palette_instance_required");
            if (request is null || request.ExpectedRevision != project.Revision)
                return StudioSaveResult.RevisionConflict(project);
            var guild = _portfolio.ReadGuild(derivation.GuildId);
            var abstraction = guild?.Abstractions.SingleOrDefault(value => value.AbstractionId == derivation.AbstractionId
                && value.Revision == derivation.AbstractionRevision && value.ContentHash == derivation.AbstractionHash);
            if (abstraction?.TargetSpawnActionId is null || abstraction.AbstractionId != "slayers-signature-hunt")
                return StudioSaveResult.Fail("hunt_stage_owned_palette_revision_required");
            var mechanic = request.Mechanic ?? derivation.Mechanic ?? "thrown_spear";
            if (!HuntMechanicAllowed(abstraction, mechanic)) return StudioSaveResult.Fail("hunt_mechanic_not_available");
            var choice = abstraction.TargetChoices.SingleOrDefault(value => value.Id == request.TargetChoiceId);
            if (choice is null || SignatureHuntPrefab(choice.RuntimeTarget) is not { } prefab)
                return StudioSaveResult.Fail("abstraction_target_choice_missing");
            if (InvariantHash(project, abstraction) != abstraction.InvariantHash)
                return StudioSaveResult.Fail("abstraction_invariant_changed");
            var entry = project.Nodes.Single(value => value.Id == abstraction.EntryNodeId);
            var route = project.Nodes.SelectMany(value => value.Routes).Single(value => value.Id == abstraction.RouteId);
            var completion = route.Actions.SingleOrDefault(value => value.Id == abstraction.CompletionActionId);
            project.Title = Bounded(request.Title, project.Title, 120);
            entry.Label = Bounded(request.Instructions, entry.Label, 500);
            route.Target = choice.RuntimeTarget;
            ApplyHuntMechanic(route, mechanic);
            entry.EntryActions!.Single(value => value.Id == abstraction.TargetSpawnActionId).Prefab = prefab;
            if (completion is not null) completion.Text = Bounded(request.CompletionMessage, completion.Text ?? string.Empty, 500);
            derivation.TargetChoiceId = choice.Id;
            derivation.TargetRuntimeValue = choice.RuntimeTarget;
            derivation.Instructions = entry.Label;
            derivation.CompletionMessage = completion?.Text ?? string.Empty;
            derivation.Mechanic = abstraction.ConfigurablePractice ? mechanic : null;
            derivation.ConfigurationHash = HuntConfigurationHash(abstraction, project.Title, choice, entry.Label, completion?.Text, mechanic);
            return _workspace.SaveDraft(projectId, new StudioSaveRequest(project.Revision, project));
        }
    }

    public StudioSaveResult DetachProject(string projectId, StudioProjectDetachRequest? request)
    {
        var project = _workspace.ReadProject(projectId);
        if (project?.Derivation is null) return StudioSaveResult.Fail("project_derivation_missing");
        if (request is null || request.ExpectedRevision != project.Revision) return StudioSaveResult.RevisionConflict(project);
        project.Derivation.Detached = true;
        var saved = _workspace.SaveDraft(projectId, new StudioSaveRequest(project.Revision, project));
        if (saved.Ok) _portfolio.RemoveProjectFromCampaigns(project.Derivation.GuildId, projectId);
        return saved;
    }

    public StudioCampaignDocument? ReadCampaign(string guildId, string campaignId) => _portfolio.ReadCampaign(guildId, campaignId);
    public StudioCampaignMutationResult CreateCampaign(string guildId, StudioCampaignCreateRequest? request) => _portfolio.CreateCampaign(guildId, request);
    public StudioCampaignMutationResult SaveCampaign(string guildId, string campaignId, StudioCampaignSaveRequest? request) => _portfolio.SaveCampaign(guildId, campaignId, request);
    public StudioCampaignMutationResult PlaceInCampaign(string guildId, string campaignId, StudioCampaignPlacementRequest? request) =>
        _portfolio.PlaceInCampaign(guildId, campaignId, request, id => _workspace.ReadProject(id) is not null, id =>
        {
            var project = _workspace.ReadProject(id)!;
            return new StudioGuildArtifactMembership
            {
                ProjectId = id,
                Kind = request?.Kind ?? "quest",
                Creator = _portfolio.ReadGuild(guildId)?.Steward ?? Environment.UserName,
                AbstractionId = project.Derivation?.AbstractionId,
                AbstractionRevision = project.Derivation?.AbstractionRevision,
                RequiresGuildCompliance = project.Derivation is not null && !project.Derivation.Detached,
            };
        });
    public StudioGuildImportResult DuplicateGuild(string guildId, StudioGuildDuplicateRequest? request)
    {
        var source = _portfolio.ReadGuild(guildId);
        if (source is null) return new(false, "guild_missing", null, Array.Empty<StudioProjectDocument>());
        if (request is null || request.ExpectedRevision != source.Revision)
            return new(false, "revision_conflict", source, Array.Empty<StudioProjectDocument>());
        var sourceError = ValidateForkSource(source, sourceProjectId => _workspace.ReadProject(sourceProjectId));
        if (sourceError is not null) return new(false, sourceError, null, Array.Empty<StudioProjectDocument>());
        return ForkGuild(source, sourceProjectId => _workspace.Duplicate(sourceProjectId), "duplicate");
    }
    public StudioGuildImportResult ImportGuild(StudioGuildImportRequest? request)
    {
        var bundle = request?.Bundle;
        if (bundle is null || bundle.SchemaVersion is not (1 or 2) || bundle.Guild is null || bundle.Projects is null)
            return new(false, "guild_bundle_invalid", null, Array.Empty<StudioProjectDocument>());
        if (bundle.Projects.Count > QuestStudioPortfolioStore.MaxProjects || bundle.Projects.Any(value => value is null || string.IsNullOrWhiteSpace(value.ProjectId)))
            return new(false, "guild_bundle_invalid", null, Array.Empty<StudioProjectDocument>());
        var groups = bundle.Projects.GroupBy(value => value.ProjectId, StringComparer.Ordinal).ToArray();
        if (groups.Any(value => value.Count() != 1)) return new(false, "guild_bundle_project_duplicate", null, Array.Empty<StudioProjectDocument>());
        var sourceProjects = groups.ToDictionary(value => value.Key, value => value.Single(), StringComparer.Ordinal);
        var sourceError = ValidateForkSource(bundle.Guild, sourceProjectId => sourceProjects.TryGetValue(sourceProjectId, out var source) ? source : null);
        if (sourceError is not null) return new(false, sourceError, null, Array.Empty<StudioProjectDocument>());
        return ForkGuild(bundle.Guild, sourceProjectId =>
        {
            if (!sourceProjects.TryGetValue(sourceProjectId, out var source)) return null;
            using var json = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(source, _host.Json));
            return _workspace.Import(new StudioImportRequest(json.RootElement.Clone())).Project;
        }, "import");
    }

    string? ValidateForkSource(StudioGuildDocument source, Func<string, StudioProjectDocument?> findProject)
    {
        var guildError = _portfolio.ValidateForkSource(source);
        if (guildError is not null) return guildError;
        foreach (var sourceId in GuildProjectIds(source))
        {
            var project = findProject(sourceId);
            if (project is null) return "guild_project_missing";
            var projectError = _workspace.ValidateForkSource(project);
            if (projectError is not null) return projectError;
        }
        return null;
    }
    public StudioDownloadResult ExportGuild(string guildId)
    {
        var guild = _portfolio.ReadGuild(guildId);
        if (guild is null) return StudioDownloadResult.Fail("guild_missing");
        var projects = _portfolio.Placements().Where(value => value.GuildId == guildId)
            .Select(value => _workspace.ReadProject(value.ProjectId)).Where(value => value is not null)
            .Cast<StudioProjectDocument>().ToArray();
        var bundle = new StudioGuildBundleDocument(2, guild, projects);
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(bundle, _host.Json);
        return StudioDownloadResult.Success(guild.GuildId + ".questguild.json", "application/vnd.comfy.questguild+json", bytes);
    }
    public StudioGuildCertificationResult CertifyGuild(string guildId)
    {
        var guild = _portfolio.ReadGuild(guildId);
        var compiled = guild is null ? CompiledGuild.Fail("guild_missing") : CompileCampaign(guild, DefaultCampaign(guild));
        return compiled.Ok
            ? StudioGuildCertificationResult.Success(compiled.ContentHash!, compiled.Experiences!.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray())
            : StudioGuildCertificationResult.Fail(compiled.Error!, compiled.Diagnostics);
    }
    public async Task<StudioGuildPublishResult> PublishGuildAsync(string guildId, StudioPublishRequest? request, CancellationToken cancellationToken)
    {
        var guild = _portfolio.ReadGuild(guildId);
        if (guild is null) return StudioGuildPublishResult.Fail("guild_missing");
        if (request is null || request.ExpectedRevision != guild.Revision)
            return StudioGuildPublishResult.RevisionConflict(guild);
        var campaign = DefaultCampaign(guild);
        var compiled = CompileCampaign(guild, campaign);
        if (!compiled.Ok) return StudioGuildPublishResult.Fail(compiled.Error!, compiled.Diagnostics);
        var bytes = StudioGraphCompiler.BuildPack(campaign.PackId, campaign.Version, compiled.Experiences!, compiled.ContentHash!);
        await using var stream = new MemoryStream(bytes, writable: false);
        var receipt = await _publisher.PublishAsync(stream, $"{campaign.PackId}-{campaign.Version}.questpack", cancellationToken);
        if (receipt.Ok) StoreCompilationReceipt(guild, campaign, compiled, "publish", receipt);
        return receipt.Ok
            ? StudioGuildPublishResult.Success(receipt.Status, receipt, guild, compiled.ContentHash!, compiled.Experiences!.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray())
            : StudioGuildPublishResult.Fail(receipt.Error!, receipt.Diagnostics ?? Array.Empty<ContractDiagnostic>(), receipt);
    }

    public async Task<StudioGuildPublishResult> PlayGuildAsync(string guildId, StudioPublishRequest? request, CancellationToken cancellationToken)
    {
        var guild = _portfolio.ReadGuild(guildId);
        if (guild is null) return StudioGuildPublishResult.Fail("guild_missing");
        if (request is null || request.ExpectedRevision != guild.Revision)
            return StudioGuildPublishResult.RevisionConflict(guild);
        var valheim = _host.FindValheim();
        if (valheim is null) return StudioGuildPublishResult.Fail("valheim_not_found");
        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        var devStatus = await StudioDevChannelConnection.WaitForConnectedAsync(
            runtimeRoot, cancellationToken);
        if (devStatus is null) return StudioGuildPublishResult.Fail("dev_channel_disconnected");
        if (devStatus.Armed != true) return StudioGuildPublishResult.Fail("dev_channel_not_armed");
        var campaign = DefaultCampaign(guild);
        var compiled = CompileCampaign(guild, campaign);
        if (!compiled.Ok) return StudioGuildPublishResult.Fail(compiled.Error!, compiled.Diagnostics);
        var bytes = StudioGraphCompiler.BuildPack(campaign.PackId, campaign.Version, compiled.Experiences!, compiled.ContentHash!);
        await using var stream = new MemoryStream(bytes, writable: false);
        var shortHash = compiled.ContentHash!.Substring(0, 12);
        var filename = $"{campaign.PackId}-{campaign.Version}-r{campaign.Revision}-{shortHash}.questpack";
        var receipt = await _publisher.PublishDevAsync(stream, filename, cancellationToken);
        if (receipt.Ok) StoreCompilationReceipt(guild, campaign, compiled, "play", receipt);
        return receipt.Ok
            ? StudioGuildPublishResult.Success(receipt.Status, receipt, guild, compiled.ContentHash!, compiled.Experiences!.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray())
            : StudioGuildPublishResult.Fail(receipt.Error!, receipt.Diagnostics ?? Array.Empty<ContractDiagnostic>(), receipt);
    }

    public StudioCampaignCertificationResult CertifyCampaign(string guildId, string campaignId)
    {
        var guild = _portfolio.ReadGuild(guildId);
        if (guild is null) return new(false, "guild_missing", null, null, Array.Empty<string>(), null, Array.Empty<ContractDiagnostic>());
        var campaign = guild.Campaigns.FirstOrDefault(value => value.CampaignId == campaignId);
        if (campaign is null) return new(false, "campaign_missing", null, null, Array.Empty<string>(), null, Array.Empty<ContractDiagnostic>());
        var compiled = CompileCampaign(guild, campaign);
        if (!compiled.Ok) return new(false, compiled.Error, campaign, null, Array.Empty<string>(), null, compiled.Diagnostics);
        var proof = StoreCompilationReceipt(guild, campaign, compiled, "certify", null);
        return new(true, null, campaign, compiled.ContentHash, compiled.Experiences!.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), proof, Array.Empty<ContractDiagnostic>());
    }

    public async Task<StudioCampaignPublishResult> PublishCampaignAsync(string guildId, string campaignId, StudioCampaignPublishRequest? request, CancellationToken cancellationToken)
        => await PublishCampaignCoreAsync(guildId, campaignId, request, play: false, cancellationToken);

    public async Task<StudioCampaignPublishResult> PlayCampaignAsync(string guildId, string campaignId, StudioCampaignPublishRequest? request, CancellationToken cancellationToken)
    {
        var guild = _portfolio.ReadGuild(guildId);
        var campaign = guild?.Campaigns.FirstOrDefault(value => value.CampaignId == campaignId);
        if (guild is not null && campaign is not null && !IsSignatureHuntCampaign(guild, campaign)
            && CampaignArtifactsInAuthoredOrder(campaign).Any(artifact => guild.Artifacts.Any(member =>
                member.ProjectId == artifact.ProjectId && member.AbstractionId == "slayers-signature-hunt")))
            return CampaignFail("signature_hunt_campaign_shape_unsupported", guild, campaign);
        return guild is not null && campaign is not null && IsSignatureHuntCampaign(guild, campaign)
            ? await PlaySignatureHuntCampaignAsync(guild, campaign, request, cancellationToken)
            : await PublishCampaignCoreAsync(guildId, campaignId, request, play: true, cancellationToken);
    }

    async Task<StudioCampaignPublishResult> PlaySignatureHuntCampaignAsync(
        StudioGuildDocument guild, StudioCampaignDocument campaign,
        StudioCampaignPublishRequest? request, CancellationToken cancellationToken, string? expectedContentHash = null)
    {
        if (request is null || request.ExpectedRevision != campaign.Revision)
            return new(false, true, "conflict", "revision_conflict", guild, campaign, null, null,
                Array.Empty<string>(), null, Array.Empty<ContractDiagnostic>());

        // Freeze and validate the exact bytes before any process, install, or world side effect.
        var compiled = CompileCampaign(guild, campaign);
        if (!compiled.Ok) return CampaignFail(compiled.Error!, guild, campaign, compiled.Diagnostics);
        if (expectedContentHash is not null && compiled.ContentHash != expectedContentHash)
            return CampaignFail("campaign_revision_changed_during_reset", guild, campaign);
        var orderedArtifacts = CampaignArtifactsInAuthoredOrder(campaign).ToArray();
        var roots = orderedArtifacts.Where(value => (value.PrerequisiteProjectIds ?? new()).Count == 0).ToArray();
        if (roots.Length != 1)
            return CampaignFail("campaign_entry_ambiguous", guild, campaign, new[]
            {
                new ContractDiagnostic("campaign.entry_ambiguous", "$.artifacts",
                    "One-operation play requires exactly one authored artifact without prerequisites.")
            });
        var firstProject = _workspace.ReadProject(roots[0].ProjectId);
        if (firstProject is null) return CampaignFail("guild_project_missing", guild, campaign);
        var signatureProjects = orderedArtifacts.Select(value => _workspace.ReadProject(value.ProjectId)).ToArray();
        if (signatureProjects.Any(value => value is null))
            return CampaignFail("guild_project_missing", guild, campaign);
        if (!SignatureHuntTargetsMatch(guild, signatureProjects!))
            return CampaignFail("signature_hunt_fixture_target_mismatch", guild, campaign, new[]
            {
                new ContractDiagnostic("campaign.signature_hunt_fixture_target_mismatch", "$.artifacts",
                    "Each hunt needs a supported target and its matching stage-entry spawn. Use the palette revision with stage-owned targets.")
            });
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var next = firstProject.ExperienceId;
        while (next is not null && visited.Add(next))
        {
            var item = compiled.Lineage.Single(value => value.ExperienceId == next);
            if (item.SuccessorExperienceIds.Count > 1) break;
            next = item.SuccessorExperienceIds.SingleOrDefault();
        }
        if (next is not null || visited.Count != signatureProjects.Length)
            return CampaignFail("campaign_continuation_unproven", guild, campaign, new[]
            {
                new ContractDiagnostic("campaign.continuation_unproven", "$.artifacts",
                    "Play requires one linear sequence of Signature Hunts, with one successor per hunt and every hunt reachable.")
            });

        var operationId = "campaign-play-" + DateTimeOffset.UtcNow.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'")
            + "-" + Guid.NewGuid().ToString("N")[..8];
        var play = new StudioCampaignPlayReceipt
        {
            OperationId = operationId,
            StartedUtc = DateTimeOffset.UtcNow,
            GuildId = guild.GuildId,
            CampaignId = campaign.CampaignId,
            CampaignRevision = campaign.Revision,
            ContentHash = compiled.ContentHash!,
            FirstProjectId = firstProject.ProjectId,
            FirstExperienceId = firstProject.ExperienceId,
            Limitations = new()
            {
                "Quest Lab fixture evidence proves preparation only; it is not kill or completion proof.",
                "Runtime bind/start evidence proves the campaign began, not that a player completed it.",
                "Slayers community credit remains outside this local R&D proof.",
            },
        };
        AddPlayStage(play, "certify", "completed", compiled.ContentHash, "Exact campaign bytes frozen before machine mutation.");

        var prerequisites = await _campaignPlayPrerequisites.EnsureAsync(operationId, cancellationToken);
        if (!prerequisites.Ok || prerequisites.Value is null)
            return FailCampaignPlay(play, "prerequisites", prerequisites.Error ?? "campaign_play_prerequisites_failed", guild, campaign);
        var prepared = prerequisites.Value;
        play.CreatorSessionId = prepared.CreatorSessionId;
        play.Machine = prepared.Machine;
        play.WorldUid = prepared.WorldUid;
        play.FixtureRequestId = prepared.FixtureRequestId;
        play.FixturePreparationId = prepared.FixturePreparationId;
        play.FixtureReceiptPath = prepared.FixtureReceiptPath;
        play.FixtureReceiptSha256 = prepared.FixtureReceiptSha256;
        play.FixtureProofLevel = prepared.FixtureProofLevel;
        play.FixtureDisclaimer = prepared.FixtureDisclaimer;
        play.FixtureTargets = prepared.FixtureTargets.Select(value => new StudioCampaignFixtureTarget
        {
            Role = value.Role,
            MatcherTarget = value.MatcherTarget,
            ZdoId = value.ZdoId,
        }).ToList();
        play.BindingAnchorZdo = prepared.BindingAnchorZdo;
        AddPlayStage(play, prepared.ResumedRunningSession ? "resume" : "prepare_launch", "completed",
            prepared.CreatorSessionId, "Pinned Creator Session was validated and entered ComfyQuestDemo.");

        var valheim = _host.FindValheim();
        if (valheim is null) return FailCampaignPlay(play, "activation", "valheim_not_found", guild, campaign);
        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        var devStatus = await StudioDevChannelConnection.WaitForConnectedAsync(runtimeRoot, cancellationToken);
        if (devStatus is null) return FailCampaignPlay(play, "arm", "dev_channel_disconnected", guild, campaign);
        if (devStatus.Armed != true) return FailCampaignPlay(play, "arm", "dev_channel_not_armed", guild, campaign);
        AddPlayStage(play, "arm", "completed", devStatus.SessionId, "Runtime heartbeat is fresh and armed before transfer.");
        AddPlayStage(play, "fixture_prepare", "completed", prepared.FixtureRequestId, prepared.FixtureProofLevel);

        var bytes = StudioGraphCompiler.BuildPack(campaign.PackId, campaign.Version, compiled.Experiences!, compiled.ContentHash!);
        await using var stream = new MemoryStream(bytes, writable: false);
        var filename = "campaign-" + compiled.ContentHash![..12] + "-r" + campaign.Revision + "-"
            + operationId[^8..] + ".questpack";
        var publication = await _publisher.PublishDevAsync(stream, filename, cancellationToken);
        if (!publication.Ok)
            return FailCampaignPlay(play, "publish", publication.Error ?? "publication_failed", guild, campaign, publication.Diagnostics, publication);
        play.PackageSha256 = publication.PackageSha256;
        AddPlayStage(play, "publish", "completed", publication.PackageSha256, publication.Filename);

        var activation = await WaitForExactActivationAsync(runtimeRoot, publication, compiled.ContentHash!, cancellationToken);
        if (activation.Active is null)
            return FailCampaignPlay(play, "activation", activation.Error ?? "campaign_activation_timeout", guild, campaign, publication: publication);
        play.ActivationId = activation.Active.ActivationId;
        AddPlayStage(play, "activation", "completed", activation.Active.ActivationId,
            activation.Active.PackId + "@" + activation.Active.Version);

        var runtimeIdentity = new StudioRuntimeIdentity(prepared.Machine, prepared.WorldUid, prepared.CreatorSessionId);
        var connectionError = await WaitForRunControlConnectionAsync(firstProject.ProjectId, runtimeIdentity, cancellationToken);
        if (connectionError is not null)
            return FailCampaignPlay(play, "binding_candidates", connectionError, guild, campaign, publication: publication);
        var candidates = await CompletePackControlAsync(firstProject.ProjectId,
            await _runControl.BindingCandidatesPinnedAsync(firstProject.ProjectId, runtimeIdentity, cancellationToken),
            runtimeIdentity, "list_binding_candidates", cancellationToken);
        play.CandidateRequestId = candidates.RequestId;
        if (!candidates.Ok || candidates.Receipt?.BindingCandidates is null)
            return FailCampaignPlay(play, "binding_candidates", candidates.Error ?? "binding_candidates_failed", guild, campaign, publication: publication);
        var anchor = candidates.Receipt.BindingCandidates
            .Where(value => value.BindingZdo == prepared.BindingAnchorZdo && value.TargetKind == "sign").ToArray();
        if (anchor.Length != 1)
            return FailCampaignPlay(play, "binding_candidates", "signature_hunt_binding_anchor_missing", guild, campaign, publication: publication);
        AddPlayStage(play, "binding_candidates", "completed", candidates.RequestId,
            "Exact fixture-owned sign is present in Runtime's bounded candidate set.");

        var bound = await CompletePackControlAsync(firstProject.ProjectId,
            await _runControl.BindExperiencePinnedAsync(firstProject.ProjectId,
                new StudioBindExperienceRequest(firstProject.ExperienceId, prepared.BindingAnchorZdo), runtimeIdentity, cancellationToken),
            runtimeIdentity, "bind_selected_experience", cancellationToken);
        play.BindRequestId = bound.RequestId;
        var change = bound.Receipt?.BindingChange;
        if (!bound.Ok)
            return FailCampaignPlay(play, "bind_start", bound.Error ?? "campaign_bind_start_failed", guild, campaign, publication: publication);
        if (!ExactAppliedBinding(change, prepared, publication, compiled.ContentHash!, firstProject.ExperienceId))
            return FailCampaignPlay(play, "bind_start", "campaign_bind_start_evidence_mismatch", guild, campaign, publication: publication);
        play.BindingChangeId = change!.ChangeId;
        play.BindingInstanceId = change.Applied.BindingInstanceId;
        AddPlayStage(play, "bind_start", "completed", bound.RequestId,
            "Runtime atomically selected, bound, and started " + firstProject.ExperienceId + ".");

        var proof = StoreCompilationReceipt(guild, campaign, compiled, "play", publication);
        play.State = "started";
        play.CompletedUtc = DateTimeOffset.UtcNow;
        StoreCampaignPlayReceipt(play);
        return new(true, false, "started", null, guild, campaign, publication, compiled.ContentHash,
            compiled.Experiences!.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), proof,
            Array.Empty<ContractDiagnostic>()) { PlayReceipt = play };
    }

    static IEnumerable<StudioGuildArtifact> CampaignArtifactsInAuthoredOrder(StudioCampaignDocument campaign) =>
        campaign.Questlines.SelectMany(value => value.Quests)
            .Concat(campaign.StandaloneQuests)
            .Concat(campaign.Events);

    static bool IsSignatureHuntCampaign(StudioGuildDocument guild, StudioCampaignDocument campaign)
    {
        var projectIds = CampaignArtifactsInAuthoredOrder(campaign)
            .Select(value => value.ProjectId).Distinct(StringComparer.Ordinal).ToArray();
        return projectIds.Length is >= 1 and <= 16 && projectIds.All(projectId => guild.Artifacts.Any(value =>
            value.ProjectId == projectId
            && value.RequiresGuildCompliance
            && value.AbstractionId == "slayers-signature-hunt"));
    }

    static bool SignatureHuntTargetsMatch(
        StudioGuildDocument guild, IEnumerable<StudioProjectDocument?> projects)
    {
        var count = 0;
        foreach (var project in projects)
        {
            var derivation = project?.Derivation;
            var abstraction = derivation is null ? null : guild.Abstractions.SingleOrDefault(value =>
                value.AbstractionId == derivation.AbstractionId
                && value.Revision == derivation.AbstractionRevision
                && value.ContentHash == derivation.AbstractionHash);
            var route = abstraction is null ? null : project!.Nodes.SelectMany(value => value.Routes)
                .SingleOrDefault(value => value.Id == abstraction.RouteId);
            if (route?.Event != "kill" || route.Target != derivation!.TargetRuntimeValue
                || !HuntMechanicMatches(abstraction!, derivation.Mechanic, route))
                return false;
            if (SignatureHuntPrefab(route.Target) is not { } prefab || abstraction!.TargetSpawnActionId is null) return false;
            var spawn = project!.Nodes.SingleOrDefault(value => value.Id == abstraction.EntryNodeId)?.EntryActions?
                .SingleOrDefault(value => value.Id == abstraction.TargetSpawnActionId);
            if (spawn?.Type != "spawn" || spawn.Kind != "creature" || spawn.Count != 1 || spawn.Prefab != prefab) return false;
            count++;
        }
        return count is >= 1 and <= 16;
    }

    static string? SignatureHuntPrefab(string? target) => target switch
    {
        "$enemy_greyling" => "Greyling",
        "$enemy_boar" => "Boar",
        "$enemy_draugr" => "Draugr",
        "$enemy_deathsquito" => "Deathsquito",
        "$enemy_drake" => "Hatchling",
        "$enemy_lox" => "Lox",
        _ => null
    };

    static bool HuntMechanicAllowed(StudioGuildAbstractionDocument abstraction, string mechanic) =>
        mechanic == "thrown_spear" || abstraction.ConfigurablePractice && mechanic == "melee";

    static string HuntConfigurationHash(StudioGuildAbstractionDocument abstraction, string title,
        StudioAbstractionTargetChoice choice, string instructions, string? completion, string mechanic) =>
        abstraction.ConfigurablePractice
            ? StudioCreativeHash.Parts(title, choice.Id, choice.RuntimeTarget, instructions, completion, mechanic)
            : StudioCreativeHash.Parts(title, choice.Id, choice.RuntimeTarget, instructions, completion);

    static void ApplyHuntMechanic(StudioRoute route, string mechanic)
    {
        route.Where["projectile"] = mechanic == "melee" ? "false" : "true";
        if (mechanic == "melee") route.Where.Remove("weapon_skill");
        else route.Where["weapon_skill"] = "Spears";
    }

    static bool HuntMechanicMatches(StudioGuildAbstractionDocument abstraction, string? mechanic, StudioRoute route)
    {
        mechanic ??= "thrown_spear";
        return HuntMechanicAllowed(abstraction, mechanic)
            && route.Where.TryGetValue("projectile", out var projectile)
            && (mechanic == "melee" ? projectile == "false" && !route.Where.ContainsKey("weapon_skill")
                : projectile == "true" && route.Where.TryGetValue("weapon_skill", out var skill) && skill == "Spears");
    }

    static bool ExactAppliedBinding(
        RuntimeBindingChange? change, StudioCampaignPlayPrerequisites prepared,
        QuestPackPublishReceipt publication, string contentHash, string experienceId) =>
        change is not null
        && change.Schema == RuntimeBindingChange.CurrentSchema
        && change.State == "applied"
        && change.BindingZdo == prepared.BindingAnchorZdo
        && (string.IsNullOrWhiteSpace(change.ResolvedBindingZdo)
            || change.ResolvedBindingZdo == prepared.BindingAnchorZdo)
        && change.WorldId == prepared.WorldUid
        && change.Applied is not null
        && change.Applied.PackId == publication.PackId
        && change.Applied.Version == publication.Version
        && change.Applied.ExperienceId == experienceId
        && change.Applied.BindingId == "default"
        && string.Equals(change.Applied.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(change.Applied.BindingInstanceId);

    async Task<(ActiveSet? Active, string? Error)> WaitForExactActivationAsync(
        string runtimeRoot, QuestPackPublishReceipt publication, string contentHash,
        CancellationToken cancellationToken)
    {
        var store = new QuestPackStore(runtimeRoot);
        var statusStore = new RuntimeDevChannelStatusStore(runtimeRoot);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        string? lastRejection = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = statusStore.Read();
            lastRejection = status?.LastRejection ?? lastRejection;
            ActiveSet? active = null;
            try { active = store.ReadActive(); } catch { }
            var now = DateTimeOffset.UtcNow;
            if (StudioDevChannelConnection.IsConnected(status, now)
                && status?.Armed == true
                && active?.SourceChannel == "dev"
                && active.Source == publication.Filename
                && active.PackId == publication.PackId
                && active.Version == publication.Version
                && string.Equals(active.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
                && string.Equals(active.PackageSha256, publication.PackageSha256, StringComparison.OrdinalIgnoreCase)
                && status.ActiveActivationId == active.ActivationId
                && status.ActivePackId == active.PackId
                && status.ActiveVersion == active.Version
                && string.Equals(status.ActiveContentHash, active.ContentHash, StringComparison.OrdinalIgnoreCase))
                return (active, null);
            await Task.Delay(250, cancellationToken);
        }
        return (null, string.IsNullOrWhiteSpace(lastRejection)
            ? "campaign_activation_timeout"
            : "campaign_activation_rejected:" + lastRejection);
    }

    async Task<string?> WaitForRunControlConnectionAsync(
        string projectId, StudioRuntimeIdentity expectedIdentity, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = _runControl.Status(projectId);
            if (status.Available && status.Connected)
                return string.Equals(status.Machine, expectedIdentity.Machine, StringComparison.OrdinalIgnoreCase)
                       && status.WorldUid == expectedIdentity.WorldUid
                    ? null
                    : "campaign_runtime_identity_changed";
            await Task.Delay(200, cancellationToken);
        }
        return "runtime_run_status_stale";
    }

    async Task<StudioRunControlResult> CompletePackControlAsync(
        string projectId, StudioRunControlResult initial, StudioRuntimeIdentity identity,
        string operation, CancellationToken cancellationToken)
    {
        if (!initial.Ok || !initial.Queued || string.IsNullOrWhiteSpace(initial.RequestId)) return initial;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(200, cancellationToken);
            var result = _runControl.ReceiptPinned(projectId, initial.RequestId, null, identity, operation);
            if (!result.Queued) return result;
        }
        return new(false, false, "run_control_receipt_timeout", null, initial.RequestId);
    }

    static void AddPlayStage(StudioCampaignPlayReceipt play, string stage, string state,
        string? evidenceId, string? detail) => play.Stages.Add(new()
    {
        Stage = stage,
        State = state,
        AtUtc = DateTimeOffset.UtcNow,
        EvidenceId = evidenceId,
        Detail = detail,
    });

    StudioCampaignPublishResult FailCampaignPlay(
        StudioCampaignPlayReceipt play, string stage, string error,
        StudioGuildDocument guild, StudioCampaignDocument campaign,
        IReadOnlyList<ContractDiagnostic>? diagnostics = null,
        QuestPackPublishReceipt? publication = null)
    {
        play.State = "failed";
        play.FailedStage = stage;
        play.Error = error;
        play.CompletedUtc = DateTimeOffset.UtcNow;
        AddPlayStage(play, stage, "failed", null, error);
        StoreCampaignPlayReceipt(play);
        return CampaignFail(error, guild, campaign, diagnostics, publication) with { PlayReceipt = play };
    }

    void StoreCampaignPlayReceipt(StudioCampaignPlayReceipt receipt)
    {
        var directory = Path.Combine(_creativeEvidenceRoot, receipt.GuildId, receipt.CampaignId);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "play-" + receipt.OperationId + ".json");
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(receipt, _host.Json));
        try { File.Move(temporary, target); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        foreach (var stale in Directory.GetFiles(directory, "play-*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc).ThenByDescending(value => value, StringComparer.Ordinal).Skip(64))
            File.Delete(stale);
    }

    async Task<StudioCampaignPublishResult> PublishCampaignCoreAsync(string guildId, string campaignId, StudioCampaignPublishRequest? request, bool play, CancellationToken cancellationToken)
    {
        var guild = _portfolio.ReadGuild(guildId);
        if (guild is null) return CampaignFail("guild_missing");
        var campaign = guild.Campaigns.FirstOrDefault(value => value.CampaignId == campaignId);
        if (campaign is null) return CampaignFail("campaign_missing", guild: guild);
        if (request is null || request.ExpectedRevision != campaign.Revision)
            return new(false, true, "conflict", "revision_conflict", guild, campaign, null, null, Array.Empty<string>(), null, Array.Empty<ContractDiagnostic>());
        if (play)
        {
            var valheim = _host.FindValheim();
            if (valheim is null) return CampaignFail("valheim_not_found", guild, campaign);
            var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
            var devStatus = await StudioDevChannelConnection.WaitForConnectedAsync(runtimeRoot, cancellationToken);
            if (devStatus is null) return CampaignFail("dev_channel_disconnected", guild, campaign);
            if (devStatus.Armed != true) return CampaignFail("dev_channel_not_armed", guild, campaign);
        }
        var compiled = CompileCampaign(guild, campaign);
        if (!compiled.Ok) return CampaignFail(compiled.Error!, guild, campaign, compiled.Diagnostics);
        var bytes = StudioGraphCompiler.BuildPack(campaign.PackId, campaign.Version, compiled.Experiences!, compiled.ContentHash!);
        await using var stream = new MemoryStream(bytes, writable: false);
        var filename = play
            ? $"{campaign.PackId}-{campaign.Version}-r{campaign.Revision}-{compiled.ContentHash![..12]}.questpack"
            : $"{campaign.PackId}-{campaign.Version}.questpack";
        var publication = play
            ? await _publisher.PublishDevAsync(stream, filename, cancellationToken)
            : await _publisher.PublishAsync(stream, filename, cancellationToken);
        if (!publication.Ok) return CampaignFail(publication.Error ?? "publication_failed", guild, campaign, publication.Diagnostics, publication);
        var proof = StoreCompilationReceipt(guild, campaign, compiled, play ? "play" : "publish", publication);
        return new(true, false, publication.Status, null, guild, campaign, publication, compiled.ContentHash,
            compiled.Experiences!.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), proof, Array.Empty<ContractDiagnostic>());
    }

    static StudioCampaignPublishResult CampaignFail(string error, StudioGuildDocument? guild = null, StudioCampaignDocument? campaign = null,
        IReadOnlyList<ContractDiagnostic>? diagnostics = null, QuestPackPublishReceipt? publication = null) =>
        new(false, false, "rejected", error, guild, campaign, publication, null, Array.Empty<string>(), null, diagnostics ?? Array.Empty<ContractDiagnostic>());

    CompiledGuild CompileCampaign(StudioGuildDocument guild, StudioCampaignDocument campaign)
    {
        var artifacts = CampaignArtifactsInAuthoredOrder(campaign).ToArray();
        if (artifacts.Length == 0) return CompiledGuild.Fail("campaign_empty",
            new[] { new ContractDiagnostic("campaign.empty", "$.artifacts", "A campaign requires at least one quest or event.") });
        var diagnostics = new List<ContractDiagnostic>();
        var projects = new Dictionary<string, StudioProjectDocument>(StringComparer.Ordinal);
        foreach (var artifact in artifacts.OrderBy(value => value.ProjectId, StringComparer.Ordinal))
        {
            var project = _workspace.ReadProject(artifact.ProjectId);
            if (project is null)
            {
                diagnostics.Add(new("guild.project_missing", "$.artifacts." + artifact.ProjectId, "The assigned Studio project does not exist."));
                continue;
            }
            projects[artifact.ProjectId] = project;
            var membership = guild.Artifacts.FirstOrDefault(value => value.ProjectId == artifact.ProjectId);
            if (membership?.RequiresGuildCompliance == true)
            {
                var compliance = ValidateAbstractionCompliance(guild, project, membership);
                if (compliance is not null)
                    diagnostics.Add(new(compliance, "$.projects." + artifact.ProjectId + ".derivation", "The project no longer matches its published Guild abstraction. Detach it or restore the locked fields."));
            }
        }
        if (diagnostics.Count > 0) return CompiledGuild.Fail("guild_project_missing", diagnostics);
        var duplicateExperiences = projects.Values.GroupBy(value => value.ExperienceId, StringComparer.Ordinal)
            .Where(group => group.Count() != 1).ToArray();
        foreach (var duplicate in duplicateExperiences)
            diagnostics.Add(new("guild.experience_duplicate", "$.experiences." + duplicate.Key, "Every project in a guild must compile to a unique experience id."));
        if (diagnostics.Count > 0) return CompiledGuild.Fail("guild_experience_duplicate", diagnostics);

        var experienceByProject = projects.ToDictionary(value => value.Key, value => value.Value.ExperienceId, StringComparer.Ordinal);
        var successorsByProject = artifacts.ToDictionary(
            source => source.ProjectId,
            source => artifacts.Where(candidate => (candidate.PrerequisiteProjectIds ?? new()).Contains(
                    source.ProjectId, StringComparer.Ordinal))
                .Select(candidate => candidate.ProjectId).ToList(),
            StringComparer.Ordinal);
        var experiences = new Dictionary<string, string>(StringComparer.Ordinal);
        var lineage = new List<StudioCampaignLineageEntry>();
        foreach (var artifact in artifacts.OrderBy(value => value.ProjectId, StringComparer.Ordinal))
        {
            var project = projects[artifact.ProjectId];
            var certification = StudioGraphCompiler.Compile(project);
            if (!certification.Ok)
            {
                diagnostics.AddRange(certification.Diagnostics.Select(value => new ContractDiagnostic(
                    value.Code, "$.projects." + artifact.ProjectId + value.Path.TrimStart('$'), value.Message)));
                continue;
            }
            var prerequisites = artifact.PrerequisiteProjectIds ?? new List<string>();
            var missing = prerequisites.Where(value => !experienceByProject.ContainsKey(value)).ToArray();
            foreach (var prerequisite in missing)
                diagnostics.Add(new("guild.prerequisite_missing", "$.artifacts." + artifact.ProjectId + ".prerequisite_project_ids", "Prerequisite project '" + prerequisite + "' is not part of this guild."));
            if (missing.Length > 0) continue;
            certification.Document!.Prerequisites = prerequisites.Count == 0
                ? null
                : prerequisites.Select(value => experienceByProject[value]).OrderBy(value => value, StringComparer.Ordinal).ToList();
            var successors = successorsByProject.TryGetValue(artifact.ProjectId, out var authoredSuccessors)
                ? authoredSuccessors.Where(experienceByProject.ContainsKey).Select(value => experienceByProject[value]).ToList()
                : new List<string>();
            var compiledJson = Newtonsoft.Json.Linq.JObject.FromObject(certification.Document);
            if (successors.Count > 0)
                compiledJson["successor_experience_ids"] = new Newtonsoft.Json.Linq.JArray(successors);
            var json = compiledJson.ToString(Formatting.Indented);
            var contract = ExperienceCompiler.CompileProductionJson(json);
            diagnostics.AddRange(contract.Diagnostics.Select(value => new ContractDiagnostic(
                value.Code, "$.projects." + artifact.ProjectId + value.Path.TrimStart('$'), value.Message)));
            if (contract.IsValid)
            {
                experiences[project.ExperienceId] = json;
                lineage.Add(new StudioCampaignLineageEntry
                {
                    ProjectId = project.ProjectId,
                    ProjectRevision = project.Revision,
                    ExperienceId = project.ExperienceId,
                    AbstractionId = project.Derivation?.AbstractionId,
                    AbstractionRevision = project.Derivation?.AbstractionRevision,
                    AbstractionHash = project.Derivation?.AbstractionHash,
                    ConfigurationHash = project.Derivation?.ConfigurationHash,
                    SuccessorExperienceIds = successors,
                });
            }
        }
        if (diagnostics.Count > 0) return CompiledGuild.Fail("guild_graph_invalid", diagnostics);
        var entries = experiences.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new KeyValuePair<string, byte[]>("experiences/" + value.Key + ".json", Encoding.UTF8.GetBytes(value.Value)));
        return CompiledGuild.Success(experiences, QuestPackContent.ComputeHash(entries), lineage);
    }

    static StudioCampaignDocument DefaultCampaign(StudioGuildDocument guild) =>
        guild.Campaigns.FirstOrDefault(value => value.CampaignId == "campaign-default") ?? guild.Campaigns.First();

    string? ValidateAbstractionCompliance(StudioGuildDocument guild, StudioProjectDocument project, StudioGuildArtifactMembership membership)
    {
        var derivation = project.Derivation;
        if (derivation is null) return "abstraction_derivation_missing";
        if (derivation.Detached) return "abstraction_project_detached";
        if (derivation.GuildId != guild.GuildId || derivation.AbstractionId != membership.AbstractionId
            || derivation.AbstractionRevision != membership.AbstractionRevision)
            return "abstraction_derivation_identity_mismatch";
        var abstraction = guild.Abstractions.SingleOrDefault(value => value.AbstractionId == derivation.AbstractionId
            && value.Revision == derivation.AbstractionRevision && value.ContentHash == derivation.AbstractionHash);
        if (abstraction is null) return "abstraction_revision_missing";
        if (abstraction.SourceSnapshotHash != derivation.SourceSnapshotHash) return "abstraction_source_mismatch";
        if (abstraction.ConfigurablePractice && !HuntMechanicMatches(abstraction, derivation.Mechanic,
                project.Nodes.SelectMany(node => node.Routes).Single(route => route.Id == abstraction.RouteId)))
            return "hunt_mechanic_configuration_mismatch";
        return InvariantHash(project, abstraction) == abstraction.InvariantHash ? null : "abstraction_invariant_changed";
    }

    StudioCampaignCompilationReceipt StoreCompilationReceipt(StudioGuildDocument guild, StudioCampaignDocument campaign,
        CompiledGuild compiled, string operation, QuestPackPublishReceipt? publication)
    {
        var now = DateTimeOffset.UtcNow;
        var receipt = new StudioCampaignCompilationReceipt
        {
            ReceiptId = "campaign-" + now.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'") + "-" + Guid.NewGuid().ToString("N")[..12],
            Operation = operation,
            CreatedUtc = now,
            GuildId = guild.GuildId,
            GuildRevision = guild.Revision,
            CampaignId = campaign.CampaignId,
            CampaignRevision = campaign.Revision,
            PackId = campaign.PackId,
            PackVersion = campaign.Version,
            ContentHash = compiled.ContentHash!,
            Experiences = compiled.Lineage.ToList(),
            PublicationStatus = publication?.Status,
            PackageSha256 = publication?.PackageSha256,
        };
        var directory = Path.Combine(_creativeEvidenceRoot, guild.GuildId, campaign.CampaignId);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, receipt.ReceiptId + ".json");
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(receipt, _host.Json));
        try { File.Move(temporary, target); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return receipt;
    }

    public object? CampaignEvidence(string guildId, string campaignId)
    {
        var guild = _portfolio.ReadGuild(guildId);
        var campaign = guild?.Campaigns.FirstOrDefault(value => value.CampaignId == campaignId);
        if (guild is null || campaign is null) return null;
        var projectIds = campaign.StandaloneQuests.Concat(campaign.Events)
            .Concat(campaign.Questlines.SelectMany(value => value.Quests))
            .Select(value => value.ProjectId).Distinct(StringComparer.Ordinal).ToArray();
        var instances = projectIds.Select(projectId =>
        {
            var project = _workspace.ReadProject(projectId);
            var membership = guild.Artifacts.FirstOrDefault(value => value.ProjectId == projectId);
            StudioRunStatusView? runtime = null;
            try { runtime = project is null ? null : _runControl.Status(projectId); }
            catch { }
            return new { membership, project, runtime };
        }).ToArray();
        var receiptRoot = Path.Combine(_creativeEvidenceRoot, guildId, campaignId);
        var receipts = Directory.Exists(receiptRoot)
            ? Directory.GetFiles(receiptRoot, "campaign-*.json").OrderBy(value => value, StringComparer.Ordinal)
                .Select(path => ReadCompilationReceipt(path)).Where(value => value is not null).Cast<StudioCampaignCompilationReceipt>().ToArray()
            : Array.Empty<StudioCampaignCompilationReceipt>();
        var playReceipts = Directory.Exists(receiptRoot)
            ? Directory.GetFiles(receiptRoot, "play-*.json").OrderBy(value => value, StringComparer.Ordinal)
                .Select(path => ReadCampaignPlayReceipt(path)).Where(value => value is not null).Cast<StudioCampaignPlayReceipt>().ToArray()
            : Array.Empty<StudioCampaignPlayReceipt>();
        var abstractionKeys = instances.Where(value => value.project?.Derivation is not null)
            .Select(value => value.project!.Derivation!.AbstractionId + "@" + value.project.Derivation.AbstractionRevision)
            .ToHashSet(StringComparer.Ordinal);
        var abstractions = guild.Abstractions.Where(value => abstractionKeys.Contains(value.AbstractionId + "@" + value.Revision)).ToArray();
        var sourceIds = abstractions.Select(value => value.SourceSnapshotId).ToHashSet(StringComparer.Ordinal);
        return new
        {
            schema_version = 1,
            guild = new { guild_id = guild.GuildId, revision = guild.Revision, title = guild.Title, steward = guild.Steward },
            campaign,
            source_snapshots = guild.SourceSnapshots.Where(value => sourceIds.Contains(value.SourceSnapshotId)).ToArray(),
            abstractions,
            instances,
            compilation_receipts = receipts,
            play_receipts = playReceipts,
        };
    }

    StudioCampaignCompilationReceipt? ReadCompilationReceipt(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Length is > 0 and <= 1024 * 1024
                ? System.Text.Json.JsonSerializer.Deserialize<StudioCampaignCompilationReceipt>(File.ReadAllText(path), _host.Json)
                : null;
        }
        catch { return null; }
    }

    StudioCampaignPlayReceipt? ReadCampaignPlayReceipt(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var value = info.Length is > 0 and <= 1024 * 1024
                ? System.Text.Json.JsonSerializer.Deserialize<StudioCampaignPlayReceipt>(File.ReadAllText(path), _host.Json)
                : null;
            return value?.Schema == "comfy-quest-studio-campaign-play/v1" ? value : null;
        }
        catch { return null; }
    }
    public StudioRunStatusView RunStatus(string projectId) => _runControl.Status(projectId);
    public Task<StudioRunControlResult> PreviewResetAsync(string projectId, StudioRunResetRequest? request, CancellationToken cancellationToken) =>
        _runControl.PreviewAsync(projectId, request, cancellationToken);
    public Task<StudioRunControlResult> ApplyResetAsync(string projectId, StudioRunResetRequest? request, CancellationToken cancellationToken) =>
        _runControl.ApplyAsync(projectId, request, cancellationToken);
    public Task<StudioRunControlResult> PreviewRetireAsync(string projectId, StudioRunRetireRequest? request, CancellationToken cancellationToken) =>
        _runControl.PreviewRetireAsync(projectId, request, cancellationToken);
    public Task<StudioRunControlResult> ApplyRetireAsync(string projectId, StudioRunRetireRequest? request, CancellationToken cancellationToken) =>
        _runControl.ApplyRetireAsync(projectId, request, cancellationToken);
    public Task<StudioRunControlResult> SelectExperienceAsync(string projectId, StudioSelectExperienceRequest? request, CancellationToken cancellationToken) =>
        _runControl.SelectExperienceAsync(projectId, request, cancellationToken);
    public Task<StudioRunControlResult> BindingCandidatesAsync(string projectId, CancellationToken cancellationToken) =>
        _runControl.BindingCandidatesAsync(projectId, cancellationToken);
    public Task<StudioRunControlResult> BindExperienceAsync(string projectId, StudioBindExperienceRequest? request, CancellationToken cancellationToken) =>
        _runControl.BindExperienceAsync(projectId, request, cancellationToken);
    public Task<StudioRunControlResult> RestoreBindingAsync(string projectId, StudioRestoreBindingRequest? request, CancellationToken cancellationToken) =>
        _runControl.RestoreBindingAsync(projectId, request, cancellationToken);
    public StudioRunControlResult RunControlReceipt(string projectId, string? requestId, string? runId) =>
        _runControl.Receipt(projectId, requestId, runId);

    StudioGuildImportResult ForkGuild(StudioGuildDocument source, Func<string, StudioProjectDocument?> forkProject, string origin)
    {
        var sourceIds = GuildProjectIds(source);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var projects = new List<StudioProjectDocument>();
        foreach (var sourceId in sourceIds)
        {
            var project = forkProject(sourceId);
            if (project is null) return new(false, "guild_project_missing", null, projects);
            map[sourceId] = project.ProjectId;
            projects.Add(project);
        }
        try
        {
            var guild = _portfolio.CreateFork(source, map, origin);
            foreach (var project in projects.Where(value => value.Derivation is not null))
            {
                project.Derivation!.GuildId = guild.GuildId;
                var saved = _workspace.SaveDraft(project.ProjectId, new StudioSaveRequest(project.Revision, project));
                if (!saved.Ok) return new(false, saved.Error ?? "guild_project_derivation_update_failed", guild, projects);
            }
            return new(true, null, guild, projects);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            return new(false, exception.Message, null, projects);
        }
    }
    public IReadOnlyList<StudioProjectSummary> ListProjects() => _workspace.ListProjects();
    public StudioProjectDocument? ReadProject(string projectId) => _workspace.ReadProject(projectId);
    public StudioProjectDocument CreateProject(string? templateId)
    {
        var project = _workspace.CreateProject(templateId);
        var effectiveTemplate = templateId is "demo-world-first-portal" or "guild-journey" or "signal-circuit" or "cooperative-ritual" or "reward-cleanup" or "desperate-defense" ? templateId : "blank";
        _usage.RecordProject("create", "accepted", project, effectiveTemplate);
        return project;
    }
    public StudioImportResult ImportProject(StudioImportRequest? request)
    {
        var result = _workspace.Import(request);
        _usage.RecordProject("import", result.Ok ? "accepted" : "rejected", result.Project);
        return result;
    }
    public StudioSpatialAnchorImportResult ImportSpatialAnchor(string projectId, StudioSpatialAnchorImportRequest? request)
    {
        var result = _workspace.ImportSpatialAnchor(projectId, request);
        _usage.RecordOutcome("spatial_anchor_import", UsageOutcome(result.Ok, result.Conflict, result.Error));
        return result;
    }
    public StudioProjectDocument? DuplicateProject(string projectId)
    {
        var project = _workspace.Duplicate(projectId);
        _usage.RecordProject("duplicate", project is null ? "missing" : "accepted", project);
        return project;
    }
    public StudioSaveResult SaveDraft(string projectId, StudioSaveRequest? request)
    {
        var before = _workspace.ReadProject(projectId);
        var result = _workspace.SaveDraft(projectId, request);
        _usage.RecordSave(UsageOutcome(result.Ok, result.Conflict, result.Error), before, result.Project);
        return result;
    }
    public StudioSaveResult BumpPatch(string projectId, int expectedRevision)
    {
        var result = _workspace.BumpPatch(projectId, expectedRevision);
        _usage.RecordOutcome("bump_patch", UsageOutcome(result.Ok, result.Conflict, result.Error));
        return result;
    }
    public StudioCertificationResult ValidateGraph(string projectId)
    {
        var result = _workspace.Validate(projectId);
        _usage.RecordOutcome("validate", UsageOutcome(result.Ok, false, result.Error));
        return result;
    }
    public StudioCertificationResult CertifyGraph(string projectId)
    {
        var result = _workspace.Certify(projectId);
        _usage.RecordOutcome("certify", UsageOutcome(result.Ok, false, result.Error));
        return result;
    }
    public async Task<StudioPublishResult> PublishGraphAsync(string projectId, int expectedRevision, CancellationToken cancellationToken)
    {
        var result = await _workspace.PublishAsync(projectId, expectedRevision, cancellationToken);
        _usage.RecordCheckpoint("publish", UsageOutcome(result.Ok, result.Conflict, result.Error), result.Ok ? result.Project : null);
        return result;
    }
    public async Task<StudioPublishResult> PlayRevisionAsync(string projectId, int expectedRevision, CancellationToken cancellationToken)
    {
        var result = await _workspace.PlayRevisionAsync(projectId, expectedRevision, cancellationToken);
        _usage.RecordCheckpoint("play_revision", UsageOutcome(result.Ok, result.Conflict, result.Error), result.Ok ? result.Project : null);
        return result;
    }
    public StudioRehearsalResult Rehearse(string projectId, StudioRehearsalRequest? request)
    {
        var result = _workspace.Rehearse(projectId, request);
        _usage.RecordCheckpoint("rehearse", UsageOutcome(result.Ok, false, result.Error), result.Ok ? _workspace.ReadProject(projectId) : null);
        return result;
    }
    public StudioRuntimeStatus RuntimeStatus(string projectId) =>
        _workspace.RuntimeStatus(projectId, RuntimePackIdentity(projectId), _runControl.Status(projectId));
    public StudioRuntimeStatusView RuntimeStatusView(string projectId)
    {
        var status = RuntimeStatus(projectId);
        var active = status.ActiveSet;
        return new StudioRuntimeStatusView(status.SchemaVersion, status.Available, status.Phase, status.NextInstruction,
            status.ContentHash, status.PackageSha256,
            active?.PackId, active?.Version, active?.ContentHash, active?.ActivationId, active?.ActivatedUtc,
            status.ActiveRelation, status.CurrentStageId, status.CurrentCount, status.RequiredCount,
            status.Receipts.Select(receipt => new StudioRuntimeReceiptSummary(
                receipt.Operation, receipt.Status,
                receipt.NextStageId ?? receipt.CurrentStageId ?? receipt.StageId,
                receipt.EventName, receipt.CurrentCount, receipt.RequiredCount, receipt.AtUtc,
                receipt.TransitionId, receipt.ActionId, receipt.ActivationId, receipt.CorrelationId, receipt.Error,
                receipt.TransitionId is not null && status.RouteLabels.TryGetValue(receipt.TransitionId, out var routeLabel) ? routeLabel : null,
                receipt.ActionId is not null && status.EffectLabels.TryGetValue(receipt.ActionId, out var effectLabel) ? effectLabel : null,
                StudioRehearsal.UnmetPhrase(receipt.Evidence),
                StudioRehearsal.NotTakenPhrase(receipt.RejectedEvidence, status.RouteLabels),
                receipt.EvidenceKind ?? "plumbing", receipt.RunId, receipt.ExperienceId, receipt.WorldId)).ToArray(),
            status.Diagnostics)
        {
            ActiveTitle = status.ActiveTitle,
            DevConnected = status.DevConnected,
            DevArmed = status.DevConnected && status.DevStatus?.Armed == true,
            DevPublished = status.DevPublished,
            DevState = status.DevStatus?.State,
            DevSessionId = status.DevStatus?.SessionId,
            LastRejection = status.DevStatus?.LastRejection,
            PassLines = ComposePassLines(status.ActiveRelation == "current" && !string.IsNullOrWhiteSpace(active?.ActivationId)
                ? status.CurrentEvidenceReceipts.Where(receipt => receipt.ActivationId == active.ActivationId).ToArray()
                : Array.Empty<RuntimeReceipt>())
        };
    }

    StudioRuntimePackIdentity? RuntimePackIdentity(string projectId)
    {
        var project = _workspace.ReadProject(projectId);
        var creatorIdentity = project is null ? null
            : _creatorCast.DurableRuntimePackIdentity(projectId, project.Revision);
        if (creatorIdentity is not null) return creatorIdentity;
        var placement = _portfolio.Placements()
            .SingleOrDefault(value => value.ProjectId == projectId);
        if (placement is null) return null;
        var guild = _portfolio.ReadGuild(placement.GuildId);
        if (guild is null) return null;
        var campaign = guild.Campaigns.FirstOrDefault(value => CampaignContains(value, projectId)) ?? DefaultCampaign(guild);
        var compiled = CompileCampaign(guild, campaign);
        return !compiled.Ok ? null : new StudioRuntimePackIdentity(
            campaign.PackId, campaign.Version, compiled.ContentHash!, compiled.Experiences!.Count > 1);
    }

    static bool CampaignContains(StudioCampaignDocument campaign, string projectId) =>
        campaign.StandaloneQuests.Any(value => value.ProjectId == projectId)
        || campaign.Events.Any(value => value.ProjectId == projectId)
        || campaign.Questlines.Any(line => line.Quests.Any(value => value.ProjectId == projectId));
    static string[] GuildProjectIds(StudioGuildDocument guild) =>
        (guild.Artifacts?.Select(value => value.ProjectId)
         ?? Enumerable.Empty<string>())
        .Concat((guild.Campaigns ?? new()).SelectMany(campaign => campaign.StandaloneQuests.Concat(campaign.Events).Concat(campaign.Questlines.SelectMany(line => line.Quests))).Select(value => value.ProjectId))
        .Concat((guild.StandaloneQuests ?? new()).Concat(guild.Events ?? new()).Concat((guild.Questlines ?? new()).SelectMany(line => line.Quests ?? new())).Select(value => value.ProjectId))
        .Distinct(StringComparer.Ordinal).ToArray();

    static IReadOnlyList<StudioRuntimePassLine> ComposePassLines(IReadOnlyList<RuntimeReceipt> receipts)
    {
        var result = new List<StudioRuntimePassLine>();
        Add("Validation", "Revision satisfies the shared contract.", "dev_validation");
        Add("Transfer", "Revision reached the game-owned dev inbox.", "dev_transfer");
        Add("Activation", "The game activated this exact revision.", "dev_activation");
        Add("Rebind", "Loaded local Charm bindings now use this revision.",
            "dev_rebind", "bind_selected_experience");
        var observed = receipts.Where(value => value.Operation is "event" or "transition"
                && value.Status is "matched" or "advanced" or "complete" or "fail")
            .OrderByDescending(value => value.AtUtc).FirstOrDefault();
        if (observed is not null)
            result.Add(new("Runtime observed", "PASS", observed.Status is "complete" or "fail"
                    ? $"Runtime observed the {observed.Status} outcome."
                    : "Runtime observed gameplay for this activation.",
                observed.ActivationId, observed.CorrelationId, observed.AtUtc));
        return result;

        void Add(string kind, string success, params string[] operations)
        {
            var receipt = receipts.Where(value => operations.Contains(value.Operation, StringComparer.Ordinal))
                .OrderByDescending(value => value.AtUtc).FirstOrDefault();
            if (receipt is null) return;
            var failed = receipt.Status == "rejected";
            var skipped = receipt.Status == "skipped";
            var message = failed ? $"{kind} stopped: {receipt.Error ?? "rejected"}.{Itemized(receipt)}"
                : skipped ? receipt.Error == "no_loaded_binding"
                    ? "No loaded Charm binding needed rebinding."
                    : $"Rebind not needed: {receipt.Error ?? "already current"}."
                : success;
            result.Add(new(kind, failed ? "FAIL" : "PASS", message,
                receipt.ActivationId, receipt.CorrelationId, receipt.AtUtc));
        }

        // A rejection receipt already carries the itemized reason (ContractDiagnostic code,
        // path and message). Showing only the coarse Error string — "pack_invalid" — wrote
        // the real answer to disk and never rendered it, leaving the creator to go and find
        // it. Bounded to three so a proof line stays one line.
        static string Itemized(RuntimeReceipt receipt)
        {
            var diagnostics = (receipt.Diagnostics ?? Array.Empty<ContractDiagnostic>())
                .Where(value => value is not null).ToArray();
            if (diagnostics.Length == 0) return string.Empty;
            var shown = diagnostics.Take(3).Select(value => $"{value.Code}: {value.Message}");
            var more = diagnostics.Length - Math.Min(3, diagnostics.Length);
            return " " + string.Join(" ", shown) + (more > 0 ? $" (+{more} more)" : string.Empty);
        }
    }
    public object ProjectHistory(string projectId) => _workspace.History(projectId);

    public StudioDownloadResult ExportProject(string projectId, StudioExportRequest? request)
    {
        var project = _workspace.ReadProject(projectId);
        var compiled = project is null ? StudioCertificationResult.Fail("project_missing") : _workspace.Validate(projectId);
        var live = request?.IncludeLiveEvidence == true && project is not null ? RuntimeStatus(projectId) : null;
        var result = _dataExport.BuildBundle(project, compiled, live, request);
        _usage.RecordOutcome("bundle_export", UsageOutcome(result.Ok, false, result.Error));
        return result;
    }

    public StudioDownloadResult DownloadQuestpack(string projectId)
    {
        var project = _workspace.ReadProject(projectId);
        var compiled = project is null ? StudioCertificationResult.Fail("project_missing") : _workspace.Validate(projectId);
        var result = _dataExport.BuildQuestpack(project, compiled);
        _usage.RecordOutcome("questpack_download", UsageOutcome(result.Ok, false, result.Error));
        return result;
    }

    public StudioDownloadResult DownloadSpatialEvidence(string projectId)
    {
        var project = _workspace.ReadProject(projectId);
        if (project is null) return StudioDownloadResult.Fail("project_missing");
        var compiled = _workspace.Validate(projectId);
        if (!compiled.Ok || compiled.Document is null || string.IsNullOrWhiteSpace(compiled.ContentHash))
            return StudioDownloadResult.Fail(compiled.Error ?? "graph_invalid");
        var status = _workspace.RuntimeStatus(projectId, RuntimePackIdentity(projectId),
            _runControl.Status(projectId), RuntimeReceiptStore.MaxListLimit);
        var areas = (compiled.Document.SpatialAreas ?? new List<SpatialArea>())
            .Where(area => area?.SourceAnchor is not null)
            .GroupBy(area => area.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var candidates = status.ExactReceipts.Where(receipt => receipt is not null
            && string.Equals(receipt.ExperienceId, project.ExperienceId, StringComparison.Ordinal)
            && string.Equals(receipt.ContentHash, status.ContentHash, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(receipt.ActivationId)
            && !string.IsNullOrWhiteSpace(receipt.RunId) && !string.IsNullOrWhiteSpace(receipt.WorldId))
            .SelectMany(receipt => SpatialRecords(receipt).Select(record => (receipt, record)))
            .Where(pair => pair.record.ResolvedCenter is not null
                && (pair.record.Spatial == "count_in_area" || pair.record.ObservedPosition is not null)
                && !string.IsNullOrWhiteSpace(pair.record.AnchorSha256)
                && areas.TryGetValue(pair.record.AreaId ?? string.Empty, out var area)
                && string.Equals(area.SourceAnchor.ContentSha256, pair.record.AnchorSha256, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0) return StudioDownloadResult.Fail("spatial_evidence_missing");
        var newest = candidates.OrderByDescending(pair => pair.receipt.AtUtc).First().receipt;
        var selected = candidates.Where(pair => pair.receipt.ActivationId == newest.ActivationId
            && pair.receipt.RunId == newest.RunId && pair.receipt.WorldId == newest.WorldId).ToArray();
        // The same exact Runtime evidence set must produce the same file. Using wall-clock export
        // time here defeated Steward's content-addressed idempotency on every repeat download.
        var exportedUtc = newest.AtUtc;
        var records = selected.Select(pair => new SpatialEvidenceRecord
            {
                ReceiptId = pair.receipt.Id,
                AtUtc = pair.receipt.AtUtc,
                CorrelationId = pair.receipt.CorrelationId,
                TransitionId = pair.receipt.TransitionId,
                EventName = pair.receipt.EventName,
                AreaId = pair.record.AreaId,
                Predicate = pair.record.Spatial,
                CurrentCount = pair.record.Current,
                RequiredCount = pair.record.Required,
                AnchorSha256 = pair.record.AnchorSha256,
                Snapshot = areas[pair.record.AreaId].SourceAnchor.Snapshot,
                Piece = areas[pair.record.AreaId].SourceAnchor.Piece,
                ResolvedCenter = pair.record.ResolvedCenter,
                RadiusMeters = pair.record.RadiusMeters.GetValueOrDefault(),
                ObservedPosition = pair.record.ObservedPosition,
                DistanceMeters = pair.record.DistanceMeters,
                Satisfied = pair.record.Satisfied
            }).Take(512).ToArray();
        SpatialEvidenceBundle? bundle = null;
        byte[]? bytes = null;
        var low = 1;
        var high = records.Length;
        while (low <= high)
        {
            var count = low + ((high - low) / 2);
            var candidate = new SpatialEvidenceBundle
            {
                ExportedUtc = exportedUtc,
                ProjectId = project.ProjectId,
                ExperienceId = project.ExperienceId,
                PackId = newest.PackId,
                ContentHash = newest.ContentHash,
                ActivationId = newest.ActivationId,
                RunId = newest.RunId,
                WorldUid = newest.WorldId,
                Records = records.Take(count).ToList()
            };
            candidate.ContentSha256 = SpatialExchangeContract.ComputeEvidenceHash(candidate);
            SpatialExchangeContract.ValidateEvidence(candidate, true);
            var candidateBytes = Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(candidate, Formatting.Indented));
            if (candidateBytes.Length <= SpatialExchangeSchema.MaxDocumentBytes)
            {
                bundle = candidate;
                bytes = candidateBytes;
                low = count + 1;
            }
            else high = count - 1;
        }
        if (bundle is null || bytes is null)
            return StudioDownloadResult.Fail("spatial_evidence_document_too_large");
        _usage.RecordOutcome("spatial_evidence_export", "accepted");
        return StudioDownloadResult.Success(project.ProjectId + ".spatial-evidence.json",
            "application/vnd.comfy.quest-spatial-evidence+json", bytes, bundle.ContentSha256);
    }

    static IEnumerable<TriggerClauseTrace> SpatialRecords(RuntimeReceipt receipt)
    {
        foreach (var trace in Walk(receipt.Evidence)) if (!string.IsNullOrWhiteSpace(trace.AreaId)) yield return trace;
        foreach (var rejected in receipt.RejectedEvidence ?? Array.Empty<RejectedTransitionEvidence>())
            foreach (var trace in Walk(rejected?.Evidence)) if (!string.IsNullOrWhiteSpace(trace.AreaId)) yield return trace;
        static IEnumerable<TriggerClauseTrace> Walk(TriggerClauseTrace? trace)
        {
            if (trace is null) yield break;
            yield return trace;
            foreach (var child in trace.Children ?? new List<TriggerClauseTrace>())
                foreach (var nested in Walk(child)) yield return nested;
        }
    }

    public StudioUsageReport UsageReport() => _usage.Report();
    public StudioUsageReport SetUsageEnabled(bool enabled) => _usage.SetEnabled(enabled);
    public StudioDownloadResult ExportUsage() => _usage.Export();
    public StudioUsageResetResult ResetUsage(bool confirmed) => _usage.Reset(confirmed);

    static string UsageOutcome(bool ok, bool conflict, string? error) => ok ? "accepted" : conflict ? "conflict"
        : error is "project_missing" ? "missing" : "rejected";

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
        var compiled = ExperienceCompiler.CompileProductionJson(json);
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
    static string Bounded(string? value, string fallback, int maximum)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return result.Length <= maximum ? result : result[..maximum];
    }
    static string EvidencePolicy(string? value) => value is "community" or "runtime" or "both" ? value : "preserve_source";
    T Clone<T>(T value) => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value, _host.Json), _host.Json)!;
    static StudioAbstractionTargetChoice CloneTarget(StudioAbstractionTargetChoice value) => new()
    {
        Id = value.Id,
        Label = value.Label,
        RuntimeTarget = value.RuntimeTarget,
        SourceQuestId = value.SourceQuestId,
        PracticeAttribution = value.PracticeAttribution,
    };
    static bool CatalogContains(string catalogJson, IReadOnlyList<string> questIds)
    {
        try
        {
            using var catalog = JsonDocument.Parse(catalogJson);
            var actual = catalog.RootElement.GetProperty("quests").EnumerateArray()
                .Select(value => value.GetProperty("quest_id").GetString()).Where(value => value is not null)
                .Cast<string>().ToHashSet(StringComparer.Ordinal);
            return questIds.All(actual.Contains);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }
    string InvariantHash(StudioProjectDocument project, StudioGuildAbstractionDocument abstraction)
    {
        var normalized = Clone(project);
        normalized.ProjectId = "$project";
        normalized.PackId = "$pack";
        normalized.ExperienceId = "$experience";
        normalized.Revision = 0;
        normalized.UpdatedUtc = default;
        normalized.Derivation = null;
        normalized.Title = "$title";
        // Old frozen abstractions keep their original serialization identity after draft migration.
        using var canonical = JsonDocument.Parse(abstraction.CanonicalProjectJson);
        normalized.SchemaVersion = canonical.RootElement.GetProperty("schema_version").GetInt32();
        var entry = normalized.Nodes.Single(value => value.Id == abstraction.EntryNodeId);
        entry.Label = "$instructions";
        var route = normalized.Nodes.SelectMany(value => value.Routes).Single(value => value.Id == abstraction.RouteId);
        route.Target = "$target";
        if (abstraction.ConfigurablePractice)
        {
            ApplyHuntMechanic(route, "thrown_spear");
            route.Where = route.Where.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
        if (abstraction.TargetSpawnActionId is not null)
            entry.EntryActions!.Single(value => value.Id == abstraction.TargetSpawnActionId).Prefab = "$target-prefab";
        if (abstraction.CompletionActionId is not null)
            route.Actions.Single(value => value.Id == abstraction.CompletionActionId).Text = "$completion";
        return StudioCreativeHash.Text(System.Text.Json.JsonSerializer.Serialize(normalized, _host.Json));
    }
    string AbstractionHash(StudioGuildAbstractionDocument value)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(new
        {
            value.AbstractionId,
            value.Revision,
            value.Title,
            value.Explanation,
            value.SourceSnapshotId,
            value.SourceSnapshotHash,
            value.SourceQuestIds,
            value.Attribution,
            value.EvidencePolicy,
            value.EvidenceExplanation,
            value.CanonicalProjectHash,
            value.InvariantHash,
            value.EntryNodeId,
            value.RouteId,
            value.CompletionActionId,
            value.TargetSpawnActionId,
            value.TargetChoices,
        }, _host.Json)!.AsObject();
        if (value.ConfigurablePractice) node["configurable_practice"] = true;
        return StudioCreativeHash.Text(node.ToJsonString(_host.Json));
    }

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

    static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

internal sealed record CompiledGuild(
    bool Ok,
    string? Error,
    IReadOnlyDictionary<string, string>? Experiences,
    string? ContentHash,
    IReadOnlyList<StudioCampaignLineageEntry> Lineage,
    IReadOnlyList<ContractDiagnostic> Diagnostics)
{
    public static CompiledGuild Success(IReadOnlyDictionary<string, string> experiences, string contentHash, IReadOnlyList<StudioCampaignLineageEntry> lineage) =>
        new(true, null, experiences, contentHash, lineage, Array.Empty<ContractDiagnostic>());
    public static CompiledGuild Fail(string error, IReadOnlyList<ContractDiagnostic>? diagnostics = null) =>
        new(false, error, null, null, Array.Empty<StudioCampaignLineageEntry>(), diagnostics ?? Array.Empty<ContractDiagnostic>());
}

public sealed record StudioGuildCertificationResult(
    bool Ok,
    string Status,
    string? Error,
    string? ContentHash,
    IReadOnlyList<string> ExperienceIds,
    IReadOnlyList<ContractDiagnostic> Diagnostics)
{
    public static StudioGuildCertificationResult Success(string contentHash, IReadOnlyList<string> experienceIds) =>
        new(true, "certified", null, contentHash, experienceIds, Array.Empty<ContractDiagnostic>());
    public static StudioGuildCertificationResult Fail(string error, IReadOnlyList<ContractDiagnostic>? diagnostics = null) =>
        new(false, "rejected", error, null, Array.Empty<string>(), diagnostics ?? Array.Empty<ContractDiagnostic>());
}

public sealed record StudioGuildPublishResult(
    bool Ok,
    bool Conflict,
    string Status,
    string? Error,
    QuestPackPublishReceipt? Receipt,
    StudioGuildDocument? Guild,
    string? ContentHash,
    IReadOnlyList<string> ExperienceIds,
    IReadOnlyList<ContractDiagnostic> Diagnostics)
{
    public static StudioGuildPublishResult Success(string status, QuestPackPublishReceipt receipt, StudioGuildDocument guild,
        string contentHash, IReadOnlyList<string> experienceIds) =>
        new(true, false, status, null, receipt, guild, contentHash, experienceIds, Array.Empty<ContractDiagnostic>());
    public static StudioGuildPublishResult RevisionConflict(StudioGuildDocument guild) =>
        new(false, true, "conflict", "revision_conflict", null, guild, null, Array.Empty<string>(), Array.Empty<ContractDiagnostic>());
    public static StudioGuildPublishResult Fail(string error, IReadOnlyList<ContractDiagnostic>? diagnostics = null, QuestPackPublishReceipt? receipt = null) =>
        new(false, false, "rejected", error, receipt, null, null, Array.Empty<string>(), diagnostics ?? Array.Empty<ContractDiagnostic>());
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

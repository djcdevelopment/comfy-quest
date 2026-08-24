using System.Text.Json;
using Comfy.Quest.Studio;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Comfy.Quest.Studio.Tests;

public sealed class QuestStudioPortfolioTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "comfy-quest-portfolio-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PortfolioAndGuildHierarchyPersistWithoutChangingProjectSchema()
    {
        var service = CreateService();
        var portfolio = service.Portfolio();
        var project = service.CreateProject("blank");
        var guild = service.CreateGuild(new StudioGuildCreateRequest("Wayfinder Guild", "Derek"));

        Assert.Equal(1, portfolio.SchemaVersion);
        Assert.Equal(StudioProjectDocument.CurrentSchemaVersion, project.SchemaVersion);
        Assert.Collection(guild.Questlines, line =>
        {
            Assert.Equal("main", line.Id);
            Assert.Equal("Main questline", line.Title);
        });
        var placed = service.PlaceProject(guild.GuildId, new StudioGuildPlacementRequest(
            guild.Revision, project.ProjectId, "quest", "questline", "main", null));
        Assert.True(placed.Ok, placed.Error);
        Assert.Equal("per_run", Assert.Single(Assert.Single(placed.Guild!.Questlines).Quests).RewardPolicy);

        var reopened = CreateService();
        Assert.Equal(portfolio.PortfolioId, reopened.Portfolio().PortfolioId);
        Assert.Equal(project.ProjectId, Assert.Single(Assert.Single(reopened.ReadGuild(guild.GuildId)!.Questlines).Quests).ProjectId);
        Assert.Equal(StudioProjectDocument.CurrentSchemaVersion, reopened.ReadProject(project.ProjectId)!.SchemaVersion);
    }

    [Fact]
    public void PlacementMovesAtomicallyAcrossGuildsAndCanReturnToUnfiled()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        var source = service.CreateGuild(new StudioGuildCreateRequest("Source", "Derek"));
        var target = service.CreateGuild(new StudioGuildCreateRequest("Target", "Derek"));
        Assert.True(service.PlaceProject(source.GuildId, new(source.Revision, project.ProjectId, "quest", "questline", "main", null)).Ok);

        var moved = service.PlaceProject(target.GuildId, new(target.Revision, project.ProjectId, "event", "event", null, null));
        Assert.True(moved.Ok, moved.Error);
        Assert.Empty(Assert.Single(service.ReadGuild(source.GuildId)!.Questlines).Quests);
        Assert.Equal(project.ProjectId, Assert.Single(service.ReadGuild(target.GuildId)!.Events).ProjectId);
        Assert.Single(Placements(service), value => value.ProjectId == project.ProjectId && value.GuildId == target.GuildId);

        var targetCurrent = service.ReadGuild(target.GuildId)!;
        var unfiled = service.PlaceProject(target.GuildId, new(targetCurrent.Revision, project.ProjectId, "quest", "unfile", null, null));
        Assert.True(unfiled.Ok, unfiled.Error);
        Assert.DoesNotContain(Placements(service), value => value.ProjectId == project.ProjectId);
        Assert.Contains(Unfiled(service), value => value.ProjectId == project.ProjectId);
    }

    [Fact]
    public void GuildWritesUseOptimisticRevisionsAndRejectDuplicateMembership()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        var first = service.CreateGuild(new StudioGuildCreateRequest("First", "Derek"));
        var second = service.CreateGuild(new StudioGuildCreateRequest("Second", "Derek"));
        var placed = service.PlaceProject(first.GuildId, new(first.Revision, project.ProjectId, "quest", "standalone", null, null));
        Assert.True(placed.Ok, placed.Error);

        var stale = service.SaveGuild(first.GuildId, new StudioGuildSaveRequest(first.Revision, first));
        Assert.False(stale.Ok);
        Assert.True(stale.Conflict);
        Assert.Equal("revision_conflict", stale.Error);

        var forged = service.ReadGuild(second.GuildId)!;
        forged.StandaloneQuests.Add(new StudioGuildArtifact { ProjectId = project.ProjectId, Kind = "quest" });
        var duplicate = service.SaveGuild(second.GuildId, new StudioGuildSaveRequest(forged.Revision, forged));
        Assert.False(duplicate.Ok);
        Assert.Equal("project_already_assigned", duplicate.Error);
    }

    [Fact]
    public void DuplicateAndExportImportAreDeepForksWithProvenance()
    {
        var service = CreateService();
        var quest = service.CreateProject("blank");
        var questEvent = service.CreateProject("blank");
        var guild = service.CreateGuild(new StudioGuildCreateRequest("Rangers", "Derek"));
        var first = service.PlaceProject(guild.GuildId, new(guild.Revision, quest.ProjectId, "quest", "questline", "main", null));
        Assert.True(first.Ok, first.Error);
        var second = service.PlaceProject(guild.GuildId, new(first.Guild!.Revision, questEvent.ProjectId, "event", "event", null, null));
        Assert.True(second.Ok, second.Error);

        var duplicated = service.DuplicateGuild(guild.GuildId, new StudioGuildDuplicateRequest(second.Guild!.Revision));
        Assert.True(duplicated.Ok, duplicated.Error);
        Assert.Equal(2, duplicated.Projects.Count);
        Assert.All(duplicated.Projects, value => Assert.DoesNotContain(value.ProjectId, new[] { quest.ProjectId, questEvent.ProjectId }));
        Assert.Equal("duplicate", duplicated.Guild!.Provenance.Origin);
        Assert.Equal(guild.GuildId, duplicated.Guild.Provenance.SourceGuildId);
        Assert.Equal("0.1.0", duplicated.Guild.Version);
        Assert.Equal(2, Artifacts(duplicated.Guild).Select(value => value.ProjectId).Distinct(StringComparer.Ordinal).Count());

        var download = service.ExportGuild(guild.GuildId);
        Assert.True(download.Ok, download.Error);
        Assert.Equal("application/vnd.comfy.questguild+json", download.ContentType);
        var bundle = JsonSerializer.Deserialize<StudioGuildBundleDocument>(download.Bytes!, HostJson())!;
        var imported = service.ImportGuild(new StudioGuildImportRequest(bundle));
        Assert.True(imported.Ok, imported.Error);
        Assert.Equal("import", imported.Guild!.Provenance.Origin);
        Assert.Equal(guild.GuildId, imported.Guild.Provenance.SourceGuildId);
        Assert.Equal(2, imported.Projects.Count);
        Assert.Empty(Artifacts(imported.Guild).Select(value => value.ProjectId).Intersect(Artifacts(guild).Select(value => value.ProjectId), StringComparer.Ordinal));
    }

    [Fact]
    public void PortfolioAndGuildIdentitiesCannotBeRewritten()
    {
        var service = CreateService();
        var portfolio = service.Portfolio();
        var changedPortfolio = Clone(portfolio);
        changedPortfolio.PortfolioId = "portfolio-forged";
        Assert.Equal("portfolio_identity_immutable", service.SavePortfolio(new(portfolio.Revision, changedPortfolio)).Error);

        var guild = service.CreateGuild(new StudioGuildCreateRequest("Identity", "Derek"));
        var changedGuild = Clone(guild);
        changedGuild.GuildId = "guild-forged";
        Assert.Equal("guild_identity_immutable", service.SaveGuild(guild.GuildId, new(guild.Revision, changedGuild)).Error);
    }

    [Fact]
    public void InvalidGuildBundleIsRejectedBeforeAnyProjectForkIsWritten()
    {
        var service = CreateService();
        var first = service.CreateProject("blank");
        var second = service.CreateProject("blank");
        var guild = service.CreateGuild(new StudioGuildCreateRequest("Preflight", "Derek"));
        var placed = service.PlaceProject(guild.GuildId, new(guild.Revision, first.ProjectId, "quest", "questline", "main", null));
        placed = service.PlaceProject(guild.GuildId, new(placed.Guild!.Revision, second.ProjectId, "event", "event", null, null));
        Assert.True(placed.Ok, placed.Error);
        var before = service.ListProjects().Count;
        var incomplete = new StudioGuildBundleDocument(1, placed.Guild!, new[] { first });

        var result = service.ImportGuild(new StudioGuildImportRequest(incomplete));
        Assert.False(result.Ok);
        Assert.Equal("guild_project_missing", result.Error);
        Assert.Equal(before, service.ListProjects().Count);
        Assert.Empty(result.Projects);
    }

    [Fact]
    public void NullQuestlineContentsFailClosedInsteadOfBeingFlattenedOnImport()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        var guild = service.CreateGuild(new StudioGuildCreateRequest("Malformed", "Derek"));
        var placed = service.PlaceProject(guild.GuildId, new(guild.Revision, project.ProjectId, "quest", "questline", "main", null));
        Assert.True(placed.Ok, placed.Error);
        var source = Clone(placed.Guild!);
        source.Questlines[0].Quests = null!;
        var before = service.ListProjects().Count;

        var result = service.ImportGuild(new StudioGuildImportRequest(new StudioGuildBundleDocument(1, source, new[] { project })));

        Assert.False(result.Ok);
        Assert.Equal("questline_invalid", result.Error);
        Assert.Equal(before, service.ListProjects().Count);
        Assert.Empty(result.Projects);
    }

    [Fact]
    public void InterruptedCrossGuildMoveFinishesForwardFromItsJournal()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        var source = service.CreateGuild(new StudioGuildCreateRequest("Source", "Derek"));
        var target = service.CreateGuild(new StudioGuildCreateRequest("Target", "Derek"));
        var placed = service.PlaceProject(source.GuildId, new(source.Revision, project.ProjectId, "quest", "questline", "main", null));
        Assert.True(placed.Ok, placed.Error);
        var sourceAfter = Clone(placed.Guild!);
        Assert.Single(sourceAfter.Questlines).Quests.Clear();
        sourceAfter.Revision++;
        sourceAfter.UpdatedUtc = DateTimeOffset.UtcNow;
        var targetAfter = Clone(target);
        targetAfter.Events.Add(new StudioGuildArtifact { ProjectId = project.ProjectId, Kind = "event" });
        targetAfter.Revision++;
        targetAfter.UpdatedUtc = sourceAfter.UpdatedUtc;
        var transaction = new
        {
            schema_version = 1,
            transaction_id = "placement-recovery",
            created_utc = DateTimeOffset.UtcNow,
            source_after = sourceAfter,
            target_after = targetAfter,
        };
        var directory = Path.Combine(_root, "quest-studio", "portfolio-transactions");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "placement-recovery.json"), JsonSerializer.Serialize(transaction, HostJson()));

        var reopened = CreateService();
        Assert.Equal(project.ProjectId, Assert.Single(reopened.ReadGuild(target.GuildId)!.Events).ProjectId);
        Assert.Empty(Assert.Single(reopened.ReadGuild(source.GuildId)!.Questlines).Quests);
        Assert.Empty(Directory.GetFiles(directory, "*.json"));
    }

    [Fact]
    public void CorruptPortfolioRollsBackToTheLastAtomicRevisionThenFailsClosedWithoutIt()
    {
        var service = CreateService();
        var original = service.Portfolio();
        var renamed = Clone(original);
        renamed.Title = "Renamed";
        Assert.True(service.SavePortfolio(new(original.Revision, renamed)).Ok);
        var path = Path.Combine(_root, "quest-studio", "portfolio.json");
        File.WriteAllText(path, "not json");

        Assert.Equal(original.Title, CreateService().Portfolio().Title);
        File.WriteAllText(path + ".previous", "also not json");
        var error = Assert.Throws<InvalidDataException>(() => CreateService().Portfolio());
        Assert.Equal("portfolio_unreadable", error.Message);
    }

    QuestStudioService CreateService()
    {
        var host = new FakeHost(_root);
        return new QuestStudioService(host, new QuestPackPublisher(host));
    }

    static IReadOnlyList<StudioGuildArtifact> Artifacts(StudioGuildDocument guild) =>
        guild.StandaloneQuests.Concat(guild.Events).Concat(guild.Questlines.SelectMany(value => value.Quests)).ToArray();

    static IReadOnlyList<StudioProjectPlacement> Placements(QuestStudioService service)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(service.PortfolioView(), HostJson()));
        return JsonSerializer.Deserialize<StudioProjectPlacement[]>(document.RootElement.GetProperty("placements"), HostJson())!;
    }

    static IReadOnlyList<StudioProjectSummary> Unfiled(QuestStudioService service)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(service.PortfolioView(), HostJson()));
        return JsonSerializer.Deserialize<StudioProjectSummary[]>(document.RootElement.GetProperty("unfiled"), HostJson())!;
    }

    static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, HostJson()), HostJson())!;
    static JsonSerializerOptions HostJson() => new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    sealed class FakeHost(string stateDirectory) : IQuestStudioHost
    {
        public string StateDirectory { get; } = stateDirectory;
        public string? FindValheim() => null;
        public bool Authorize(HttpRequest request) => true;
        public JsonSerializerOptions Json { get; } = HostJson();
    }
}

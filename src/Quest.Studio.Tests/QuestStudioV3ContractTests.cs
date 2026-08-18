using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Comfy.Quest.Studio;
using ComfyQuestContracts;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Comfy.Quest.Studio.Tests;

public sealed class QuestStudioV3ContractTests : IDisposable
{
    readonly string _root = Path.Combine(
        Path.GetTempPath(), "comfy-quest-studio-v3-tests", Guid.NewGuid().ToString("N"));
    readonly TestHost _host;

    public QuestStudioV3ContractTests()
    {
        _host = new TestHost(_root);
    }

    [Fact]
    public void Catalog_separates_all_creator_meanings_from_the_fail_closed_runtime_surface()
    {
        var service = CreateService();
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(service.WorkspaceCatalog(), _host.Json));
        var catalog = document.RootElement;
        var creatorEvents = catalog.GetProperty("creator_events").EnumerateArray().ToArray();
        var engineEvents = catalog.GetProperty("engine_events").EnumerateArray().ToArray();

        Assert.Equal(3, catalog.GetProperty("schema_version").GetInt32());
        Assert.Equal(34, creatorEvents.Length);
        Assert.Equal(28, creatorEvents.Count(value => value.GetProperty("profile").GetString() == "core"));
        Assert.Equal(6, creatorEvents.Count(value => value.GetProperty("profile").GetString() == "extended"));
        Assert.Equal(26, creatorEvents.Count(value => value.GetProperty("production_available").GetBoolean()));
        Assert.Equal(8, creatorEvents.Count(value => !value.GetProperty("production_available").GetBoolean()));
        Assert.All(creatorEvents, value =>
            Assert.Equal(value.GetProperty("production_available").GetBoolean(), value.GetProperty("addable").GetBoolean()));

        Assert.Equal(2, engineEvents.Length);
        Assert.Equal(
            new[] { "chat_received", "timer_elapsed" },
            engineEvents.Select(value => value.GetProperty("name").GetString()).OrderBy(value => value, StringComparer.Ordinal));
        Assert.All(engineEvents, value =>
        {
            Assert.True(value.GetProperty("production_available").GetBoolean());
            Assert.True(value.GetProperty("addable").GetBoolean());
        });

        Assert.Equal(34, CreatorEventCatalog.Count);
        Assert.Equal(26, RuntimeProductionEventCatalog.Count);
        Assert.Equal(2, RuntimeProductionEventCatalog.EngineEvents.Count);
        Assert.All(creatorEvents, value =>
        {
            var name = value.GetProperty("name").GetString()!;
            Assert.Equal(value.GetProperty("production_available").GetBoolean(), RuntimeProductionEventCatalog.Contains(name));
        });
        var placed = Assert.Single(creatorEvents, value => value.GetProperty("name").GetString() == "piece_placed");
        Assert.Equal("listen_host", placed.GetProperty("fixed_where").GetProperty("actor_role").GetString());
        var sent = Assert.Single(creatorEvents, value => value.GetProperty("name").GetString() == "chat_sent");
        Assert.Equal(new[] { "normal", "shout" }, sent.GetProperty("targets").EnumerateArray().Select(value => value.GetString()));
        foreach (var name in new[] { "container_emptied", "item_unequipped", "piece_destroyed", "piece_removed", "piece_repaired", "player_teleported", "attack_blocked", "character_staggered", "damage_dealt", "resource_damaged", "resource_picked", "item_crafted", "station_fuel_added", "station_input_added", "station_output_collected", "station_output_produced" })
        {
            var promoted = Assert.Single(creatorEvents, value => value.GetProperty("name").GetString() == name);
            Assert.True(promoted.GetProperty("production_available").GetBoolean());
            Assert.True(promoted.GetProperty("addable").GetBoolean());
            Assert.Equal("automated-contract", promoted.GetProperty("availability").GetProperty("evidence_state").GetString());
        }
    }

    [Fact]
    public void Compiler_rejects_targets_and_fixed_filters_the_runtime_cannot_emit()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        var route = Assert.Single(Assert.Single(project.Nodes).Routes);
        route.Event = "chat_sent";
        route.Target = "private";
        route.Where.Clear();
        project = Save(service, project);

        var impossibleTarget = service.CertifyGraph(project.ProjectId);
        Assert.False(impossibleTarget.Ok);
        Assert.Contains(impossibleTarget.Diagnostics, value => value.Code == "trigger.target_value");

        route = Assert.Single(Assert.Single(project.Nodes).Routes);
        route.Event = "piece_placed";
        route.Target = "wood_wall";
        route.Where = new(StringComparer.Ordinal) { ["actor_role"] = "peer" };
        project = Save(service, project);
        var impossibleRole = service.CertifyGraph(project.ProjectId);
        Assert.False(impossibleRole.Ok);
        Assert.Contains(impossibleRole.Diagnostics, value => value.Code is "piece_placed_listen_host_required" or "where_field_fixed");
    }

    [Fact]
    public void Compiler_accepts_production_events_and_rejects_research_only_meanings()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        var route = Assert.Single(Assert.Single(project.Nodes).Routes);
        route.Event = "character_healed";
        route.Target = "you";
        route.Where = new(StringComparer.Ordinal) { ["amount"] = "25" };
        project = Save(service, project);

        var production = service.CertifyGraph(project.ProjectId);
        Assert.True(production.Ok, Diagnostics(production));
        Assert.Equal("25", Assert.Single(Assert.Single(production.Document!.Stages).Transitions)
            .When.Where!["amount"]);

        route = Assert.Single(Assert.Single(project.Nodes).Routes);
        route.Event = "max_health_changed";
        route.Target = "health";
        route.Where.Clear();
        project = Save(service, project);

        var researchOnly = service.CertifyGraph(project.ProjectId);
        Assert.False(researchOnly.Ok);
        Assert.Contains(researchOnly.Diagnostics, value => value.Code == "event_unknown");
        Assert.True(CreatorEventCatalog.TryGet("max_health_changed", out _));
        Assert.False(RuntimeProductionEventCatalog.Contains("max_health_changed"));
    }

    [Theory]
    [InlineData("container_emptied", "piece_chest_wood")]
    [InlineData("item_unequipped", "AxeBronze")]
    [InlineData("piece_destroyed", "wood_wall")]
    [InlineData("piece_removed", "piece")]
    [InlineData("piece_repaired", "wood_wall")]
    [InlineData("player_teleported", null)]
    [InlineData("attack_blocked", "$enemy_greyling")]
    [InlineData("character_staggered", "$enemy_greyling")]
    [InlineData("damage_dealt", "$enemy_greyling")]
    [InlineData("resource_damaged", "Beech1")]
    [InlineData("resource_picked", "RaspberryBush")]
    [InlineData("item_crafted", "piece_workbench")]
    [InlineData("station_fuel_added", "Coal")]
    [InlineData("station_input_added", "CopperOre")]
    [InlineData("station_output_collected", "cooking output")]
    [InlineData("station_output_produced", "Copper")]
    public void Promoted_actions_compile_and_guided_rehearsal_uses_production_target_policy(
        string eventName, string? target)
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        var route = Assert.Single(Assert.Single(project.Nodes).Routes);
        route.Event = eventName;
        route.Target = target;
        route.Where.Clear();
        project = Save(service, project);

        var certification = service.CertifyGraph(project.ProjectId);
        Assert.True(certification.Ok, Diagnostics(certification));
        var rehearsal = service.Rehearse(project.ProjectId, new StudioRehearsalRequest { Mode = "guided" });
        Assert.True(rehearsal.Ok, rehearsal.Error);
        Assert.Equal(target, Assert.Single(rehearsal.GeneratedSteps).Target);
    }

    [Fact]
    public void Compiler_allows_only_emitted_where_fields_and_rejects_literal_any()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        var route = Assert.Single(Assert.Single(project.Nodes).Routes);
        route.Event = "kill";
        route.Target = "$enemy_greyling";
        route.Where = new(StringComparer.Ordinal)
        {
            ["weapon_skill"] = "Axes",
            ["projectile"] = "true"
        };
        project = Save(service, project);

        var allowed = service.CertifyGraph(project.ProjectId);
        Assert.True(allowed.Ok, Diagnostics(allowed));
        var where = Assert.Single(Assert.Single(allowed.Document!.Stages).Transitions).When.Where!;
        Assert.Equal("Axes", where["weapon_skill"]);
        Assert.Equal("true", where["projectile"]);

        route = Assert.Single(Assert.Single(project.Nodes).Routes);
        route.Where["amount"] = "1";
        project = Save(service, project);
        var unsupported = service.CertifyGraph(project.ProjectId);
        Assert.False(unsupported.Ok);
        Assert.Contains(unsupported.Diagnostics, value => value.Code == "where_field_unsupported");

        route = Assert.Single(Assert.Single(project.Nodes).Routes);
        route.Where.Remove("amount");
        route.Where["projectile"] = "any";
        route.Target = "any";
        project = Save(service, project);
        var literalAny = service.CertifyGraph(project.ProjectId);
        Assert.False(literalAny.Ok);
        Assert.Contains(literalAny.Diagnostics, value => value.Code == "where_any_literal");
        Assert.Contains(literalAny.Diagnostics, value => value.Code == "target_any_literal");
    }

    [Fact]
    public void Where_serialization_and_content_hash_are_independent_of_dictionary_insertion_order()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        var route = Assert.Single(Assert.Single(project.Nodes).Routes);
        route.Event = "kill";
        route.Target = "$enemy_greyling";
        route.Where = new(StringComparer.Ordinal)
        {
            ["weapon_skill"] = "Axes",
            ["projectile"] = "true"
        };
        project = Save(service, project);
        var first = service.CertifyGraph(project.ProjectId);
        Assert.True(first.Ok, Diagnostics(first));

        route = Assert.Single(Assert.Single(project.Nodes).Routes);
        route.Where = new(StringComparer.Ordinal)
        {
            ["projectile"] = "true",
            ["weapon_skill"] = "Axes"
        };
        project = Save(service, project);
        var second = service.CertifyGraph(project.ProjectId);
        Assert.True(second.Ok, Diagnostics(second));

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(first.ExperienceJson, second.ExperienceJson);
        using var compiled = JsonDocument.Parse(second.ExperienceJson!);
        var rawWhere = compiled.RootElement.GetProperty("stages")[0].GetProperty("transitions")[0]
            .GetProperty("when").GetProperty("where").GetRawText();
        Assert.True(
            rawWhere.IndexOf("\"projectile\"", StringComparison.Ordinal)
            < rawWhere.IndexOf("\"weapon_skill\"", StringComparison.Ordinal),
            rawWhere);
    }

    [Fact]
    public void Schema_two_legacy_route_fields_migrate_in_memory_without_rewriting_on_read()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        project.Nodes = new()
        {
            new StudioNode
            {
                Id = "start", Label = "Start", X = 100, Y = 100,
                Routes = new()
                {
                    new StudioRoute
                    {
                        Id = "hear-peer", Event = "chat_received", Target = "shout", ActorRole = "peer",
                        DestinationNodeId = "wait", Actions = new()
                    }
                }
            },
            new StudioNode
            {
                Id = "wait", Label = "Wait", X = 300, Y = 100,
                Routes = new()
                {
                    new StudioRoute
                    {
                        Id = "timer-done", Event = "timer_elapsed", TimerId = "ritual-timer",
                        Outcome = "complete", Actions = new()
                    }
                }
            }
        };
        project.EntryNodeId = "start";
        project = Save(service, project);

        var draftPath = Path.Combine(_root, "quest-studio", "projects", project.ProjectId, "draft.json");
        var legacy = JsonNode.Parse(File.ReadAllText(draftPath))!.AsObject();
        legacy["schema_version"] = 2;
        var nodes = legacy["nodes"]!.AsArray();
        var chatRoute = nodes[0]!["routes"]!.AsArray()[0]!.AsObject();
        chatRoute.Remove("where");
        chatRoute["actor_role"] = "peer";
        var timerRoute = nodes[1]!["routes"]!.AsArray()[0]!.AsObject();
        timerRoute.Remove("where");
        timerRoute["timer_id"] = "ritual-timer";
        File.WriteAllText(draftPath, legacy.ToJsonString(_host.Json), new UTF8Encoding(false));
        var exactLegacyBytes = File.ReadAllBytes(draftPath);

        var migrated = Assert.IsType<StudioProjectDocument>(service.ReadProject(project.ProjectId));
        Assert.Equal(StudioProjectDocument.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal("peer", migrated.Nodes[0].Routes[0].Where["actor_role"]);
        Assert.Equal("ritual-timer", migrated.Nodes[1].Routes[0].Where["timer_id"]);
        Assert.Equal(exactLegacyBytes, File.ReadAllBytes(draftPath));

        migrated = Save(service, migrated);
        using var canonical = JsonDocument.Parse(File.ReadAllBytes(draftPath));
        Assert.Equal(3, canonical.RootElement.GetProperty("schema_version").GetInt32());
        var canonicalNodes = canonical.RootElement.GetProperty("nodes");
        var canonicalChat = canonicalNodes[0].GetProperty("routes")[0];
        var canonicalTimer = canonicalNodes[1].GetProperty("routes")[0];
        Assert.Equal("peer", canonicalChat.GetProperty("where").GetProperty("actor_role").GetString());
        Assert.False(canonicalChat.TryGetProperty("actor_role", out _));
        Assert.Equal("ritual-timer", canonicalTimer.GetProperty("where").GetProperty("timer_id").GetString());
        Assert.False(canonicalTimer.TryGetProperty("timer_id", out _));
    }

    [Fact]
    public void Guided_rehearsal_generates_specific_repeats_advances_real_timers_and_reports_uncovered_branches()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        project.Nodes = new()
        {
            new StudioNode
            {
                Id = "start", Label = "Start", X = 100, Y = 100,
                Routes = new()
                {
                    new StudioRoute
                    {
                        Id = "default-kills", Priority = 200, Event = "kill", RepeatCount = 2,
                        WithinSeconds = 30, DestinationNodeId = "wait",
                        Actions = new()
                        {
                            new StudioAction { Id = "start-timer", Type = "timer_start", TimerId = "finish", Seconds = 7 }
                        }
                    },
                    new StudioRoute
                    {
                        Id = "alternate-sign", Priority = 100, Event = "sign_written", Target = "sign",
                        Outcome = "fail", Actions = new()
                    }
                }
            },
            new StudioNode
            {
                Id = "wait", Label = "Wait", X = 350, Y = 100,
                Routes = new()
                {
                    new StudioRoute
                    {
                        Id = "finish-timer", Event = "timer_elapsed", TimerId = "finish",
                        Outcome = "complete", Actions = new()
                    }
                }
            }
        };
        project.EntryNodeId = "start";
        project = Save(service, project);

        var result = service.Rehearse(project.ProjectId, new StudioRehearsalRequest { Mode = "guided" });
        Assert.True(result.Ok, result.Error);
        Assert.Equal("complete", result.Outcome);
        Assert.Collection(result.GeneratedSteps,
            first => AssertSyntheticKill(first),
            second => AssertSyntheticKill(second),
            advance =>
            {
                Assert.Equal("advance", advance.Kind);
                Assert.Equal(7, advance.Seconds);
            });
        Assert.Contains(result.Trace, value => value.EventName == "kill" && value.Status == "ignored"
            && value.CurrentCount == 1 && value.RequiredCount == 2);
        Assert.Contains(result.Trace, value => value.EventName == "timer_elapsed" && value.Status == "matched");
        Assert.Empty(result.Timers);
        Assert.Contains(result.Limitations, value => value.Contains("followed highest priority route default-kills", StringComparison.Ordinal));
        Assert.Equal(new[] { "start:alternate-sign", "start:default-kills" },
            result.AvailablePaths.OrderBy(value => value, StringComparer.Ordinal));

        static void AssertSyntheticKill(StudioRehearsalInput input)
        {
            Assert.Equal("event", input.Kind);
            Assert.Equal("kill", input.EventName);
            Assert.Equal("$enemy_greyling", input.Target);
            Assert.Equal("Axes", input.Fields["weapon_skill"]);
            Assert.Equal("true", input.Fields["projectile"]);
        }
    }

    [Fact]
    public void Captured_fixture_proof_level_requires_the_exact_canonical_steps()
    {
        var service = CreateService();
        var project = service.CreateProject("cooperative-ritual");
        var spoofed = service.Rehearse(project.ProjectId, new StudioRehearsalRequest
        {
            Mode = "manual",
            ScenarioId = "captured-1.6",
            Steps = new() { new StudioRehearsalInput { Kind = "event", EventName = "sign_written", Target = "sign" } }
        });
        Assert.True(spoofed.Ok, spoofed.Error);
        Assert.Equal("rehearsal", spoofed.ProofLevel);

        var exact = service.Rehearse(project.ProjectId, new StudioRehearsalRequest
        {
            Mode = "manual",
            ScenarioId = "captured-1.6",
            Steps = new()
            {
                new StudioRehearsalInput { Kind = "event", EventName = "chat_received", Target = "shout", Fields = new() { ["actor_role"] = "peer" } },
                new StudioRehearsalInput { Kind = "event", EventName = "piece_placed", Target = "sign", Fields = new() { ["actor_role"] = "listen_host" } }
            }
        });
        Assert.True(exact.Ok, exact.Error);
        Assert.Equal("captured_contract_fixture", exact.ProofLevel);
    }

    StudioProjectDocument Save(QuestStudioService service, StudioProjectDocument project)
    {
        var result = service.SaveDraft(project.ProjectId, new StudioSaveRequest(project.Revision, project));
        Assert.True(result.Ok, result.Error);
        return Assert.IsType<StudioProjectDocument>(result.Project);
    }

    QuestStudioService CreateService() => new(_host, new QuestPackPublisher(_host));

    static string Diagnostics(StudioCertificationResult result) =>
        string.Join("; ", result.Diagnostics.Select(value => $"{value.Code} {value.Path}: {value.Message}"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    sealed class TestHost(string stateDirectory) : IQuestStudioHost
    {
        public string StateDirectory { get; } = stateDirectory;
        public string? FindValheim() => null;
        public bool Authorize(HttpRequest request) => true;
        public JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }
}

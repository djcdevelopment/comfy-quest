using System.Text.Json;
using Comfy.Quest.Studio;
using ComfyQuestContracts;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Comfy.Quest.Studio.Tests;

public sealed class QuestStudioServiceTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "comfy-quest-studio-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Starter_is_the_live_two_stage_cooperative_ritual()
    {
        var project = QuestStudioProject.Starter();
        Assert.Equal("Two Voices, One Rune", project.Title);
        Assert.Equal("sign", project.BindingTargetKind);
        Assert.Collection(project.Stages!,
            first =>
            {
                Assert.Equal("chat_received", first.Event);
                Assert.Equal("shout", first.Target);
                Assert.Equal("peer", first.ActorRole);
            },
            second =>
            {
                Assert.Equal("piece_placed", second.Event);
                Assert.Equal("sign", second.Target);
                Assert.Equal("listen_host", second.ActorRole);
            });
    }

    [Fact]
    public void Certify_compiles_a_linear_multi_stage_graph()
    {
        var service = CreateService();
        var result = service.Certify(QuestStudioProject.Starter());
        Assert.True(result.Ok, result.Error);
        using var document = JsonDocument.Parse(result.ExperienceJson!);
        var root = document.RootElement;
        Assert.Equal("await-peer-shout", root.GetProperty("entry_stage").GetString());
        var stages = root.GetProperty("stages");
        Assert.Equal(2, stages.GetArrayLength());
        var first = stages[0].GetProperty("transitions")[0];
        Assert.Equal("peer", first.GetProperty("when").GetProperty("where").GetProperty("actor_role").GetString());
        Assert.Equal("await-host-sign", first.GetProperty("next_stage").GetString());
        var second = stages[1].GetProperty("transitions")[0];
        Assert.Equal("complete", second.GetProperty("outcome").GetString());
    }

    [Fact]
    public void Chat_stage_requires_an_explicit_actor_role()
    {
        var project = QuestStudioProject.Starter() with
        {
            Stages = new[] { new QuestStudioStage("start", "chat_received", "shout", null, "Wake.") }
        };
        var result = CreateService().Certify(project);
        Assert.False(result.Ok);
        Assert.Equal("chat_actor_role_required", result.Error);
    }

    [Fact]
    public void Studio_rejects_catalog_events_without_a_runtime_adapter()
    {
        var project = QuestStudioProject.Starter() with
        {
            Stages = new[] { new QuestStudioStage("start", "item_picked_up", "Wood", null, "Picked up.") }
        };
        var result = CreateService().Certify(project);
        Assert.False(result.Ok);
        Assert.Equal("event_unknown", result.Error);
    }

    [Fact]
    public void Host_placement_cannot_be_mislabeled_as_a_peer_event()
    {
        var project = QuestStudioProject.Starter() with
        {
            Stages = new[] { new QuestStudioStage("start", "piece_placed", "sign", "peer", "Placed.") }
        };
        var result = CreateService().Certify(project);
        Assert.False(result.Ok);
        Assert.Equal("piece_placed_listen_host_required", result.Error);
    }

    [Fact]
    public void Legacy_single_stage_projects_still_certify()
    {
        var project = new QuestStudioProject("legacy", "1.0.0", "legacy", "Legacy", "kill", "$enemy_greyling", "Done.");
        var result = CreateService().Certify(project);
        Assert.True(result.Ok, result.Error);
        using var document = JsonDocument.Parse(result.ExperienceJson!);
        Assert.Single(document.RootElement.GetProperty("stages").EnumerateArray());
    }

    [Fact]
    public async Task Publish_writes_a_certified_pack_to_the_local_runtime_inbox()
    {
        var valheim = Path.Combine(_root, "Valheim");
        Directory.CreateDirectory(valheim);
        var host = new FakeHost(_root, valheim);
        var service = new QuestStudioService(host, new QuestPackPublisher(host));
        var result = await service.PublishAsync(QuestStudioProject.Starter(), CancellationToken.None);
        Assert.True(result.Ok, result.Error);
        Assert.Equal("published", result.Status);
        Assert.True(File.Exists(Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime", "inbox", result.Receipt!.Filename!)));
    }

    [Fact]
    public void Legacy_live_1_7_project_migrates_once_and_preserves_its_content_hash()
    {
        var state = Path.Combine(_root, "quest-studio");
        Directory.CreateDirectory(state);
        var legacy = new QuestStudioProject(
            "studio-two-voices-one-rune", "1.7.0", "two-voices-one-rune", "Two Voices, One Rune",
            "chat_received", "shout", "A distant voice wakes the Charm.",
            BindingTargetKind: "sign",
            Stages: new[]
            {
                new QuestStudioStage("await-peer-shout", "chat_received", "shout", "peer", "A distant voice wakes the Charm."),
                new QuestStudioStage("await-host-sign", "piece_placed", "sign", "listen_host", "The host raises the answering rune. now inscribed final rune"),
                new QuestStudioStage("inscribe-final-rune", "sign_written", null, null, "The quest advances.Two voices and one rune are joined. The ritual is complete.")
            });
        var host = new FakeHost(_root, null);
        File.WriteAllText(Path.Combine(state, "project.json"), JsonSerializer.Serialize(legacy, host.Json));

        var first = new QuestStudioService(host, new QuestPackPublisher(host));
        var migrated = Assert.Single(first.ListProjects());
        var certification = first.CertifyGraph(migrated.ProjectId);
        Assert.True(certification.Ok, certification.Error);
        Assert.Equal("374c43056f479089fca1faf680a3a074b55db0bcc098884b5c212cce0118bab1", certification.ContentHash);

        var reopened = new QuestStudioService(host, new QuestPackPublisher(host));
        Assert.Single(reopened.ListProjects());
        Assert.True(File.Exists(Path.Combine(state, "project.json")));
    }

    [Fact]
    public void Autosave_accepts_incomplete_drafts_and_rejects_stale_revisions()
    {
        var service = CreateService();
        var created = service.CreateProject("blank");
        created.Nodes[0].Id = string.Empty;
        var saved = service.SaveDraft(created.ProjectId, new StudioSaveRequest(created.Revision, created));
        Assert.True(saved.Ok, saved.Error);
        Assert.Equal(2, saved.Project!.Revision);
        Assert.False(service.CertifyGraph(created.ProjectId).Ok);

        created.Title = "A stale browser edit";
        var conflict = service.SaveDraft(created.ProjectId, new StudioSaveRequest(1, created));
        Assert.True(conflict.Conflict);
        Assert.Equal(2, conflict.Project!.Revision);
        Assert.NotEqual("A stale browser edit", conflict.Project.Title);
    }

    [Fact]
    public void Guided_branch_compiles_to_prioritized_acyclic_routes()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        project.Nodes.Add(new StudioNode
        {
            Id = "alternate", Label = "Alternate", X = 400, Y = 260,
            Routes = new() { new StudioRoute { Id = "alternate-finish", Priority = 100, Event = "kill", Target = "$enemy_greyling", Outcome = "fail", Actions = new() } }
        });
        project.Nodes[0].Routes.Insert(0, new StudioRoute
        {
            Id = "peer-branch", Priority = 200, Event = "chat_received", Target = "shout", ActorRole = "peer",
            DestinationNodeId = "alternate", Actions = new() { new StudioAction { Id = "message-peer", Type = "message", Text = "A peer changes the path." } }
        });
        Assert.True(service.SaveDraft(project.ProjectId, new StudioSaveRequest(project.Revision, project)).Ok);
        var result = service.CertifyGraph(project.ProjectId);
        Assert.True(result.Ok, string.Join("; ", result.Diagnostics.Select(x => x.Code)));
        var first = result.Document!.Stages.Single(x => x.Id == "start").Transitions;
        Assert.Equal(200, first[0].Priority);
        Assert.Equal("alternate", first[0].NextStage);
        Assert.Equal("fail", result.Document.Stages.Single(x => x.Id == "alternate").Transitions[0].Outcome);
    }

    [Fact]
    public void Reward_template_rehearses_grant_spawn_timer_and_marked_cleanup()
    {
        var service = CreateService();
        var project = service.CreateProject("reward-cleanup");
        var result = service.Rehearse(project.ProjectId, new StudioRehearsalRequest
        {
            ScenarioId = "reward-cleanup",
            Steps = new()
            {
                new StudioRehearsalInput { Kind = "event", EventName = "sign_written", Target = "sign" },
                new StudioRehearsalInput { Kind = "advance", Seconds = 5 }
            }
        });
        Assert.True(result.Ok, result.Error);
        Assert.Equal("complete", result.Outcome);
        Assert.Equal(1, result.Inventory["Wood"]);
        Assert.Empty(result.Spawns);
        Assert.Empty(result.Timers);
        Assert.Collection(result.Trace,
            first => Assert.Contains(first.Effects, effect => effect.Contains("spawn 1 wood_floor")),
            second => Assert.Contains(second.Effects, effect => effect.Contains("clear 1 from raise-floor")));
    }

    [Fact]
    public void Captured_multiplayer_scenario_is_labeled_as_fixture_not_live_proof()
    {
        var service = CreateService();
        var project = service.CreateProject("cooperative-ritual");
        var result = service.Rehearse(project.ProjectId, new StudioRehearsalRequest
        {
            ScenarioId = "captured-1.6",
            Steps = new()
            {
                new StudioRehearsalInput { Kind = "event", EventName = "chat_received", Target = "shout", ActorRole = "peer" },
                new StudioRehearsalInput { Kind = "event", EventName = "piece_placed", Target = "sign", ActorRole = "listen_host" }
            }
        });
        Assert.Equal("complete", result.Outcome);
        Assert.Equal("captured_contract_fixture", result.ProofLevel);
        Assert.Contains("does not prove", result.Disclaimer);
    }

    [Fact]
    public void Clear_action_must_select_a_spawn_owned_by_the_same_quest()
    {
        var service = CreateService();
        var project = service.CreateProject("blank");
        project.Nodes[0].Routes[0].Actions = new()
        {
            new StudioAction { Id = "clear", Type = "clear_spawned", ActionId = "some-other-quest-spawn" }
        };
        Assert.True(service.SaveDraft(project.ProjectId, new StudioSaveRequest(project.Revision, project)).Ok);
        var result = service.CertifyGraph(project.ProjectId);
        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, value => value.Code == "clear_spawn_reference_invalid");
    }

    [Fact]
    public async Task Runtime_cockpit_advances_from_publish_through_check_and_load()
    {
        var valheim = Path.Combine(_root, "Valheim");
        Directory.CreateDirectory(valheim);
        var host = new FakeHost(_root, valheim);
        var service = new QuestStudioService(host, new QuestPackPublisher(host));
        var project = service.CreateProject("blank");
        var published = await service.PublishGraphAsync(project.ProjectId, CancellationToken.None);
        Assert.True(published.Ok, published.Error);
        Assert.Equal("published", service.RuntimeStatus(project.ProjectId).Phase);

        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        new RuntimeReceiptStore(runtimeRoot).Write(new RuntimeReceipt { Operation = "check", Status = "accepted", Diagnostics = Array.Empty<ContractDiagnostic>() });
        Assert.Equal("checked", service.RuntimeStatus(project.ProjectId).Phase);

        var candidate = new QuestPackStore(runtimeRoot).LoadLatest();
        new RuntimeReceiptStore(runtimeRoot).Write(new RuntimeReceipt { Operation = "load", Status = "activated", PackId = candidate.Manifest.PackId, Version = candidate.Manifest.Version, ContentHash = candidate.ContentHash, Diagnostics = Array.Empty<ContractDiagnostic>() });
        Assert.Equal("active", service.RuntimeStatus(project.ProjectId).Phase);
    }

    [Fact]
    public async Task Published_content_is_immutable_and_new_iteration_bumps_the_patch()
    {
        var valheim = Path.Combine(_root, "Valheim");
        Directory.CreateDirectory(valheim);
        var host = new FakeHost(_root, valheim);
        var service = new QuestStudioService(host, new QuestPackPublisher(host));
        var project = service.CreateProject("blank");
        Assert.True((await service.PublishGraphAsync(project.ProjectId, CancellationToken.None)).Ok);

        project.Nodes[0].Routes[0].Actions[0].Text = "Changed immutable content.";
        var saved = service.SaveDraft(project.ProjectId, new StudioSaveRequest(project.Revision, project));
        Assert.True(saved.Ok, saved.Error);
        var collision = await service.PublishGraphAsync(project.ProjectId, CancellationToken.None);
        Assert.False(collision.Ok);
        Assert.Equal("same_version_hash_collision", collision.Error);

        var bumped = service.BumpPatch(project.ProjectId, saved.Project!.Revision);
        Assert.True(bumped.Ok, bumped.Error);
        Assert.Equal("1.0.1", bumped.Project!.Version);
        Assert.True((await service.PublishGraphAsync(project.ProjectId, CancellationToken.None)).Ok);
    }

    QuestStudioService CreateService()
    {
        var host = new FakeHost(_root, null);
        return new QuestStudioService(host, new QuestPackPublisher(host));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    sealed class FakeHost(string stateDirectory, string? valheim) : IQuestStudioHost
    {
        public string StateDirectory { get; } = stateDirectory;
        public string? FindValheim() => valheim;
        public bool Authorize(HttpRequest request) => true;
        public JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }
}

using System.Text.Json;
using Comfy.Quest.Studio;
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

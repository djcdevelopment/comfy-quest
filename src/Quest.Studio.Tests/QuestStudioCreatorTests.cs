using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Comfy.Quest.Studio;
using ComfyQuestContracts;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Xunit;

namespace Quest.Studio.Tests;

public sealed class QuestStudioCreatorTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(),
        "comfy-quest-creator-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EmbeddedRendererMatchesThePinnedStewardArtifact()
    {
        var bytes = Encoding.UTF8.GetBytes(QuestStudioCreatorRenderer.Js);
        Assert.Equal(QuestStudioCreatorRenderer.ArtifactSha256, Hash(bytes));
        Assert.Contains("comfy-steward-creator-renderer/v1", QuestStudioCreatorRenderer.Js);
    }

    [Fact]
    public async Task StewardSceneIsPulledWithServerTokenAndPersistedAsExactMembership()
    {
        Directory.CreateDirectory(_root);
        var bytes = SceneBytes(17);
        var handler = new StubHandler(request =>
        {
            Assert.Equal("operator-secret", request.Headers.GetValues("X-Steward-Quest-Token").Single());
            Assert.Contains("/api/creator/scene?", request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
                {
                    Headers = { ContentType = new("application/vnd.comfysteward.authoring-scene") },
                },
            };
        });
        var host = new FakeHost(_root);
        var publisher = new QuestPackPublisher(host);
        var workspace = new QuestStudioWorkspace(host, publisher);
        var creator = new QuestStudioCreator(host, workspace,
            new QuestStudioRunControl(host, workspace), handler);

        var result = await creator.FetchSceneAsync(new(42, -10, 10, -10, 10), default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1, result.PieceCount);
        Assert.Equal(1, result.InstanceCount);
        Assert.Equal(bytes, result.Bytes);
        var receiptPath = Path.Combine(_root, "quest-studio", "creator", "scenes",
            result.SceneId + ".json");
        var receipt = File.ReadAllText(receiptPath);
        Assert.Contains("17", receipt);
        Assert.DoesNotContain("operator-secret", receipt);
        var retained = creator.RetainedScene(result.SceneId!);
        Assert.True(retained.Ok, retained.Error);
        Assert.Equal(bytes, retained.Bytes);
        var binaryPath = Path.ChangeExtension(receiptPath, ".svca");
        var tampered = bytes.ToArray();
        tampered[^1] ^= 1;
        File.WriteAllBytes(binaryPath, tampered);
        Assert.Equal("creator_scene_hash_mismatch", creator.RetainedScene(result.SceneId!).Error);
    }

    [Fact]
    public void FieldLodgeTemplateCompilesAsDurableRiteAndQuickCastCompanion()
    {
        var project = QuestStudioWorkspace.FieldLodgeOfferingTemplate("project-field-lodge", "lodge1");
        var durable = StudioGraphCompiler.Compile(project);

        Assert.True(durable.Ok, durable.Error);
        var route = durable.Document!.Stages.Single().Transitions.Single();
        Assert.Equal("item_dropped", route.When.Children[0].Event);
        Assert.Equal("Resin", route.When.Children[0].Target);
        Assert.Equal("within_radius", route.When.Children[1].Spatial);
        Assert.Equal("The lodge answers the offering.",
            (string?)route.Actions[0].Parameters["text"]);
        Assert.Equal("Greyling", (string?)route.Actions[1].Parameters["prefab"]);
        Assert.Equal(1, (int?)route.Actions[1].Parameters["count"]);
        Assert.Equal(6, (int?)route.Actions[1].Parameters["radius"]);

        var quickId = QuestStudioCreatorCast.QuickExperienceId(project, route.Id);
        var quick = QuestStudioCreatorCast.BuildQuickExperience(project, route, quickId);
        var compiled = ExperienceCompiler.CompileProductionJson(
            JsonConvert.SerializeObject(quick, Formatting.Indented));
        Assert.True(compiled.IsValid);
        Assert.Equal("experience_started", quick.Stages[0].Transitions[0].When.Event);
        Assert.Equal("held", quick.Stages[0].Transitions[0].NextStage);
        Assert.Empty(quick.Stages[1].Transitions);
    }

    [Fact]
    public async Task HeldQuickCastSurvivesReloadAndPreventsASecondCast()
    {
        Directory.CreateDirectory(_root);
        var host = new FakeHost(_root);
        var publisher = new QuestPackPublisher(host);
        var workspace = new QuestStudioWorkspace(host, publisher);
        var project = workspace.CreateProject("field-lodge-offering");
        var runControl = new QuestStudioRunControl(host, workspace);
        var creator = new QuestStudioCreator(host, workspace, runControl);
        var casts = new QuestStudioCreatorCast(host, publisher, workspace, runControl, creator);
        var receipt = new StudioCreatorCastReceipt
        {
            CastId = "cast-reload-proof",
            State = "held",
            Mode = "quick_cast",
            StartedUtc = DateTimeOffset.UtcNow,
            ProjectId = project.ProjectId,
            ProjectRevision = project.Revision,
            BindingZdo = "42:17",
            ExperienceId = "creator-cast-reload-proof",
            ContentHash = new string('c', 64),
            CreatorSessionId = "session-reload-proof",
            Machine = "am4",
            WorldUid = "777",
            BindingChangeId = "binding-change-reload-proof",
            RunId = "run-reload-proof",
        };
        var castRoot = Path.Combine(_root, "quest-studio", "creator", "casts");
        Directory.CreateDirectory(castRoot);
        File.WriteAllText(Path.Combine(castRoot, receipt.CastId + ".json"),
            System.Text.Json.JsonSerializer.Serialize(receipt, host.Json));

        var status = casts.Status(project.ProjectId);
        var duplicate = await casts.CastAsync(project.ProjectId,
            new(project.Revision, null, "quick_cast"), default);

        Assert.True(status.Ok);
        Assert.Equal(receipt.CastId, status.CastId);
        Assert.Equal("held", status.State);
        Assert.False(duplicate.Ok);
        Assert.Equal("quick_cast_already_held", duplicate.Error);
        Assert.Equal(receipt.CastId, duplicate.CastId);
    }

    static byte[] SceneBytes(uint identity)
    {
        var instances = new byte[80];
        var identities = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(identities, identity);
        var manifest = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "steward-zdo-authoring-scene/v1",
            snapshotId = 42,
            worldId = "quest-demo-world",
            fileSha256 = new string('a', 64),
            producerRevision = new string('b', 40),
            pieces = 1,
            renderInstances = 1,
            instanceBytes = instances.Length,
            instanceSha256 = Hash(instances),
            identityStride = 4,
            identityCount = 1,
            identityBytes = identities.Length,
            identitySha256 = Hash(identities),
            absoluteOrigin = new[] { 1d, 2d, 3d },
            lens = "build-density",
            exact = true,
            forced = false,
            instanceStride = 80,
            scope = new { minX = -10d, maxX = 10d, minZ = -10d, maxZ = 10d, biomes = Array.Empty<string>() },
        });
        var instanceOffset = (20 + manifest.Length + 3) & ~3;
        var identityOffset = instanceOffset + instances.Length;
        var result = new byte[identityOffset + identities.Length];
        Encoding.ASCII.GetBytes("SVCA").CopyTo(result, 0);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8, 4), manifest.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12, 4), instanceOffset);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16, 4), identityOffset);
        manifest.CopyTo(result, 20);
        instances.CopyTo(result, instanceOffset);
        identities.CopyTo(result, identityOffset);
        return result;
    }

    static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(send(request));
    }

    sealed class FakeHost(string stateDirectory) : IQuestStudioHost, IQuestStudioStewardHost
    {
        public string StateDirectory { get; } = stateDirectory;
        public string? FindValheim() => null;
        public bool Authorize(HttpRequest request) => true;
        public JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
        public QuestStudioStewardConnection? StewardConnection { get; } = new(
            new Uri("http://127.0.0.1:8040"), new Uri("http://127.0.0.1:8003"),
            "operator-secret");
    }
}

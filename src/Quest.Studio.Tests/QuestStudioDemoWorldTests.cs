using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Comfy.Quest.Studio;
using ComfyQuestContracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Comfy.Quest.Studio.Tests;

public sealed class QuestStudioDemoWorldTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "comfy-quest-demo-world-tests", Guid.NewGuid().ToString("N"));
    readonly FakeHost _host;
    readonly QuestStudioService _service;

    public QuestStudioDemoWorldTests()
    {
        _host = new FakeHost(_root);
        _service = new QuestStudioService(_host, new QuestPackPublisher(_host));
    }

    [Fact]
    public void Import_opens_a_valid_schema_v3_project_as_a_semantically_equal_fork()
    {
        var source = _service.CreateProject("demo-world-first-portal");
        var sourceBefore = JsonSerializer.Serialize(source, _host.Json);

        var result = _service.ImportProject(Request(source));

        Assert.True(result.Ok, result.Error);
        var fork = Assert.IsType<StudioProjectDocument>(result.Project);
        Assert.NotEqual(source.ProjectId, fork.ProjectId);
        Assert.NotEqual(source.PackId, fork.PackId);
        Assert.NotEqual(source.ExperienceId, fork.ExperienceId);
        Assert.Contains("-fork-", fork.PackId, StringComparison.Ordinal);
        Assert.Contains("-fork-", fork.ExperienceId, StringComparison.Ordinal);
        Assert.Equal(1, fork.Revision);
        Assert.Equal(source.Version, fork.Version);
        Assert.Equal(source.Title, fork.Title);
        Assert.Equal(JsonSerializer.Serialize(source.Nodes, _host.Json), JsonSerializer.Serialize(fork.Nodes, _host.Json));
        Assert.Equal(source.BindingTargetKind, fork.BindingTargetKind);
        Assert.Equal(source.BindingTargetKinds, fork.BindingTargetKinds);
        Assert.Equal(source.EntryNodeId, fork.EntryNodeId);
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(source, _host.Json));
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(_service.ReadProject(source.ProjectId), _host.Json));
        var usage = Assert.Single(_service.UsageReport().Weeks).Value;
        Assert.Equal(1, usage.Templates["demo-world-first-portal"]);
        Assert.Equal(1, usage.Outcomes["import.accepted"]);
    }

    [Fact]
    public void Import_rejects_non_v3_and_server_side_draft_bounds()
    {
        var source = _service.CreateProject("demo-world-first-portal");
        var missingSchemaNode = JsonNode.Parse(JsonSerializer.Serialize(source, _host.Json))!.AsObject();
        missingSchemaNode.Remove("schema_version");
        var missingSchema = _service.ImportProject(Request(missingSchemaNode));
        Assert.False(missingSchema.Ok);
        Assert.Equal("project_schema_unsupported", missingSchema.Error);

        source.SchemaVersion = 2;
        var oldSchema = _service.ImportProject(Request(source));
        Assert.False(oldSchema.Ok);
        Assert.Equal("project_schema_unsupported", oldSchema.Error);

        source.SchemaVersion = 3;
        source.Nodes[0].Label = new string('x', 121);
        var fieldBounds = _service.ImportProject(Request(source));
        Assert.False(fieldBounds.Ok);
        Assert.Equal("draft_field_bounds", fieldBounds.Error);

        source.Nodes[0].Label = "Take the World portal";
        source.Nodes = Enumerable.Range(0, ExperienceSchema.MaxStages + 1)
            .Select(index => new StudioNode { Id = "node-" + index, Label = "Node", Routes = new() })
            .ToList();
        var nodeBounds = _service.ImportProject(Request(source));
        Assert.False(nodeBounds.Ok);
        Assert.Equal("draft_node_bounds", nodeBounds.Error);

        var oversizedNode = JsonNode.Parse(JsonSerializer.Serialize(source, _host.Json))!.AsObject();
        oversizedNode["unknown_payload"] = new string('x', 1024 * 1024);
        var oversized = _service.ImportProject(Request(oversizedNode));
        Assert.False(oversized.Ok);
        Assert.Equal("draft_too_large", oversized.Error);
    }

    [Fact]
    public void Import_never_treats_source_ids_as_paths_and_rejects_pathlike_contract_ids()
    {
        var source = _service.CreateProject("demo-world-first-portal");
        source.ProjectId = @"..\..\outside-project";
        var forked = _service.ImportProject(Request(source));
        Assert.True(forked.Ok, forked.Error);
        Assert.Matches("^project-[a-f0-9]{12}$", forked.Project!.ProjectId);
        Assert.True(File.Exists(Path.Combine(_root, "quest-studio", "projects", forked.Project.ProjectId, "draft.json")));
        Assert.False(File.Exists(Path.Combine(_root, "outside-project", "draft.json")));

        source.PackId = @"..\outside-pack";
        var rejected = _service.ImportProject(Request(source));
        Assert.False(rejected.Ok);
        Assert.Equal("graph_invalid", rejected.Error);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "stable_id_invalid");
        Assert.False(Directory.Exists(Path.Combine(_root, "outside-pack")));

        var pathField = JsonNode.Parse(JsonSerializer.Serialize(_service.CreateProject("demo-world-first-portal"), _host.Json))!.AsObject();
        pathField["path"] = @"C:\arbitrary\server\project.json";
        var unknownPath = _service.ImportProject(Request(pathField));
        Assert.False(unknownPath.Ok);
        Assert.Equal("project_schema_invalid", unknownPath.Error);
    }

    [Fact]
    public async Task Import_endpoint_caps_the_raw_request_before_model_binding()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_service);
        builder.Services.AddSingleton(new QuestPackPublisher(_host));
        await using var app = builder.Build();
        QuestStudioEndpoints.Map(app, _host);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(value => value.RoutePattern.RawText == "/api/v2/quest-studio/projects/import");
        var limit = Assert.IsAssignableFrom<IRequestSizeLimitMetadata>(
            endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>());
        Assert.Equal(1024 * 1024 + 1024, limit.MaxRequestBodySize);
    }

    [Fact]
    public void Repeated_imports_cannot_collide_or_overwrite_the_source_identity()
    {
        var source = _service.CreateProject("demo-world-first-portal");
        var first = Assert.IsType<StudioProjectDocument>(_service.ImportProject(Request(source)).Project);
        var second = Assert.IsType<StudioProjectDocument>(_service.ImportProject(Request(source)).Project);
        var projects = _service.ListProjects();

        Assert.Equal(3, projects.Count);
        Assert.Equal(3, projects.Select(project => project.ProjectId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, projects.Select(project => project.PackId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, new[] { source.ExperienceId, first.ExperienceId, second.ExperienceId }.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(source.PackId, _service.ReadProject(source.ProjectId)!.PackId);

        first.Nodes[0].Routes[0].Actions[0].Text = "A later edit on this fork.";
        var saved = _service.SaveDraft(first.ProjectId, new StudioSaveRequest(first.Revision, first));
        Assert.True(saved.Ok, saved.Error);
        Assert.Equal(first.PackId, saved.Project!.PackId);
        Assert.Equal(first.ExperienceId, saved.Project.ExperienceId);
    }

    [Fact]
    public void Create_only_atomic_write_never_overwrites_an_existing_draft()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "atomic-write-collision"));
        var target = Path.Combine(directory.FullName, "draft.json");
        File.WriteAllText(target, "original");
        var workspace = typeof(QuestStudioService).Assembly.GetType("Comfy.Quest.Studio.QuestStudioWorkspace");
        var atomicWrite = workspace!.GetMethod(
            "AtomicWrite",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => atomicWrite!.Invoke(null, new object[] { target, "replacement", true }));

        Assert.IsType<IOException>(thrown.InnerException);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(directory.FullName, "draft.json.tmp-*"));
    }

    [Fact]
    public void Built_in_minimal_tutorial_source_compiled_artifact_and_runtime_v2_pack_do_not_drift()
    {
        var bundle = BundleRoot();
        var source = JsonSerializer.Deserialize<StudioProjectDocument>(
            File.ReadAllText(Path.Combine(bundle, "studio-project.json")), _host.Json)!;
        var template = _service.CreateProject("demo-world-first-portal");

        template.ProjectId = source.ProjectId;
        template.Revision = source.Revision;
        template.UpdatedUtc = source.UpdatedUtc;
        template.PackId = source.PackId;
        template.ExperienceId = source.ExperienceId;
        Assert.True(JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(source, _host.Json),
            JsonSerializer.SerializeToNode(template, _host.Json)));

        using var catalog = JsonDocument.Parse(JsonSerializer.Serialize(_service.WorkspaceCatalog(), _host.Json));
        var catalogTemplate = catalog.RootElement.GetProperty("templates").EnumerateArray()
            .Single(value => value.GetProperty("id").GetString() == "demo-world-first-portal");
        Assert.True(catalogTemplate.GetProperty("minimal_tutorial").GetBoolean());

        var persisted = _service.CreateProject("demo-world-first-portal");
        persisted.PackId = source.PackId;
        persisted.ExperienceId = source.ExperienceId;
        var saved = _service.SaveDraft(persisted.ProjectId, new StudioSaveRequest(persisted.Revision, persisted));
        Assert.True(saved.Ok, saved.Error);
        var certified = _service.ValidateGraph(persisted.ProjectId);
        Assert.True(certified.Ok, certified.Error);
        var expectedExperience = File.ReadAllText(Path.Combine(bundle, "experience.json"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedExperience), JsonNode.Parse(certified.ExperienceJson!)));
        Assert.True(ExperienceCompiler.CompileProductionJson(expectedExperience).IsValid);

        var checkedPack = File.ReadAllBytes(Path.Combine(bundle, "demo-world-first-portal-1.0.0.questpack"));
        var runtimeRoot = Path.Combine(_root, "runtime-contract");
        var inbox = Directory.CreateDirectory(Path.Combine(runtimeRoot, "inbox")).FullName;
        File.WriteAllBytes(Path.Combine(inbox, "demo-world-first-portal-1.0.0.questpack"), checkedPack);
        var candidate = Assert.Single(new QuestPackStore(runtimeRoot).CheckInbox());
        Assert.True(candidate.IsValid, string.Join("; ", candidate.Diagnostics.Select(value => value.Code)));
        Assert.Equal("comfy-quest-pack/v2", candidate.Manifest.Schema);
        Assert.Equal("demo-world-first-portal", candidate.Manifest.PackId);
        Assert.Equal("1.0.0", candidate.Manifest.Version);

        using var archive = new ZipArchive(new MemoryStream(checkedPack), ZipArchiveMode.Read);
        using var reader = new StreamReader(Assert.IsType<ZipArchiveEntry>(
            archive.GetEntry("experiences/demo-world-first-portal.json")).Open());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedExperience), JsonNode.Parse(reader.ReadToEnd())));
        Assert.Null(archive.GetEntry("quest.json"));
    }

    [Fact]
    public void Tutorial_expectations_pin_exact_activation_cast_rebind_receipts_and_demo_world_landmarks()
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(BundleRoot(), "expected.json")));
        var root = expected.RootElement;
        var world = root.GetProperty("demo_world");
        Assert.Equal("marble-grand", world.GetProperty("profile").GetString());
        Assert.Equal("generated_world_school_sign", world.GetProperty("binding_target").GetProperty("role").GetString());
        Assert.Equal("WORLD", world.GetProperty("binding_target").GetProperty("text_heading").GetString());
        Assert.Equal("ground welcome camp", world.GetProperty("unavoidable_ascent_portal").GetProperty("from").GetString());
        Assert.Equal("Creator Hub ascent portal", world.GetProperty("unavoidable_ascent_portal").GetProperty("to").GetString());
        Assert.Equal("unbound_no_progress", world.GetProperty("unavoidable_ascent_portal").GetProperty("imported_fork_state").GetString());
        Assert.Equal("World school paired portal", world.GetProperty("tutorial_completion_portal").GetProperty("role").GetString());

        var canonical = root.GetProperty("canonical_artifact");
        Assert.Equal("demo-world-first-portal", canonical.GetProperty("required_pack_id").GetString());
        Assert.Equal("load_selected", canonical.GetProperty("activation_receipt").GetProperty("operation").GetString());
        var canonicalPaths = canonical.GetProperty("accepted_paths").EnumerateArray().ToArray();
        Assert.Equal(new[] { "matching_prebound_ascent", "portable_cast_then_world_portal" },
            canonicalPaths.Select(value => value.GetProperty("id").GetString()));
        Assert.False(canonicalPaths[0].GetProperty("matching_prebound_save_is_live_proven").GetBoolean());
        Assert.True(canonicalPaths[1].GetProperty("fresh_cast_before_completion_portal").GetBoolean());

        var fork = root.GetProperty("imported_fork");
        Assert.True(fork.GetProperty("ascend_unbound_before_cast").GetBoolean());
        Assert.True(fork.GetProperty("fresh_cast_before_completion_portal").GetBoolean());
        var later = fork.GetProperty("later_same_fork_revision");
        Assert.True(later.GetProperty("pack_and_experience_ids_are_preserved").GetBoolean());
        Assert.Equal("rebound", later.GetProperty("changed_content_receipt").GetProperty("status").GetString());
        Assert.Equal("already_current", later.GetProperty("unchanged_content_receipt").GetProperty("error").GetString());
        Assert.Contains(fork.GetProperty("first_revision_receipts").EnumerateArray(), value =>
            value.GetProperty("operation").GetString() == "dev_rebind"
            && value.GetProperty("status").GetString() == "skipped"
            && value.GetProperty("error").GetString() == "no_loaded_binding");

        var behavior = root.GetProperty("behavior");
        Assert.Equal("player_teleported", behavior.GetProperty("event_name").GetString());
        Assert.Equal(JsonValueKind.Null, behavior.GetProperty("event_target").ValueKind);
        Assert.Equal(new[] { "event/matched", "action/executed", "transition/complete" },
            behavior.GetProperty("receipt_assertions").EnumerateArray()
                .Select(value => value.GetProperty("operation").GetString() + "/" + value.GetProperty("status").GetString()));
        Assert.False(root.GetProperty("replay_precondition").GetProperty("world_restore_alone_resets_completion").GetBoolean());
        Assert.False(root.GetProperty("replay_precondition").GetProperty("scoped_reset_is_live_proven").GetBoolean());
    }

    static string BundleRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "examples", "demo-world", "first-portal");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("examples/demo-world/first-portal was not found from the test output directory.");
    }

    StudioImportRequest Request(StudioProjectDocument project) =>
        new(JsonSerializer.SerializeToElement(project, _host.Json));

    static StudioImportRequest Request(JsonNode project)
    {
        using var document = JsonDocument.Parse(project.ToJsonString());
        return new(document.RootElement.Clone());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    sealed class FakeHost(string stateDirectory) : IQuestStudioHost
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

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Comfy.Quest.Studio;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Comfy.Quest.Studio.Tests;

public sealed class QuestStudioArchitecturalBuildTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "comfy-quest-build-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Valid_capsule_import_is_content_addressed_and_idempotent()
    {
        var service = CreateService(importer: true);
        var capsule = ArchitecturalCapsuleFixture.Create();

        var first = await service.ImportAsync(new MemoryStream(capsule), capsule.Length);
        var second = await service.ImportAsync(new MemoryStream(capsule), capsule.Length);

        Assert.True(first.Ok, first.Error);
        Assert.Equal(201, first.Status);
        Assert.NotNull(first.Build);
        Assert.Equal(40, first.Build.PieceCount);
        Assert.Equal(16, first.Build.PrefabCounts["wood_floor"]);
        Assert.Equal(16, first.Build.PrefabCounts["woodwall"]);
        Assert.Equal(8, first.Build.PrefabCounts["wood_roof_45"]);
        Assert.Equal(first.Build.BuildId, second.Build?.BuildId);
        Assert.True(second.AlreadyPresent);
        Assert.Equal(200, second.Status);
        Assert.Single(service.List());
        Assert.Equal(Sha256(capsule), first.Build.CapsuleSha256);
        Assert.Equal(first.Build.CandidatePiecesSha256, first.Build.CanonicalPiecesSha256);
    }

    [Theory]
    [InlineData("tampered", "capsule_hash_mismatch")]
    [InlineData("traversal", "capsule_member_invalid")]
    [InlineData("piece-count", "capsule_piece_count_mismatch")]
    [InlineData("failed-gate", "capsule_interpretation_gate_failed")]
    [InlineData("non-architectural", "capsule_not_architectural")]
    public async Task Invalid_capsules_are_rejected_before_persistence(string variant,
        string expectedError)
    {
        var service = CreateService(importer: true);
        var capsule = ArchitecturalCapsuleFixture.Create(variant);

        var result = await service.ImportAsync(new MemoryStream(capsule), capsule.Length);

        Assert.False(result.Ok);
        Assert.Equal(expectedError, result.Error);
        Assert.Empty(service.List());
    }

    [Fact]
    public async Task Compressed_and_expanded_size_limits_fail_closed()
    {
        var service = CreateService(importer: true);
        var compressed = await service.ImportAsync(Stream.Null,
            QuestStudioBuildService.MaxCompressedBytes + 1);
        Assert.Equal(413, compressed.Status);
        Assert.Equal("capsule_too_large", compressed.Error);

        var expandedZip = ArchitecturalCapsuleFixture.CreateExpandedBomb();
        Assert.True(expandedZip.Length < QuestStudioBuildService.MaxCompressedBytes);
        var expanded = await service.ImportAsync(new MemoryStream(expandedZip),
            expandedZip.Length);
        Assert.Equal("capsule_expanded_too_large", expanded.Error);
        Assert.Empty(service.List());
    }

    [Fact]
    public async Task Missing_authoritative_importer_is_an_explicit_scar()
    {
        var service = CreateService(importer: false);
        var capsule = ArchitecturalCapsuleFixture.Create();

        var result = await service.ImportAsync(new MemoryStream(capsule), capsule.Length);

        Assert.Equal(503, result.Status);
        Assert.Equal("architectural_importer_unavailable", result.Error);
        Assert.Empty(service.List());
    }

    [Fact]
    public async Task Placement_conflicts_do_not_drift_canonical_artifacts()
    {
        var service = CreateService(importer: true);
        var capsule = ArchitecturalCapsuleFixture.Create();
        var imported = await service.ImportAsync(new MemoryStream(capsule), capsule.Length);
        var original = Assert.IsType<StudioBuildDocument>(imported.Build);
        var canonical = CanonicalIdentity(original);

        var placed = service.Placement(original.BuildId,
            new StudioBuildPlacementRequest(1, 12.5, 1.25, -3.75, 22.5));
        var conflict = service.Placement(original.BuildId,
            new StudioBuildPlacementRequest(1, 0, 0, 0, 0));

        Assert.True(placed.Ok, placed.Error);
        Assert.Equal(2, placed.Build?.Revision);
        Assert.Equal(canonical, CanonicalIdentity(Assert.IsType<StudioBuildDocument>(placed.Build)));
        Assert.Equal(409, conflict.Status);
        Assert.Equal("revision_conflict", conflict.Error);
        Assert.Equal(2, conflict.Revision);
        Assert.Equal(canonical, CanonicalIdentity(Assert.IsType<StudioBuildDocument>(service.Read(original.BuildId))));
    }

    [Fact]
    public async Task Stage_is_exact_idempotent_collision_safe_and_never_writes_a_request()
    {
        var service = CreateService(importer: true);
        var capsule = ArchitecturalCapsuleFixture.Create();
        var imported = await service.ImportAsync(new MemoryStream(capsule), capsule.Length);
        var build = Assert.IsType<StudioBuildDocument>(imported.Build);

        var first = service.Stage(build.BuildId);
        var second = service.Stage(build.BuildId);

        Assert.True(first.Ok, first.Error);
        Assert.False(first.AlreadyPresent);
        Assert.True(second.Ok, second.Error);
        Assert.True(second.AlreadyPresent);
        var receipt = Assert.IsType<StudioBuildStageReceipt>(first.Receipt);
        Assert.False(receipt.PlacementApplied);
        Assert.False(receipt.CreatorSessionStarted);
        Assert.False(receipt.MailboxRequestWritten);
        Assert.False(receipt.WorldMutationPerformed);
        Assert.Equal(2, Directory.GetFiles(receipt.Destination).Length);
        Assert.All(receipt.Artifacts.Values, artifact =>
            Assert.Equal(artifact.Sha256, Sha256(File.ReadAllBytes(artifact.Path))));
        Assert.False(Directory.Exists(Path.Combine(_root, "Valheim", "BepInEx", "config",
            "comfy-quest-lab", "requests")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Valheim", "BepInEx", "config",
            "comfy-creator-session")));

        File.AppendAllText(receipt.Artifacts["capture"].Path, "drift");
        var collision = service.Stage(build.BuildId);
        Assert.Equal(409, collision.Status);
        Assert.Equal("stage_collision", collision.Error);
    }

    [Fact]
    public async Task Interrupted_pair_commit_rolls_back_before_retry()
    {
        var service = CreateService(importer: true);
        var capsule = ArchitecturalCapsuleFixture.Create();
        var imported = await service.ImportAsync(new MemoryStream(capsule), capsule.Length);
        var build = Assert.IsType<StudioBuildDocument>(imported.Build);
        service.StageFaultInjector = point =>
        {
            if (point == "after_capture_commit") throw new IOException("synthetic stage fault");
        };

        var failed = service.Stage(build.BuildId);
        var destination = Path.Combine(_root, "Valheim", "BepInEx", "config",
            "comfy-quest-lab", "blueprints");
        Assert.False(failed.Ok);
        Assert.Empty(Directory.GetFiles(destination));

        service.StageFaultInjector = null;
        var retried = service.Stage(build.BuildId);
        Assert.True(retried.Ok, retried.Error);
        Assert.Equal(2, Directory.GetFiles(destination).Length);
    }

    QuestStudioBuildService CreateService(bool importer)
    {
        var valheim = Path.Combine(_root, "Valheim");
        Directory.CreateDirectory(valheim);
        return new QuestStudioBuildService(new BuildHost(_root, valheim,
            importer ? FindImporter() : null));
    }

    static string FindImporter()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null)
        {
            var candidate = Path.Combine(root.FullName, "tools", "blueprints",
                "import_capture.py");
            if (File.Exists(candidate)) return candidate;
            root = root.Parent;
        }
        throw new InvalidOperationException("Could not locate tools/blueprints/import_capture.py");
    }

    static string CanonicalIdentity(StudioBuildDocument build) => string.Join(':',
        build.CanonicalCaptureSha256, build.CanonicalBlueprintSha256,
        build.CanonicalPiecesSha256, build.CanonicalManifestSha256);

    static string Sha256(byte[] bytes) => Convert.ToHexString(
        SHA256.HashData(bytes)).ToLowerInvariant();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    sealed class BuildHost(string stateDirectory, string valheim, string? importer) :
        IQuestStudioHost, IQuestStudioArchitecturalRAndDHost
    {
        public string StateDirectory { get; } = stateDirectory;
        public string? FindValheim() => valheim;
        public bool Authorize(HttpRequest request) => true;
        public JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
        public QuestStudioArchitecturalImporter? ArchitecturalImporter => importer is null
            ? null : new(importer, OperatingSystem.IsWindows() ? "python" : "python3", null);
    }
}

static class ArchitecturalCapsuleFixture
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    const string ReconciliationId = "reconcile:fixture:ridge-center";

    public static byte[] Create(string variant = "valid")
    {
        var pieces = new List<JsonObject>();
        var captures = new List<(string Signature, JsonObject Piece)>();
        for (var index = 0; index < 40; index++)
        {
            var prefab = index < 16 ? "wood_floor" : index < 32 ? "woodwall" : "wood_roof_45";
            var family = index < 16 ? "floor" : index < 32 ? "wall" : "roof";
            var x = index % 8;
            var y = family == "floor" ? 0 : family == "wall" ? 1 : 2.2225;
            var z = index / 8;
            var source = new JsonObject
            {
                ["index"] = index, ["prefab"] = prefab,
                ["category"] = "BuildingWorkbench", ["family"] = family,
                ["position"] = new JsonArray(x, y, z),
                ["rotation"] = new JsonArray(0, 0, 0, 1),
                ["yaw_degrees"] = 0, ["semantic_role"] = family,
                ["source"] = null,
            };
            pieces.Add(source);
            var capture = CapturePiece(prefab, x, y, z);
            captures.Add((Signature(prefab, x, y, z), capture));
        }
        captures.Sort((left, right) => string.CompareOrdinal(left.Signature, right.Signature));
        var piecesHash = Sha256(Encoding.UTF8.GetBytes(string.Join('\n',
            captures.Select(value => value.Signature))));
        var candidate = new JsonObject
        {
            ["Schema"] = "comfy-questlab-capture/v1",
            ["Name"] = "tn0304-architectural-fixture",
            ["Selection"] = variant == "non-architectural" ? "lab" :
                "architectural-import-candidate",
            ["RadiusMetres"] = 40,
            ["PieceCount"] = 40,
            ["PiecesSha256"] = piecesHash,
            ["Pieces"] = new JsonArray(captures.Select(value => value.Piece).ToArray()),
        };
        var reconciliation = new JsonObject
        {
            ["id"] = ReconciliationId, ["delta"] = -0.029171,
            ["bound"] = 0.1, ["reason"] = "bounded fixture repair",
            ["source_observation_mutated"] = false,
        };
        var adaptations = new JsonArray(new JsonObject
        {
            ["id"] = "game-floor-surface", ["kind"] = "GAME_ONLY",
            ["reason"] = "stable buildable surface",
        });
        var graph = new JsonObject
        {
            ["schema"] = "architectural-solved-envelope/v0",
            ["building_id"] = "tn0304", ["status"] = "SOLVED_RND",
            ["dimensions"] = Features(), ["features"] = Features(),
            ["footprints"] = new JsonArray(new JsonObject
            {
                ["id"] = "primary", ["kind"] = "rectangle",
                ["polygon_xz"] = new JsonArray(),
            }),
            ["roofs"] = new JsonArray(new JsonObject
            {
                ["id"] = "primary-gable", ["kind"] = "centered-gable",
                ["equal_pitch"] = true, ["ridge_axis"] = "x",
                ["pitch_degrees"] = 43.907838,
                ["eave_y_m"] = 2.2225, ["ridge_y_m"] = 5.8166,
            }),
            ["reconciliations"] = new JsonArray(reconciliation.DeepClone()),
            ["game_adaptations"] = adaptations.DeepClone(),
        };
        var constraints = new JsonObject
        {
            ["schema"] = "architectural-constraint-model/v0",
            ["fixture_id"] = "tn0304", ["observations"] = new JsonArray(),
            ["constraints"] = new JsonObject(), ["hypotheses"] = new JsonArray(),
        };
        var interpretation = new JsonObject
        {
            ["schema"] = "architectural-interpretation-receipt/v0",
            ["fixture_id"] = "tn0304", ["status"] = "PASS",
            ["gates"] = new JsonArray(new JsonObject
            {
                ["id"] = "piece-budget",
                ["status"] = variant == "failed-gate" ? "FAIL" : "PASS",
                ["actual"] = 40, ["limit"] = 256,
            }),
            ["contradictions"] = new JsonArray(),
            ["reconciliations"] = new JsonArray(reconciliation.DeepClone()),
        };
        var compilation = new JsonObject
        {
            ["schema"] = "architectural-envelope-compilation-receipt/v0",
            ["piece_count"] = variant == "piece-count" ? 39 : 40,
            ["maximum_pieces"] = 256, ["within_budget"] = true,
            ["prefab_counts"] = new JsonObject
            {
                ["wood_floor"] = 16, ["woodwall"] = 16, ["wood_roof_45"] = 8,
            },
            ["reconciliation_ids"] = new JsonArray(ReconciliationId),
        };
        var geometry = new JsonObject
        {
            ["schema"] = "creator-os-prefab-geometry/v0",
            ["prefabs"] = new JsonObject
            {
                ["wood_floor"] = Bounds(2, .2, 2),
                ["woodwall"] = Bounds(2, 2, .2),
                ["wood_roof_45"] = Bounds(2, 2.4633, 2.8284),
            },
        };
        var members = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["solved-building.graph.json"] = Bytes(graph),
            ["constraint-model.json"] = Bytes(constraints),
            ["interpretation-receipt.json"] = Bytes(interpretation),
            ["compilation-receipt.json"] = Bytes(compilation),
            ["pieces.json"] = Bytes(new JsonArray(pieces.ToArray())),
            ["architectural-candidate.capture.json"] = Bytes(candidate),
            ["prefab-geometry.json"] = Bytes(geometry),
        };
        var pins = new JsonObject();
        foreach (var member in members)
            pins[member.Key] = new JsonObject
            {
                ["bytes"] = member.Value.Length, ["sha256"] = Sha256(member.Value),
            };
        var capsule = new JsonObject
        {
            ["schema"] = "creator-os-architectural-build-capsule/v0",
            ["fixture_id"] = "tn0304", ["source_revision"] = "fixture-revision",
            ["source"] = new JsonObject
            {
                ["envelope_revision"] = "fixture-revision",
                ["identity_sha256"] = new string('1', 64),
                ["producer"] = "tests/ArchitecturalCapsuleFixture",
                ["producer_sha256"] = new string('2', 64),
            },
            ["entrypoints"] = new JsonObject
            {
                ["graph"] = "solved-building.graph.json",
                ["constraints"] = "constraint-model.json",
                ["interpretation"] = "interpretation-receipt.json",
                ["compilation"] = "compilation-receipt.json",
                ["pieces"] = "pieces.json",
                ["candidate_capture"] = "architectural-candidate.capture.json",
                ["prefab_geometry"] = "prefab-geometry.json",
            },
            ["piece_count"] = 40,
            ["compiled_pieces_sha256"] = Sha256(members["pieces.json"]),
            ["candidate_pieces_sha256"] = piecesHash,
            ["prefab_counts"] = new JsonObject
            {
                ["wood_floor"] = 16, ["woodwall"] = 16, ["wood_roof_45"] = 8,
            },
            ["reconciliation_ids"] = new JsonArray(ReconciliationId),
            ["game_adaptations"] = adaptations.DeepClone(), ["members"] = pins,
        };
        members["capsule.json"] = Bytes(capsule);
        if (variant == "tampered")
            members["solved-building.graph.json"] =
                [.. members["solved-building.graph.json"], (byte)' '];
        return Zip(members, traversal: variant == "traversal");
    }

    public static byte[] CreateExpandedBomb()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("capsule.json", CompressionLevel.SmallestSize);
            using var stream = entry.Open();
            var block = new byte[64 * 1024];
            var remaining = QuestStudioBuildService.MaxExpandedBytes + 1;
            while (remaining > 0)
            {
                var count = (int)Math.Min(block.Length, remaining);
                stream.Write(block, 0, count);
                remaining -= count;
            }
        }
        return output.ToArray();
    }

    static JsonObject Features() => new()
    {
        ["width_m"] = Feature(7.953375, "MEASURED", "plan"),
        ["depth_m"] = Feature(7.4676, "MEASURED", "plan"),
        ["wall_height_m"] = Feature(2.2225, "MEASURED", "section"),
        ["ridge_height_m"] = Feature(5.8166, "CONSTRAINED", "section"),
        ["roof_pitch_degrees"] = Feature(43.907838, "CONSTRAINED", "section"),
    };

    static JsonObject Feature(double value, string derivation, string view) => new()
    {
        ["value"] = value, ["confidence"] = .9, ["derivation"] = derivation,
        ["source_views"] = new JsonArray(view),
        ["supporting_constraints"] = new JsonArray("fixture support"),
        ["conflicting_constraints"] = new JsonArray(),
    };

    static JsonObject Bounds(double x, double y, double z) => new()
    {
        ["kind"] = "bounded-proxy", ["mesh_bounds_m"] = new JsonArray(x, y, z),
    };

    static JsonObject CapturePiece(string prefab, double x, double y, double z) => new()
    {
        ["Prefab"] = prefab, ["Category"] = "BuildingWorkbench",
        ["X"] = x, ["Y"] = y, ["Z"] = z,
        ["Qx"] = 0, ["Qy"] = 0, ["Qz"] = 0, ["Qw"] = 1,
        ["HasSignText"] = false, ["SignText"] = "",
        ["HasItemStand"] = false, ["ItemPrefab"] = "",
        ["ItemVariant"] = 0, ["ItemQuality"] = 0, ["ItemType"] = 0,
        ["RuneSchool"] = "", ["RuneStyle"] = "", ["TextGlowSchool"] = "",
    };

    static string Signature(string prefab, double x, double y, double z) => string.Join('\t',
        prefab, "BuildingWorkbench", Number(x, 4), Number(y, 4), Number(z, 4),
        "0", "0", "0", "1", "0", "", "0", "", "0", "0", "0", "", "", "");

    static string Number(double value, int digits)
    {
        if (value == 0) return "0";
        return value.ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);
    }

    static byte[] Bytes(JsonNode value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);

    static byte[] Zip(Dictionary<string, byte[]> members, bool traversal)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var member in members.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(member.Key, CompressionLevel.SmallestSize);
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var stream = entry.Open();
                stream.Write(member.Value);
            }
            if (traversal)
            {
                var entry = archive.CreateEntry("../escape", CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.WriteByte(1);
            }
        }
        return output.ToArray();
    }

    static string Sha256(byte[] bytes) => Convert.ToHexString(
        SHA256.HashData(bytes)).ToLowerInvariant();
}

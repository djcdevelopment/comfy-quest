using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComfyQuestContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Comfy.Quest.Studio;

public sealed record StudioCreatorSceneRequest(
    long SnapshotId,
    double MinX,
    double MaxX,
    double MinZ,
    double MaxZ,
    IReadOnlyList<string>? Biomes = null);

public sealed record StudioCreatorTargetRequest(
    int ExpectedRevision,
    string? SceneId,
    long ZdoIndex,
    string? RouteId,
    string? Predicate,
    string? AnchorId,
    string? Mode,
    double RadiusMeters);

public sealed record StudioCreatorWorldLink(
    string Schema,
    string CreatorSessionId,
    string Machine,
    string WorldName,
    string WorldUid,
    long SnapshotId,
    string SnapshotWorldId,
    string SnapshotFileSha256);

public sealed record StudioCreatorTargetResult(
    bool Ok,
    bool Conflict,
    string? Error,
    string? TargetId,
    string? BindingZdo,
    StudioProjectDocument? Project,
    StudioSpatialArea? Area,
    StudioCreatorWorldLink? WorldLink)
{
    public static StudioCreatorTargetResult Fail(string error, bool conflict = false,
        StudioProjectDocument? project = null) =>
        new(false, conflict, error, null, null, project, null, null);
}

public sealed record StudioCreatorSceneResult(
    bool Ok,
    string? Error,
    byte[]? Bytes,
    string? SceneId,
    string? ContentType,
    int PieceCount,
    int InstanceCount)
{
    public static StudioCreatorSceneResult Fail(string error) =>
        new(false, error, null, null, null, 0, 0);
}

internal sealed class StudioCreatorSceneReceipt
{
    public string Schema { get; set; } = "comfy-quest-steward-scene-receipt/v1";
    public string SceneId { get; set; } = string.Empty;
    public DateTimeOffset FetchedUtc { get; set; }
    public long SnapshotId { get; set; }
    public string SnapshotWorldId { get; set; } = string.Empty;
    public string SnapshotFileSha256 { get; set; } = string.Empty;
    public string ProducerRevision { get; set; } = string.Empty;
    public double[] AbsoluteOrigin { get; set; } = Array.Empty<double>();
    public int PieceCount { get; set; }
    public int InstanceCount { get; set; }
    public double MinX { get; set; }
    public double MaxX { get; set; }
    public double MinZ { get; set; }
    public double MaxZ { get; set; }
    public IReadOnlyList<string> Biomes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<long> ZdoIndexes { get; set; } = Array.Empty<long>();
}

internal sealed class StudioCreatorTargetReceipt
{
    public string Schema { get; set; } = "comfy-quest-creator-target/v1";
    public string TargetId { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public int ProjectRevision { get; set; }
    public string SceneId { get; set; } = string.Empty;
    public long ZdoIndex { get; set; }
    public string RouteId { get; set; } = string.Empty;
    public string BindingZdo { get; set; } = string.Empty;
    public string AnchorSha256 { get; set; } = string.Empty;
    public StudioCreatorWorldLink WorldLink { get; set; } = null!;
}

/// <summary>Private server-side bridge between Steward's measured scene and Runtime's live
/// candidate set. Browsers can select only an identity Steward actually returned; coordinates,
/// world linkage, and the operator credential are all resolved and verified here.</summary>
internal sealed class QuestStudioCreator
{
    const string SceneContentType = "application/vnd.comfysteward.authoring-scene";
    const int MaxSceneBytes = 64 * 1024 * 1024;
    const int MaxManifestBytes = 1024 * 1024;
    const int InstanceStride = 80;
    const int IdentityStride = 4;
    const int MaxReceipts = 64;
    readonly object _gate = new();
    readonly IQuestStudioHost _host;
    readonly QuestStudioWorkspace _workspace;
    readonly QuestStudioRunControl _runControl;
    readonly HttpClient _http;
    readonly string _sceneRoot;
    readonly string _targetRoot;

    public QuestStudioCreator(IQuestStudioHost host, QuestStudioWorkspace workspace,
        QuestStudioRunControl runControl, HttpMessageHandler? handler = null)
    {
        _host = host;
        _workspace = workspace;
        _runControl = runControl;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = TimeSpan.FromSeconds(30);
        var root = Path.Combine(host.StateDirectory, "quest-studio", "creator");
        _sceneRoot = Path.Combine(root, "scenes");
        _targetRoot = Path.Combine(root, "targets");
    }

    public async Task<StudioCreatorSceneResult> FetchSceneAsync(
        StudioCreatorSceneRequest? request, CancellationToken cancellationToken)
    {
        var connection = Connection();
        if (connection is null) return StudioCreatorSceneResult.Fail("steward_connection_unavailable");
        if (!ValidSceneRequest(request)) return StudioCreatorSceneResult.Fail("creator_scene_request_invalid");
        var query = new List<string>
        {
            "snapshot=" + request!.SnapshotId.ToString(CultureInfo.InvariantCulture),
            "lens=build-density",
            "minX=" + request.MinX.ToString("R", CultureInfo.InvariantCulture),
            "maxX=" + request.MaxX.ToString("R", CultureInfo.InvariantCulture),
            "minZ=" + request.MinZ.ToString("R", CultureInfo.InvariantCulture),
            "maxZ=" + request.MaxZ.ToString("R", CultureInfo.InvariantCulture),
        };
        foreach (var biome in request.Biomes ?? Array.Empty<string>())
            query.Add("biome=" + Uri.EscapeDataString(biome));
        using var message = new HttpRequestMessage(HttpMethod.Get,
            new Uri(connection.SceneOrigin, "/api/creator/scene?" + string.Join("&", query)));
        message.Headers.TryAddWithoutValidation("X-Steward-Quest-Token", connection.OperatorToken);
        try
        {
            using var response = await _http.SendAsync(message,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StudioCreatorSceneResult.Fail("steward_operator_forbidden");
            if (!response.IsSuccessStatusCode)
                return StudioCreatorSceneResult.Fail("steward_scene_unavailable");
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, SceneContentType,
                    StringComparison.OrdinalIgnoreCase))
                return StudioCreatorSceneResult.Fail("steward_scene_content_type_invalid");
            var bytes = await ReadBoundedAsync(response.Content, MaxSceneBytes, cancellationToken);
            if (bytes is null) return StudioCreatorSceneResult.Fail("steward_scene_too_large");
            var parsed = ParseScene(bytes);
            if (parsed.Error is not null) return StudioCreatorSceneResult.Fail(parsed.Error);
            if (parsed.Receipt!.SnapshotId != request.SnapshotId
                || parsed.Receipt.MinX != request.MinX || parsed.Receipt.MaxX != request.MaxX
                || parsed.Receipt.MinZ != request.MinZ || parsed.Receipt.MaxZ != request.MaxZ
                || !parsed.Receipt.Biomes.OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual((request.Biomes ?? Array.Empty<string>())
                        .OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
                return StudioCreatorSceneResult.Fail("steward_scene_scope_mismatch");
            StoreScene(parsed.Receipt!);
            return new(true, null, bytes, parsed.Receipt!.SceneId, SceneContentType,
                parsed.Receipt.PieceCount, parsed.Receipt.InstanceCount);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StudioCreatorSceneResult.Fail("steward_scene_timeout");
        }
        catch (HttpRequestException) { return StudioCreatorSceneResult.Fail("steward_scene_unavailable"); }
        catch (IOException) { return StudioCreatorSceneResult.Fail("steward_scene_unreadable"); }
    }

    public async Task<StudioCreatorTargetResult> SelectTargetAsync(string projectId,
        StudioCreatorTargetRequest? request, CancellationToken cancellationToken)
    {
        if (!SafeLocalId(projectId) || request is null || !Sha(request.SceneId)
            || request.ZdoIndex is < 0 or > int.MaxValue || !SafeId(request.RouteId)
            || !SafeId(request.AnchorId) || request.Mode is not ("world" or "binding_relative")
            || !SpatialPredicateCatalog.TryGet(request.Predicate, out _)
            || !Finite(request.RadiusMeters) || request.RadiusMeters is < 1 or > 100)
            return StudioCreatorTargetResult.Fail("creator_target_request_invalid");
        var scene = ReadScene(request.SceneId!);
        if (scene is null) return StudioCreatorTargetResult.Fail("creator_scene_receipt_missing");
        if (!scene.ZdoIndexes.Contains(request.ZdoIndex))
            return StudioCreatorTargetResult.Fail("creator_scene_identity_missing");
        var project = _workspace.ReadProject(projectId);
        if (project is null) return StudioCreatorTargetResult.Fail("project_missing");
        if (project.Revision != request.ExpectedRevision)
            return StudioCreatorTargetResult.Fail("revision_conflict", true, project);
        if (project.Nodes.SelectMany(value => value.Routes ?? new()).Count(value => value.Id == request.RouteId) != 1)
            return StudioCreatorTargetResult.Fail("route_missing");

        var anchorResult = await FetchAnchorAsync(scene, request, cancellationToken);
        if (anchorResult.Error is not null)
            return StudioCreatorTargetResult.Fail(anchorResult.Error);
        var anchor = anchorResult.Anchor!;
        var status = _runControl.Status(projectId);
        if (!status.Available || !status.Connected)
            return StudioCreatorTargetResult.Fail(status.Error ?? "runtime_disconnected");
        var worldLink = ReadWorldLink(scene, status);
        if (worldLink.Error is not null)
            return StudioCreatorTargetResult.Fail(worldLink.Error);
        var identity = new StudioRuntimeIdentity(status.Machine!, status.WorldUid!,
            worldLink.Link!.CreatorSessionId);
        var candidates = await CompletePackControlAsync(projectId,
            await _runControl.BindingCandidatesPinnedAsync(projectId, identity, cancellationToken),
            identity, "list_binding_candidates", cancellationToken);
        if (!candidates.Ok || candidates.Receipt?.BindingCandidates is null)
            return StudioCreatorTargetResult.Fail(candidates.Error ?? "binding_candidates_failed");
        var targetKinds = EffectiveTargetKinds(project);
        var matches = candidates.Receipt.BindingCandidates.Where(value =>
            value.Position is not null && !string.IsNullOrWhiteSpace(value.Prefab)
            && targetKinds.Contains(value.TargetKind)
            && string.Equals(value.Prefab, anchor.Piece.Prefab, StringComparison.Ordinal)
            && Quantize(value.Position.X) == Quantize(anchor.Piece.Position.X)
            && Quantize(value.Position.Y) == Quantize(anchor.Piece.Position.Y)
            && Quantize(value.Position.Z) == Quantize(anchor.Piece.Position.Z)).ToArray();
        if (matches.Length == 0) return StudioCreatorTargetResult.Fail("creator_live_target_missing");
        if (matches.Length > 1) return StudioCreatorTargetResult.Fail("creator_live_target_ambiguous");

        var imported = _workspace.ImportSpatialAnchor(projectId, new(
            request.ExpectedRevision, request.RouteId, request.Predicate, anchorResult.Json));
        if (!imported.Ok)
            return StudioCreatorTargetResult.Fail(imported.Error ?? "anchor_import_failed",
                imported.Conflict, imported.Project);
        var target = new StudioCreatorTargetReceipt
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            ProjectId = projectId,
            ProjectRevision = imported.Project!.Revision,
            SceneId = scene.SceneId,
            ZdoIndex = request.ZdoIndex,
            RouteId = request.RouteId!,
            BindingZdo = matches[0].BindingZdo,
            AnchorSha256 = anchor.ContentSha256.ToLowerInvariant(),
            WorldLink = worldLink.Link!,
        };
        target.TargetId = Hash(string.Join("\n", target.ProjectId,
            target.ProjectRevision.ToString(CultureInfo.InvariantCulture), target.SceneId,
            target.ZdoIndex.ToString(CultureInfo.InvariantCulture), target.RouteId,
            target.BindingZdo, target.AnchorSha256, target.WorldLink.CreatorSessionId));
        StoreTarget(target);
        return new(true, false, null, target.TargetId, target.BindingZdo,
            imported.Project, imported.Area, worldLink.Link);
    }

    internal StudioCreatorTargetReceipt? ReadTarget(string projectId, string? targetId)
    {
        if (!SafeLocalId(projectId) || !Sha(targetId)) return null;
        try
        {
            var path = Path.Combine(_targetRoot, targetId + ".json");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > 1024 * 1024) return null;
            var value = System.Text.Json.JsonSerializer.Deserialize<StudioCreatorTargetReceipt>(
                File.ReadAllText(path), _host.Json);
            return value?.Schema == "comfy-quest-creator-target/v1"
                && value.TargetId == targetId && value.ProjectId == projectId
                && SafeId(value.RouteId) && !string.IsNullOrWhiteSpace(value.BindingZdo)
                && value.WorldLink?.Schema == "comfy-quest-steward-world-link/v1" ? value : null;
        }
        catch { return null; }
    }

    async Task<(SpatialAnchorExchange? Anchor, string? Json, string? Error)> FetchAnchorAsync(
        StudioCreatorSceneReceipt scene, StudioCreatorTargetRequest request,
        CancellationToken cancellationToken)
    {
        var connection = Connection();
        if (connection is null) return (null, null, "steward_connection_unavailable");
        using var message = new HttpRequestMessage(HttpMethod.Post,
            new Uri(connection.ViewerOrigin, "/api/v1/quest/spatial-anchor/export"));
        message.Headers.TryAddWithoutValidation("X-Steward-Quest-Token", connection.OperatorToken);
        message.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
        {
            snapshot_id = scene.SnapshotId,
            zdo_index = (int)request.ZdoIndex,
            anchor_id = request.AnchorId,
            mode = request.Mode,
            radius_meters = request.RadiusMeters,
        }), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _http.SendAsync(message,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return (null, null, "steward_anchor_unavailable");
            var bytes = await ReadBoundedAsync(response.Content,
                SpatialExchangeSchema.MaxDocumentBytes, cancellationToken);
            if (bytes is null) return (null, null, "steward_anchor_too_large");
            var json = Encoding.UTF8.GetString(bytes);
            SpatialAnchorExchange anchor;
            try { anchor = SpatialExchangeContract.ParseAnchor(json); }
            catch (SpatialContractException exception) { return (null, null, exception.Code); }
            if (anchor.Snapshot.SnapshotId != scene.SnapshotId
                || anchor.Snapshot.WorldId != scene.SnapshotWorldId
                || !string.Equals(anchor.Snapshot.FileSha256, scene.SnapshotFileSha256,
                    StringComparison.OrdinalIgnoreCase)
                || anchor.Piece.ZdoIndex != request.ZdoIndex
                || anchor.Producer.Revision != scene.ProducerRevision)
                return (null, null, "steward_scene_anchor_mismatch");
            return (anchor, json, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return (null, null, "steward_anchor_timeout"); }
        catch (HttpRequestException) { return (null, null, "steward_anchor_unavailable"); }
    }

    (StudioCreatorWorldLink? Link, string? Error) ReadWorldLink(
        StudioCreatorSceneReceipt scene, StudioRunStatusView status)
    {
        var valheim = _host.FindValheim();
        if (valheim is null) return (null, "valheim_not_found");
        var path = Path.Combine(Path.GetFullPath(valheim), "BepInEx", "config",
            "comfy-quest-creator", "session.json");
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > 1024 * 1024)
                return (null, "creator_session_unavailable");
            var root = ParseObjectStrict(File.ReadAllText(path));
            if ((string?)root["schema"] != "comfy-quest-creator-session/v1"
                || (string?)root["state"] != "active")
                return (null, "creator_session_inactive");
            var sessionId = (string?)root["session_id"];
            var machine = (string?)root["expected_machine"];
            var worldName = (string?)root["world_name"];
            var worldUid = Convert.ToString(root["world_uid"], CultureInfo.InvariantCulture);
            if (!SafeToken(sessionId, 80) || !SafeToken(machine, 80)
                || string.IsNullOrWhiteSpace(worldName) || worldName.Length > 80
                || !string.Equals(machine, status.Machine, StringComparison.OrdinalIgnoreCase)
                || worldUid != status.WorldUid)
                return (null, "creator_session_identity_mismatch");
            var backups = (root["world_backup"] as JArray)?.OfType<JObject>()
                .Where(value => string.Equals(Path.GetExtension((string?)value["source"]), ".db",
                    StringComparison.OrdinalIgnoreCase)).ToArray() ?? Array.Empty<JObject>();
            if (backups.Length != 1) return (null, "creator_session_world_backup_ambiguous");
            var backupHash = (string?)backups[0]["sha256"];
            if (!Sha(backupHash) || !string.Equals(backupHash, scene.SnapshotFileSha256,
                    StringComparison.OrdinalIgnoreCase))
                return (null, "steward_snapshot_world_backup_mismatch");
            return (new("comfy-quest-steward-world-link/v1", sessionId!, machine!, worldName!,
                worldUid!, scene.SnapshotId, scene.SnapshotWorldId,
                scene.SnapshotFileSha256.ToLowerInvariant()), null);
        }
        catch { return (null, "creator_session_unreadable"); }
    }

    async Task<StudioRunControlResult> CompletePackControlAsync(string projectId,
        StudioRunControlResult initial, StudioRuntimeIdentity identity, string operation,
        CancellationToken cancellationToken)
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

    QuestStudioStewardConnection? Connection()
    {
        var value = (_host as IQuestStudioStewardHost)?.StewardConnection;
        return value is not null && SafeOrigin(value.SceneOrigin) && SafeOrigin(value.ViewerOrigin)
            && !string.IsNullOrWhiteSpace(value.OperatorToken) && value.OperatorToken.Length <= 1024
            && !value.OperatorToken.Any(char.IsControl) ? value : null;
    }

    static bool SafeOrigin(Uri value) => value.IsAbsoluteUri && string.IsNullOrEmpty(value.UserInfo)
        && string.IsNullOrEmpty(value.Query) && string.IsNullOrEmpty(value.Fragment)
        && (value.Scheme == Uri.UriSchemeHttps || value.Scheme == Uri.UriSchemeHttp && value.IsLoopback);

    static bool ValidSceneRequest(StudioCreatorSceneRequest? value) => value is not null
        && value.SnapshotId > 0 && Finite(value.MinX) && Finite(value.MaxX)
        && Finite(value.MinZ) && Finite(value.MaxZ)
        && value.MinX < value.MaxX && value.MinZ < value.MaxZ
        && Math.Abs(value.MinX) <= 10_500 && Math.Abs(value.MaxX) <= 10_500
        && Math.Abs(value.MinZ) <= 10_500 && Math.Abs(value.MaxZ) <= 10_500
        && (value.Biomes?.Count ?? 0) <= 16
        && (value.Biomes ?? Array.Empty<string>()).All(item => SafeToken(item, 64));

    static (StudioCreatorSceneReceipt? Receipt, string? Error) ParseScene(byte[] bytes)
    {
        if (bytes.Length < 20 || Encoding.ASCII.GetString(bytes, 0, 4) != "SVCA")
            return (null, "steward_scene_magic_invalid");
        var version = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
        var manifestLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
        var instanceOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4));
        var identityOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16, 4));
        if (version != 1 || manifestLength is <= 0 or > MaxManifestBytes
            || instanceOffset < 20 + manifestLength || (instanceOffset & 3) != 0
            || identityOffset < instanceOffset || identityOffset > bytes.Length)
            return (null, "steward_scene_layout_invalid");
        JObject manifest;
        try { manifest = ParseObjectStrict(Encoding.UTF8.GetString(bytes, 20, manifestLength)); }
        catch { return (null, "steward_scene_manifest_invalid"); }
        if ((string?)manifest["schema"] != "steward-zdo-authoring-scene/v1")
            return (null, "steward_scene_schema_unsupported");
        var snapshotId = (long?)manifest["snapshotId"] ?? 0;
        var worldId = (string?)manifest["worldId"];
        var fileHash = (string?)manifest["fileSha256"];
        var revision = (string?)manifest["producerRevision"];
        var pieces = (int?)manifest["pieces"] ?? -1;
        var instances = (int?)manifest["renderInstances"] ?? -1;
        var instanceBytes = (int?)manifest["instanceBytes"] ?? -1;
        var instanceSha = (string?)manifest["instanceSha256"];
        var identityStride = (int?)manifest["identityStride"] ?? -1;
        var identityCount = (int?)manifest["identityCount"] ?? -1;
        var identityBytes = (int?)manifest["identityBytes"] ?? -1;
        var identitySha = (string?)manifest["identitySha256"];
        var origin = (manifest["absoluteOrigin"] as JArray)?.Values<double>().ToArray();
        var scope = manifest["scope"] as JObject;
        var minX = (double?)scope?["minX"];
        var maxX = (double?)scope?["maxX"];
        var minZ = (double?)scope?["minZ"];
        var maxZ = (double?)scope?["maxZ"];
        var biomeTokens = scope?["biomes"] as JArray;
        var biomes = biomeTokens is not null && biomeTokens.All(value => value.Type == JTokenType.String)
            ? biomeTokens.Select(value => value.Value<string>()!).ToArray() : null;
        if (snapshotId <= 0 || !SafeToken(worldId, 128) || !Sha(fileHash)
            || !Hex(revision, 40) || pieces is < 0 or > 5000 || instances < pieces
            || instances > 500_000 || instanceBytes != instances * InstanceStride
            || (int?)manifest["instanceStride"] != InstanceStride
            || (string?)manifest["lens"] != "build-density"
            || (bool?)manifest["exact"] != true || (bool?)manifest["forced"] != false
            || identityStride != IdentityStride || identityCount != instances
            || identityBytes != instances * IdentityStride
            || identityOffset != instanceOffset + instanceBytes
            || bytes.Length != identityOffset + identityBytes || !Sha(instanceSha) || !Sha(identitySha)
            || origin is null || origin.Length != 3 || origin.Any(value => !Finite(value))
            || !minX.HasValue || !maxX.HasValue || !minZ.HasValue || !maxZ.HasValue
            || !Finite(minX.Value) || !Finite(maxX.Value) || !Finite(minZ.Value) || !Finite(maxZ.Value)
            || minX >= maxX || minZ >= maxZ || biomes is null
            || biomes.Length > 16 || biomes.Any(value => !SafeToken(value, 64)))
            return (null, "steward_scene_manifest_invalid");
        var actualInstanceSha = Hash(bytes.AsSpan(instanceOffset, instanceBytes));
        var actualIdentitySha = Hash(bytes.AsSpan(identityOffset, identityBytes));
        if (!string.Equals(actualInstanceSha, instanceSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(actualIdentitySha, identitySha, StringComparison.OrdinalIgnoreCase))
            return (null, "steward_scene_integrity_mismatch");
        var identities = new HashSet<long>();
        for (var offset = identityOffset; offset < bytes.Length; offset += IdentityStride)
            identities.Add(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, IdentityStride)));
        if (identities.Count != pieces)
            return (null, "steward_scene_membership_invalid");
        return (new StudioCreatorSceneReceipt
        {
            SceneId = Hash(bytes), FetchedUtc = DateTimeOffset.UtcNow,
            SnapshotId = snapshotId, SnapshotWorldId = worldId!,
            SnapshotFileSha256 = fileHash!.ToLowerInvariant(),
            ProducerRevision = revision!.ToLowerInvariant(), AbsoluteOrigin = origin,
            PieceCount = pieces, InstanceCount = instances,
            MinX = minX.Value, MaxX = maxX.Value, MinZ = minZ.Value, MaxZ = maxZ.Value,
            Biomes = biomes,
            ZdoIndexes = identities.OrderBy(value => value).ToArray(),
        }, null);
    }

    void StoreScene(StudioCreatorSceneReceipt receipt)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_sceneRoot);
            WriteAtomic(Path.Combine(_sceneRoot, receipt.SceneId + ".json"), receipt);
            Prune(_sceneRoot);
        }
    }

    StudioCreatorSceneReceipt? ReadScene(string sceneId)
    {
        try
        {
            var info = new FileInfo(Path.Combine(_sceneRoot, sceneId + ".json"));
            if (!info.Exists || info.Length is <= 0 or > 2 * 1024 * 1024) return null;
            var value = System.Text.Json.JsonSerializer.Deserialize<StudioCreatorSceneReceipt>(
                File.ReadAllText(info.FullName), _host.Json);
            return value?.Schema == "comfy-quest-steward-scene-receipt/v1"
                && value.SceneId == sceneId && value.ZdoIndexes.Count <= 5000 ? value : null;
        }
        catch { return null; }
    }

    void StoreTarget(StudioCreatorTargetReceipt receipt)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_targetRoot);
            WriteAtomic(Path.Combine(_targetRoot, receipt.TargetId + ".json"), receipt);
            Prune(_targetRoot);
        }
    }

    void WriteAtomic<T>(string path, T value)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary,
                System.Text.Json.JsonSerializer.Serialize(value, _host.Json));
            if (File.Exists(path)) File.Delete(temporary);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static void Prune(string root)
    {
        foreach (var file in new DirectoryInfo(root).GetFiles("*.json")
                     .OrderByDescending(value => value.LastWriteTimeUtc).ThenBy(value => value.Name,
                         StringComparer.Ordinal).Skip(MaxReceipts))
            file.Delete();
    }

    static async Task<byte[]?> ReadBoundedAsync(HttpContent content, int maximum,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength.HasValue
            && content.Headers.ContentLength.Value > maximum) return null;
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) return output.ToArray();
            if (output.Length + read > maximum) return null;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    static JObject ParseObjectStrict(string json)
    {
        using var input = new StringReader(json);
        using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None };
        var token = JToken.ReadFrom(reader, new JsonLoadSettings
        {
            CommentHandling = CommentHandling.Load,
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            LineInfoHandling = LineInfoHandling.Load,
        });
        if (token is not JObject value || value.Descendants().Any(item => item.Type == JTokenType.Comment)
            || reader.Read()) throw new JsonReaderException("Object JSON required.");
        return value;
    }

    static HashSet<string> EffectiveTargetKinds(StudioProjectDocument project)
    {
        IEnumerable<string> values = !string.IsNullOrWhiteSpace(project.BindingTargetKind)
            ? new[] { project.BindingTargetKind! }
            : project.BindingTargetKinds ?? new List<string>();
        return values.ToHashSet(StringComparer.Ordinal);
    }
    static long Quantize(double value) => checked((long)Math.Round(value * 100d,
        MidpointRounding.AwayFromZero));
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static bool SafeLocalId(string? value) => SafeToken(value, 80);
    static bool SafeId(string? value) => SafeToken(value, 64);
    static bool SafeToken(string? value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '$' or '.');
    static bool Sha(string? value) => Hex(value, 64);
    static bool Hex(string? value, int length) => value?.Length == length
        && value.All(Uri.IsHexDigit);
    static string Hash(byte[] value) => Hash(value.AsSpan());
    static string Hash(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
}

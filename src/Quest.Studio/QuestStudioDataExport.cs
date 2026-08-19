using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComfyQuestContracts;

namespace Comfy.Quest.Studio;

/// <summary>
/// Builds ephemeral, bounded downloads from Quest Studio's own state. Export bytes are
/// returned to the caller and are never retained beneath the Studio state directory.
/// </summary>
internal sealed class QuestStudioDataExport
{
    internal const int MaxEntries = 512;
    internal const long MaxExpandedBytes = 128L * 1024 * 1024;
    const long MaxDraftBytes = 1024 * 1024;

    readonly string _projectsRoot;
    readonly IQuestStudioHost _host;

    public QuestStudioDataExport(IQuestStudioHost host)
    {
        _host = host;
        _projectsRoot = Path.Combine(host.StateDirectory, "quest-studio", "projects");
    }

    public StudioDownloadResult BuildQuestpack(StudioProjectDocument? project, StudioCertificationResult compiled)
    {
        if (project is null) return StudioDownloadResult.Fail("project_missing");
        if (!TryReadPersistedProject(project.ProjectId, out var persisted, out _))
            return StudioDownloadResult.Fail("project_state_unreadable");
        var exactProject = persisted!;
        compiled = StudioGraphCompiler.Compile(exactProject);
        if (!compiled.Ok || string.IsNullOrWhiteSpace(compiled.ExperienceJson) || string.IsNullOrWhiteSpace(compiled.ContentHash))
            return StudioDownloadResult.Fail(compiled.Error ?? "graph_invalid");
        if (HasPublishedIdentityCollision(exactProject, compiled.ContentHash))
            return StudioDownloadResult.Fail("same_version_hash_collision");

        var bytes = StudioGraphCompiler.BuildPack(exactProject, compiled.ExperienceJson, compiled.ContentHash);
        if (bytes.LongLength > QuestPackPublisher.MaxPackageBytes)
            return StudioDownloadResult.Fail("package_too_large");
        return StudioDownloadResult.Success(
            SafeDownloadName($"{exactProject.PackId}-{exactProject.Version}", exactProject.ProjectId) + ".questpack",
            "application/vnd.comfy.questpack+zip",
            bytes,
            compiled.ContentHash);
    }

    public StudioDownloadResult BuildBundle(
        StudioProjectDocument? project,
        StudioCertificationResult compiled,
        StudioRuntimeStatus? liveStatus,
        StudioExportRequest? request)
    {
        if (project is null || !SafeLocalId(project.ProjectId)) return StudioDownloadResult.Fail("project_missing");
        var includePublished = request?.IncludePublishedPackages ?? true;
        var includeLive = request?.IncludeLiveEvidence ?? false;

        try
        {
            var builder = new BundleBuilder();
            var projectRoot = Path.Combine(_projectsRoot, project.ProjectId);
            if (!IsDirectChild(_projectsRoot, projectRoot)) return StudioDownloadResult.Fail("project_identity_invalid");
            var draftPath = Path.Combine(projectRoot, "draft.json");
            if (!TryReadBounded(draftPath, MaxDraftBytes, out var draft))
                return StudioDownloadResult.Fail("project_state_unreadable");
            StudioProjectDocument? persisted;
            try { persisted = JsonSerializer.Deserialize<StudioProjectDocument>(draft!, _host.Json); }
            catch { return StudioDownloadResult.Fail("project_state_unreadable"); }
            if (persisted is null || persisted.ProjectId != project.ProjectId || !QuestStudioWorkspace.NormalizeDocument(persisted))
                return StudioDownloadResult.Fail("project_state_unreadable");
            project = persisted;
            compiled = StudioGraphCompiler.Compile(project);
            builder.Add("project/draft.json", draft!);
            var publishedIdentities = new List<PublishedIdentity>();
            if (compiled.Ok && !string.IsNullOrWhiteSpace(compiled.ContentHash))
                publishedIdentities.Add(new(project.PackId, project.Version, compiled.ContentHash));

            var historyRoot = Path.Combine(projectRoot, "history");
            if (Directory.Exists(historyRoot))
            {
                foreach (var path in Directory.GetFiles(historyRoot, "*.json").OrderBy(value => Path.GetFileName(value), StringComparer.Ordinal))
                {
                    var stem = Path.GetFileNameWithoutExtension(path);
                    if (!SafeHistoryStem(stem) || !TryReadBounded(path, MaxDraftBytes, out var history))
                        return StudioDownloadResult.Fail("project_history_unreadable");
                    builder.Add($"project/history/{stem}.json", history!);
                    AddCertifiedHistoryIdentity(history!, project.ProjectId, publishedIdentities);
                }
            }

            if (compiled.Ok && compiled.ExperienceJson is not null)
            {
                builder.Add("compiled/experience.json", Encoding.UTF8.GetBytes(compiled.ExperienceJson));
            }
            else
            {
                builder.Add("compiled/diagnostics.json", JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schema_version = 1,
                    ok = false,
                    error = compiled.Error ?? "graph_invalid",
                    diagnostics = compiled.Diagnostics
                }, _host.Json));
            }

            var publishedCount = includePublished
                ? AddMatchingPublishedPackages(builder, publishedIdentities)
                : 0;
            var omissions = new List<object>();
            if (!compiled.Ok) omissions.Add(new { role = "current_experience", reason = "draft_invalid; diagnostics included" });
            if (!includePublished) omissions.Add(new { role = "published_package", reason = "not_requested" });
            else if (publishedCount == 0) omissions.Add(new { role = "published_package", reason = _host.FindValheim() is null ? "valheim_not_found" : "no_matching_local_package" });
            if (!includeLive) omissions.Add(new { role = "runtime_evidence", reason = "not_requested" });

            var liveMatchesDraft = includeLive && liveStatus is not null
                && liveStatus.Available
                && !string.IsNullOrWhiteSpace(compiled.ContentHash)
                && string.Equals(liveStatus.ContentHash, compiled.ContentHash, StringComparison.OrdinalIgnoreCase);
            if (includeLive && !liveMatchesDraft)
            {
                omissions.Add(new { role = "runtime_evidence", reason = liveStatus is null || !liveStatus.Available ? "unavailable" : "draft_changed_during_export" });
            }

            var matchingReceiptCount = 0;
            var matchingActiveSetIncluded = false;
            if (liveMatchesDraft)
            {
                var exactLiveStatus = liveStatus!;
                var matchingActive = exactLiveStatus.ActiveSet is not null
                    && exactLiveStatus.ActiveSet.PackId == project.PackId
                    && exactLiveStatus.ActiveSet.Version == project.Version
                    && string.Equals(exactLiveStatus.ActiveSet.ContentHash, compiled.ContentHash, StringComparison.OrdinalIgnoreCase)
                    ? exactLiveStatus.ActiveSet
                    : null;
                matchingActiveSetIncluded = matchingActive is not null;
                var matchingReceipts = exactLiveStatus.Receipts.Where(receipt => receipt.PackId == project.PackId
                    && receipt.Version == project.Version
                    && !string.IsNullOrWhiteSpace(receipt.ContentHash)
                    && string.Equals(receipt.ContentHash, compiled.ContentHash, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                matchingReceiptCount = matchingReceipts.Length;
                var evidenceJson = new JsonSerializerOptions(_host.Json)
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                builder.Add("evidence/runtime-status.json", JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schema_version = 1,
                    privacy_notice = "Explicitly included local Runtime evidence may contain local world or binding identifiers.",
                    exactLiveStatus.Available,
                    exactLiveStatus.Phase,
                    exactLiveStatus.NextInstruction,
                    exactLiveStatus.ContentHash,
                    exactLiveStatus.PackageSha256,
                    active_set = matchingActive,
                    exactLiveStatus.CurrentStageId,
                    exactLiveStatus.CurrentCount,
                    exactLiveStatus.RequiredCount,
                    receipts = matchingReceipts,
                    exactLiveStatus.Diagnostics
                }, evidenceJson));
            }

            var manifest = new
            {
                schema = "comfy-quest-studio-export/v1",
                project_id = project.ProjectId,
                current_revision = project.Revision,
                pack_id = project.PackId,
                version = project.Version,
                content_hash = compiled.ContentHash,
                options = new
                {
                    published_packages_requested = includePublished,
                    published_packages_included = publishedCount,
                    live_evidence_requested = includeLive,
                    live_status_included = liveMatchesDraft,
                    matching_active_set_included = matchingActiveSetIncluded,
                    matching_receipt_count = matchingReceiptCount
                },
                privacy = new
                {
                    authored_free_text = true,
                    absolute_paths = false,
                    browser_token = false,
                    runtime_receipts = matchingReceiptCount > 0,
                    usage_aggregate = false,
                    notice = liveMatchesDraft
                        ? "This bundle contains creator-authored text and explicitly requested local Runtime evidence; inspect it before sharing."
                        : includeLive
                            ? "This bundle contains creator-authored text. Requested Runtime evidence was unavailable or did not match this exact draft."
                            : "This bundle contains creator-authored text. Local Runtime evidence is excluded by default."
                },
                omissions,
                payload_entry_count = builder.Count,
                payload_expanded_byte_count = builder.TotalBytes,
                entries = builder.ManifestEntries(),
                inventory_note = "entries inventories every payload entry; manifest.json is the self-describing container index and is intentionally excluded."
            };
            builder.Add("manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, _host.Json));
            return StudioDownloadResult.Success(
                SafeDownloadName($"{project.PackId}-{project.Version}", project.ProjectId) + ".queststudio.zip",
                "application/vnd.comfy.queststudio.export+zip",
                builder.BuildZip());
        }
        catch (BundleLimitException ex)
        {
            return StudioDownloadResult.Fail(ex.Message);
        }
        catch (IOException)
        {
            return StudioDownloadResult.Fail("export_state_unreadable");
        }
        catch (UnauthorizedAccessException)
        {
            return StudioDownloadResult.Fail("export_state_unreadable");
        }
    }

    bool TryReadPersistedProject(string projectId, out StudioProjectDocument? project, out byte[]? draft)
    {
        project = null;
        draft = null;
        if (!SafeLocalId(projectId)) return false;
        var projectRoot = Path.Combine(_projectsRoot, projectId);
        if (!IsDirectChild(_projectsRoot, projectRoot)
            || !TryReadBounded(Path.Combine(projectRoot, "draft.json"), MaxDraftBytes, out draft)) return false;
        try
        {
            project = JsonSerializer.Deserialize<StudioProjectDocument>(draft!, _host.Json);
            return project is not null && project.ProjectId == projectId
                && QuestStudioWorkspace.NormalizeDocument(project);
        }
        catch { return false; }
    }

    void AddCertifiedHistoryIdentity(byte[] history, string expectedProjectId, List<PublishedIdentity> identities)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<StudioProjectSnapshot>(history, _host.Json);
            if (snapshot?.Project is null || snapshot.SchemaVersion is not (2 or 3)
                || snapshot.Project.ProjectId != expectedProjectId
                || !SafeHash(snapshot.ContentHash)
                || !QuestStudioWorkspace.NormalizeDocument(snapshot.Project)) return;
            var compiled = StudioGraphCompiler.Compile(snapshot.Project);
            if (!compiled.Ok || !string.Equals(compiled.ContentHash, snapshot.ContentHash, StringComparison.OrdinalIgnoreCase)) return;
            identities.Add(new(snapshot.Project.PackId, snapshot.Project.Version, snapshot.ContentHash));
        }
        catch { /* exact future/unknown history bytes remain exported but cannot authorize package inclusion */ }
    }

    int AddMatchingPublishedPackages(BundleBuilder builder, IReadOnlyList<PublishedIdentity> identities)
    {
        var valheim = _host.FindValheim();
        if (valheim is null || identities.Count == 0) return 0;
        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        var inboxRoot = Path.GetFullPath(Path.Combine(runtimeRoot, "inbox"));
        var count = 0;
        foreach (var candidate in new QuestPackStore(runtimeRoot).CheckInbox(RuntimeProductionEventCatalog.CreateSet())
                     .Where(value => value.IsValid
                         && identities.Any(identity => identity.PackId == value.Manifest.PackId
                             && identity.Version == value.Manifest.Version
                             && string.Equals(identity.ContentHash, value.ContentHash, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(value => Path.GetFileName(value.Path), StringComparer.Ordinal))
        {
            var name = Path.GetFileName(candidate.Path);
            if (!SafeQuestpackName(name) || !IsDirectChild(inboxRoot, candidate.Path)
                || !TryReadBounded(candidate.Path, QuestPackPublisher.MaxPackageBytes, out var bytes)) continue;
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes!)).ToLowerInvariant();
            if (!string.Equals(sha256, candidate.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            builder.Add("packages/published/" + name, bytes!);
            count++;
        }
        return count;
    }

    bool HasPublishedIdentityCollision(StudioProjectDocument project, string contentHash)
    {
        var valheim = _host.FindValheim();
        if (valheim is null) return false;
        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        return new QuestPackStore(runtimeRoot).CheckInbox(RuntimeProductionEventCatalog.CreateSet()).Any(candidate =>
            candidate.IsValid && candidate.Manifest.PackId == project.PackId && candidate.Manifest.Version == project.Version
            && !string.Equals(candidate.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase));
    }

    static bool TryReadBounded(string path, long maximum, out byte[]? bytes)
    {
        bytes = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 0 || info.Length > maximum) return false;
            bytes = File.ReadAllBytes(path);
            return bytes.LongLength == info.Length && bytes.LongLength <= maximum;
        }
        catch { return false; }
    }

    static bool SafeLocalId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 80
        && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
    static bool IsDirectChild(string parent, string candidate) => string.Equals(
        Path.GetDirectoryName(Path.GetFullPath(candidate)), Path.GetFullPath(parent),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    static bool SafeHash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    static bool SafeHistoryStem(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 220
        && !value.Contains("..", StringComparison.Ordinal)
        && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
        && (SafeHash(value) || value.Length > 65 && SafeHash(value[^64..]));
    static bool SafeQuestpackName(string value) => value.Length is > 0 and <= 120 && value == Path.GetFileName(value)
        && value.EndsWith(".questpack", StringComparison.OrdinalIgnoreCase)
        && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
    static string SafeDownloadName(string value, string fallback) => !string.IsNullOrWhiteSpace(value) && value.Length <= 110
        && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.') ? value : fallback;

    sealed class BundleBuilder
    {
        static readonly DateTimeOffset StableZipTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        readonly List<BundleEntry> _entries = new();
        public int Count => _entries.Count;
        public long TotalBytes { get; private set; }

        public void Add(string path, byte[] bytes)
        {
            if (!SafeEntry(path) || _entries.Any(value => value.Path == path)) throw new BundleLimitException("export_path_invalid");
            if (_entries.Count >= MaxEntries) throw new BundleLimitException("export_entry_limit");
            if (bytes.LongLength > MaxExpandedBytes - TotalBytes) throw new BundleLimitException("export_size_limit");
            _entries.Add(new(path, bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
            TotalBytes += bytes.LongLength;
        }

        public object[] ManifestEntries() => _entries.OrderBy(value => value.Path, StringComparer.Ordinal)
            .Select(value => (object)new { path = value.Path, byte_count = value.Bytes.LongLength, sha256 = value.Sha256 }).ToArray();

        public byte[] BuildZip()
        {
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var item in _entries.OrderBy(value => value.Path, StringComparer.Ordinal))
                {
                    var entry = archive.CreateEntry(item.Path, CompressionLevel.Optimal);
                    entry.LastWriteTime = StableZipTime;
                    using var stream = entry.Open();
                    stream.Write(item.Bytes);
                }
            }
            return output.ToArray();
        }

        static bool SafeEntry(string path) => path.Length is > 0 and <= 180 && !path.StartsWith('/') && !path.Contains('\\')
            && path.Split('/').All(part => part.Length > 0 && part is not "." and not ".."
                && part.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'));
    }

    sealed record BundleEntry(string Path, byte[] Bytes, string Sha256);
    sealed record PublishedIdentity(string PackId, string Version, string ContentHash);
    sealed class BundleLimitException(string message) : Exception(message);
}

public sealed record StudioExportRequest(bool? IncludePublishedPackages = null, bool? IncludeLiveEvidence = null);

public sealed record StudioDownloadResult(bool Ok, string? Error, string? Filename, string? ContentType, byte[]? Bytes,
    string? Sha256, string? ContentHash)
{
    public static StudioDownloadResult Success(string filename, string contentType, byte[] bytes, string? contentHash = null) =>
        new(true, null, filename, contentType, bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), contentHash);
    public static StudioDownloadResult Fail(string error) => new(false, error, null, null, null, null, null);
}

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Comfy.Quest.Studio;

public sealed record StudioBuildPlacementRequest(int ExpectedRevision, double X, double Y, double Z, double Yaw);
public sealed record StudioBuildPlacement(int Revision, double X, double Y, double Z, double Yaw);
public sealed record StudioBuildArtifactPin(string Name, string Path, long Bytes, string Sha256);

public sealed record StudioBuildStageReceipt(
    string Schema, string StageId, DateTimeOffset CompletedUtc, string BuildId,
    string CapsuleSha256, string SourceRevision, string GraphSha256,
    string CompiledPiecesSha256, string CanonicalPiecesSha256, string ImporterSha256,
    string? ImporterBundleSha256, StudioBuildPlacement Placement,
    IReadOnlyDictionary<string, StudioBuildArtifactPin> Artifacts, string Destination,
    bool PlacementApplied, bool CreatorSessionStarted, bool MailboxRequestWritten,
    bool WorldMutationPerformed);

public sealed record StudioBuildDocument(
    string Schema, string BuildId, string CapsuleSha256, string FixtureId,
    string SourceRevision, int Revision, StudioBuildPlacement Placement, bool Staged,
    bool PlacementApplied, string DerivedName, int PieceCount,
    IReadOnlyDictionary<string, int> PrefabCounts, IReadOnlyList<string> ReconciliationIds,
    string GraphSha256, string ConstraintModelSha256, string InterpretationReceiptSha256,
    string CompilationReceiptSha256, string CompiledPiecesSha256,
    string CandidateCaptureSha256, string CandidatePiecesSha256, string PrefabGeometrySha256,
    string CanonicalCaptureSha256, string CanonicalBlueprintSha256,
    string CanonicalPiecesSha256, string CanonicalManifestSha256, string ImporterSha256,
    string? ImporterBundleSha256, JsonElement Source, JsonElement Architecture,
    JsonElement ConstraintModel, JsonElement InterpretationReceipt,
    JsonElement CompilationReceipt, JsonElement Pieces, JsonElement PrefabGeometry,
    StudioBuildStageReceipt? LatestStage, bool NoWorldMutation);

public sealed record StudioBuildOperationResult(
    bool Ok, int Status, string? Error = null, StudioBuildDocument? Build = null,
    StudioBuildStageReceipt? Receipt = null, bool AlreadyPresent = false, int? Revision = null)
{
    public static StudioBuildOperationResult Fail(int status, string error, int? revision = null) =>
        new(false, status, error, Revision: revision);
}

sealed record StudioBuildDescriptor(
    string Schema, string BuildId, string CapsuleSha256, string FixtureId,
    string SourceRevision, string DerivedName, int PieceCount,
    Dictionary<string, int> PrefabCounts, string[] ReconciliationIds,
    string GraphSha256, string ConstraintModelSha256, string InterpretationReceiptSha256,
    string CompilationReceiptSha256, string CompiledPiecesSha256,
    string CandidateCaptureSha256, string CandidatePiecesSha256, string PrefabGeometrySha256,
    string CanonicalCaptureSha256, string CanonicalBlueprintSha256,
    string CanonicalPiecesSha256, string CanonicalManifestSha256, string ImporterSha256,
    string? ImporterBundleSha256, JsonElement Source, JsonElement Architecture,
    JsonElement ConstraintModel, JsonElement InterpretationReceipt,
    JsonElement CompilationReceipt, JsonElement Pieces, JsonElement PrefabGeometry);

sealed record ValidatedArchitecturalCapsule(
    string BuildId, string SourceRevision, int PieceCount, Dictionary<string, int> PrefabCounts,
    string[] ReconciliationIds, Dictionary<string, byte[]> Files, JsonElement Source,
    JsonElement Graph, JsonElement Constraints, JsonElement Interpretation,
    JsonElement Compilation, JsonElement Pieces, JsonElement Candidate,
    JsonElement Geometry, string CandidatePiecesSha256);

sealed record ImporterRunResult(bool Available, int ExitCode, string Output, string Error);
sealed record ValidatedImporterOutput(
    string Folder, string CapturePath, string BlueprintPath, string ManifestPath,
    string CaptureSha256, string BlueprintSha256, string ManifestSha256, string PiecesSha256);
sealed record StageJournal(
    string Schema, string BuildId, string CaptureTemporary, string BlueprintTemporary,
    string CaptureDestination, string BlueprintDestination, string CaptureSha256,
    string BlueprintSha256);

sealed class BuildContractException(string error, int status = StatusCodes.Status400BadRequest) : Exception(error)
{
    public string Error { get; } = error;
    public int Status { get; } = status;
}

public sealed class QuestStudioBuildService
{
    public const long MaxCompressedBytes = 2 * 1024 * 1024;
    public const long MaxExpandedBytes = 8 * 1024 * 1024;
    public const int MaxMembers = 32;
    public const int MaxPieces = 256;
    const int MaxImporterOutputCharacters = 32 * 1024;
    const string CapsuleSchema = "creator-os-architectural-build-capsule/v0";
    const string BuildSchema = "comfy-quest-studio-build/v1";
    const string DescriptorSchema = "comfy-quest-studio-build-descriptor/v1";
    const string StageSchema = "comfy-quest-studio-build-stage/v1";
    const string CaptureSchema = "comfy-questlab-capture/v1";
    static readonly string[] RequiredMembers =
    [
        "capsule.json", "solved-building.graph.json", "constraint-model.json",
        "interpretation-receipt.json", "compilation-receipt.json", "pieces.json",
        "architectural-candidate.capture.json", "prefab-geometry.json",
    ];
    static readonly IReadOnlyDictionary<string, string> RequiredEntrypoints =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["graph"] = "solved-building.graph.json",
            ["constraints"] = "constraint-model.json",
            ["interpretation"] = "interpretation-receipt.json",
            ["compilation"] = "compilation-receipt.json",
            ["pieces"] = "pieces.json",
            ["candidate_capture"] = "architectural-candidate.capture.json",
            ["prefab_geometry"] = "prefab-geometry.json",
        };

    readonly object _gate = new();
    readonly IQuestStudioHost _host;
    readonly string _root;
    internal Action<string>? StageFaultInjector { get; set; }

    public QuestStudioBuildService(IQuestStudioHost host)
    {
        _host = host;
        _root = Path.Combine(host.StateDirectory, "quest-studio", "builds");
        Directory.CreateDirectory(_root);
    }

    public IReadOnlyList<StudioBuildDocument> List()
    {
        lock (_gate)
        {
            return Directory.EnumerateDirectories(_root)
                .Where(path => SafeBuildId(Path.GetFileName(path)))
                .Select(path => ReadBuild(Path.GetFileName(path)))
                .Where(value => value is not null).Cast<StudioBuildDocument>()
                .OrderBy(value => value.BuildId, StringComparer.Ordinal).ToArray();
        }
    }

    public StudioBuildDocument? Read(string buildId)
    {
        if (!SafeBuildId(buildId)) return null;
        lock (_gate) return ReadBuild(buildId);
    }

    public Task<StudioBuildOperationResult> ImportAsync(HttpRequest request, CancellationToken cancellationToken) =>
        ImportAsync(request.Body, request.ContentLength, cancellationToken);

    public async Task<StudioBuildOperationResult> ImportAsync(
        Stream source, long? contentLength, CancellationToken cancellationToken = default)
    {
        if (contentLength is < 0 or > MaxCompressedBytes)
            return StudioBuildOperationResult.Fail(413, "capsule_too_large");
        byte[] capsuleBytes;
        try { capsuleBytes = await ReadBoundedAsync(source, MaxCompressedBytes, "capsule_too_large", cancellationToken); }
        catch (BuildContractException error) { return StudioBuildOperationResult.Fail(error.Status, error.Error); }

        var buildId = Sha256(capsuleBytes);
        lock (_gate)
        {
            var existing = ReadBuild(buildId);
            if (existing is not null) return new(true, 200, Build: existing, AlreadyPresent: true);
            if (Directory.Exists(BuildPath(buildId)))
                return StudioBuildOperationResult.Fail(409, "build_state_conflict");
        }

        ValidatedArchitecturalCapsule validated;
        try { validated = await ValidateCapsuleAsync(capsuleBytes, buildId, cancellationToken); }
        catch (BuildContractException error) { return StudioBuildOperationResult.Fail(error.Status, error.Error); }
        catch (InvalidDataException) { return StudioBuildOperationResult.Fail(400, "capsule_not_zip"); }
        catch (JsonException) { return StudioBuildOperationResult.Fail(400, "capsule_json_invalid"); }
        catch (InvalidOperationException) { return StudioBuildOperationResult.Fail(400, "capsule_contract_inconsistent"); }

        var importer = (_host as IQuestStudioArchitecturalRAndDHost)?.ArchitecturalImporter;
        if (importer is null || string.IsNullOrWhiteSpace(importer.ScriptPath) ||
            string.IsNullOrWhiteSpace(importer.PythonExecutable) || !File.Exists(importer.ScriptPath) ||
            importer.StandaloneBundleManifestPath is not null && !File.Exists(importer.StandaloneBundleManifestPath))
            return StudioBuildOperationResult.Fail(503, "architectural_importer_unavailable");

        var importerHash = Sha256File(importer.ScriptPath);
        var bundleHash = importer.StandaloneBundleManifestPath is null
            ? null : Sha256File(importer.StandaloneBundleManifestPath);
        var temporaryRoot = Path.Combine(Path.GetTempPath(),
            "comfy-quest-architectural-import-" + Guid.NewGuid().ToString("N"));
        var candidateRoot = Path.Combine(_root, ".import-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            var candidateCapture = Path.Combine(temporaryRoot, "architectural-candidate.capture.json");
            await File.WriteAllBytesAsync(candidateCapture,
                validated.Files["architectural-candidate.capture.json"], cancellationToken);
            var derivedName = DerivedName(buildId);
            var outputRoot = Path.Combine(temporaryRoot, "derived");
            var importerResult = await RunImporterAsync(importer, candidateCapture, outputRoot,
                derivedName, cancellationToken);
            if (!importerResult.Available)
                return StudioBuildOperationResult.Fail(503, "architectural_importer_unavailable");
            if (importerResult.ExitCode != 0)
                return StudioBuildOperationResult.Fail(400, "architectural_import_rejected");

            var imported = ValidateImporterOutput(outputRoot, derivedName,
                validated.Candidate, validated.PieceCount);
            Directory.CreateDirectory(candidateRoot);
            await File.WriteAllBytesAsync(Path.Combine(candidateRoot, "capsule.zip"),
                capsuleBytes, cancellationToken);
            var memberRoot = Path.Combine(candidateRoot, "members");
            Directory.CreateDirectory(memberRoot);
            foreach (var (name, bytes) in validated.Files.OrderBy(item => item.Key, StringComparer.Ordinal))
                await File.WriteAllBytesAsync(Path.Combine(memberRoot, name), bytes, cancellationToken);
            var derivedRoot = Path.Combine(candidateRoot, "derived", derivedName);
            Directory.CreateDirectory(derivedRoot);
            foreach (var file in Directory.EnumerateFiles(imported.Folder))
                File.Copy(file, Path.Combine(derivedRoot, Path.GetFileName(file)));

            var descriptor = new StudioBuildDescriptor(
                DescriptorSchema, buildId, buildId, "tn0304", validated.SourceRevision,
                derivedName, validated.PieceCount, validated.PrefabCounts,
                validated.ReconciliationIds,
                Sha256(validated.Files["solved-building.graph.json"]),
                Sha256(validated.Files["constraint-model.json"]),
                Sha256(validated.Files["interpretation-receipt.json"]),
                Sha256(validated.Files["compilation-receipt.json"]),
                Sha256(validated.Files["pieces.json"]),
                Sha256(validated.Files["architectural-candidate.capture.json"]),
                validated.CandidatePiecesSha256,
                Sha256(validated.Files["prefab-geometry.json"]),
                imported.CaptureSha256, imported.BlueprintSha256, imported.PiecesSha256,
                imported.ManifestSha256, importerHash, bundleHash, validated.Source,
                validated.Graph, validated.Constraints, validated.Interpretation,
                validated.Compilation, validated.Pieces, validated.Geometry);
            AtomicWriteJson(Path.Combine(candidateRoot, "descriptor.json"), descriptor, create: true);
            AtomicWriteJson(Path.Combine(candidateRoot, "placement.json"),
                new StudioBuildPlacement(1, 0, 0, 0, 0), create: true);

            lock (_gate)
            {
                var existing = ReadBuild(buildId);
                if (existing is not null)
                    return new(true, 200, Build: existing, AlreadyPresent: true);
                if (Directory.Exists(BuildPath(buildId)))
                    return StudioBuildOperationResult.Fail(409, "build_state_conflict");
                Directory.Move(candidateRoot, BuildPath(buildId));
                var build = ReadBuild(buildId) ??
                    throw new InvalidDataException("published build could not be reopened");
                return new(true, 201, Build: build);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (BuildContractException error) { return StudioBuildOperationResult.Fail(error.Status, error.Error); }
        catch (IOException) { return StudioBuildOperationResult.Fail(500, "architectural_import_persist_failed"); }
        catch (UnauthorizedAccessException) { return StudioBuildOperationResult.Fail(500, "architectural_import_persist_failed"); }
        finally
        {
            TryDeleteDirectory(temporaryRoot);
            TryDeleteDirectory(candidateRoot);
        }
    }

    public StudioBuildOperationResult Placement(string buildId, StudioBuildPlacementRequest request)
    {
        if (!SafeBuildId(buildId)) return StudioBuildOperationResult.Fail(404, "build_not_found");
        if (!new[] { request.X, request.Y, request.Z, request.Yaw }.All(double.IsFinite))
            return StudioBuildOperationResult.Fail(400, "placement_invalid");
        lock (_gate)
        {
            var build = ReadBuild(buildId);
            if (build is null) return StudioBuildOperationResult.Fail(404, "build_not_found");
            if (build.Revision != request.ExpectedRevision)
                return StudioBuildOperationResult.Fail(409, "revision_conflict", build.Revision);
            var placement = new StudioBuildPlacement(build.Revision + 1,
                request.X, request.Y, request.Z, request.Yaw);
            AtomicWriteJson(Path.Combine(BuildPath(buildId), "placement.json"), placement, create: false);
            return new(true, 200, Build: ReadBuild(buildId));
        }
    }

    public StudioBuildOperationResult Stage(string buildId)
    {
        if (!SafeBuildId(buildId)) return StudioBuildOperationResult.Fail(404, "build_not_found");
        lock (_gate)
        {
            var build = ReadBuild(buildId);
            var descriptor = ReadJson<StudioBuildDescriptor>(
                Path.Combine(BuildPath(buildId), "descriptor.json"));
            if (build is null || descriptor is null)
                return StudioBuildOperationResult.Fail(404, "build_not_found");
            var valheim = _host.FindValheim();
            if (string.IsNullOrWhiteSpace(valheim) || !Directory.Exists(valheim))
                return StudioBuildOperationResult.Fail(503, "valheim_not_found");

            var canonicalRoot = Path.Combine(BuildPath(buildId), "derived", descriptor.DerivedName);
            var sourceCapture = Path.Combine(canonicalRoot, descriptor.DerivedName + ".capture.json");
            var sourceBlueprint = Path.Combine(canonicalRoot, descriptor.DerivedName + ".blueprint");
            var manifest = Path.Combine(canonicalRoot, "manifest.json");
            try { VerifyCanonicalArtifacts(descriptor, sourceCapture, sourceBlueprint, manifest); }
            catch (BuildContractException error)
            { return StudioBuildOperationResult.Fail(error.Status, error.Error); }

            var destination = Path.Combine(Path.GetFullPath(valheim), "BepInEx", "config",
                "comfy-quest-lab", "blueprints");
            Directory.CreateDirectory(destination);
            var destinationCapture = Path.Combine(destination,
                descriptor.DerivedName + ".capture.json");
            var destinationBlueprint = Path.Combine(destination,
                descriptor.DerivedName + ".blueprint");
            var captureBytes = File.ReadAllBytes(sourceCapture);
            var blueprintBytes = File.ReadAllBytes(sourceBlueprint);
            try
            {
                using var lease = new FileStream(Path.Combine(_root, ".stage.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                RecoverStageJournal(buildId, descriptor);
                var captureExists = File.Exists(destinationCapture);
                var blueprintExists = File.Exists(destinationBlueprint);
                var captureMatches = captureExists && BytesEqual(destinationCapture, captureBytes);
                var blueprintMatches = blueprintExists && BytesEqual(destinationBlueprint, blueprintBytes);
                if (captureExists || blueprintExists)
                {
                    if (!(captureExists && blueprintExists && captureMatches && blueprintMatches))
                        return StudioBuildOperationResult.Fail(409, "stage_collision");
                }
                else
                {
                    CommitStagePair(buildId, descriptor, captureBytes, blueprintBytes,
                        destinationCapture, destinationBlueprint);
                }
                if (!BytesEqual(destinationCapture, captureBytes) ||
                    !BytesEqual(destinationBlueprint, blueprintBytes))
                    return StudioBuildOperationResult.Fail(500, "stage_verification_failed");

                var stageId = Sha256(Encoding.UTF8.GetBytes(string.Join("\n",
                    buildId, build.Revision.ToString(CultureInfo.InvariantCulture),
                    descriptor.CanonicalCaptureSha256, descriptor.CanonicalBlueprintSha256,
                    Path.GetFullPath(destination))));
                var stagesRoot = Path.Combine(BuildPath(buildId), "stages");
                Directory.CreateDirectory(stagesRoot);
                var receiptPath = Path.Combine(stagesRoot, stageId + ".json");
                var receipt = ReadJson<StudioBuildStageReceipt>(receiptPath) ??
                    new StudioBuildStageReceipt(
                        StageSchema, stageId, DateTimeOffset.UtcNow, buildId,
                        descriptor.CapsuleSha256, descriptor.SourceRevision,
                        descriptor.GraphSha256, descriptor.CompiledPiecesSha256,
                        descriptor.CanonicalPiecesSha256, descriptor.ImporterSha256,
                        descriptor.ImporterBundleSha256, build.Placement,
                        new Dictionary<string, StudioBuildArtifactPin>(StringComparer.Ordinal)
                        {
                            ["capture"] = new(Path.GetFileName(destinationCapture),
                                destinationCapture, captureBytes.LongLength,
                                descriptor.CanonicalCaptureSha256),
                            ["blueprint"] = new(Path.GetFileName(destinationBlueprint),
                                destinationBlueprint, blueprintBytes.LongLength,
                                descriptor.CanonicalBlueprintSha256),
                        }, destination, false, false, false, false);
                if (!File.Exists(receiptPath)) AtomicWriteJson(receiptPath, receipt, create: true);
                AtomicWriteJson(Path.Combine(BuildPath(buildId), "latest-stage.json"),
                    receipt, create: false);
                return new(true, 200, Build: ReadBuild(buildId), Receipt: receipt,
                    AlreadyPresent: captureExists);
            }
            catch (IOException) { return StudioBuildOperationResult.Fail(409, "stage_busy_or_failed"); }
            catch (UnauthorizedAccessException)
            { return StudioBuildOperationResult.Fail(500, "stage_failed"); }
        }
    }

    void CommitStagePair(string buildId, StudioBuildDescriptor descriptor,
        byte[] captureBytes, byte[] blueprintBytes, string captureDestination,
        string blueprintDestination)
    {
        var token = Guid.NewGuid().ToString("N");
        var captureTemporary = Path.Combine(Path.GetDirectoryName(captureDestination)!,
            "." + Path.GetFileName(captureDestination) + "." + token + ".tmp");
        var blueprintTemporary = Path.Combine(Path.GetDirectoryName(blueprintDestination)!,
            "." + Path.GetFileName(blueprintDestination) + "." + token + ".tmp");
        var journalPath = Path.Combine(BuildPath(buildId), "stage-transaction.json");
        var captureCommitted = false;
        var blueprintCommitted = false;
        try
        {
            WriteThrough(captureTemporary, captureBytes);
            WriteThrough(blueprintTemporary, blueprintBytes);
            if (!BytesEqual(captureTemporary, captureBytes) ||
                !BytesEqual(blueprintTemporary, blueprintBytes))
                throw new IOException("stage temporary verification failed");
            AtomicWriteJson(journalPath, new StageJournal(
                "comfy-quest-studio-build-stage-transaction/v1", buildId,
                captureTemporary, blueprintTemporary, captureDestination,
                blueprintDestination, descriptor.CanonicalCaptureSha256,
                descriptor.CanonicalBlueprintSha256), create: false);
            StageFaultInjector?.Invoke("before_commit");
            File.Move(captureTemporary, captureDestination);
            captureCommitted = true;
            StageFaultInjector?.Invoke("after_capture_commit");
            File.Move(blueprintTemporary, blueprintDestination);
            blueprintCommitted = true;
            StageFaultInjector?.Invoke("after_blueprint_commit");
            File.Delete(journalPath);
        }
        catch
        {
            if (blueprintCommitted && File.Exists(blueprintDestination) &&
                Sha256File(blueprintDestination) == descriptor.CanonicalBlueprintSha256)
                File.Delete(blueprintDestination);
            if (captureCommitted && File.Exists(captureDestination) &&
                Sha256File(captureDestination) == descriptor.CanonicalCaptureSha256)
                File.Delete(captureDestination);
            if (File.Exists(journalPath)) File.Delete(journalPath);
            throw;
        }
        finally
        {
            TryDeleteFile(captureTemporary);
            TryDeleteFile(blueprintTemporary);
        }
    }

    void RecoverStageJournal(string buildId, StudioBuildDescriptor descriptor)
    {
        var path = Path.Combine(BuildPath(buildId), "stage-transaction.json");
        var journal = ReadJson<StageJournal>(path);
        if (journal is null) { TryDeleteFile(path); return; }
        if (journal.BuildId != buildId ||
            !SafeStagePath(journal.CaptureDestination,
                descriptor.DerivedName + ".capture.json") ||
            !SafeStagePath(journal.BlueprintDestination,
                descriptor.DerivedName + ".blueprint"))
            throw new IOException("stage journal identity mismatch");
        var captureComplete = File.Exists(journal.CaptureDestination) &&
            Sha256File(journal.CaptureDestination) == journal.CaptureSha256;
        var blueprintComplete = File.Exists(journal.BlueprintDestination) &&
            Sha256File(journal.BlueprintDestination) == journal.BlueprintSha256;
        if (!(captureComplete && blueprintComplete))
        {
            if (captureComplete) File.Delete(journal.CaptureDestination);
            if (blueprintComplete) File.Delete(journal.BlueprintDestination);
        }
        TryDeleteFile(journal.CaptureTemporary);
        TryDeleteFile(journal.BlueprintTemporary);
        TryDeleteFile(path);
    }

    static bool SafeStagePath(string path, string filename) =>
        string.Equals(Path.GetFileName(path), filename, StringComparison.Ordinal) &&
        Path.IsPathFullyQualified(path);

    async Task<ValidatedArchitecturalCapsule> ValidateCapsuleAsync(
        byte[] bytes, string buildId, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes, writable: false),
            ZipArchiveMode.Read);
        if (archive.Entries.Count > MaxMembers)
            throw new BuildContractException("capsule_member_limit");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name != entry.Name || name.Contains('\\') || name.StartsWith('/') ||
                name.Contains("..", StringComparison.Ordinal) ||
                !RequiredMembers.Contains(name, StringComparer.Ordinal))
                throw new BuildContractException("capsule_member_invalid");
            if (!files.TryAdd(name, []))
                throw new BuildContractException("capsule_duplicate_member");
            var remaining = MaxExpandedBytes - expanded;
            if (entry.Length < 0 || entry.Length > remaining)
                throw new BuildContractException("capsule_expanded_too_large");
            await using var entryStream = entry.Open();
            files[name] = await ReadBoundedAsync(entryStream, remaining,
                "capsule_expanded_too_large", cancellationToken);
            expanded += files[name].LongLength;
        }
        if (!new HashSet<string>(files.Keys, StringComparer.Ordinal).SetEquals(RequiredMembers))
            throw new BuildContractException("capsule_members_incomplete");

        var capsule = Parse(files["capsule.json"]);
        RequireSchema(capsule, "schema", CapsuleSchema, "capsule_schema_unsupported");
        RequireString(capsule, "fixture_id", "capsule_contract_inconsistent", "tn0304");
        var sourceRevision = RequireString(capsule, "source_revision",
            "capsule_contract_inconsistent");
        if (sourceRevision.Length is < 1 or > 128)
            throw new BuildContractException("capsule_contract_inconsistent");
        var source = RequireObject(capsule, "source", "capsule_contract_inconsistent");
        RequireString(source, "envelope_revision", "capsule_contract_inconsistent",
            sourceRevision);
        RequireSha(source, "identity_sha256", "capsule_contract_inconsistent");
        RequireSha(source, "producer_sha256", "capsule_contract_inconsistent");
        ValidateEntrypoints(RequireObject(capsule, "entrypoints",
            "capsule_contract_inconsistent"));
        ValidateMemberPins(RequireObject(capsule, "members",
            "capsule_contract_inconsistent"), files);

        var graph = Parse(files["solved-building.graph.json"]);
        var constraints = Parse(files["constraint-model.json"]);
        var interpretation = Parse(files["interpretation-receipt.json"]);
        var compilation = Parse(files["compilation-receipt.json"]);
        var pieces = Parse(files["pieces.json"]);
        var candidate = Parse(files["architectural-candidate.capture.json"]);
        var geometry = Parse(files["prefab-geometry.json"]);
        RequireSchema(graph, "schema", "architectural-solved-envelope/v0",
            "capsule_contract_inconsistent");
        RequireString(graph, "building_id", "capsule_contract_inconsistent", "tn0304");
        RequireString(graph, "status", "capsule_contract_inconsistent", "SOLVED_RND");
        RequireSchema(constraints, "schema", "architectural-constraint-model/v0",
            "capsule_contract_inconsistent");
        RequireString(constraints, "fixture_id", "capsule_contract_inconsistent", "tn0304");
        RequireSchema(interpretation, "schema", "architectural-interpretation-receipt/v0",
            "capsule_contract_inconsistent");
        RequireString(interpretation, "fixture_id", "capsule_contract_inconsistent", "tn0304");
        RequireString(interpretation, "status", "capsule_contract_inconsistent", "PASS");
        var gates = RequireArray(interpretation, "gates", "capsule_contract_inconsistent");
        if (gates.GetArrayLength() == 0 || gates.EnumerateArray().Any(value =>
                RequireString(value, "status", "capsule_contract_inconsistent") != "PASS"))
            throw new BuildContractException("capsule_interpretation_gate_failed");
        RequireSchema(compilation, "schema",
            "architectural-envelope-compilation-receipt/v0", "capsule_contract_inconsistent");
        if (!RequireBool(compilation, "within_budget", "capsule_contract_inconsistent"))
            throw new BuildContractException("capsule_contract_inconsistent");
        if (pieces.ValueKind != JsonValueKind.Array)
            throw new BuildContractException("capsule_contract_inconsistent");
        var pieceCount = pieces.GetArrayLength();
        if (pieceCount is < 1 or > MaxPieces ||
            RequireInt(capsule, "piece_count", "capsule_contract_inconsistent") != pieceCount ||
            RequireInt(compilation, "piece_count", "capsule_contract_inconsistent") != pieceCount ||
            RequireInt(compilation, "maximum_pieces", "capsule_contract_inconsistent") < pieceCount)
            throw new BuildContractException("capsule_piece_count_mismatch");

        var sourceSignatures = new List<string>(pieceCount);
        var prefabCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var piece in pieces.EnumerateArray())
        {
            var prefab = RequireBoundedString(piece, "prefab", 128,
                "capsule_contract_inconsistent");
            var category = RequireBoundedString(piece, "category", 64,
                "capsule_contract_inconsistent");
            var position = NumberArray(piece, "position", 3, "capsule_contract_inconsistent");
            var rotation = NumberArray(piece, "rotation", 4, "capsule_contract_inconsistent");
            if (Math.Abs(rotation.Sum(value => value * value) - 1) > 0.1)
                throw new BuildContractException("capsule_contract_inconsistent");
            prefabCounts[prefab] = prefabCounts.GetValueOrDefault(prefab) + 1;
            sourceSignatures.Add(CaptureSignature(prefab, category, position, rotation));
        }
        sourceSignatures.Sort(StringComparer.Ordinal);
        if (!MapsEqual(prefabCounts, ReadIntMap(compilation, "prefab_counts",
                "capsule_contract_inconsistent")) ||
            !MapsEqual(prefabCounts, ReadIntMap(capsule, "prefab_counts",
                "capsule_contract_inconsistent")))
            throw new BuildContractException("capsule_prefab_count_mismatch");
        if (RequireSha(capsule, "compiled_pieces_sha256",
                "capsule_contract_inconsistent") != Sha256(files["pieces.json"]))
            throw new BuildContractException("capsule_hash_mismatch");

        RequireSchema(candidate, "Schema", CaptureSchema, "capsule_contract_inconsistent");
        RequireString(candidate, "Selection", "capsule_not_architectural",
            "architectural-import-candidate");
        if (RequireInt(candidate, "PieceCount", "capsule_contract_inconsistent") != pieceCount)
            throw new BuildContractException("capsule_piece_count_mismatch");
        var candidatePieces = RequireArray(candidate, "Pieces", "capsule_contract_inconsistent");
        if (candidatePieces.GetArrayLength() != pieceCount)
            throw new BuildContractException("capsule_piece_count_mismatch");
        var candidateSignatures = candidatePieces.EnumerateArray()
            .Select(CapturePieceSignature).ToArray();
        if (!candidateSignatures.SequenceEqual(sourceSignatures, StringComparer.Ordinal))
            throw new BuildContractException("capsule_piece_projection_mismatch");
        var candidatePiecesSha = ComputePiecesHash(candidateSignatures);
        if (RequireSha(candidate, "PiecesSha256", "capsule_contract_inconsistent") !=
                candidatePiecesSha ||
            RequireSha(capsule, "candidate_pieces_sha256",
                "capsule_contract_inconsistent") != candidatePiecesSha)
            throw new BuildContractException("capsule_piece_hash_mismatch");

        var graphReconciliations = ReadIdSet(graph, "reconciliations");
        var interpretationReconciliations = ReadIdSet(interpretation, "reconciliations");
        var compilationReconciliations = ReadStringSet(compilation, "reconciliation_ids");
        var capsuleReconciliations = ReadStringSet(capsule, "reconciliation_ids");
        if (graphReconciliations.Count == 0 ||
            !graphReconciliations.SetEquals(interpretationReconciliations) ||
            !graphReconciliations.SetEquals(compilationReconciliations) ||
            !graphReconciliations.SetEquals(capsuleReconciliations))
            throw new BuildContractException("capsule_reconciliation_mismatch");
        if (!JsonElement.DeepEquals(RequireArray(graph, "game_adaptations",
                "capsule_contract_inconsistent"), RequireArray(capsule,
                "game_adaptations", "capsule_contract_inconsistent")))
            throw new BuildContractException("capsule_adaptation_mismatch");
        ValidateArchitecturalDimensions(graph);
        ValidateGeometry(geometry, prefabCounts.Keys);

        return new(buildId, sourceRevision, pieceCount, prefabCounts,
            graphReconciliations.Order(StringComparer.Ordinal).ToArray(), files,
            source.Clone(), graph, constraints, interpretation, compilation, pieces,
            candidate, geometry, candidatePiecesSha);
    }

    static void ValidateArchitecturalDimensions(JsonElement graph)
    {
        var dimensions = RequireObject(graph, "dimensions", "capsule_contract_inconsistent");
        foreach (var name in new[]
                 { "width_m", "depth_m", "wall_height_m", "ridge_height_m", "roof_pitch_degrees" })
        {
            var feature = RequireObject(dimensions, name, "capsule_contract_inconsistent");
            if (RequireFiniteNumber(feature, "value", "capsule_contract_inconsistent") <= 0)
                throw new BuildContractException("capsule_contract_inconsistent");
            RequireBoundedString(feature, "derivation", 40, "capsule_contract_inconsistent");
            RequireArray(feature, "source_views", "capsule_contract_inconsistent");
            RequireArray(feature, "supporting_constraints", "capsule_contract_inconsistent");
            RequireArray(feature, "conflicting_constraints", "capsule_contract_inconsistent");
        }
        var roofs = RequireArray(graph, "roofs", "capsule_contract_inconsistent");
        if (roofs.GetArrayLength() != 1 ||
            !RequireBool(roofs[0], "equal_pitch", "capsule_contract_inconsistent"))
            throw new BuildContractException("capsule_contract_inconsistent");
    }

    static void ValidateGeometry(JsonElement geometry, IEnumerable<string> usedPrefabs)
    {
        RequireSchema(geometry, "schema", "creator-os-prefab-geometry/v0",
            "capsule_contract_inconsistent");
        var prefabs = RequireObject(geometry, "prefabs", "capsule_contract_inconsistent");
        if (!new HashSet<string>(prefabs.EnumerateObject().Select(value => value.Name),
                StringComparer.Ordinal).SetEquals(usedPrefabs))
            throw new BuildContractException("capsule_prefab_geometry_mismatch");
        foreach (var prefab in prefabs.EnumerateObject())
        {
            RequireString(prefab.Value, "kind", "capsule_contract_inconsistent",
                "bounded-proxy");
            if (NumberArray(prefab.Value, "mesh_bounds_m", 3,
                    "capsule_contract_inconsistent").Any(value => value <= 0))
                throw new BuildContractException("capsule_contract_inconsistent");
        }
    }

    static void ValidateEntrypoints(JsonElement entrypoints)
    {
        var actual = entrypoints.EnumerateObject().ToDictionary(value => value.Name,
            value => value.Value.ValueKind == JsonValueKind.String
                ? value.Value.GetString()! : string.Empty, StringComparer.Ordinal);
        if (actual.Count != RequiredEntrypoints.Count || RequiredEntrypoints.Any(item =>
                !actual.TryGetValue(item.Key, out var value) || value != item.Value))
            throw new BuildContractException("capsule_entrypoints_invalid");
    }

    static void ValidateMemberPins(JsonElement pins,
        IReadOnlyDictionary<string, byte[]> files)
    {
        var expected = new HashSet<string>(RequiredMembers.Where(value =>
            value != "capsule.json"), StringComparer.Ordinal);
        var actual = new HashSet<string>(pins.EnumerateObject().Select(value => value.Name),
            StringComparer.Ordinal);
        if (!expected.SetEquals(actual))
            throw new BuildContractException("capsule_member_manifest_invalid");
        foreach (var pin in pins.EnumerateObject())
        {
            if (RequireInt64(pin.Value, "bytes", "capsule_contract_inconsistent") !=
                    files[pin.Name].LongLength ||
                RequireSha(pin.Value, "sha256", "capsule_contract_inconsistent") !=
                    Sha256(files[pin.Name]))
                throw new BuildContractException("capsule_hash_mismatch");
        }
    }

    async Task<ImporterRunResult> RunImporterAsync(QuestStudioArchitecturalImporter importer,
        string capture, string output, string name, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = importer.PythonExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(importer.ScriptPath)!,
        };
        foreach (var argument in new[]
                 {
                     importer.ScriptPath, capture, "--output-root", output,
                     "--derive-architectural-name", name, "--derive-yaw-degrees", "0",
                 })
            start.ArgumentList.Add(argument);
        if (importer.StandaloneBundleManifestPath is not null)
        {
            start.ArgumentList.Add("--standalone-bundle-manifest");
            start.ArgumentList.Add(importer.StandaloneBundleManifestPath);
        }
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) return new(false, -1, string.Empty, string.Empty);
        }
        catch { return new(false, -1, string.Empty, string.Empty); }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            cancellationToken.ThrowIfCancellationRequested();
            return new(true, -1, string.Empty, "importer_timeout");
        }
        return new(true, process.ExitCode, Bound(await outputTask), Bound(await errorTask));
    }

    static ValidatedImporterOutput ValidateImporterOutput(string outputRoot,
        string derivedName, JsonElement architecturalCandidate, int expectedCount)
    {
        if (!Directory.Exists(outputRoot))
            throw new BuildContractException("architectural_import_incomplete");
        var directories = Directory.GetDirectories(outputRoot);
        if (directories.Length != 1 || Path.GetFileName(directories[0]) != derivedName)
            throw new BuildContractException("architectural_import_incomplete");
        var folder = directories[0];
        var expectedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            derivedName + ".capture.json", derivedName + ".blueprint",
            "plan.json", "preview.svg", "manifest.json",
        };
        var actualFiles = Directory.GetFiles(folder).Select(path => Path.GetFileName(path)!)
            .ToHashSet(StringComparer.Ordinal);
        if (!expectedFiles.SetEquals(actualFiles) || Directory.GetDirectories(folder).Length != 0)
            throw new BuildContractException("architectural_import_incomplete");
        if (Directory.GetFiles(folder).Sum(path => new FileInfo(path).Length) > MaxExpandedBytes)
            throw new BuildContractException("architectural_import_incomplete");
        var capturePath = Path.Combine(folder, derivedName + ".capture.json");
        var blueprintPath = Path.Combine(folder, derivedName + ".blueprint");
        var manifestPath = Path.Combine(folder, "manifest.json");
        var capture = Parse(File.ReadAllBytes(capturePath));
        RequireSchema(capture, "Schema", CaptureSchema, "architectural_import_incomplete");
        RequireString(capture, "Name", "architectural_import_incomplete", derivedName);
        RequireString(capture, "Selection", "architectural_import_incomplete", "lab");
        if (RequireInt(capture, "PieceCount", "architectural_import_incomplete") !=
            expectedCount) throw new BuildContractException("architectural_import_incomplete");
        var pieces = RequireArray(capture, "Pieces", "architectural_import_incomplete");
        if (pieces.GetArrayLength() != expectedCount)
            throw new BuildContractException("architectural_import_incomplete");
        var signatures = pieces.EnumerateArray().Select(CapturePieceSignature).ToArray();
        var piecesHash = ComputePiecesHash(signatures);
        if (RequireSha(capture, "PiecesSha256", "architectural_import_incomplete") !=
                piecesHash || piecesHash != ExpectedDerivedPiecesHash(architecturalCandidate))
            throw new BuildContractException("architectural_import_drift");

        var manifest = Parse(File.ReadAllBytes(manifestPath));
        RequireSchema(manifest, "schema", "comfy-quest-godbuild/v1",
            "architectural_import_incomplete");
        RequireString(manifest, "name", "architectural_import_incomplete", derivedName);
        RequireString(manifest, "source_pieces_sha256", "architectural_import_incomplete",
            piecesHash);
        if (RequireInt(manifest, "piece_count", "architectural_import_incomplete") !=
            expectedCount) throw new BuildContractException("architectural_import_incomplete");
        var artifacts = RequireObject(manifest, "artifacts", "architectural_import_incomplete");
        var pinnedFiles = expectedFiles.Where(value => value != "manifest.json")
            .ToHashSet(StringComparer.Ordinal);
        if (!pinnedFiles.SetEquals(artifacts.EnumerateObject().Select(value => value.Name)))
            throw new BuildContractException("architectural_import_incomplete");
        foreach (var artifact in artifacts.EnumerateObject())
        {
            var path = Path.Combine(folder, artifact.Name);
            if (RequireInt64(artifact.Value, "bytes", "architectural_import_incomplete") !=
                    new FileInfo(path).Length ||
                RequireSha(artifact.Value, "sha256", "architectural_import_incomplete") !=
                    Sha256File(path))
                throw new BuildContractException("architectural_import_drift");
        }
        return new(folder, capturePath, blueprintPath, manifestPath,
            Sha256File(capturePath), Sha256File(blueprintPath), Sha256File(manifestPath),
            piecesHash);
    }

    static string ExpectedDerivedPiecesHash(JsonElement candidate)
    {
        var pieces = RequireArray(candidate, "Pieces", "architectural_import_drift")
            .EnumerateArray().ToArray();
        var minima = new[]
        {
            pieces.Min(value => RequireFiniteNumber(value, "X", "architectural_import_drift")),
            pieces.Min(value => RequireFiniteNumber(value, "Y", "architectural_import_drift")),
            pieces.Min(value => RequireFiniteNumber(value, "Z", "architectural_import_drift")),
        };
        var signatures = new List<string>(pieces.Length);
        foreach (var piece in pieces)
        {
            var position = new[]
            {
                Round(RequireFiniteNumber(piece, "X", "architectural_import_drift") - minima[0], 4),
                Round(RequireFiniteNumber(piece, "Y", "architectural_import_drift") - minima[1], 4),
                Round(RequireFiniteNumber(piece, "Z", "architectural_import_drift") - minima[2], 4),
            };
            var rotation = new[]
            {
                RequireFiniteNumber(piece, "Qx", "architectural_import_drift"),
                RequireFiniteNumber(piece, "Qy", "architectural_import_drift"),
                RequireFiniteNumber(piece, "Qz", "architectural_import_drift"),
                RequireFiniteNumber(piece, "Qw", "architectural_import_drift"),
            };
            var norm = Math.Sqrt(rotation.Sum(value => value * value));
            rotation = norm < 0.000001 ? [0, 0, 0, 1] :
                rotation.Select(value => Round(value / norm, 6)).ToArray();
            if (rotation[3] < 0 || rotation[3] == 0 &&
                (rotation[2] < 0 || rotation[2] == 0 &&
                    (rotation[1] < 0 || rotation[1] == 0 && rotation[0] < 0)))
                rotation = rotation.Select(value => Round(-value, 6)).ToArray();
            var category = RequireString(piece, "Category", "architectural_import_drift");
            signatures.Add(CaptureSignature(
                RequireString(piece, "Prefab", "architectural_import_drift"),
                string.IsNullOrEmpty(category) ? "Building" : category, position, rotation,
                RequireBool(piece, "HasSignText", "architectural_import_drift"),
                RequireString(piece, "SignText", "architectural_import_drift"),
                RequireBool(piece, "HasItemStand", "architectural_import_drift"),
                RequireString(piece, "ItemPrefab", "architectural_import_drift"),
                RequireInt(piece, "ItemVariant", "architectural_import_drift"),
                RequireInt(piece, "ItemQuality", "architectural_import_drift"),
                RequireInt(piece, "ItemType", "architectural_import_drift"),
                RequireString(piece, "RuneSchool", "architectural_import_drift"),
                RequireString(piece, "RuneStyle", "architectural_import_drift"),
                RequireString(piece, "TextGlowSchool", "architectural_import_drift")));
        }
        signatures.Sort(StringComparer.Ordinal);
        return ComputePiecesHash(signatures);
    }

    static void VerifyCanonicalArtifacts(StudioBuildDescriptor descriptor,
        string capture, string blueprint, string manifest)
    {
        if (!File.Exists(capture) || !File.Exists(blueprint) || !File.Exists(manifest) ||
            Sha256File(capture) != descriptor.CanonicalCaptureSha256 ||
            Sha256File(blueprint) != descriptor.CanonicalBlueprintSha256 ||
            Sha256File(manifest) != descriptor.CanonicalManifestSha256)
            throw new BuildContractException("canonical_artifact_drift", 409);
        var manifestJson = Parse(File.ReadAllBytes(manifest));
        RequireString(manifestJson, "source_pieces_sha256", "canonical_manifest_invalid",
            descriptor.CanonicalPiecesSha256);
        var artifacts = RequireObject(manifestJson, "artifacts", "canonical_manifest_invalid");
        foreach (var (name, path, hash) in new[]
                 {
                     (Path.GetFileName(capture), capture, descriptor.CanonicalCaptureSha256),
                     (Path.GetFileName(blueprint), blueprint, descriptor.CanonicalBlueprintSha256),
                 })
        {
            if (!artifacts.TryGetProperty(name, out var pin) ||
                RequireSha(pin, "sha256", "canonical_manifest_invalid") != hash ||
                RequireInt64(pin, "bytes", "canonical_manifest_invalid") !=
                    new FileInfo(path).Length)
                throw new BuildContractException("canonical_manifest_invalid", 409);
        }
    }

    StudioBuildDocument? ReadBuild(string buildId)
    {
        try
        {
            var path = BuildPath(buildId);
            var descriptor = ReadJson<StudioBuildDescriptor>(Path.Combine(path, "descriptor.json"));
            var placement = ReadJson<StudioBuildPlacement>(Path.Combine(path, "placement.json"));
            if (descriptor is null || placement is null || descriptor.BuildId != buildId ||
                descriptor.Schema != DescriptorSchema) return null;
            var latest = ReadJson<StudioBuildStageReceipt>(Path.Combine(path, "latest-stage.json"));
            var staged = latest is not null && latest.BuildId == buildId &&
                latest.Placement.Revision == placement.Revision;
            return new(BuildSchema, buildId, descriptor.CapsuleSha256, descriptor.FixtureId,
                descriptor.SourceRevision, placement.Revision, placement, staged, false,
                descriptor.DerivedName, descriptor.PieceCount, descriptor.PrefabCounts,
                descriptor.ReconciliationIds, descriptor.GraphSha256,
                descriptor.ConstraintModelSha256, descriptor.InterpretationReceiptSha256,
                descriptor.CompilationReceiptSha256, descriptor.CompiledPiecesSha256,
                descriptor.CandidateCaptureSha256, descriptor.CandidatePiecesSha256,
                descriptor.PrefabGeometrySha256, descriptor.CanonicalCaptureSha256,
                descriptor.CanonicalBlueprintSha256, descriptor.CanonicalPiecesSha256,
                descriptor.CanonicalManifestSha256, descriptor.ImporterSha256,
                descriptor.ImporterBundleSha256, descriptor.Source, descriptor.Architecture,
                descriptor.ConstraintModel, descriptor.InterpretationReceipt,
                descriptor.CompilationReceipt, descriptor.Pieces, descriptor.PrefabGeometry,
                latest, true);
        }
        catch { return null; }
    }

    string BuildPath(string buildId) => Path.Combine(_root, buildId);
    static bool SafeBuildId(string value) => value.Length == 64 && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');

    static async Task<byte[]> ReadBoundedAsync(Stream source, long limit, string error,
        CancellationToken cancellationToken)
    {
        using (var output = new MemoryStream())
        {
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                if (output.Length + read > limit)
                    throw new BuildContractException(error,
                        error == "capsule_too_large" ? 413 : 400);
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
    }

    static JsonElement Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64,
        });
        return document.RootElement.Clone();
    }

    T? ReadJson<T>(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), _host.Json) : default;
        }
        catch { return default; }
    }

    void AtomicWriteJson<T>(string path, T value, bool create) =>
        AtomicWrite(path, JsonSerializer.SerializeToUtf8Bytes(value, _host.Json), create);

    static void AtomicWrite(string path, byte[] bytes, bool create)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            WriteThrough(temporary, bytes);
            if (create) File.Move(temporary, path);
            else File.Move(temporary, path, overwrite: true);
        }
        finally { TryDeleteFile(temporary); }
    }

    static void WriteThrough(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    static string DerivedName(string hexDigest)
    {
        var bytes = Convert.FromHexString(hexDigest);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var output = new StringBuilder("tn0304-");
        var buffer = 0;
        var bits = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output.Append(alphabet[(buffer >> bits) & 31]);
            }
            if (bits > 0) buffer &= (1 << bits) - 1;
            else buffer = 0;
        }
        if (bits > 0) output.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    static string CapturePieceSignature(JsonElement piece) => CaptureSignature(
        RequireString(piece, "Prefab", "capsule_contract_inconsistent"),
        RequireString(piece, "Category", "capsule_contract_inconsistent"),
        [RequireFiniteNumber(piece, "X", "capsule_contract_inconsistent"),
            RequireFiniteNumber(piece, "Y", "capsule_contract_inconsistent"),
            RequireFiniteNumber(piece, "Z", "capsule_contract_inconsistent")],
        [RequireFiniteNumber(piece, "Qx", "capsule_contract_inconsistent"),
            RequireFiniteNumber(piece, "Qy", "capsule_contract_inconsistent"),
            RequireFiniteNumber(piece, "Qz", "capsule_contract_inconsistent"),
            RequireFiniteNumber(piece, "Qw", "capsule_contract_inconsistent")],
        RequireBool(piece, "HasSignText", "capsule_contract_inconsistent"),
        RequireString(piece, "SignText", "capsule_contract_inconsistent"),
        RequireBool(piece, "HasItemStand", "capsule_contract_inconsistent"),
        RequireString(piece, "ItemPrefab", "capsule_contract_inconsistent"),
        RequireInt(piece, "ItemVariant", "capsule_contract_inconsistent"),
        RequireInt(piece, "ItemQuality", "capsule_contract_inconsistent"),
        RequireInt(piece, "ItemType", "capsule_contract_inconsistent"),
        RequireString(piece, "RuneSchool", "capsule_contract_inconsistent"),
        RequireString(piece, "RuneStyle", "capsule_contract_inconsistent"),
        RequireString(piece, "TextGlowSchool", "capsule_contract_inconsistent"));

    static string CaptureSignature(string prefab, string category,
        IReadOnlyList<double> position, IReadOnlyList<double> rotation,
        bool hasSign = false, string signText = "", bool hasItemStand = false,
        string itemPrefab = "", int itemVariant = 0, int itemQuality = 0,
        int itemType = 0, string runeSchool = "", string runeStyle = "",
        string textGlowSchool = "") => string.Join('\t', prefab, category,
        Number(position[0], 4), Number(position[1], 4), Number(position[2], 4),
        Number(rotation[0], 6), Number(rotation[1], 6), Number(rotation[2], 6),
        Number(rotation[3], 6), hasSign ? "1" : "0", Escape(signText),
        hasItemStand ? "1" : "0", itemPrefab,
        itemVariant.ToString(CultureInfo.InvariantCulture),
        itemQuality.ToString(CultureInfo.InvariantCulture),
        itemType.ToString(CultureInfo.InvariantCulture), runeSchool, runeStyle,
        textGlowSchool);

    static string ComputePiecesHash(IEnumerable<string> signatures) =>
        Sha256(Encoding.UTF8.GetBytes(string.Join('\n', signatures)));

    static string Number(double value, int digits)
    {
        value = Round(value, digits);
        if (value == 0) return "0";
        return value.ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);
    }

    static double Round(double value, int digits)
    {
        var rounded = Math.Round(value, digits, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }

    static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    static JsonElement RequireObject(JsonElement value, string name, string error) =>
        RequireProperty(value, name, JsonValueKind.Object, error);
    static JsonElement RequireArray(JsonElement value, string name, string error) =>
        RequireProperty(value, name, JsonValueKind.Array, error);

    static JsonElement RequireProperty(JsonElement value, string name,
        JsonValueKind kind, string error)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(name, out var property) || property.ValueKind != kind)
            throw new BuildContractException(error);
        return property;
    }

    static string RequireString(JsonElement value, string name, string error,
        string? exact = null)
    {
        var text = RequireProperty(value, name, JsonValueKind.String, error).GetString()!;
        if (exact is not null && text != exact) throw new BuildContractException(error);
        return text;
    }

    static string RequireBoundedString(JsonElement value, string name, int maximum,
        string error)
    {
        var text = RequireString(value, name, error);
        if (text.Length is < 1 || text.Length > maximum ||
            text.Any(character => char.IsControl(character)))
            throw new BuildContractException(error);
        return text;
    }

    static string RequireSha(JsonElement value, string name, string error)
    {
        var hash = RequireString(value, name, error);
        if (hash.Length != 64 || hash.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new BuildContractException(error);
        return hash;
    }

    static int RequireInt(JsonElement value, string name, string error)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var result))
            throw new BuildContractException(error);
        return result;
    }

    static long RequireInt64(JsonElement value, string name, string error)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var result) || result < 0)
            throw new BuildContractException(error);
        return result;
    }

    static bool RequireBool(JsonElement value, string name, string error)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(name, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new BuildContractException(error);
        return property.GetBoolean();
    }

    static double RequireFiniteNumber(JsonElement value, string name, string error)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new BuildContractException(error);
        return result;
    }

    static double[] NumberArray(JsonElement value, string name, int count, string error)
    {
        var array = RequireArray(value, name, error);
        if (array.GetArrayLength() != count) throw new BuildContractException(error);
        var result = new double[count];
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number ||
                !item.TryGetDouble(out var number) || !double.IsFinite(number))
                throw new BuildContractException(error);
            result[index++] = number;
        }
        return result;
    }

    static Dictionary<string, int> ReadIntMap(JsonElement value, string name,
        string error)
    {
        var map = RequireObject(value, name, error);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in map.EnumerateObject())
        {
            if (!property.Value.TryGetInt32(out var count) || count < 1 ||
                !result.TryAdd(property.Name, count))
                throw new BuildContractException(error);
        }
        return result;
    }

    static bool MapsEqual(IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right) => left.Count == right.Count &&
        left.All(item => right.TryGetValue(item.Key, out var value) && value == item.Value);

    static HashSet<string> ReadIdSet(JsonElement value, string name)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in RequireArray(value, name,
                     "capsule_contract_inconsistent").EnumerateArray())
            if (!result.Add(RequireString(item, "id", "capsule_contract_inconsistent")))
                throw new BuildContractException("capsule_contract_inconsistent");
        return result;
    }

    static HashSet<string> ReadStringSet(JsonElement value, string name)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in RequireArray(value, name,
                     "capsule_contract_inconsistent").EnumerateArray())
            if (item.ValueKind != JsonValueKind.String || !result.Add(item.GetString()!))
                throw new BuildContractException("capsule_contract_inconsistent");
        return result;
    }

    static void RequireSchema(JsonElement value, string property, string schema,
        string error) => RequireString(value, property, error, schema);
    static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    static string Sha256File(string path) => Sha256(File.ReadAllBytes(path));
    static bool BytesEqual(string path, byte[] expected) => File.Exists(path) &&
        File.ReadAllBytes(path).AsSpan().SequenceEqual(expected);
    static string Bound(string value) => value.Length <= MaxImporterOutputCharacters
        ? value : value[^MaxImporterOutputCharacters..];
    static void TryDeleteFile(string path)
    { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    static void TryDeleteDirectory(string path)
    { try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { } }
}

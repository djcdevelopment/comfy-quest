using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comfy.Quest.Studio;

/// <summary>Released ingress contract. IDs address explicit authority, never paths or URLs.</summary>
public sealed record StudioCreatorOperationRequest(
    string Schema, string CommandId, string Operation, string ProjectId, int ExpectedRevision,
    string ExpectedMachine, string ExpectedWorldUid, string CreatorSessionId,
    string? TargetId = null, string? CastId = null, string? RunId = null,
    string? PreviewToken = null, bool Confirm = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? GuildId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CampaignId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ExpectedCampaignRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AttemptId = null);

public sealed record StudioCreatorOperationOutcome(
    bool Ok, string? Error, string? RuntimeState = null, string? CastId = null,
    string? RunId = null, string? RequestId = null, JsonElement? Receipt = null,
    bool Pending = false, bool RecoveryRequired = false);

public sealed class StudioCreatorOperationRecord
{
    public string Schema { get; set; } = "comfy-quest-creator-operation/v1";
    public string OperationId { get; set; } = string.Empty;
    public string RequestSha256 { get; set; } = string.Empty;
    public StudioCreatorOperationRequest Request { get; set; } = null!;
    public string State { get; set; } = "queued";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public StudioCreatorOperationOutcome? Outcome { get; set; }
}

public sealed record StudioCreatorOperationAcceptance(
    bool Ok, string? Error, bool Replayed, StudioCreatorOperationRecord? Operation,
    int StatusCode);

/// <summary>
/// One bounded install-local command journal. A browser disconnect never cancels a
/// dispatched operation. Unknown outcomes are retained and block new mutations;
/// neither a retry nor a host restart silently re-executes a side effect.
/// </summary>
internal sealed class CreatorOperationJournal
{
    internal const int MaxOperations = 256;
    internal const int MaxBytes = 4 * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
    internal static readonly string[] Operations =
        ["quick_cast", "undo_cast", "activate", "reset_preview", "reset",
         "campaign_play", "campaign_reset_preview", "campaign_reset"];
    readonly object _gate = new();
    readonly string _path;
    readonly Dictionary<string, StudioCreatorOperationRecord> _records;
    Task _work = Task.CompletedTask;

    internal CreatorOperationJournal(string stateRoot)
    {
        _path = Path.Combine(stateRoot, "quest-studio", "creator", "operations.json");
        _records = Read();
        var changed = false;
        foreach (var value in _records.Values.Where(value => value.State is "queued" or "running"))
        {
            value.State = "recovery_required";
            value.Outcome = new(false, "creator_operation_interrupted");
            value.UpdatedUtc = DateTimeOffset.UtcNow;
            changed = true;
        }
        if (changed) Store();
    }

    internal StudioCreatorOperationAcceptance Submit(StudioCreatorOperationRequest? request,
        Func<StudioCreatorOperationRequest, string?> validate,
        Func<StudioCreatorOperationRequest, Task<StudioCreatorOperationOutcome>> execute)
    {
        var error = Validate(request);
        if (error is not null) return new(false, error, false, null, 400);
        lock (_gate)
        {
            var digest = Hash(request!);
            if (_records.TryGetValue(request!.CommandId, out var previous))
                return previous.RequestSha256 == digest
                    ? new(true, null, true, Clone(previous), 200)
                    : new(false, "creator_command_id_conflict", false, null, 409);
            if (_records.Values.Any(value => value.State is
                    "queued" or "running" or "awaiting_runtime" or "recovery_required"))
                return new(false, "creator_operation_busy", false, null, 409);
            if (_records.Count >= MaxOperations)
                return new(false, "creator_operation_limit", false, null, 409);
            error = validate(request);
            if (error is not null) return new(false, error, false, null, 409);
            var now = DateTimeOffset.UtcNow;
            var record = new StudioCreatorOperationRecord
            {
                OperationId = request.CommandId, RequestSha256 = digest,
                Request = request, CreatedUtc = now, UpdatedUtc = now,
            };
            _records.Add(record.OperationId, record);
            try { Store(); }
            catch { _records.Remove(record.OperationId); throw; }
            var accepted = Clone(record);
            _work = Task.Run(async () =>
            {
                try
                {
                    lock (_gate)
                    {
                        record.State = "running";
                        record.UpdatedUtc = DateTimeOffset.UtcNow;
                        Store();
                    }
                    // Revalidate after enqueueing: the draft/world may have changed.
                    var stale = validate(request);
                    var outcome = stale is null ? await execute(request) : new(false, stale);
                    lock (_gate)
                    {
                        record.Outcome = outcome;
                        record.State = outcome.RecoveryRequired ? "recovery_required"
                            : outcome.Pending ? "awaiting_runtime"
                            : outcome.Ok ? "completed" : "failed";
                        record.UpdatedUtc = DateTimeOffset.UtcNow;
                        Store();
                    }
                }
                catch
                {
                    lock (_gate)
                    {
                        record.State = "recovery_required";
                        record.Outcome = new(false, "creator_operation_outcome_unknown");
                        record.UpdatedUtc = DateTimeOffset.UtcNow;
                        Store();
                    }
                }
            });
            return new(true, null, false, accepted, 202);
        }
    }

    internal StudioCreatorOperationRecord? Get(string id)
    {
        if (!Safe(id)) return null;
        lock (_gate) return _records.TryGetValue(id, out var record) ? Clone(record) : null;
    }

    internal IReadOnlyList<StudioCreatorOperationRecord> ForProject(string projectId)
    {
        lock (_gate) return _records.Values.Where(value => value.Request.ProjectId == projectId)
            .OrderBy(value => value.CreatedUtc).ThenBy(value => value.OperationId, StringComparer.Ordinal)
            .Select(Clone).ToArray();
    }

    internal void CompletePending(string id, StudioCreatorOperationOutcome outcome)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(id, out var record) || record.State != "awaiting_runtime"
                || outcome.Pending) return;
            record.Outcome = outcome;
            record.State = outcome.Ok ? "completed" : "failed";
            record.UpdatedUtc = DateTimeOffset.UtcNow;
            Store();
        }
    }

    internal Task DrainAsync() { lock (_gate) return _work; }

    internal static string? Validate(StudioCreatorOperationRequest? value)
    {
        if (value is null || value.Schema != "comfy-quest-creator-operation-request/v1"
            || !Safe(value.CommandId) || !Safe(value.ProjectId)
            || !Operations.Contains(value.Operation) || value.ExpectedRevision < 0
            || !Safe(value.ExpectedMachine) || !Safe(value.CreatorSessionId)
            || !long.TryParse(value.ExpectedWorldUid, out var world) || world == 0)
            return "creator_operation_request_invalid";
        if (value.Operation is "quick_cast" or "activate" && !Safe(value.TargetId))
            return "creator_target_required";
        if (value.Operation == "undo_cast" && !Safe(value.CastId)) return "creator_cast_required";
        if (value.Operation is "reset_preview" or "reset" && !Safe(value.RunId))
            return "creator_run_required";
        if (value.Operation == "reset" && (!value.Confirm || !Safe(value.PreviewToken)))
            return "reset_confirmation_required";
        if (value.Operation.StartsWith("campaign_", StringComparison.Ordinal)
            && (!Safe(value.GuildId) || !Safe(value.CampaignId) || value.ExpectedCampaignRevision is null or < 0))
            return "creator_campaign_identity_required";
        if (value.Operation is "campaign_reset_preview" or "campaign_reset" && !Safe(value.AttemptId))
            return "creator_campaign_attempt_required";
        if (value.Operation == "campaign_reset" && (!value.Confirm || !Safe(value.PreviewToken)))
            return "campaign_reset_confirmation_required";
        return null;
    }

    internal static bool Safe(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 96 && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');

    static string Hash(StudioCreatorOperationRequest value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));

    static StudioCreatorOperationRecord Clone(StudioCreatorOperationRecord record) =>
        JsonSerializer.Deserialize<StudioCreatorOperationRecord>(JsonSerializer.Serialize(record, Json), Json)!;

    Dictionary<string, StudioCreatorOperationRecord> Read()
    {
        if (!File.Exists(_path)) return new(StringComparer.Ordinal);
        var info = new FileInfo(_path);
        if (info.Length is <= 0 or > MaxBytes) throw new InvalidDataException("creator_journal_size_invalid");
        var records = JsonSerializer.Deserialize<List<StudioCreatorOperationRecord>>(File.ReadAllText(_path), Json)
            ?? throw new InvalidDataException("creator_journal_invalid");
        if (records.Count > MaxOperations || records.Any(value =>
                value.Schema != "comfy-quest-creator-operation/v1" || Validate(value.Request) is not null
                || value.OperationId != value.Request.CommandId || value.RequestSha256 != Hash(value.Request)
                || value.State is not ("queued" or "running" or "awaiting_runtime" or "completed"
                    or "failed" or "recovery_required"))
            || records.Select(value => value.OperationId).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("creator_journal_invalid");
        return records.ToDictionary(value => value.OperationId, StringComparer.Ordinal);
    }

    void Store()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_records.Values, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("creator_journal_size_invalid");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

namespace ComfyQuestContracts;

using System;
using System.Globalization;
using Newtonsoft.Json;

/// <summary>One expiring, identity-pinned control request for the Runtime creator loop.
/// The vocabulary intentionally stops at status/arm/disarm: it cannot load a pack,
/// cast a Charm, inject a key, run a command, or name a path.</summary>
public sealed class RuntimeCreatorRequest {
  public const string CurrentSchema = "comfy-quest-runtime-request/v1";

  [JsonProperty("schema")] public string Schema { get; set; }
  [JsonProperty("request_id")] public string RequestId { get; set; }
  [JsonProperty("operation")] public string Operation { get; set; }
  [JsonProperty("created_utc")] public string CreatedUtc { get; set; }
  [JsonProperty("expires_utc")] public string ExpiresUtc { get; set; }
  [JsonProperty("expected_machine")] public string ExpectedMachine { get; set; }
  [JsonProperty("expected_world_uid")] public string ExpectedWorldUid { get; set; }
  [JsonProperty("creator_session_id")] public string CreatorSessionId { get; set; }
}

public static class RuntimeCreatorRequestPolicy {
  public static readonly string[] Operations = { "status", "arm", "disarm" };

  public static bool Validate(
      RuntimeCreatorRequest request, DateTimeOffset now, out string error) {
    if (request == null || request.Schema != RuntimeCreatorRequest.CurrentSchema) {
      error = "request_schema_invalid";
      return false;
    }
    if (!SafeToken(request.RequestId, 80)
        || !SafeToken(request.ExpectedMachine, 80)
        || !SafeToken(request.CreatorSessionId, 80)
        || !long.TryParse(request.ExpectedWorldUid, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out long worldUid)
        || worldUid == 0L) {
      error = "request_identity_invalid";
      return false;
    }
    string operation = (request.Operation ?? string.Empty).Trim().ToLowerInvariant();
    if (Array.IndexOf(Operations, operation) < 0) {
      error = "operation_not_allowlisted";
      return false;
    }
    if (!DateTimeOffset.TryParse(request.CreatedUtc ?? string.Empty,
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
            out DateTimeOffset created)
        || !DateTimeOffset.TryParse(request.ExpiresUtc ?? string.Empty,
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
            out DateTimeOffset expires)
        || created < now.AddMinutes(-30)
        || created > now.AddMinutes(1)
        || expires <= created) {
      error = "request_time_invalid";
      return false;
    }
    if (expires <= now || expires > now.AddMinutes(30)) {
      error = expires <= now ? "request_expired" : "request_expiry_too_far";
      return false;
    }
    error = null;
    return true;
  }

  static bool SafeToken(string value, int maxLength) {
    if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength) return false;
    foreach (char c in value) {
      if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.') return false;
    }
    return true;
  }
}

public sealed class RuntimeCreatorRequestReceipt {
  [JsonProperty("schema")] public string Schema { get; set; }
      = "comfy-quest-runtime-request-receipt/v1";
  [JsonProperty("request_id")] public string RequestId { get; set; }
  [JsonProperty("operation")] public string Operation { get; set; }
  [JsonProperty("state")] public string State { get; set; }
  [JsonProperty("detail")] public string Detail { get; set; }
  [JsonProperty("machine")] public string Machine { get; set; }
  [JsonProperty("world_uid")] public string WorldUid { get; set; }
  [JsonProperty("creator_session_id")] public string CreatorSessionId { get; set; }
  [JsonProperty("completed_utc")] public DateTimeOffset CompletedUtc { get; set; }
  [JsonProperty("dev_session_id", NullValueHandling=NullValueHandling.Ignore)]
  public string DevSessionId { get; set; }
  [JsonProperty("dev_armed")] public bool DevArmed { get; set; }
  [JsonProperty("dev_state", NullValueHandling=NullValueHandling.Ignore)]
  public string DevState { get; set; }
  [JsonProperty("active_pack_id", NullValueHandling=NullValueHandling.Ignore)]
  public string ActivePackId { get; set; }
  [JsonProperty("active_version", NullValueHandling=NullValueHandling.Ignore)]
  public string ActiveVersion { get; set; }
  [JsonProperty("active_content_hash", NullValueHandling=NullValueHandling.Ignore)]
  public string ActiveContentHash { get; set; }
  [JsonProperty("active_activation_id", NullValueHandling=NullValueHandling.Ignore)]
  public string ActiveActivationId { get; set; }
  [JsonProperty("current_stage_id", NullValueHandling=NullValueHandling.Ignore)]
  public string CurrentStageId { get; set; }
}

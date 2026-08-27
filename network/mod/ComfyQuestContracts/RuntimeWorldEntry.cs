namespace ComfyQuestContracts;

using System;
using System.Globalization;
using Newtonsoft.Json;

/// <summary>
/// One expiring request to place a Creator Session in an exact local world through
/// Valheim's own profile/world APIs. The contract deliberately has no server, port,
/// launch-argument, console, input, or arbitrary-path field.
/// </summary>
public sealed class RuntimeWorldEntryRequest {
  public const string CurrentSchema = "comfy-quest-world-entry-request/v1";

  [JsonProperty("schema")] public string Schema { get; set; }
  [JsonProperty("request_id")] public string RequestId { get; set; }
  [JsonProperty("created_utc")] public string CreatedUtc { get; set; }
  [JsonProperty("expires_utc")] public string ExpiresUtc { get; set; }
  [JsonProperty("expected_machine")] public string ExpectedMachine { get; set; }
  [JsonProperty("expected_world_uid")] public string ExpectedWorldUid { get; set; }
  [JsonProperty("world_name")] public string WorldName { get; set; }
  [JsonProperty("world_display_name")] public string WorldDisplayName { get; set; }
  [JsonProperty("character_profile")] public string CharacterProfile { get; set; }
  [JsonProperty("creator_session_id")] public string CreatorSessionId { get; set; }
}

public static class RuntimeWorldEntryRequestPolicy {
  public static bool Validate(
      RuntimeWorldEntryRequest request, DateTimeOffset now, out string error) {
    if (request == null || request.Schema != RuntimeWorldEntryRequest.CurrentSchema) {
      error = "world_entry_schema_invalid";
      return false;
    }
    if (!SafeToken(request.RequestId, 80)
        || !SafeToken(request.ExpectedMachine, 80)
        || !SafeToken(request.CharacterProfile, 80)
        || !SafeToken(request.CreatorSessionId, 80)
        || !SafeWorldName(request.WorldName)
        || !SafeWorldName(request.WorldDisplayName)
        || !long.TryParse(request.ExpectedWorldUid, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out long worldUid)
        || worldUid == 0L) {
      error = "world_entry_identity_invalid";
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
      error = "world_entry_time_invalid";
      return false;
    }
    if (expires <= now || expires > now.AddMinutes(30)) {
      error = expires <= now
          ? "world_entry_request_expired"
          : "world_entry_expiry_too_far";
      return false;
    }
    error = null;
    return true;
  }

  public static bool CanAddressReceipt(RuntimeWorldEntryRequest request) =>
      request != null && SafeToken(request.RequestId, 80)
      && SafeToken(request.CreatorSessionId, 80);

  static bool SafeToken(string value, int maxLength) {
    if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength) return false;
    foreach (char c in value)
      if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.') return false;
    return true;
  }

  static bool SafeWorldName(string value) {
    if (string.IsNullOrWhiteSpace(value) || value.Length > 80) return false;
    foreach (char c in value)
      if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.' && c != ' ')
        return false;
    return true;
  }
}

public sealed class RuntimeWorldEntryReceipt {
  [JsonProperty("schema")] public string Schema { get; set; }
      = "comfy-quest-world-entry-receipt/v1";
  [JsonProperty("request_id")] public string RequestId { get; set; }
  [JsonProperty("creator_session_id")] public string CreatorSessionId { get; set; }
  [JsonProperty("state")] public string State { get; set; }
  [JsonProperty("detail")] public string Detail { get; set; }
  [JsonProperty("machine")] public string Machine { get; set; }
  [JsonProperty("expected_world_uid")] public string ExpectedWorldUid { get; set; }
  [JsonProperty("world_uid", NullValueHandling=NullValueHandling.Ignore)]
  public string WorldUid { get; set; }
  [JsonProperty("world_name")] public string WorldName { get; set; }
  [JsonProperty("world_display_name")] public string WorldDisplayName { get; set; }
  [JsonProperty("character_profile")] public string CharacterProfile { get; set; }
  [JsonProperty("character_name", NullValueHandling=NullValueHandling.Ignore)]
  public string CharacterName { get; set; }
  [JsonProperty("completed_utc")] public DateTimeOffset CompletedUtc { get; set; }
}

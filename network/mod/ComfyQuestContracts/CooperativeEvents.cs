namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;

/// <summary>Privacy-minimal event vocabulary for listen-host cooperative quests.</summary>
public static class CooperativeEventContract {
  public const string ChatReceivedEvent = "chat_received";
  public const string PeerRole = "peer";
  public const string ListenHostRole = "listen_host";

  public static bool TryCreateInboundChat(
      long senderId,
      long localCharacterUserId,
      string chatMode,
      string message,
      DateTimeOffset at,
      out RuntimeEvent runtimeEvent) {
    runtimeEvent = null;
    if (senderId == 0L || localCharacterUserId == 0L || string.IsNullOrWhiteSpace(message)) {
      return false;
    }
    string mode = Normalize(chatMode);
    if (string.IsNullOrWhiteSpace(mode)) {
      return false;
    }
    runtimeEvent = new RuntimeEvent {
      Name = ChatReceivedEvent,
      Target = mode,
      At = at,
      Fields = new Dictionary<string, string> {
        ["actor_role"] = senderId == localCharacterUserId ? ListenHostRole : PeerRole,
      },
    };
    return true;
  }

  public static RuntimeEvent CreateHostPlacement(string prefab, DateTimeOffset at) => new() {
    Name = "piece_placed",
    Target = Normalize(prefab),
    At = at,
    Fields = new Dictionary<string, string> { ["actor_role"] = ListenHostRole },
  };

  public static string ActorRole(RuntimeEvent runtimeEvent) {
    return runtimeEvent?.Fields != null
        && runtimeEvent.Fields.TryGetValue("actor_role", out string role)
      ? role
      : null;
  }

  static string Normalize(string value) {
    string normalized = (value ?? string.Empty).Trim();
    if (normalized.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - "(Clone)".Length).Trim();
    }
    return normalized.ToLowerInvariant();
  }
}

/// <summary>Privacy-minimal, one-player signals used by the fast Quest Studio loop.</summary>
public static class EasyEventContract {
  public const string ChatSentEvent = "chat_sent";
  public const string ItemDroppedEvent = "item_dropped";
  public const string ItemPickedUpEvent = "item_picked_up";
  public const string ItemEquippedEvent = "item_equipped";
  public const string ItemConsumedEvent = "item_consumed";
  public const string CharacterHealedEvent = "character_healed";

  public static RuntimeEvent ChatSent(string chatMode, string message, DateTimeOffset at) {
    if (string.IsNullOrWhiteSpace(message)) return null;
    var mode = NormalizeChatMode(chatMode);
    if (mode == null) return null;
    return new RuntimeEvent { Name = ChatSentEvent, Target = mode, At = at };
  }

  public static RuntimeEvent ItemDropped(string prefab, int quantity, DateTimeOffset at) {
    if (quantity <= 0) return null;
    return Inventory(ItemDroppedEvent, prefab, at);
  }

  public static RuntimeEvent ItemPickedUp(string prefab, DateTimeOffset at) =>
    Inventory(ItemPickedUpEvent, prefab, at);

  public static RuntimeEvent ItemEquipped(string prefab, DateTimeOffset at) =>
    Inventory(ItemEquippedEvent, prefab, at);

  public static RuntimeEvent ItemConsumed(string prefab, DateTimeOffset at) =>
    Inventory(ItemConsumedEvent, prefab, at);

  public static RuntimeEvent CharacterHealed(float amount, DateTimeOffset at) => amount > 0f
    ? new RuntimeEvent { Name = CharacterHealedEvent, Target = "you", At = at }
    : null;

  static RuntimeEvent Inventory(string eventName, string prefab, DateTimeOffset at) {
    var target = NormalizePrefab(prefab);
    return target == null ? null : new RuntimeEvent { Name = eventName, Target = target, At = at };
  }

  static string NormalizeChatMode(string value) {
    var mode = (value ?? string.Empty).Trim().ToLowerInvariant();
    return mode == "shout" ? "shout" : mode == "normal" ? "normal" : null;
  }

  static string NormalizePrefab(string value) {
    var result = (value ?? string.Empty).Trim();
    if (result.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
      result = result.Substring(0, result.Length - "(Clone)".Length).Trim();
    return result.Length == 0 ? null : result;
  }
}

/// <summary>
/// Coalesces the nested Shout callbacks without persisting sender IDs, names, or message text.
/// A repeated witness is a new message; its alternate witness joins the same short-lived action.
/// </summary>
public sealed class CooperativeChatDedupe {
  sealed class State {
    public double At;
    public readonly HashSet<string> Witnesses = new(StringComparer.Ordinal);
  }

  readonly double windowSeconds;
  readonly Dictionary<string, State> states = new(StringComparer.Ordinal);

  public CooperativeChatDedupe(double correlationSeconds = 0.75) {
    windowSeconds = Math.Max(0.01, correlationSeconds);
  }

  public bool ShouldEmit(
      long senderId,
      string chatMode,
      string message,
      string witness,
      double now) {
    // GetHashCode is deliberately process-local: this key is never serialized or receipted.
    string key = senderId + "\n" + (chatMode ?? string.Empty) + "\n"
        + (message ?? string.Empty).GetHashCode();
    string route = witness ?? string.Empty;
    if (states.TryGetValue(key, out State state)
        && now >= state.At
        && now - state.At <= windowSeconds
        && !state.Witnesses.Contains(route)) {
      state.At = now;
      state.Witnesses.Add(route);
      return false;
    }
    if (states.Count >= 256) {
      states.Clear();
    }
    var next = new State { At = now };
    next.Witnesses.Add(route);
    states[key] = next;
    return true;
  }

  public void Clear() => states.Clear();
}

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

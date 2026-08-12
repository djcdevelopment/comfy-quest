namespace ComfyQuestLab;

using HarmonyLib;
using UnityEngine;

/// <summary>World seams: moving through the world, and the world changing state.
///
/// `ZoneSystem.SetGlobalKey` is the sleeper here. Global keys are how Valheim remembers
/// that a boss is dead and a biome's rules have changed — `defeated_eikthyr` and the
/// rest. It is the closest thing the game has to a server-wide progression event, which
/// makes it the natural seam for a guild-scale quest, and nothing has ever hooked it.
///
/// Three overloads and the RPC witness are patched. The action correlator coalesces an
/// overload cascade into one creator event while retaining exact provenance in verbose
/// diagnostics.</summary>
public static class WorldPatches {
  public static void Apply(Harmony harmony) {
    LabPatching.TryPatch(harmony, typeof(Player), "TeleportTo",
        new[] { typeof(Vector3), typeof(Quaternion), typeof(bool) },
        nameof(TeleportToPostfix), "Player.TeleportTo");
    LabPatching.TryPatch(harmony, typeof(ZoneSystem), "SetGlobalKey", new[] { typeof(string) },
        nameof(SetGlobalKeyPostfix), "ZoneSystem.SetGlobalKey");
    LabPatching.TryPatch(harmony, typeof(ZoneSystem), "SetGlobalKey",
        new[] { typeof(GlobalKeys) },
        nameof(SetGlobalKeyEnumPostfix), "ZoneSystem.SetGlobalKey");
    LabPatching.TryPatch(harmony, typeof(ZoneSystem), "SetGlobalKey",
        new[] { typeof(GlobalKeys), typeof(float) },
        nameof(SetGlobalKeyEnumTimedPostfix), "ZoneSystem.SetGlobalKey");
    LabPatching.TryPatch(harmony, typeof(ZoneSystem), "RPC_SetGlobalKey",
        new[] { typeof(long), typeof(string) },
        nameof(RpcSetGlobalKeyPostfix), "ZoneSystem.RPC_SetGlobalKey");
    LabPatching.TryPatch(harmony, typeof(ZoneSystem), "RemoveGlobalKey",
        new[] { typeof(string) },
        nameof(RemoveGlobalKeyPostfix), "ZoneSystem.RemoveGlobalKey");
    LabPatching.TryPatch(harmony, typeof(ZoneSystem), "RemoveGlobalKey",
        new[] { typeof(GlobalKeys) },
        nameof(RemoveGlobalKeyEnumPostfix), "ZoneSystem.RemoveGlobalKey");
  }

  static void TeleportToPostfix(Player __instance, bool __result) {
    if (!__result) {
      return;   // refused, e.g. carrying something a portal will not take
    }
    LabObserve.LocalPlayer(
        "Player.TeleportTo(Vector3, Quaternion, bool)", __instance, "portal", "teleported");
  }

  /// <summary>Not player-filtered: a global key is world state, and it is interesting
  /// precisely because someone else may have caused it.</summary>
  static void SetGlobalKeyPostfix(string __0) {
    GlobalKey("ZoneSystem.SetGlobalKey(string)", __0, "world flag set");
  }

  static void SetGlobalKeyEnumPostfix(GlobalKeys __0) {
    GlobalKey("ZoneSystem.SetGlobalKey(GlobalKeys)", __0.ToString(), "world flag set");
  }

  static void SetGlobalKeyEnumTimedPostfix(GlobalKeys __0, float __1) {
    GlobalKey(
        "ZoneSystem.SetGlobalKey(GlobalKeys, float)", __0.ToString(),
        "world flag set for " + __1.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
  }

  static void RpcSetGlobalKeyPostfix(long __0, string __1) {
    GlobalKey("ZoneSystem.RPC_SetGlobalKey(long, string)", __1, "world flag RPC set");
  }

  static void RemoveGlobalKeyPostfix(string __0) {
    GlobalKey("ZoneSystem.RemoveGlobalKey(string)", __0, "world flag removed");
  }

  static void RemoveGlobalKeyEnumPostfix(GlobalKeys __0) {
    GlobalKey(
        "ZoneSystem.RemoveGlobalKey(GlobalKeys)", __0.ToString(), "world flag removed");
  }

  static void GlobalKey(string signatureId, string key, string detail) {
    if (string.IsNullOrWhiteSpace(key)) {
      return;
    }
    LabObserve.Seam(signatureId, key, detail, fingerprint: key);
  }
}

/// <summary>Social seams: chat, signs, and NPC-style speech.
///
/// The quiet one with real quest potential. A quest that completes when a player writes
/// a particular sign, or says a phrase at a location, needs nothing from the combat
/// system at all — and community rituals tend to look far more like that than like
/// killing things.
///
/// `Chat.SendText` is what the local player typed. `Talker.Say` is the broadcast that
/// results, including from other players, so hooking both would double-count your own
/// messages; only the local send is taken.</summary>
public static class SocialPatches {
  public static void Apply(Harmony harmony) {
    LabPatching.TryPatch(harmony, typeof(Chat), "SendText",
        new[] { typeof(Talker.Type), typeof(string) },
        nameof(SendTextPostfix), "Chat.SendText");
    LabPatching.TryPatch(harmony, typeof(Sign), "SetText", new[] { typeof(string) },
        nameof(SetTextPostfix), "Sign.SetText");
    LabPatching.TryPatch(harmony, typeof(Chat), "OnNewChatMessage",
        new[] {
          typeof(GameObject), typeof(long), typeof(Vector3), typeof(Talker.Type),
          typeof(UserInfo), typeof(string),
        }, nameof(OnNewChatMessagePostfix),
        "Chat.OnNewChatMessage(GameObject, long, Vector3, Type, UserInfo, string)");
    LabPatching.TryPatch(harmony, typeof(Chat), "RPC_ChatMessage",
        new[] { typeof(long), typeof(Vector3), typeof(int), typeof(UserInfo), typeof(string) },
        nameof(RpcChatMessagePostfix),
        "Chat.RPC_ChatMessage(long, Vector3, int, UserInfo, string)");
  }

  static void SendTextPostfix(Talker.Type __0, string __1) {
    if (string.IsNullOrEmpty(__1)) {
      return;
    }
    LabObserve.Seam(
        "Chat.SendText(Type, string)", __0.ToString(), "message text redacted");
  }

  static void SetTextPostfix(Sign __instance, string __0) {
    LabObserve.Seam(
        "Sign.SetText(string)",
        LabObserve.Clean(__instance == null ? null : __instance.name),
        "sign text redacted",
        __instance);
  }

  static void OnNewChatMessagePostfix(long __1, Talker.Type __3, string __5) {
    ObserveIncoming(__1, __3.ToString(), __5,
        "Chat.OnNewChatMessage(GameObject, long, Vector3, Type, UserInfo, string)");
  }

  static void RpcChatMessagePostfix(long __0, int __2, string __4) {
    ObserveIncoming(__0, ((Talker.Type)__2).ToString(), __4,
        "Chat.RPC_ChatMessage(long, Vector3, int, UserInfo, string)");
  }

  static void ObserveIncoming(long senderId, string mode, string message, string signatureId) {
    if (senderId == 0L || string.IsNullOrWhiteSpace(message)) {
      return;
    }
    string role = "peer";
    try {
      if (ZNet.instance != null
          && senderId == ZNet.instance.LocalPlayerCharacterID.UserID) {
        role = "listen_host";
      }
    } catch {
      return;
    }
    LabEventRouter.Emit(
        signatureId,
        (mode ?? string.Empty).Trim().ToLowerInvariant(),
        "message text redacted · actor " + role,
        senderId.ToString(),
        (mode ?? string.Empty) + "|" + message.GetHashCode(),
        evaluate: false);
  }

  /// <summary>A console row is not a chat log. Long messages are cut so one paragraph
  /// cannot push everything else off the panel.</summary>
  static string Truncate(string text) {
    if (string.IsNullOrEmpty(text)) {
      return "(empty)";
    }
    text = text.Replace("\n", " ").Trim();
    return text.Length <= 48 ? text : text.Substring(0, 47) + "…";
  }
}

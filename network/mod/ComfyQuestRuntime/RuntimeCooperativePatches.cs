namespace ComfyQuestRuntime;

using System;
using ComfyQuestContracts;
using HarmonyLib;
using UnityEngine;

/// <summary>Host-observed, privacy-minimal multiplayer signals for cooperative experiences.</summary>
static class RuntimeCooperativePatches {
  public static void Apply(Harmony harmony) {
    RuntimePatching.TryPatchEngine(harmony, typeof(Chat), "OnNewChatMessage",
        new[] { typeof(GameObject), typeof(long), typeof(Vector3), typeof(Talker.Type),
          typeof(UserInfo), typeof(string) }, typeof(RuntimeCooperativePatches),
        nameof(OnNewChatMessage),
        "Chat.OnNewChatMessage(GameObject, long, Vector3, Type, UserInfo, string)");
    RuntimePatching.TryPatchEngine(harmony, typeof(Chat), "RPC_ChatMessage",
        new[] { typeof(long), typeof(Vector3), typeof(int), typeof(UserInfo), typeof(string) },
        typeof(RuntimeCooperativePatches), nameof(RpcChatMessage),
        "Chat.RPC_ChatMessage(long, Vector3, int, UserInfo, string)");
    RuntimePatching.TryPatch(harmony, typeof(Player), "PlacePiece",
        new[] { typeof(Piece), typeof(Vector3), typeof(Quaternion), typeof(bool) },
        typeof(RuntimeCooperativePatches), nameof(PlacePiece),
        "Player.PlacePiece(Piece, Vector3, Quaternion, bool)");
  }

  static void OnNewChatMessage(long __1, Talker.Type __3, string __5) =>
      EmitChat(__1, __3.ToString(), __5,
          "Chat.OnNewChatMessage(GameObject, long, Vector3, Type, UserInfo, string)");

  static void RpcChatMessage(long __0, int __2, string __4) =>
      EmitChat(__0, ((Talker.Type)__2).ToString(), __4,
          "Chat.RPC_ChatMessage(long, Vector3, int, UserInfo, string)");

  static void PlacePiece(Player __instance, Piece __0) {
    try {
      if (!IsListenHost() || __instance == null || __instance != Player.m_localPlayer
          || __0 == null) return;
      var evt = CooperativeEventContract.CreateHostPlacement(
          __0.name, DateTimeOffset.UtcNow);
      RuntimeEventRouter.Emit("Player.PlacePiece(Piece, Vector3, Quaternion, bool)", evt,
          Identity(__instance), evt.Target);
    } catch { }
  }

  static void EmitChat(long senderId, string mode, string message, string witness) {
    try {
      if (!IsListenHost() || ZNet.instance == null) return;
      long localUserId = ZNet.instance.LocalPlayerCharacterID.UserID;
      if (!CooperativeEventContract.TryCreateInboundChat(
          senderId, localUserId, mode, message, DateTimeOffset.UtcNow,
          out RuntimeEvent evt)) return;
      // IDs and text are used only for bounded in-memory correlation.
      RuntimeEventRouter.EmitEngine(evt, "chat-received", senderId.ToString(),
          mode + "|" + message.GetHashCode(), witness);
    } catch { }
  }

  static bool IsListenHost() =>
      ZNet.instance != null && ZNet.instance.IsServer() && !ZNet.instance.IsDedicated();

  static string Identity(UnityEngine.Object value) =>
      value == null ? null : value.GetType().Name + ":" + value.GetInstanceID();
}

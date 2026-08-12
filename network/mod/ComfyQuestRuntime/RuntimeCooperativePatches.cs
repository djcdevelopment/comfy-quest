namespace ComfyQuestRuntime;

using System;
using ComfyQuestContracts;
using HarmonyLib;
using UnityEngine;

/// <summary>Host-observed, privacy-minimal multiplayer signals for cooperative experiences.</summary>
[HarmonyPatch]
static class RuntimeCooperativePatches {
  public static RuntimeExperienceEngine Engine;
  static readonly CooperativeChatDedupe ChatDedupe = new();

  [HarmonyPatch(
      typeof(Chat), "OnNewChatMessage",
      typeof(GameObject), typeof(long), typeof(Vector3), typeof(Talker.Type),
      typeof(UserInfo), typeof(string))]
  [HarmonyPostfix]
  static void OnNewChatMessage(long __1, Talker.Type __3, string __5) {
    EmitChat(__1, __3.ToString(), __5, "Chat.OnNewChatMessage");
  }

  [HarmonyPatch(
      typeof(Chat), "RPC_ChatMessage",
      typeof(long), typeof(Vector3), typeof(int), typeof(UserInfo), typeof(string))]
  [HarmonyPostfix]
  static void RpcChatMessage(long __0, int __2, string __4) {
    EmitChat(__0, ((Talker.Type)__2).ToString(), __4, "Chat.RPC_ChatMessage");
  }

  [HarmonyPatch(
      typeof(Player), "PlacePiece",
      typeof(Piece), typeof(Vector3), typeof(Quaternion), typeof(bool))]
  [HarmonyPostfix]
  static void PlacePiece(Player __instance, Piece __0) {
    try {
      if (!IsListenHost() || __instance == null || __instance != Player.m_localPlayer || __0 == null) {
        return;
      }
      Engine?.OnEvent(CooperativeEventContract.CreateHostPlacement(
          __0.name, DateTimeOffset.UtcNow));
    } catch {
      // A cooperative observation must never break the underlying Valheim action.
    }
  }

  static void EmitChat(long senderId, string mode, string message, string witness) {
    try {
      if (!IsListenHost() || ZNet.instance == null) {
        return;
      }
      long localUserId = ZNet.instance.LocalPlayerCharacterID.UserID;
      if (!CooperativeEventContract.TryCreateInboundChat(
          senderId, localUserId, mode, message, DateTimeOffset.UtcNow, out RuntimeEvent evt)) {
        return;
      }
      if (!ChatDedupe.ShouldEmit(
          senderId, mode, message, witness, Time.realtimeSinceStartup)) {
        return;
      }
      Engine?.OnEvent(evt);
    } catch {
      // Chat privacy and game stability both fail closed.
    }
  }

  static bool IsListenHost() {
    return ZNet.instance != null && ZNet.instance.IsServer() && !ZNet.instance.IsDedicated();
  }
}

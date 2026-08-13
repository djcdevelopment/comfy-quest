namespace ComfyQuestRuntime;

using System;
using ComfyQuestContracts;
using HarmonyLib;

/// <summary>Small, local-player signals for quick solo Quest Studio laps.</summary>
[HarmonyPatch]
static class RuntimeEasyEventPatches {

  [HarmonyPatch(typeof(Chat), "SendText", typeof(Talker.Type), typeof(string))]
  [HarmonyPostfix]
  static void ChatSent(Talker.Type __0, string __1) => Emit(EasyEventContract.ChatSent(
      __0.ToString(), __1, DateTimeOffset.UtcNow));

  [HarmonyPatch(typeof(Humanoid), "DropItem", typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int))]
  [HarmonyPostfix]
  static void ItemDropped(Humanoid __instance, ItemDrop.ItemData __1, int __2, bool __result) {
    if (!__result || __instance == null || __instance != Player.m_localPlayer) return;
    var prefab = __1?.m_dropPrefab == null ? null : __1.m_dropPrefab.name;
    Emit(EasyEventContract.ItemDropped(prefab, __2, DateTimeOffset.UtcNow));
  }

  [HarmonyPatch(typeof(Character), "Heal", typeof(float), typeof(bool))]
  [HarmonyPostfix]
  static void CharacterHealed(Character __instance, float __0) {
    if (__instance == null || __instance != Player.m_localPlayer) return;
    Emit(EasyEventContract.CharacterHealed(__0, DateTimeOffset.UtcNow));
  }

  static void Emit(RuntimeEvent evt) {
    try { if (evt != null) RuntimeCooperativePatches.Engine?.OnEasyEvent(evt); } catch { }
  }
}

namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using System.Globalization;
using ComfyQuestContracts;
using HarmonyLib;

/// <summary>Small, local-player signals for quick solo Quest Studio laps.</summary>
static class RuntimeEasyEventPatches {
  public static void Apply(Harmony harmony) {
    RuntimePatching.TryPatch(harmony, typeof(Chat), "SendText",
        new[] { typeof(Talker.Type), typeof(string) }, typeof(RuntimeEasyEventPatches),
        nameof(ChatSent), "Chat.SendText(Type, string)");
    RuntimePatching.TryPatch(harmony, typeof(Humanoid), "DropItem",
        new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int) },
        typeof(RuntimeEasyEventPatches), nameof(ItemDropped),
        "Humanoid.DropItem(Inventory, ItemData, int)");
    RuntimePatching.TryPatch(harmony, typeof(Humanoid), "Pickup",
        new[] { typeof(UnityEngine.GameObject), typeof(bool), typeof(bool) },
        typeof(RuntimeEasyEventPatches), nameof(ItemPickedUp),
        "Humanoid.Pickup(GameObject, bool, bool)");
    RuntimePatching.TryPatch(harmony, typeof(Humanoid), "EquipItem",
        new[] { typeof(ItemDrop.ItemData), typeof(bool) }, typeof(RuntimeEasyEventPatches),
        nameof(ItemEquipped), "Humanoid.EquipItem(ItemData, bool)");
    RuntimePatching.TryPatch(harmony, typeof(Player), "ConsumeItem",
        new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(bool) },
        typeof(RuntimeEasyEventPatches), nameof(ItemConsumed),
        "Player.ConsumeItem(Inventory, ItemData, bool)");
    RuntimePatching.TryPatch(harmony, typeof(Character), "Heal",
        new[] { typeof(float), typeof(bool) }, typeof(RuntimeEasyEventPatches),
        nameof(CharacterHealed), "Character.Heal(float, bool)");
    RuntimePatching.TryPatch(harmony, typeof(Character), "RPC_Heal",
        new[] { typeof(long), typeof(float), typeof(bool) }, typeof(RuntimeEasyEventPatches),
        nameof(CharacterRpcHealed), "Character.RPC_Heal(long, float, bool)");
  }

  static void ChatSent(Talker.Type __0, string __1) {
    var evt = EasyEventContract.ChatSent(__0.ToString(), __1, DateTimeOffset.UtcNow);
    RuntimeEventRouter.Emit("Chat.SendText(Type, string)", evt, "local-player", evt?.Target);
  }

  static void ItemDropped(Humanoid __instance, ItemDrop.ItemData __1, int __2, bool __result) {
    if (!__result || __instance == null || __instance != Player.m_localPlayer) return;
    var prefab = __1?.m_dropPrefab == null ? null : __1.m_dropPrefab.name;
    var evt = EasyEventContract.ItemDropped(prefab, __2, DateTimeOffset.UtcNow);
    RuntimeEventRouter.Emit("Humanoid.DropItem(Inventory, ItemData, int)", evt,
        Identity(__instance), (evt?.Target ?? "") + "|" + __2);
  }

  static void ItemPickedUp(Humanoid __instance, UnityEngine.GameObject __0, bool __result) {
    if (!__result || __instance == null || __instance != Player.m_localPlayer) return;
    var item = __0 == null ? null : __0.GetComponent<ItemDrop>()?.m_itemData;
    var prefab = item?.m_dropPrefab == null ? __0?.name : item.m_dropPrefab.name;
    var evt = EasyEventContract.ItemPickedUp(prefab, DateTimeOffset.UtcNow);
    RuntimeEventRouter.Emit("Humanoid.Pickup(GameObject, bool, bool)", evt,
        Identity(__instance), evt?.Target);
  }

  static void ItemEquipped(Humanoid __instance, ItemDrop.ItemData __0, bool __result) {
    if (!__result || __instance == null || __instance != Player.m_localPlayer) return;
    var prefab = __0?.m_dropPrefab == null ? null : __0.m_dropPrefab.name;
    var evt = EasyEventContract.ItemEquipped(prefab, DateTimeOffset.UtcNow);
    RuntimeEventRouter.Emit("Humanoid.EquipItem(ItemData, bool)", evt,
        Identity(__instance), evt?.Target);
  }

  static void ItemConsumed(Player __instance, ItemDrop.ItemData __1, bool __result) {
    if (!__result || __instance == null || __instance != Player.m_localPlayer) return;
    var prefab = __1?.m_dropPrefab == null ? null : __1.m_dropPrefab.name;
    var evt = EasyEventContract.ItemConsumed(prefab, DateTimeOffset.UtcNow);
    RuntimeEventRouter.Emit("Player.ConsumeItem(Inventory, ItemData, bool)", evt,
        Identity(__instance), evt?.Target);
  }

  static void CharacterHealed(Character __instance, float __0) =>
      EmitHeal("Character.Heal(float, bool)", __instance, __0);

  static void CharacterRpcHealed(Character __instance, float __1) =>
      EmitHeal("Character.RPC_Heal(long, float, bool)", __instance, __1);

  static void EmitHeal(string signature, Character character, float amount) {
    if (character == null || character != Player.m_localPlayer || amount <= 0f) return;
    string value = amount.ToString("R", CultureInfo.InvariantCulture);
    var evt = EasyEventContract.CharacterHealed(amount, DateTimeOffset.UtcNow);
    evt.Fields = new Dictionary<string, string> { ["amount"] = value };
    RuntimeEventRouter.Emit(signature, evt, Identity(character), value);
  }

  static string Identity(UnityEngine.Object value) =>
      value == null ? null : value.GetType().Name + ":" + value.GetInstanceID();
}

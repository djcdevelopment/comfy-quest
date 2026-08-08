namespace ComfyQuestLab;

using HarmonyLib;

/// <summary>Inventory seams: picking up, equipping, eating, dropping.
///
/// The largest category in the atlas at 22 seams, and the one that best makes the case
/// for extracting rather than hand-listing. Inventory.AddItem has SEVEN overloads and
/// RemoveItem has four. "Hook AddItem" is not a specification — you have to name a
/// signature, and if you name the wrong one you get silence.
///
/// So this hooks the seams that correspond to a thing a player did, not every mutation
/// of the underlying list:
///
///   Humanoid.Pickup      picked something up off the ground
///   Humanoid.EquipItem   put something in a hand
///   Humanoid.ConsumeItem ate or drank something
///   Container.TakeAll    emptied a chest
///
/// Inventory.AddItem itself is diagnostic-only. All seven overloads are patched by
/// DiagnosticPatches so a builder can inspect them deliberately, but the generated
/// policy structurally prevents those low-level mutations from completing quests.</summary>
public static class InventoryPatches {
  public static void Apply(Harmony harmony) {
    LabPatching.TryPatch(harmony, typeof(Humanoid), "Pickup",
        new[] { typeof(UnityEngine.GameObject), typeof(bool), typeof(bool) },
        nameof(PickupPostfix), "Humanoid.Pickup");
    LabPatching.TryPatch(harmony, typeof(Humanoid), "EquipItem",
        new[] { typeof(ItemDrop.ItemData), typeof(bool) },
        nameof(EquipItemPostfix), "Humanoid.EquipItem");
    LabPatching.TryPatch(harmony, typeof(Humanoid), "ConsumeItem",
        new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(bool) },
        nameof(ConsumeItemPostfix), "Humanoid.ConsumeItem");
    LabPatching.TryPatch(harmony, typeof(Player), "ConsumeItem",
        new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(bool) },
        nameof(PlayerConsumeItemPostfix), "Player.ConsumeItem");
    LabPatching.TryPatch(harmony, typeof(Humanoid), "DropItem",
        new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int) },
        nameof(DropItemPostfix), "Humanoid.DropItem");
    LabPatching.TryPatch(harmony, typeof(Humanoid), "UnequipItem",
        new[] { typeof(ItemDrop.ItemData), typeof(bool) },
        nameof(UnequipItemPostfix), "Humanoid.UnequipItem");
    LabPatching.TryPatch(harmony, typeof(Container), "TakeAll", new[] { typeof(Humanoid) },
        nameof(TakeAllPostfix), "Container.TakeAll");
  }

  static void PickupPostfix(Humanoid __instance, UnityEngine.GameObject __0, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer("Humanoid.Pickup(GameObject, bool, bool)", __instance,
        LabObserve.Clean(__0 == null ? null : __0.name), "picked up", __0);
  }

  static void EquipItemPostfix(Humanoid __instance, ItemDrop.ItemData __0, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer(
        "Humanoid.EquipItem(ItemData, bool)", __instance, ItemName(__0), "equipped");
  }

  static void ConsumeItemPostfix(Humanoid __instance, ItemDrop.ItemData __1, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer(
        "Humanoid.ConsumeItem(Inventory, ItemData, bool)",
        __instance, ItemName(__1), "consumed");
  }

  static void PlayerConsumeItemPostfix(Player __instance, ItemDrop.ItemData __1, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer(
        "Player.ConsumeItem(Inventory, ItemData, bool)",
        __instance, ItemName(__1), "consumed",
        fingerprint: ItemName(__1));
  }

  static void DropItemPostfix(Humanoid __instance, ItemDrop.ItemData __1, int __2, bool __result) {
    if (!__result || __2 <= 0) {
      return;
    }
    LabObserve.LocalPlayer(
        "Humanoid.DropItem(Inventory, ItemData, int)",
        __instance, ItemName(__1), "dropped " + __2,
        fingerprint: ItemName(__1) + "|" + __2);
  }

  static void UnequipItemPostfix(Humanoid __instance, ItemDrop.ItemData __0) {
    LabObserve.LocalPlayer(
        "Humanoid.UnequipItem(ItemData, bool)",
        __instance, ItemName(__0), "unequipped",
        fingerprint: ItemName(__0));
  }

  static void TakeAllPostfix(Container __instance, Humanoid __0) {
    LabObserve.LocalPlayer(
        "Container.TakeAll(Humanoid)", __0 as Character,
        LabObserve.Clean(__instance == null ? null : __instance.name), "took all",
        __instance);
  }

  /// <summary>The shared prefab name, which is what a quest would match on — not the
  /// localised display name, which differs per language and would make a quest that
  /// works for you fail for someone else.</summary>
  static string ItemName(ItemDrop.ItemData item) {
    if (item == null || item.m_dropPrefab == null) {
      return "unknown";
    }
    return LabObserve.Clean(item.m_dropPrefab.name);
  }
}

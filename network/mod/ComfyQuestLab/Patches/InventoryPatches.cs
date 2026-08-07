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
/// Inventory.AddItem itself is left alone on purpose. It fires for every internal
/// shuffle — crafting output, container moves, stack merges — so a quest built on it
/// would fire constantly and mean nothing. That is a judgement, and a builder who
/// disagrees can see the seam listed as lab-only in the atlas and argue for it.</summary>
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
    LabPatching.TryPatch(harmony, typeof(Container), "TakeAll", new[] { typeof(Humanoid) },
        nameof(TakeAllPostfix), "Container.TakeAll");
  }

  static void PickupPostfix(Humanoid __instance, UnityEngine.GameObject __0, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer("Humanoid.Pickup", __instance,
        LabObserve.Clean(__0 == null ? null : __0.name), "picked up");
  }

  static void EquipItemPostfix(Humanoid __instance, ItemDrop.ItemData __0, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer("Humanoid.EquipItem", __instance, ItemName(__0), "equipped");
  }

  static void ConsumeItemPostfix(Humanoid __instance, ItemDrop.ItemData __1, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer("Humanoid.ConsumeItem", __instance, ItemName(__1), "consumed");
  }

  static void TakeAllPostfix(Humanoid __0) {
    LabObserve.LocalPlayer("Container.TakeAll", __0 as Character, "container", "took all");
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

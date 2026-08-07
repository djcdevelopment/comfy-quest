namespace ComfyQuestLab;

using HarmonyLib;

/// <summary>Crafting seams: making things, and feeding the machines that make things.
///
/// Note where the craft actually happens — `InventoryGui.DoCrafting`, on the UI class,
/// not on `Player` or `CraftingStation`. That is genuinely surprising and it is the kind
/// of thing you only learn by reading the assembly. A builder guessing at
/// "Player.Craft" would find nothing and conclude crafting is unhookable.
///
/// The smelter and cooking station are the other half: feeding ore, coal and meat into a
/// station is a distinct player act from crafting at a bench, and a production quest
/// ("smelt 50 iron") lives here rather than in the craft seam.</summary>
public static class CraftingPatches {
  public static void Apply(Harmony harmony) {
    LabPatching.TryPatch(harmony, typeof(InventoryGui), "DoCrafting", new[] { typeof(Player) },
        nameof(DoCraftingPostfix), "InventoryGui.DoCrafting");
    LabPatching.TryPatch(harmony, typeof(Smelter), "OnAddOre",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) },
        nameof(OnAddOrePostfix), "Smelter.OnAddOre");
    LabPatching.TryPatch(harmony, typeof(Smelter), "OnAddFuel",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) },
        nameof(OnAddFuelPostfix), "Smelter.OnAddFuel");
  }

  /// <summary>A craft completed. The recipe is on the GUI's own selection state, which
  /// the lab deliberately does not reach into — a private-field read is exactly the kind
  /// of thing that breaks on a game update and teaches a builder a lie in the meantime.
  /// The event itself is the lesson; naming the item is a later increment.</summary>
  static void DoCraftingPostfix(Player __0) {
    LabObserve.LocalPlayer("InventoryGui.DoCrafting", __0, "crafting bench", "crafted");
  }

  static void OnAddOrePostfix(Smelter __instance, Humanoid __1, ItemDrop.ItemData __2, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer("Smelter.OnAddOre", __1 as Character,
        StationName(__instance), "added ore " + ItemName(__2));
  }

  static void OnAddFuelPostfix(Smelter __instance, Humanoid __1, ItemDrop.ItemData __2, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer("Smelter.OnAddFuel", __1 as Character,
        StationName(__instance), "added fuel " + ItemName(__2));
  }

  static string StationName(Smelter smelter) {
    return LabObserve.Clean(smelter == null ? null : smelter.name);
  }

  static string ItemName(ItemDrop.ItemData item) {
    if (item == null || item.m_dropPrefab == null) {
      return "unknown";
    }
    return LabObserve.Clean(item.m_dropPrefab.name);
  }
}

namespace ComfyQuestLab;

using System.Collections.Generic;
using System.Globalization;

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
    LabPatching.TryPatch(harmony, typeof(Smelter), "RPC_AddOre",
        new[] { typeof(long), typeof(string) },
        nameof(SmelterRpcAddOrePostfix), "Smelter.RPC_AddOre");
    LabPatching.TryPatch(harmony, typeof(Smelter), "RPC_AddFuel",
        new[] { typeof(long) },
        nameof(SmelterRpcAddFuelPostfix), "Smelter.RPC_AddFuel");
    LabPatching.TryPatch(harmony, typeof(Smelter), "Spawn",
        new[] { typeof(string), typeof(int) },
        nameof(SmelterSpawnPostfix), "Smelter.Spawn");
    LabPatching.TryPatch(harmony, typeof(CookingStation), "RPC_AddFuel",
        new[] { typeof(long) },
        nameof(CookingRpcAddFuelPostfix), "CookingStation.RPC_AddFuel");
    LabPatching.TryPatch(harmony, typeof(CookingStation), "RPC_AddItem",
        new[] { typeof(long), typeof(string) },
        nameof(CookingRpcAddItemPostfix), "CookingStation.RPC_AddItem");
    LabPatching.TryPatch(harmony, typeof(CookingStation), "RPC_RemoveDoneItem",
        new[] { typeof(long), typeof(UnityEngine.Vector3), typeof(int) },
        nameof(CookingRpcRemoveDoneItemPostfix), "CookingStation.RPC_RemoveDoneItem");
    LabPatching.TryPatch(harmony, typeof(Fermenter), "RPC_AddItem",
        new[] { typeof(long), typeof(string) },
        nameof(FermenterRpcAddItemPostfix), "Fermenter.RPC_AddItem");
  }

  /// <summary>A craft completed. The recipe is on the GUI's own selection state, which
  /// the lab deliberately does not reach into — a private-field read is exactly the kind
  /// of thing that breaks on a game update and teaches a builder a lie in the meantime.
  /// The event itself is the lesson; naming the item is a later increment.</summary>
  static void DoCraftingPostfix(Player __0) {
    LabObserve.LocalPlayer(
        "InventoryGui.DoCrafting(Player)", __0, "crafting bench", "crafted");
  }

  static void OnAddOrePostfix(Smelter __instance, Humanoid __1, ItemDrop.ItemData __2, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer(
        "Smelter.OnAddOre(Switch, Humanoid, ItemData)", __1 as Character,
        ItemName(__2), "added at " + StationName(__instance), __instance,
        ItemName(__2), StationFields(__instance, ItemName(__2)));
  }

  static void OnAddFuelPostfix(Smelter __instance, Humanoid __1, ItemDrop.ItemData __2, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer(
        "Smelter.OnAddFuel(Switch, Humanoid, ItemData)", __1 as Character,
        ItemName(__2), "added at " + StationName(__instance), __instance,
        "fuel", StationFields(__instance, ItemName(__2)));
  }

  static void SmelterRpcAddOrePostfix(Smelter __instance, long __0, string __1) {
    if (!LabEventRouter.IsLocalSender(__0)) {
      return;
    }
    string item = LabObserve.Clean(__1);
    LabObserve.Seam(
        "Smelter.RPC_AddOre(long, string)", item,
        "added at " + StationName(__instance), __instance, item,
        StationFields(__instance, item));
  }

  static void SmelterRpcAddFuelPostfix(Smelter __instance, long __0) {
    if (!LabEventRouter.IsLocalSender(__0)) {
      return;
    }
    LabObserve.Seam(
        "Smelter.RPC_AddFuel(long)", "fuel",
        "added at " + StationName(__instance), __instance, "fuel",
        StationFields(__instance, "fuel"));
  }

  static void SmelterSpawnPostfix(Smelter __instance, string __0, int __1) {
    if (__1 <= 0) {
      return;
    }
    string item = LabObserve.Clean(__0);
    LabObserve.Seam(
        "Smelter.Spawn(string, int)", item,
        "produced " + __1 + " at " + StationName(__instance), __instance,
        item + "|" + __1.ToString(CultureInfo.InvariantCulture),
        StationFields(__instance, item, __1));
  }

  static void CookingRpcAddFuelPostfix(CookingStation __instance, long __0) {
    if (!LabEventRouter.IsLocalSender(__0)) {
      return;
    }
    LabObserve.Seam(
        "CookingStation.RPC_AddFuel(long)", "fuel", "cooking fuel added",
        __instance, "fuel", StationFields(__instance, "fuel"));
  }

  static void CookingRpcAddItemPostfix(CookingStation __instance, long __0, string __1) {
    if (!LabEventRouter.IsLocalSender(__0)) {
      return;
    }
    string item = LabObserve.Clean(__1);
    LabObserve.Seam(
        "CookingStation.RPC_AddItem(long, string)", item, "cooking input added",
        __instance, item, StationFields(__instance, item));
  }

  static void CookingRpcRemoveDoneItemPostfix(
      CookingStation __instance, long __0, int __2) {
    if (!LabEventRouter.IsLocalSender(__0)) {
      return;
    }
    string slot = __2.ToString(CultureInfo.InvariantCulture);
    LabObserve.Seam(
        "CookingStation.RPC_RemoveDoneItem(long, Vector3, int)",
        "cooking output", "collected slot " + slot,
        __instance, slot, StationFields(__instance, "cooking output", __2));
  }

  static void FermenterRpcAddItemPostfix(Fermenter __instance, long __0, string __1) {
    if (!LabEventRouter.IsLocalSender(__0)) {
      return;
    }
    string item = LabObserve.Clean(__1);
    LabObserve.Seam(
        "Fermenter.RPC_AddItem(long, string)", item, "fermenter input added",
        __instance, item, StationFields(__instance, item));
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

  static IReadOnlyDictionary<string, string> StationFields(
      UnityEngine.Object station, string item, int quantity = 1) {
    return new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) {
      ["station"] = LabObserve.Clean(station == null ? null : station.name),
      ["item"] = item ?? "unknown",
      ["quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
    };
  }
}

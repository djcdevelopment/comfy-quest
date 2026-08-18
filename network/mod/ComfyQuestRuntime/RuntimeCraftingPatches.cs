namespace ComfyQuestRuntime;

using System;
using ComfyQuestContracts;
using HarmonyLib;

/// <summary>Result-proven local crafting and host-owned station production.</summary>
static class RuntimeCraftingPatches {
  sealed class CraftState {
    public Player Player;
    public string Output;
    public string Station;
    public int Before;
    public bool Candidate;
  }

  sealed class StationState {
    public ZNetView View;
    public string Station;
    public string Item;
    public double Before;
    public int CountBefore;
    public int SlotCapacity;
    public int Quantity = 1;
    public bool Local;
    public bool Owner;
  }

  public static void Apply(Harmony harmony) {
    RuntimePatching.TryPatchWithState(harmony, typeof(InventoryGui), "DoCrafting",
        new[] { typeof(Player) }, typeof(RuntimeCraftingPatches),
        nameof(CraftPrefix), nameof(Crafted), "InventoryGui.DoCrafting(Player)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Smelter), "OnAddOre",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) },
        typeof(RuntimeCraftingPatches), nameof(SmelterOrePrefix), nameof(SmelterOreAdded),
        "Smelter.OnAddOre(Switch, Humanoid, ItemData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Smelter), "RPC_AddOre",
        new[] { typeof(long), typeof(string) }, typeof(RuntimeCraftingPatches),
        nameof(SmelterRpcOrePrefix), nameof(SmelterRpcOreAdded),
        "Smelter.RPC_AddOre(long, string)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Smelter), "OnAddFuel",
        new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) },
        typeof(RuntimeCraftingPatches), nameof(SmelterFuelPrefix), nameof(SmelterFuelAdded),
        "Smelter.OnAddFuel(Switch, Humanoid, ItemData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Smelter), "RPC_AddFuel",
        new[] { typeof(long) }, typeof(RuntimeCraftingPatches),
        nameof(SmelterRpcFuelPrefix), nameof(SmelterRpcFuelAdded),
        "Smelter.RPC_AddFuel(long)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Smelter), "Spawn",
        new[] { typeof(string), typeof(int) }, typeof(RuntimeCraftingPatches),
        nameof(SmelterSpawnPrefix), nameof(SmelterSpawned), "Smelter.Spawn(string, int)");
    RuntimePatching.TryPatchWithState(harmony, typeof(CookingStation), "RPC_AddFuel",
        new[] { typeof(long) }, typeof(RuntimeCraftingPatches),
        nameof(CookingFuelPrefix), nameof(CookingFuelAdded),
        "CookingStation.RPC_AddFuel(long)");
    RuntimePatching.TryPatchWithState(harmony, typeof(CookingStation), "RPC_AddItem",
        new[] { typeof(long), typeof(string) }, typeof(RuntimeCraftingPatches),
        nameof(CookingItemPrefix), nameof(CookingItemAdded),
        "CookingStation.RPC_AddItem(long, string)");
    RuntimePatching.TryPatchWithState(harmony, typeof(CookingStation), "RPC_RemoveDoneItem",
        new[] { typeof(long), typeof(UnityEngine.Vector3), typeof(int) },
        typeof(RuntimeCraftingPatches), nameof(CookingRemovePrefix), nameof(CookingRemoved),
        "CookingStation.RPC_RemoveDoneItem(long, Vector3, int)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Fermenter), "RPC_AddItem",
        new[] { typeof(long), typeof(string) }, typeof(RuntimeCraftingPatches),
        nameof(FermenterPrefix), nameof(FermenterAdded),
        "Fermenter.RPC_AddItem(long, string)");
  }

  static void CraftPrefix(Player __0, Recipe ___m_craftRecipe,
      ItemDrop.ItemData ___m_craftUpgradeItem, out CraftState __state) {
    __state = new CraftState();
    try {
      if (__0 == null || __0 != Player.m_localPlayer || ___m_craftRecipe?.m_item == null
          || ___m_craftUpgradeItem != null) return;
      string output = ___m_craftRecipe.m_item.gameObject.name;
      __state.Player = __0;
      __state.Output = output;
      __state.Station = __0.GetCurrentCraftingStation()?.name ?? "handcrafting";
      __state.Before = Count(__0, output);
      __state.Candidate = true;
    } catch { }
  }

  static void Crafted(CraftState __state) {
    try {
      if (__state == null || !__state.Candidate || !CraftingProof.ItemCrafted(
          __state.Player == Player.m_localPlayer, true, __state.Before,
          Count(__state.Player, __state.Output))) return;
      var evt = CraftingEventContract.ItemCrafted(__state.Station, DateTimeOffset.UtcNow);
      RuntimeEventRouter.Emit("InventoryGui.DoCrafting(Player)", evt,
          Identity(__state.Player), __state.Output);
    } catch { }
  }

  static void SmelterOrePrefix(Smelter __instance, Humanoid __1, ItemDrop.ItemData __2,
      out StationState __state) => __state = CaptureQueue(
          __instance, __1 == Player.m_localPlayer, Prefab(__2));
  static void SmelterRpcOrePrefix(Smelter __instance, long __0, string __1,
      out StationState __state) => __state = CaptureQueue(__instance, Local(__0), __1);
  static void SmelterOreAdded(StationState __state) => CompleteInput(
      "Smelter.OnAddOre(Switch, Humanoid, ItemData)", __state);
  static void SmelterRpcOreAdded(StationState __state) => CompleteInput(
      "Smelter.RPC_AddOre(long, string)", __state);

  static void SmelterFuelPrefix(Smelter __instance, Humanoid __1,
      out StationState __state) => __state = CaptureFuel(
          __instance, __1 == Player.m_localPlayer, __instance?.m_fuelItem?.name);
  static void SmelterRpcFuelPrefix(Smelter __instance, long __0,
      out StationState __state) => __state = CaptureFuel(
          __instance, Local(__0), __instance?.m_fuelItem?.name);
  static void SmelterFuelAdded(StationState __state) => CompleteFuel(
      "Smelter.OnAddFuel(Switch, Humanoid, ItemData)", __state);
  static void SmelterRpcFuelAdded(StationState __state) => CompleteFuel(
      "Smelter.RPC_AddFuel(long)", __state);

  static void CookingFuelPrefix(CookingStation __instance, long __0,
      out StationState __state) => __state = CaptureFuel(
          __instance, Local(__0), __instance?.m_fuelItem?.name);
  static void CookingFuelAdded(StationState __state) => CompleteFuel(
      "CookingStation.RPC_AddFuel(long)", __state);

  static void CookingItemPrefix(CookingStation __instance, long __0, string __1,
      out StationState __state) => __state = CaptureSlots(__instance, Local(__0), __1);
  static void CookingItemAdded(StationState __state) => CompleteInput(
      "CookingStation.RPC_AddItem(long, string)", __state);

  static void CookingRemovePrefix(CookingStation __instance, long __0, int __2,
      out StationState __state) {
    __state = CaptureSlots(__instance, Local(__0), "cooking output");
    __state.Quantity = __2;
  }
  static void CookingRemoved(StationState __state) {
    try {
      int after = SlotCount(__state?.View, __state?.SlotCapacity ?? 0);
      if (__state == null || !CraftingProof.StationOutputCollected(
          __state.Local, __state.Owner, __state.CountBefore, after, __state.Quantity)) return;
      var evt = CraftingEventContract.StationOutputCollected(
          __state.Station, __state.Quantity, DateTimeOffset.UtcNow);
      Emit("CookingStation.RPC_RemoveDoneItem(long, Vector3, int)", evt, __state);
    } catch { }
  }

  static void FermenterPrefix(Fermenter __instance, long __0, string __1,
      out StationState __state) {
    __state = Capture(__instance, Local(__0), __1);
    try { __state.Before = __state.View?.GetZDO()?.GetString(ZDOVars.s_content).Length ?? 0; }
    catch { __state.Owner = false; }
  }
  static void FermenterAdded(StationState __state) {
    try {
      int after = __state?.View?.GetZDO()?.GetString(ZDOVars.s_content).Length ?? 0;
      if (__state == null || !CraftingProof.StationSlotAdded(
          __state.Local, __state.Owner, (int)__state.Before, after)) return;
      Emit("Fermenter.RPC_AddItem(long, string)",
          CraftingEventContract.StationInputAdded(
              __state.Station, __state.Item, DateTimeOffset.UtcNow), __state);
    } catch { }
  }

  static void SmelterSpawnPrefix(Smelter __instance, string __0, int __1,
      out StationState __state) {
    __state = Capture(__instance, false, null);
    __state.Local = true; // observed-world, host-owned producer
    __state.Quantity = __1;
    try {
      foreach (var conversion in __instance?.m_conversion ?? new())
        if (conversion?.m_from?.gameObject?.name == __0) {
          __state.Item = conversion.m_to?.gameObject?.name;
          break;
        }
    } catch { }
  }
  static void SmelterSpawned(StationState __state) {
    try {
      if (__state == null || !CraftingProof.StationOutputProduced(
          __state.Owner, !string.IsNullOrWhiteSpace(__state.Item), __state.Quantity)) return;
      Emit("Smelter.Spawn(string, int)", CraftingEventContract.StationOutputProduced(
          __state.Station, __state.Item, __state.Quantity, DateTimeOffset.UtcNow), __state);
    } catch { }
  }

  static StationState CaptureQueue(Smelter station, bool local, string item) {
    var state = Capture(station, local, item);
    try { state.CountBefore = state.View?.GetZDO()?.GetInt(ZDOVars.s_queued) ?? 0; }
    catch { state.Owner = false; }
    return state;
  }

  static StationState CaptureFuel(UnityEngine.Component station, bool local, string item) {
    var state = Capture(station, local, item);
    try { state.Before = state.View?.GetZDO()?.GetFloat(ZDOVars.s_fuel) ?? 0f; }
    catch { state.Owner = false; }
    return state;
  }

  static StationState CaptureSlots(CookingStation station, bool local, string item) {
    var state = Capture(station, local, item);
    state.SlotCapacity = station?.m_slots?.Length ?? 0;
    state.CountBefore = SlotCount(state.View, state.SlotCapacity);
    return state;
  }

  static StationState Capture(UnityEngine.Component station, bool local, string item) {
    var view = station == null ? null : station.GetComponent<ZNetView>();
    return new StationState {
      View = view, Station = station?.name, Item = item, Local = local,
      Owner = view != null && view.IsValid() && view.IsOwner() && view.GetZDO() != null,
    };
  }

  static void CompleteInput(string signature, StationState state) {
    try {
      int after = state?.View?.GetZDO()?.GetInt(ZDOVars.s_queued) ?? 0;
      if (signature.StartsWith("CookingStation", StringComparison.Ordinal))
        after = SlotCount(state?.View, state?.SlotCapacity ?? 0);
      if (state == null || !CraftingProof.StationSlotAdded(
          state.Local, state.Owner, state.CountBefore, after)) return;
      Emit(signature, CraftingEventContract.StationInputAdded(
          state.Station, state.Item, DateTimeOffset.UtcNow), state);
    } catch { }
  }

  static void CompleteFuel(string signature, StationState state) {
    try {
      double after = state?.View?.GetZDO()?.GetFloat(ZDOVars.s_fuel) ?? 0f;
      if (state == null || !CraftingProof.StationValueIncreased(
          state.Local, state.Owner, state.Before, after)) return;
      Emit(signature, CraftingEventContract.StationFuelAdded(
          state.Station, state.Item, DateTimeOffset.UtcNow), state);
    } catch { }
  }

  static void Emit(string signature, RuntimeEvent evt, StationState state) =>
      RuntimeEventRouter.Emit(signature, evt, Identity(state?.View),
          (state?.Item ?? "") + "|" + state?.Quantity);

  static int SlotCount(ZNetView view, int slots) {
    try {
      var zdo = view?.GetZDO();
      if (zdo == null) return 0;
      int count = 0;
      for (int index = 0; index < slots; index++)
        if (!string.IsNullOrEmpty(zdo.GetString("slot" + index))) count++;
      return count;
    } catch { return 0; }
  }

  static int Count(Player player, string prefab) {
    int count = 0;
    try {
      foreach (var item in player?.GetInventory()?.GetAllItems() ?? new())
        if (item?.m_dropPrefab?.name == prefab) count += item.m_stack;
    } catch { }
    return count;
  }

  static string Prefab(ItemDrop.ItemData item) => item?.m_dropPrefab?.name;
  static bool Local(long sender) { try { return sender != 0L && ZNet.GetUID() == sender; } catch { return false; } }
  static string Identity(UnityEngine.Object value) => value == null ? null : value.GetType().Name + ":" + value.GetInstanceID();
}

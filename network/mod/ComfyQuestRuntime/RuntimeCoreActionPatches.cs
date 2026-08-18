namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using ComfyQuestContracts;
using HarmonyLib;
using UnityEngine;

/// <summary>Evidence-gated local inventory, building, and travel actions.</summary>
static class RuntimeCoreActionPatches {
  sealed class TakeAllState {
    public int Key;
    public string Subject;
    public string Target;
    public double At;
    public int ItemsBefore;
    public bool Candidate;
  }

  sealed class UnequipState {
    public bool WasEquipped;
    public string Subject;
    public string Target;
  }

  class PieceState {
    public string Subject;
    public string Target;
    public bool Candidate;
  }

  sealed class RepairState : PieceState {
    public WearNTear Wear;
    public bool Succeeded;
    public RepairState Previous;
  }

  sealed class RpcState : PieceState { }

  static readonly Dictionary<int, TakeAllState> PendingTakeAll = new();
  static RepairState activeRepair;

  public static void Apply(Harmony harmony) {
    RuntimePatching.TryPatchWithState(harmony, typeof(Container), "TakeAll",
        new[] { typeof(Humanoid) }, typeof(RuntimeCoreActionPatches),
        nameof(TakeAllPrefix), nameof(TakeAllPostfix), "Container.TakeAll(Humanoid)");
    RuntimePatching.TryPatchEngine(harmony, typeof(Container), "RPC_TakeAllRespons",
        new[] { typeof(long), typeof(bool) }, typeof(RuntimeCoreActionPatches),
        nameof(TakeAllResponse), "Container.RPC_TakeAllRespons(long, bool)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Humanoid), "UnequipItem",
        new[] { typeof(ItemDrop.ItemData), typeof(bool) }, typeof(RuntimeCoreActionPatches),
        nameof(UnequipPrefix), nameof(ItemUnequipped),
        "Humanoid.UnequipItem(ItemData, bool)");
    RuntimePatching.TryPatchWithState(harmony, typeof(WearNTear), "Destroy",
        new[] { typeof(HitData), typeof(bool) }, typeof(RuntimeCoreActionPatches),
        nameof(DestroyPrefix), nameof(PieceDestroyed), "WearNTear.Destroy(HitData, bool)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Player), "RemovePiece", Type.EmptyTypes,
        typeof(RuntimeCoreActionPatches), nameof(RemovePrefix), nameof(PieceRemoved),
        "Player.RemovePiece()");
    RuntimePatching.TryPatchEngineWithState(harmony, typeof(WearNTear), "RPC_Remove",
        new[] { typeof(long), typeof(bool) }, typeof(RuntimeCoreActionPatches),
        nameof(RpcRemovePrefix), nameof(RpcRemoved), "WearNTear.RPC_Remove(long, bool)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Player), "Repair",
        new[] { typeof(ItemDrop.ItemData), typeof(Piece) }, typeof(RuntimeCoreActionPatches),
        nameof(RepairPrefix), nameof(PieceRepaired), "Player.Repair(ItemData, Piece)");
    RuntimePatching.TryPatchEngine(harmony, typeof(WearNTear), "Repair", Type.EmptyTypes,
        typeof(RuntimeCoreActionPatches), nameof(RepairResult), "WearNTear.Repair()");
    RuntimePatching.TryPatchEngineWithState(harmony, typeof(WearNTear), "RPC_Repair",
        new[] { typeof(long) }, typeof(RuntimeCoreActionPatches),
        nameof(RpcRepairPrefix), nameof(RpcRepaired), "WearNTear.RPC_Repair(long)");
    RuntimePatching.TryPatch(harmony, typeof(Player), "TeleportTo",
        new[] { typeof(Vector3), typeof(Quaternion), typeof(bool) },
        typeof(RuntimeCoreActionPatches), nameof(PlayerTeleported),
        "Player.TeleportTo(Vector3, Quaternion, bool)");
  }

  static void TakeAllPrefix(Container __instance, Humanoid __0, out TakeAllState __state) {
    __state = new TakeAllState();
    try {
      if (__instance == null || __0 == null || __0 != Player.m_localPlayer
          || __instance.GetInventory() == null || __instance.GetInventory().NrOfItems() <= 0) return;
      __state.Key = __instance.GetInstanceID();
      __state.Subject = Identity(__instance);
      __state.Target = __instance.name;
      __state.At = Time.realtimeSinceStartup;
      __state.ItemsBefore = __instance.GetInventory().NrOfItems();
      __state.Candidate = true;
      if (PendingTakeAll.Count >= 64) PendingTakeAll.Clear();
      // Installed before the original call because a local RPC response may be synchronous.
      PendingTakeAll[__state.Key] = __state;
    } catch { }
  }

  static void TakeAllPostfix(bool __result, TakeAllState __state) {
    if (__state != null && __state.Candidate && !__result) PendingTakeAll.Remove(__state.Key);
  }

  static void TakeAllResponse(Container __instance, bool __1) {
    try {
      if (__instance == null || !PendingTakeAll.TryGetValue(__instance.GetInstanceID(), out var pending)) return;
      PendingTakeAll.Remove(pending.Key);
      double now = Time.realtimeSinceStartup;
      if (__instance.GetInventory() == null || !CoreActionProof.ContainerEmptied(
          true, __1, pending.ItemsBefore, __instance.GetInventory().NrOfItems(), now - pending.At)) return;
      var evt = CoreActionEventContract.ContainerEmptied(pending.Target, DateTimeOffset.UtcNow);
      RuntimeEventRouter.Emit("Container.TakeAll(Humanoid)", evt,
          pending.Subject, evt?.Target);
    } catch { }
  }

  static void UnequipPrefix(Humanoid __instance, ItemDrop.ItemData __0,
      out UnequipState __state) {
    __state = new UnequipState();
    try {
      if (__instance == null || __instance != Player.m_localPlayer || __0 == null
          || !__instance.IsItemEquiped(__0)) return;
      __state.WasEquipped = true;
      __state.Subject = Identity(__instance);
      __state.Target = __0.m_dropPrefab == null ? null : __0.m_dropPrefab.name;
    } catch { }
  }

  static void ItemUnequipped(Humanoid __instance, ItemDrop.ItemData __0,
      UnequipState __state) {
    try {
      if (__state == null || __instance == null || __0 == null
          || !CoreActionProof.ItemUnequipped(
              __instance == Player.m_localPlayer, __state.WasEquipped,
              __instance.IsItemEquiped(__0))) return;
      var evt = CoreActionEventContract.ItemUnequipped(__state.Target, DateTimeOffset.UtcNow);
      RuntimeEventRouter.Emit("Humanoid.UnequipItem(ItemData, bool)", evt,
          __state.Subject, evt?.Target);
    } catch { }
  }

  static void DestroyPrefix(WearNTear __instance, HitData __0, out PieceState __state) {
    __state = new PieceState();
    try {
      if (__instance == null || __0 == null || __0.GetAttacker() != Player.m_localPlayer) return;
      __state.Subject = NetworkIdentity(__instance);
      __state.Target = __instance.name;
      __state.Candidate = true;
    } catch { }
  }

  static void PieceDestroyed(PieceState __state) {
    if (__state == null || !__state.Candidate || !CoreActionProof.PieceDestroyed(true)) return;
    var evt = CoreActionEventContract.PieceDestroyed(__state.Target, DateTimeOffset.UtcNow);
    RuntimeEventRouter.Emit("WearNTear.Destroy(HitData, bool)", evt,
        __state.Subject, evt?.Target);
  }

  static void RemovePrefix(Player __instance, out PieceState __state) {
    __state = CapturePiece(__instance);
  }

  static void PieceRemoved(Player __instance, bool __result, PieceState __state) {
    if (__instance == null || !CoreActionProof.PieceRemoved(
        __instance == Player.m_localPlayer, __result)) return;
    string subject = __state?.Subject ?? Identity(__instance);
    var evt = CoreActionEventContract.PieceRemoved(DateTimeOffset.UtcNow);
    RuntimeEventRouter.Emit("Player.RemovePiece()", evt, subject, subject);
  }

  static void RpcRemovePrefix(WearNTear __instance, long __0, out RpcState __state) {
    __state = CaptureRpc(__instance, __0);
  }

  static void RpcRemoved(RpcState __state) {
    if (__state == null || !__state.Candidate) return;
    var evt = CoreActionEventContract.PieceRemoved(DateTimeOffset.UtcNow);
    RuntimeEventRouter.Emit("WearNTear.RPC_Remove(long, bool)", evt,
        __state.Subject, __state.Subject);
  }

  static void RepairPrefix(Player __instance, out RepairState __state) {
    PieceState piece = CapturePiece(__instance);
    __state = new RepairState {
      Candidate = piece.Candidate,
      Subject = piece.Subject,
      Target = piece.Target,
      Wear = __instance == null ? null : __instance.GetHoveringPiece()?.GetComponent<WearNTear>(),
      Previous = activeRepair,
    };
    activeRepair = __state;
  }

  static void RepairResult(WearNTear __instance, bool __result) {
    if (__result && activeRepair != null && activeRepair.Wear == __instance)
      activeRepair.Succeeded = true;
  }

  static void PieceRepaired(Player __instance, RepairState __state) {
    try {
      if (__state == null || !__state.Candidate || __instance == null
          || !CoreActionProof.PieceRepaired(
              __instance == Player.m_localPlayer, __state.Succeeded)) return;
      var evt = CoreActionEventContract.PieceRepaired(__state.Target, DateTimeOffset.UtcNow);
      RuntimeEventRouter.Emit("Player.Repair(ItemData, Piece)", evt,
          __state.Subject, evt?.Target);
    } finally {
      if (__state != null) activeRepair = __state.Previous;
    }
  }

  static void RpcRepairPrefix(WearNTear __instance, long __0, out RpcState __state) {
    __state = CaptureRpc(__instance, __0);
  }

  static void RpcRepaired(RpcState __state) {
    if (__state == null || !__state.Candidate) return;
    var evt = CoreActionEventContract.PieceRepaired(__state.Target, DateTimeOffset.UtcNow);
    RuntimeEventRouter.Emit("WearNTear.RPC_Repair(long)", evt,
        __state.Subject, evt?.Target);
  }

  static void PlayerTeleported(Player __instance, bool __result) {
    if (__instance == null || !CoreActionProof.PlayerTeleported(
        __instance == Player.m_localPlayer, __result)) return;
    var evt = CoreActionEventContract.PlayerTeleported(DateTimeOffset.UtcNow);
    RuntimeEventRouter.Emit("Player.TeleportTo(Vector3, Quaternion, bool)", evt,
        Identity(__instance), "successful-teleport");
  }

  static PieceState CapturePiece(Player player) {
    var state = new PieceState();
    try {
      if (player == null || player != Player.m_localPlayer) return state;
      Piece piece = player.GetHoveringPiece();
      if (piece == null) return state;
      state.Subject = NetworkIdentity(piece);
      state.Target = piece.name;
      state.Candidate = true;
    } catch { }
    return state;
  }

  static RpcState CaptureRpc(WearNTear wear, long sender) {
    var state = new RpcState();
    try {
      var view = wear == null ? null : wear.GetComponent<ZNetView>();
      if (sender == 0L || ZNet.GetUID() != sender || view == null
          || !view.IsValid() || !view.IsOwner()) return state;
      state.Subject = NetworkIdentity(wear);
      state.Target = wear.name;
      state.Candidate = true;
    } catch { }
    return state;
  }

  static string NetworkIdentity(Component value) {
    try {
      var zdo = value == null ? null : value.GetComponent<ZNetView>()?.GetZDO();
      if (zdo != null) return zdo.m_uid.ToString();
    } catch { }
    return Identity(value);
  }

  static string Identity(UnityEngine.Object value) =>
      value == null ? null : value.GetType().Name + ":" + value.GetInstanceID();

  public static void Reset() {
    PendingTakeAll.Clear();
    activeRepair = null;
  }
}

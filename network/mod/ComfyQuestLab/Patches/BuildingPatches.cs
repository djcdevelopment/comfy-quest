namespace ComfyQuestLab;

using HarmonyLib;
using UnityEngine;

/// <summary>Building seams: placing, repairing, and breaking structures.
///
/// A category worth wanting. "Build a longhouse", "repair the dock", "raise a ward" are
/// the quests a settlement steward actually writes, and `structure_placed` has existed
/// as a contract name since long before anything could produce it.
///
/// The three player verbs come off `Player` rather than `Piece`, because a piece being
/// placed is a consequence and the player placing it is the act. `WearNTear.Destroy` is
/// the opposite side — something broke, which may or may not have been you.</summary>
public static class BuildingPatches {
  public static void Apply(Harmony harmony) {
    LabPatching.TryPatch(harmony, typeof(Player), "PlacePiece",
        new[] { typeof(Piece), typeof(Vector3), typeof(Quaternion), typeof(bool) },
        nameof(PlacePiecePostfix), "Player.PlacePiece");
    LabPatching.TryPatch(harmony, typeof(Player), "RemovePiece", System.Type.EmptyTypes,
        nameof(RemovePiecePostfix), "Player.RemovePiece");
    LabPatching.TryPatch(harmony, typeof(Player), "Repair",
        new[] { typeof(ItemDrop.ItemData), typeof(Piece) },
        nameof(RepairPostfix), "Player.Repair");
    LabPatching.TryPatch(harmony, typeof(WearNTear), "Destroy",
        new[] { typeof(HitData), typeof(bool) },
        nameof(DestroyPostfix), "WearNTear.Destroy");
    LabPatching.TryPatch(harmony, typeof(WearNTear), "ApplyDamage",
        new[] { typeof(float), typeof(HitData) },
        nameof(ApplyDamagePostfix), "WearNTear.ApplyDamage");
    LabPatching.TryPatch(harmony, typeof(WearNTear), "RPC_Remove",
        new[] { typeof(long), typeof(bool) },
        nameof(RpcRemovePostfix), "WearNTear.RPC_Remove");
    LabPatching.TryPatch(harmony, typeof(WearNTear), "RPC_Repair", new[] { typeof(long) },
        nameof(RpcRepairPostfix), "WearNTear.RPC_Repair");
  }

  static void PlacePiecePostfix(Player __instance, Piece __0) {
    LabObserve.LocalPlayer(
        "Player.PlacePiece(Piece, Vector3, Quaternion, bool)",
        __instance, PieceName(__0), "placed", __0);
  }

  static void RemovePiecePostfix(Player __instance, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer("Player.RemovePiece()", __instance, "piece", "removed");
  }

  static void RepairPostfix(Player __instance, Piece __1) {
    LabObserve.LocalPlayer(
        "Player.Repair(ItemData, Piece)", __instance, PieceName(__1), "repaired", __1);
  }

  /// <summary>Not filtered to the local player: a structure breaking is worth seeing
  /// whether or not you broke it, and it is the seam a "defend the base" quest would
  /// want. It carries HitData, so the console can say when it was you.</summary>
  static void DestroyPostfix(WearNTear __instance, HitData __0) {
    string by = __0 != null && ComfyQuestLab.IsLocalPlayerAttacker(__0) ? "destroyed by you" : "destroyed";
    bool local = __0 != null && ComfyQuestLab.IsLocalPlayerAttacker(__0);
    LabObserve.Seam(
        "WearNTear.Destroy(HitData, bool)",
        LabObserve.Clean(__instance == null ? null : __instance.name),
        by,
        __instance,
        evaluate: local);
  }

  static void ApplyDamagePostfix(
      WearNTear __instance, float __0, HitData __1, bool __result) {
    if (!__result || __0 <= 0f) {
      return;
    }
    LabObserve.PlayerHit(
        "WearNTear.ApplyDamage(float, HitData)", __1, __instance,
        LabObserve.Clean(__instance == null ? null : __instance.name),
        "structure damage " + __0.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
  }

  static void RpcRemovePostfix(WearNTear __instance, long __0) {
    if (!LabEventRouter.IsLocalSender(__0)) {
      return;
    }
    // Player.RemovePiece is the attributed creator trigger. Keep the RPC witness visible,
    // but never let the same click complete a zero-cooldown quest twice.
    LabObserve.Seam(
        "WearNTear.RPC_Remove(long, bool)",
        LabObserve.Clean(__instance == null ? null : __instance.name),
        "remove RPC witnessed", __instance, "remove", evaluate: false);
  }

  static void RpcRepairPostfix(WearNTear __instance, long __0) {
    if (!LabEventRouter.IsLocalSender(__0)) {
      return;
    }
    // Player.Repair carries the local-player attribution and is the sole evaluator route.
    LabObserve.Seam(
        "WearNTear.RPC_Repair(long)",
        LabObserve.Clean(__instance == null ? null : __instance.name),
        "repair RPC witnessed", __instance, "repair", evaluate: false);
  }

  /// <summary>The prefab name a quest would match on, not the localised label.</summary>
  static string PieceName(Piece piece) {
    if (piece == null) {
      return "unknown";
    }
    return LabObserve.Clean(piece.name);
  }
}

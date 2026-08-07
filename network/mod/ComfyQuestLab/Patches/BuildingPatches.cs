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
  }

  static void PlacePiecePostfix(Player __instance, Piece __0) {
    LabObserve.LocalPlayer("Player.PlacePiece", __instance, PieceName(__0), "placed");
  }

  static void RemovePiecePostfix(Player __instance, bool __result) {
    if (!__result) {
      return;
    }
    LabObserve.LocalPlayer("Player.RemovePiece", __instance, "piece", "removed");
  }

  static void RepairPostfix(Player __instance, Piece __1) {
    LabObserve.LocalPlayer("Player.Repair", __instance, PieceName(__1), "repaired");
  }

  /// <summary>Not filtered to the local player: a structure breaking is worth seeing
  /// whether or not you broke it, and it is the seam a "defend the base" quest would
  /// want. It carries HitData, so the console can say when it was you.</summary>
  static void DestroyPostfix(WearNTear __instance, HitData __0) {
    string by = __0 != null && ComfyQuestLab.IsLocalPlayerAttacker(__0) ? "destroyed by you" : "destroyed";
    LabObserve.Seam("WearNTear.Destroy",
        LabObserve.Clean(__instance == null ? null : __instance.name), by);
  }

  /// <summary>The prefab name a quest would match on, not the localised label.</summary>
  static string PieceName(Piece piece) {
    if (piece == null) {
      return "unknown";
    }
    return LabObserve.Clean(piece.name);
  }
}

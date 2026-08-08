namespace ComfyQuestLab;

using System;

using HarmonyLib;

/// <summary>Make the panel usable without making the player flail.
///
/// Two postfixes, applied independently so one failing does not cost the other. Both are
/// the same shape ComfyNetworkSense and the retired ComfyControlSurface use — this is a
/// solved problem in this codebase and the solution is worth copying exactly.
///
/// InputGuard also acquires and maintains the cursor directly; these patches compose
/// that lifecycle with Valheim's own per-frame capture and player-input decisions.</summary>
public static class LabPanelInputPatches {
  public static void Apply(Harmony harmony) {
    LabPatching.TryPatch(harmony, typeof(GameCamera), "UpdateMouseCapture", Type.EmptyTypes,
        nameof(UpdateMouseCapturePostfix), "GameCamera.UpdateMouseCapture (panel cursor)");
    LabPatching.TryPatch(harmony, typeof(Character), "TakeInput", Type.EmptyTypes,
        nameof(TakeInputPostfix), "Character.TakeInput (panel input)");
  }

  /// <summary>Free the cursor while the panel is open, so its buttons and filter field
  /// can actually be clicked.</summary>
  static void UpdateMouseCapturePostfix() {
    try {
      InputGuard.MaintainPanelInput();
    } catch (Exception) {
    }
  }

  /// <summary>Stop the local player acting on input while the panel is open — otherwise
  /// clicking a filter button also swings whatever is equipped.</summary>
  static void TakeInputPostfix(Character __instance, ref bool __result) {
    try {
      if (InputGuard.PanelOwnsInput && __instance == Player.m_localPlayer) {
        __result = false;
      }
    } catch (Exception) {
    }
  }
}

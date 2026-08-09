namespace ComfyQuestLab;

using System;

using HarmonyLib;

using UnityEngine;

/// <summary>Keep a raised gallery standing.
///
/// This is deliberately NOT a seam and stays out of the seam roster: it observes nothing
/// and teaches nothing, it only stops the practice ground from falling on a student. The
/// roster answers "what can a quest be bound to", and this would be a wrong answer.
///
/// FALSE is off, on both flags. They are opt-INS whatever the "no" in the names suggests:
/// in the shipped WearNTear.UpdateWear the support check sits behind a `brfalse` on
/// m_noSupportWear and the rain damage behind a `brfalse` on m_noRoofWear, so the damage
/// is only ever reached when the flag is TRUE. The atlas annotation says "disables" for
/// both, which is backwards, and believing it cost two rounds of watching a gallery come
/// down faster each time. Read the IL, not the field name.
///
/// The component atlas is the reason the patch is needed at all. WearNTear carries both
/// flags as TunableFields — plain fields on the component instance — while only "health"
/// and "support" appear in its ZdoFields. So switching wear off at placement time lasts
/// exactly as long as that one instance does. ZNetScene rebuilds a piece from its ZDO
/// every time its zone reloads, and the rebuilt instance comes back carrying the prefab's
/// value. A gallery is 76 m across and can sit metres up, so it spans several zones and
/// starts reloading the moment its builder walks or looks around.
///
/// Re-applying the flags in Awake is the durable form of the same intent. Raising m_health
/// instead would not help — it is a TunableField too, and would only slow the fall.
///
/// Which pieces are ours is read from a mark in the piece's own ZDO, never from a
/// remembered id: ZDO ids do not survive a reload, and a gallery is expected to.
/// "Ours" spans both world-changing lanes — gallery and blueprint builds — via
/// LabMarks.IsLabBuilt, because a blueprint's cantilevers need exactly the same holding
/// up on zone reload that the gallery's raised platform does.</summary>
public static class GalleryStructurePatches {
  /// <summary>Marker carried only by generated ceiling braziers.</summary>
  public const string InfiniteBrazierMark = "comfyQuestLabInfiniteBrazier";

  public static void Apply(Harmony harmony) {
    try {
      var target = AccessTools.Method(typeof(WearNTear), "Awake");
      if (target == null) {
        ComfyQuestLab.LogInfo("gallery structure: WearNTear.Awake not found on this game "
            + "build — a raised gallery will collapse. The rest of the lab is unaffected.");
      } else {
        harmony.Patch(
            target,
            postfix: new HarmonyMethod(
                AccessTools.Method(typeof(GalleryStructurePatches), nameof(KeepStandingPostfix))));
      }
    } catch (Exception ex) {
      ComfyQuestLab.LogInfo("gallery structure: could not patch WearNTear.Awake — "
          + ex.Message + ". A raised gallery will collapse.");
    }

    try {
      var fireplace = AccessTools.Method(typeof(Fireplace), "Awake");
      if (fireplace == null) {
        ComfyQuestLab.LogInfo("gallery ambience: Fireplace.Awake not found; hanging "
            + "braziers will need ordinary fuel. Nothing else is affected.");
      } else {
        harmony.Patch(
            fireplace,
            postfix: new HarmonyMethod(
                AccessTools.Method(typeof(GalleryStructurePatches), nameof(KeepBrazierLitPostfix))));
      }
    } catch (Exception ex) {
      ComfyQuestLab.LogInfo("gallery ambience: could not patch Fireplace.Awake: "
          + ex.Message + ". Hanging braziers will need ordinary fuel.");
    }
  }

  /// <summary>Runs for every piece in the world, so it does the cheapest possible test
  /// first and touches nothing that is not ours.</summary>
  static void KeepStandingPostfix(WearNTear __instance) {
    try {
      if (__instance == null) {
        return;
      }
      var view = __instance.GetComponent<ZNetView>();
      if (view == null) {
        return;
      }
      ZDO zdo = view.GetZDO();
      if (zdo == null || !LabMarks.IsLabBuilt(zdo)) {
        return;
      }
      __instance.m_noSupportWear = false;
      __instance.m_noRoofWear = false;
    } catch (Exception) {
      // One piece failing to opt out is not worth a log line that repeats on zone load.
    }
  }

  /// <summary>Mark one real vanilla brazier as gallery ambience, fill it, and make its
  /// fuel invariant explicit. The ZDO marker lets the same treatment survive reloads.</summary>
  public static void MarkAndLight(GameObject host, bool infiniteFuel) {
    if (host == null || !infiniteFuel) {
      return;
    }
    try {
      var view = host.GetComponent<ZNetView>();
      ZDO zdo = view == null ? null : view.GetZDO();
      if (zdo == null) {
        return;
      }
      zdo.Set(InfiniteBrazierMark, 1);
      ApplyBrazier(host.GetComponent<Fireplace>(), zdo);
    } catch (Exception) {
      // The brazier still exists and can take ordinary fuel.
    }
  }

  static void KeepBrazierLitPostfix(Fireplace __instance) {
    try {
      if (__instance == null) {
        return;
      }
      var view = __instance.GetComponent<ZNetView>();
      ZDO zdo = view == null ? null : view.GetZDO();
      if (zdo == null || zdo.GetInt(InfiniteBrazierMark, 0) != 1
          || !LabGalleryBuilder.IsGalleryPiece(zdo)) {
        return;
      }
      ApplyBrazier(__instance, zdo);
    } catch (Exception) {
      // Runs at load time; never turn one cosmetic failure into a repeating error.
    }
  }

  static void ApplyBrazier(Fireplace fireplace, ZDO zdo) {
    if (fireplace == null || zdo == null) {
      return;
    }
    fireplace.m_infiniteFuel = true;
    zdo.Set(ZDOVars.s_fuel, Mathf.Max(1f, fireplace.m_maxFuel));
    zdo.Set(ZDOVars.s_state, 1);
  }
}

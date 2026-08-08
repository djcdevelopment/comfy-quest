namespace ComfyQuestLab;

using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>The one call every patch makes.
///
/// A postfix names the seam it hooked and describes what happened. Category and
/// usability come from <see cref="LabSeamCatalog"/>, which is generated from the event
/// atlas — so a patch cannot disagree with the atlas about what it is or whether a quest
/// can fire on it, and regenerating the atlas updates every row at once.
///
/// Everything here is wrapped. These run inside Valheim's damage, inventory and
/// building paths; a postfix that throws takes that path down with it, and a teaching
/// tool that can break someone's game is worse than no teaching tool.</summary>
public static class LabObserve {
  /// <summary>Record something the local player caused.</summary>
  public static void Seam(
      string signatureId,
      string target,
      string detail,
      UnityEngine.Object subject = null,
      string fingerprint = null,
      IReadOnlyDictionary<string, string> fields = null,
      bool evaluate = true) {
    try {
      LabEventRouter.Emit(
          signatureId,
          target,
          detail,
          LabEventRouter.Identity(subject),
          fingerprint,
          fields: fields,
          evaluate: evaluate);
    } catch (Exception) {
    }
  }

  /// <summary>Record only when the local player dealt the hit.
  ///
  /// A world of creatures fighting each other buries the console within seconds of a
  /// fight starting, and a quest can only ever be about what the player did.</summary>
  public static void PlayerHit(
      string signatureId,
      HitData hit,
      UnityEngine.Object subject,
      string target,
      string extra) {
    try {
      if (hit == null || !ComfyQuestLab.IsLocalPlayerAttacker(hit)) {
        return;
      }
      string detail = "skill " + hit.m_skill + (hit.m_ranged ? " · ranged" : string.Empty);
      if (!string.IsNullOrEmpty(extra)) {
        detail += " · " + extra;
      }
      LabEventRouter.Emit(
          signatureId,
          target,
          detail,
          LabEventRouter.Identity(subject),
          hit.m_skill.ToString(),
          hit.m_skill.ToString(),
          hit.m_ranged);
    } catch (Exception) {
    }
  }

  /// <summary>Record only when this is the local player's own character.
  ///
  /// Used by seams that are about the player rather than about a target: stamina,
  /// skills, teleports. On a multiplayer world the same method fires for every peer's
  /// character, and a builder learning what THEY just did does not want that.</summary>
  public static void LocalPlayer(
      string signatureId,
      Character who,
      string target,
      string detail,
      UnityEngine.Object subject = null,
      string fingerprint = null,
      IReadOnlyDictionary<string, string> fields = null) {
    try {
      if (who == null || Player.m_localPlayer == null || who != Player.m_localPlayer) {
        return;
      }
      LabEventRouter.Emit(
          signatureId,
          target,
          detail,
          LabEventRouter.Identity(subject == null ? who : subject),
          fingerprint,
          fields: fields);
    } catch (Exception) {
    }
  }

  /// <summary>Unity appends "(Clone)" to every spawned instance. A quest author never
  /// types that, so the lab never shows it. The rule itself lives in
  /// <see cref="LabCreatureNaming"/>, where a test can reach it.</summary>
  public static string Clean(string name) {
    return LabCreatureNaming.Clean(name);
  }
}

namespace ComfyQuestLab;

using System;

/// <summary>The rules for what a creature is called, and which of its names a quest can match on.
///
/// Split out of <see cref="LabKillWatch"/> so it can be tested. The rules themselves are pure
/// string work, but they were living beside <c>Character</c> and <c>HitData</c>, which meant the
/// load-bearing half of a bug fix could only be checked by launching Valheim and killing
/// something. That is a bad place for the one piece of logic that had already been wrong once.
///
/// <b>The bug this exists because of.</b> The lab's console used to show the GameObject name and
/// promise that <c>QuestTriggerEvaluator</c> compared against exactly that. It does not — the
/// shipping mod passes the creature's <c>m_name</c>, a localization token. For <c>Neck</c> the
/// token contains the prefab name and the promise held by luck. For <c>Greydwarf_Elite</c> the
/// token is <c>$enemy_greydwarfbrute</c> and the two share nothing, so a builder who typed what
/// the console showed them got a quest that parsed, errored nowhere, and could never fire.
///
/// Unity-free by construction — no UnityEngine, BepInEx, or game types may appear here — so it
/// links into ComfyNetworkSense.Tests and the Greydwarf case is a test rather than a ritual.
///
/// Still owed: <see cref="Normalize"/> is a deliberate copy of
/// <c>GameplayEventProducer.NormalizeCreatureName</c> (ComfyNetworkSense, ~line 327). Extracting
/// that one into a file both mods link is the real fix and is a change to the shipping mod, so it
/// carries its own note. Until then this file is where the lab's copy lives, in one place, tested.</summary>
public static class LabCreatureNaming {
  /// <summary>The name the quest matcher will actually be compared against: prefer <c>m_name</c>,
  /// fall back to the GameObject name only when it is empty, and strip Unity's <c>(Clone)</c>.
  ///
  /// Mirrors the producer's rule exactly. If these two ever disagree, the lab is teaching a rule
  /// the shipping mod does not apply, which is the whole failure mode the lab exists to prevent.</summary>
  public static string Normalize(string mName, string gameObjectName) {
    string name = string.IsNullOrWhiteSpace(mName) ? gameObjectName : mName;
    return Clean(name);
  }

  /// <summary>Unity appends "(Clone)" to every spawned instance. A quest author never types that,
  /// so the lab never shows it.</summary>
  public static string Clean(string name) {
    if (string.IsNullOrEmpty(name)) {
      return "unknown";
    }

    int marker = name.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
    return (marker >= 0 ? name.Substring(0, marker) : name).Trim();
  }

  /// <summary>True when the name a builder reads off <c>questlab_prefabs</c> is not a substring of
  /// what the matcher compares against — the case where typing the obvious thing never works.
  ///
  /// Asked in the direction the evaluator actually runs it: <c>CreatureMatches</c> tests whether
  /// the creature name contains the filter, not the reverse.</summary>
  public static bool NamesDisagree(string matcherName, string prefabName) {
    if (string.IsNullOrEmpty(matcherName) || string.IsNullOrEmpty(prefabName)) {
      return false;
    }

    return !matcherName.ToLowerInvariant().Contains(prefabName.ToLowerInvariant());
  }

  /// <summary>How a creature is shown anywhere in the lab: the matchable name, with the prefab
  /// name beside it only when the two disagree.
  ///
  /// Showing both unconditionally would be noise on the great majority of creatures, where the
  /// token already contains the prefab name. Showing only one is how the console came to promise
  /// something the matcher did not honour.</summary>
  public static string Display(string matcherName, string prefabName) {
    if (string.IsNullOrEmpty(matcherName)) {
      return string.IsNullOrEmpty(prefabName) ? "unknown" : prefabName;
    }

    return NamesDisagree(matcherName, prefabName)
        ? matcherName + " (prefab " + prefabName + ")"
        : matcherName;
  }
}

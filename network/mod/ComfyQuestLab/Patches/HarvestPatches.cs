namespace ComfyQuestLab;

using System;

using HarmonyLib;

/// <summary>Harvest seams: hitting a tree, a felled log, or a bush.
///
/// This is the worked example for every other category. Copy the shape, not the details:
/// a <c>TryPatch</c> per seam, a postfix that does nothing but describe what happened,
/// and no gameplay consequence anywhere in the file.
///
/// Why harvest is first: punching a bush was the very first trigger this project ever
/// proved, and it is the thing a new builder tries within a minute of loading in. It is
/// also the clearest demonstration of the gap the lab exists to show — the retired
/// ComfyControlSurface hooked exactly these three methods and fired quests on them, and
/// the shipping mod hooks none of them, so a bush quest authored today is silently dead.
///
/// The three targets are not interchangeable and that is the lesson:
///   TreeBase      a standing tree
///   TreeLog       the trunk after it falls — a different type, so a different hook
///   Destructible  bushes, and a great deal else besides
///
/// Source for the shape: comfy/handoffs/comfy-control-surface/Patches/QuestTriggerPatches.cs:16-21.</summary>
public static class HarvestPatches {
  public static void Apply(Harmony harmony) {
    LabPatching.TryPatch(harmony, typeof(TreeBase), "Damage", new[] { typeof(HitData) },
        nameof(TreeBaseDamagePostfix), "TreeBase.Damage");
    LabPatching.TryPatch(harmony, typeof(TreeLog), "Damage", new[] { typeof(HitData) },
        nameof(TreeLogDamagePostfix), "TreeLog.Damage");
    LabPatching.TryPatch(harmony, typeof(Destructible), "Damage", new[] { typeof(HitData) },
        nameof(DestructibleDamagePostfix), "Destructible.Damage");
  }

  static void TreeBaseDamagePostfix(TreeBase __instance, HitData __0) {
    Record("TreeBase.Damage", "tree", __instance == null ? null : __instance.name, __0);
  }

  static void TreeLogDamagePostfix(TreeLog __instance, HitData __0) {
    Record("TreeLog.Damage", "tree", __instance == null ? null : __instance.name, __0);
  }

  static void DestructibleDamagePostfix(Destructible __instance, HitData __0) {
    string prefab = __instance == null ? null : __instance.name;
    // The retired mod's rule, kept verbatim so a quest that matched there matches here:
    // a Destructible is a "bush" when its prefab name says so. Everything else stays
    // "destructible", which is honest — Destructible covers a lot of scenery.
    string kind = IsBush(prefab) ? "bush" : "destructible";
    Record("Destructible.Damage", kind, prefab, __0);
  }

  static bool IsBush(string prefabName) {
    if (string.IsNullOrEmpty(prefabName)) {
      return false;
    }
    string n = prefabName.ToLowerInvariant();
    return n.Contains("bush") || n.Contains("shrub");
  }

  /// <summary>Describe the hit and hand it to the ring. Nothing here changes the game.
  ///
  /// Only the local player counts. Watching every tree the world decides to knock over
  /// would drown the console, and a quest can only ever be about something the player
  /// did anyway.</summary>
  static void Record(string seam, string kind, string prefabName, HitData hit) {
    try {
      if (hit == null || !ComfyQuestLab.IsLocalPlayerAttacker(hit)) {
        return;
      }
      string detail = "skill " + hit.m_skill + (hit.m_ranged ? " · ranged" : string.Empty);
      ComfyQuestLab.Observe(new LabEvent(
          LabCategory.Harvest,
          seam,
          Clean(prefabName) + " (" + kind + ")",
          detail,
          // Nothing in the shipping mod hooks these, so a quest cannot fire on them
          // today no matter what the builder writes. Saying so on every row is the
          // point of the column.
          LabUsability.LabCandidate));
    } catch (Exception) {
      // A postfix that throws takes the game's damage path with it. Never let that happen.
    }
  }

  /// <summary>Unity appends "(Clone)" to every spawned instance; a quest author never
  /// types that, so the lab never shows it.</summary>
  static string Clean(string prefabName) {
    if (string.IsNullOrEmpty(prefabName)) {
      return "unknown";
    }
    int marker = prefabName.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
    return (marker >= 0 ? prefabName.Substring(0, marker) : prefabName).Trim();
  }
}

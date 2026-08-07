namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using ComfyNetworkSense;

/// <summary>What this game build actually contains, injected rather than looked up.
///
/// Every field is optional: a null one skips its checks instead of guessing. That matters
/// because the lab loads quests during <c>Awake</c>, before <c>ZNetScene</c> exists — a
/// catalog check that ran too early would report every target as unknown and teach a
/// creator to distrust the advisor.</summary>
public sealed class LabWorldFacts {
  /// <summary>Every <c>Skills.SkillType</c> name this build knows. Null skips skill checks.</summary>
  public IReadOnlyCollection<string> KnownSkills;

  /// <summary>Is there a prefab by this name? Null skips catalog checks.</summary>
  public Func<string, bool> PrefabKnown;

  /// <summary>Given a prefab name, the string the quest matcher would ACTUALLY see for it —
  /// i.e. the creature's <c>m_name</c>, which is usually a localization token. Returns null
  /// when the prefab is not a creature or is unknown. Null delegate skips the check.</summary>
  public Func<string, string> MatcherNameFor;

  /// <summary>Nothing known — every check skipped. What <c>Awake</c> uses.</summary>
  public static readonly LabWorldFacts None = new LabWorldFacts();
}

/// <summary>Lab-only advice about a quest that already parsed.
///
/// Everything here is advisory and never fatal: the quest is loaded either way. These are the
/// checks the shipping mod has no reason to run, because it consumes a file the picker wrote,
/// while the lab consumes a file a human typed.
///
/// Unity-free by construction — the game facts arrive through <see cref="LabWorldFacts"/> — so
/// this links into ComfyNetworkSense.Tests and every advisory has a test.
///
/// The rule for adding one: it must be able to fail. An advisory that fires on every quest, or
/// on none, is decoration. Nothing here emits a "looks fine" line.</summary>
public static class LabQuestAdvisor {
  /// <summary>Skills a hit can never be ranged with. Deliberately conservative — only names
  /// that cannot possibly throw or fire. Spears are absent on purpose: a spear can be thrown,
  /// so <c>projectile: true</c> with Spears is legitimate and must not be flagged.</summary>
  static readonly string[] MeleeOnlySkills = {
    "Swords", "Axes", "Clubs", "Knives", "Polearms", "Unarmed", "Pickaxes",
  };

  public static List<string> Advise(TrackedQuest quest, LabWorldFacts facts) {
    var notes = new List<string>();
    if (quest == null) {
      return notes;
    }

    facts = facts ?? LabWorldFacts.None;

    AdviseSkill(quest, facts, notes);
    AdviseTarget(quest, facts, notes);
    AdviseProjectile(quest, notes);
    AdviseShots(quest, notes);

    return notes;
  }

  static void AdviseSkill(TrackedQuest quest, LabWorldFacts facts, List<string> notes) {
    string skill = quest.TriggerWeaponSkill;
    if (string.IsNullOrWhiteSpace(skill) || facts.KnownSkills == null) {
      return;
    }

    foreach (string known in facts.KnownSkills) {
      if (string.Equals(known, skill, StringComparison.OrdinalIgnoreCase)) {
        return;
      }
    }

    // The evaluator compares weapon_skill with an exact (case-insensitive) equality, so a
    // near-miss like "Sword" never matches and never errors — it just silently never fires.
    string suggestion = NearestSkill(skill, facts.KnownSkills);
    notes.Add("weapon_skill '" + skill + "' is not a skill this game build knows"
            + (suggestion == null ? "." : " — did you mean '" + suggestion + "'?")
            + " It will never match.");
  }

  static void AdviseTarget(TrackedQuest quest, LabWorldFacts facts, List<string> notes) {
    string target = quest.TriggerTarget;
    if (string.IsNullOrWhiteSpace(target)
        || string.Equals(target, "any", StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    // The name the matcher sees is the creature's m_name (a localization token), NOT the prefab
    // name a creator reads off questlab_prefabs. For most creatures the two share a substring and
    // it works by luck; for Greydwarf_Elite / $enemy_greydwarfbrute they share nothing at all.
    // This is the single advisory most likely to save somebody an hour.
    if (facts.MatcherNameFor != null) {
      string matcherName = facts.MatcherNameFor(target);
      if (!string.IsNullOrWhiteSpace(matcherName) && !Substring(matcherName, target)) {
        notes.Add("'" + target + "' is the prefab name, but the matcher sees '" + matcherName
                + "' — they share nothing, so this will never match. Use a piece of '"
                + matcherName + "' instead.");
        return;
      }

      if (!string.IsNullOrWhiteSpace(matcherName)) {
        return;   // known creature, and the target is a substring of what the matcher sees
      }
    }

    if (facts.PrefabKnown != null && !facts.PrefabKnown(target)) {
      notes.Add("target '" + target + "' matches nothing in this world's catalog — try "
              + "questlab_prefabs " + target.ToLowerInvariant() + " to find the real name.");
    }
  }

  static void AdviseProjectile(TrackedQuest quest, List<string> notes) {
    if (!quest.TriggerProjectile || string.IsNullOrWhiteSpace(quest.TriggerWeaponSkill)) {
      return;
    }

    foreach (string melee in MeleeOnlySkills) {
      if (string.Equals(melee, quest.TriggerWeaponSkill, StringComparison.OrdinalIgnoreCase)) {
        notes.Add("projectile is true with weapon_skill '" + quest.TriggerWeaponSkill
                + "', which can never be a ranged hit — this quest cannot fire. Drop one of them.");
        return;
      }
    }
  }

  static void AdviseShots(TrackedQuest quest, List<string> notes) {
    if (quest.TriggerShots == null || quest.TriggerShots.Count == 0) {
      return;
    }

    // Straight from QuestTriggerEvaluator's own doc comment: shots only ever decided how many
    // screenshots to take, and screenshots left with the outbox. Kept on the model, no behaviour.
    notes.Add("shots are recorded but carry no behaviour — a kill quest completes on the "
            + "killing blow whether it was the first hit or the tenth.");
  }

  /// <summary>The closest known skill by shared prefix, or null when nothing is close. Cheap on
  /// purpose: "Sword" → "Swords" is the miss worth catching, and a fuzzy distance metric would
  /// start suggesting nonsense for genuinely novel input.</summary>
  static string NearestSkill(string typed, IReadOnlyCollection<string> known) {
    string best = null;
    int bestShared = 0;

    foreach (string candidate in known) {
      int shared = SharedPrefixLength(typed, candidate);
      if (shared > bestShared) {
        bestShared = shared;
        best = candidate;
      }
    }

    // Three characters is the floor: "Bow"/"Bows" clears it, and an unrelated pair rarely does.
    return bestShared >= 3 ? best : null;
  }

  static int SharedPrefixLength(string a, string b) {
    int n = Math.Min(a.Length, b.Length), i = 0;
    while (i < n && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) {
      i++;
    }
    return i;
  }

  /// <summary>The evaluator's own containment test, in the direction it actually runs:
  /// <c>CreatureMatches</c> asks whether the creature name contains the filter.</summary>
  static bool Substring(string creatureName, string filter) {
    return creatureName.ToLowerInvariant().Contains(filter.ToLowerInvariant());
  }
}

namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using ComfyNetworkSense;

/// <summary>Why a quest cannot fire in the lab, in the vocabulary the panel already speaks.</summary>
public static class LabArmed {
  /// <summary>The real evaluator matched it. A kill can fire this today.</summary>
  public const string Yes = "yes";

  /// <summary>No trigger block at all — a quest somebody checks by hand.</summary>
  public const string NoTrigger = "no-trigger";

  /// <summary>auto_checked: something outside the game decides this one.</summary>
  public const string AutoChecked = "auto-checked";

  /// <summary>venue "irl": the game will never see it.</summary>
  public const string Irl = "irl";

  /// <summary>A real in-game trigger the evaluator has no lane for. The whole lesson.</summary>
  public const string VerbNotKill = "verb-not-kill";

  /// <summary>Matched nothing and none of the above explains it. Should not happen; if it
  /// does, the contract moved and the lab should say so rather than invent a reason.</summary>
  public const string Unknown = "unknown";
}

/// <summary>One quest as the lab sees it: the contract's own <see cref="TrackedQuest"/>, plus the
/// provenance and verdicts the shipping mod has no reason to carry.</summary>
public sealed class LabQuest {
  public TrackedQuest Quest;

  /// <summary>File it came from, so a creator with four drafts open knows which to edit.</summary>
  public string SourceFile;

  /// <summary>One of <see cref="LabArmed"/>.</summary>
  public string Armed;

  /// <summary>Advice, never fatal. Empty is the normal case.</summary>
  public readonly List<string> Advisories = new List<string>();

  /// <summary>Times this quest fired since the last reload. Reset on purpose: the question a
  /// creator is asking is "did my edit work", not "how many all session".</summary>
  public int Fires;

  public bool IsArmed { get { return Armed == LabArmed.Yes; } }

  public string QuestId { get { return Quest == null ? null : Quest.QuestId; } }

  /// <summary>The honest one-line answer to "can this fire?", in <c>LabPanel.UsabilityLine</c>'s
  /// register. Names the creator's actual trigger verb rather than a generic complaint.</summary>
  public string ArmedLine() {
    switch (Armed) {
      case LabArmed.Yes:
        return "this quest can fire in the lab today";
      case LabArmed.NoTrigger:
        return "no trigger — this quest is checked by hand, not by the game";
      case LabArmed.AutoChecked:
        return "marked auto_checked, so something outside the game checks it";
      case LabArmed.Irl:
        return "an IRL quest; the game will never fire it";
      case LabArmed.VerbNotKill:
        return "parsed fine — but nothing binds a '" + (Quest == null ? "?" : Quest.TriggerEvent)
             + "' trigger to a quest today; only a creature kill can fire one";
      default:
        return "the evaluator matched nothing here, and the usual reasons do not explain it";
    }
  }
}

/// <summary>A file that would not parse, with the contract's own words and a lab-side remedy.</summary>
public sealed class LabQuestFileError {
  public string SourceFile;

  /// <summary>Verbatim from <see cref="QuestViewLoader.Parse"/>. Never reworded — see the class
  /// note on <see cref="LabQuestSet"/>.</summary>
  public string ContractMessage;

  /// <summary>What the lab suggests doing about it. Ours to write, kept separate from the
  /// contract's sentence so a creator can tell who is talking.</summary>
  public string Remedy;
}

/// <summary>The lab's quest set: every <c>*.json</c> under the quest directory, parsed
/// independently, with an armed verdict per quest.
///
/// Unity-free by construction — no UnityEngine, BepInEx, or game types — so it links into
/// ComfyNetworkSense.Tests and a change here is provable in seconds without a game install.
/// <see cref="Build"/> takes file CONTENTS rather than paths for exactly that reason; disk IO
/// lives in <c>LabQuestEngine</c>.
///
/// Two decisions worth knowing before changing anything here:
///
/// <b>It calls <see cref="QuestViewLoader.Parse"/>, never <c>Load</c>.</b> <c>Load</c> keeps one
/// slot of static state and clears the whole tracked set on any exception, so three good drafts
/// plus one typo'd draft would leave a creator with zero quests and one error. Per-file
/// <c>Parse</c> means a bad file loses only itself. (The two mods do not actually share those
/// statics — same type name, two assemblies, separate CLR types — but code that looks like it
/// shares state is worse than code that doesn't.)
///
/// <b>Armed state comes from dry-firing the real evaluator</b>, not from a predicate that
/// restates its rules. A mirror predicate is the thing that drifts from the contract silently,
/// and the lab's entire promise is "fires here ⇒ fires there". See <see cref="ProbeArmed"/>.
///
/// Contract messages are passed through untouched. Rewording them is how the lab would start
/// lying about the shipping mod; the lab's own advice is appended as a separate sentence.</summary>
public sealed class LabQuestSet {
  public readonly List<LabQuest> Quests = new List<LabQuest>();
  public readonly List<LabQuestFileError> Errors = new List<LabQuestFileError>();

  /// <summary>Files that parsed, in the order they were handed over. Used by the reload diff so
  /// "nothing changed" can be told apart from "the directory disappeared".</summary>
  public readonly List<string> FilesRead = new List<string>();

  public int ArmedCount {
    get {
      int n = 0;
      foreach (LabQuest q in Quests) {
        if (q.IsArmed) {
          n++;
        }
      }
      return n;
    }
  }

  public int AdvisoryCount {
    get {
      int n = 0;
      foreach (LabQuest q in Quests) {
        n += q.Advisories.Count;
      }
      return n;
    }
  }

  /// <summary>Parse every file independently. <paramref name="advise"/> is the per-quest advisory
  /// pass — null skips it, which is what the pure parse tests want. A file whose text throws is
  /// recorded in <see cref="Errors"/> and costs only its own quests.</summary>
  public static LabQuestSet Build(
      IEnumerable<KeyValuePair<string, string>> files,
      Func<TrackedQuest, IEnumerable<string>> advise = null) {
    var set = new LabQuestSet();
    if (files == null) {
      return set;
    }

    foreach (KeyValuePair<string, string> file in files) {
      string name = file.Key;
      set.FilesRead.Add(name);

      List<TrackedQuest> parsed;
      try {
        parsed = QuestViewLoader.Parse(file.Value, out string _);
      } catch (Exception ex) {
        // InvalidOperationException is the contract's own signal; anything else here is a
        // surprise, and a creator is better served by seeing it than by a swallowed file.
        set.Errors.Add(new LabQuestFileError {
          SourceFile = name,
          ContractMessage = ex.Message,
          // Parse throws on the FIRST bad quest in a file, so one report does not mean one
          // problem. Saying so is cheaper than letting somebody fix one thing and assume they
          // are done. Widening Parse to collect every error is a change to the shared contract.
          Remedy = "This file has at least one problem — fix it and reload to see the next. "
                 + "Its quests are not loaded; the other files are unaffected.",
        });
        continue;
      }

      // The contract's parser is regex-and-brace based, not a JSON validator: a trailing comma
      // or an unbalanced brace can silently yield FEWER quests than were written, with no error
      // at all. This is the one check that catches that, and it can genuinely fail.
      int declared = CountOccurrences(file.Value, "\"quest_id\"");
      if (declared > parsed.Count) {
        set.Errors.Add(new LabQuestFileError {
          SourceFile = name,
          ContractMessage = "This file names " + declared + " quests but only " + parsed.Count
                          + " parsed.",
          Remedy = "Something between them is malformed — a trailing comma or an unbalanced "
                 + "brace will do it. The quests that did parse are loaded.",
        });
      }

      foreach (TrackedQuest quest in parsed) {
        var lab = new LabQuest { Quest = quest, SourceFile = name, Armed = ProbeArmed(quest) };
        if (advise != null) {
          foreach (string note in advise(quest)) {
            lab.Advisories.Add(note);
          }
        }
        set.Quests.Add(lab);
      }
    }

    set.FlagDuplicateIds();
    return set;
  }

  /// <summary>Two files claiming one quest_id is a cross-file fact, so it is found here rather
  /// than in the per-quest advisor. Nothing dedupes — both stay loaded and both would fire —
  /// so the advisory says what actually happens rather than what a creator might assume.</summary>
  void FlagDuplicateIds() {
    var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (LabQuest q in Quests) {
      if (string.IsNullOrWhiteSpace(q.QuestId)) {
        continue;
      }

      if (seen.TryGetValue(q.QuestId, out string first)) {
        q.Advisories.Add("quest_id '" + q.QuestId + "' is also in " + first
                       + " — both are loaded, and both will fire.");
      } else {
        seen[q.QuestId] = q.SourceFile;
      }
    }
  }

  /// <summary>Ask the real matcher whether any kill could ever satisfy this quest, by echoing the
  /// quest's own filters back at a throwaway zero-cooldown evaluator.
  ///
  /// <c>CreatureMatches(filter, filter)</c> is a substring of itself; a skill equals itself; and
  /// <c>ranged: true</c> satisfies both states of <c>TriggerProjectile</c> (the evaluator only
  /// rejects a projectile quest on a melee kill, never the reverse). So an empty result means no
  /// kill can EVER fire this quest — not that this particular probe missed.
  ///
  /// The point of doing it this way: zero duplicated matching logic. If the evaluator's rules
  /// change, this answer changes with them instead of drifting into a comfortable lie.</summary>
  static string ProbeArmed(TrackedQuest quest) {
    if (quest == null) {
      return LabArmed.Unknown;
    }

    if (Fires(quest)) {
      return LabArmed.Yes;
    }

    // Order matters: forcing the verb below would make a trigger-less quest look like a verb
    // problem, so the shapes that have nothing to do with the verb are ruled out first.
    if (!quest.HasTrigger) {
      return LabArmed.NoTrigger;
    }

    if (quest.AutoChecked) {
      return LabArmed.AutoChecked;
    }

    if (!quest.IsCapturable) {
      return LabArmed.Irl;
    }

    // One ablation, and it is the interesting one: same quest, verb forced to "kill". If it fires
    // now, the verb was the only thing standing in the way — which is the lab's central lesson,
    // and worth stating by name rather than as "no".
    if (Fires(WithKillVerb(quest))) {
      return LabArmed.VerbNotKill;
    }

    return LabArmed.Unknown;
  }

  static bool Fires(TrackedQuest quest) {
    var probe = new QuestTriggerEvaluator(0.0);
    IReadOnlyList<QuestCompletion> hits = probe.OnCreatureKilled(
        new[] { quest },
        string.IsNullOrWhiteSpace(quest.TriggerTarget) ? "any" : quest.TriggerTarget,
        quest.TriggerWeaponSkill,
        true,
        0.0);
    return hits.Count > 0;
  }

  /// <summary>A shallow copy with the trigger verb forced to "kill". Only ever fed to
  /// <see cref="Fires"/> — it must never escape into the loaded set.</summary>
  static TrackedQuest WithKillVerb(TrackedQuest quest) {
    return new TrackedQuest {
      QuestId = quest.QuestId,
      Name = quest.Name,
      Guild = quest.Guild,
      Era = quest.Era,
      Category = quest.Category,
      BotCommand = quest.BotCommand,
      AutoChecked = quest.AutoChecked,
      Venue = quest.Venue,
      TriggerEvent = "kill",
      TriggerTarget = quest.TriggerTarget,
      TriggerWeaponSkill = quest.TriggerWeaponSkill,
      TriggerProjectile = quest.TriggerProjectile,
      TriggerShots = quest.TriggerShots,
    };
  }

  static int CountOccurrences(string haystack, string needle) {
    if (string.IsNullOrEmpty(haystack)) {
      return 0;
    }

    int count = 0, i = 0;
    while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) {
      count++;
      i += needle.Length;
    }
    return count;
  }

  // ---- reload diff -----------------------------------------------------------------------

  /// <summary>What changed between two loads, named quest by quest.
  ///
  /// This is what makes a hot-reload trustworthy. "Reloaded" alone does not tell a creator that
  /// the file they just saved is the file the lab just read — a diff naming their quest does.</summary>
  public List<string> DiffFrom(LabQuestSet previous) {
    var lines = new List<string>();
    var before = new Dictionary<string, LabQuest>(StringComparer.OrdinalIgnoreCase);
    if (previous != null) {
      foreach (LabQuest q in previous.Quests) {
        if (!string.IsNullOrWhiteSpace(q.QuestId)) {
          before[q.QuestId] = q;
        }
      }
    }

    int unchanged = 0;
    foreach (LabQuest now in Quests) {
      if (string.IsNullOrWhiteSpace(now.QuestId)) {
        continue;
      }

      if (!before.TryGetValue(now.QuestId, out LabQuest was)) {
        lines.Add("+ " + now.QuestId);
        continue;
      }

      before.Remove(now.QuestId);
      string change = Changed(was, now);
      if (change == null) {
        unchanged++;
      } else {
        lines.Add("~ " + now.QuestId + " (" + change + ")");
      }
    }

    foreach (KeyValuePair<string, LabQuest> gone in before) {
      lines.Add("- " + gone.Key);
    }

    if (unchanged > 0) {
      lines.Add("= " + unchanged + " unchanged");
    }

    return lines;
  }

  /// <summary>What a creator actually changed, or null when nothing they can see moved. Only the
  /// fields that decide whether and when a quest fires — a reworded name is not a change worth
  /// a line in a reload report.</summary>
  static string Changed(LabQuest was, LabQuest now) {
    TrackedQuest a = was.Quest, b = now.Quest;
    if (!Same(a.TriggerEvent, b.TriggerEvent)
        || !Same(a.TriggerTarget, b.TriggerTarget)
        || !Same(a.TriggerWeaponSkill, b.TriggerWeaponSkill)
        || a.TriggerProjectile != b.TriggerProjectile) {
      return "trigger changed";
    }

    if (was.Armed != now.Armed) {
      return now.IsArmed ? "now armed" : "no longer armed";
    }

    if (!Same(was.SourceFile, now.SourceFile)) {
      return "moved to " + now.SourceFile;
    }

    return null;
  }

  static bool Same(string a, string b) {
    return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
  }
}

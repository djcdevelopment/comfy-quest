namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using ComfyNetworkSense;

using UnityEngine;

/// <summary>The lab's quest lane: read the quest directory, hold the loaded set, and let a kill
/// ask the real evaluator whether anything fires.
///
/// Deliberately thin. Everything worth testing lives in <see cref="LabQuestSet"/> and
/// <see cref="LabQuestAdvisor"/>, which are Unity-free and covered; what is left here is disk IO,
/// evaluator lifetime, and talking to the HUD — the parts that need a running game anyway.
///
/// <b>The lab never reads the shipping mod's quest-view.json.</b> Its whole claim is that
/// uninstalling it changes nothing, and reaching into <c>comfy-network-sense/</c> would make that
/// false. Files here are whole quest-view documents precisely so a creator can copy one across
/// themselves when they are ready.</summary>
public static class LabQuestEngine {
  static LabQuestSet _set = new LabQuestSet();
  static QuestTriggerEvaluator _evaluator = new QuestTriggerEvaluator(60.0);
  static string _lastKillLine;
  static List<string> _lastReport = new List<string>();

  /// <summary>The loaded set. Never null, even before the first load.</summary>
  public static LabQuestSet Set { get { return _set; } }

  /// <summary>The last kill the evaluator was actually given, exactly as it was given.
  ///
  /// The single most useful line in the panel: it turns "why didn't my quest fire" from a guess
  /// into a read, because the three strings the matcher compared are right there.</summary>
  public static string LastKillLine { get { return _lastKillLine; } }

  /// <summary>Diff, failures and advisories from the last reload, for the Quests tab.</summary>
  public static List<string> LastReport { get { return _lastReport; } }

  public static string QuestDir {
    get {
      return Path.Combine(BepInEx.Paths.ConfigPath,
          Path.Combine("comfy-quest-lab", "quests"));
    }
  }

  // ---- loading ---------------------------------------------------------------------------

  /// <summary>Re-read every quest file and re-arm. Returns the one-line summary for the HUD; the
  /// detail goes to <see cref="LastReport"/> and the log, because a multi-line block pushed
  /// through MessageHud is unreadable.</summary>
  public static string Reload() {
    // Wrapped whole, and this is not belt-and-braces: Awake calls Reload, so anything that
    // escapes here costs the entire mod rather than the quest lane -- no console, no panel, no
    // seams, on a plugin whose whole job is to be the thing that does not break your game. The
    // file a creator hand-edited is the least trustworthy input in the build, and it is read
    // before there is any UI to report a failure through.
    try {
      return ReloadCore();
    } catch (Exception ex) {
      _set = new LabQuestSet();
      _evaluator = new QuestTriggerEvaluator(CooldownSeconds());
      _lastReport = new List<string> {
        "the quest lane failed to load: " + ex.Message,
        "The rest of the lab is unaffected. Fix the file and run lab_reload.",
      };
      ComfyQuestLab.LogInfo("[quests] reload failed: " + ex);
      return "quest reload failed — see the Quests tab.";
    }
  }

  static string ReloadCore() {
    LabQuestSet previous = _set;
    var report = new List<string>();

    List<KeyValuePair<string, string>> files;
    try {
      files = ReadQuestFiles();
    } catch (Exception ex) {
      _lastReport = new List<string> { "could not read " + QuestDir + ": " + ex.Message };
      return "quest reload failed — see the log.";
    }

    LabWorldFacts facts = WorldFacts();
    _set = LabQuestSet.Build(files, quest => LabQuestAdvisor.Advise(quest, facts));

    // A fresh evaluator, which drops every cooldown. This is a DELIBERATE divergence from the
    // shipping mod, where a 60 s cooldown persists for the session: a creator who fires a quest,
    // edits it, and then has to wait a minute to retest has had exactly the flow destroyed that
    // lab_reload exists to protect. Reloading is an authoring act, not a gameplay one.
    _evaluator = new QuestTriggerEvaluator(CooldownSeconds());
    LabKillWatch.Clear();

    foreach (string line in _set.DiffFrom(previous)) {
      report.Add(line);
    }

    foreach (LabQuestFileError error in _set.Errors) {
      report.Add(error.SourceFile + ": " + error.ContractMessage);
      report.Add("   " + error.Remedy);
    }

    foreach (LabQuest quest in _set.Quests) {
      foreach (string note in quest.Advisories) {
        report.Add(quest.QuestId + ": " + note);
      }
    }

    if (files.Count == 0) {
      report.Add("no quest files yet. Drop a quest-view.json into " + QuestDir
               + ", or run lab_setup to get a starter one.");
    }

    _lastReport = report;
    foreach (string line in report) {
      ComfyQuestLab.LogInfo("[quests] " + line);
    }

    return Summary();
  }

  /// <summary>One line, for the HUD and the event console.</summary>
  public static string Summary() {
    var sb = new StringBuilder();
    sb.Append("quest set reloaded: ")
      .Append(_set.Quests.Count).Append(_set.Quests.Count == 1 ? " quest, " : " quests, ")
      .Append(_set.ArmedCount).Append(" armed");

    int problems = _set.Errors.Count + _set.AdvisoryCount;
    if (problems > 0) {
      sb.Append(", ").Append(problems).Append(problems == 1 ? " problem" : " problems");
    }

    sb.Append(" — see the Quests tab");
    return sb.ToString();
  }

  static List<KeyValuePair<string, string>> ReadQuestFiles() {
    var files = new List<KeyValuePair<string, string>>();
    if (!Directory.Exists(QuestDir)) {
      return files;
    }

    string[] paths = Directory.GetFiles(QuestDir, "*.json");
    Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
    foreach (string path in paths) {
      try {
        files.Add(new KeyValuePair<string, string>(
            Path.GetFileName(path), File.ReadAllText(path, Encoding.UTF8)));
      } catch (Exception ex) {
        // An unreadable file is a fact about that file, not a reason to lose the others.
        ComfyQuestLab.LogInfo("[quests] could not read " + path + ": " + ex.Message);
      }
    }

    return files;
  }

  // ---- the kill lane ---------------------------------------------------------------------

  /// <summary>A creature died. If the local player landed the fatal blow, ask the real evaluator
  /// what completes, and record what it was asked either way.
  ///
  /// Called from <c>CombatPatches.OnDeathPostfix</c> after the seam row, so a quest firing reads
  /// as a consequence of the kill rather than as an unrelated event.</summary>
  public static void OnKill(Character victim) {
    try {
      if (!LabConfig.QuestsEnabled.Value) {
        return;
      }

      double now = Time.realtimeSinceStartup;
      if (!LabKillWatch.TryTakeKill(victim, now, out LabKill kill)) {
        return;   // not the player's kill; nothing to explain
      }

      IReadOnlyList<QuestCompletion> completions = _evaluator.OnCreatureKilled(
          _set.Quests.Count == 0 ? EmptyQuests : TrackedQuests(),
          kill.Creature, kill.WeaponSkill, kill.Ranged, now);

      _lastKillLine = DateTime.Now.ToString("HH:mm:ss") + " · " + kill.Display + " · "
          + kill.WeaponSkill + " · " + (kill.Ranged ? "ranged" : "melee") + " → "
          + (completions.Count == 0 ? "matched nothing" : "fired " + completions.Count);

      foreach (QuestCompletion completion in completions) {
        Credit(completion, kill);
      }
    } catch (Exception) {
      // A postfix that throws takes Valheim's death path down with it.
    }
  }

  static readonly TrackedQuest[] EmptyQuests = new TrackedQuest[0];

  static List<TrackedQuest> TrackedQuests() {
    var quests = new List<TrackedQuest>(_set.Quests.Count);
    foreach (LabQuest quest in _set.Quests) {
      quests.Add(quest.Quest);
    }
    return quests;
  }

  /// <summary>Count the fire and put one row in the console beside the death that caused it.</summary>
  static void Credit(QuestCompletion completion, LabKill kill) {
    foreach (LabQuest quest in _set.Quests) {
      if (string.Equals(quest.QuestId, completion.QuestId, StringComparison.OrdinalIgnoreCase)) {
        quest.Fires++;
      }
    }

    // Category is chosen here rather than looked up: LabSeamCatalog.Category() answers Combat for
    // any id it does not know, so routing "quest.fired" through it would be right by accident.
    // A quest firing on a kill IS combat — but it should say so on purpose.
    ComfyQuestLab.Observe(new LabEvent(
        LabCategory.Combat,
        "quest.fired",
        completion.Name,
        "kill " + kill.Display + " · " + kill.WeaponSkill,
        LabUsability.Today));

    ComfyQuestLab.Report("quest fired: " + completion.Name);
  }

  static double CooldownSeconds() {
    return LabConfig.QuestCooldownSeconds == null ? 60.0 : LabConfig.QuestCooldownSeconds.Value;
  }

  /// <summary>Swap in an evaluator carrying the new cooldown, keeping the loaded set.
  ///
  /// Wired to SettingChanged for the same reason the rune lamps are: the useful form of a knob
  /// about timing is one you turn while watching the thing, not one that needs a reload.</summary>
  public static void RetuneCooldown() {
    _evaluator = new QuestTriggerEvaluator(CooldownSeconds());
  }

  public static double CooldownRemaining(string questId) {
    try {
      return _evaluator.CooldownRemaining(questId, Time.realtimeSinceStartup);
    } catch (Exception) {
      return 0.0;
    }
  }

  // ---- world facts -------------------------------------------------------------------------

  /// <summary>What this game build knows, snapshotted once per reload.
  ///
  /// Returns <see cref="LabWorldFacts.None"/> before the world exists — during Awake there is no
  /// ZNetScene, and a catalog check that ran anyway would report every target as unknown and
  /// teach a creator to ignore the advisor.</summary>
  static LabWorldFacts WorldFacts() {
    try {
      string[] skills = Enum.GetNames(typeof(Skills.SkillType));
      if (ZNetScene.instance == null) {
        return new LabWorldFacts { KnownSkills = skills };
      }

      var prefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var creatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (GameObject prefab in ZNetScene.instance.m_prefabs) {
        if (prefab == null) {
          continue;
        }

        prefabs.Add(prefab.name);
        Character character = prefab.GetComponent<Character>();
        if (character != null && !creatures.ContainsKey(prefab.name)) {
          creatures[prefab.name] = LabKillWatch.MatcherName(character);
        }
      }

      return new LabWorldFacts {
        KnownSkills = skills,
        PrefabKnown = name => ContainsMatch(prefabs, name),
        MatcherNameFor = name => LookupMatcherName(creatures, name),
      };
    } catch (Exception) {
      return LabWorldFacts.None;
    }
  }

  /// <summary>The evaluator matches on substrings, so the catalog check has to as well —
  /// "greydwarf" is a legitimate target that no prefab is named exactly.</summary>
  static bool ContainsMatch(HashSet<string> names, string needle) {
    if (names.Contains(needle)) {
      return true;
    }

    string lower = needle.ToLowerInvariant();
    foreach (string name in names) {
      if (name.ToLowerInvariant().Contains(lower)) {
        return true;
      }
    }
    return false;
  }

  /// <summary>The matcher-visible name for a target, preferring an exact prefab hit so a creator
  /// who typed a full prefab name gets an answer about that prefab and not a longer one that
  /// happens to contain it.</summary>
  static string LookupMatcherName(Dictionary<string, string> creatures, string needle) {
    if (creatures.TryGetValue(needle, out string exact)) {
      return exact;
    }

    string lower = needle.ToLowerInvariant();
    foreach (KeyValuePair<string, string> entry in creatures) {
      if (entry.Key.ToLowerInvariant().Contains(lower)) {
        // A creature whose matcher name already contains what they typed is working as intended;
        // only report the one that does not, so the advisory stays a real finding.
        return entry.Value.ToLowerInvariant().Contains(lower) ? null : entry.Value;
      }
    }

    return null;
  }
}

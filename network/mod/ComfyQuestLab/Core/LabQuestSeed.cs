namespace ComfyQuestLab;

using System.IO;
using System.Text;

/// <summary>The starter quest file <c>lab_setup</c> leaves behind, and the rule for writing it.
///
/// <b>The seed is a lesson, not a template.</b> It holds exactly two quests and they are chosen
/// to disagree with each other:
///
///   first_blood    kill / Greyling        → ARMED, and it targets the creature standing under
///                                            the combat monument the gallery just raised. No
///                                            filters, so any kill fires it on the first try;
///                                            the requirements text names adding weapon_skill as
///                                            the next edit to make.
///   punchwood      hit / tree_or_bush     → NOT ARMED, on purpose. The quest loader accepts
///                                            "hit" without complaint and QuestTriggerEvaluator
///                                            matches "kill" only, so a bush quest produces no
///                                            error and no event. That trap is what the mod's
///                                            README opens with, and reading about it teaches
///                                            far less than opening the file it happened in.
///
/// So a creator's first launch shows one quest that works, one that silently cannot, and the
/// panel explaining the difference — against real files they can edit rather than prose.
///
/// <b>The armed quest must target something the gallery supplies.</b> It first targeted a Neck,
/// lifted from the test fixture because that was provably schema-valid — and the note here even
/// said "a Neck is a swim away" while shipping it anyway. A creator's first act would have been
/// to leave the practice ground they had just raised and go find a shoreline. Building hallways
/// and stations exists precisely so nobody has to go hunting for the thing their quest is about;
/// a seed that sends them hunting cancels the gallery. If this changes again, it changes to
/// something <see cref="LabGalleryBuilder.Restock"/> can put back in front of them.
///
/// Unity-free by construction, so it links into the test project.</summary>
public static class LabQuestSeed {
  public const string FileName = "starter.json";

  /// <summary>A complete quest-view.json — not a fragment. Any file in the lab's quest directory
  /// can be copied byte-for-byte to <c>BepInEx/config/comfy-network-sense/quest-view.json</c> and
  /// the shipping mod will accept it unchanged. That round trip is the lab's whole promise, and a
  /// lab-specific format would quietly break it.</summary>
  public const string Text = """
  {
    "schema_version": 1,
    "player": { "name": "you", "discord": null },
    "created_at": "2026-08-07T00:00:00Z",
    "picker_version": 1,
    "quests": [
      {
        "quest_id": "first_blood",
        "name": "First Blood",
        "category": "Starter",
        "requirements": "Kill the Greyling standing under the combat monument. This one is ARMED - open the Quests tab (F6) and you will see it say so. Killed it already? Type lab_target for a fresh one; you never have to go hunting. Next edit to try: add \"weapon_skill\": \"Unarmed\" to the trigger, run lab_reload, and watch it stop firing unless you punch.",
        "bot_command": "/comfy test summons_type:First Blood image:",
        "auto_checked": false,
        "venue": "in_game",
        "trigger": { "event": "kill", "target": "Greyling" },
        "guild": "Starter",
        "era": 17
      },
      {
        "quest_id": "punchwood",
        "name": "Punchwood",
        "category": "Starter",
        "requirements": "Punch a tree with your bare hands. This one is NOT armed, deliberately. Nothing binds a 'hit' trigger to a quest today, so it will never fire - and nothing errors to tell you. That is the trap this lab exists to make visible. Change event to 'kill' and target to 'Greyling', then run lab_reload.",
        "_note": "Every station in the gallery is one lab_target away from being replaced, so nothing here needs you to go looking for it.",
        "bot_command": "/comfy test summons_type:Punchwood image:",
        "auto_checked": false,
        "venue": "in_game",
        "trigger": { "event": "hit", "target": "tree_or_bush", "weapon_skill": "Unarmed" },
        "guild": "Starter",
        "era": 17
      }
    ]
  }
  """;

  /// <summary>Write the seed only into a directory that holds no <c>*.json</c> at all.
  ///
  /// Never overwrites, and deliberately does not check for <c>starter.json</c> specifically: a
  /// creator who renamed their drafts should not get a starter file back, and one who deleted
  /// the starter on purpose should not have it reappear on the next <c>lab_setup</c>.
  ///
  /// Returns what happened, for the console. Failures are reported, not thrown — a read-only
  /// config directory should cost the seed, not the gallery.</summary>
  public static string EnsureSeeded(string questDir) {
    try {
      if (!Directory.Exists(questDir)) {
        Directory.CreateDirectory(questDir);
      } else if (Directory.GetFiles(questDir, "*.json").Length > 0) {
        return null;   // the creator's files are already here; say nothing
      }

      string path = Path.Combine(questDir, FileName);
      File.WriteAllText(path, Text, new UTF8Encoding(false));
      return "wrote a starter quest file: " + path;
    } catch (System.Exception ex) {
      return "could not write the starter quest file: " + ex.Message;
    }
  }
}

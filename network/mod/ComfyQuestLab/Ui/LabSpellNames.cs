namespace ComfyQuestLab;

/// <summary>The plain name for a true name.
///
/// The live view shows what happened in the same words the tome uses, so a student can
/// recognise a row without first learning to read method signatures. The mapping already
/// exists in the generated tome; this only reaches into it.
///
/// A spell with no page falls back to its true name, which is honest — an unnamed thing
/// is one nobody has written about yet, not one to hide.</summary>
public static class LabSpellNames {
  /// <summary>Rows the lab itself emits, which have no seam and so no page in the generated
  /// tome. Kept as an override rather than pushed into the generator because these are not
  /// things the world did — they are things the lab did about your quest files, and inventing
  /// an atlas entry for them would put a lie in the seam catalog.</summary>
  static readonly string[,] LabRows = {
    { "quest.fired", "a quest fired" },
    { "quest.reloaded", "your quest files were re-read" },
  };

  public static string For(string trueName) {
    for (int i = 0; i < LabRows.GetLength(0); i++) {
      if (LabRows[i, 0] == trueName) {
        return LabRows[i, 1];
      }
    }

    foreach (LabJournal.Page page in LabJournal.Pages) {
      foreach (LabJournal.Spell spell in page.Spells) {
        if (spell.TrueName == trueName) {
          return spell.Name;
        }
      }
    }
    return trueName;
  }
}

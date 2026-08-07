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
  public static string For(string trueName) {
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

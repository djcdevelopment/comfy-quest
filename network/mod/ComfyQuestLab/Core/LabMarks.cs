namespace ComfyQuestLab;

using System;

/// <summary>The one question every world-changing lane must answer before touching an
/// object: is this ours?
///
/// Ownership is read from a mark in the piece's own ZDO, never from a remembered id —
/// ZDO ids are session-scoped and trusting one across a reload is how a clear destroys
/// somebody's player. The gallery carries a boolean-ish mark; blueprint pieces carry the
/// blueprint's name, so a clear can be scoped to one build. Anything that must treat
/// every lab-built piece alike (the WearNTear keep-standing patch) asks
/// <see cref="IsLabBuilt"/> instead of enumerating lanes.</summary>
public static class LabMarks {
  /// <summary>ZDO string field on every blueprint-built piece; the value is the
  /// blueprint name the operator typed, lowercased, so clear is per-build.</summary>
  public const string BlueprintMark = "comfyQuestLabBlueprint";

  public static bool IsBlueprintPiece(ZDO zdo) {
    return BlueprintName(zdo).Length > 0;
  }

  /// <summary>Which blueprint placed this piece, or "" for not-ours.</summary>
  public static string BlueprintName(ZDO zdo) {
    if (zdo == null) {
      return string.Empty;
    }
    try {
      return zdo.GetString(BlueprintMark, string.Empty);
    } catch (Exception) {
      return string.Empty;
    }
  }

  /// <summary>Did any lab lane place this? The keep-standing patch keys off this, so a
  /// new lane that marks its pieces gets its cantilevers held up for free.</summary>
  public static bool IsLabBuilt(ZDO zdo) {
    return LabGalleryBuilder.IsGalleryPiece(zdo) || IsBlueprintPiece(zdo);
  }
}

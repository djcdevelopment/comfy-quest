namespace ComfyQuestLab;

using System;

/// <summary>Which classified integration profiles a live Quest Lab session enables.</summary>
public static class LabRuntimeProfile {
  public const string Core = "core";
  public const string Extended = "extended";
  public const string Diagnostic = "diagnostic";

  public static bool Allows(string configured, string required) {
    string active = Normalize(configured);
    string need = (required ?? string.Empty).Trim().ToLowerInvariant();
    if (need == Core) {
      return true;
    }
    if (need == Extended) {
      return active == Extended || active == Diagnostic;
    }
    if (need == Diagnostic) {
      return active == Diagnostic;
    }
    return false; // disabled and unknown profiles fail closed
  }

  public static string Normalize(string configured) {
    if (string.Equals(configured, Core, StringComparison.OrdinalIgnoreCase)) {
      return Core;
    }
    if (string.Equals(configured, Diagnostic, StringComparison.OrdinalIgnoreCase)) {
      return Diagnostic;
    }
    return Extended;
  }
}

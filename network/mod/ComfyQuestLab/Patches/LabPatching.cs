namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.Reflection;

using HarmonyLib;

/// <summary>Apply one patch, and survive not being able to.
///
/// The lab reaches into far more of the game than the shipping mod does — that is the
/// whole idea — which means it is far more exposed to a game update renaming or removing
/// something. A mod that throws on load because one seam of ninety-one moved is useless
/// to the person it was built for.
///
/// So every patch is attempted independently and every outcome is recorded. The result
/// is a roster the lab can show in-game: a builder who wonders why bushes are silent
/// gets "Destructible.Damage — unavailable on this game build" instead of nothing.</summary>
public static class LabPatching {
  public struct Outcome {
    public string Label;
    public bool Applied;
    public string Detail;
  }

  static readonly List<Outcome> _outcomes = new();
  static readonly Dictionary<MethodBase, string> _signatureIds = new();

  public static IReadOnlyList<Outcome> Outcomes { get { return _outcomes; } }

  public static int AppliedCount {
    get {
      int n = 0;
      foreach (Outcome o in _outcomes) {
        if (o.Applied) {
          n++;
        }
      }
      return n;
    }
  }

  public static string SignatureFor(MethodBase method) {
    if (method != null && _signatureIds.TryGetValue(method, out string signatureId)) {
      return signatureId;
    }
    return method == null
        ? "unknown"
        : method.DeclaringType.Name + "." + method.Name;
  }

  /// <summary>Patch <paramref name="declaringType"/>.<paramref name="methodName"/> with a
  /// postfix named <paramref name="postfixName"/> on the calling patch class.
  ///
  /// Argument types are explicit because overloads are the norm, not the exception:
  /// Inventory.AddItem has seven and MineRock5.RPC_Damage takes an argument its siblings
  /// do not. "Patch AddItem" is not a specification.</summary>
  public static void TryPatch(
      Harmony harmony,
      Type declaringType,
      string methodName,
      Type[] argumentTypes,
      string postfixName,
      string label) {
    try {
      if (declaringType == null) {
        Record(label, false, "type not present on this game build");
        return;
      }

      MethodInfo target = AccessTools.Method(declaringType, methodName, argumentTypes);
      if (target == null) {
        Record(label, false, "method not found — signature may have changed");
        return;
      }

      // The postfix lives on the class that called us, which keeps each category's
      // patches and their handlers in one readable file.
      Type owner = new System.Diagnostics.StackFrame(1).GetMethod().DeclaringType;
      MethodInfo postfix = AccessTools.Method(owner, postfixName);
      if (postfix == null) {
        Record(label, false, "postfix " + postfixName + " not found on " + owner);
        return;
      }

      harmony.Patch(target, postfix: new HarmonyMethod(postfix));
      _signatureIds[target] = label;
      Record(label, true, "ok");
    } catch (Exception ex) {
      Record(label, false, ex.Message);
    }
  }

  static void Record(string label, bool applied, string detail) {
    _outcomes.Add(new Outcome { Label = label, Applied = applied, Detail = detail });
  }
}

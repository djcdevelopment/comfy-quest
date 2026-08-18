namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using System.Reflection;
using ComfyQuestContracts;
using HarmonyLib;

/// <summary>Applies exact overloads independently so one moved seam cannot disable Runtime.</summary>
static class RuntimePatching {
  public readonly struct Outcome {
    public string SignatureId { get; }
    public bool Applied { get; }
    public string Detail { get; }
    public Outcome(string signatureId, bool applied, string detail) {
      SignatureId=signatureId; Applied=applied; Detail=detail;
    }
  }

  static readonly List<Outcome> outcomes = new();
  public static IReadOnlyList<Outcome> Outcomes => outcomes;

  public static void TryPatch(Harmony harmony, Type declaringType, string methodName,
      Type[] arguments, Type owner, string postfixName, string signatureId) {
    if (!RuntimeWitnessCatalog.TryGet(signatureId, out _)) {
      Record(signatureId, false, "signature absent from generated creator-safe witness catalog");
      return;
    }
    TryPatchCore(harmony, declaringType, methodName, arguments, owner, null, postfixName, signatureId);
  }

  public static void TryPatchWithState(Harmony harmony, Type declaringType, string methodName,
      Type[] arguments, Type owner, string prefixName, string postfixName, string signatureId) {
    if (!RuntimeWitnessCatalog.TryGet(signatureId, out _)) {
      Record(signatureId, false, "signature absent from generated creator-safe witness catalog");
      return;
    }
    TryPatchCore(harmony, declaringType, methodName, arguments, owner,
        prefixName, postfixName, signatureId);
  }

  public static void TryPatchEngine(Harmony harmony, Type declaringType, string methodName,
      Type[] arguments, Type owner, string postfixName, string signatureId) =>
      TryPatchCore(harmony, declaringType, methodName, arguments, owner, null, postfixName, signatureId);

  public static void TryPatchEngineWithState(Harmony harmony, Type declaringType,
      string methodName, Type[] arguments, Type owner, string prefixName,
      string postfixName, string signatureId) =>
      TryPatchCore(harmony, declaringType, methodName, arguments, owner,
          prefixName, postfixName, signatureId);

  static void TryPatchCore(Harmony harmony, Type declaringType, string methodName,
      Type[] arguments, Type owner, string prefixName, string postfixName, string signatureId) {
    try {
      var target = declaringType == null ? null : AccessTools.Method(declaringType, methodName, arguments);
      if (target == null) { Record(signatureId, false, "exact method not found"); return; }
      MethodInfo prefix = prefixName == null ? null : AccessTools.Method(owner, prefixName);
      MethodInfo postfix = postfixName == null ? null : AccessTools.Method(owner, postfixName);
      if (prefixName != null && prefix == null) { Record(signatureId, false, "prefix not found"); return; }
      if (postfixName != null && postfix == null) { Record(signatureId, false, "postfix not found"); return; }
      harmony.Patch(target,
          prefix: prefix == null ? null : new HarmonyMethod(prefix),
          postfix: postfix == null ? null : new HarmonyMethod(postfix));
      Record(signatureId, true, "ok");
    } catch (Exception error) {
      Record(signatureId, false, error.Message);
    }
  }

  static void Record(string signatureId, bool applied, string detail) =>
      outcomes.Add(new Outcome(signatureId, applied, detail));
}

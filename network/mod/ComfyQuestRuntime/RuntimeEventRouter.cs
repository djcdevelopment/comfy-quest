namespace ComfyQuestRuntime;

using ComfyQuestContracts;

/// <summary>Exact witness routing, bounded correlation, and privacy normalization.</summary>
static class RuntimeEventRouter {
  static readonly RuntimeActionDedupe Dedupe = new();
  public static RuntimeExperienceEngine Engine { get; set; }

  public static void Emit(string signatureId, RuntimeEvent runtimeEvent,
      string subjectIdentity, string fingerprint) {
    try {
      if (runtimeEvent == null
          || !RuntimeWitnessCatalog.TryGet(signatureId, out var witness)
          || !witness.ProductionAvailable
          || !string.Equals(witness.EventName, runtimeEvent.Name,
              System.StringComparison.OrdinalIgnoreCase)) return;
      runtimeEvent.DedupeKey = Dedupe.Key(
          witness.DedupeGroup, subjectIdentity, fingerprint, signatureId,
          UnityEngine.Time.realtimeSinceStartup);
      Engine?.OnEvent(runtimeEvent);
    } catch {
      // Observation is never allowed to disrupt the underlying Valheim action.
    }
  }

  public static void EmitEngine(RuntimeEvent runtimeEvent,
      string dedupeGroup, string subjectIdentity, string fingerprint, string witness) {
    try {
      if (runtimeEvent == null
          || !RuntimeProductionEventCatalog.IsEngineEvent(runtimeEvent.Name)) return;
      runtimeEvent.DedupeKey = Dedupe.Key(
          dedupeGroup, subjectIdentity, fingerprint, witness,
          UnityEngine.Time.realtimeSinceStartup);
      Engine?.OnEvent(runtimeEvent);
    } catch { }
  }

  public static void Reset() => Dedupe.Clear();
}

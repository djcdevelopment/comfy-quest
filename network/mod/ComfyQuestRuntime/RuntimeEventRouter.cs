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
      RuntimeObservation.StampLocalPlayer(runtimeEvent);
      // Emit() is used only by patches that prove the local player performed the action before
      // constructing the event. The dedicated personal profile relies on that exact witness;
      // engine/network events take the separate path below and do not inherit it.
      Engine?.OnEvent(runtimeEvent, true);
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
      RuntimeObservation.StampLocalPlayer(runtimeEvent);
      Engine?.OnEvent(runtimeEvent, false);
    } catch { }
  }

  public static void Reset() => Dedupe.Clear();
}

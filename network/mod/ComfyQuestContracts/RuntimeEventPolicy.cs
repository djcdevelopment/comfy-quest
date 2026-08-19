namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;

/// <summary>Privacy-minimal normalization at the shipping Runtime boundary.</summary>
public static class RuntimeEventPolicy {
  public static RuntimeEvent Normalize(RuntimeEvent source) {
    if (source == null || string.IsNullOrWhiteSpace(source.Name)) return null;
    if (!RuntimeProductionEventCatalog.Contains(source.Name)
        && !RuntimeProductionEventCatalog.IsEngineEvent(source.Name)) return null;
    var target = NormalizeTarget(source.Target);
    if (RuntimeProductionEventCatalog.TryGet(
          source.Name, out RuntimeProductionEventCatalog.Definition runtime)
        && !TargetAllowed(runtime.TargetPolicy, runtime.FixedTarget,
            runtime.AllowedTargets, target)) return null;
    if (RuntimeProductionEventCatalog.TryGetEngine(
          source.Name, out RuntimeProductionEventCatalog.EngineDefinition engineTarget)
        && !TargetAllowed(engineTarget.TargetPolicy, engineTarget.FixedTarget,
            engineTarget.AllowedTargets, target)) return null;

    var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var field in source.Fields ?? EmptyFields) {
      if (!RuntimeProductionEventCatalog.IsAllowedWhere(source.Name, field.Key)) continue;
      var value = NormalizeScalar(field.Value);
      if (value != null) fields[field.Key] = value;
    }
    if (RuntimeProductionEventCatalog.TryGetEngine(
        source.Name, out RuntimeProductionEventCatalog.EngineDefinition engine)) {
      foreach (var required in engine.RequiredWhereFields) {
        if (!fields.ContainsKey(required)) return null;
      }
    }
    var position = NormalizePosition(source);
    return new RuntimeEvent {
      Name = source.Name.Trim().ToLowerInvariant(),
      Target = target,
      SourceId = NormalizeScalar(source.SourceId),
      At = source.At,
      Fields = fields.Count == 0 ? null : fields,
      PosX = position?.X,
      PosY = position?.Y,
      PosZ = position?.Z,
      DedupeKey = NormalizeScalar(source.DedupeKey),
    };
  }

  // A witness position passes the boundary only complete, finite, inside the reviewed world
  // bounds, and rounded to 0.1 m; anything else is dropped whole rather than partially kept.
  static SpatialPoint? NormalizePosition(RuntimeEvent source) {
    if (!source.PosX.HasValue || !source.PosY.HasValue || !source.PosZ.HasValue) return null;
    var x = source.PosX.Value; var y = source.PosY.Value; var z = source.PosZ.Value;
    if (!BoundedCoordinate(x) || !BoundedCoordinate(y) || !BoundedCoordinate(z)) return null;
    return new SpatialPoint(
        Math.Round(x, 1, MidpointRounding.AwayFromZero),
        Math.Round(y, 1, MidpointRounding.AwayFromZero),
        Math.Round(z, 1, MidpointRounding.AwayFromZero));
  }

  static bool BoundedCoordinate(double value) =>
      !double.IsNaN(value) && !double.IsInfinity(value)
      && Math.Abs(value) <= SpatialPredicateCatalog.MaxWorldCoordinate;

  static readonly IReadOnlyDictionary<string, string> EmptyFields =
      new Dictionary<string, string>();

  static string NormalizeTarget(string value) {
    var result = NormalizeScalar(value);
    if (result != null && result.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
      result = result.Substring(0, result.Length - "(Clone)".Length).Trim();
    return string.IsNullOrWhiteSpace(result) ? null : result;
  }

  static bool TargetAllowed(string policy, string fixedTarget,
      IReadOnlyList<string> allowedTargets, string target) {
    if (policy == "none") return target == null;
    if (policy == "fixed-output")
      return string.Equals(target, fixedTarget, StringComparison.OrdinalIgnoreCase);
    if (policy == "closed") {
      foreach (var allowed in allowedTargets ?? Array.Empty<string>())
        if (string.Equals(target, allowed, StringComparison.OrdinalIgnoreCase)) return true;
      return false;
    }
    return true;
  }

  static string NormalizeScalar(string value) {
    var result = (value ?? string.Empty).Trim();
    return result.Length == 0 || result.Length > 128 ? null : result;
  }
}

/// <summary>
/// Correlates alternate exact witnesses without coalescing two repetitions of the same witness.
/// Keys are process-local and are never serialized or exposed in receipts.
/// </summary>
public sealed class RuntimeActionDedupe {
  sealed class State {
    public string Key;
    public double At;
    public readonly HashSet<string> Witnesses = new(StringComparer.Ordinal);
  }

  readonly double window;
  readonly int capacity;
  readonly Dictionary<string, State> states = new(StringComparer.Ordinal);
  long sequence;

  public RuntimeActionDedupe(double correlationSeconds = 0.75, int maxEntries = 512) {
    window = Math.Max(0.01, correlationSeconds);
    capacity = Math.Max(16, maxEntries);
  }

  public string Key(string group, string subject, string fingerprint, string witness, double now) {
    string basis = Part(group) + "\n" + Part(subject) + "\n" + Part(fingerprint);
    string route = Part(witness);
    if (states.TryGetValue(basis, out State state)
        && now >= state.At && now - state.At <= window
        && !state.Witnesses.Contains(route)) {
      state.At = now;
      state.Witnesses.Add(route);
      return state.Key;
    }
    if (states.Count >= capacity) Trim();
    var next = new State { Key = Part(group) + ":" + (++sequence), At = now };
    next.Witnesses.Add(route);
    states[basis] = next;
    return next.Key;
  }

  public void Clear() => states.Clear();

  void Trim() {
    var rows = new List<KeyValuePair<string, State>>(states);
    rows.Sort((left, right) => left.Value.At.CompareTo(right.Value.At));
    for (var index = 0; index < rows.Count - capacity / 2; index++)
      states.Remove(rows[index].Key);
  }

  static string Part(string value) => string.IsNullOrWhiteSpace(value) ? "_" : value.Trim();
}

/// <summary>Event names referenced by one compiled experience, prepared once at activation.</summary>
public sealed class RuntimeSubscriptionIndex {
  readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

  RuntimeSubscriptionIndex(ExperienceDocument document) {
    foreach (var stage in document?.Stages ?? new List<ExperienceStage>())
      foreach (var transition in stage?.Transitions ?? new List<ExperienceTransition>())
        Add(transition?.When);
  }

  public static RuntimeSubscriptionIndex Create(ExperienceDocument document) => new(document);
  public bool Contains(string eventName) =>
      !string.IsNullOrWhiteSpace(eventName) && names.Contains(eventName);
  public int Count => names.Count;

  void Add(TriggerExpression expression) {
    if (expression == null) return;
    if (string.Equals(expression.Op, "EVENT", StringComparison.OrdinalIgnoreCase)) {
      if (!string.IsNullOrWhiteSpace(expression.Event)) names.Add(expression.Event);
      return;
    }
    // A counted measure needs its source event delivered even when no clause names it.
    if (string.Equals(expression.Op, "THRESHOLD", StringComparison.OrdinalIgnoreCase)) {
      if (AdaptiveMeasureCatalog.TryGet(expression.Measure, out var measure)
          && !string.IsNullOrWhiteSpace(measure.SourceEvent)) names.Add(measure.SourceEvent);
      return;
    }
    foreach (var child in expression.Children ?? new List<TriggerExpression>()) Add(child);
  }
}

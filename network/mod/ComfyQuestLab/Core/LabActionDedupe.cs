namespace ComfyQuestLab;

using System;
using System.Collections.Generic;

/// <summary>
/// Gives alternative method/RPC/overload witnesses one action identity without merging two real
/// actions. A repeated witness name starts a new action; a different witness arriving inside the
/// short correlation window joins the current one. Unity-free so the pairing rule is headlessly
/// tested rather than inferred from quest cooldown.
/// </summary>
public sealed class LabActionDedupe {
  sealed class State {
    public string ActionKey;
    public double At;
    public readonly HashSet<string> Witnesses =
        new HashSet<string>(StringComparer.Ordinal);
  }

  readonly double _correlationSeconds;
  readonly int _capacity;
  readonly Dictionary<string, State> _states =
      new Dictionary<string, State>(StringComparer.Ordinal);
  long _sequence;

  public LabActionDedupe(double correlationSeconds = 0.75, int capacity = 512) {
    _correlationSeconds = Math.Max(0.01, correlationSeconds);
    _capacity = Math.Max(16, capacity);
  }

  public string Key(
      string dedupeGroup,
      string subjectIdentity,
      string fingerprint,
      string witness,
      double now) {
    string basis = Part(dedupeGroup) + "\n" + Part(subjectIdentity) + "\n" + Part(fingerprint);
    string source = Part(witness);

    if (_states.TryGetValue(basis, out State state)
        && now >= state.At
        && now - state.At <= _correlationSeconds
        && !state.Witnesses.Contains(source)) {
      state.At = now;
      state.Witnesses.Add(source);
      return state.ActionKey;
    }

    if (_states.Count >= _capacity) {
      Trim();
    }
    var next = new State {
      ActionKey = Part(dedupeGroup) + ":" + (++_sequence).ToString(),
      At = now,
    };
    next.Witnesses.Add(source);
    _states[basis] = next;
    return next.ActionKey;
  }

  public void Clear() {
    _states.Clear();
  }

  void Trim() {
    var ordered = new List<KeyValuePair<string, State>>(_states);
    ordered.Sort((left, right) => left.Value.At.CompareTo(right.Value.At));
    int remove = ordered.Count - (_capacity / 2);
    for (int i = 0; i < remove; i++) {
      _states.Remove(ordered[i].Key);
    }
  }

  static string Part(string value) {
    return string.IsNullOrWhiteSpace(value) ? "_" : value.Trim();
  }
}

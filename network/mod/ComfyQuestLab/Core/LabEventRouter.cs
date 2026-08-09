namespace ComfyQuestLab;

using System;
using System.Collections.Generic;

using ComfyNetworkSense;

using UnityEngine;

/// <summary>
/// The only bridge from an atlas witness to a creator quest event. It applies the live profile,
/// records stable event vocabulary in the console, correlates overload/RPC witnesses, and hands
/// only policy-approved events to the source-shared evaluator.
/// </summary>
public static class LabEventRouter {
  static readonly LabActionDedupe ActionKeys = new LabActionDedupe();
  static readonly Dictionary<string, double> DisplayedActions =
      new Dictionary<string, double>(StringComparer.Ordinal);
  static string _batchProfileOverride;

  public static string Emit(
      string signatureId,
      string target,
      string detail,
      string subjectIdentity = null,
      string fingerprint = null,
      string weaponSkill = null,
      bool projectile = false,
      IReadOnlyDictionary<string, string> fields = null,
      bool evaluate = true) {
    try {
      if (!LabSeamCatalog.TryGet(signatureId, out LabSeamCatalog.Entry capability)) {
        ComfyQuestLab.ObserveRuntimeEvent(new LabEvent(
            LabCategory.Combat,
            signatureId,
            signatureId,
            target,
            detail,
            LabUsability.LabCandidate), null);
        return null;
      }

      string configured = _batchProfileOverride ?? (LabConfig.EventProfile == null
          ? LabRuntimeProfile.Extended
          : LabConfig.EventProfile.Value);
      if (!LabRuntimeProfile.Allows(configured, capability.Profile)) {
        return null;
      }

      double now = Time.realtimeSinceStartup;
      string actionKey = null;
      bool firstCreatorWitness = true;
      if (capability.CreatorSafe) {
        actionKey = ActionKeys.Key(
            capability.DedupeGroup,
            string.IsNullOrWhiteSpace(subjectIdentity) ? target : subjectIdentity,
            string.IsNullOrWhiteSpace(fingerprint)
                ? (target ?? string.Empty) + "|" + (weaponSkill ?? string.Empty)
                    + "|" + projectile.ToString()
                : fingerprint,
            capability.SignatureId,
            now);
        firstCreatorWitness = RememberDisplayed(actionKey, now);
      }

      if (firstCreatorWitness) {
        string usability = capability.CreatorSafe
            ? LabUsability.Today
            : LabUsability.DiagnosticOnly;
        ComfyQuestLab.ObserveRuntimeEvent(new LabEvent(
            capability.Category,
            capability.SignatureId,
            capability.CanonicalEvent,
            target,
            detail,
            usability), actionKey);
      } else if (LabConfig.VerboseLogging != null && LabConfig.VerboseLogging.Value) {
        ComfyQuestLab.LogInfo(
            "[lab] coalesced " + capability.SignatureId + " into " + actionKey);
      }

      if (capability.CreatorSafe && ComfyQuestLab.Batch != null) {
        ComfyQuestLab.Batch.Observe(
            capability.Category,
            capability.CanonicalEvent,
            capability.SignatureId,
            target,
            actionKey,
            firstCreatorWitness,
            evaluate);
      }

      if (!evaluate || !capability.CreatorSafe) {
        return actionKey;
      }

      var gameplayEvent = new QuestEvent(
          capability.CanonicalEvent,
          target,
          weaponSkill,
          projectile,
          actionKey,
          fields);
      LabQuestEngine.OnEvent(gameplayEvent, capability.Category, detail, now);
      return actionKey;
    } catch (Exception) {
      // Every caller is inside a Valheim path. A lab event is never worth breaking it.
      return null;
    }
  }

  public static string Identity(UnityEngine.Object subject) {
    try {
      return subject == null ? null : subject.GetType().Name + ":" + subject.GetInstanceID();
    } catch (Exception) {
      return null;
    }
  }

  public static bool IsLocalSender(long senderPeerId) {
    try {
      return senderPeerId != 0L && ZNet.GetUID() == senderPeerId;
    } catch (Exception) {
      return false;
    }
  }

  public static void Reset() {
    ActionKeys.Clear();
    DisplayedActions.Clear();
    _batchProfileOverride = null;
  }

  /// <summary>Use the stable extended event surface for one live suite without writing the
  /// creator's config. Reset/quest reload removes the override.</summary>
  public static void BeginBatchCapture() {
    ActionKeys.Clear();
    DisplayedActions.Clear();
    _batchProfileOverride = LabRuntimeProfile.Extended;
  }

  static bool RememberDisplayed(string actionKey, double now) {
    if (DisplayedActions.ContainsKey(actionKey)) {
      return false;
    }
    if (DisplayedActions.Count >= 1024) {
      var ordered = new List<KeyValuePair<string, double>>(DisplayedActions);
      ordered.Sort((left, right) => left.Value.CompareTo(right.Value));
      for (int i = 0; i < ordered.Count / 2; i++) {
        DisplayedActions.Remove(ordered[i].Key);
      }
    }
    DisplayedActions[actionKey] = now;
    return true;
  }
}

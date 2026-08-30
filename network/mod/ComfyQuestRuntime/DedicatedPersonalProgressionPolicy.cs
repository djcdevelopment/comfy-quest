namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using System.Linq;
using ComfyQuestContracts;

/// <summary>
/// The deliberately narrow dedicated-server beta profile. The server continues to own every
/// shared object; one peer may only read the pinned venue reference, evaluate its own witnessed
/// hunt events, persist its own run state, and present authored messages locally.
/// </summary>
sealed class DedicatedPersonalProgressionProfile {
  public bool Enabled { get; set; }
  public string WorldUid { get; set; }
  public string ContentHash { get; set; }
}

static class DedicatedPersonalProgressionPolicy {
  static readonly HashSet<string> PersonalEvents = new(StringComparer.OrdinalIgnoreCase) {
    "kill",
    ExperienceSchema.ExperienceStartedEvent,
  };

  public static PolicyDecision CanUse(
      DedicatedPersonalProgressionProfile profile,
      WorldAuthority world,
      string currentWorldUid,
      string activeContentHash,
      IEnumerable<ExperienceDocument> documents) {
    if (profile?.Enabled != true)
      return PolicyDecision.Deny("dedicated_personal_progression_disabled");
    if (world == null || !world.IsPeerClient || world.IsDedicated
        || world.IsListenHost || world.IsSolo)
      return PolicyDecision.Deny("dedicated_personal_progression_peer_required");
    if (!WorldUid(profile.WorldUid) || !string.Equals(
          profile.WorldUid, currentWorldUid, StringComparison.Ordinal))
      return PolicyDecision.Deny("dedicated_personal_progression_world_mismatch");
    if (!Hash(profile.ContentHash) || !string.Equals(
          profile.ContentHash, activeContentHash, StringComparison.OrdinalIgnoreCase))
      return PolicyDecision.Deny("dedicated_personal_progression_content_mismatch");

    var values = (documents ?? Array.Empty<ExperienceDocument>())
        .Where(value => value != null).ToArray();
    if (values.Length == 0)
      return PolicyDecision.Deny("dedicated_personal_progression_content_missing");
    foreach (var document in values) {
      foreach (var stage in document.Stages ?? new List<ExperienceStage>()) {
        foreach (var action in stage?.EntryActions ?? new List<ExperienceAction>())
          if (!Message(action))
            return PolicyDecision.Deny("dedicated_personal_progression_action_denied");
        foreach (var transition in stage?.Transitions ?? new List<ExperienceTransition>()) {
          foreach (var action in transition?.Actions ?? new List<ExperienceAction>())
            if (!Message(action))
              return PolicyDecision.Deny("dedicated_personal_progression_action_denied");
          if (!PersonalTrigger(transition?.When))
            return PolicyDecision.Deny("dedicated_personal_progression_event_denied");
        }
      }
    }
    return PolicyDecision.Allow();
  }

  public static PolicyDecision CanAcceptEvent(
      DedicatedPersonalProgressionProfile profile,
      WorldAuthority world,
      string currentWorldUid,
      string activeContentHash,
      IEnumerable<ExperienceDocument> documents,
      RuntimeEvent runtimeEvent,
      bool locallyWitnessed) {
    var profileDecision = CanUse(
        profile, world, currentWorldUid, activeContentHash, documents);
    if (!profileDecision.Allowed) return profileDecision;
    if (!locallyWitnessed)
      return PolicyDecision.Deny("dedicated_personal_progression_foreign_event");
    return runtimeEvent != null && PersonalEvents.Contains(runtimeEvent.Name)
        ? PolicyDecision.Allow()
        : PolicyDecision.Deny("dedicated_personal_progression_event_denied");
  }

  public static bool Message(ExperienceAction action) =>
      action != null && string.Equals(action.Type, "message", StringComparison.Ordinal);

  static bool PersonalTrigger(TriggerExpression trigger) {
    if (trigger == null) return false;
    if (string.Equals(trigger.Op, "EVENT", StringComparison.OrdinalIgnoreCase))
      return PersonalEvents.Contains(trigger.Event);
    if (string.Equals(trigger.Op, "THRESHOLD", StringComparison.OrdinalIgnoreCase))
      return false;
    return (trigger.Children?.Count ?? 0) > 0
        && trigger.Children.All(PersonalTrigger);
  }

  static bool WorldUid(string value) =>
      long.TryParse(value, out var parsed) && parsed != 0;

  static bool Hash(string value) => value != null && value.Length == 64
      && value.All(Uri.IsHexDigit);
}

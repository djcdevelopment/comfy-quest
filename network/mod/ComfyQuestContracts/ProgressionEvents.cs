namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>Privacy-minimal local-player progression events.</summary>
public static class ProgressionEventContract {
  public const string MaxHealthChangedEvent = "max_health_changed";
  public const string PlayerDiedEvent = "player_died";
  public const string SkillRaisedEvent = "skill_raised";
  public const string SkillsLoweredEvent = "skills_lowered";
  public const string StaminaGainedEvent = "stamina_gained";
  public const string StaminaSpentEvent = "stamina_spent";

  public static RuntimeEvent MaxHealthChanged(float resultingMaximum, DateTimeOffset at) =>
      Attribute(MaxHealthChangedEvent, "health", "health", resultingMaximum, at);

  public static RuntimeEvent PlayerDied(DateTimeOffset at) => new() {
    Name = PlayerDiedEvent, Target = "you", At = at,
  };

  public static RuntimeEvent SkillRaised(
      string skill, float progressAdded, DateTimeOffset at) {
    string target = Normalize(skill);
    return target == null ? null
      : Attribute(SkillRaisedEvent, target, target, progressAdded, at);
  }

  public static RuntimeEvent SkillsLowered(float lossFraction, DateTimeOffset at) =>
      Attribute(SkillsLoweredEvent, "all skills", "all", lossFraction, at);

  public static RuntimeEvent StaminaGained(float amount, DateTimeOffset at) =>
      Attribute(StaminaGainedEvent, "stamina", "stamina", amount, at);

  public static RuntimeEvent StaminaSpent(float amount, DateTimeOffset at) =>
      Attribute(StaminaSpentEvent, "stamina", "stamina", amount, at);

  static RuntimeEvent Attribute(
      string name, string target, string subject, float amount, DateTimeOffset at) {
    if (!Finite(amount) || amount <= 0f) return null;
    return new RuntimeEvent {
      Name = name,
      Target = target,
      At = at,
      Fields = new Dictionary<string, string> {
        ["subject"] = subject,
        ["amount"] = amount.ToString("R", CultureInfo.InvariantCulture),
      },
    };
  }

  static string Normalize(string value) {
    string result = (value ?? string.Empty).Trim();
    return result.Length == 0 || result.Length > 128 ? null : result;
  }

  static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>Pure before/after gates for local-player progression adapters.</summary>
public static class ProgressionProof {
  public static bool ValueChanged(bool localPlayer, double before, double after) =>
      localPlayer && Finite(before) && Finite(after) && before != after;

  public static bool ValueIncreased(bool localPlayer, double before, double after) =>
      localPlayer && Finite(before) && Finite(after) && after > before;

  public static bool ValueDecreased(bool localPlayer, double before, double after) =>
      localPlayer && Finite(before) && Finite(after) && after < before;

  public static bool SkillRaised(
      bool localPlayer, double levelBefore, double progressBefore,
      double levelAfter, double progressAfter) =>
    localPlayer && Finite(levelBefore) && Finite(progressBefore)
      && Finite(levelAfter) && Finite(progressAfter)
      && (levelAfter > levelBefore
        || (levelAfter == levelBefore && progressAfter > progressBefore));

  public static bool SkillsLowered(
      bool localPlayer, double lossFraction,
      double levelsBefore, double progressBefore,
      double levelsAfter, double progressAfter) =>
    localPlayer && Finite(lossFraction) && lossFraction > 0d
      && Finite(levelsBefore) && Finite(progressBefore)
      && Finite(levelsAfter) && Finite(progressAfter)
      && (levelsAfter < levelsBefore || progressAfter < progressBefore);

  public static bool PlayerDied(
      bool localPlayer, bool authoritativeOwner, bool deadBefore, bool deadAfter) =>
    localPlayer && authoritativeOwner && !deadBefore && deadAfter;

  static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

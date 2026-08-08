namespace ComfyQuestLab;

using System.Collections.Generic;
using System.Globalization;

using HarmonyLib;

/// <summary>Progression seams: skills and stamina.
///
/// These are the two people reach for by name and then cannot find, because the names
/// they reach for do not exist. There is no `CurrentSkillLevel` and no `EnergySpent`.
/// What exists is:
///
///   Skills.RaiseSkill(SkillType, float)   the event — a skill went up
///   Skills.GetSkillLevel(SkillType)       a query, not an event, so not hooked here
///   Player.UseStamina(float)              the event — stamina was spent
///
/// The distinction between an event and a query is the thing to take away. You can
/// build a quest on "raised a skill"; you cannot build one on "has skill level 30"
/// without something asking the question on a schedule, and nothing does that today.
///
/// Nothing in the shipping mod touches skills at all. The only skill-adjacent value it
/// carries is hit.m_skill as a string on a combat event.
///
/// UseStamina is deliberately quiet-by-default: it fires on essentially every action,
/// including running. It is behind its own config flag so a builder can turn it on to
/// see the shape and then turn it off before it drowns the console.</summary>
public static class ProgressionPatches {
  public static void Apply(Harmony harmony) {
    LabPatching.TryPatch(harmony, typeof(Skills), "RaiseSkill",
        new[] { typeof(Skills.SkillType), typeof(float) },
        nameof(RaiseSkillPostfix), "Skills.RaiseSkill");
    LabPatching.TryPatch(harmony, typeof(Player), "OnDeath", System.Type.EmptyTypes,
        nameof(PlayerOnDeathPostfix), "Player.OnDeath");
    LabPatching.TryPatch(harmony, typeof(Player), "RaiseSkill",
        new[] { typeof(Skills.SkillType), typeof(float) },
        nameof(PlayerRaiseSkillPostfix), "Player.RaiseSkill");
    LabPatching.TryPatch(harmony, typeof(Player), "AddStamina", new[] { typeof(float) },
        nameof(AddStaminaPostfix), "Player.AddStamina");
    LabPatching.TryPatch(harmony, typeof(Player), "SetMaxHealth",
        new[] { typeof(float), typeof(bool) },
        nameof(SetMaxHealthPostfix), "Player.SetMaxHealth");
    LabPatching.TryPatch(harmony, typeof(Skills), "LowerAllSkills", new[] { typeof(float) },
        nameof(LowerAllSkillsPostfix), "Skills.LowerAllSkills");

    if (LabConfig.ObserveStamina.Value) {
      LabPatching.TryPatch(harmony, typeof(Player), "UseStamina", new[] { typeof(float) },
          nameof(UseStaminaPostfix), "Player.UseStamina");
    }
  }

  static void RaiseSkillPostfix(Skills.SkillType __0, float __1) {
    // Skills hangs off the local player, so anything raising one is the player's own.
    LabObserve.Seam(
        "Skills.RaiseSkill(SkillType, float)",
        __0.ToString(), "+" + __1.ToString("0.##"));
  }

  static void PlayerOnDeathPostfix(Player __instance) {
    LabObserve.LocalPlayer("Player.OnDeath()", __instance, "you", "died");
  }

  static void PlayerRaiseSkillPostfix(Player __instance, Skills.SkillType __0, float __1) {
    string amount = __1.ToString("R", CultureInfo.InvariantCulture);
    LabObserve.LocalPlayer(
        "Player.RaiseSkill(SkillType, float)", __instance, __0.ToString(),
        "raised by " + __1.ToString("0.##", CultureInfo.InvariantCulture),
        fingerprint: __0 + "|" + amount,
        fields: ProgressFields(__0.ToString(), amount));
  }

  static void AddStaminaPostfix(Player __instance, float __0) {
    if (__0 <= 0f) {
      return;
    }
    string amount = __0.ToString("R", CultureInfo.InvariantCulture);
    LabObserve.LocalPlayer(
        "Player.AddStamina(float)", __instance, "stamina",
        "+" + __0.ToString("0.#", CultureInfo.InvariantCulture),
        fingerprint: amount, fields: ProgressFields("stamina", amount));
  }

  static void SetMaxHealthPostfix(Player __instance, float __0) {
    if (__0 <= 0f) {
      return;
    }
    string amount = __0.ToString("R", CultureInfo.InvariantCulture);
    LabObserve.LocalPlayer(
        "Player.SetMaxHealth(float, bool)", __instance, "health",
        "maximum " + __0.ToString("0.#", CultureInfo.InvariantCulture),
        fingerprint: amount, fields: ProgressFields("health", amount));
  }

  static void LowerAllSkillsPostfix(Skills __instance, float __0) {
    try {
      if (Player.m_localPlayer == null || __instance != Player.m_localPlayer.GetSkills()) {
        return;
      }
      string amount = __0.ToString("R", CultureInfo.InvariantCulture);
      LabEventRouter.Emit(
          "Skills.LowerAllSkills(float)", "all skills",
          "lowered by " + __0.ToString("0.##", CultureInfo.InvariantCulture),
          LabEventRouter.Identity(Player.m_localPlayer), amount,
          fields: ProgressFields("all", amount));
    } catch (System.Exception) {
    }
  }

  static void UseStaminaPostfix(Player __instance, float __0) {
    if (__0 <= 0f) {
      return;
    }
    LabObserve.LocalPlayer(
        "Player.UseStamina(float)", __instance, "stamina",
        "-" + __0.ToString("0.#", CultureInfo.InvariantCulture),
        fingerprint: __0.ToString("R", CultureInfo.InvariantCulture),
        fields: ProgressFields(
            "stamina", __0.ToString("R", CultureInfo.InvariantCulture)));
  }

  static IReadOnlyDictionary<string, string> ProgressFields(string subject, string amount) {
    return new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) {
      ["subject"] = subject,
      ["amount"] = amount,
    };
  }
}

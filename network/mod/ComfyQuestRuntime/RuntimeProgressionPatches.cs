namespace ComfyQuestRuntime;

using System;
using ComfyQuestContracts;
using HarmonyLib;

/// <summary>Local-player progression witnesses proven by bounded before/after state.</summary>
static class RuntimeProgressionPatches {
  sealed class ScalarState {
    public bool Local;
    public float Before = float.NaN;
  }

  sealed class SkillState {
    public bool Local;
    public Skills Skills;
    public Skills.SkillType Skill;
    public float LevelBefore = float.NaN;
    public float ProgressBefore = float.NaN;
  }

  sealed class SkillSetState {
    public bool Local;
    public float LossFraction = float.NaN;
    public float LevelsBefore = float.NaN;
    public float ProgressBefore = float.NaN;
  }

  sealed class DeathState {
    public bool Local;
    public bool Owner;
    public bool DeadBefore;
    public ZDO Zdo;
  }

  public static void Apply(Harmony harmony) {
    RuntimePatching.TryPatchWithState(harmony, typeof(Player), "SetMaxHealth",
        new[] { typeof(float), typeof(bool) }, typeof(RuntimeProgressionPatches),
        nameof(MaxHealthPrefix), nameof(MaxHealthChanged),
        "Player.SetMaxHealth(float, bool)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Player), "OnDeath",
        Type.EmptyTypes, typeof(RuntimeProgressionPatches),
        nameof(PlayerDeathPrefix), nameof(PlayerDied), "Player.OnDeath()");
    RuntimePatching.TryPatchWithState(harmony, typeof(Player), "RaiseSkill",
        new[] { typeof(Skills.SkillType), typeof(float) },
        typeof(RuntimeProgressionPatches), nameof(SkillPrefix), nameof(SkillRaised),
        "Player.RaiseSkill(SkillType, float)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Skills), "LowerAllSkills",
        new[] { typeof(float) }, typeof(RuntimeProgressionPatches),
        nameof(SkillsLoweredPrefix), nameof(SkillsLowered),
        "Skills.LowerAllSkills(float)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Player), "AddStamina",
        new[] { typeof(float) }, typeof(RuntimeProgressionPatches),
        nameof(StaminaPrefix), nameof(StaminaGained), "Player.AddStamina(float)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Player), "UseStamina",
        new[] { typeof(float) }, typeof(RuntimeProgressionPatches),
        nameof(StaminaPrefix), nameof(StaminaSpent), "Player.UseStamina(float)");
  }

  static void MaxHealthPrefix(Player __instance, out ScalarState __state) =>
      __state = Capture(__instance, player => player.GetMaxHealth());

  static void MaxHealthChanged(Player __instance, ScalarState __state) {
    try {
      float after = __instance == null ? float.NaN : __instance.GetMaxHealth();
      if (__state == null || !ProgressionProof.ValueChanged(
          __state.Local && Local(__instance), __state.Before, after)) return;
      Emit("Player.SetMaxHealth(float, bool)",
          ProgressionEventContract.MaxHealthChanged(after, DateTimeOffset.UtcNow),
          __instance, "health");
    } catch { }
  }

  static void StaminaPrefix(Player __instance, out ScalarState __state) =>
      __state = Capture(__instance, player => player.GetStamina());

  static void StaminaGained(Player __instance, ScalarState __state) {
    try {
      float after = __instance == null ? float.NaN : __instance.GetStamina();
      if (__state == null || !ProgressionProof.ValueIncreased(
          __state.Local && Local(__instance), __state.Before, after)) return;
      float amount = after - __state.Before;
      Emit("Player.AddStamina(float)",
          ProgressionEventContract.StaminaGained(amount, DateTimeOffset.UtcNow),
          __instance, "stamina");
    } catch { }
  }

  static void StaminaSpent(Player __instance, ScalarState __state) {
    try {
      float after = __instance == null ? float.NaN : __instance.GetStamina();
      if (__state == null || !ProgressionProof.ValueDecreased(
          __state.Local && Local(__instance), __state.Before, after)) return;
      float amount = __state.Before - after;
      Emit("Player.UseStamina(float)",
          ProgressionEventContract.StaminaSpent(amount, DateTimeOffset.UtcNow),
          __instance, "stamina");
    } catch { }
  }

  static void SkillPrefix(
      Player __instance, Skills.SkillType __0, out SkillState __state) {
    __state = new SkillState { Local = Local(__instance), Skill = __0 };
    try {
      __state.Skills = __instance?.GetSkills();
      Snapshot(__state.Skills, __0, out __state.LevelBefore, out __state.ProgressBefore);
    } catch { __state.Local = false; }
  }

  static void SkillRaised(Player __instance, SkillState __state) {
    try {
      if (__state == null || __state.Skills == null) return;
      Snapshot(__state.Skills, __state.Skill, out float levelAfter, out float progressAfter);
      if (!ProgressionProof.SkillRaised(
          __state.Local && Local(__instance), __state.LevelBefore,
          __state.ProgressBefore, levelAfter, progressAfter)) return;
      float amount = ProgressAdded(__state, levelAfter, progressAfter);
      var evt = ProgressionEventContract.SkillRaised(
          __state.Skill.ToString(), amount, DateTimeOffset.UtcNow);
      Emit("Player.RaiseSkill(SkillType, float)", evt, __instance, evt?.Target);
    } catch { }
  }

  static void SkillsLoweredPrefix(
      Skills __instance, float __0, out SkillSetState __state) {
    __state = new SkillSetState {
      Local = LocalSkills(__instance),
      LossFraction = __0,
    };
    Snapshot(__instance, out __state.LevelsBefore, out __state.ProgressBefore);
  }

  static void SkillsLowered(Skills __instance, SkillSetState __state) {
    try {
      if (__state == null) return;
      Snapshot(__instance, out float levelsAfter, out float progressAfter);
      if (!ProgressionProof.SkillsLowered(
          __state.Local && LocalSkills(__instance), __state.LossFraction,
          __state.LevelsBefore, __state.ProgressBefore,
          levelsAfter, progressAfter)) return;
      var evt = ProgressionEventContract.SkillsLowered(
          __state.LossFraction, DateTimeOffset.UtcNow);
      Emit("Skills.LowerAllSkills(float)", evt, Player.m_localPlayer, "all");
    } catch { }
  }

  static void PlayerDeathPrefix(Player __instance, out DeathState __state) {
    __state = new DeathState { Local = Local(__instance) };
    try {
      var view = __instance == null ? null : __instance.GetComponent<ZNetView>();
      __state.Owner = view != null && view.IsValid() && view.IsOwner();
      __state.Zdo = view?.GetZDO();
      __state.DeadBefore = __state.Zdo != null && __state.Zdo.GetBool(ZDOVars.s_dead);
    } catch { __state.Owner = false; }
  }

  static void PlayerDied(Player __instance, DeathState __state) {
    try {
      bool deadAfter = __state?.Zdo != null && __state.Zdo.GetBool(ZDOVars.s_dead);
      if (__state == null || !ProgressionProof.PlayerDied(
          __state.Local && Local(__instance), __state.Owner,
          __state.DeadBefore, deadAfter)) return;
      Emit("Player.OnDeath()", ProgressionEventContract.PlayerDied(DateTimeOffset.UtcNow),
          __instance, "you");
    } catch { }
  }

  static ScalarState Capture(Player player, Func<Player, float> read) {
    var state = new ScalarState { Local = Local(player) };
    try { state.Before = player == null ? float.NaN : read(player); }
    catch { state.Local = false; }
    return state;
  }

  static void Snapshot(
      Skills skills, Skills.SkillType target, out float level, out float progress) {
    level = 0f;
    progress = 0f;
    if (skills == null || target == Skills.SkillType.None) return;
    foreach (var skill in skills.GetSkillList()) {
      if (skill?.m_info == null || skill.m_info.m_skill != target) continue;
      level = skill.m_level;
      progress = skill.m_accumulator;
      return;
    }
  }

  static void Snapshot(Skills skills, out float levels, out float progress) {
    levels = 0f;
    progress = 0f;
    try {
      foreach (var skill in skills?.GetSkillList() ?? new()) {
        if (skill == null) continue;
        levels += skill.m_level;
        progress += skill.m_accumulator;
      }
    } catch { levels = float.NaN; progress = float.NaN; }
  }

  static float ProgressAdded(SkillState state, float levelAfter, float progressAfter) {
    if (levelAfter > state.LevelBefore) {
      double requirement = Math.Pow(Math.Floor(state.LevelBefore + 1f), 1.5d) * 0.5d + 0.5d;
      return (float)Math.Max(0d, requirement - state.ProgressBefore);
    }
    return Math.Max(0f, progressAfter - state.ProgressBefore);
  }

  static bool Local(Player player) => player != null && player == Player.m_localPlayer;

  static bool LocalSkills(Skills skills) {
    try { return Player.m_localPlayer != null && Player.m_localPlayer.GetSkills() == skills; }
    catch { return false; }
  }

  static void Emit(
      string signature, RuntimeEvent evt, UnityEngine.Object subject, string fingerprint) =>
    RuntimeEventRouter.Emit(signature, evt, Identity(subject), fingerprint);

  static string Identity(UnityEngine.Object value) =>
      value == null ? null : value.GetType().Name + ":" + value.GetInstanceID();
}

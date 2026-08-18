namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using ComfyQuestContracts;
using HarmonyLib;

static class RuntimeKillPatches {
  sealed class LastHit {
    public double At;
    public string WeaponSkill;
    public bool Projectile;
  }

  sealed class DamageState {
    public Character Victim;
    public LastHit Hit;
    public LastHit Previous;
    public double HealthBefore;
    public string Subject;
    public string Target;
    public bool Candidate;
  }

  sealed class StaggerState {
    public LastHit Hit;
    public bool WasStaggering;
    public string Subject;
    public string Target;
    public bool Candidate;
  }

  static readonly Dictionary<int, LastHit> Hits = new();
  const double FreshSeconds = 15;
  const double StaggerFreshSeconds = 1.5;

  public static void Apply(Harmony harmony) {
    RuntimePatching.TryPatchWithState(harmony, typeof(Character), "Damage",
        new[] { typeof(HitData) }, typeof(RuntimeKillPatches),
        nameof(DamagePrefix), nameof(DamagePostfix), "Character.Damage(HitData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Character), "RPC_Damage",
        new[] { typeof(long), typeof(HitData) }, typeof(RuntimeKillPatches),
        nameof(RpcDamagePrefix), nameof(RpcDamagePostfix),
        "Character.RPC_Damage(long, HitData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Character), "Stagger",
        new[] { typeof(UnityEngine.Vector3) }, typeof(RuntimeKillPatches),
        nameof(StaggerPrefix), nameof(StaggerPostfix), "Character.Stagger(Vector3)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Character), "RPC_Stagger",
        new[] { typeof(long), typeof(UnityEngine.Vector3) }, typeof(RuntimeKillPatches),
        nameof(RpcStaggerPrefix), nameof(RpcStaggerPostfix),
        "Character.RPC_Stagger(long, Vector3)");
    RuntimePatching.TryPatch(harmony, typeof(Humanoid), "BlockAttack",
        new[] { typeof(HitData), typeof(Character) }, typeof(RuntimeKillPatches),
        nameof(AttackBlocked), "Humanoid.BlockAttack(HitData, Character)");
    RuntimePatching.TryPatch(harmony, typeof(Character), "OnDeath", Type.EmptyTypes,
        typeof(RuntimeKillPatches), nameof(Death), "Character.OnDeath()");
    RuntimePatching.TryPatch(harmony, typeof(WearNTear), "ApplyDamage",
        new[] { typeof(float), typeof(HitData) }, typeof(RuntimeKillPatches),
        nameof(PieceDamage), "WearNTear.ApplyDamage(float, HitData)");
    RuntimePatching.TryPatch(harmony, typeof(Sign), "SetText", new[] { typeof(string) },
        typeof(RuntimeKillPatches), nameof(SignWritten), "Sign.SetText(string)");
  }

  static void DamagePrefix(Character __instance, HitData __0, out DamageState __state) =>
      __state = CaptureDamage(__instance, __0);

  static void RpcDamagePrefix(Character __instance, HitData __1, out DamageState __state) =>
      __state = CaptureDamage(__instance, __1);

  static void DamagePostfix(DamageState __state) => CompleteDamage(
      "Character.Damage(HitData)", __state);

  static void RpcDamagePostfix(DamageState __state) => CompleteDamage(
      "Character.RPC_Damage(long, HitData)", __state);

  static void StaggerPrefix(Character __instance, out StaggerState __state) =>
      __state = CaptureStagger(__instance);

  static void RpcStaggerPrefix(Character __instance, out StaggerState __state) =>
      __state = CaptureStagger(__instance);

  static void StaggerPostfix(Character __instance, StaggerState __state) =>
      CompleteStagger("Character.Stagger(Vector3)", __instance, __state,
          __instance != null && __instance.IsStaggering());

  static void RpcStaggerPostfix(StaggerState __state) =>
      CompleteStagger("Character.RPC_Stagger(long, Vector3)", null, __state,
          __state != null && !__state.WasStaggering);

  static void AttackBlocked(
      Humanoid __instance, HitData __0, Character __1, bool __result) {
    try {
      if (__0 == null || __1 == null || !CombatHarvestProof.AttackBlocked(
          __instance != null && __instance == Player.m_localPlayer, __result)) return;
      var evt = CombatHarvestEventContract.AttackBlocked(
          __1.m_name, __0.m_skill.ToString(), __0.m_ranged, DateTimeOffset.UtcNow);
      RuntimeEventRouter.Emit("Humanoid.BlockAttack(HitData, Character)", evt,
          Identity(__instance), Fingerprint(__0));
    } catch { }
  }

  static void Death(Character __instance) {
    try {
      if (__instance == null || __instance.IsPlayer()) return;
      var id = __instance.GetInstanceID();
      if (!Hits.TryGetValue(id, out var hit)) return;
      Hits.Remove(id);
      if (UnityEngine.Time.realtimeSinceStartup - hit.At > FreshSeconds) return;
      var fields = new Dictionary<string, string> {
        ["weapon_skill"] = hit.WeaponSkill ?? "None",
        ["projectile"] = hit.Projectile ? "true" : "false",
      };
      var evt = new RuntimeEvent {
        Name = "kill", Target = __instance.m_name, At = DateTimeOffset.UtcNow, Fields = fields,
      };
      RuntimeEventRouter.Emit("Character.OnDeath()", evt, Identity(__instance),
          (hit.WeaponSkill ?? "") + "|" + hit.Projectile);
    } catch { }
  }

  static void PieceDamage(
      WearNTear __instance, float __0, HitData __1, bool __result) {
    try {
      if (!__result || __0 <= 0f || __instance == null || __1 == null
          || __1.GetAttacker() != Player.m_localPlayer) return;
      var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
      if (zdo == null) return;
      var fields = new Dictionary<string, string> {
        ["weapon_skill"] = __1.m_skill.ToString(),
        ["projectile"] = __1.m_ranged ? "true" : "false",
      };
      var evt = new RuntimeEvent {
        Name = "piece_damaged", Target = __instance.name, SourceId = zdo.m_uid.ToString(),
        At = DateTimeOffset.UtcNow, Fields = fields,
      };
      RuntimeEventRouter.Emit("WearNTear.ApplyDamage(float, HitData)", evt,
          zdo.m_uid.ToString(), __1.m_skill + "|" + __1.m_ranged + "|" + __0);
    } catch { }
  }

  static void SignWritten(Sign __instance, string __0) {
    try {
      if (__instance == null || string.IsNullOrWhiteSpace(__0)
          || Player.m_localPlayer == null) return;
      var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
      if (zdo == null) return;
      var evt = new RuntimeEvent {
        Name = "sign_written", Target = __instance.name, SourceId = zdo.m_uid.ToString(),
        At = DateTimeOffset.UtcNow,
      };
      // Text participates only in the process-local correlation fingerprint; it is never retained.
      RuntimeEventRouter.Emit("Sign.SetText(string)", evt, zdo.m_uid.ToString(),
          __0.GetHashCode().ToString());
    } catch { }
  }

  static DamageState CaptureDamage(Character victim, HitData hit) {
    var state = new DamageState();
    try {
      if (victim == null || victim.IsPlayer() || hit == null
          || hit.GetAttacker() != Player.m_localPlayer) return state;
      if (Hits.Count >= 256) Hits.Clear();
      var next = new LastHit {
        At = UnityEngine.Time.realtimeSinceStartup,
        WeaponSkill = hit.m_skill.ToString(),
        Projectile = hit.m_ranged,
      };
      int id = victim.GetInstanceID();
      Hits.TryGetValue(id, out state.Previous);
      Hits[id] = next;
      state.Victim = victim;
      state.Hit = next;
      state.HealthBefore = victim.GetHealth();
      state.Subject = Identity(victim);
      state.Target = victim.m_name;
      state.Candidate = true;
    } catch { }
    return state;
  }

  static void CompleteDamage(string signature, DamageState state) {
    try {
      if (state == null || !state.Candidate || state.Victim == null) return;
      bool succeeded = CombatHarvestProof.DamageDealt(
          true, !state.Victim.IsPlayer(), state.HealthBefore, state.Victim.GetHealth());
      int id = state.Victim.GetInstanceID();
      if (!succeeded) {
        if (Hits.TryGetValue(id, out var current) && ReferenceEquals(current, state.Hit)) {
          if (state.Previous == null) Hits.Remove(id); else Hits[id] = state.Previous;
        }
        return;
      }
      var evt = CombatHarvestEventContract.DamageDealt(
          state.Target, state.Hit.WeaponSkill, state.Hit.Projectile, DateTimeOffset.UtcNow);
      RuntimeEventRouter.Emit(signature, evt, state.Subject,
          state.Hit.WeaponSkill + "|" + state.Hit.Projectile);
    } catch { }
  }

  static StaggerState CaptureStagger(Character victim) {
    var state = new StaggerState();
    try {
      if (victim == null || victim.IsPlayer()) return state;
      if (!Hits.TryGetValue(victim.GetInstanceID(), out var hit)
          || UnityEngine.Time.realtimeSinceStartup - hit.At > StaggerFreshSeconds) return state;
      state.Hit = hit;
      state.WasStaggering = victim.IsStaggering();
      state.Subject = Identity(victim);
      state.Target = victim.m_name;
      state.Candidate = true;
    } catch { }
    return state;
  }

  static void CompleteStagger(
      string signature, Character victim, StaggerState state, bool accepted) {
    try {
      if (state == null || !state.Candidate || !CombatHarvestProof.CharacterStaggered(
          true, state.WasStaggering, accepted)) return;
      var evt = CombatHarvestEventContract.CharacterStaggered(
          state.Target, state.Hit.WeaponSkill, state.Hit.Projectile, DateTimeOffset.UtcNow);
      RuntimeEventRouter.Emit(signature, evt, state.Subject,
          state.Hit.WeaponSkill + "|" + state.Hit.Projectile);
    } catch { }
  }

  static string Fingerprint(HitData hit) =>
      hit == null ? null : hit.m_skill + "|" + hit.m_ranged;

  static string Identity(UnityEngine.Object value) =>
      value == null ? null : value.GetType().Name + ":" + value.GetInstanceID();
}

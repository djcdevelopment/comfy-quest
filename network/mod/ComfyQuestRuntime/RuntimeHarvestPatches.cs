namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using System.Reflection;
using ComfyQuestContracts;
using HarmonyLib;

/// <summary>Authoritative local-player harvest witnesses with no-op rejection.</summary>
static class RuntimeHarvestPatches {
  sealed class DamageState {
    public ZDO Zdo;
    public float Before;
    public float DefaultHealth;
    public string NamedHealthKey;
    public string SerializedBefore;
    public string Subject;
    public string Target;
    public string WeaponSkill;
    public bool Projectile;
    public bool Serialized;
    public bool Candidate;
    public bool Primary;
    public bool SuppressEmit;
    public int InstanceId;
  }

  sealed class PickState {
    public string Subject;
    public string Target;
    public bool Local;
    public bool Owner;
    public bool WasPicked;
  }

  static readonly Dictionary<int, int> ActivePrimary = new();
  static readonly Dictionary<int, int> MineRock5Frames = new();
  static readonly MethodInfo MineRockArea = AccessTools.Method(
      typeof(MineRock), "GetAreaIndex", new[] { typeof(UnityEngine.Collider) });

  public static void Apply(Harmony harmony) {
    RuntimeCraftingPatches.Apply(harmony);
    RuntimePatching.TryPatchWithState(harmony, typeof(TreeBase), "Damage",
        new[] { typeof(HitData) }, typeof(RuntimeHarvestPatches),
        nameof(TreeBasePrefix), nameof(TreeBaseDamage), "TreeBase.Damage(HitData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(TreeBase), "RPC_Damage",
        new[] { typeof(long), typeof(HitData) }, typeof(RuntimeHarvestPatches),
        nameof(TreeBaseRpcPrefix), nameof(TreeBaseRpcDamage),
        "TreeBase.RPC_Damage(long, HitData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(TreeLog), "Damage",
        new[] { typeof(HitData) }, typeof(RuntimeHarvestPatches),
        nameof(TreeLogPrefix), nameof(TreeLogDamage), "TreeLog.Damage(HitData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(TreeLog), "RPC_Damage",
        new[] { typeof(long), typeof(HitData) }, typeof(RuntimeHarvestPatches),
        nameof(TreeLogRpcPrefix), nameof(TreeLogRpcDamage),
        "TreeLog.RPC_Damage(long, HitData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Destructible), "Damage",
        new[] { typeof(HitData) }, typeof(RuntimeHarvestPatches),
        nameof(DestructiblePrefix), nameof(DestructibleDamage),
        "Destructible.Damage(HitData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Destructible), "RPC_Damage",
        new[] { typeof(long), typeof(HitData) }, typeof(RuntimeHarvestPatches),
        nameof(DestructibleRpcPrefix), nameof(DestructibleRpcDamage),
        "Destructible.RPC_Damage(long, HitData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(MineRock), "Damage",
        new[] { typeof(HitData) }, typeof(RuntimeHarvestPatches),
        nameof(MineRockPrefix), nameof(MineRockDamage), "MineRock.Damage(HitData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(MineRock5), "Damage",
        new[] { typeof(HitData) }, typeof(RuntimeHarvestPatches),
        nameof(MineRock5Prefix), nameof(MineRock5Damage), "MineRock5.Damage(HitData)");
    RuntimePatching.TryPatchWithState(harmony, typeof(MineRock5), "RPC_Damage",
        new[] { typeof(long), typeof(HitData), typeof(int) },
        typeof(RuntimeHarvestPatches), nameof(MineRock5RpcPrefix),
        nameof(MineRock5RpcDamage), "MineRock5.RPC_Damage(long, HitData, int)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Pickable), "Interact",
        new[] { typeof(Humanoid), typeof(bool), typeof(bool) },
        typeof(RuntimeHarvestPatches), nameof(PickableInteractPrefix),
        nameof(PickableInteract), "Pickable.Interact(Humanoid, bool, bool)");
    RuntimePatching.TryPatchWithState(harmony, typeof(Pickable), "RPC_Pick",
        new[] { typeof(long), typeof(int) }, typeof(RuntimeHarvestPatches),
        nameof(PickableRpcPrefix), nameof(PickableRpc), "Pickable.RPC_Pick(long, int)");
  }

  static void TreeBasePrefix(TreeBase __instance, HitData __0, out DamageState __state) =>
      __state = CaptureFloat(__instance, __0, __instance == null ? 0f : __instance.m_health, true);
  static void TreeBaseRpcPrefix(TreeBase __instance, HitData __1, out DamageState __state) =>
      __state = CaptureFloat(__instance, __1, __instance == null ? 0f : __instance.m_health, false);
  static void TreeBaseDamage(DamageState __state) => CompleteFloat("TreeBase.Damage(HitData)", __state);
  static void TreeBaseRpcDamage(DamageState __state) => CompleteFloat("TreeBase.RPC_Damage(long, HitData)", __state);

  static void TreeLogPrefix(TreeLog __instance, HitData __0, out DamageState __state) =>
      __state = CaptureFloat(__instance, __0, __instance == null ? 0f : __instance.m_health, true);
  static void TreeLogRpcPrefix(TreeLog __instance, HitData __1, out DamageState __state) =>
      __state = CaptureFloat(__instance, __1, __instance == null ? 0f : __instance.m_health, false);
  static void TreeLogDamage(DamageState __state) => CompleteFloat("TreeLog.Damage(HitData)", __state);
  static void TreeLogRpcDamage(DamageState __state) => CompleteFloat("TreeLog.RPC_Damage(long, HitData)", __state);

  static void DestructiblePrefix(
      Destructible __instance, HitData __0, out DamageState __state) =>
      __state = CaptureFloat(__instance, __0, DestructibleDefault(__instance), true);
  static void DestructibleRpcPrefix(
      Destructible __instance, HitData __1, out DamageState __state) =>
      __state = CaptureFloat(__instance, __1, DestructibleDefault(__instance), false);
  static void DestructibleDamage(DamageState __state) =>
      CompleteFloat("Destructible.Damage(HitData)", __state);
  static void DestructibleRpcDamage(DamageState __state) =>
      CompleteFloat("Destructible.RPC_Damage(long, HitData)", __state);

  static void MineRockPrefix(MineRock __instance, HitData __0, out DamageState __state) {
    __state = new DamageState();
    try {
      if (__instance == null || __0 == null || __0.m_hitCollider == null || MineRockArea == null) return;
      int area = (int)MineRockArea.Invoke(__instance, new object[] { __0.m_hitCollider });
      if (area < 0) return;
      __state = CaptureFloat(__instance, __0, __instance.m_health, true, "Health" + area);
    } catch { }
  }
  static void MineRockDamage(DamageState __state) =>
      CompleteFloat("MineRock.Damage(HitData)", __state);

  static void MineRock5Prefix(MineRock5 __instance, HitData __0, out DamageState __state) =>
      __state = CaptureSerialized(__instance, __0, true);
  static void MineRock5RpcPrefix(MineRock5 __instance, HitData __1, out DamageState __state) =>
      __state = CaptureMineRock5Rpc(__instance, __1);
  static void MineRock5Damage(DamageState __state) =>
      CompleteSerialized("MineRock5.Damage(HitData)", __state);
  static void MineRock5RpcDamage(DamageState __state) =>
      CompleteSerialized("MineRock5.RPC_Damage(long, HitData, int)", __state);

  static void PickableInteractPrefix(
      Pickable __instance, Humanoid __0, bool ___m_picked,
      ZNetView ___m_nview, out PickState __state) =>
      __state = CapturePick(__instance, __0 == Player.m_localPlayer,
          ___m_nview, ___m_picked);

  static void PickableRpcPrefix(
      Pickable __instance, long __0, bool ___m_picked,
      ZNetView ___m_nview, out PickState __state) =>
      __state = CapturePick(__instance,
          __0 != 0L && ZNet.GetUID() == __0, ___m_nview, ___m_picked);

  static void PickableInteract(bool ___m_picked, PickState __state) =>
      CompletePick("Pickable.Interact(Humanoid, bool, bool)", ___m_picked, __state);

  static void PickableRpc(bool ___m_picked, PickState __state) =>
      CompletePick("Pickable.RPC_Pick(long, int)", ___m_picked, __state);

  static DamageState CaptureFloat(
      UnityEngine.Component subject, HitData hit, float defaultHealth,
      bool primary, string namedHealthKey = null) {
    var state = new DamageState();
    try {
      var view = subject == null ? null : subject.GetComponent<ZNetView>();
      var zdo = view?.GetZDO();
      if (hit == null || hit.GetAttacker() != Player.m_localPlayer
          || view == null || !view.IsValid() || !view.IsOwner() || zdo == null) return state;
      state.Zdo = zdo;
      state.DefaultHealth = defaultHealth;
      state.NamedHealthKey = namedHealthKey;
      state.Before = namedHealthKey == null
          ? zdo.GetFloat(ZDOVars.s_health, defaultHealth)
          : zdo.GetFloat(namedHealthKey, defaultHealth);
      Populate(state, subject, hit, primary);
    } catch { }
    return state;
  }

  static DamageState CaptureSerialized(
      UnityEngine.Component subject, HitData hit, bool primary) {
    var state = new DamageState { Serialized = true };
    try {
      var view = subject == null ? null : subject.GetComponent<ZNetView>();
      var zdo = view?.GetZDO();
      if (hit == null || hit.GetAttacker() != Player.m_localPlayer
          || view == null || !view.IsValid() || !view.IsOwner() || zdo == null) return state;
      state.Zdo = zdo;
      state.SerializedBefore = zdo.GetString(ZDOVars.s_health);
      Populate(state, subject, hit, primary);
    } catch { }
    return state;
  }

  static DamageState CaptureMineRock5Rpc(MineRock5 subject, HitData hit) {
    DamageState state = CaptureSerialized(subject, hit, false);
    try {
      if (state == null || !state.Candidate || state.SuppressEmit) return state;
      int frame = UnityEngine.Time.frameCount;
      if (MineRock5Frames.TryGetValue(state.InstanceId, out int previous) && previous == frame)
        state.SuppressEmit = true;
      else {
        if (MineRock5Frames.Count >= 256) MineRock5Frames.Clear();
        MineRock5Frames[state.InstanceId] = frame;
      }
    } catch { state.SuppressEmit = true; }
    return state;
  }

  static void Populate(
      DamageState state, UnityEngine.Component subject, HitData hit, bool primary) {
    state.InstanceId = subject.GetInstanceID();
    state.Subject = NetworkIdentity(subject);
    state.Target = subject.name;
    state.WeaponSkill = hit.m_skill.ToString();
    state.Projectile = hit.m_ranged;
    state.Primary = primary;
    state.SuppressEmit = !primary && ActivePrimary.ContainsKey(state.InstanceId);
    state.Candidate = true;
    if (primary) BeginPrimary(state.InstanceId);
  }

  static void CompleteFloat(string signature, DamageState state) {
    try {
      if (state == null || !state.Candidate || state.Zdo == null) return;
      float after = state.NamedHealthKey == null
          ? state.Zdo.GetFloat(ZDOVars.s_health, state.DefaultHealth)
          : state.Zdo.GetFloat(state.NamedHealthKey, state.DefaultHealth);
      EmitDamage(signature, state, after < state.Before);
    } catch { }
    finally { EndPrimary(state); }
  }

  static void CompleteSerialized(string signature, DamageState state) {
    try {
      if (state == null || !state.Candidate || state.Zdo == null) return;
      string after = state.Zdo.GetString(ZDOVars.s_health);
      EmitDamage(signature, state,
          !string.Equals(state.SerializedBefore, after, StringComparison.Ordinal));
    } catch { }
    finally { EndPrimary(state); }
  }

  static void EmitDamage(string signature, DamageState state, bool healthChanged) {
    if (state.SuppressEmit || !CombatHarvestProof.ResourceDamaged(
        true, true, healthChanged)) return;
    var evt = CombatHarvestEventContract.ResourceDamaged(
        state.Target, state.WeaponSkill, state.Projectile, DateTimeOffset.UtcNow);
    RuntimeEventRouter.Emit(signature, evt, state.Subject,
        state.WeaponSkill + "|" + state.Projectile);
  }

  static PickState CapturePick(
      Pickable subject, bool local, ZNetView view, bool wasPicked) {
    var state = new PickState();
    try {
      state.Local = local;
      state.Owner = view != null && view.IsValid() && view.IsOwner();
      state.WasPicked = wasPicked;
      state.Subject = NetworkIdentity(subject);
      state.Target = subject == null ? null : subject.name;
    } catch { }
    return state;
  }

  static void CompletePick(string signature, bool pickedAfter, PickState state) {
    try {
      if (state == null || !CombatHarvestProof.ResourcePicked(
          state.Local, state.Owner, state.WasPicked, pickedAfter)) return;
      var evt = CombatHarvestEventContract.ResourcePicked(state.Target, DateTimeOffset.UtcNow);
      RuntimeEventRouter.Emit(signature, evt, state.Subject, evt?.Target);
    } catch { }
  }

  static float DestructibleDefault(Destructible value) {
    try {
      return value == null ? 0f : value.m_health
          + Game.m_worldLevel * value.m_health * Game.instance.m_worldLevelMineHPMultiplier;
    } catch { return value == null ? 0f : value.m_health; }
  }

  static void BeginPrimary(int key) {
    ActivePrimary.TryGetValue(key, out int depth);
    ActivePrimary[key] = depth + 1;
  }

  static void EndPrimary(DamageState state) {
    if (state == null || !state.Primary) return;
    if (!ActivePrimary.TryGetValue(state.InstanceId, out int depth) || depth <= 1)
      ActivePrimary.Remove(state.InstanceId);
    else ActivePrimary[state.InstanceId] = depth - 1;
  }

  static string NetworkIdentity(UnityEngine.Component value) {
    try {
      var zdo = value == null ? null : value.GetComponent<ZNetView>()?.GetZDO();
      if (zdo != null) return zdo.m_uid.ToString();
    } catch { }
    return value == null ? null : value.GetType().Name + ":" + value.GetInstanceID();
  }

  public static void Reset() {
    ActivePrimary.Clear();
    MineRock5Frames.Clear();
  }
}

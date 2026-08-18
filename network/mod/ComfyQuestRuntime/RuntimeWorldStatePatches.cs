namespace ComfyQuestRuntime;

using System;
using System.Globalization;
using ComfyQuestContracts;
using HarmonyLib;

/// <summary>Server-authoritative global-key witnesses with exact no-op rejection.</summary>
static class RuntimeWorldStatePatches {
  sealed class KeyState {
    public ZoneSystem Zone;
    public string Key;
    public bool Authority;
    public bool WasSet;
  }

  public static void Apply(Harmony harmony) {
    RuntimePatching.TryPatchWithState(harmony, typeof(ZoneSystem), "SetGlobalKey",
        new[] { typeof(string) }, typeof(RuntimeWorldStatePatches),
        nameof(SetStringPrefix), nameof(SetString), "ZoneSystem.SetGlobalKey(string)");
    RuntimePatching.TryPatchWithState(harmony, typeof(ZoneSystem), "SetGlobalKey",
        new[] { typeof(GlobalKeys) }, typeof(RuntimeWorldStatePatches),
        nameof(SetEnumPrefix), nameof(SetEnum), "ZoneSystem.SetGlobalKey(GlobalKeys)");
    RuntimePatching.TryPatchWithState(harmony, typeof(ZoneSystem), "SetGlobalKey",
        new[] { typeof(GlobalKeys), typeof(float) }, typeof(RuntimeWorldStatePatches),
        nameof(SetTimedPrefix), nameof(SetTimed),
        "ZoneSystem.SetGlobalKey(GlobalKeys, float)");
    RuntimePatching.TryPatchWithState(harmony, typeof(ZoneSystem), "RPC_SetGlobalKey",
        new[] { typeof(long), typeof(string) }, typeof(RuntimeWorldStatePatches),
        nameof(RpcSetPrefix), nameof(RpcSet),
        "ZoneSystem.RPC_SetGlobalKey(long, string)");
    RuntimePatching.TryPatchWithState(harmony, typeof(ZoneSystem), "RemoveGlobalKey",
        new[] { typeof(string) }, typeof(RuntimeWorldStatePatches),
        nameof(RemoveStringPrefix), nameof(RemoveString),
        "ZoneSystem.RemoveGlobalKey(string)");
    RuntimePatching.TryPatchWithState(harmony, typeof(ZoneSystem), "RemoveGlobalKey",
        new[] { typeof(GlobalKeys) }, typeof(RuntimeWorldStatePatches),
        nameof(RemoveEnumPrefix), nameof(RemoveEnum),
        "ZoneSystem.RemoveGlobalKey(GlobalKeys)");
  }

  static void SetStringPrefix(ZoneSystem __instance, string __0, out KeyState __state) =>
      __state = Capture(__instance, __0);
  static void SetEnumPrefix(ZoneSystem __instance, GlobalKeys __0, out KeyState __state) =>
      __state = Capture(__instance, __0.ToString());
  static void SetTimedPrefix(
      ZoneSystem __instance, GlobalKeys __0, float __1, out KeyState __state) =>
      __state = Capture(__instance,
          $"{__0} {__1.ToString(CultureInfo.InvariantCulture)}");
  static void RpcSetPrefix(
      ZoneSystem __instance, string __1, out KeyState __state) =>
      __state = Capture(__instance, __1);

  static void SetString(KeyState __state) =>
      CompleteSet("ZoneSystem.SetGlobalKey(string)", __state);
  static void SetEnum(KeyState __state) =>
      CompleteSet("ZoneSystem.SetGlobalKey(GlobalKeys)", __state);
  static void SetTimed(KeyState __state) =>
      CompleteSet("ZoneSystem.SetGlobalKey(GlobalKeys, float)", __state);
  static void RpcSet(KeyState __state) =>
      CompleteSet("ZoneSystem.RPC_SetGlobalKey(long, string)", __state);

  static void RemoveStringPrefix(
      ZoneSystem __instance, string __0, out KeyState __state) =>
      __state = Capture(__instance, __0);
  static void RemoveEnumPrefix(
      ZoneSystem __instance, GlobalKeys __0, out KeyState __state) =>
      __state = Capture(__instance, __0.ToString());

  static void RemoveString(KeyState __state) =>
      CompleteRemoved("ZoneSystem.RemoveGlobalKey(string)", __state);
  static void RemoveEnum(KeyState __state) =>
      CompleteRemoved("ZoneSystem.RemoveGlobalKey(GlobalKeys)", __state);

  static KeyState Capture(ZoneSystem zone, string key) {
    var state = new KeyState { Zone = zone, Key = key };
    try {
      state.Authority = zone != null && ZNet.instance != null && ZNet.instance.IsServer();
      state.WasSet = state.Authority && !string.IsNullOrWhiteSpace(key)
        && zone.GetGlobalKeyExact(key);
    } catch { state.Authority = false; }
    return state;
  }

  static void CompleteSet(string signature, KeyState state) {
    try {
      bool isSet = IsSet(state);
      if (state == null || !WorldStateProof.GlobalKeySet(
          state.Authority, state.WasSet, isSet)) return;
      var evt = WorldStateEventContract.GlobalKeySet(state.Key, DateTimeOffset.UtcNow);
      RuntimeEventRouter.Emit(signature, evt, "world-authority", evt?.Target);
    } catch { }
  }

  static void CompleteRemoved(string signature, KeyState state) {
    try {
      bool isSet = IsSet(state);
      if (state == null || !WorldStateProof.GlobalKeyRemoved(
          state.Authority, state.WasSet, isSet)) return;
      var evt = WorldStateEventContract.GlobalKeyRemoved(state.Key, DateTimeOffset.UtcNow);
      RuntimeEventRouter.Emit(signature, evt, "world-authority", evt?.Target);
    } catch { }
  }

  static bool IsSet(KeyState state) => state?.Zone != null
      && !string.IsNullOrWhiteSpace(state.Key)
      && state.Zone.GetGlobalKeyExact(state.Key);
}

namespace ComfyQuestLab;

using HarmonyLib;

/// <summary>Harvest seams: hitting a tree, a felled log, or a bush.
///
/// The worked example for every other category. Copy the shape, not the details: a
/// TryPatch per seam, a postfix that does nothing but describe, and no gameplay
/// consequence anywhere in the file.
///
/// Why harvest is first: punching a bush was this project's first ever trigger, it is
/// what a new builder tries within a minute of loading in, and it is the clearest
/// demonstration of the gap the lab exists to show. The retired ComfyControlSurface
/// hooked exactly these three methods and fired quests on them; the shipping mod hooks
/// none of them, so a bush quest authored today is silently dead.
///
/// The three targets are not interchangeable, and that is the lesson:
///   TreeBase      a standing tree
///   TreeLog       the trunk after it falls — a different type, so a different hook
///   Destructible  bushes, and a great deal else besides
///
/// Shape from comfy/handoffs/comfy-control-surface/Patches/QuestTriggerPatches.cs:16-21.</summary>
public static class HarvestPatches {
  public static void Apply(Harmony harmony) {
    LabPatching.TryPatch(harmony, typeof(TreeBase), "Damage", new[] { typeof(HitData) },
        nameof(TreeBaseDamagePostfix), "TreeBase.Damage");
    LabPatching.TryPatch(harmony, typeof(TreeLog), "Damage", new[] { typeof(HitData) },
        nameof(TreeLogDamagePostfix), "TreeLog.Damage");
    LabPatching.TryPatch(harmony, typeof(Destructible), "Damage", new[] { typeof(HitData) },
        nameof(DestructibleDamagePostfix), "Destructible.Damage");
    LabPatching.TryPatch(harmony, typeof(Pickable), "Interact",
        new[] { typeof(Humanoid), typeof(bool), typeof(bool) },
        nameof(PickableInteractPostfix), "Pickable.Interact");
    LabPatching.TryPatch(harmony, typeof(TreeBase), "RPC_Damage",
        new[] { typeof(long), typeof(HitData) },
        nameof(TreeBaseRpcDamagePostfix), "TreeBase.RPC_Damage");
    LabPatching.TryPatch(harmony, typeof(TreeLog), "RPC_Damage",
        new[] { typeof(long), typeof(HitData) },
        nameof(TreeLogRpcDamagePostfix), "TreeLog.RPC_Damage");
    LabPatching.TryPatch(harmony, typeof(Destructible), "RPC_Damage",
        new[] { typeof(long), typeof(HitData) },
        nameof(DestructibleRpcDamagePostfix), "Destructible.RPC_Damage");
    LabPatching.TryPatch(harmony, typeof(MineRock), "Damage", new[] { typeof(HitData) },
        nameof(MineRockDamagePostfix), "MineRock.Damage");
    LabPatching.TryPatch(harmony, typeof(MineRock5), "Damage", new[] { typeof(HitData) },
        nameof(MineRock5DamagePostfix), "MineRock5.Damage");
    LabPatching.TryPatch(harmony, typeof(MineRock5), "RPC_Damage",
        new[] { typeof(long), typeof(HitData), typeof(int) },
        nameof(MineRock5RpcDamagePostfix), "MineRock5.RPC_Damage");
    LabPatching.TryPatch(harmony, typeof(Pickable), "RPC_Pick",
        new[] { typeof(long), typeof(int) },
        nameof(PickableRpcPickPostfix), "Pickable.RPC_Pick");
  }

  static void TreeBaseDamagePostfix(TreeBase __instance, HitData __0) {
    LabObserve.PlayerHit("TreeBase.Damage(HitData)", __0, __instance,
        LabObserve.Clean(__instance == null ? null : __instance.name) + " (tree)", null);
  }

  static void TreeLogDamagePostfix(TreeLog __instance, HitData __0) {
    LabObserve.PlayerHit("TreeLog.Damage(HitData)", __0, __instance,
        LabObserve.Clean(__instance == null ? null : __instance.name) + " (log)", null);
  }

  static void DestructibleDamagePostfix(Destructible __instance, HitData __0) {
    string prefab = LabObserve.Clean(__instance == null ? null : __instance.name);
    // The retired mod's rule, kept verbatim so a quest that matched there matches here:
    // a Destructible is a "bush" when its prefab name says so. Everything else stays
    // "destructible", which is honest — the type covers a lot of scenery.
    string kind = IsBush(prefab) ? "bush" : "destructible";
    LabObserve.PlayerHit(
        "Destructible.Damage(HitData)", __0, __instance, prefab + " (" + kind + ")", null);
  }

  /// <summary>Berry picking, which is an interact rather than a damage event — the kind
  /// of distinction nobody guesses right and the extractor found for free.</summary>
  static void PickableInteractPostfix(Pickable __instance, Humanoid __0, bool __result) {
    if (!__result) {
      return;   // the interact was refused; nothing happened worth teaching
    }
    string target = LabObserve.Clean(__instance == null ? null : __instance.name)
        + " (pickable)";
    LabObserve.LocalPlayer(
        "Pickable.Interact(Humanoid, bool, bool)", __0 as Character,
        target, "picked", __instance, target);
  }

  static void TreeBaseRpcDamagePostfix(TreeBase __instance, HitData __1) {
    LabObserve.PlayerHit("TreeBase.RPC_Damage(long, HitData)", __1, __instance,
        LabObserve.Clean(__instance == null ? null : __instance.name) + " (tree)", null);
  }

  static void TreeLogRpcDamagePostfix(TreeLog __instance, HitData __1) {
    LabObserve.PlayerHit("TreeLog.RPC_Damage(long, HitData)", __1, __instance,
        LabObserve.Clean(__instance == null ? null : __instance.name) + " (log)", null);
  }

  static void DestructibleRpcDamagePostfix(Destructible __instance, HitData __1) {
    string prefab = LabObserve.Clean(__instance == null ? null : __instance.name);
    string kind = IsBush(prefab) ? "bush" : "destructible";
    LabObserve.PlayerHit(
        "Destructible.RPC_Damage(long, HitData)",
        __1,
        __instance,
        prefab + " (" + kind + ")",
        null);
  }

  static void MineRockDamagePostfix(MineRock __instance, HitData __0) {
    LabObserve.PlayerHit("MineRock.Damage(HitData)", __0, __instance,
        LabObserve.Clean(__instance == null ? null : __instance.name) + " (rock)", null);
  }

  static void MineRock5DamagePostfix(MineRock5 __instance, HitData __0) {
    LabObserve.PlayerHit("MineRock5.Damage(HitData)", __0, __instance,
        LabObserve.Clean(__instance == null ? null : __instance.name) + " (rock)", null);
  }

  static void MineRock5RpcDamagePostfix(MineRock5 __instance, HitData __1) {
    LabObserve.PlayerHit("MineRock5.RPC_Damage(long, HitData, int)", __1, __instance,
        LabObserve.Clean(__instance == null ? null : __instance.name) + " (rock)", null);
  }

  static void PickableRpcPickPostfix(Pickable __instance, long __0) {
    if (!LabEventRouter.IsLocalSender(__0)) {
      return;
    }
    string target = LabObserve.Clean(__instance == null ? null : __instance.name)
        + " (pickable)";
    LabEventRouter.Emit(
        "Pickable.RPC_Pick(long, int)",
        target,
        "picked",
        LabEventRouter.Identity(__instance),
        target);
  }

  static bool IsBush(string prefabName) {
    if (string.IsNullOrEmpty(prefabName)) {
      return false;
    }
    string n = prefabName.ToLowerInvariant();
    return n.Contains("bush") || n.Contains("shrub");
  }
}

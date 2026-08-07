namespace ComfyQuestLab;

using HarmonyLib;

/// <summary>Combat seams — the only category with any quest-usable ground today.
///
/// Worth understanding before anything else, because the three verdicts a builder will
/// see here are all different and the difference is the whole lesson:
///
///   Character.OnDeath     a quest can fire on this today. The one seam that can.
///   Character.Damage      emits first_hit, and no trigger matches first_hit. Visible,
///   Character.RPC_Damage  unusable. This is the trap.
///   everything else       lab only.
///
/// Damage and RPC_Damage are both patched because they are different ownership paths,
/// not alternatives: client-owned melee goes through one and server-routed damage the
/// other. Hooking only one silently loses half your hits.
///
/// OnDeath rather than deciding the kill inside a Damage postfix: IsDead() is still
/// false there. The retired mod got this wrong and the shipping mod's comment records
/// it as proven live.</summary>
public static class CombatPatches {
  public static void Apply(Harmony harmony) {
    LabPatching.TryPatch(harmony, typeof(Character), "Damage", new[] { typeof(HitData) },
        nameof(DamagePostfix), "Character.Damage");
    LabPatching.TryPatch(harmony, typeof(Character), "RPC_Damage",
        new[] { typeof(long), typeof(HitData) },
        nameof(RpcDamagePostfix), "Character.RPC_Damage");
    LabPatching.TryPatch(harmony, typeof(Character), "OnDeath", System.Type.EmptyTypes,
        nameof(OnDeathPostfix), "Character.OnDeath");
    LabPatching.TryPatch(harmony, typeof(Character), "Stagger", new[] { typeof(UnityEngine.Vector3) },
        nameof(StaggerPostfix), "Character.Stagger");
  }

  static void DamagePostfix(Character __instance, HitData __0) {
    LabObserve.PlayerHit("Character.Damage", __0, Describe(__instance), null);
  }

  static void RpcDamagePostfix(Character __instance, HitData __1) {
    LabObserve.PlayerHit("Character.RPC_Damage", __1, Describe(__instance), null);
  }

  /// <summary>The kill. Not filtered through PlayerHit because OnDeath carries no
  /// HitData — attribution is the shipping mod's job, via a last-hit window, and
  /// duplicating that here would teach a builder a rule the real mod does not use.</summary>
  static void OnDeathPostfix(Character __instance) {
    if (__instance == null || __instance.IsPlayer()) {
      return;   // the player dying is progression, not a kill
    }
    LabObserve.Seam("Character.OnDeath", Describe(__instance), "died");
  }

  static void StaggerPostfix(Character __instance) {
    if (__instance == null || __instance.IsPlayer()) {
      return;
    }
    LabObserve.Seam("Character.Stagger", Describe(__instance), "staggered");
  }

  /// <summary>The name a quest would actually match on. QuestTriggerEvaluator does a
  /// case-insensitive substring against exactly this, with (Clone) stripped — so what
  /// the console shows is what a builder should type into trigger.target.</summary>
  static string Describe(Character character) {
    if (character == null) {
      return "unknown";
    }
    return LabObserve.Clean(character.name);
  }
}

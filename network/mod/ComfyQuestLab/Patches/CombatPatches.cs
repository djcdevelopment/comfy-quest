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
    LabKillWatch.RecordPlayerHit(__instance, __0, UnityEngine.Time.realtimeSinceStartup);
  }

  static void RpcDamagePostfix(Character __instance, HitData __1) {
    LabObserve.PlayerHit("Character.RPC_Damage", __1, Describe(__instance), null);
    LabKillWatch.RecordPlayerHit(__instance, __1, UnityEngine.Time.realtimeSinceStartup);
  }

  /// <summary>The kill. Not filtered through PlayerHit because OnDeath carries no
  /// HitData — no weapon, no skill, no attacker. Attribution comes from the last-hit
  /// window in LabKillWatch, which is the same shape the shipping mod's producer uses,
  /// so a builder learns the rule the real mod actually applies.
  ///
  /// Note this is deliberately NOT filtered to the local player: every creature's death
  /// is worth showing, because here the target is the subject. The quest lane is filtered
  /// by construction instead — LabKillWatch only ever holds hits the player landed.</summary>
  static void OnDeathPostfix(Character __instance) {
    if (__instance == null || __instance.IsPlayer()) {
      return;   // the player dying is progression, not a kill
    }
    LabObserve.Seam("Character.OnDeath", Describe(__instance), "died");

    // After the seam row on purpose, so a quest firing reads as a consequence of the
    // kill immediately above it rather than as an unrelated event.
    LabQuestEngine.OnKill(__instance);
  }

  static void StaggerPostfix(Character __instance) {
    if (__instance == null || __instance.IsPlayer()) {
      return;
    }
    LabObserve.Seam("Character.Stagger", Describe(__instance), "staggered");
  }

  /// <summary>The name a quest would actually match on — which is NOT the prefab name.
  ///
  /// This used to return the GameObject name and claim the matcher compared against it.
  /// It does not: the shipping mod hands QuestTriggerEvaluator the creature's m_name, a
  /// localization token. For Neck and Boar the token contains the prefab name and the
  /// claim held by luck; for Greydwarf_Elite the token is $enemy_greydwarfbrute and the
  /// two share nothing, so a builder who typed what the console showed them got a quest
  /// that parsed, errored nowhere, and could never fire.
  ///
  /// Now it shows the matchable name, and adds the prefab name beside it only when they
  /// disagree — so the console is honest about which of the two to type without adding
  /// noise to the creatures where it never mattered.</summary>
  static string Describe(Character character) {
    return LabKillWatch.DisplayName(character);
  }
}

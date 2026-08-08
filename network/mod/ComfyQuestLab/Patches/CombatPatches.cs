namespace ComfyQuestLab;

using System.Collections.Generic;
using System.Globalization;

using HarmonyLib;

/// <summary>Combat witnesses normalized into stable creator events.
///
/// Damage and RPC_Damage are both patched because they are different ownership paths.
/// The central action correlator gives both witnesses one dedupe key when they represent
/// the same hit, while a repeated witness correctly starts a new action.
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
    LabPatching.TryPatch(harmony, typeof(Character), "RPC_Stagger",
        new[] { typeof(long), typeof(UnityEngine.Vector3) },
        nameof(RpcStaggerPostfix), "Character.RPC_Stagger");
    LabPatching.TryPatch(harmony, typeof(Character), "Heal",
        new[] { typeof(float), typeof(bool) },
        nameof(HealPostfix), "Character.Heal");
    LabPatching.TryPatch(harmony, typeof(Character), "RPC_Heal",
        new[] { typeof(long), typeof(float), typeof(bool) },
        nameof(RpcHealPostfix), "Character.RPC_Heal");
    LabPatching.TryPatch(harmony, typeof(Humanoid), "BlockAttack",
        new[] { typeof(HitData), typeof(Character) },
        nameof(BlockAttackPostfix), "Humanoid.BlockAttack");
  }

  static void DamagePostfix(Character __instance, HitData __0) {
    LabObserve.PlayerHit(
        "Character.Damage(HitData)", __0, __instance, Describe(__instance), null);
    LabKillWatch.RecordPlayerHit(__instance, __0, UnityEngine.Time.realtimeSinceStartup);
  }

  static void RpcDamagePostfix(Character __instance, HitData __1) {
    LabObserve.PlayerHit(
        "Character.RPC_Damage(long, HitData)", __1, __instance, Describe(__instance), null);
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
    LabObserve.Seam(
        "Character.OnDeath()", Describe(__instance), "died", __instance, evaluate: false);

    // After the seam row on purpose, so a quest firing reads as a consequence of the
    // kill immediately above it rather than as an unrelated event.
    LabQuestEngine.OnKill(__instance);
  }

  static void StaggerPostfix(Character __instance) {
    if (__instance == null || __instance.IsPlayer()) {
      return;
    }
    ObserveAttributedStagger("Character.Stagger(Vector3)", __instance);
  }

  static void RpcStaggerPostfix(Character __instance) {
    if (__instance == null || __instance.IsPlayer()) {
      return;
    }
    ObserveAttributedStagger("Character.RPC_Stagger(long, Vector3)", __instance);
  }

  static void ObserveAttributedStagger(string signatureId, Character victim) {
    double now = UnityEngine.Time.realtimeSinceStartup;
    bool attributed = LabKillWatch.TryPeekPlayerHit(
        victim, now, 1.5, out string skill, out bool ranged);
    LabEventRouter.Emit(
        signatureId,
        Describe(victim),
        attributed ? "staggered by you" : "staggered",
        LabEventRouter.Identity(victim),
        skill,
        skill,
        ranged,
        evaluate: attributed);
  }

  static void HealPostfix(Character __instance, float __0) {
    ObserveLocalHeal("Character.Heal(float, bool)", __instance, __0);
  }

  static void RpcHealPostfix(Character __instance, float __1) {
    ObserveLocalHeal("Character.RPC_Heal(long, float, bool)", __instance, __1);
  }

  static void ObserveLocalHeal(string signatureId, Character character, float amount) {
    if (character == null || character != Player.m_localPlayer || amount <= 0f) {
      return;
    }
    string value = amount.ToString("R", CultureInfo.InvariantCulture);
    LabEventRouter.Emit(
        signatureId,
        "you",
        "+" + amount.ToString("0.##", CultureInfo.InvariantCulture) + " health",
        LabEventRouter.Identity(character),
        value,
        fields: new Dictionary<string, string> { ["amount"] = value });
  }

  static void BlockAttackPostfix(
      Humanoid __instance, HitData __0, Character __1, bool __result) {
    if (!__result || __instance == null || __instance != Player.m_localPlayer) {
      return;
    }
    string target = __1 == null ? "attacker" : Describe(__1);
    string skill = __0 == null ? null : __0.m_skill.ToString();
    LabEventRouter.Emit(
        "Humanoid.BlockAttack(HitData, Character)",
        target,
        "blocked",
        LabEventRouter.Identity(__instance),
        target + "|" + (skill ?? string.Empty),
        skill,
        __0 != null && __0.m_ranged);
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

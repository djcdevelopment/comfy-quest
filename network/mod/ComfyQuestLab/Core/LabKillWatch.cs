namespace ComfyQuestLab;

using System;
using System.Collections.Generic;

/// <summary>Remembers the local player's last hit on each creature, so a kill can be attributed
/// when it lands.
///
/// <c>Character.OnDeath</c> carries no <see cref="HitData"/> — no weapon, no skill, no attacker —
/// and <c>IsDead()</c> is still false inside a damage postfix, so the kill cannot be decided
/// there either. The only way to hand <c>QuestTriggerEvaluator</c> the three strings it needs is
/// to record them at hit time and consume them at death. That is what the shipping mod's producer
/// does; this is the lab's own minimal version of it, sized to match
/// (<c>GameplayEventProducer.RecentHitSeconds</c> / <c>LastHitMaxEntries</c>).
///
/// Not linked from the producer, and it cannot be: that file is 353 lines of Unity and BepInEx,
/// and the csproj already records why it cannot come across.
///
/// <b>Attribution is implicit and load-bearing.</b> <c>OnDeathPostfix</c> is deliberately not
/// filtered to the local player — the lab shows every creature's death because the target is the
/// subject. The quest lane must be filtered, and it is, by construction: an entry is only ever
/// written when <see cref="ComfyQuestLab.IsLocalPlayerAttacker"/> is true, so no entry means no
/// evaluation. Nothing here needs its own attacker check.
///
/// Main thread only. All three postfixes run on Unity's main thread, so a plain Dictionary is
/// right and the ring's lock would be ceremony.</summary>
public static class LabKillWatch {
  /// <summary>How long a hit stays eligible to explain a death. Matches the producer's window;
  /// long enough for a creature to bleed out, short enough that an unrelated earlier fight
  /// cannot claim a kill somebody else finished.</summary>
  const double FreshSeconds = 15.0;

  /// <summary>Cap on remembered creatures. A cave full of greydwarves would otherwise grow this
  /// for the length of a session.</summary>
  const int MaxEntries = 256;

  struct LastHit {
    /// <summary>What the quest matcher will actually be compared against.</summary>
    public string Creature;

    /// <summary>What <c>questlab_prefabs</c> shows for the same thing. Kept so the console can
    /// print both when they differ, which is the whole reason the mismatch is now visible.</summary>
    public string PrefabName;

    public string WeaponSkill;
    public bool Ranged;
    public double At;
  }

  static readonly Dictionary<int, LastHit> _hits = new Dictionary<int, LastHit>();

  /// <summary>Record a hit the local player landed. Last write wins, which is correct:
  /// <c>Character.Damage</c> and <c>RPC_Damage</c> are two ownership paths for one hit and carry
  /// the same values, so no classifier is needed to tell them apart.</summary>
  public static void RecordPlayerHit(Character victim, HitData hit, double now) {
    try {
      if (victim == null || hit == null || !ComfyQuestLab.IsLocalPlayerAttacker(hit)) {
        return;
      }

      if (_hits.Count >= MaxEntries) {
        Prune(now);
      }

      _hits[victim.GetInstanceID()] = new LastHit {
        Creature = MatcherName(victim),
        PrefabName = LabObserve.Clean(victim.name),
        WeaponSkill = hit.m_skill.ToString(),
        Ranged = hit.m_ranged,
        At = now,
      };
    } catch (Exception) {
    }
  }

  /// <summary>Claim the kill on this creature, if the local player landed a recent enough hit on
  /// it. The entry is consumed either way, so a second <c>OnDeath</c> for the same creature
  /// cannot fire a quest twice.</summary>
  public static bool TryTakeKill(Character victim, double now, out LabKill kill) {
    kill = default(LabKill);
    try {
      if (victim == null) {
        return false;
      }

      int id = victim.GetInstanceID();
      if (!_hits.TryGetValue(id, out LastHit hit)) {
        return false;   // something else killed it, or the player never touched it
      }

      _hits.Remove(id);
      if (now - hit.At > FreshSeconds) {
        return false;   // the player hit it once, long ago; this death is not theirs
      }

      kill = new LabKill {
        Creature = hit.Creature,
        PrefabName = hit.PrefabName,
        WeaponSkill = hit.WeaponSkill,
        Ranged = hit.Ranged,
      };
      return true;
    } catch (Exception) {
      return false;
    }
  }

  public static void Clear() {
    _hits.Clear();
  }

  /// <summary>The name the quest matcher will actually see. The rule lives in
  /// <see cref="LabCreatureNaming"/> — it is pure string work and belongs somewhere a test can
  /// reach it, because it has been wrong once already.</summary>
  public static string MatcherName(Character creature) {
    return creature == null
        ? "unknown"
        : LabCreatureNaming.Normalize(creature.m_name, creature.name);
  }

  /// <summary>How a creature is shown anywhere in the lab: the matchable name, with the prefab
  /// name beside it only when the two disagree.</summary>
  public static string DisplayName(Character creature) {
    if (creature == null) {
      return "unknown";
    }
    return LabCreatureNaming.Display(
        MatcherName(creature), LabCreatureNaming.Clean(creature.name));
  }

  /// <summary>Drop everything already too old to matter; if that frees nothing (a long fight with
  /// many live creatures), drop the oldest so the map stays bounded.</summary>
  static void Prune(double now) {
    var stale = new List<int>();
    foreach (KeyValuePair<int, LastHit> entry in _hits) {
      if (now - entry.Value.At > FreshSeconds) {
        stale.Add(entry.Key);
      }
    }

    if (stale.Count == 0) {
      int oldestId = 0;
      double oldestAt = double.MaxValue;
      foreach (KeyValuePair<int, LastHit> entry in _hits) {
        if (entry.Value.At < oldestAt) {
          oldestAt = entry.Value.At;
          oldestId = entry.Key;
        }
      }
      stale.Add(oldestId);
    }

    foreach (int id in stale) {
      _hits.Remove(id);
    }
  }
}

/// <summary>One attributed kill, reduced to what the evaluator asks for plus the prefab name the
/// console needs to explain a mismatch.</summary>
public struct LabKill {
  public string Creature;
  public string PrefabName;
  public string WeaponSkill;
  public bool Ranged;

  /// <summary>True when the name a builder reads off <c>questlab_prefabs</c> is not a substring of
  /// what the matcher compares against — the case where typing the obvious thing never works.</summary>
  public bool NamesDisagree {
    get { return LabCreatureNaming.NamesDisagree(Creature, PrefabName); }
  }

  /// <summary>How the console shows the target: the matchable name, with the prefab name beside
  /// it only when they differ enough to matter.</summary>
  public string Display {
    get { return LabCreatureNaming.Display(Creature, PrefabName); }
  }
}

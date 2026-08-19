namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using ComfyQuestContracts;

/// <summary>
/// The single Runtime seam that turns live scene state into contract facts: the local witness
/// position stamped onto normalized events, the bound Charm's position, and the current positions
/// and tallies of one workflow's tracked spawned objects. Distance math stays in the pure Contracts
/// SpatialEvaluator and tally meaning stays in AdaptiveEvaluator; binding discovery keeps no radius
/// and this module never filters, orders, or rejects bindings.
///
/// Every tally is read on the world authority, which holds each ZDO whether or not its zone is
/// loaded, so an unresolvable record means the object was destroyed rather than merely distant.
/// </summary>
static class RuntimeObservation {
  const string Prefix = "comfyQuestRuntime.";

  /// <summary>Stamp where the local player stood when this event was witnessed. The shared
  /// privacy boundary bounds and rounds the value; absent players leave the event unstamped so
  /// spatial predicates fail closed.</summary>
  public static void StampLocalPlayer(RuntimeEvent runtimeEvent) {
    try {
      var player = Player.m_localPlayer;
      if (runtimeEvent == null || player == null) return;
      var position = player.transform.position;
      runtimeEvent.PosX = position.x;
      runtimeEvent.PosY = position.y;
      runtimeEvent.PosZ = position.z;
    } catch {
      // Observation is never allowed to disrupt the underlying Valheim action.
    }
  }

  /// <summary>Resolve one binding's workflow facts in a single ledger pass: the binding's own
  /// position, the positions of tracked spawned objects whose identity marks still match, and the
  /// staged-versus-still-present tally per authored spawn action. Unresolvable facts stay null and
  /// fail their predicates closed; a ledger that cannot be read supplies no tally at all.</summary>
  public static ObservedFacts Facts(
      ZDO binding, SpawnExecutionStore spawned, string ownerKey, string contentHash) {
    var facts = new ObservedFacts { Spatial = new SpatialFacts() };
    try {
      if (binding != null) {
        var center = binding.GetPosition();
        facts.Spatial.BindingPosition = new SpatialPoint(center.x, center.y, center.z);
      }
      if (spawned == null || string.IsNullOrWhiteSpace(ownerKey)) return facts;
      if (!spawned.TryForOwner(ownerKey, out var records)) return facts;
      var positions = new List<SpatialPoint>();
      var staged = new Dictionary<string, int>(StringComparer.Ordinal);
      var live = new Dictionary<string, int>(StringComparer.Ordinal);
      foreach (var record in records) {
        if (record == null || record.ContentHash != contentHash
            || string.IsNullOrWhiteSpace(record.ActionId)) continue;
        staged.TryGetValue(record.ActionId, out var stagedCount);
        staged[record.ActionId] = stagedCount + 1;
        var zdo = ZDOMan.instance?.GetZDO(new ZDOID(record.UserId, record.ObjectId));
        if (zdo == null
            || zdo.GetString(Prefix + "spawnedContentHash", "") != record.ContentHash
            || zdo.GetString(Prefix + "spawnedActionId", "") != record.ActionId
            || zdo.GetString(Prefix + "spawnedActionKey", "") != record.ActionKey) continue;
        live.TryGetValue(record.ActionId, out var liveCount);
        live[record.ActionId] = liveCount + 1;
        var at = zdo.GetPosition();
        positions.Add(new SpatialPoint(at.x, at.y, at.z));
      }
      var tallies = new Dictionary<string, SpawnTally>(StringComparer.Ordinal);
      foreach (var pair in staged) {
        live.TryGetValue(pair.Key, out var liveCount);
        tallies[pair.Key] = new SpawnTally(pair.Value, liveCount);
      }
      facts.Spatial.SpawnedPositions = positions;
      facts.Encounter = new EncounterFacts { SpawnsByAction = tallies };
    } catch { }
    return facts;
  }
}

/// <summary>One observation pass, carrying both fact bags the evaluators consume.</summary>
sealed class ObservedFacts {
  public SpatialFacts Spatial { get; set; }
  public EncounterFacts Encounter { get; set; }
}

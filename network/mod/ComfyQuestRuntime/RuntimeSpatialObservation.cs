namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using ComfyQuestContracts;

/// <summary>
/// The single Runtime seam that turns live scene positions into contract facts: the local
/// witness position stamped onto normalized events, the bound Charm's position, and the current
/// positions of one workflow's still-live tracked spawned objects. Distance math stays in the
/// pure Contracts SpatialEvaluator; binding discovery keeps no radius and this module never
/// filters, orders, or rejects bindings.
/// </summary>
static class RuntimeSpatialObservation {
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

  /// <summary>Resolve the live spatial facts for one binding's workflow: the binding's own
  /// position and the positions of tracked spawned objects whose ledger identity marks still
  /// match. Unresolvable facts stay null and fail their predicates closed.</summary>
  public static SpatialFacts Facts(
      ZDO binding, SpawnExecutionStore spawned, string ownerKey, string contentHash) {
    var facts = new SpatialFacts();
    try {
      if (binding != null) {
        var center = binding.GetPosition();
        facts.BindingPosition = new SpatialPoint(center.x, center.y, center.z);
      }
      if (spawned == null || string.IsNullOrWhiteSpace(ownerKey)) return facts;
      var positions = new List<SpatialPoint>();
      foreach (var record in spawned.ForOwner(ownerKey)) {
        var zdo = ZDOMan.instance?.GetZDO(new ZDOID(record.UserId, record.ObjectId));
        if (zdo == null
            || record.ContentHash != contentHash
            || zdo.GetString(Prefix + "spawnedContentHash", "") != record.ContentHash
            || zdo.GetString(Prefix + "spawnedActionId", "") != record.ActionId
            || zdo.GetString(Prefix + "spawnedActionKey", "") != record.ActionKey) continue;
        var at = zdo.GetPosition();
        positions.Add(new SpatialPoint(at.x, at.y, at.z));
      }
      facts.SpawnedPositions = positions;
    } catch { }
    return facts;
  }
}

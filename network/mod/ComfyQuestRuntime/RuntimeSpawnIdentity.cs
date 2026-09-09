namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using System.Linq;
using ComfyQuestContracts;
using HarmonyLib;

/// <summary>Resolve durable spawn marks across Valheim's saved-ZDO ID remapping.</summary>
static class RuntimeSpawnIdentity {
  const string Prefix = "comfyQuestRuntime.";
  static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> ObjectsById =
      AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");

  internal static IEnumerable<ZDO> AuthoritativeObjects() {
    if (ZDOMan.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
      throw new InvalidOperationException("spawn_world_authority_unavailable");
    return ObjectsById(ZDOMan.instance).Values;
  }

  internal static void Mark(ZDO zdo) => zdo.Set(Prefix + "spawnedObjectId", zdo.m_uid.ToString());

  internal static ZDO Resolve(SpawnedObject row) {
    if (row == null || string.IsNullOrWhiteSpace(row.ActionKey))
      throw new InvalidOperationException("spawn_row_missing");
    var identity = new ZDOID(row.UserId, row.ObjectId).ToString();
    bool Matches(ZDO zdo) => zdo != null
        && zdo.GetString(Prefix + "spawnedContentHash", "") == row.ContentHash
        && zdo.GetString(Prefix + "spawnedActionId", "") == row.ActionId
        && zdo.GetString(Prefix + "spawnedActionKey", "") == row.ActionKey;
    var objects = AuthoritativeObjects();
    var direct = ZDOMan.instance.GetZDO(new ZDOID(row.UserId, row.ObjectId));
    if (Matches(direct) && (direct.GetString(Prefix + "spawnedObjectId", "") == identity
        || direct.GetString(Prefix + "spawnedObjectId", "") == "")) return direct;
    var matches = objects.Where(Matches).Where(zdo => {
      var marker = zdo.GetString(Prefix + "spawnedObjectId", "");
      return marker == identity || marker == "";
    }).Take(2).ToArray();
    // Legacy single-object actions can be recovered by their complete action identity.
    // Legacy batches with indistinguishable survivors require explicit recovery.
    if (matches.Length > 1) throw new InvalidOperationException("spawn_identity_ambiguous");
    var found = matches.SingleOrDefault();
    if (found != null && found.GetString(Prefix + "spawnedObjectId", "") == ""
        && new SpawnExecutionStore(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,
            "comfy-quest-runtime")).ForAction(row.ActionKey).Count != 1)
      throw new InvalidOperationException("legacy_spawn_batch_identity_ambiguous");
    return found;
  }
}

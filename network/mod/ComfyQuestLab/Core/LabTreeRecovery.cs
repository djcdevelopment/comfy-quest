namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using UnityEngine;

/// <summary>Recoverably clears natural trees from the generated Gallery footprint.
///
/// The grand court deliberately stays below Valheim's witnessed snow line now. Mature
/// Meadows trees are taller than that deck, so elevation alone cannot solve both visuals.
/// This lane removes only loaded TreeBase roots whose trunks fall inside the generated
/// platform cells, writes their exact prefab/transform ledger before changing the world,
/// and can restore them after the marked gallery is safely gone. It never damages a tree,
/// spawns logs, increments player stats, or accepts an arbitrary prefab/path from a command.
/// </summary>
public static class LabTreeRecovery {
  const string Schema = "comfy-questlab-tree-recovery/v1";
  const float TileHalfWidth = 1f;
  // The committed prefab survey measures Beech1 at 21.5 m across. A trunk just outside
  // the deck can therefore push branches ten metres into a hall; twelve metres clears
  // that witnessed crown with a small tolerance and is still bounded by generated cells.
  const float CanopyMargin = 12f;
  const float DuplicateDistance = 0.35f;

  [Serializable]
  sealed class TreeRecord {
    public string Prefab;
    public int PrefabHash;
    public float X, Y, Z;
    public float Qx, Qy, Qz, Qw;
    public float Sx, Sy, Sz;
    public bool HasHealth;
    public float Health;
  }

  [Serializable]
  sealed class Ledger {
    public string Schema;
    public string PluginRelease;
    public string ProfileId;
    public string BuildId;
    public string CreatedUtc;
    public string RestoredUtc;
    public bool Restored;
    public int RemovedCount;
    public int RestoredCount;
    public List<TreeRecord> Trees = new List<TreeRecord>();
  }

  public sealed class PruneResult {
    public bool Success;
    public int Removed;
    public string LedgerId;
    public string Message;
  }

  static string RecoveryDirectory {
    get {
      return Path.Combine(BepInEx.Paths.ConfigPath,
          Path.Combine("comfy-quest-lab", "tree-recovery"));
    }
  }

  /// <summary>Write-ahead ledger and then directly retire matching natural trees.</summary>
  public static PruneResult Prune(
      LabGalleryPlan.Profile profile,
      Vector3 origin,
      string buildId) {
    var result = new PruneResult {
      Success = true,
      Removed = 0,
      LedgerId = string.Empty,
      Message = "natural-tree pruning is disabled for this profile."
    };
    if (profile == null || !profile.PruneNaturalTrees) {
      return result;
    }
    if (ZNetScene.instance == null || ZDOMan.instance == null) {
      result.Success = false;
      result.Message = "Valheim's world object table is not ready.";
      return result;
    }

    try {
      var candidates = new List<TreeBase>();
      var ledger = new Ledger {
        Schema = Schema,
        PluginRelease = ComfyQuestLab.ReleaseId,
        ProfileId = profile.Id,
        BuildId = buildId,
        CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
      };

      foreach (TreeBase tree in UnityEngine.Object.FindObjectsByType<TreeBase>(
          FindObjectsSortMode.None)) {
        if (tree == null || !InsideFootprint(profile, origin, tree.transform.position)) {
          continue;
        }
        var view = tree.GetComponent<ZNetView>();
        ZDO zdo = view == null ? null : view.GetZDO();
        if (zdo == null || LabGalleryBuilder.IsGalleryPiece(zdo)) {
          continue;
        }
        GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
        if (prefab == null || prefab.GetComponent<TreeBase>() == null) {
          continue;
        }

        Vector3 position = zdo.GetPosition();
        Quaternion rotation = zdo.GetRotation();
        Vector3 scale = tree.transform.localScale;
        float health = zdo.GetFloat(ZDOVars.s_health, float.NaN);
        ledger.Trees.Add(new TreeRecord {
          Prefab = prefab.name,
          PrefabHash = zdo.GetPrefab(),
          X = position.x,
          Y = position.y,
          Z = position.z,
          Qx = rotation.x,
          Qy = rotation.y,
          Qz = rotation.z,
          Qw = rotation.w,
          Sx = scale.x,
          Sy = scale.y,
          Sz = scale.z,
          HasHealth = !float.IsNaN(health),
          Health = float.IsNaN(health) ? 0f : health,
        });
        candidates.Add(tree);
      }

      if (ledger.Trees.Count == 0) {
        result.Message = "no loaded natural TreeBase roots intersected the generated footprint.";
        return result;
      }

      ledger.Trees.Sort((left, right) => {
        int byX = left.X.CompareTo(right.X);
        if (byX != 0) return byX;
        int byZ = left.Z.CompareTo(right.Z);
        return byZ != 0 ? byZ : string.CompareOrdinal(left.Prefab, right.Prefab);
      });

      Directory.CreateDirectory(RecoveryDirectory);
      string ledgerId = NextLedgerId(buildId);
      string path = Path.Combine(RecoveryDirectory, ledgerId + ".json");
      result.LedgerId = ledgerId;
      WriteLedger(path, ledger); // Write before the first world mutation.

      int removed = 0;
      foreach (TreeBase tree in candidates) {
        if (tree == null) {
          continue;
        }
        var view = tree.GetComponent<ZNetView>();
        if (view == null || view.GetZDO() == null) {
          continue;
        }
        view.ClaimOwnership();
        view.Destroy();
        removed++;
      }

      ledger.RemovedCount = removed;
      WriteLedger(path, ledger);
      result.Removed = removed;
      result.Message = "pruned " + removed.ToString(CultureInfo.InvariantCulture)
          + " natural tree(s); recovery ledger " + ledgerId + ".";
      return result;
    } catch (Exception ex) {
      result.Success = false;
      result.Message = "natural-tree pruning stopped before the gallery build: " + ex.Message;
      return result;
    }
  }

  /// <summary>Restore pending ledgers selected only by profile/build id or `all`.</summary>
  public static string Restore(string selector = null) {
    selector = string.IsNullOrWhiteSpace(selector) ? "all" : selector.Trim();
    if (ZNetScene.instance == null || ZDOMan.instance == null) {
      return "tree recovery needs a loaded world.";
    }
    if (!Directory.Exists(RecoveryDirectory)) {
      return "no tree-recovery ledgers exist.";
    }

    int ledgers = 0;
    int restored = 0;
    int alreadyPresent = 0;
    int failed = 0;
    var existing = new List<TreeBase>(UnityEngine.Object.FindObjectsByType<TreeBase>(
        FindObjectsSortMode.None));
    foreach (string path in Directory.GetFiles(RecoveryDirectory, "*.json")) {
      Ledger ledger;
      try {
        ledger = JsonUtility.FromJson<Ledger>(File.ReadAllText(path));
      } catch (Exception) {
        failed++;
        continue;
      }
      if (ledger == null || ledger.Schema != Schema || ledger.Restored
          || !Matches(ledger, selector)) {
        continue;
      }
      ledgers++;
      int ledgerRestored = 0;
      int ledgerFailed = 0;
      foreach (TreeRecord record in ledger.Trees) {
        if (record == null) {
          continue;
        }
        if (AlreadyPresent(existing, record)) {
          alreadyPresent++;
          continue;
        }
        try {
          GameObject prefab = ZNetScene.instance.GetPrefab(record.PrefabHash);
          if (prefab == null || prefab.name != record.Prefab
              || prefab.GetComponent<TreeBase>() == null) {
            ledgerFailed++;
            continue;
          }
          var position = new Vector3(record.X, record.Y, record.Z);
          var rotation = new Quaternion(record.Qx, record.Qy, record.Qz, record.Qw);
          GameObject tree = UnityEngine.Object.Instantiate(prefab, position, rotation);
          if (tree == null || tree.GetComponent<TreeBase>() == null) {
            ledgerFailed++;
            continue;
          }
          var view = tree.GetComponent<ZNetView>();
          if (view != null) {
            Vector3 scale = new Vector3(record.Sx, record.Sy, record.Sz);
            if (scale != Vector3.zero) {
              view.SetLocalScale(scale);
            }
            ZDO zdo = view.GetZDO();
            if (zdo != null && record.HasHealth) {
              zdo.Set(ZDOVars.s_health, record.Health);
            }
          }
          existing.Add(tree.GetComponent<TreeBase>());
          ledgerRestored++;
        } catch (Exception) {
          ledgerFailed++;
        }
      }
      restored += ledgerRestored;
      failed += ledgerFailed;
      if (ledgerFailed == 0) {
        ledger.Restored = true;
        ledger.RestoredUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        ledger.RestoredCount = ledgerRestored;
      }
      try {
        WriteLedger(path, ledger);
      } catch (Exception) {
        failed++;
      }
    }

    if (ledgers == 0) {
      return "no pending tree-recovery ledgers matched '" + selector + "'.";
    }
    return "tree recovery matched " + ledgers.ToString(CultureInfo.InvariantCulture)
        + " ledger(s): restored " + restored.ToString(CultureInfo.InvariantCulture)
        + ", already present " + alreadyPresent.ToString(CultureInfo.InvariantCulture)
        + ", failed " + failed.ToString(CultureInfo.InvariantCulture) + ".";
  }

  public static string Status() {
    if (!Directory.Exists(RecoveryDirectory)) {
      return "no tree-recovery ledgers exist.";
    }
    int pendingLedgers = 0;
    int pendingTrees = 0;
    int restoredLedgers = 0;
    int unreadable = 0;
    foreach (string path in Directory.GetFiles(RecoveryDirectory, "*.json")) {
      try {
        Ledger ledger = JsonUtility.FromJson<Ledger>(File.ReadAllText(path));
        if (ledger == null || ledger.Schema != Schema) {
          unreadable++;
        } else if (ledger.Restored) {
          restoredLedgers++;
        } else {
          pendingLedgers++;
          pendingTrees += ledger.Trees == null ? 0 : ledger.Trees.Count;
        }
      } catch (Exception) {
        unreadable++;
      }
    }
    return pendingLedgers.ToString(CultureInfo.InvariantCulture)
        + " pending tree-recovery ledger(s), "
        + pendingTrees.ToString(CultureInfo.InvariantCulture) + " recorded tree(s); "
        + restoredLedgers.ToString(CultureInfo.InvariantCulture) + " restored, "
        + unreadable.ToString(CultureInfo.InvariantCulture) + " unreadable.";
  }

  static bool InsideFootprint(
      LabGalleryPlan.Profile profile,
      Vector3 origin,
      Vector3 worldPosition) {
    float localX = worldPosition.x - origin.x;
    float localZ = worldPosition.z - origin.z;
    float reach = TileHalfWidth + CanopyMargin;
    foreach (LabGalleryPlan.Tile tile in profile.PlatformTiles) {
      if (Mathf.Abs(localX - tile.X) <= reach && Mathf.Abs(localZ - tile.Z) <= reach) {
        return true;
      }
    }
    return false;
  }

  static bool AlreadyPresent(List<TreeBase> existing, TreeRecord record) {
    float maxDistance = DuplicateDistance * DuplicateDistance;
    foreach (TreeBase tree in existing) {
      if (tree == null) {
        continue;
      }
      var view = tree.GetComponent<ZNetView>();
      ZDO zdo = view == null ? null : view.GetZDO();
      if (zdo == null || zdo.GetPrefab() != record.PrefabHash) {
        continue;
      }
      Vector3 at = zdo.GetPosition();
      float dx = at.x - record.X;
      float dy = at.y - record.Y;
      float dz = at.z - record.Z;
      if (dx * dx + dy * dy + dz * dz <= maxDistance) {
        return true;
      }
    }
    return false;
  }

  static bool Matches(Ledger ledger, string selector) {
    return string.Equals(selector, "all", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ledger.ProfileId, selector, StringComparison.OrdinalIgnoreCase)
        || string.Equals(ledger.BuildId, selector, StringComparison.OrdinalIgnoreCase);
  }

  static string SafeId(string value) {
    if (string.IsNullOrWhiteSpace(value)) {
      return "gallery-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
    }
    var clean = new char[value.Length];
    int n = 0;
    foreach (char c in value) {
      if (char.IsLetterOrDigit(c) || c == '-' || c == '_') {
        clean[n++] = c;
      }
    }
    return n == 0 ? "gallery" : new string(clean, 0, n);
  }

  /// <summary>A comparison build deliberately gives both profiles one build id. Never let
  /// the second footprint replace the first footprint's recovery evidence.</summary>
  static string NextLedgerId(string buildId) {
    string stem = SafeId(buildId);
    string candidate = stem;
    int suffix = 1;
    while (File.Exists(Path.Combine(RecoveryDirectory, candidate + ".json"))) {
      candidate = stem + "-" + suffix.ToString("00", CultureInfo.InvariantCulture);
      suffix++;
    }
    return candidate;
  }

  static void WriteLedger(string path, Ledger ledger) {
    string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
    try {
      File.WriteAllText(temporary, JsonUtility.ToJson(ledger, true) + Environment.NewLine);
      if (File.Exists(path)) {
        File.Replace(temporary, path, null);
      } else {
        File.Move(temporary, path);
      }
    } finally {
      if (File.Exists(temporary)) {
        File.Delete(temporary);
      }
    }
  }
}

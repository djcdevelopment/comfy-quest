namespace ComfyQuestLab;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using HarmonyLib;
using UnityEngine;

/// <summary>Unity provider for exactly one reviewed fixture: Slayers Signature Hunt.
///
/// It accepts no plan data. Before the first mutation it requires one of the two exact reviewed
/// world identities, a local single-player host, every fixed prefab, every expected component,
/// and a natural-terrain position for every placement. It clears and counts only ZDOs carrying
/// this fixture's exact mark. The authored Runtime spawn registry is not involved.</summary>
public sealed class LabSignatureHuntProvider {
  const float DestroySettleSeconds = 5f;
  const int DestroyQuiescenceFrames = 2;
  const int MaxReceiptFiles = 32;

  static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> ObjectsById =
      AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");

  bool _running;
  bool _lastSucceeded;
  string _lastResult = "Slayers Signature Hunt fixture has not run.";
  string _lastReceiptPath;

  public bool IsRunning { get { return _running; } }
  public bool LastLifecycleSucceeded { get { return _lastSucceeded; } }
  public string LastResult { get { return _lastResult; } }
  public string LastReceiptPath { get { return _lastReceiptPath; } }

  static string ReceiptDirectory {
    get {
      return Path.Combine(BepInEx.Paths.ConfigPath,
          Path.Combine("comfy-quest-lab", Path.Combine("receipts", "fixtures")));
    }
  }

  public IEnumerator Prepare(string requestId = null) {
    _lastSucceeded = false;
    if (_running) {
      Finish("signature hunt fixture operation already in progress.", false);
      yield break;
    }

    string precondition = WorldPrecondition();
    if (precondition != null) {
      Finish(precondition, false);
      yield break;
    }
    if (!TryResolvePlan(out List<ResolvedPlacement> plan, out Vector3 origin,
        out string planError)) {
      Finish(planError, false);
      yield break;
    }

    _running = true;
    try {
      int removed = DestroyOwned(out string clearError);
      if (clearError != null) {
        Finish("signature hunt prepare stopped before placement: " + clearError, false);
        yield break;
      }
      IEnumerator settled = WaitForOwnedCountZero();
      while (settled.MoveNext()) yield return settled.Current;
      int afterClear = StandingObjectCount();
      if (afterClear != 0) {
        Finish("signature hunt prepare stopped safely: " + afterClear
            + " fixture-marked object(s) remained after the bounded clear window.", false);
        yield break;
      }

      string preparationId = "signature-hunt-"
          + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)
          + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
      var targets = new List<LabSignatureHuntTargetEvidence>(2);
      LabSignatureHuntBindingEvidence bindingAnchor = null;
      string placementError = null;
      foreach (ResolvedPlacement item in plan) {
        if (!TryPlace(item, preparationId, targets, ref bindingAnchor, out placementError)) break;
      }

      if (placementError != null || targets.Count != 2 || bindingAnchor == null) {
        int partialRemoved = DestroyOwned(out string partialClearError);
        IEnumerator partialSettle = WaitForOwnedCountZero();
        while (partialSettle.MoveNext()) yield return partialSettle.Current;
        string cleanup = partialClearError == null && StandingObjectCount() == 0
            ? "partial fixture removed (" + partialRemoved.ToString(CultureInfo.InvariantCulture)
                + " marked object(s))."
            : "partial fixture cleanup could not be verified: "
                + (partialClearError ?? StandingObjectCount().ToString(CultureInfo.InvariantCulture)
                    + " marked object(s) remain.");
        Finish("signature hunt preparation failed: "
            + (placementError ?? (bindingAnchor == null
                ? "fixture binding anchor identity capture was incomplete"
                : "live target identity capture was incomplete")) + " " + cleanup,
            false);
        yield break;
      }

      int standing = StandingObjectCount();
      if (standing != LabSignatureHuntContract.Placements.Length) {
        int mismatchRemoved = DestroyOwned(out string mismatchClearError);
        IEnumerator mismatchSettle = WaitForOwnedCountZero();
        while (mismatchSettle.MoveNext()) yield return mismatchSettle.Current;
        Finish("signature hunt preparation failed closed: expected "
            + LabSignatureHuntContract.Placements.Length.ToString(CultureInfo.InvariantCulture)
            + " marked objects but found " + standing.ToString(CultureInfo.InvariantCulture)
            + "; removed " + mismatchRemoved.ToString(CultureInfo.InvariantCulture)
            + " from the partial fixture"
            + (mismatchClearError == null ? "." : " (" + mismatchClearError + ")."), false);
        yield break;
      }

      string preparedUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
      var receipt = new LabSignatureHuntFixtureReceipt {
        RequestId = SafeToken(requestId) ? requestId : preparationId,
        PreparationId = preparationId,
        PreparedUtc = preparedUtc,
        Machine = Environment.MachineName,
        PluginVersion = ComfyQuestLab.PluginVersion,
        ReleaseId = ComfyQuestLab.ReleaseId,
        WorldName = CanonicalWorldFileName(),
        WorldUid = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture),
        ExpectedObjectCount = LabSignatureHuntContract.Placements.Length,
        StandingObjectCount = standing,
        OriginX = origin.x,
        OriginY = origin.y,
        OriginZ = origin.z,
        BindingAnchor = bindingAnchor,
        Targets = targets.ToArray(),
      };
      if (!TryWriteReceipt(receipt, out string receiptPath, out string receiptError)) {
        int receiptFailureRemoved = DestroyOwned(out string receiptClearError);
        IEnumerator receiptSettle = WaitForOwnedCountZero();
        while (receiptSettle.MoveNext()) yield return receiptSettle.Current;
        Finish("signature hunt preparation refused an unrecorded fixture: " + receiptError
            + "; removed " + receiptFailureRemoved.ToString(CultureInfo.InvariantCulture)
            + " marked object(s)"
            + (receiptClearError == null ? "." : " (" + receiptClearError + ")."), false);
        yield break;
      }

      _lastReceiptPath = receiptPath;
      Finish("Slayers Signature Hunt ready in " + ZNet.instance.GetWorldName()
          + ": Deathsquito and Drake occupy "
          + LabSignatureHuntContract.ArenaSeparationMetres.ToString("0", CultureInfo.InvariantCulture)
          + " m-separated marked arenas; four Carapace spears are staged at the start. "
          + "Captured exact live Character.m_name identities in " + receiptPath + ".", true);
    } finally {
      _running = false;
    }
  }

  public IEnumerator Clear() {
    _lastSucceeded = false;
    if (_running) {
      Finish("signature hunt fixture operation already in progress.", false);
      yield break;
    }
    string precondition = WorldPrecondition();
    if (precondition != null) {
      Finish(precondition, false);
      yield break;
    }

    _running = true;
    try {
      int removed = DestroyOwned(out string error);
      if (error != null) {
        Finish(error, false);
        yield break;
      }
      IEnumerator settled = WaitForOwnedCountZero();
      while (settled.MoveNext()) yield return settled.Current;
      int remaining = StandingObjectCount();
      if (remaining != 0) {
        Finish("signature hunt clear failed closed: " + remaining
            + " fixture-marked object(s) remained after the bounded destroy window.", false);
        yield break;
      }
      Finish("cleared " + removed.ToString(CultureInfo.InvariantCulture)
          + " Slayers Signature Hunt object(s); non-fixture world objects were not selected.",
          true);
    } finally {
      _running = false;
    }
  }

  public string Status() {
    string precondition = WorldPrecondition();
    if (precondition != null) return precondition;
    int standing = StandingObjectCount();
    if (standing < 0) return "signature hunt fixture ownership table could not be read.";
    string receipt = string.IsNullOrWhiteSpace(_lastReceiptPath)
        ? "no preparation receipt in this plugin session"
        : "receipt " + _lastReceiptPath;
    if (standing == LabSignatureHuntContract.Placements.Length) {
      return "Slayers Signature Hunt ready: " + standing
          + " exact-mark objects standing; " + receipt + ".";
    }
    if (standing == 0) return "Slayers Signature Hunt is clear; " + receipt + ".";
    return "Slayers Signature Hunt is degraded: " + standing + "/"
        + LabSignatureHuntContract.Placements.Length + " exact-mark objects standing; "
        + receipt + ". Prepare replaces only this partial fixture.";
  }

  public int StandingObjectCount() {
    if (!TryCollectOwned(out List<ZDO> owned, out string _)) return -1;
    return owned.Count;
  }

  void Finish(string result, bool succeeded) {
    _lastResult = result;
    _lastSucceeded = succeeded;
    ComfyQuestLab.Report(result);
  }

  static string WorldPrecondition() {
    try {
      if (ZNet.instance == null || ZNetScene.instance == null || ZDOMan.instance == null
          || ZoneSystem.instance == null || Player.m_localPlayer == null) {
        return "signature hunt fixture requires a loaded local world.";
      }
      string worldName = CanonicalWorldFileName();
      string worldUid = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);
      if (!LabSignatureHuntContract.SupportsWorld(worldName, worldUid)) {
        return "signature hunt world mismatch: expected reviewed "
            + LabSignatureHuntContract.ExpectedWorldName + " UID "
            + LabSignatureHuntContract.ExpectedWorldUid + " or "
            + LabSignatureHuntContract.CreatorOsBetaWorldName + " UID "
            + LabSignatureHuntContract.CreatorOsBetaWorldUid + ", found "
            + (string.IsNullOrWhiteSpace(worldName) ? "unknown" : worldName) + " UID "
            + (string.IsNullOrWhiteSpace(worldUid) ? "unknown" : worldUid) + ".";
      }
      if (!ZNet.instance.IsServer() || !ZNet.IsSinglePlayer) {
        return "signature hunt fixture requires a private local reviewed-world host; "
            + "peer and open-server mutation is refused.";
      }
      return null;
    } catch (Exception ex) {
      return "signature hunt world identity unreadable: " + ex.GetType().Name + ".";
    }
  }

  static string CanonicalWorldFileName() {
    // The reviewed identity is a save filename. GetWorldName() returns the
    // editable display label ("Comfy Quest Demo" on the installed AM4 world).
    long uid = ZNet.instance.GetWorldUID();
    string display = ZNet.instance.GetWorldName();
    string[] names = SaveSystem.GetWorldList()
        .Where(world => world != null && world.m_uid == uid
            && string.Equals(world.m_name, display, StringComparison.Ordinal))
        .Select(world => world.m_fileName).Distinct(StringComparer.Ordinal).ToArray();
    return names.Length == 1 ? names[0] : null;
  }

  static bool TryResolvePlan(
      out List<ResolvedPlacement> resolved, out Vector3 origin, out string error) {
    resolved = new List<ResolvedPlacement>(LabSignatureHuntContract.Placements.Length);
    origin = Player.m_localPlayer.transform.position;
    error = null;
    try {
      var checkedPrefabs = new HashSet<string>(StringComparer.Ordinal);
      foreach (LabSignatureHuntPlacement placement in LabSignatureHuntContract.Placements) {
        if (!checkedPrefabs.Add(placement.Prefab)) continue;
        GameObject prefab = ZNetScene.instance.GetPrefab(placement.Prefab);
        if (prefab == null) {
          error = "signature hunt prefab preflight failed: " + placement.Prefab
              + " is unavailable; the standing fixture was not changed.";
          return false;
        }
        if (prefab.GetComponent<ZNetView>() == null) {
          error = "signature hunt prefab preflight failed: " + placement.Prefab
              + " has no ZNetView for durable ownership; the standing fixture was not changed.";
          return false;
        }
      }

      foreach (LabSignatureHuntPlacement placement in LabSignatureHuntContract.Placements) {
        GameObject prefab = ZNetScene.instance.GetPrefab(placement.Prefab);
        if (placement.Kind == LabSignatureHuntContract.TargetKind
            && prefab.GetComponent<Character>() == null) {
          error = "signature hunt prefab preflight failed: " + placement.Prefab
              + " has no Character.m_name to capture; the standing fixture was not changed.";
          return false;
        }
        if (placement.Kind == LabSignatureHuntContract.LoadoutKind
            && prefab.GetComponent<ItemDrop>() == null) {
          error = "signature hunt prefab preflight failed: " + placement.Prefab
              + " is not a usable item drop; the standing fixture was not changed.";
          return false;
        }
      }

      Vector3 forward = Player.m_localPlayer.transform.forward;
      forward.y = 0f;
      if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
      forward.Normalize();
      Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
      Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);
      foreach (LabSignatureHuntPlacement placement in LabSignatureHuntContract.Placements) {
        Vector3 point = origin + right * placement.LocalX + forward * placement.LocalZ;
        if (!ZoneSystem.instance.GetGroundHeight(point, out float ground)) {
          error = "signature hunt terrain preflight failed at " + placement.Role
              + "; the standing fixture was not changed.";
          return false;
        }
        point.y = ground + placement.LocalY;
        resolved.Add(new ResolvedPlacement {
          Contract = placement,
          Position = point,
          Rotation = facing * Quaternion.Euler(0f, placement.Yaw, 0f),
        });
      }
      return true;
    } catch (Exception ex) {
      error = "signature hunt preflight failed: " + ex.GetType().Name + ": " + ex.Message;
      return false;
    }
  }

  static bool TryPlace(
      ResolvedPlacement resolved,
      string preparationId,
      List<LabSignatureHuntTargetEvidence> targets,
      ref LabSignatureHuntBindingEvidence bindingAnchor,
      out string error) {
    error = null;
    LabSignatureHuntPlacement placement = resolved.Contract;
    GameObject placed = null;
    try {
      GameObject prefab = ZNetScene.instance.GetPrefab(placement.Prefab);
      placed = UnityEngine.Object.Instantiate(prefab, resolved.Position, resolved.Rotation);
      if (placed == null) {
        error = "could not instantiate " + placement.Role;
        return false;
      }
      ZNetView view = placed.GetComponent<ZNetView>();
      ZDO zdo = view == null ? null : view.GetZDO();
      if (zdo == null) {
        UnityEngine.Object.Destroy(placed);
        error = placement.Role + " produced no durable ZDO";
        return false;
      }

      // Mark before any later operation can fail. A partial placement therefore remains visible
      // to the same exact-mark cleanup path; transient ids are never treated as ownership.
      zdo.Set(LabSignatureHuntContract.MarkKey, LabSignatureHuntContract.MarkValue);
      zdo.Set(LabSignatureHuntContract.RoleMarkKey, placement.Role);
      zdo.Set(LabSignatureHuntContract.PreparationMarkKey, preparationId);

      Piece piece = placed.GetComponent<Piece>();
      if (piece != null && Player.m_localPlayer != null) {
        piece.SetCreator(Player.m_localPlayer.GetPlayerID());
      }
      if (!string.IsNullOrWhiteSpace(placement.Text)) {
        zdo.Set("text", placement.Text);
      }

      if (string.Equals(placement.Role, "marker-loadout-sign", StringComparison.Ordinal)) {
        if (piece == null || placed.GetComponent<Sign>() == null) {
          error = placement.Role + " was not a bindable sign after instantiation";
          return false;
        }
        bindingAnchor = new LabSignatureHuntBindingEvidence {
          Role = placement.Role,
          TargetKind = "sign",
          ZdoId = ZdoId(zdo),
          X = placed.transform.position.x,
          Y = placed.transform.position.y,
          Z = placed.transform.position.z,
        };
      }

      if (placement.Kind == LabSignatureHuntContract.LoadoutKind) {
        ItemDrop drop = placed.GetComponent<ItemDrop>();
        if (drop == null) {
          error = placement.Role + " was not a usable item drop after instantiation";
          return false;
        }
        drop.m_itemData.m_stack = placement.Stack;
        ItemDrop.SaveToZDO(drop.m_itemData, zdo);
      }

      if (placement.Kind == LabSignatureHuntContract.TargetKind) {
        Character character = placed.GetComponent<Character>();
        if (character == null || string.IsNullOrWhiteSpace(character.m_name)) {
          error = placement.Role + " exposed no live Character.m_name";
          return false;
        }
        string matcherTarget = LabCreatureNaming.Normalize(character.m_name, character.name);
        if (string.IsNullOrWhiteSpace(matcherTarget)
            || string.Equals(matcherTarget, "unknown", StringComparison.OrdinalIgnoreCase)) {
          error = placement.Role + " produced no matchable target identity";
          return false;
        }
        targets.Add(new LabSignatureHuntTargetEvidence {
          Role = placement.Role,
          CreatureLabel = placement.CreatureLabel,
          Prefab = placement.Prefab,
          GameObjectName = character.name,
          RawMName = character.m_name,
          MatcherTarget = matcherTarget,
          ZdoId = ZdoId(zdo),
          X = character.transform.position.x,
          Y = character.transform.position.y,
          Z = character.transform.position.z,
        });
        character.GetBaseAI()?.SetPatrolPoint();
      }
      return true;
    } catch (Exception ex) {
      error = placement.Role + " placement failed: " + ex.GetType().Name + ": " + ex.Message;
      return false;
    }
  }

  static int DestroyOwned(out string error) {
    error = null;
    if (!TryCollectOwned(out List<ZDO> owned, out error)) return 0;
    int removed = 0;
    foreach (ZDO zdo in owned) {
      try {
        // Re-read the mark immediately before mutation. Collection membership and a transient
        // ZDO id are not authority if the object changed between the sweep and this line.
        if (!IsOwned(zdo)) continue;
        ZNetView view = ZNetScene.instance.FindInstance(zdo);
        if (view != null) {
          view.ClaimOwnership();
          view.Destroy();
        } else {
          zdo.SetOwner(ZDOMan.GetSessionID());
          ZDOMan.instance.DestroyZDO(zdo);
        }
        removed++;
      } catch (Exception) {
        // Per-object best effort; the post-destroy exact-mark count decides success.
      }
    }
    return removed;
  }

  static bool TryCollectOwned(out List<ZDO> owned, out string error) {
    owned = new List<ZDO>();
    error = null;
    try {
      if (ZDOMan.instance == null) {
        error = "signature hunt ownership table is unavailable; no object was changed.";
        return false;
      }
      foreach (ZDO zdo in ObjectsById(ZDOMan.instance).Values) {
        if (IsOwned(zdo)) owned.Add(zdo);
      }
      return true;
    } catch (Exception ex) {
      error = "signature hunt ownership sweep failed: " + ex.GetType().Name
          + "; no unverified object was selected.";
      return false;
    }
  }

  static bool IsOwned(ZDO zdo) {
    if (zdo == null) return false;
    try {
      return LabSignatureHuntContract.OwnsMark(
          zdo.GetString(LabSignatureHuntContract.MarkKey, string.Empty));
    } catch (Exception) {
      return false;
    }
  }

  IEnumerator WaitForOwnedCountZero() {
    float deadline = Time.realtimeSinceStartup + DestroySettleSeconds;
    while (StandingObjectCount() > 0 && Time.realtimeSinceStartup < deadline) {
      yield return null;
    }
    if (StandingObjectCount() == 0) {
      for (int frame = 0; frame < DestroyQuiescenceFrames; frame++) yield return null;
    }
  }

  static bool TryWriteReceipt(
      LabSignatureHuntFixtureReceipt receipt,
      out string path,
      out string error) {
    path = null;
    error = null;
    try {
      Directory.CreateDirectory(ReceiptDirectory);
      path = Path.Combine(ReceiptDirectory, receipt.PreparationId + ".json");
      string staging = path + ".writing";
      if (File.Exists(staging)) File.Delete(staging);
      File.WriteAllText(staging, receipt.ToJson(), new System.Text.UTF8Encoding(false));
      if (File.Exists(path)) File.Delete(path);
      File.Move(staging, path);
      PruneReceipts();
      return true;
    } catch (Exception ex) {
      error = "fixture receipt write failed: " + ex.GetType().Name + ": " + ex.Message;
      path = null;
      return false;
    }
  }

  static void PruneReceipts() {
    try {
      FileInfo[] files = new DirectoryInfo(ReceiptDirectory)
          .GetFiles("signature-hunt-*.json", SearchOption.TopDirectoryOnly)
          .OrderByDescending(value => value.LastWriteTimeUtc)
          .ThenByDescending(value => value.Name, StringComparer.Ordinal)
          .ToArray();
      for (int i = MaxReceiptFiles; i < files.Length; i++) files[i].Delete();
    } catch (Exception) {
      // Retention cannot invalidate the receipt just written or the standing fixture.
    }
  }

  static string ZdoId(ZDO zdo) {
    return zdo == null
        ? string.Empty
        : zdo.m_uid.UserID.ToString(CultureInfo.InvariantCulture) + ":"
            + zdo.m_uid.ID.ToString(CultureInfo.InvariantCulture);
  }

  static bool SafeToken(string value) {
    if (string.IsNullOrWhiteSpace(value) || value.Length > 80) return false;
    foreach (char c in value) {
      if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.') return false;
    }
    return true;
  }

  sealed class ResolvedPlacement {
    public LabSignatureHuntPlacement Contract;
    public Vector3 Position;
    public Quaternion Rotation;
  }
}

namespace ComfyQuestLab;

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

using HarmonyLib;

using UnityEngine;

/// <summary>Builds a PlanBuild-format blueprint in the world, and takes it down again.
///
/// Like the gallery, this changes the world and therefore only ever moves when a person
/// types a command. Unlike the gallery, the structure is not compiled in: blueprints are
/// .blueprint files in the mod's config directory, which is what lets a community
/// blueprint — or an offline generator's Fallingwater — reach the world without anyone
/// rebuilding a DLL.
///
/// Four commands, in the order you should use them:
///
///   list    what is in the blueprints directory.
///   check   parse one file, resolve every prefab it names, place nothing. A blueprint
///           downloaded from the internet is exactly the input to distrust; build
///           refuses to run past a failed check.
///   build   raise it at your feet, spread across frames so the game does not hitch.
///   clear   take it down. No manifest: every piece carries the blueprint's name in its
///           own ZDO, and clear sweeps the loaded ZDO table for that mark. Ids are
///           session-scoped; the mark is not — this survives any number of restarts.
///
/// Placement is origin-relative at the player's position with one ground sample: a house
/// keeps its own internal levels, so the blueprint's lowest piece is set just above the
/// ground under your feet and everything else rides at its authored offset. On a slope
/// that means buried or floating edges — pick flat ground.</summary>
public sealed class LabBlueprintBuilder {
  const float GroundClearance = 0.3f;

  /// <summary>Sky mode, the gallery's own move generalized: the build rides this far
  /// above the ground and a portal pair connects it to where the operator was aiming.
  /// High enough that a 12 m building's underside stays clear of trees and rooflines;
  /// low enough to see from the ground.</summary>
  const float SkyLift = 40f;
  const string PortalPrefab = "portal_wood";
  const string PadPrefab = "wood_floor";

  static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> _objectsByIdRef =
      AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");

  bool _running;

  public bool IsRunning { get { return _running; } }

  static string BlueprintsDir {
    get {
      return Path.Combine(BepInEx.Paths.ConfigPath,
          Path.Combine("comfy-quest-lab", "blueprints"));
    }
  }

  // ---- list ------------------------------------------------------------------------

  public string List() {
    try {
      if (!Directory.Exists(BlueprintsDir)) {
        Directory.CreateDirectory(BlueprintsDir);
        return "no blueprints yet. Drop .blueprint files (PlanBuild format) into\n  "
            + BlueprintsDir;
      }
      string[] files = Directory.GetFiles(BlueprintsDir, "*.blueprint");
      if (files.Length == 0) {
        return "no blueprints yet. Drop .blueprint files (PlanBuild format) into\n  "
            + BlueprintsDir;
      }
      Array.Sort(files, StringComparer.OrdinalIgnoreCase);
      var sb = new StringBuilder();
      sb.AppendLine(files.Length + " blueprint(s) in " + BlueprintsDir + ":");
      foreach (string f in files) {
        sb.AppendLine("  " + Path.GetFileNameWithoutExtension(f));
      }
      sb.Append("questlab_blueprint check <name> comes before build, always.");
      return sb.ToString();
    } catch (Exception ex) {
      return "could not list blueprints: " + ex.Message;
    }
  }

  // ---- check -----------------------------------------------------------------------

  public string Check(string name) {
    BlueprintFile bp;
    List<string> problems;
    string fail = Load(name, out bp, out problems);
    if (fail != null) {
      return fail;
    }
    if (ZNetScene.instance == null) {
      return "not in a world yet — load a world first.";
    }

    List<string> missing;
    Dictionary<string, GameObject> prefabs = ResolvePrefabs(bp, out missing);

    var sb = new StringBuilder();
    sb.AppendLine("blueprint check — " + CanonicalName(name)
        + (bp.Name != null ? " (\"" + bp.Name + "\")" : ""));
    sb.AppendLine("  " + bp.BuildablePieceCount + " buildable piece(s), "
        + prefabs.Count + " distinct prefab(s), footprint "
        + (bp.MaxX - bp.MinX).ToString("0.#") + " x " + (bp.MaxZ - bp.MinZ).ToString("0.#")
        + " m, " + (bp.MaxY - bp.MinY).ToString("0.#") + " m tall.");
    if (bp.ScaleRejectedCount > 0) {
      sb.AppendLine("  " + bp.ScaleRejectedCount + " piece(s) carry a non-unit scale and "
          + "will be SKIPPED — vanilla pieces do not scale, and a silently unscaled "
          + "piece is a wrong building.");
    }
    if (bp.SnapPointCount > 0 || bp.TerrainOpCount > 0) {
      sb.AppendLine("  ignoring " + bp.SnapPointCount + " snap point(s) and "
          + bp.TerrainOpCount + " terrain op(s) — the lab does not shape terrain.");
    }
    foreach (string p in problems) {
      sb.AppendLine("  parse: " + p);
    }
    if (missing.Count > 0) {
      sb.AppendLine("MISSING (this game build has no prefab by these names):");
      foreach (string m in missing) {
        sb.AppendLine("  " + m);
      }
      sb.AppendLine("Find the right name with: questlab_prefabs <part of the name>");
      sb.Append("Not ready — build will refuse until the names resolve.");
    } else {
      sb.Append("Ready. questlab_blueprint build " + CanonicalName(name));
    }
    return sb.ToString();
  }

  // ---- build -----------------------------------------------------------------------

  public IEnumerator Build(MonoBehaviour host, string name, bool sky = false) {
    if (_running) {
      Report("already building.");
      yield break;
    }

    BlueprintFile bp;
    List<string> problems;
    string fail = Load(name, out bp, out problems);
    if (fail != null) {
      Report(fail);
      yield break;
    }
    Player player = Player.m_localPlayer;
    if (player == null || ZNetScene.instance == null) {
      Report("not in a world yet.");
      yield break;
    }

    List<string> missing;
    Dictionary<string, GameObject> prefabs = ResolvePrefabs(bp, out missing);
    if (sky) {
      // Sky mode adds its own hardware: the portal pair and the arrival pad.
      foreach (string extra in new[] { PortalPrefab, PadPrefab }) {
        if (!prefabs.ContainsKey(extra)) {
          GameObject pf = ZNetScene.instance.GetPrefab(extra);
          if (pf != null) prefabs[extra] = pf;
          else if (!missing.Contains(extra)) missing.Add(extra);
        }
      }
    }
    if (missing.Count > 0) {
      // check exists so a guess never reaches the world; build inherits that refusal
      // rather than trusting whoever skipped a step.
      Report("refusing to build: " + missing.Count + " prefab name(s) do not resolve. "
          + "Run questlab_blueprint check " + CanonicalName(name) + " for the list.");
      yield break;
    }

    _running = true;
    string mark = CanonicalName(name);

    // Ground mode builds at the player's feet. Sky mode anchors on whatever the
    // crosshair is aimed at — sampled NOW, before anything exists to hit — and rides
    // SkyLift above it, exactly the gallery's raised-platform move.
    Vector3 anchor = player.transform.position;
    if (sky && !TryCursorPoint(out anchor)) {
      anchor = player.transform.position + player.transform.forward * 3f;
      Report("no cursor target within range — anchoring just ahead of you instead.");
    }

    // One ground sample, at the anchor. A house is not the gallery's platform: it keeps
    // its own internal levels, so leveling to the highest ground under the footprint
    // would hoist the whole building by the tallest bump. The lowest authored piece sits
    // just above the ground (or SkyLift above it); a sloped site will bury or float the
    // edges, and the answer to that is picking flat ground, not per-piece sampling.
    float ground;
    if (!TryGroundHeight(anchor, out ground)) {
      ground = anchor.y;
    }
    Vector3 origin = new Vector3(anchor.x, 0f, anchor.z);
    float baseY = ground + (sky ? SkyLift : GroundClearance) - bp.MinY;

    int placed = 0;
    int failed = 0;
    string portalTag = null;
    if (sky) {
      // The ground half of the pair, where the operator was aiming, facing them. The
      // pairing tag is the blueprint's name, so every sky build binds its own doors and
      // two builds never cross-connect. Tag is a plain ZDO string field, per the atlas;
      // the gallery proved tags longer than the UI's ten characters pair fine.
      portalTag = "bp " + mark;
      Vector3 groundDoor = new Vector3(anchor.x, ground, anchor.z);
      Vector3 toPlayer = player.transform.position - groundDoor;
      float yaw = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg;
      GameObject gate = Place(prefabs[PortalPrefab], PortalPrefab, groundDoor,
                              Quaternion.Euler(0f, yaw, 0f), mark);
      if (gate != null) {
        TagPortal(gate, portalTag);
        placed++;
      }
    }

    int total = bp.BuildablePieceCount;
    Report("building " + mark + " — " + total + " pieces, "
        + (bp.MaxX - bp.MinX).ToString("0") + " x " + (bp.MaxZ - bp.MinZ).ToString("0")
        + " m " + (sky ? SkyLift.ToString("0") + " m overhead" : "at your feet")
        + ". Stand back.");

    int attempted = 0;
    int reportStep = Mathf.Max(1, total / 10);
    int perFrame = Mathf.Clamp(LabConfig.BlueprintPiecesPerFrame.Value, 1, 200);

    foreach (BpPiece piece in bp.Pieces) {
      if (piece.ScaleRejected) {
        continue;
      }
      var at = new Vector3(origin.x + piece.PosX, baseY + piece.PosY,
                           origin.z + piece.PosZ);
      var rot = new Quaternion(piece.RotX, piece.RotY, piece.RotZ, piece.RotW);
      // A zero quaternion (all four components 0) is what a hand-edited line produces;
      // Unity would propagate NaNs through the transform rather than complain.
      if (rot.x == 0f && rot.y == 0f && rot.z == 0f && rot.w == 0f) {
        rot = Quaternion.identity;
      }

      if (Place(prefabs[piece.Prefab], piece.Prefab, at, rot, mark) != null) {
        placed++;
      } else {
        failed++;
      }

      attempted++;
      if (attempted % reportStep == 0 && attempted < total) {
        Report(mark + ": " + attempted + "/" + total);
      }
      if (attempted % perFrame == 0) {
        yield return null;
      }
    }

    if (sky) {
      // The arrival: a small pad off the west edge with the sky half of the portal
      // pair. Outside the footprint on purpose — a generic blueprint offers no square
      // metre that is provably empty, and the pad is provably empty because we made it.
      // wood_floor's pivot IS its walking surface (snaps at y=0 in the dump). The
      // blueprint's lowest walking level sits within half a metre of MinY + 0.5
      // whichever way its slabs pivot, so the worst case is a step, not a ledge.
      float zc = Mathf.Round((bp.MinZ + bp.MaxZ) / 2f);
      float padX = origin.x + bp.MinX - 2f;
      float padY = baseY + bp.MinY + 0.5f;
      for (int dz = -1; dz <= 1; dz += 2) {
        if (Place(prefabs[PadPrefab], PadPrefab,
                  new Vector3(padX, padY, origin.z + zc + dz),
                  Quaternion.identity, mark) != null) {
          placed++;
        }
      }
      GameObject door = Place(prefabs[PortalPrefab], PortalPrefab,
                              new Vector3(padX, padY, origin.z + zc),
                              Quaternion.Euler(0f, 90f, 0f), mark);
      if (door != null) {
        TagPortal(door, portalTag);
        placed++;
      }
      yield return null;
    }

    _running = false;
    Report(mark + " raised: " + placed + " piece(s)"
        + (failed > 0 ? ", " + failed + " failed (see the log)" : "")
        + (sky ? ". The portal at your aim point binds \"" + portalTag + "\"" : "")
        + ". questlab_blueprint clear " + mark + " takes it down"
        + (sky ? ", doors and pad included." : "."));
  }

  /// <summary>Instantiate one piece and mark it ours.
  ///
  /// Same shape as the gallery's Place, deliberately copied rather than shared: the
  /// gallery lane is human-verified and stays untouched. The one difference is the mark
  /// — blueprint pieces carry the blueprint's name so clear can be scoped.
  ///
  /// FALSE switches the wear OFF. Both WearNTear flags are opt-INS despite the "no" in
  /// their names — UpdateWear reaches its support check and its rain damage only when
  /// the matching flag is true. Setting them true, which reads correctly, arms exactly
  /// the decay it was meant to prevent; that mistake cost two galleries. The
  /// KeepStandingPostfix re-applies this on every zone rebuild, but it cannot help at
  /// placement time: it runs from Awake, during Instantiate, before the ZDO below has
  /// been marked as ours.</summary>
  GameObject Place(GameObject prefab, string prefabName, Vector3 position,
                   Quaternion rotation, string mark) {
    try {
      GameObject go = UnityEngine.Object.Instantiate(prefab, position, rotation);
      if (go == null) {
        return null;
      }

      var wear = go.GetComponent<WearNTear>();
      if (wear != null) {
        wear.m_noSupportWear = false;
        wear.m_noRoofWear = false;
      }

      var piece = go.GetComponent<Piece>();
      if (piece != null && Player.m_localPlayer != null) {
        piece.SetCreator(Player.m_localPlayer.GetPlayerID());
      }

      var view = go.GetComponent<ZNetView>();
      if (view != null && view.GetZDO() != null) {
        // Marked before the ZDO is shared, so the piece is identifiable as ours in this
        // session and every later one. Clear keys off this, never off an id.
        view.GetZDO().Set(LabMarks.BlueprintMark, mark);
      }
      return go;
    } catch (Exception ex) {
      LogOnce("could not place " + prefabName + ": " + ex.Message);
      return null;
    }
  }

  // ---- count -----------------------------------------------------------------------

  /// <summary>What is actually standing, per prefab — the non-destructive half of the
  /// mark-sweep. Exists because "the build looks wrong" has two very different causes:
  /// pieces that never placed (count low, log has the failures) and pieces you cannot
  /// see (count full — go look again). Same loaded-zones caveat as clear.</summary>
  public string Count(string name) {
    if (ZNetScene.instance == null || ZDOMan.instance == null) {
      return "not in a world yet.";
    }
    string wanted = string.IsNullOrEmpty(name) ? null : CanonicalName(name);

    var byName = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
    try {
      foreach (ZDO zdo in _objectsByIdRef(ZDOMan.instance).Values) {
        string m = LabMarks.BlueprintName(zdo);
        if (m.Length == 0) continue;
        if (wanted != null && !string.Equals(m, wanted, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }
        Dictionary<string, int> prefabs;
        if (!byName.TryGetValue(m, out prefabs)) {
          prefabs = new Dictionary<string, int>(StringComparer.Ordinal);
          byName[m] = prefabs;
        }
        GameObject pf = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
        string pfName = pf != null ? pf.name : ("#" + zdo.GetPrefab());
        int c;
        prefabs.TryGetValue(pfName, out c);
        prefabs[pfName] = c + 1;
      }
    } catch (Exception ex) {
      return "could not read the ZDO table on this game build: " + ex.Message;
    }

    if (byName.Count == 0) {
      return "no blueprint-built pieces in the loaded area. Only loaded zones are "
          + "counted — stand near the build.";
    }
    var sb = new StringBuilder();
    foreach (KeyValuePair<string, Dictionary<string, int>> kv in byName) {
      int total = 0;
      foreach (int c in kv.Value.Values) total += c;
      sb.AppendLine(kv.Key + ": " + total + " piece(s) standing in loaded zones");
      foreach (KeyValuePair<string, int> p in kv.Value) {
        sb.AppendLine("  " + p.Key + " " + p.Value);
      }
    }
    sb.Append("Loaded zones only — stand near the build for a true count.");
    return sb.ToString().TrimEnd();
  }

  // ---- clear -----------------------------------------------------------------------

  /// <summary>Mark-sweep over the loaded ZDO table. This is the fix the gallery's
  /// session-scoped manifest documents wanting: the mark travels with the piece, so a
  /// restart costs nothing. Only loaded zones are swept — stand near what you built.</summary>
  public string Clear(string name) {
    if (ZNetScene.instance == null || ZDOMan.instance == null) {
      return "not in a world yet.";
    }

    string wanted = string.IsNullOrEmpty(name) ? null : CanonicalName(name);

    // Snapshot: the table mutates under us as pieces are destroyed.
    var candidates = new List<ZDO>();
    try {
      foreach (ZDO zdo in _objectsByIdRef(ZDOMan.instance).Values) {
        string m = LabMarks.BlueprintName(zdo);
        if (m.Length == 0) continue;
        if (wanted != null && !string.Equals(m, wanted, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }
        candidates.Add(zdo);
      }
    } catch (Exception ex) {
      return "could not read the ZDO table on this game build: " + ex.Message;
    }

    var removedByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    int removed = 0;
    foreach (ZDO zdo in candidates) {
      try {
        string m = LabMarks.BlueprintName(zdo);
        ZNetView view = ZNetScene.instance.FindInstance(zdo);
        if (view != null) {
          view.ClaimOwnership();
          view.Destroy();
        } else {
          ZDOMan.instance.DestroyZDO(zdo);
        }
        removed++;
        int c;
        removedByName.TryGetValue(m, out c);
        removedByName[m] = c + 1;
      } catch (Exception) {
        // A piece somebody already broke is not an error worth stopping for.
      }
    }

    if (removed == 0) {
      return wanted == null
          ? "no blueprint-built pieces in the loaded area."
          : "no pieces of \"" + wanted + "\" in the loaded area. Only loaded zones are "
            + "swept — if it stands beyond view distance, walk there and clear again.";
    }
    var sb = new StringBuilder();
    sb.Append("cleared " + removed + " piece(s)");
    if (wanted == null && removedByName.Count > 1) {
      sb.Append(" (");
      bool first = true;
      foreach (KeyValuePair<string, int> kv in removedByName) {
        if (!first) sb.Append(", ");
        sb.Append(kv.Key).Append(": ").Append(kv.Value);
        first = false;
      }
      sb.Append(')');
    }
    sb.Append(". Only loaded zones are swept — if part of the build sits beyond view "
        + "distance, walk there and clear again.");
    return sb.ToString();
  }

  // ---- plumbing --------------------------------------------------------------------

  /// <summary>The name the operator typed is the identity: file name, mark value, and
  /// clear argument all use it lowercased, so the three always agree.</summary>
  static string CanonicalName(string name) {
    return (name ?? string.Empty).Trim().ToLowerInvariant();
  }

  static string Load(string name, out BlueprintFile bp, out List<string> problems) {
    bp = null;
    problems = null;
    string canonical = CanonicalName(name);
    if (canonical.Length == 0) {
      return "which one? questlab_blueprint list shows what is available.";
    }
    if (canonical.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
        || canonical.Contains("..")) {
      return "blueprint names are plain file names, no paths.";
    }
    string path = Path.Combine(BlueprintsDir, canonical + ".blueprint");
    if (!File.Exists(path)) {
      return "no blueprint named \"" + canonical + "\" in " + BlueprintsDir
          + " — questlab_blueprint list shows what is there.";
    }
    string[] lines;
    try {
      lines = File.ReadAllLines(path);
    } catch (Exception ex) {
      return "could not read " + path + ": " + ex.Message;
    }
    if (!BlueprintFile.TryParse(lines, out bp, out problems)) {
      var sb = new StringBuilder();
      sb.AppendLine("\"" + canonical + "\" parsed to zero buildable pieces:");
      foreach (string p in problems) {
        sb.AppendLine("  " + p);
      }
      bp = null;
      return sb.ToString().TrimEnd();
    }
    return null;
  }

  Dictionary<string, GameObject> ResolvePrefabs(BlueprintFile bp, out List<string> missing) {
    var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
    missing = new List<string>();
    foreach (BpPiece piece in bp.Pieces) {
      if (piece.ScaleRejected || prefabs.ContainsKey(piece.Prefab)) {
        continue;
      }
      GameObject prefab = ZNetScene.instance.GetPrefab(piece.Prefab);
      if (prefab != null) {
        prefabs[piece.Prefab] = prefab;
      } else if (!missing.Contains(piece.Prefab)) {
        missing.Add(piece.Prefab);
      }
    }
    return prefabs;
  }

  /// <summary>Where the crosshair is aimed: a ray from the game camera through the
  /// screen center. The console being open does not move the camera, so "what you were
  /// looking at when you typed it" is exactly what this returns. No layer mask — the
  /// operator aims at ground or at a roof, and either is a legitimate anchor.</summary>
  static bool TryCursorPoint(out Vector3 point) {
    point = Vector3.zero;
    try {
      if (GameCamera.instance == null) {
        return false;
      }
      Transform cam = GameCamera.instance.transform;
      RaycastHit hit;
      if (Physics.Raycast(cam.position, cam.forward, out hit, 200f)) {
        point = hit.point;
        return true;
      }
      return false;
    } catch (Exception) {
      return false;
    }
  }

  /// <summary>Two portals sharing a tag connect. The tag is a plain ZDO string field
  /// ("tag"), straight out of the component atlas — the same proven shape the gallery
  /// uses, written before the ZDO is shared.</summary>
  void TagPortal(GameObject go, string tag) {
    try {
      var view = go.GetComponent<ZNetView>();
      if (view == null || view.GetZDO() == null) {
        return;
      }
      view.GetZDO().Set("tag", tag);
    } catch (Exception ex) {
      LogOnce("could not tag a portal: " + ex.Message);
    }
  }

  static bool TryGroundHeight(Vector3 at, out float height) {
    height = at.y;
    try {
      if (ZoneSystem.instance == null) {
        return false;
      }
      return ZoneSystem.instance.GetSolidHeight(at, out height);
    } catch (Exception) {
      return false;
    }
  }

  readonly HashSet<string> _logged = new HashSet<string>();

  void LogOnce(string message) {
    if (_logged.Add(message)) {
      ComfyQuestLab.LogInfo("[blueprint] " + message);
    }
  }

  static void Report(string message) {
    ComfyQuestLab.LogInfo("[blueprint] " + message);
    try {
      if (MessageHud.instance != null) {
        MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message);
      }
    } catch (Exception) {
    }
  }
}

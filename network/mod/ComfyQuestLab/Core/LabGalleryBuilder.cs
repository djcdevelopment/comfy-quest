namespace ComfyQuestLab;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

/// <summary>Raises the gallery described by <see cref="LabGalleryPlan"/>.
///
/// This is the one part of the Tome that changes the world rather than witnessing it,
/// and it only ever does so when a person types a command. Nothing here runs on its own.
///
/// Three commands, in the order you should use them:
///
///   check   resolve every prefab the plan names and report what is missing. Places
///           nothing. Prefab names are the one thing in this project NOT read out of the
///           game assembly, so this exists to turn a guess into a fact before 600 pieces
///           are committed to somebody's world.
///   build   raise it, spread across frames so the game does not hitch.
///   clear   take it down again, using a manifest written at build time.
///
/// A gallery is raised at the player's feet, so the plan stays origin-relative and the
/// Tome is not tied to one world.</summary>
public sealed class LabGalleryBuilder {
  const int PiecesPerFrame = 12;
  const float PlatformClearance = 0.6f;   // floor sits this far above the highest ground

  /// <summary>Portal tags. Two portals sharing a tag connect to each other; the pairing
  /// is a plain ZDO string field, per the component atlas.</summary>
  const string GalleryTag = "gallery";
  const string WorldTag = "gallery world";

  /// <summary>Materials, so the gallery can be extended, repaired, or stair-cased by
  /// hand. Stacks rather than singles — a student who wants to build should not have to
  /// go logging first.</summary>
  static readonly string[] Supplies = { "Wood", "Wood", "FineWood", "RoundLog", "Stone" };

  readonly List<ZDOID> _placed = new List<ZDOID>();
  bool _running;

  public bool IsRunning { get { return _running; } }

  static string ManifestPath {
    get {
      return Path.Combine(BepInEx.Paths.ConfigPath,
          Path.Combine("comfy-quest-lab", "gallery-manifest.txt"));
    }
  }

  // ---- check -----------------------------------------------------------------------

  /// <summary>Resolve everything the plan names, place nothing, and say what is missing.
  /// Run this first, always.</summary>
  public string Check() {
    if (ZNetScene.instance == null) {
      return "not in a world yet — load a world first.";
    }

    var wanted = new List<string> { "wood_floor", "wood_beam", "wood_pole" };
    foreach (LabGalleryPlan.Monument m in LabGalleryPlan.Monuments) {
      if (!wanted.Contains(m.Station.Prefab)) {
        wanted.Add(m.Station.Prefab);
      }
    }
    wanted.Add("itemstand");
    foreach (LabGalleryPlan.RackItem r in LabGalleryPlan.Armoury) {
      if (!wanted.Contains(r.Item)) {
        wanted.Add(r.Item);
      }
    }

    var found = new List<string>();
    var missing = new List<string>();
    foreach (string name in wanted) {
      (ZNetScene.instance.GetPrefab(name) != null ? found : missing).Add(name);
    }

    var sb = new StringBuilder();
    sb.AppendLine("gallery check — " + found.Count + " of " + wanted.Count + " prefabs resolved");
    if (missing.Count > 0) {
      sb.AppendLine("MISSING (the plan names these and this game build does not have them):");
      foreach (string name in missing) {
        sb.AppendLine("  " + name);
      }
      sb.AppendLine("Find the right name with: questlab_prefabs <part of the name>");
    }
    sb.AppendLine(LabGalleryPlan.PlatformTiles.GetLength(0) + " floor tiles, "
        + CountBeams() + " beams, " + LabGalleryPlan.Armoury.Length + " stands.");
    sb.Append(missing.Count == 0
        ? "Ready. questlab_gallery build"
        : "Not ready — fix the names above first.");
    return sb.ToString();
  }

  static int CountBeams() {
    int n = 0;
    foreach (LabGalleryPlan.Monument m in LabGalleryPlan.Monuments) {
      n += m.Beams.Length;
    }
    return n;
  }

  /// <summary>Search what this game build actually has. The honest way to fix a name the
  /// plan got wrong, rather than guessing again.</summary>
  public static string SearchPrefabs(string needle) {
    if (ZNetScene.instance == null) {
      return "not in a world yet.";
    }
    if (string.IsNullOrEmpty(needle) || needle.Length < 2) {
      return "give me at least two characters: questlab_prefabs floor";
    }

    needle = needle.ToLowerInvariant();
    var hits = new List<string>();
    foreach (GameObject prefab in ZNetScene.instance.m_prefabs) {
      if (prefab != null && prefab.name.ToLowerInvariant().Contains(needle)) {
        hits.Add(prefab.name);
      }
    }
    hits.Sort(StringComparer.OrdinalIgnoreCase);

    var sb = new StringBuilder();
    sb.AppendLine(hits.Count + " prefab(s) matching \"" + needle + "\"");
    for (int i = 0; i < hits.Count && i < 40; i++) {
      sb.AppendLine("  " + hits[i]);
    }
    if (hits.Count > 40) {
      sb.Append("  … and " + (hits.Count - 40) + " more; narrow the search.");
    }
    return sb.ToString().TrimEnd();
  }

  // ---- build -----------------------------------------------------------------------

  public IEnumerator Build(MonoBehaviour host) {
    if (_running) {
      Report("already building.");
      yield break;
    }
    Player player = Player.m_localPlayer;
    if (player == null || ZNetScene.instance == null) {
      Report("not in a world yet.");
      yield break;
    }

    _running = true;
    _placed.Clear();
    Vector3 origin = player.transform.position;

    // One height for the whole platform: the highest ground under the footprint, plus a
    // clearance. A level floor on uneven ground is the entire point — sampling per tile
    // would reproduce the hillside the platform exists to hide.
    float top = origin.y;
    int tiles = LabGalleryPlan.PlatformTiles.GetLength(0);
    for (int i = 0; i < tiles; i += 7) {   // every seventh tile is plenty to find the max
      Vector3 at = origin + new Vector3(LabGalleryPlan.PlatformTiles[i, 0], 0f,
                                        LabGalleryPlan.PlatformTiles[i, 1]);
      float ground;
      if (TryGroundHeight(at, out ground) && ground > top) {
        top = ground;
      }
    }
    float floorY = top + PlatformClearance;
    Report("raising the gallery — floor at " + (floorY - origin.y).ToString("0.0")
        + " m above you. Stand back.");

    int placed = 0;

    // 1. the floor
    for (int i = 0; i < tiles; i++) {
      Vector3 at = new Vector3(origin.x + LabGalleryPlan.PlatformTiles[i, 0], floorY,
                               origin.z + LabGalleryPlan.PlatformTiles[i, 1]);
      if (Place("wood_floor", at, Quaternion.identity)) {
        placed++;
      }
      if (placed % PiecesPerFrame == 0) {
        yield return null;
      }
    }

    // 1b. a way up.
    //
    // The floor levels to the HIGHEST ground under the footprint, which is what makes it
    // level — and on any ground with a rise in it you end up standing underneath your own
    // gallery. That is exactly what the first real build did.
    //
    // A portal pair rather than stairs: it is instant, it is the right idiom for a tome,
    // and taking it fires Player.TeleportTo — a World-school spell — so the first thing
    // anyone does in the gallery already makes the live view move.
    //
    // The pairing is a ZDO string field called "tag", read straight out of the component
    // atlas, so there is no RPC signature to guess at here.
    Vector3 arrival = origin + new Vector3(2f, 0f, 0f);
    float arrivalGround;
    if (TryGroundHeight(arrival, out arrivalGround)) {
      arrival.y = arrivalGround;
    }
    if (PlacePortal(arrival, GalleryTag, 0f)) {
      placed++;
    }
    if (PlacePortal(new Vector3(origin.x + 3.5f, floorY, origin.z), GalleryTag, 180f)) {
      placed++;
    }
    // The far end of the World school's own portal, so "take a portal" is a thing a
    // student can actually do from the plaza rather than a thing they read about.
    if (PlacePortal(new Vector3(origin.x - 3.5f, floorY, origin.z), WorldTag, 180f)) {
      placed++;
    }
    yield return null;

    // 2. the monuments, and the station on each pad
    foreach (LabGalleryPlan.Monument monument in LabGalleryPlan.Monuments) {
      foreach (LabGalleryPlan.Beam beam in monument.Beams) {
        var at = new Vector3(origin.x + beam.X, floorY + beam.Y, origin.z + beam.Z);
        var along = new Vector3(beam.Dx, beam.Dy, beam.Dz);
        // A wood beam runs along its local Z, so look down the stroke. Up is world-up
        // except for a vertical stroke, where any perpendicular will do.
        Vector3 up = Mathf.Abs(along.y) > 0.95f ? Vector3.forward : Vector3.up;
        if (Place("wood_beam", at, Quaternion.LookRotation(along, up))) {
          placed++;
        }
        if (placed % PiecesPerFrame == 0) {
          yield return null;
        }
      }

      var stationAt = new Vector3(origin.x + monument.Station.X, floorY,
                                  origin.z + monument.Station.Z);
      GameObject station = Place(monument.Station.Prefab, stationAt, Quaternion.identity);
      if (station != null) {
        placed++;
        if (monument.Station.Prefab == "portal_wood") {
          TagPortal(station, WorldTag);
        }
      }
      yield return null;
    }

    // 3. the armoury
    foreach (LabGalleryPlan.RackItem item in LabGalleryPlan.Armoury) {
      var at = new Vector3(origin.x + item.X, floorY, origin.z + item.Z);
      GameObject stand = Place("itemstand", at, Quaternion.Euler(0f, item.Yaw, 0f));
      if (stand != null) {
        placed++;
        // ItemStand exposes SetVisualItem only as an RPC, and an RPC signature is not
        // something to guess at from outside the game. Try it, and do not care if it
        // fails — the real gear is placed below either way, so a bare stand is a
        // cosmetic loss rather than a student with no axe.
        TryDressStand(stand, item.Item);
      }

      // The gear itself, on the floor beside its stand. Instantiating an item prefab
      // gives a real ItemDrop, which needs no API this could get wrong — and picking it
      // up is itself one of the inventory school's spells, so the first thing a student
      // does in the gallery already makes the live view move.
      var loose = new Vector3(origin.x + item.X * 0.82f, floorY + 0.4f,
                              origin.z + item.Z * 0.82f);
      if (Place(item.Item, loose, Quaternion.identity) != null) {
        placed++;
      }
      yield return null;
    }

    // Materials, so the gallery can be extended or repaired by hand without a
    // logging trip first.
    for (int i = 0; i < Supplies.Length; i++) {
      float a = 2f * Mathf.PI * i / Supplies.Length;
      var at = new Vector3(origin.x + Mathf.Sin(a) * 3.0f, floorY + 0.4f,
                           origin.z + Mathf.Cos(a) * 3.0f);
      GameObject drop = Place(Supplies[i], at, Quaternion.identity);
      if (drop != null) {
        placed++;
        SetStack(drop, 50);
      }
      yield return null;
    }

    SaveManifest();
    _running = false;
    Report("gallery raised: " + placed + " pieces. questlab_gallery clear takes it down.");
  }

  /// <summary>Instantiate one piece and remember it.
  ///
  /// Support wear is switched off on everything placed. A platform standing clear of the
  /// ground has nothing holding it up as far as the game is concerned, and would begin
  /// collapsing within minutes — which is a spectacular way to lose a student.</summary>
  GameObject Place(string prefabName, Vector3 position, Quaternion rotation) {
    try {
      GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
      if (prefab == null) {
        LogOnce("no prefab named " + prefabName + " — run questlab_gallery check");
        return null;
      }

      GameObject go = UnityEngine.Object.Instantiate(prefab, position, rotation);
      if (go == null) {
        return null;
      }

      var wear = go.GetComponent<WearNTear>();
      if (wear != null) {
        wear.m_noSupportWear = true;
        wear.m_noRoofWear = true;
      }

      var piece = go.GetComponent<Piece>();
      if (piece != null && Player.m_localPlayer != null) {
        // Attribute it, so the gallery shows up as the student's own work rather than
        // as ownerless scenery.
        piece.SetCreator(Player.m_localPlayer.GetPlayerID());
      }

      var view = go.GetComponent<ZNetView>();
      if (view != null && view.GetZDO() != null) {
        _placed.Add(view.GetZDO().m_uid);
      }
      return go;
    } catch (Exception ex) {
      LogOnce("could not place " + prefabName + ": " + ex.Message);
      return null;
    }
  }

  // ---- clear -----------------------------------------------------------------------

  public string Clear() {
    if (ZNetScene.instance == null) {
      return "not in a world yet.";
    }
    LoadManifestIfEmpty();
    if (_placed.Count == 0) {
      return "nothing recorded to clear. The manifest is written at build time; if you "
           + "built on another character or wiped it, take the pieces down by hand.";
    }

    int removed = 0;
    foreach (ZDOID id in _placed) {
      try {
        ZDO zdo = ZDOMan.instance.GetZDO(id);
        if (zdo == null) {
          continue;
        }
        ZNetView view = ZNetScene.instance.FindInstance(zdo);
        if (view != null) {
          view.ClaimOwnership();
          view.Destroy();
          removed++;
        } else {
          ZDOMan.instance.DestroyZDO(zdo);
          removed++;
        }
      } catch (Exception) {
        // A piece somebody already broke is not an error worth stopping for.
      }
    }
    _placed.Clear();
    SaveManifest();
    return "cleared " + removed + " piece(s).";
  }

  // ---- manifest --------------------------------------------------------------------

  void SaveManifest() {
    try {
      Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath));
      var sb = new StringBuilder();
      foreach (ZDOID id in _placed) {
        sb.Append(id.UserID.ToString(CultureInfo.InvariantCulture)).Append(':')
          .Append(id.ID.ToString(CultureInfo.InvariantCulture)).Append('\n');
      }
      File.WriteAllText(ManifestPath, sb.ToString());
    } catch (Exception ex) {
      LogOnce("could not write the gallery manifest: " + ex.Message);
    }
  }

  void LoadManifestIfEmpty() {
    if (_placed.Count > 0 || !File.Exists(ManifestPath)) {
      return;
    }
    try {
      foreach (string line in File.ReadAllLines(ManifestPath)) {
        string[] parts = line.Split(':');
        long user;
        uint id;
        if (parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out user)
            && uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) {
          _placed.Add(new ZDOID(user, id));
        }
      }
    } catch (Exception ex) {
      LogOnce("could not read the gallery manifest: " + ex.Message);
    }
  }

  // ---- plumbing --------------------------------------------------------------------

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

  /// <summary>Place a portal and tag it. Two portals with the same tag connect.
  ///
  /// The tag is a ZDO string field ("tag"), which the component atlas reports directly —
  /// so this needs no RPC and no guessed signature. Written before the ZDO is shared so
  /// the pairing is there from the first frame.</summary>
  bool PlacePortal(Vector3 at, string tag, float yaw) {
    GameObject go = Place("portal_wood", at, Quaternion.Euler(0f, yaw, 0f));
    if (go == null) {
      return false;
    }
    TagPortal(go, tag);
    return true;
  }

  /// <summary>Make a dropped item a stack rather than a single. Best effort: if the
  /// field shape differs on a game build, one log is worth more than a crash.</summary>
  void SetStack(GameObject go, int count) {
    try {
      var drop = go.GetComponent<ItemDrop>();
      if (drop == null || drop.m_itemData == null) {
        return;
      }
      drop.m_itemData.m_stack = Mathf.Min(count, drop.m_itemData.m_shared.m_maxStackSize);
      // No Save() on ItemDrop — the stack set before the ZDO is shared persists on its own.
    } catch (Exception ex) {
      LogOnce("could not stack a supply drop: " + ex.Message);
    }
  }

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

  /// <summary>Ask a stand to display an item, without depending on being right.
  ///
  /// The extractor says SetVisualItem is a registered RPC rather than a callable method,
  /// and its parameter list is not something to guess at from outside a running game. So
  /// this attempts the most likely shape, gives up quietly, and says so once.</summary>
  void TryDressStand(GameObject stand, string itemName) {
    try {
      var view = stand.GetComponent<ZNetView>();
      if (view == null) {
        return;
      }
      view.InvokeRPC(ZNetView.Everybody, "SetVisualItem", new object[] { itemName, 0, 1 });
    } catch (Exception ex) {
      LogOnce("stands are staying bare — SetVisualItem did not take (" + ex.Message
          + "). The gear is on the floor beside them, which is what matters.");
    }
  }

  readonly HashSet<string> _logged = new HashSet<string>();

  /// <summary>Six hundred pieces means six hundred chances to say the same thing. Say it
  /// once.</summary>
  void LogOnce(string message) {
    if (_logged.Add(message)) {
      ComfyQuestLab.LogInfo("[gallery] " + message);
    }
  }

  static void Report(string message) {
    ComfyQuestLab.LogInfo("[gallery] " + message);
    try {
      if (MessageHud.instance != null) {
        MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message);
      }
    } catch (Exception) {
    }
  }
}

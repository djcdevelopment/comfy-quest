namespace ComfyQuestLab;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using HarmonyLib;

using UnityEngine;

/// <summary>Raises the gallery described by <see cref="LabGalleryPlan"/>.
///
/// This is the one part of the Tome that changes the world rather than witnessing it,
/// and it only ever does so after a typed command or validated bounded batch request. Nothing
/// here runs on its own.
///
/// Gallery v2 commands, in the order you should use them:
///
///   check   resolve every prefab the plan names and report what is missing. Places
///           nothing. Prefab names are the one thing in this project NOT read out of the
///           game assembly, so this exists to turn a guess into a fact before 1,350 pieces
///           are committed to somebody's world.
///   profiles list the reversible geometry choices and their exact generated counts.
///   build   raise one profile, spread across frames so the game does not hitch.
///   compare raise two labelled profiles side by side for human judgement.
///   identify report the profile/build marks on loaded gallery structures.
///   clear   take down only marked gallery objects, optionally by profile or build id.
///   rebuild clear one profile and raise it again at the player's current position.
///
/// A gallery is raised at the player's feet, so the plan stays origin-relative and the
/// Tome is not tied to one world.</summary>
public sealed class LabGalleryBuilder {

  /// <summary>Portal tags. Two portals sharing a tag connect to each other; the pairing
  /// is a plain ZDO string field, per the component atlas.</summary>
  const string GalleryTag = "gallery";
  const string WorldTag = "gallery world";

  readonly List<ZDOID> _placed = new List<ZDOID>();
  static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> _objectsByIdRef =
      AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");
  bool _running;
  string _lastLifecycleResult = "no gallery lifecycle command has run yet.";
  bool _lastLifecycleSucceeded;
  string _activeProfileId = "legacy";
  string _activeBuildId = "legacy";
  int _buildSequence;

  /// <summary>A mark every gallery piece carries in its own ZDO.
  ///
  /// A ZDOID cannot answer "is this ours". ZDO ids are session-scoped: the manifest
  /// written while raising a gallery records ids from that session, and after a reload
  /// those same numbers belong to entirely unrelated objects. Trusting them across a
  /// restart is how a "clear" ends up destroying somebody's player.
  ///
  /// The mark travels with the object instead, saved in the world like any other ZDO
  /// field — the same mechanism the portals use for their pairing tag, so it is a proven
  /// shape rather than a guessed one. It answers the question in any session.</summary>
  const string GalleryMark = "comfyQuestLabGallery";
  const string GalleryProfileMark = "comfyQuestLabGalleryProfile";
  const string GalleryBuildMark = "comfyQuestLabGalleryBuild";

  /// <summary>Is this object part of a gallery the Tome raised? Used both to decide what
  /// clear may destroy and to keep support wear off after a zone reload.</summary>
  public static bool IsGalleryPiece(ZDO zdo) {
    if (zdo == null) {
      return false;
    }
    try {
      return zdo.GetString(GalleryMark, string.Empty).Length > 0;
    } catch (Exception) {
      return false;
    }
  }

  public static string GalleryProfile(ZDO zdo) {
    try {
      return zdo == null
          ? "unknown"
          : zdo.GetString(GalleryProfileMark, "legacy");
    } catch (Exception) {
      return "unknown";
    }
  }

  public static string GalleryBuild(ZDO zdo) {
    try {
      return zdo == null
          ? "unknown"
          : zdo.GetString(GalleryBuildMark, "legacy");
    } catch (Exception) {
      return "unknown";
    }
  }

  public bool IsRunning { get { return _running; } }
  public string LastLifecycleResult { get { return _lastLifecycleResult; } }
  public bool LastLifecycleSucceeded { get { return _lastLifecycleSucceeded; } }

  static string ManifestPath {
    get {
      return Path.Combine(BepInEx.Paths.ConfigPath,
          Path.Combine("comfy-quest-lab", "gallery-manifest.txt"));
    }
  }

  // ---- check -----------------------------------------------------------------------

  /// <summary>Resolve everything the plan names, place nothing, and say what is missing.
  /// Run this first, always.</summary>
  public string Check(string profileId = null) {
    if (ZNetScene.instance == null) {
      return "not in a world yet — load a world first.";
    }

    LabGalleryPlan.Profile profile = LabGalleryPlan.Find(profileId);
    if (profile == null) {
      return UnknownProfile(profileId);
    }

    var wanted = new List<string> { "wood_beam", "portal_wood" };
    foreach (LabGalleryPlan.Tile tile in profile.PlatformTiles) {
      AddUnique(wanted, tile.Prefab);
    }
    foreach (LabGalleryPlan.Fixture fixture in profile.Fixtures) {
      AddUnique(wanted, fixture.Prefab);
    }
    foreach (LabGalleryPlan.Monument m in profile.Monuments) {
      if (!wanted.Contains(m.Station.Prefab)) {
        wanted.Add(m.Station.Prefab);
      }
    }
    foreach (LabGalleryPlan.CourseDrop drop in profile.CourseDrops) {
      AddUnique(wanted, drop.Prefab);
    }

    var found = new List<string>();
    var missing = new List<string>();
    foreach (string name in wanted) {
      (ZNetScene.instance.GetPrefab(name) != null ? found : missing).Add(name);
    }

    var sb = new StringBuilder();
    sb.AppendLine("gallery check " + profile.Id + " — "
        + found.Count + " of " + wanted.Count + " prefabs resolved");
    if (missing.Count > 0) {
      sb.AppendLine("MISSING (the plan names these and this game build does not have them):");
      foreach (string name in missing) {
        sb.AppendLine("  " + name);
      }
      sb.AppendLine("Find the right name with: questlab_prefabs <part of the name>");
    }
    sb.AppendLine(profile.PlatformTiles.Length + " floor tiles, "
        + profile.Fixtures.Length + " fixtures, "
        + CountBeams(profile) + " beams, " + profile.CourseDrops.Length
        + " interaction-local drops; about "
        + profile.EstimatedPlacedObjects + " placed objects.");
    sb.AppendLine(profile.HallWidth.ToString("0.#", CultureInfo.InvariantCulture)
        + " m halls; " + profile.SpokeLength.ToString("0.#", CultureInfo.InvariantCulture)
        + " m hub-to-station walks; floor " + string.Join(", ", profile.FloorMaterials)
        + (profile.SolidMarbleFloor ? " (solid marble)" : " (mixed material)"));
    sb.AppendLine(profile.PlatformClearance.ToString("0.#", CultureInfo.InvariantCulture)
        + " m terrain clearance; "
        + profile.RuneNameHeaders.ToString(CultureInfo.InvariantCulture)
        + " horizontal rune headers ("
        + profile.RuneNameSigns.ToString(CultureInfo.InvariantCulture) + " letter signs, "
        + profile.RuneNameLights.ToString(CultureInfo.InvariantCulture) + " lights).");
    sb.Append(missing.Count == 0
        ? "Ready. questlab_gallery build " + profile.Id
        : "Not ready — fix the names above first.");
    return sb.ToString();
  }

  static int CountBeams(LabGalleryPlan.Profile profile) {
    int n = 0;
    foreach (LabGalleryPlan.Monument m in profile.Monuments) {
      n += m.Beams.Length;
    }
    return n;
  }

  static void AddUnique(List<string> values, string value) {
    if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value)) {
      values.Add(value);
    }
  }

  public static string Profiles() {
    var sb = new StringBuilder();
    sb.AppendLine("gallery profiles (default: " + LabGalleryPlan.DefaultProfileId + "):");
    foreach (LabGalleryPlan.Profile profile in LabGalleryPlan.Profiles) {
      sb.Append("  ").Append(profile.Id).Append(" — ")
        .Append(profile.Name).Append(": about ")
        .Append(profile.EstimatedPlacedObjects.ToString(CultureInfo.InvariantCulture))
        .Append(" objects, ")
        .Append(profile.HallWidth.ToString("0.#", CultureInfo.InvariantCulture))
        .Append(" m halls, ")
        .Append(profile.SpokeLength.ToString("0.#", CultureInfo.InvariantCulture))
        .Append(" m hub-to-station walks, ")
        .Append(profile.PlatformClearance.ToString("0.#", CultureInfo.InvariantCulture))
        .Append(" m terrain clearance, ")
        .Append(profile.SolidMarbleFloor ? "solid marble" : "mixed floor")
        .Append(", ")
        .Append(profile.RuneNameHeaders.ToString(CultureInfo.InvariantCulture))
        .Append(" horizontal rune headers")
        .AppendLine();
      sb.AppendLine("    " + profile.Description);
    }
    sb.Append("Use questlab_gallery check <profile> before building.");
    return sb.ToString();
  }

  static string UnknownProfile(string profileId) {
    var ids = new List<string>();
    foreach (LabGalleryPlan.Profile profile in LabGalleryPlan.Profiles) {
      ids.Add(profile.Id);
    }
    return "unknown gallery profile '" + (profileId ?? string.Empty)
        + "'. One of: " + string.Join(", ", ids);
  }

  string NextBuildId(string label) {
    _buildSequence++;
    return label + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
        + "-" + _buildSequence.ToString("00", CultureInfo.InvariantCulture);
  }

  static int GalleryPiecesPerFrame() {
    try {
      return Mathf.Clamp(LabConfig.GalleryPiecesPerFrame.Value, 1, 200);
    } catch (Exception) {
      return 24;
    }
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
    return Build(host, LabGalleryPlan.DefaultProfileId);
  }

  public IEnumerator Build(
      MonoBehaviour host,
      string profileId,
      Vector3? originOffset = null,
      bool preservePlaced = false,
      string buildId = null) {
    if (_running) {
      Report("already building.");
      yield break;
    }
    Player player = Player.m_localPlayer;
    if (player == null || ZNetScene.instance == null) {
      Report("not in a world yet.");
      yield break;
    }

    LabGalleryPlan.Profile profile = LabGalleryPlan.Find(profileId);
    if (profile == null) {
      Report(UnknownProfile(profileId));
      yield break;
    }

    _running = true;
    if (!preservePlaced) {
      _placed.Clear();
    }
    Vector3 origin = player.transform.position + (originOffset ?? Vector3.zero);
    _activeProfileId = profile.Id;
    _activeBuildId = string.IsNullOrWhiteSpace(buildId) ? NextBuildId(profile.Id) : buildId;
    int piecesPerFrame = GalleryPiecesPerFrame();

    // One height for the whole platform: the highest ground under the footprint, plus a
    // clearance. A level floor on uneven ground is the entire point — sampling per tile
    // would reproduce the hillside the platform exists to hide.
    float top = origin.y;
    int tiles = profile.PlatformTiles.Length;
    for (int i = 0; i < tiles; i += 7) {   // every seventh tile is plenty to find the max
      Vector3 at = origin + new Vector3(profile.PlatformTiles[i].X, 0f,
                                        profile.PlatformTiles[i].Z);
      float ground;
      if (TryGroundHeight(at, out ground) && ground > top) {
        top = ground;
      }
    }
    float floorY = top + profile.PlatformClearance;
    Report("raising " + profile.Id + " (" + _activeBuildId + ") — floor at "
        + (floorY - origin.y).ToString("0.0", CultureInfo.InvariantCulture)
        + " m above its origin. Stand back.");

    // Where the ground-level portal goes, sampled NOW — before a single floor tile
    // exists. TryGroundHeight asks for the SOLID height, which counts placed pieces and
    // not just terrain, so asking after the floor is down returns the top of the deck and
    // puts the "ground" portal on the platform beside its own partner. That is exactly
    // what the first build did: two portals up top, none to walk to.
    Vector3 arrival = origin + new Vector3(2f, 0f, 0f);
    float arrivalGround;
    if (TryGroundHeight(arrival, out arrivalGround)) {
      arrival.y = arrivalGround;
    }

    int placed = 0;

    // 1. the floor
    for (int i = 0; i < tiles; i++) {
      LabGalleryPlan.Tile tile = profile.PlatformTiles[i];
      // Drop the slab by its own half-thickness so the walking surface lands ON floorY,
      // which is what every other position in the plan is measured against.
      var at = new Vector3(origin.x + tile.X, floorY + SurfaceDrop(tile.Prefab),
                           origin.z + tile.Z);
      if (Place(tile.Prefab, at, Quaternion.identity)) {
        placed++;
      }
      if (placed % piecesPerFrame == 0) {
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
    // atlas, so there is no RPC signature to guess at here. The arrival position was
    // sampled before the floor went down — see above.
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

    // 1c. the halls, and the sign at the mouth of each.
    //
    // Two courses of marble a side and no roof: the corridor frames the glyph at its end
    // without enclosing it, which is the composition — read the sign, walk the throat,
    // and the rune is the lit thing against open sky.
    foreach (LabGalleryPlan.Fixture fixture in profile.Fixtures) {
      bool panel = fixture.Orient == "panel";

      // Fixture.Y is where the piece's BOTTOM goes for anything standing on the floor: a
      // marble wall pivots at its centre, so placing it at that height directly buries
      // half of it, which is why the halls first read waist-high. A panel is the other
      // case — a slab stood on edge, whose Y is already a centre height and whose local
      // Y is thickness rather than height, so it takes no lift at all.
      float lift = panel ? 0f : BaseLift(fixture.Prefab);
      var fixtureAt = new Vector3(origin.x + fixture.X, floorY + fixture.Y + lift,
                                  origin.z + fixture.Z);

      Quaternion rot;
      if (panel) {
        // Stand the slab up. LookRotation puts local Z on its first argument and local Y
        // on its second, so aiming Z at world up and Y down the hall's ray turns the
        // slab's 2 m thickness into the backdrop's depth and its 8x8 face into the wall.
        Vector3 ray = Quaternion.Euler(0f, fixture.Yaw, 0f) * Vector3.forward;
        rot = Quaternion.LookRotation(Vector3.up, ray);
      } else {
        rot = Quaternion.Euler(0f, fixture.Yaw, 0f);
      }

      GameObject built = Place(fixture.Prefab, fixtureAt, rot);
      if (built == null) {
        continue;
      }
      placed++;
      if (!string.IsNullOrEmpty(fixture.Text)) {
        WriteSign(built, fixture.Text);
      }
      if (!string.IsNullOrEmpty(fixture.LightSchool)) {
        // r5 proved that colour alone does not survive distance and mist: the long-form
        // school names were dark vertical threads over otherwise readable runes. The
        // generator now uses one sign per letter and marks only the central letter, so
        // each word gets one coloured light rather than one costly light per character.
        var headerView = built.GetComponent<ZNetView>();
        if (headerView != null && headerView.GetZDO() != null) {
          LabRuneLight.Mark(headerView.GetZDO(), fixture.LightSchool);
          LabRuneLight.Apply(built, fixture.LightSchool);
        }
      }
      if (placed % piecesPerFrame == 0) {
        yield return null;
      }
    }
    yield return null;

    // 2. the monuments, and the station on each pad
    //
    // Which way a beam points is measured off the prefab, never assumed — see Measure.
    PieceMetrics beamShape = Measure("wood_beam");
    foreach (LabGalleryPlan.Monument monument in profile.Monuments) {
      // The lamp hangs on whichever beam lands nearest the middle of the glyph, so one
      // light covers the whole 11 m of strokes. Tracked as the beams go up rather than
      // computed afterwards, because the piece that gets marked has to be a real one.
      var runeCentre = new Vector3(origin.x + monument.Cx,
                                   floorY + profile.RuneHeight * 0.5f,
                                   origin.z + monument.Cz);
      GameObject lampHost = null;
      float lampBest = float.MaxValue;

      foreach (LabGalleryPlan.Beam beam in monument.Beams) {
        var at = new Vector3(origin.x + beam.X, floorY + beam.Y, origin.z + beam.Z);
        var along = new Vector3(beam.Dx, beam.Dy, beam.Dz).normalized;
        // Swing the prefab's own long axis onto the stroke, then back out the offset
        // between its pivot and the middle of its mesh, so the stroke lands centred where
        // the plan asked for it rather than hanging off one end.
        Quaternion rot = Quaternion.FromToRotation(beamShape.LongAxis, along);
        GameObject piece = Place("wood_beam", at - rot * beamShape.Center, rot);
        if (piece != null) {
          placed++;
          float toCentre = (at - runeCentre).sqrMagnitude;
          if (toCentre < lampBest) {
            lampBest = toCentre;
            lampHost = piece;
          }
        }
        if (placed % piecesPerFrame == 0) {
          yield return null;
        }
      }

      // Mark the lamp beam and light it here as well as in the patch: the patch runs from
      // Awake, during the Instantiate inside Place, before the mark below exists.
      if (lampHost != null) {
        var lampView = lampHost.GetComponent<ZNetView>();
        if (lampView != null && lampView.GetZDO() != null) {
          LabRuneLight.Mark(lampView.GetZDO(), monument.Category);
          LabRuneLight.Apply(lampHost, monument.Category);
        }
      }

      var stationAt = new Vector3(origin.x + monument.Station.X, floorY,
                                  origin.z + monument.Station.Z);
      GameObject station = Place(monument.Station.Prefab, stationAt,
          Quaternion.Euler(0f, monument.Station.Yaw, 0f));
      if (station != null) {
        placed++;
        if (monument.Station.Prefab == "portal_wood") {
          TagPortal(station, WorldTag);
        }
        if (!string.IsNullOrEmpty(monument.Station.Text)) {
          WriteSign(station, monument.Station.Text);
        }
      }
      yield return null;
    }

    // 3. the course kit. Every item is beside the interaction that consumes it: axe by
    // the arrival birch, bow and arrows on the player's side of Combat, coal directly in
    // front of the smelter, hammer and wood at Building, and three foods in the hub.
    // Dropped items glint and pick up normally, which is both more discoverable than an
    // uncertain item-stand RPC and an Inventory-school witness for free.
    foreach (LabGalleryPlan.CourseDrop item in profile.CourseDrops) {
      var at = new Vector3(origin.x + item.X, floorY + item.Y, origin.z + item.Z);
      GameObject drop = Place(item.Prefab, at, Quaternion.identity);
      if (drop != null) {
        placed++;
        SetStack(drop, item.Stack);
      }
      yield return null;
    }

    SaveManifest();
    _running = false;
    Report("gallery raised: " + profile.Id + " build " + _activeBuildId + ", " + placed
        + " objects. questlab_gallery identify reports it; clear " + profile.Id
        + " takes that profile down.");
  }

  /// <summary>Raise two profiles around one captured origin. Both carry the same build id,
  /// so one clear command can remove the comparison while profile marks still allow either
  /// side to come down independently.</summary>
  public IEnumerator Compare(MonoBehaviour host, string leftProfileId, string rightProfileId) {
    if (_running) {
      Report("already building.");
      yield break;
    }
    Player player = Player.m_localPlayer;
    if (player == null || ZNetScene.instance == null) {
      Report("not in a world yet.");
      yield break;
    }

    LabGalleryPlan.Profile left = LabGalleryPlan.Find(leftProfileId);
    LabGalleryPlan.Profile right = LabGalleryPlan.Find(rightProfileId);
    if (left == null) {
      Report(UnknownProfile(leftProfileId));
      yield break;
    }
    if (right == null) {
      Report(UnknownProfile(rightProfileId));
      yield break;
    }

    Vector3 anchor = player.transform.position;
    Vector3 axis = player.transform.right;
    float separation = left.FootprintRadius + right.FootprintRadius + 20f;
    Vector3 leftOrigin = anchor - axis * (separation * 0.5f);
    Vector3 rightOrigin = anchor + axis * (separation * 0.5f);
    string comparisonId = NextBuildId("compare");
    Report("raising comparison " + comparisonId + ": " + left.Id + " | " + right.Id
        + ". Clear by that id, or clear either profile independently.");

    IEnumerator first = Build(host, left.Id,
        leftOrigin - Player.m_localPlayer.transform.position, false, comparisonId);
    while (first.MoveNext()) {
      yield return first.Current;
    }

    if (Player.m_localPlayer == null) {
      Report("comparison stopped after " + left.Id + " because the player left the world.");
      yield break;
    }
    IEnumerator second = Build(host, right.Id,
        rightOrigin - Player.m_localPlayer.transform.position, true, comparisonId);
    while (second.MoveNext()) {
      yield return second.Current;
    }
    Report("comparison ready: " + comparisonId + " — " + left.Id + " | " + right.Id + ".");
  }

  public IEnumerator Rebuild(MonoBehaviour host, string profileId) {
    if (_running) {
      Report("already building.");
      yield break;
    }
    LabGalleryPlan.Profile profile = LabGalleryPlan.Find(profileId);
    if (profile == null) {
      Report(UnknownProfile(profileId));
      yield break;
    }
    IEnumerator clear = ClearSafely(profile.Id);
    while (clear.MoveNext()) {
      yield return clear.Current;
    }
    if (!LastLifecycleSucceeded) {
      Report("rebuild stopped because the old " + profile.Id + " gallery could not be cleared safely.");
      yield break;
    }
    IEnumerator build = Build(host, profile.Id);
    while (build.MoveNext()) {
      yield return build.Current;
    }
  }

  /// <summary>Start a clean creator course: safely remove every marked Gallery profile,
  /// then raise one fresh profile at the same reusable site. Unlike selective rebuild,
  /// this is the lifecycle used by lab_setup and all-schools preparation, where stale
  /// targets, supplies, or comparison structures would make the exercise ambiguous.</summary>
  public IEnumerator ResetSite(MonoBehaviour host, string profileId) {
    if (_running) {
      Report("gallery lifecycle operation already in progress.");
      yield break;
    }
    LabGalleryPlan.Profile profile = LabGalleryPlan.Find(profileId);
    if (profile == null) {
      Report(UnknownProfile(profileId));
      yield break;
    }
    IEnumerator clear = ClearSafely("all");
    while (clear.MoveNext()) {
      yield return clear.Current;
    }
    if (!LastLifecycleSucceeded) {
      Report("lab reset stopped because the old marked Gallery could not be cleared safely.");
      yield break;
    }
    IEnumerator build = Build(host, profile.Id);
    while (build.MoveNext()) {
      yield return build.Current;
    }
  }

  /// <summary>Instantiate one piece and remember it.
  ///
  /// Support wear is switched off on everything placed. A platform standing clear of the
  /// ground has nothing holding it up as far as the game is concerned, and would begin
  /// collapsing within minutes — which is a spectacular way to lose a student.
  ///
  /// "Off" is false on both wear flags. See the note at the assignment: they are opt-ins
  /// wearing a name that reads like an opt-out.</summary>
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

      // FALSE switches the wear OFF. Both fields are opt-INS despite the "no" in their
      // names — WearNTear.UpdateWear reaches its support check and its rain damage only
      // when the matching flag is true. Setting them true, which reads correctly and is
      // what this did at first, arms exactly the decay it was meant to prevent.
      //
      // GalleryStructurePatches does the same on every later rebuild, but it cannot help
      // here: it runs from Awake, during the Instantiate above, before the ZDO below has
      // been marked as ours.
      var wear = go.GetComponent<WearNTear>();
      if (wear != null) {
        wear.m_noSupportWear = false;
        wear.m_noRoofWear = false;
      }

      var piece = go.GetComponent<Piece>();
      if (piece != null && Player.m_localPlayer != null) {
        // Attribute it, so the gallery shows up as the student's own work rather than
        // as ownerless scenery.
        piece.SetCreator(Player.m_localPlayer.GetPlayerID());
      }

      var view = go.GetComponent<ZNetView>();
      if (view != null && view.GetZDO() != null) {
        ZDO zdo = view.GetZDO();
        // Mark it before the ZDO is shared, so the piece is identifiable as ours in this
        // session and every later one. Everything else keys off this, not off the id.
        zdo.Set(GalleryMark, LabGalleryPlan.PlanVersion.ToString(CultureInfo.InvariantCulture));
        zdo.Set(GalleryProfileMark, _activeProfileId);
        zdo.Set(GalleryBuildMark, _activeBuildId);
        _placed.Add(zdo.m_uid);
      }
      return go;
    } catch (Exception ex) {
      LogOnce("could not place " + prefabName + ": " + ex.Message);
      return null;
    }
  }

  // ---- clear -----------------------------------------------------------------------

  const float TerrainRetreatThreshold = 0.45f;
  const float SupportingPieceRadius = 3f;
  const float SupportingPieceVerticalRange = 4f;
  const float TerrainRetreatLift = 0.5f;
  const float TeleportAcceptTimeout = 4f;
  const float TeleportFinishTimeout = 18f;
  const float DestroySettleTimeout = 5f;

  /// <summary>Return a player standing on the selected gallery to the natural terrain at
  /// the same X/Z, then remove every matching marked object.
  ///
  /// Clear used to be synchronous. That made its object ownership rules safe, but not its
  /// player lifecycle: using it from an elevated marble floor removed the floor before the
  /// player had somewhere else to stand. Requiring people to find a new field (or remember
  /// which portal was the ground portal) is not a reusable authoring loop.
  ///
  /// The terrain target deliberately comes from ZoneSystem.GetGroundHeight, whose ray mask
  /// is terrain-only. GetSolidHeight sees the gallery floor and would teleport the player
  /// right back onto the object about to disappear. Valheim's own TeleportTo owns movement
  /// and replication. If it is cooling down, this waits a bounded four seconds; if the
  /// teleport never completes at the intended ground target, the gallery stays standing.</summary>
  public IEnumerator ClearSafely(string selector = null) {
    _lastLifecycleSucceeded = false;
    _lastLifecycleResult = "gallery clear has not completed.";
    if (ZNetScene.instance == null || Player.m_localPlayer == null) {
      _lastLifecycleResult = "not in a world yet.";
      Report(_lastLifecycleResult);
      yield break;
    }
    if (_running) {
      _lastLifecycleResult = "gallery lifecycle operation in progress — wait for it to finish before clearing.";
      Report(_lastLifecycleResult);
      yield break;
    }

    selector = NormalizeSelector(selector);
    _running = true;
    try {
      Player player = Player.m_localPlayer;
      Vector3 retreat;
      string retreatError;
      if (TryTerrainRetreat(player, selector, out retreat, out retreatError)) {
        Report("returning you to natural ground before clearing '" + selector + "'.");
        Quaternion facing = player.transform.rotation;
        bool accepted = false;
        float acceptDeadline = Time.realtimeSinceStartup + TeleportAcceptTimeout;
        while (Time.realtimeSinceStartup < acceptDeadline) {
          if (Player.m_localPlayer != player) {
            break;
          }
          if (player.TeleportTo(retreat, facing, false)) {
            accepted = true;
            break;
          }
          yield return null;
        }
        if (!accepted) {
          _lastLifecycleResult = "clear stopped safely: Valheim did not accept the terrain return. "
              + "The marked gallery is still standing; wait a moment and run clear again.";
          Report(_lastLifecycleResult);
          yield break;
        }

        float finishDeadline = Time.realtimeSinceStartup + TeleportFinishTimeout;
        while (player.IsTeleporting() && Time.realtimeSinceStartup < finishDeadline) {
          yield return null;
        }
        if (player.IsTeleporting() || !ReachedTerrainRetreat(player, retreat)) {
          _lastLifecycleResult = "clear stopped safely: the terrain return did not finish at its verified target. "
              + "The marked gallery is still standing.";
          Report(_lastLifecycleResult);
          yield break;
        }
      } else if (!string.IsNullOrEmpty(retreatError)) {
        _lastLifecycleResult = retreatError;
        Report(_lastLifecycleResult);
        yield break;
      }

      _lastLifecycleResult = ClearMarked(selector);
      float settleDeadline = Time.realtimeSinceStartup + DestroySettleTimeout;
      while (StandingPieceCount(selector) > 0
          && Time.realtimeSinceStartup < settleDeadline) {
        yield return null;
      }
      int remaining = StandingPieceCount(selector);
      _lastLifecycleSucceeded = remaining == 0;
      if (remaining > 0) {
        _lastLifecycleResult += " " + remaining + " matching marked piece(s) remained locally "
            + "known after the bounded destroy-settle window.";
      }
      Report(_lastLifecycleResult);
    } finally {
      _running = false;
    }
  }

  static string NormalizeSelector(string selector) {
    return string.IsNullOrWhiteSpace(selector) ? "all" : selector.Trim();
  }

  bool TryTerrainRetreat(
      Player player, string selector, out Vector3 retreat, out string error) {
    Vector3 current = player.transform.position;
    retreat = current;
    error = string.Empty;

    float radiusSquared = SupportingPieceRadius * SupportingPieceRadius;
    bool matchingPieceUnderfoot = false;
    try {
      foreach (ZDO zdo in _objectsByIdRef(ZDOMan.instance).Values) {
        if (zdo == null || !IsGalleryPiece(zdo) || !MatchesSelector(zdo, selector)) {
          continue;
        }
        Vector3 piece = zdo.GetPosition();
        float dx = piece.x - current.x;
        float dz = piece.z - current.z;
        if (dx * dx + dz * dz <= radiusSquared
            && Mathf.Abs(piece.y - current.y) <= SupportingPieceVerticalRange) {
          matchingPieceUnderfoot = true;
          break;
        }
      }
    } catch (Exception ex) {
      LogOnce("could not verify whether the selected gallery supports the player: " + ex.Message);
      error = "clear stopped safely: could not verify what is supporting the player. "
          + "The marked gallery is still standing.";
      return false;
    }
    if (!matchingPieceUnderfoot) {
      return false;
    }

    float terrain;
    if (!TryNaturalTerrainHeight(current, out terrain)) {
      error = "clear stopped safely: Valheim could not resolve the natural terrain below you. "
          + "The marked gallery is still standing.";
      return false;
    }
    if (current.y - terrain < TerrainRetreatThreshold) {
      return false;
    }

    retreat = new Vector3(current.x, terrain + TerrainRetreatLift, current.z);
    return true;
  }

  static bool ReachedTerrainRetreat(Player player, Vector3 target) {
    if (player == null) {
      return false;
    }
    Vector3 actual = player.transform.position;
    float dx = actual.x - target.x;
    float dz = actual.z - target.z;
    return dx * dx + dz * dz <= 2.25f && Mathf.Abs(actual.y - target.y) <= 1.5f;
  }

  /// <summary>Take the gallery down — every gallery, not just the last one.
  ///
  /// The manifest cannot be the answer here and never could. Every build begins by
  /// emptying it and rewrites it at the end, so it only ever describes the most recent
  /// gallery; the moment a second one went up the first became unreachable, and clear
  /// could not have found it however hard it tried. What that looks like from inside the
  /// world is 1500 pieces of accumulated scaffolding and a clear that reports zero.
  ///
  /// So the mark leads and the manifest follows. The locally known ZDO table includes structural
  /// pieces, portals, stations, and loose supplies alike, so a sweep finds every kind of
  /// gallery object in any session no matter which build placed it. The manifest is still
  /// worth consulting afterwards for marked pieces not synchronized to this client.
  ///
  /// Blueprint pieces are deliberately not touched: this asks IsGalleryPiece, not
  /// LabMarks.IsLabBuilt, so clearing a gallery never takes somebody's blueprint with it.</summary>
  string ClearMarked(string selector) {
    if (ZNetScene.instance == null) {
      return "not in a world yet.";
    }
    selector = NormalizeSelector(selector);

    // Collect first, destroy second. Destroying while walking the instance list mutates
    // the thing being walked.
    var doomed = new List<ZDO>();
    var swept = new HashSet<ZDOID>();
    try {
      foreach (ZDO zdo in _objectsByIdRef(ZDOMan.instance).Values) {
        if (zdo == null || !IsGalleryPiece(zdo) || !MatchesSelector(zdo, selector)) {
          continue;
        }
        doomed.Add(zdo);
        swept.Add(zdo.m_uid);
      }
    } catch (Exception ex) {
      LogOnce("could not sweep locally known pieces: " + ex.Message);
    }

    int removed = 0;
    foreach (ZDO zdo in doomed) {
      try {
        if (DestroyMarkedZdo(zdo)) removed++;
      } catch (Exception) {
        // A piece somebody already broke is not an error worth stopping for.
      }
    }

    LoadManifestIfEmpty();
    int notOurs = 0;
    var kept = new List<ZDOID>();
    foreach (ZDOID id in _placed) {
      if (swept.Contains(id)) {
        continue;
      }
      try {
        ZDO zdo = ZDOMan.instance.GetZDO(id);
        if (zdo == null) {
          continue;
        }
        // The id on its own is not proof of anything. ZDO ids are session-scoped, so a
        // recorded id can resolve after a reload to an entirely unrelated object — a
        // tree, a boat, a player. Only the mark the piece carries decides, and anything
        // without it is left alone. Destroying by id cost somebody their session once.
        if (!IsGalleryPiece(zdo)) {
          notOurs++;
          continue;
        }
        if (!MatchesSelector(zdo, selector)) {
          kept.Add(id);
          continue;
        }
        if (DestroyMarkedZdo(zdo)) removed++;
      } catch (Exception) {
        // A piece somebody already broke is not an error worth stopping for.
      }
    }
    _placed.Clear();
    _placed.AddRange(kept);
    SaveManifest();

    if (removed == 0) {
      return "no marked gallery pieces matching '" + selector + "' are locally known.";
    }
    return "cleared " + removed + " piece(s) matching '" + selector + "'"
         + (notOurs > 0
            ? ", and left " + notOurs + " manifest entr" + (notOurs == 1 ? "y" : "ies")
              + " alone — those ids belong to something else now, which is what happens to "
              + "a manifest across a reload"
            : string.Empty)
         + ".";
  }

  /// <summary>Destroy one already mark-validated gallery ZDO.
  ///
  /// A local world's ZDO table contains objects outside the currently instantiated zones.
  /// <c>ZDOMan.DestroyZDO</c> silently ignores an unowned ZDO, so the no-view branch must
  /// claim it just like <c>ZNetView.ClaimOwnership</c> does. The old branch counted that
  /// ignored call as a removal, leaving thousands of durable gallery marks behind.</summary>
  static bool DestroyMarkedZdo(ZDO zdo) {
    if (zdo == null || ZDOMan.instance == null || ZNetScene.instance == null) {
      return false;
    }
    ZNetView view = ZNetScene.instance.FindInstance(zdo);
    if (view != null) {
      view.ClaimOwnership();
      view.Destroy();
      return true;
    }
    zdo.SetOwner(ZDOMan.GetSessionID());
    ZDOMan.instance.DestroyZDO(zdo);
    return true;
  }

  static bool MatchesSelector(ZDO zdo, string selector) {
    if (string.IsNullOrWhiteSpace(selector)
        || string.Equals(selector, "all", StringComparison.OrdinalIgnoreCase)) {
      return true;
    }
    return string.Equals(GalleryProfile(zdo), selector, StringComparison.OrdinalIgnoreCase)
        || string.Equals(GalleryBuild(zdo), selector, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>Describe locally known marked structures without relying on their prefab names
  /// or transient ids. This is deliberately read-only and is the safe first step before a
  /// selective clear after comparison testing.</summary>
  public string Identify() {
    if (ZNetScene.instance == null) {
      return "not in a world yet.";
    }

    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var seen = new HashSet<ZDOID>();
    try {
      foreach (ZDO zdo in _objectsByIdRef(ZDOMan.instance).Values) {
        AddIdentity(zdo, counts, seen);
      }
    } catch (Exception ex) {
      LogOnce("could not identify locally known pieces: " + ex.Message);
    }

    LoadManifestIfEmpty();
    foreach (ZDOID id in _placed) {
      try {
        if (ZDOMan.instance != null) {
          AddIdentity(ZDOMan.instance.GetZDO(id), counts, seen);
        }
      } catch (Exception) {
      }
    }

    if (counts.Count == 0) {
      return "no marked gallery structures are locally known.\n" + Profiles();
    }
    var keys = new List<string>(counts.Keys);
    keys.Sort(StringComparer.OrdinalIgnoreCase);
    var sb = new StringBuilder("locally known gallery structures:\n");
    foreach (string key in keys) {
      sb.Append("  ").Append(key.Replace("\t", " | build ")).Append(": ")
        .Append(counts[key].ToString(CultureInfo.InvariantCulture)).AppendLine(" marked objects");
    }
    sb.Append("Clear with questlab_gallery clear <profile-or-build-id>.");
    return sb.ToString();
  }

  static void AddIdentity(
      ZDO zdo,
      Dictionary<string, int> counts,
      HashSet<ZDOID> seen) {
    if (zdo == null || !IsGalleryPiece(zdo) || !seen.Add(zdo.m_uid)) {
      return;
    }
    string key = GalleryProfile(zdo) + "\t" + GalleryBuild(zdo);
    int count;
    counts.TryGetValue(key, out count);
    counts[key] = count + 1;
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

  /// <summary>What a prefab is actually shaped like: which of its local axes the mesh is
  /// longest on, and where that mesh sits relative to the pivot.</summary>
  struct PieceMetrics {
    public Vector3 LongAxis;
    public Vector3 Center;
    public Vector3 Size;
  }

  /// <summary>Measure a prefab instead of assuming how it is built.
  ///
  /// The monuments first came out looking like the dots in a connect-the-dots book. The
  /// orientation was a guess — the code took it as read that a wood beam runs along its
  /// local Z and aimed that down each stroke. A 2 m beam whose mesh actually runs along a
  /// different axis stands end-on to the rune it is drawing, and 89 of them read as a
  /// scatter of points rather than eight glyphs.
  ///
  /// The component atlas cannot answer this one: mesh extents live in the asset bundles,
  /// not in assembly_valheim.dll, so there is no IL to read. The prefab can answer it
  /// though, at runtime, for nothing. So ask it, and report what it said — a build that
  /// prints "drawing along local Y" can be checked by the person watching it.</summary>
  readonly Dictionary<string, PieceMetrics> _metrics = new Dictionary<string, PieceMetrics>();

  /// <summary>How far to drop a floor slab so its TOP face lands on the target height.
  ///
  /// The plan positions everything else against the walking surface, so the surface has
  /// to be where the plan thinks it is. wood_floor pivoted at its top face and this was
  /// free; the marble and stone slabs pivot at their centre, which silently raised the
  /// surface half a metre under everything standing on it.</summary>
  float SurfaceDrop(string prefabName) {
    PieceMetrics m = Measure(prefabName);
    return m.Size.y <= 0f ? 0f : -(m.Center.y + m.Size.y * 0.5f);
  }

  /// <summary>How far to lift a piece so its BOTTOM rests on the surface. Zero for a
  /// prefab that already pivots at its base, half its height for one that pivots at its
  /// centre — measured either way, so neither has to be assumed.</summary>
  float BaseLift(string prefabName) {
    PieceMetrics m = Measure(prefabName);
    return m.Size.y <= 0f ? 0f : m.Size.y * 0.5f - m.Center.y;
  }

  PieceMetrics Measure(string prefabName) {
    PieceMetrics cached;
    if (_metrics.TryGetValue(prefabName, out cached)) {
      return cached;
    }
    var fallback = new PieceMetrics { LongAxis = Vector3.forward };
    try {
      GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
      if (prefab == null) {
        return fallback;
      }

      bool any = false;
      Bounds local = default;
      foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true)) {
        Mesh mesh = filter.sharedMesh;
        if (mesh == null) {
          continue;
        }
        Bounds mb = mesh.bounds;
        // All eight corners, each carried into the prefab root's frame, so that a rotated
        // child cannot quietly stretch the box along an axis the mesh does not use.
        for (int i = 0; i < 8; i++) {
          var corner = new Vector3(
              (i & 1) == 0 ? mb.min.x : mb.max.x,
              (i & 2) == 0 ? mb.min.y : mb.max.y,
              (i & 4) == 0 ? mb.min.z : mb.max.z);
          Vector3 inRoot =
              prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
          if (!any) {
            local = new Bounds(inRoot, Vector3.zero);
            any = true;
          } else {
            local.Encapsulate(inRoot);
          }
        }
      }
      if (!any) {
        return fallback;
      }

      Vector3 size = local.size;
      Vector3 axis;
      string named;
      if (size.x >= size.y && size.x >= size.z) {
        axis = Vector3.right;
        named = "X";
      } else if (size.y >= size.z) {
        axis = Vector3.up;
        named = "Y";
      } else {
        axis = Vector3.forward;
        named = "Z";
      }

      Report(prefabName + " measures " + size.x.ToString("0.00") + " x "
          + size.y.ToString("0.00") + " x " + size.z.ToString("0.00")
          + " — drawing along local " + named + ", pivot "
          + (Mathf.Abs(local.center.y) < 0.05f ? "centre" : "offset") + ".");
      var measured = new PieceMetrics { LongAxis = axis, Center = local.center, Size = size };
      _metrics[prefabName] = measured;
      return measured;
    } catch (Exception ex) {
      LogOnce("could not measure " + prefabName + ": " + ex.Message);
      return fallback;
    }
  }

  /// <summary>How many lab-marked pieces are present in the locally known ZDO table.
  ///
  /// Setup and batch preparation use this after clear and after build to verify that their
  /// clean-course lifecycle actually reached zero and then produced a marked structure. Before
  /// that lifecycle existed, a second <c>lab_setup</c> silently raised another 620 pieces through
  /// the first — the same mistake that once let the count reach 1527 before anybody noticed.
  ///
  /// A local world knows its complete table; a remote client can only answer for synchronized
  /// objects. That is the safe direction to be wrong in — it offers to build rather than
  /// destroying anything it cannot prove belongs to the lab.</summary>
  public int StandingPieceCount(string selector = null) {
    int n = 0;
    try {
      foreach (ZDO zdo in _objectsByIdRef(ZDOMan.instance).Values) {
        if (zdo != null && IsGalleryPiece(zdo) && MatchesSelector(zdo, selector)) {
          n++;
        }
      }
    } catch (Exception ex) {
      LogOnce("could not count standing pieces: " + ex.Message);
    }
    return n;
  }

  /// <summary>Put a fresh practice target in front of the player.
  ///
  /// The gallery plan has always declared <c>Kind = "spawner"</c> and a note reading "respawned
  /// on demand", and nothing ever read either — the builder places each station exactly once. So
  /// the combat target was a single Greyling: kill it and the practice ground had nothing left to
  /// kill, which breaks the authoring loop on its second iteration. Harvest has the same problem
  /// the moment you fell the birch.
  ///
  /// The whole reason for building hallways and stations is that a creator should not have to go
  /// hunting for the thing their quest is about. A one-word command is the smallest honest way to
  /// keep that true.
  ///
  /// Spawns ahead of the player rather than at the monument's pad, deliberately: someone testing
  /// a quest is standing wherever they are standing, and making them walk back to the ring is the
  /// same wasted minute in a different costume.</summary>
  public string Restock(string category) {
    try {
      if (ZNetScene.instance == null || Player.m_localPlayer == null) {
        return "not in a world yet.";
      }

      category = string.IsNullOrEmpty(category)
          ? LabCategory.Combat
          : category.ToLowerInvariant();

      // Monument is a struct, so "not found" is a flag rather than a null.
      LabGalleryPlan.Monument monument = default(LabGalleryPlan.Monument);
      bool found = false;
      foreach (LabGalleryPlan.Monument m in LabGalleryPlan.Monuments) {
        if (m.Category == category) {
          monument = m;
          found = true;
          break;
        }
      }

      if (!found) {
        return "no practice target for '" + category + "'. One of: "
            + string.Join(", ", LabCategory.All);
      }

      Player player = Player.m_localPlayer;
      Vector3 at = player.transform.position + player.transform.forward * RestockDistance;
      if (TryGroundHeight(at, out float ground)) {
        at.y = ground;
      }

      // Face the player, so a target that fights back does not spawn with its back turned.
      Quaternion facing = Quaternion.LookRotation(
          new Vector3(player.transform.position.x - at.x, 0f, player.transform.position.z - at.z));

      GameObject spawned = Place(monument.Station.Prefab, at, facing);
      if (spawned == null) {
        return "could not place " + monument.Station.Prefab
            + " — run questlab_gallery check to see what this build has.";
      }

      return "placed a " + monument.Station.Prefab + " in front of you (" + category + "). "
          + monument.Station.Note;
    } catch (Exception ex) {
      return "could not restock: " + ex.Message;
    }
  }

  /// <summary>Far enough not to spawn inside the player, close enough to be obviously for them.</summary>
  const float RestockDistance = 4f;

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

  /// <summary>Terrain only. Unlike TryGroundHeight, this intentionally ignores pieces,
  /// characters, and every other solid so gallery clear can find the reusable site below
  /// its own raised floor.</summary>
  static bool TryNaturalTerrainHeight(Vector3 at, out float height) {
    height = at.y;
    try {
      if (ZoneSystem.instance == null) {
        return false;
      }
      return ZoneSystem.instance.GetGroundHeight(at, out height);
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

  /// <summary>Put words on a sign.
  ///
  /// The copy is a ZDO string field called "text", which the component atlas reports as a
  /// plain read/write on Sign — the same shape as the portal pairing tag, so this needs
  /// no RPC and no guessed signature. Written before the ZDO is shared, so a sign is
  /// never briefly blank for anyone watching the hall go up.</summary>
  void WriteSign(GameObject go, string text) {
    try {
      var view = go.GetComponent<ZNetView>();
      if (view == null || view.GetZDO() == null) {
        return;
      }
      view.GetZDO().Set("text", text);
    } catch (Exception ex) {
      LogOnce("could not write a sign: " + ex.Message);
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

  readonly HashSet<string> _logged = new HashSet<string>();

  /// <summary>A thousand pieces means a thousand chances to say the same thing. Say it
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

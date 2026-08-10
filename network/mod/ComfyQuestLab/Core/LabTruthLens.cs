namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using HarmonyLib;

using UnityEngine;

/// <summary>Read-only, machine-checkable evidence for a standing Gallery build.
///
/// This is deliberately a lens, not a camera controller. It never teleports the player,
/// changes the environment, instantiates a comparison object, or mutates a ZDO. The named
/// views are deterministic plans for the existing camera proof lane; the bounds and
/// assertions explain what the engine has actually loaded before a human judges the frame.
/// Human visual acceptance remains authoritative, especially for snow that is rendered by
/// environment/shader state rather than stored on the marble piece.</summary>
public static class LabTruthLens {
  const string Schema = "comfy-questlab-gallery-truth/v1";
  const int MaxSelectorLength = 80;
  const int RenderSamplesPerGroup = 2;

  static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> _objectsByIdRef =
      AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");

  public sealed class CaptureResult {
    public bool Succeeded;
    public string Verdict;
    public string Path;
    public string Summary;
  }

  sealed class Extent {
    public bool Any;
    public Vector3 Min;
    public Vector3 Max;

    public void Add(Vector3 point) {
      if (!Any) {
        Min = point;
        Max = point;
        Any = true;
      } else {
        Min = Vector3.Min(Min, point);
        Max = Vector3.Max(Max, point);
      }
    }

    public void Add(Bounds bounds) {
      Add(bounds.min);
      Add(bounds.max);
    }

    public Vector3 Center { get { return Any ? (Min + Max) * 0.5f : Vector3.zero; } }
    public Vector3 Size { get { return Any ? Max - Min : Vector3.zero; } }
  }

  sealed class FixtureCheck {
    public string Prefab;
    public Bounds Bounds;
    public bool Measured;
    public float RoofUnderface;
    public float Clearance;
    public string Verdict;
  }

  sealed class RenderGroup {
    public string Role;
    public string Prefab;
    public int LoadedInstances;
    public int Samples;
    public int ConfigurationMismatches;
    public int IlluminationDeltas;
    public int SurfaceDeltas;
    public int LightDeltas;
    public string FreshDigest;
    public readonly List<string> LiveDigests = new List<string>();
  }

  sealed class NamedView {
    public string Id;
    public string Purpose;
    public Vector3 Lens;
    public Vector3 Target;
    public Vector3 Up;
    public float FieldOfView;
  }

  sealed class Assertion {
    public string Id;
    public string Verdict;
    public string Detail;
  }

  sealed class Subject {
    public string Profile;
    public string Build;
    public int MarkedObjects;
    public int LoadedObjects;
    public int LoadedWithBounds;
    public int LoadedFloors;
    public int RoofProtectedFloors;
    public readonly Dictionary<string, int> Roles =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public readonly Extent World = new Extent();
    public readonly List<Bounds> RoofBounds = new List<Bounds>();
    public readonly List<FixtureCheck> Fixtures = new List<FixtureCheck>();
    public readonly Dictionary<string, RenderGroup> RenderGroups =
        new Dictionary<string, RenderGroup>(StringComparer.OrdinalIgnoreCase);
    public readonly List<NamedView> Views = new List<NamedView>();
    public readonly List<Assertion> Assertions = new List<Assertion>();
    public string Verdict;
  }

  /// <summary>Write one fixed-directory artifact for all marked objects matching a profile
  /// or build id. artifactId is supplied only by the bounded request lane and becomes a
  /// filename after the same safe-token validation as a request id.</summary>
  public static CaptureResult Capture(string selector = null, string artifactId = null) {
    selector = string.IsNullOrWhiteSpace(selector) ? "all" : selector.Trim();
    if (ZNetScene.instance == null || ZDOMan.instance == null) {
      return Failed("not in a world yet — load a private world first.");
    }
    if (!SafeToken(selector, MaxSelectorLength)) {
      return Failed("selector must be all, a profile, or one safe build id.");
    }
    if (!string.IsNullOrWhiteSpace(artifactId) && !SafeToken(artifactId, 80)) {
      return Failed("evidence id was not a safe bounded token.");
    }

    try {
      var subjects = new Dictionary<string, Subject>(StringComparer.OrdinalIgnoreCase);
      var seen = new HashSet<ZDOID>();
      foreach (ZDO zdo in _objectsByIdRef(ZDOMan.instance).Values) {
        if (zdo == null || !LabGalleryBuilder.IsGalleryPiece(zdo)
            || !Matches(zdo, selector) || !seen.Add(zdo.m_uid)) {
          continue;
        }
        Add(subjects, zdo);
      }
      if (subjects.Count == 0) {
        return Failed("no locally known marked Gallery objects match '" + selector + "'.");
      }

      var ordered = new List<Subject>(subjects.Values);
      ordered.Sort((left, right) => string.Compare(
          left.Profile + "\t" + left.Build,
          right.Profile + "\t" + right.Build,
          StringComparison.OrdinalIgnoreCase));
      string overall = "pass";
      foreach (Subject subject in ordered) {
        FinalizeSubject(subject);
        overall = Worse(overall, subject.Verdict);
      }

      string id = string.IsNullOrWhiteSpace(artifactId)
          ? "gallery-truth-" + DateTime.UtcNow.ToString(
              "yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)
          : artifactId;
      string dir = Path.Combine(BepInEx.Paths.ConfigPath,
          Path.Combine("comfy-quest-lab", Path.Combine("receipts", "truth")));
      Directory.CreateDirectory(dir);
      string path = Path.Combine(dir, id + ".json");
      WriteAtomic(path, ToJson(selector, overall, ordered));

      int objects = 0;
      int loaded = 0;
      int warnings = 0;
      int failures = 0;
      foreach (Subject subject in ordered) {
        objects += subject.MarkedObjects;
        loaded += subject.LoadedObjects;
        foreach (Assertion assertion in subject.Assertions) {
          if (assertion.Verdict == "warn") warnings++;
          if (assertion.Verdict == "fail") failures++;
        }
      }
      return new CaptureResult {
        Succeeded = true,
        Verdict = overall,
        Path = path,
        Summary = "Gallery truth " + overall.ToUpperInvariant() + ": "
            + ordered.Count + " build(s), " + objects + " marked object(s), "
            + loaded + " loaded; " + failures + " failed assertion(s), "
            + warnings + " warning(s). No camera or world state changed.\nartifact: " + path,
      };
    } catch (Exception ex) {
      return Failed("Gallery truth capture failed: " + ex.Message);
    }
  }

  static void Add(Dictionary<string, Subject> subjects, ZDO zdo) {
    string profile = LabGalleryBuilder.GalleryProfile(zdo);
    string build = LabGalleryBuilder.GalleryBuild(zdo);
    string key = profile + "\t" + build;
    if (!subjects.TryGetValue(key, out Subject subject)) {
      subject = new Subject { Profile = profile, Build = build };
      subjects[key] = subject;
    }
    subject.MarkedObjects++;
    string role = LabGalleryBuilder.GalleryRole(zdo);
    if (string.IsNullOrWhiteSpace(role)) role = "other";
    subject.Roles.TryGetValue(role, out int roleCount);
    subject.Roles[role] = roleCount + 1;
    subject.World.Add(zdo.GetPosition());

    ZNetView view = ZNetScene.instance.FindInstance(zdo);
    if (view == null) return;
    subject.LoadedObjects++;
    Bounds bounds;
    bool hasBounds = TryWorldRenderBounds(view.gameObject, out bounds);
    if (hasBounds) {
      subject.LoadedWithBounds++;
      subject.World.Add(bounds);
    }
    // Clearance is about physical mesh geometry, not smoke/fire particle render bounds.
    // A brazier's particle renderer can extend through the roof while its metal body hangs
    // correctly below it, so use transformed shared-mesh corners for both sides of this test.
    Bounds physicalBounds;
    if (TryWorldMeshBounds(view.gameObject, out physicalBounds)) {
      if (string.Equals(role, "roof", StringComparison.OrdinalIgnoreCase)) {
        subject.RoofBounds.Add(physicalBounds);
      } else if (string.Equals(role, "ceiling-brazier", StringComparison.OrdinalIgnoreCase)) {
        subject.Fixtures.Add(new FixtureCheck {
          Prefab = PrefabName(zdo),
          Bounds = physicalBounds,
        });
      }
    }

    if (string.Equals(role, "floor", StringComparison.OrdinalIgnoreCase)) {
      subject.LoadedFloors++;
      try {
        GameObject roof;
        if (WearNTear.RoofCheck(view.transform.position + Vector3.up * 0.75f, out roof)) {
          subject.RoofProtectedFloors++;
        }
      } catch (Exception) {
      }
    }
    AddRenderSample(subject, zdo, view.gameObject, role);
  }

  static void AddRenderSample(Subject subject, ZDO zdo, GameObject live, string role) {
    GameObject fresh = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
    string prefabName = fresh == null ? PrefabName(zdo) : fresh.name;
    string key = role + "\t" + prefabName;
    if (!subject.RenderGroups.TryGetValue(key, out RenderGroup group)) {
      group = new RenderGroup { Role = role, Prefab = prefabName };
      subject.RenderGroups[key] = group;
    }
    group.LoadedInstances++;
    if (fresh == null || group.Samples >= RenderSamplesPerGroup) return;

    LabRenderInspector.RenderSummary freshState =
        LabRenderInspector.SummarizeConfiguration(fresh);
    LabRenderInspector.RenderSummary liveState =
        LabRenderInspector.SummarizeConfiguration(live);
    group.Samples++;
    group.FreshDigest = freshState.ConfigurationDigest;
    if (!group.LiveDigests.Contains(liveState.ConfigurationDigest)) {
      group.LiveDigests.Add(liveState.ConfigurationDigest);
    }
    if (!string.Equals(freshState.ConfigurationDigest, liveState.ConfigurationDigest,
        StringComparison.Ordinal)) {
      group.ConfigurationMismatches++;
    }
    if (freshState.IlluminationSignals != liveState.IlluminationSignals) {
      group.IlluminationDeltas++;
    }
    if (freshState.SurfaceSignals != liveState.SurfaceSignals) {
      group.SurfaceDeltas++;
    }
    if (freshState.ConfiguredLights != liveState.ConfiguredLights) {
      group.LightDeltas++;
    }
  }

  static void FinalizeSubject(Subject subject) {
    foreach (FixtureCheck fixture in subject.Fixtures) {
      float roofUnderface = float.PositiveInfinity;
      float nearestDistance = float.PositiveInfinity;
      foreach (Bounds roof in subject.RoofBounds) {
        if (!OverlapsXZ(fixture.Bounds, roof)) continue;
        float distance = Mathf.Abs(roof.min.y - fixture.Bounds.max.y);
        if (distance < nearestDistance) {
          nearestDistance = distance;
          roofUnderface = roof.min.y;
        }
      }
      if (float.IsPositiveInfinity(roofUnderface)) {
        fixture.Verdict = "unmeasured";
        continue;
      }
      fixture.Measured = true;
      fixture.RoofUnderface = roofUnderface;
      fixture.Clearance = roofUnderface - fixture.Bounds.max.y;
      fixture.Verdict = fixture.Clearance > 0.02f ? "pass" : "fail";
    }

    AddViews(subject);
    AddAssertions(subject);
    subject.Verdict = "pass";
    foreach (Assertion assertion in subject.Assertions) {
      subject.Verdict = Worse(subject.Verdict, assertion.Verdict);
    }
  }

  static void AddViews(Subject subject) {
    Vector3 center = subject.World.Center;
    Vector3 size = subject.World.Size;
    float width = Mathf.Max(12f, size.x);
    float depth = Mathf.Max(12f, size.z);
    float height = Mathf.Max(8f, size.y);
    float northDistance = Mathf.Max(18f, depth * 0.85f);
    float eastDistance = Mathf.Max(18f, width * 0.85f);
    float eyeY = subject.World.Any ? subject.World.Min.y + 1.7f : center.y;
    Vector3 eyeTarget = new Vector3(center.x, eyeY, center.z);
    subject.Views.Add(new NamedView {
      Id = "overview-north", Purpose = "whole-build silhouette and hall alignment",
      Lens = center + new Vector3(0f, height * 0.45f, -northDistance), Target = center,
      Up = Vector3.up, FieldOfView = 60f,
    });
    subject.Views.Add(new NamedView {
      Id = "overview-east", Purpose = "orthogonal scale and comparison check",
      Lens = center + new Vector3(eastDistance, height * 0.45f, 0f), Target = center,
      Up = Vector3.up, FieldOfView = 60f,
    });
    subject.Views.Add(new NamedView {
      Id = "overhead", Purpose = "footprint, spoke length, and roof coverage",
      Lens = center + new Vector3(0f, Mathf.Max(28f, Mathf.Max(width, depth) * 1.1f), 0f),
      Target = center, Up = Vector3.forward, FieldOfView = 55f,
    });
    subject.Views.Add(new NamedView {
      Id = "arrival-eye", Purpose = "first-person wayfinding and sign readability",
      Lens = eyeTarget + new Vector3(0f, 0f, -Mathf.Max(8f, depth * 0.22f)),
      Target = eyeTarget, Up = Vector3.up, FieldOfView = 65f,
    });
    if (subject.RoofBounds.Count > 0) {
      float underface = float.PositiveInfinity;
      foreach (Bounds roof in subject.RoofBounds) underface = Mathf.Min(underface, roof.min.y);
      Vector3 target = new Vector3(center.x, underface - 0.5f, center.z);
      subject.Views.Add(new NamedView {
        Id = "roof-underside", Purpose = "fixture attachment side and slab clearance",
        Lens = target + new Vector3(0f, -2.2f, -Mathf.Max(8f, depth * 0.2f)),
        Target = target, Up = Vector3.up, FieldOfView = 70f,
      });
    }
  }

  static void AddAssertions(Subject subject) {
    subject.Assertions.Add(new Assertion {
      Id = "loaded-world-bounds",
      Verdict = subject.LoadedObjects > 0 && subject.LoadedWithBounds > 0 ? "pass" : "warn",
      Detail = subject.LoadedWithBounds + "/" + subject.LoadedObjects
          + " loaded objects contributed renderer bounds; unloaded ZDO pivots still bound the view plan.",
    });

    string floorVerdict = "warn";
    string floorDetail;
    if (subject.LoadedFloors == 0) {
      floorDetail = "No marked floor slabs were loaded; weather and snow exposure is unmeasured.";
    } else if (subject.RoofProtectedFloors == subject.LoadedFloors) {
      floorVerdict = "pass";
      floorDetail = "All " + subject.LoadedFloors
          + " loaded floor slabs pass Valheim's live RoofCheck.";
    } else {
      floorDetail = subject.RoofProtectedFloors + "/" + subject.LoadedFloors
          + " loaded floor slabs pass Valheim's live RoofCheck; the remainder can receive "
          + "weather/snow treatment. Actual visible snow still requires a captured frame.";
    }
    subject.Assertions.Add(new Assertion {
      Id = "floor-weather-protection", Verdict = floorVerdict, Detail = floorDetail,
    });

    int fixturePass = 0;
    int fixtureFail = 0;
    int fixtureUnknown = 0;
    foreach (FixtureCheck fixture in subject.Fixtures) {
      if (fixture.Verdict == "pass") fixturePass++;
      else if (fixture.Verdict == "fail") fixtureFail++;
      else fixtureUnknown++;
    }
    subject.Assertions.Add(new Assertion {
      Id = "ceiling-fixture-clearance",
      Verdict = fixtureFail > 0 ? "fail" : (fixtureUnknown > 0 || subject.Fixtures.Count == 0
          ? "warn" : "pass"),
      Detail = fixturePass + " below roof, " + fixtureFail + " intersecting/above, "
          + fixtureUnknown + " without an overlapping loaded roof bound.",
    });

    int renderSamples = 0;
    int renderMismatches = 0;
    foreach (RenderGroup group in subject.RenderGroups.Values) {
      renderSamples += group.Samples;
      renderMismatches += group.ConfigurationMismatches;
    }
    subject.Assertions.Add(new Assertion {
      Id = "fresh-prefab-configuration",
      Verdict = renderSamples == 0 || renderMismatches > 0 ? "warn" : "pass",
      Detail = renderMismatches + "/" + renderSamples
          + " bounded live samples differ from the fresh prefab render configuration. "
          + "Differences are diagnostic, not automatically defects.",
    });
    subject.Assertions.Add(new Assertion {
      Id = "named-view-plan", Verdict = subject.Views.Count >= 4 ? "pass" : "warn",
      Detail = subject.Views.Count + " deterministic read-only camera plans generated.",
    });
  }

  static string ToJson(string selector, string verdict, List<Subject> subjects) {
    var sb = new StringBuilder(128 * 1024);
    sb.Append("{\n  \"schema\": ").Append(Quote(Schema));
    sb.Append(",\n  \"pluginRelease\": ").Append(Quote(ComfyQuestLab.ReleaseId));
    sb.Append(",\n  \"generatedAt\": ").Append(Quote(DateTime.UtcNow.ToString(
        "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
    sb.Append(",\n  \"selector\": ").Append(Quote(selector));
    sb.Append(",\n  \"verdict\": ").Append(Quote(verdict));
    sb.Append(",\n  \"capturePolicy\": ").Append(Quote(
        "read-only; named views are plans; human visual acceptance is authoritative"));
    sb.Append(",\n  \"environment\": {\"name\": ").Append(Quote(CurrentEnvironment()))
      .Append(", \"wet\": ").Append(Quote(CurrentWetState()))
      .Append(", \"visibleSnow\": \"human-frame-required\"}");
    sb.Append(",\n  \"subjects\": [");
    for (int i = 0; i < subjects.Count; i++) {
      if (i > 0) sb.Append(',');
      AppendSubject(sb, subjects[i]);
    }
    return sb.Append("\n  ]\n}\n").ToString();
  }

  static void AppendSubject(StringBuilder sb, Subject subject) {
    sb.Append("\n    {\"profile\": ").Append(Quote(subject.Profile))
      .Append(", \"build\": ").Append(Quote(subject.Build))
      .Append(", \"verdict\": ").Append(Quote(subject.Verdict))
      .Append(", \"markedObjects\": ").Append(subject.MarkedObjects)
      .Append(", \"loadedObjects\": ").Append(subject.LoadedObjects)
      .Append(", \"worldBounds\": ");
    AppendExtent(sb, subject.World);
    sb.Append(", \"roles\": {");
    var roles = new List<string>(subject.Roles.Keys);
    roles.Sort(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < roles.Count; i++) {
      if (i > 0) sb.Append(", ");
      sb.Append(Quote(roles[i])).Append(": ").Append(subject.Roles[roles[i]]);
    }
    sb.Append("}, \"weatherExposure\": {\"loadedFloors\": ")
      .Append(subject.LoadedFloors).Append(", \"roofProtectedFloors\": ")
      .Append(subject.RoofProtectedFloors).Append("}");
    AppendFixtures(sb, subject.Fixtures);
    AppendRenderGroups(sb, subject.RenderGroups);
    AppendViews(sb, subject.Views);
    AppendAssertions(sb, subject.Assertions);
    sb.Append('}');
  }

  static void AppendFixtures(StringBuilder sb, List<FixtureCheck> fixtures) {
    sb.Append(", \"ceilingFixtures\": [");
    for (int i = 0; i < fixtures.Count; i++) {
      if (i > 0) sb.Append(',');
      FixtureCheck fixture = fixtures[i];
      sb.Append("{\"prefab\": ").Append(Quote(fixture.Prefab))
        .Append(", \"verdict\": ").Append(Quote(fixture.Verdict))
        .Append(", \"bounds\": ");
      AppendBounds(sb, fixture.Bounds);
      sb.Append(", \"roofUnderfaceY\": ")
        .Append(fixture.Measured ? Num(fixture.RoofUnderface) : "null")
        .Append(", \"clearanceMeters\": ")
        .Append(fixture.Measured ? Num(fixture.Clearance) : "null").Append('}');
    }
    sb.Append(']');
  }

  static void AppendRenderGroups(StringBuilder sb, Dictionary<string, RenderGroup> groups) {
    var keys = new List<string>(groups.Keys);
    keys.Sort(StringComparer.OrdinalIgnoreCase);
    sb.Append(", \"renderComparisons\": [");
    for (int i = 0; i < keys.Count; i++) {
      if (i > 0) sb.Append(',');
      RenderGroup group = groups[keys[i]];
      sb.Append("{\"role\": ").Append(Quote(group.Role))
        .Append(", \"prefab\": ").Append(Quote(group.Prefab))
        .Append(", \"loadedInstances\": ").Append(group.LoadedInstances)
        .Append(", \"sampleLimit\": ").Append(RenderSamplesPerGroup)
        .Append(", \"samples\": ").Append(group.Samples)
        .Append(", \"configurationMismatches\": ").Append(group.ConfigurationMismatches)
        .Append(", \"illuminationDeltas\": ").Append(group.IlluminationDeltas)
        .Append(", \"surfaceDeltas\": ").Append(group.SurfaceDeltas)
        .Append(", \"lightDeltas\": ").Append(group.LightDeltas)
        .Append(", \"freshDigest\": ").Append(Quote(group.FreshDigest))
        .Append(", \"liveDigests\": ");
      AppendStrings(sb, group.LiveDigests);
      sb.Append('}');
    }
    sb.Append(']');
  }

  static void AppendViews(StringBuilder sb, List<NamedView> views) {
    sb.Append(", \"namedViews\": [");
    for (int i = 0; i < views.Count; i++) {
      if (i > 0) sb.Append(',');
      NamedView view = views[i];
      sb.Append("{\"id\": ").Append(Quote(view.Id))
        .Append(", \"purpose\": ").Append(Quote(view.Purpose))
        .Append(", \"lens\": ").Append(Vector(view.Lens))
        .Append(", \"target\": ").Append(Vector(view.Target))
        .Append(", \"up\": ").Append(Vector(view.Up))
        .Append(", \"fieldOfView\": ").Append(Num(view.FieldOfView)).Append('}');
    }
    sb.Append(']');
  }

  static void AppendAssertions(StringBuilder sb, List<Assertion> assertions) {
    sb.Append(", \"assertions\": [");
    for (int i = 0; i < assertions.Count; i++) {
      if (i > 0) sb.Append(',');
      Assertion assertion = assertions[i];
      sb.Append("{\"id\": ").Append(Quote(assertion.Id))
        .Append(", \"verdict\": ").Append(Quote(assertion.Verdict))
        .Append(", \"detail\": ").Append(Quote(assertion.Detail)).Append('}');
    }
    sb.Append(']');
  }

  static bool Matches(ZDO zdo, string selector) {
    return string.Equals(selector, "all", StringComparison.OrdinalIgnoreCase)
        || string.Equals(LabGalleryBuilder.GalleryProfile(zdo), selector,
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(LabGalleryBuilder.GalleryBuild(zdo), selector,
            StringComparison.OrdinalIgnoreCase);
  }

  static string PrefabName(ZDO zdo) {
    try {
      GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
      return prefab == null ? "hash:" + zdo.GetPrefab() : prefab.name;
    } catch (Exception) {
      return "unknown";
    }
  }

  static bool TryWorldRenderBounds(GameObject host, out Bounds world) {
    world = default;
    bool any = false;
    if (host == null) return false;
    foreach (Renderer renderer in host.GetComponentsInChildren<Renderer>(true)) {
      if (renderer == null) continue;
      Bounds bounds = renderer.bounds;
      if (!any) {
        world = bounds;
        any = true;
      } else {
        world.Encapsulate(bounds);
      }
    }
    return any;
  }

  static bool TryWorldMeshBounds(GameObject host, out Bounds world) {
    world = default;
    bool any = false;
    if (host == null) return false;
    foreach (MeshFilter filter in host.GetComponentsInChildren<MeshFilter>(true)) {
      Mesh mesh = filter == null ? null : filter.sharedMesh;
      if (mesh == null) continue;
      Bounds local = mesh.bounds;
      for (int i = 0; i < 8; i++) {
        var corner = new Vector3(
            (i & 1) == 0 ? local.min.x : local.max.x,
            (i & 2) == 0 ? local.min.y : local.max.y,
            (i & 4) == 0 ? local.min.z : local.max.z);
        Vector3 point = filter.transform.TransformPoint(corner);
        if (!any) {
          world = new Bounds(point, Vector3.zero);
          any = true;
        } else {
          world.Encapsulate(point);
        }
      }
    }
    return any;
  }

  static bool OverlapsXZ(Bounds one, Bounds two) {
    const float tolerance = 0.15f;
    return one.min.x <= two.max.x + tolerance && one.max.x >= two.min.x - tolerance
        && one.min.z <= two.max.z + tolerance && one.max.z >= two.min.z - tolerance;
  }

  static string CurrentEnvironment() {
    try {
      if (EnvMan.instance == null) return "unavailable";
      var method = AccessTools.Method(typeof(EnvMan), "GetCurrentEnvironment", Type.EmptyTypes);
      object environment = method == null ? null : method.Invoke(EnvMan.instance, null);
      if (environment == null) return "unknown";
      var field = AccessTools.Field(environment.GetType(), "m_name");
      object name = field == null ? null : field.GetValue(environment);
      return name == null ? environment.ToString() : name.ToString();
    } catch (Exception) {
      return "unknown";
    }
  }

  static string CurrentWetState() {
    try {
      if (EnvMan.instance == null) return "unavailable";
      var method = AccessTools.Method(typeof(EnvMan), "IsWet", Type.EmptyTypes);
      object wet = method == null ? null : method.Invoke(EnvMan.instance, null);
      return wet == null ? "unknown" : ((bool)wet ? "true" : "false");
    } catch (Exception) {
      return "unknown";
    }
  }

  static CaptureResult Failed(string summary) {
    return new CaptureResult {
      Succeeded = false, Verdict = "failed", Path = string.Empty, Summary = summary,
    };
  }

  static string Worse(string one, string two) {
    return Rank(two) > Rank(one) ? two : one;
  }

  static int Rank(string verdict) {
    if (verdict == "fail" || verdict == "failed") return 2;
    if (verdict == "warn") return 1;
    return 0;
  }

  static bool SafeToken(string value, int maxLength) {
    if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength) return false;
    foreach (char c in value) {
      if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.') return false;
    }
    return true;
  }

  static void WriteAtomic(string path, string content) {
    string temporary = path + ".tmp";
    File.WriteAllText(temporary, content, new UTF8Encoding(false));
    if (File.Exists(path)) File.Delete(path);
    File.Move(temporary, path);
  }

  static void AppendExtent(StringBuilder sb, Extent extent) {
    if (!extent.Any) {
      sb.Append("null");
      return;
    }
    sb.Append("{\"min\": ").Append(Vector(extent.Min))
      .Append(", \"max\": ").Append(Vector(extent.Max))
      .Append(", \"center\": ").Append(Vector(extent.Center))
      .Append(", \"size\": ").Append(Vector(extent.Size)).Append('}');
  }

  static void AppendBounds(StringBuilder sb, Bounds bounds) {
    sb.Append("{\"min\": ").Append(Vector(bounds.min))
      .Append(", \"max\": ").Append(Vector(bounds.max))
      .Append(", \"center\": ").Append(Vector(bounds.center))
      .Append(", \"size\": ").Append(Vector(bounds.size)).Append('}');
  }

  static void AppendStrings(StringBuilder sb, List<string> values) {
    sb.Append('[');
    for (int i = 0; i < values.Count; i++) {
      if (i > 0) sb.Append(", ");
      sb.Append(Quote(values[i]));
    }
    sb.Append(']');
  }

  static string Vector(Vector3 value) {
    return "[" + Num(value.x) + ", " + Num(value.y) + ", " + Num(value.z) + "]";
  }

  static string Num(float value) {
    return value.ToString("R", CultureInfo.InvariantCulture);
  }

  static string Quote(string value) {
    if (value == null) return "null";
    var sb = new StringBuilder(value.Length + 8).Append('"');
    foreach (char c in value) {
      switch (c) {
        case '\\': sb.Append("\\\\"); break;
        case '"': sb.Append("\\\""); break;
        case '\n': sb.Append("\\n"); break;
        case '\r': sb.Append("\\r"); break;
        case '\t': sb.Append("\\t"); break;
        default: sb.Append(c < 0x20 ? ' ' : c); break;
      }
    }
    return sb.Append('"').ToString();
  }
}

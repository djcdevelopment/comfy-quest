namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComfyQuestContracts;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>Client-local, read-only visibility for loaded Runtime Charm bindings.</summary>
/// <remarks>
/// Arcane Sight never writes a ZDO or changes event routing. It owns only renderer property
/// blocks and child lights created here, and releases both when the Runtime drawer closes.
/// </remarks>
sealed class RuntimeArcaneSight {
  const string Prefix = "comfyQuestRuntime.";
  const string LampName = "comfy-quest-runtime-arcane-sight";
  const int MaxLabels = 24;
  static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

  readonly string root;
  readonly Dictionary<Renderer, MaterialPropertyBlock> previous = new();
  readonly List<Marker> markers = new();
  bool active;
  float nextSync;

  public RuntimeArcaneSight(string runtimeRoot) { root = runtimeRoot; }
  public bool Active => active;

  public void Enable() {
    if (active) return;
    active = true;
    nextSync = 0f;
    Sync();
  }

  public void Toggle() {
    if (active) Disable();
    else Enable();
  }

  public void Tick() {
    if (!active || Time.realtimeSinceStartup < nextSync) return;
    Sync();
  }

  public string Describe() {
    if (!active) return "Off · reveal loaded Charm bindings without changing the world.";
    var current = markers.Count(value => value.Current);
    var eligible = markers.Count(value => value.Current && value.LocallyOwned);
    var player = Player.m_localPlayer;
    var distances = player == null
        ? Array.Empty<float>()
        : markers.Where(value => value.Host != null)
            .Select(value => Vector3.Distance(player.transform.position,
                value.Host.transform.position)).ToArray();
    var nearest = distances.Length == 0 ? "" : $" · nearest {Math.Round(distances.Min())}m";
    return $"{markers.Count} loaded binding{(markers.Count == 1 ? "" : "s")}"
        + $" · {current} active · {eligible} locally owned{nearest}"
        + " · loaded scene, no fixed radius";
  }

  public void Draw(GUIStyle style) {
    if (!active || style == null || markers.Count == 0) return;
    var camera = Camera.main ?? GameCamera.instance?.GetComponent<Camera>();
    if (camera == null) return;
    var player = Player.m_localPlayer;
    var visible = markers.Where(value => value.Host != null)
        .OrderBy(value => player == null ? 0f : Vector3.Distance(
            player.transform.position, value.Host.transform.position))
        .Take(MaxLabels);
    foreach (var marker in visible) {
      var world = marker.Host.transform.position + Vector3.up * marker.LabelHeight;
      var screen = camera.WorldToScreenPoint(world);
      if (screen.z <= 0f) continue;
      var x = screen.x - 210f;
      var y = Screen.height - screen.y - 24f;
      if (x < -420f || x > Screen.width || y < -48f || y > Screen.height) continue;
      var distance = player == null ? "" : $" · {Math.Round(Vector3.Distance(
          player.transform.position, marker.Host.transform.position))}m";
      var state = marker.Current ? "ACTIVE" : "OTHER VERSION";
      var owner = marker.LocallyOwned ? "LOCAL OWNER" : "REMOTE OWNER";
      var activation = marker.Current && !string.IsNullOrWhiteSpace(marker.ActivationId)
          ? " · " + marker.ActivationId : "";
      GUI.Label(new Rect(x, y, 420f, 48f),
          $"{state}{activation} / {owner} · {marker.ExperienceId} {marker.Version}{distance}\n"
          + $"{marker.PackId} · ZDO {marker.BindingZdo} · loaded-scene scope", style);
    }
  }

  void Sync() {
    nextSync = Time.realtimeSinceStartup + 0.75f;
    var activeSet = ReadActive();
    var activeHash = activeSet?.ContentHash;
    var found = new List<Marker>();
    try {
      foreach (var wear in WearNTear.GetAllInstances()) {
        if (wear == null) continue;
        var view = wear.GetComponent<ZNetView>();
        var zdo = view == null ? null : view.GetZDO();
        var reference = Read(zdo);
        if (reference == null) continue;
        var current = !string.IsNullOrWhiteSpace(activeHash)
            && string.Equals(reference.ContentHash, activeHash,
                StringComparison.OrdinalIgnoreCase);
        var locallyOwned = false;
        try { locallyOwned = view.IsOwner(); }
        catch { }
        found.Add(new Marker {
          Host = wear.gameObject,
          PackId = reference.PackId,
          ExperienceId = reference.ExperienceId,
          Version = reference.Version,
          ActivationId = current ? ShortActivationId(activeSet?.ActivationId) : null,
          BindingZdo = ShortBindingZdo(zdo.m_uid.ToString()),
          Current = current,
          LocallyOwned = locallyOwned,
          LabelHeight = Apply(wear.gameObject, current, locallyOwned),
        });
      }
    } catch {
      // Diagnostics must never interrupt gameplay or Runtime input.
    }
    markers.Clear();
    markers.AddRange(found);
    foreach (var renderer in previous.Keys.Where(value => value == null).ToArray())
      previous.Remove(renderer);
  }

  float Apply(GameObject host, bool current, bool locallyOwned) {
    var color = current && locallyOwned
        ? new Color(.72f, .43f, 1f, 1f)
        : current
            ? new Color(.32f, .65f, 1f, 1f)
            : new Color(1f, .57f, .22f, 1f);
    var emission = new Color(color.r * 1.35f, color.g * 1.35f,
        color.b * 1.35f, 1f);
    var existing = host.transform.Find(LampName);
    Light lamp;
    if (existing == null) {
      var lampObject = new GameObject(LampName) { hideFlags = HideFlags.DontSave };
      lampObject.transform.SetParent(host.transform, false);
      lamp = lampObject.AddComponent<Light>();
      lamp.type = LightType.Point;
      lamp.shadows = LightShadows.None;
    } else {
      lamp = existing.GetComponent<Light>();
    }
    if (lamp != null) {
      lamp.color = color;
      lamp.intensity = 1.05f;
      lamp.range = 6f;
    }

    var top = host.transform.position.y + 1.8f;
    foreach (var renderer in host.GetComponentsInChildren<Renderer>(true)) {
      if (renderer == null) continue;
      top = Math.Max(top, renderer.bounds.max.y + .35f);
      if (renderer.sharedMaterial == null
          || !renderer.sharedMaterial.HasProperty(EmissionColor)) continue;
      if (!previous.ContainsKey(renderer)) {
        var prior = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(prior);
        previous[renderer] = prior;
      }
      var block = new MaterialPropertyBlock();
      renderer.GetPropertyBlock(block);
      block.SetColor(EmissionColor, emission);
      renderer.SetPropertyBlock(block);
    }
    return Math.Max(1.8f, top - host.transform.position.y);
  }

  public void Disable() {
    foreach (var pair in previous) {
      try { if (pair.Key != null) pair.Key.SetPropertyBlock(pair.Value); }
      catch { }
    }
    previous.Clear();
    foreach (var wear in SafeInstances()) {
      try {
        var lamp = wear?.transform.Find(LampName);
        if (lamp != null) UnityEngine.Object.Destroy(lamp.gameObject);
      } catch { }
    }
    markers.Clear();
    active = false;
  }

  ActiveSet ReadActive() {
    try {
      var path = Path.Combine(root, "active", "active-set.json");
      if (!File.Exists(path)) return null;
      return JsonConvert.DeserializeObject<ActiveSet>(File.ReadAllText(path));
    } catch { return null; }
  }

  static string ShortActivationId(string value) {
    if (string.IsNullOrWhiteSpace(value)) return null;
    var separator = value.LastIndexOf('-');
    var suffix = separator >= 0 ? value.Substring(separator + 1) : value;
    return suffix.Length <= 8 ? "act-" + suffix : ShortId(value);
  }

  static string ShortBindingZdo(string value) => ShortId(value);

  static string ShortId(string value) {
    if (string.IsNullOrWhiteSpace(value) || value.Length <= 16) return value;
    return value.Substring(0, 7) + "…" + value.Substring(value.Length - 8);
  }

  static CharmReference Read(ZDO zdo) {
    if (zdo == null) return null;
    var reference = new CharmReference {
      PackId = zdo.GetString(Prefix + "packId", ""),
      ExperienceId = zdo.GetString(Prefix + "experienceId", ""),
      BindingId = zdo.GetString(Prefix + "bindingId", ""),
      Version = zdo.GetString(Prefix + "version", ""),
      ContentHash = zdo.GetString(Prefix + "contentHash", ""),
    };
    return CharmPolicy.ValidateReference(reference).Allowed ? reference : null;
  }

  static IEnumerable<WearNTear> SafeInstances() {
    try { return WearNTear.GetAllInstances(); }
    catch { return Array.Empty<WearNTear>(); }
  }

  sealed class Marker {
    public GameObject Host;
    public string PackId;
    public string ExperienceId;
    public string Version;
    public string ActivationId;
    public string BindingZdo;
    public bool Current;
    public bool LocallyOwned;
    public float LabelHeight;
  }
}

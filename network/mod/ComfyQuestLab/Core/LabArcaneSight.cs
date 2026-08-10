namespace ComfyQuestLab;

using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>Client-local visual telemetry for the interactive Quest Lab panel.
///
/// Arcane Sight intentionally owns only property blocks and child lights created by this
/// class. Shared materials are never cloned or edited, and all state is released when the
/// panel closes. The gallery's persistent rune-light marks remain a separate cosmetic lane.
/// </summary>
public static class LabArcaneSight {
  const string LampName = "comfy-quest-lab-arcane-sight";
  static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
  static readonly Dictionary<Renderer, MaterialPropertyBlock> Previous =
      new Dictionary<Renderer, MaterialPropertyBlock>();
  static bool _active;

  public static bool Active { get { return _active; } }

  public static void Enable() {
    if (_active) {
      Sync();
      return;
    }
    _active = true;
    Sync();
  }

  public static void Sync() {
    if (!_active) return;
    try {
      foreach (WearNTear wear in WearNTear.GetAllInstances()) {
        if (wear == null) continue;
        ZNetView view = wear.GetComponent<ZNetView>();
        ZDO zdo = view == null ? null : view.GetZDO();
        string school = LabRuneLight.SchoolOf(zdo);
        if (string.IsNullOrWhiteSpace(school)) continue;
        Apply(wear.gameObject, school);
      }
    } catch (Exception) {
      // Diagnostic presentation must never interrupt panel input or gameplay.
    }
  }

  static void Apply(GameObject host, string school) {
    Color color = LabRuneLight.ColourFor(school);
    Color emission = new Color(color.r * 1.35f, color.g * 1.35f, color.b * 1.35f, 1f);
    Transform existingLamp = host.transform.Find(LampName);
    if (existingLamp == null) {
      var lampObject = new GameObject(LampName);
      lampObject.transform.SetParent(host.transform, false);
      var lamp = lampObject.AddComponent<Light>();
      lamp.type = LightType.Point;
      lamp.color = color;
      lamp.intensity = 0.8f;
      lamp.range = 4f;
      lamp.shadows = LightShadows.None;
    }
    foreach (Renderer renderer in host.GetComponentsInChildren<Renderer>(true)) {
      if (renderer == null || renderer.sharedMaterial == null
          || !renderer.sharedMaterial.HasProperty(EmissionColor)) continue;
      if (!Previous.ContainsKey(renderer)) {
        var prior = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(prior);
        Previous[renderer] = prior;
      }
      var block = new MaterialPropertyBlock();
      renderer.GetPropertyBlock(block);
      block.SetColor(EmissionColor, emission);
      renderer.SetPropertyBlock(block);
    }
  }

  public static void Disable() {
    if (!_active && Previous.Count == 0) return;
    foreach (KeyValuePair<Renderer, MaterialPropertyBlock> pair in Previous) {
      try {
        if (pair.Key != null) pair.Key.SetPropertyBlock(pair.Value);
      } catch (Exception) { }
    }
    Previous.Clear();
    foreach (WearNTear wear in SafeInstances()) {
      try {
        Transform lamp = wear.transform.Find(LampName);
        if (lamp != null) UnityEngine.Object.Destroy(lamp.gameObject);
      } catch (Exception) { }
    }
    _active = false;
  }

  static IEnumerable<WearNTear> SafeInstances() {
    try { return WearNTear.GetAllInstances(); }
    catch (Exception) { return Array.Empty<WearNTear>(); }
  }
}

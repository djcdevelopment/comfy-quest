namespace ComfyQuestLab;

using System;
using System.Collections.Generic;

using BepInEx.Configuration;

using HarmonyLib;

using UnityEngine;

/// <summary>Light the monuments, one coloured lamp per school.
///
/// There is no Valheim knob for this, which is worth stating plainly because it is the
/// first thing anyone looks for. The component atlas has `LightFlicker` (flicker speed,
/// fade, a brightness multiplier for accessibility) and `LightLod` (cull distances) — both
/// of which *modulate* a light that already exists — and exactly three fields in the whole
/// assembly that hold a Light at all: EnvMan.m_dirLight, MenuScene.m_dirLight, and
/// ShieldGenerator.m_coloredLights. Colour and intensity are `UnityEngine.Light`'s, not
/// the game's. A wood beam emits nothing and no vanilla field will change that.
///
/// So the lamp is ours, which has a consequence: a Light is a plain Unity component and
/// is not networked and not saved. ZNetScene rebuilds pieces from their ZDOs on every zone
/// reload, and a lamp parented to one would simply be gone. Same problem the wear flags
/// had, so the same shape of answer — one beam per monument carries a mark naming its
/// school, and the lamp is re-hung from that mark whenever the piece is constructed.
///
/// One lamp per monument, not per beam: 89 realtime point lights would be a frame-rate
/// bill for no visual gain, and a rune is 11 m of strokes that a single lamp at its middle
/// covers.</summary>
public static class LabRuneLight {
  /// <summary>ZDO string field on the one beam per monument that carries the lamp; the
  /// value is the school, so the colour survives a reload without a lookup table on disk.</summary>
  public const string RuneLightMark = "comfyQuestLabRuneLight";

  const string LampChildName = "comfy-quest-lab-rune-lamp";

  public static ConfigEntry<bool> Enabled;
  public static ConfigEntry<float> Intensity;
  public static ConfigEntry<float> Range;

  /// <summary>A colour per school, so a glance across the ring tells you which monument
  /// you are walking toward before you can read the glyph.</summary>
  static readonly Dictionary<string, Color> SchoolColours = new Dictionary<string, Color> {
    { LabCategory.Combat,      new Color(1.00f, 0.28f, 0.22f) },   // ember
    { LabCategory.Harvest,     new Color(0.45f, 0.95f, 0.40f) },   // green wood
    { LabCategory.Inventory,   new Color(0.95f, 0.78f, 0.30f) },   // brass
    { LabCategory.Building,    new Color(0.98f, 0.55f, 0.20f) },   // fired clay
    { LabCategory.Crafting,    new Color(0.55f, 0.80f, 1.00f) },   // forge-quench blue
    { LabCategory.Progression, new Color(0.80f, 0.50f, 1.00f) },   // violet
    { LabCategory.World,       new Color(0.35f, 0.90f, 0.90f) },   // portal cyan
    { LabCategory.Social,      new Color(1.00f, 0.70f, 0.85f) },   // rose
  };

  public static Color ColourFor(string school) {
    Color colour;
    return SchoolColours.TryGetValue(school ?? string.Empty, out colour) ? colour : Color.white;
  }

  /// <summary>Bound off the plugin's own config file rather than through LabConfig, so
  /// this lane can be added and removed without touching a shared file.</summary>
  public static void Bind(ConfigFile config) {
    if (Enabled != null || config == null) {
      return;
    }
    Enabled = config.Bind("Gallery", "runeLights", true,
        "Hang a coloured light on each monument, one per school. Client-side only — the "
        + "lamps are not networked and nobody else sees them. Hot-reloadable.");
    Intensity = config.Bind("Gallery", "runeLightIntensity", 6f,
        new ConfigDescription("How bright each monument's lamp burns.",
            new AcceptableValueRange<float>(0f, 40f)));
    Range = config.Bind("Gallery", "runeLightRange", 22f,
        new ConfigDescription("How far each monument's lamp reaches, in metres. A rune is "
            + "about 11 m of strokes, so below that only part of it lights.",
            new AcceptableValueRange<float>(1f, 128f)));
  }

  /// <summary>Mark a piece as the one carrying this monument's lamp.</summary>
  public static void Mark(ZDO zdo, string school) {
    if (zdo == null || string.IsNullOrEmpty(school)) {
      return;
    }
    try {
      zdo.Set(RuneLightMark, school);
    } catch (Exception) {
      // A monument that ends up unlit is a cosmetic loss, not a failed build.
    }
  }

  public static string SchoolOf(ZDO zdo) {
    if (zdo == null) {
      return string.Empty;
    }
    try {
      return zdo.GetString(RuneLightMark, string.Empty);
    } catch (Exception) {
      return string.Empty;
    }
  }

  /// <summary>Hang the lamp, or take it down if the config says so. Idempotent: a piece
  /// that already has its lamp is retuned rather than given a second one.</summary>
  public static void Apply(GameObject host, string school) {
    if (host == null) {
      return;
    }
    try {
      Transform existing = host.transform.Find(LampChildName);

      if (Enabled != null && !Enabled.Value) {
        if (existing != null) {
          UnityEngine.Object.Destroy(existing.gameObject);
        }
        return;
      }

      GameObject lamp;
      if (existing != null) {
        lamp = existing.gameObject;
      } else {
        lamp = new GameObject(LampChildName);
        lamp.transform.SetParent(host.transform, false);
        lamp.transform.localPosition = Vector3.zero;
        lamp.AddComponent<Light>();
      }

      var light = lamp.GetComponent<Light>();
      if (light == null) {
        return;
      }
      light.type = LightType.Point;
      light.color = ColourFor(school);
      light.intensity = Intensity != null ? Intensity.Value : 6f;
      light.range = Range != null ? Range.Value : 22f;
      // No shadows on purpose: eight shadow-casting point lights across a 76 m gallery is
      // a real frame cost, and a rune reads by its own glow rather than by what it casts.
      light.shadows = LightShadows.None;
    } catch (Exception) {
      // Same as above — an unlit monument is not worth breaking a build over.
    }
  }
}

/// <summary>Re-hang the rune lamps whenever a marked piece is constructed.
///
/// Not a seam, for the same reason the keep-standing patch is not one: it observes
/// nothing. It shares WearNTear.Awake with that patch quite happily — Harmony composes
/// postfixes — and lives in its own file so this lane can be lifted out whole.</summary>
public static class RuneLightPatches {
  public static void Apply(Harmony harmony) {
    try {
      LabRuneLight.Bind(ComfyQuestLab.Instance != null ? ComfyQuestLab.Instance.Config : null);
      var target = AccessTools.Method(typeof(WearNTear), "Awake");
      if (target == null) {
        ComfyQuestLab.LogInfo("rune lights: WearNTear.Awake not found — monuments will "
            + "build unlit. Nothing else is affected.");
        return;
      }
      harmony.Patch(
          target,
          postfix: new HarmonyMethod(
              AccessTools.Method(typeof(RuneLightPatches), nameof(HangLampPostfix))));
    } catch (Exception ex) {
      ComfyQuestLab.LogInfo("rune lights: could not patch WearNTear.Awake — " + ex.Message);
    }
  }

  static void HangLampPostfix(WearNTear __instance) {
    try {
      if (__instance == null) {
        return;
      }
      var view = __instance.GetComponent<ZNetView>();
      if (view == null) {
        return;
      }
      ZDO zdo = view.GetZDO();
      if (zdo == null) {
        return;
      }
      string school = LabRuneLight.SchoolOf(zdo);
      if (school.Length == 0) {
        return;
      }
      LabRuneLight.Apply(__instance.gameObject, school);
    } catch (Exception) {
      // Runs for every piece in the world; one failure is not worth a repeating log line.
    }
  }
}

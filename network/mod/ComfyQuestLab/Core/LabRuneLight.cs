namespace ComfyQuestLab;

using System;
using System.Globalization;

using BepInEx.Configuration;

using HarmonyLib;

using TMPro;

using UnityEngine;

/// <summary>Light the monuments and their long-form school headers.
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
/// One lamp per monument, not per beam: scores of realtime point lights would be a
/// frame-rate bill for no visual gain, and one at the rune's middle covers its strokes.
/// Gallery v2 adds one more at the middle of each lettered header, still one per word
/// rather than one per letter.</summary>
public static class LabRuneLight {
  /// <summary>ZDO string field on a rune beam or central header sign that carries a lamp;
  /// the value is the school, so the colour survives a reload without a lookup table.</summary>
  public const string RuneLightMark = "comfyQuestLabRuneLight";
  public const string RuneLightStyleMark = "comfyQuestLabRuneLightStyle";
  public const string SignTextGlowMark = "comfyQuestLabSignTextGlow";
  public const string SignFaceStyle = "sign-face";
  public const string BannerFaceStyle = "banner-face";

  const string LampChildName = "comfy-quest-lab-rune-lamp";

  public static ConfigEntry<bool> Enabled;
  public static ConfigEntry<float> Intensity;
  public static ConfigEntry<float> Range;

  /// <summary>A colour per school, read out of the plan rather than kept here.
  ///
  /// The generator writes the same values into the sign heading at each hall mouth, so
  /// keeping a second copy in the mod would let the lamp and the sign drift apart on any
  /// day somebody edited one of them. One table, two consumers.</summary>
  public static Color ColourFor(string school) {
    if (string.IsNullOrEmpty(school)) {
      return Color.white;
    }
    foreach (LabGalleryPlan.Monument monument in LabGalleryPlan.Monuments) {
      if (monument.Category == school) {
        return new Color(monument.R, monument.G, monument.B);
      }
    }
    return Color.white;
  }

  /// <summary>Bound off the plugin's own config file rather than through LabConfig, so
  /// this lane can be added and removed without touching a shared file.</summary>
  public static void Bind(ConfigFile config) {
    if (Enabled != null || config == null) {
      return;
    }
    Enabled = config.Bind("Gallery", "runeLights", true,
        "Hang a coloured light on each monument and long-form header. Client-side only — the "
        + "lamps are not networked and nobody else sees them. Hot-reloadable.");
    // 3 / 11, dialled in against the marble backdrop rather than guessed.
    //
    // This moved a long way and the reason is worth keeping. With the rune standing
    // against open sky it needed 40 / 6 — bright and tight, because a wide reach lit the
    // mist between you and the glyph instead of the glyph, and eight of those overlapped
    // into a white wall in any weather at all. Once each rune got a black backdrop there
    // was nothing left to out-shine: the panel does the contrast, so the lamp only has to
    // wash it. A soft, wider light reads better there, and 40 / 6 now blows out.
    //
    // The lesson is that this pair is not a property of the lamp, it is a property of
    // what the rune is seen against. Change the backdrop and expect to retune, live,
    // with questlab_runelight.
    Intensity = config.Bind("Gallery", "runeLightIntensity", 3f,
        new ConfigDescription("How bright each monument's lamp burns.",
            new AcceptableValueRange<float>(0f, 200f)));
    Range = config.Bind("Gallery", "runeLightRange", 11f,
        new ConfigDescription("How far each monument's lamp reaches, in metres. Against a "
            + "backdrop this can be generous; against open sky keep it short, because "
            + "reach is what turns light mist into haze.",
            new AcceptableValueRange<float>(1f, 128f)));

    // Retune what is already standing, rather than only what gets built next. A lamp is
    // a judgement call about light in weather nobody can predict — the useful form of
    // that knob is one you turn while looking at the thing, not one that needs a rebuild
    // to take effect.
    Enabled.SettingChanged += (s, e) => Retune();
    Intensity.SettingChanged += (s, e) => Retune();
    Range.SettingChanged += (s, e) => Retune();
  }

  /// <summary>Re-apply the current settings to every lamp already in the world.
  ///
  /// Finds them the same way clear does — WearNTear.GetAllInstances() over loaded pieces,
  /// filtered by the mark — so it needs no register of live lamps to go stale.</summary>
  public static int Retune() {
    int touched = 0;
    try {
      foreach (WearNTear wear in WearNTear.GetAllInstances()) {
        if (wear == null) {
          continue;
        }
        var view = wear.GetComponent<ZNetView>();
        ZDO zdo = view == null ? null : view.GetZDO();
        if (zdo == null) {
          continue;
        }
        bool changed = false;
        string glowSchool = TextGlowSchoolOf(zdo);
        if (glowSchool.Length > 0) {
          ApplyTextGlow(wear.gameObject, glowSchool);
          changed = true;
        }
        string school = SchoolOf(zdo);
        if (school.Length > 0) {
          Apply(wear.gameObject, school, StyleOf(zdo));
          changed = true;
        }
        if (changed) {
          touched++;
        }
      }
    } catch (Exception) {
      // Cosmetic. A lamp that will not retune is not worth interrupting anything for.
    }
    return touched;
  }

  /// <summary>Console entry point: read or set the lamps, and retune what is standing.
  ///
  /// Values are written back to the config file, so a look dialled in against real
  /// weather survives a restart instead of being re-guessed every session.</summary>
  public static string Tune(string intensityArg, string rangeArg) {
    if (Enabled == null) {
      return "rune lights are not bound yet — load a world first.";
    }

    if (string.IsNullOrEmpty(intensityArg)) {
      return "rune lights " + (Enabled.Value ? "on" : "off")
           + ", intensity " + Intensity.Value.ToString("0.##")
           + ", range " + Range.Value.ToString("0.##") + " m. "
           + "Set with questlab_runelight <intensity> [range], or off | on.";
    }

    if (string.Equals(intensityArg, "off", StringComparison.OrdinalIgnoreCase)
        || string.Equals(intensityArg, "on", StringComparison.OrdinalIgnoreCase)) {
      Enabled.Value = string.Equals(intensityArg, "on", StringComparison.OrdinalIgnoreCase);
      return "rune lights " + (Enabled.Value ? "on" : "off") + " — " + Retune() + " retuned.";
    }

    float intensity;
    if (!float.TryParse(intensityArg, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out intensity)) {
      return "not a number: " + intensityArg;
    }
    float wanted = intensity;
    Intensity.Value = Mathf.Clamp(intensity, 0f, 200f);
    string clamped = Mathf.Abs(wanted - Intensity.Value) > 0.01f
        ? " (asked for " + wanted.ToString("0.##") + ", capped)"
        : string.Empty;

    float range;
    if (!string.IsNullOrEmpty(rangeArg)
        && float.TryParse(rangeArg, NumberStyles.Float, CultureInfo.InvariantCulture,
                          out range)) {
      Range.Value = Mathf.Clamp(range, 1f, 128f);
    }

    return "intensity " + Intensity.Value.ToString("0.##") + clamped
         + ", range " + Range.Value.ToString("0.##") + " m — "
         + Retune() + " lamp(s) retuned.";
  }

  /// <summary>Mark a piece as the one carrying this monument's lamp.</summary>
  public static void Mark(ZDO zdo, string school, string style = null) {
    if (zdo == null || string.IsNullOrEmpty(school)) {
      return;
    }
    try {
      zdo.Set(RuneLightMark, school);
      zdo.Set(RuneLightStyleMark, style ?? string.Empty);
    } catch (Exception) {
      // A monument that ends up unlit is a cosmetic loss, not a failed build.
    }
  }

  /// <summary>Mark a sign whose glyph material should carry a school-coloured halo.
  /// This is deliberately not RuneLightMark: a whole banner may glow while only its
  /// centre letter owns the single realtime point light.</summary>
  public static void MarkTextGlow(ZDO zdo, string school) {
    if (zdo == null || string.IsNullOrEmpty(school)) {
      return;
    }
    try {
      zdo.Set(SignTextGlowMark, school);
    } catch (Exception) {
      // Readability polish must never turn a successful gallery build into a failure.
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

  public static string StyleOf(ZDO zdo) {
    if (zdo == null) {
      return string.Empty;
    }
    try {
      return zdo.GetString(RuneLightStyleMark, string.Empty);
    } catch (Exception) {
      return string.Empty;
    }
  }

  public static string TextGlowSchoolOf(ZDO zdo) {
    if (zdo == null) {
      return string.Empty;
    }
    try {
      return zdo.GetString(SignTextGlowMark, string.Empty);
    } catch (Exception) {
      return string.Empty;
    }
  }

  /// <summary>Give one sign's TextMeshPro material a compact HDR halo. Each widget gets
  /// its own material instance, so school colour cannot leak into ordinary signs or the
  /// neighbouring letters. The glow shader expands the glyph itself; unlike a point
  /// light, it remains readable through rain and mist without illuminating the floor.</summary>
  public static void ApplyTextGlow(GameObject host, string school) {
    if (host == null || string.IsNullOrEmpty(school)) {
      return;
    }
    try {
      Sign sign = host.GetComponent<Sign>();
      TextMeshProUGUI widget = sign == null ? null : sign.m_textWidget;
      if (widget == null) {
        return;
      }
      Material material = widget.fontMaterial;
      if (material == null || !material.HasProperty(ShaderUtilities.ID_GlowColor)) {
        return;
      }

      bool enabled = Enabled == null || Enabled.Value;
      if (!enabled) {
        material.DisableKeyword(ShaderUtilities.Keyword_Glow);
      } else {
        Color colour = ColourFor(school);
        // Values above one are intentional: bloom sees the letters as emissive while
        // the tight shader halo keeps their school colour and does not wash the marble.
        Color glow = new Color(colour.r * 2.2f, colour.g * 2.2f, colour.b * 2.2f, 0.9f);
        material.EnableKeyword(ShaderUtilities.Keyword_Glow);
        material.SetColor(ShaderUtilities.ID_GlowColor, glow);
        material.SetFloat(ShaderUtilities.ID_GlowOffset, 0f);
        material.SetFloat(ShaderUtilities.ID_GlowInner, 0.02f);
        material.SetFloat(ShaderUtilities.ID_GlowOuter, 0.22f);
        material.SetFloat(ShaderUtilities.ID_GlowPower, 0.55f);
      }
      widget.UpdateMeshPadding();
      widget.SetMaterialDirty();
    } catch (Exception) {
      // Cosmetic and client-local, exactly like the nearby point-light treatment.
    }
  }

  /// <summary>Hang the lamp, or take it down if the config says so. Idempotent: a piece
  /// that already has its lamp is retuned rather than given a second one.</summary>
  public static void Apply(GameObject host, string school, string style = null) {
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
        lamp.AddComponent<Light>();
      }

      bool signFace = string.Equals(style, SignFaceStyle, StringComparison.Ordinal);
      bool bannerFace = string.Equals(style, BannerFaceStyle, StringComparison.Ordinal);
      bool faceLamp = signFace || bannerFace;
      // The Social prompt is only two metres above a reflective marble floor. The ordinary
      // 11 m rune wash turned that floor into a white bloom and left the sign itself dark.
      // Put a tight lamp just off the sign face; at 1.6 m it cannot reach the floor, while
      // the sign and top of its post remain visibly pink. Bridge banners use the same face
      // offset with a 5.5 m word wash that still stops above the floor; rune hosts keep the
      // ordinary wide setting.
      lamp.transform.localPosition = faceLamp
          ? new Vector3(0f, 0f, -0.32f)
          : Vector3.zero;

      var light = lamp.GetComponent<Light>();
      if (light == null) {
        return;
      }
      light.type = LightType.Point;
      light.color = ColourFor(school);
      float configuredIntensity = Intensity != null ? Intensity.Value : 6f;
      float configuredRange = Range != null ? Range.Value : 22f;
      light.intensity = signFace
          ? Mathf.Min(configuredIntensity, 1.5f)
          : (bannerFace ? Mathf.Min(configuredIntensity, 2.5f) : configuredIntensity);
      light.range = signFace
          ? Mathf.Min(configuredRange, 1.6f)
          : (bannerFace ? Mathf.Min(configuredRange, 5.5f) : configuredRange);
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
      string glowSchool = LabRuneLight.TextGlowSchoolOf(zdo);
      if (glowSchool.Length > 0) {
        LabRuneLight.ApplyTextGlow(__instance.gameObject, glowSchool);
      }
      string school = LabRuneLight.SchoolOf(zdo);
      if (school.Length == 0) {
        return;
      }
      LabRuneLight.Apply(__instance.gameObject, school, LabRuneLight.StyleOf(zdo));
    } catch (Exception) {
      // Runs for every piece in the world; one failure is not worth a repeating log line.
    }
  }
}

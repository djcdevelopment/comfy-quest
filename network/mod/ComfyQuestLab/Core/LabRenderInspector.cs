namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using HarmonyLib;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Read the renderer state Valheim is actually using for one prefab and its
/// loaded instances.
///
/// Visual acceptance belongs to the rendered frame, but diagnosis should not depend on
/// guessing from source placement. This inspector records the asset's startup material
/// state before world pieces load, its current prefab state, and every distinct state
/// found on loaded instances. It reads shared materials deliberately: touching
/// <c>Renderer.material</c> would instantiate a material and change the state being
/// inspected.
///
/// The JSON artifact includes every shader property, every enabled keyword, renderer
/// property-block overrides, GI flags, and child lights. The console summary answers the
/// immediate questions (is emission present, did the shared prefab material change, and
/// do live instances differ) without pretending those answers replace a human look.</summary>
public static class LabRenderInspector {
  const string Schema = "comfy-questlab-render-inspection/v1";
  const int MaxNameLength = 80;
  const int MaxSamplePositions = 8;

  // Keep startup snapshots bounded to the surfaces under active consideration. An exact
  // prefab can still be inspected later; it simply reports that no startup baseline was
  // captured for that name.
  static readonly string[] StartupPrefabNames = {
    "blackmarble_floor",
    "blackmarble_2x2x1",
    "blackmarble_2x2x2",
    "blackmarble_base_2",
    "blackmarble_tile_floor_2x2",
    "stone_floor_2x2",
  };

  static readonly Dictionary<string, Baseline> _startup =
      new Dictionary<string, Baseline>(StringComparer.OrdinalIgnoreCase);

  sealed class Baseline {
    public string Digest;
    public int EmissionSignals;
    public int EnabledLights;
  }

  sealed class RootState {
    public string Name;
    public string Digest;
    public int RendererCount;
    public int MaterialCount;
    public int EmissionSignals;
    public int PropertyBlockEmissionSignals;
    public int LightCount;
    public int EnabledLights;
    public readonly List<RendererState> Renderers = new List<RendererState>();
    public readonly List<LightState> Lights = new List<LightState>();
  }

  sealed class RendererState {
    public string Path;
    public string Type;
    public bool Enabled;
    public bool HasPropertyBlock;
    public int LightmapIndex;
    public int RealtimeLightmapIndex;
    public string ShadowCastingMode;
    public bool ReceiveShadows;
    public readonly List<MaterialState> Materials = new List<MaterialState>();
    public readonly List<PropertyBlockState> PropertyBlocks = new List<PropertyBlockState>();
  }

  sealed class MaterialState {
    public int Slot;
    public int InstanceId;
    public string Name;
    public string Shader;
    public string GiFlags;
    public int RenderQueue;
    public readonly List<string> Keywords = new List<string>();
    public readonly List<PropertyState> Properties = new List<PropertyState>();
    public readonly List<string> EmissionEvidence = new List<string>();
  }

  sealed class PropertyState {
    public int Id;
    public string Name;
    public string Type;
    public string Value;
    public bool IlluminationRelated;
    public bool ActiveSignal;
  }

  sealed class PropertyBlockState {
    public int Slot;
    public readonly List<PropertyState> Properties = new List<PropertyState>();
  }

  sealed class LightState {
    public string Path;
    public string Type;
    public bool Enabled;
    public float Intensity;
    public float Range;
    public Color Color;
    public string Shadows;
  }

  sealed class LiveGroup {
    public RootState State;
    public int Count;
    public int MarkedCount;
    public readonly List<Vector3> SamplePositions = new List<Vector3>();
  }

  /// <summary>Capture immutable string summaries immediately after ZNetScene has built
  /// its prefab table and before saved world instances are streamed in.</summary>
  public static void CaptureStartupBaselines(ZNetScene scene = null) {
    try {
      scene = scene != null ? scene : ZNetScene.instance;
      if (scene == null) {
        return;
      }
      foreach (string prefabName in StartupPrefabNames) {
        GameObject prefab = scene.GetPrefab(prefabName);
        if (prefab == null) {
          continue;
        }
        RootState state = CaptureRoot(prefab);
        _startup[prefabName] = new Baseline {
          Digest = state.Digest,
          EmissionSignals = state.EmissionSignals + state.PropertyBlockEmissionSignals,
          EnabledLights = state.EnabledLights,
        };
      }
    } catch (Exception ex) {
      ComfyQuestLab.LogInfo("render inspector startup snapshot failed: " + ex.Message);
    }
  }

  /// <summary>Write a machine-readable artifact and return a bounded human summary.</summary>
  public static string Inspect(string prefabName) {
    if (ZNetScene.instance == null) {
      return "not in a world yet — load a world first.";
    }
    if (!SafePrefabName(prefabName)) {
      return "give one exact prefab name (letters, numbers, underscore, dash; max 80).";
    }

    GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
    if (prefab == null) {
      return "no prefab named '" + prefabName + "' — search with questlab_prefabs "
          + prefabName + ".";
    }

    try {
      RootState prefabState = CaptureRoot(prefab);
      var groups = CaptureLiveGroups(prefabName, prefab.name.GetStableHashCode());
      Baseline startup;
      bool hasStartup = _startup.TryGetValue(prefabName, out startup);

      string dir = Path.Combine(BepInEx.Paths.ConfigPath,
          Path.Combine("comfy-quest-lab", "render-inspections"));
      Directory.CreateDirectory(dir);
      string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
      string path = Path.Combine(dir, prefabName + "-" + stamp + ".json");
      File.WriteAllText(path, ToJson(prefabName, prefabState, hasStartup ? startup : null, groups));

      int liveCount = 0;
      int markedCount = 0;
      int liveEmissionGroups = 0;
      int liveLightGroups = 0;
      foreach (LiveGroup group in groups.Values) {
        liveCount += group.Count;
        markedCount += group.MarkedCount;
        if (group.State.EmissionSignals + group.State.PropertyBlockEmissionSignals > 0) {
          liveEmissionGroups++;
        }
        if (group.State.EnabledLights > 0) {
          liveLightGroups++;
        }
      }

      bool startupChanged = hasStartup && !string.Equals(
          startup.Digest, prefabState.Digest, StringComparison.Ordinal);
      bool anyLiveDiffers = false;
      foreach (LiveGroup group in groups.Values) {
        if (!string.Equals(group.State.Digest, prefabState.Digest, StringComparison.Ordinal)) {
          anyLiveDiffers = true;
          break;
        }
      }

      var sb = new StringBuilder();
      sb.AppendLine("render inspection " + prefabName);
      sb.AppendLine("prefab: " + prefabState.RendererCount + " renderer(s), "
          + prefabState.MaterialCount + " material slot(s), "
          + (prefabState.EmissionSignals + prefabState.PropertyBlockEmissionSignals)
          + " illumination signal(s), " + prefabState.EnabledLights + " enabled light(s)");
      sb.AppendLine("startup baseline: " + (hasStartup
          ? (startupChanged ? "CHANGED since ZNetScene startup" : "unchanged")
              + " (started with " + startup.EmissionSignals + " illumination signal(s), "
              + startup.EnabledLights + " enabled light(s))"
          : "not captured for this prefab"));
      sb.AppendLine("loaded: " + liveCount + " instance(s), " + markedCount
          + " Quest Lab-marked, " + groups.Count + " distinct render state(s); "
          + liveEmissionGroups + " state(s) with illumination signals, "
          + liveLightGroups + " with enabled lights; "
          + (anyLiveDiffers ? "LIVE STATE DIFFERS FROM PREFAB" : "same as current prefab"));
      AppendEmissionSummary(sb, prefabState);
      sb.Append("artifact: ").Append(path);
      return sb.ToString();
    } catch (Exception ex) {
      return "render inspection failed: " + ex.Message;
    }
  }

  static Dictionary<string, LiveGroup> CaptureLiveGroups(string prefabName, int prefabHash) {
    var groups = new Dictionary<string, LiveGroup>(StringComparer.Ordinal);
    var seen = new HashSet<int>();
    foreach (WearNTear wear in WearNTear.GetAllInstances()) {
      if (wear == null || !seen.Add(wear.gameObject.GetInstanceID())) {
        continue;
      }
      ZNetView view = wear.GetComponent<ZNetView>();
      ZDO zdo = view == null ? null : view.GetZDO();
      if (zdo == null || zdo.GetPrefab() != prefabHash) {
        continue;
      }

      RootState state = CaptureRoot(wear.gameObject);
      LiveGroup group;
      if (!groups.TryGetValue(state.Digest, out group)) {
        group = new LiveGroup { State = state };
        groups[state.Digest] = group;
      }
      group.Count++;
      if (LabGalleryBuilder.IsGalleryPiece(zdo)) {
        group.MarkedCount++;
      }
      if (group.SamplePositions.Count < MaxSamplePositions) {
        group.SamplePositions.Add(wear.transform.position);
      }
    }
    return groups;
  }

  static RootState CaptureRoot(GameObject root) {
    var state = new RootState { Name = root == null ? "null" : root.name };
    if (root == null) {
      state.Digest = Digest("null");
      return state;
    }

    Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
    state.RendererCount = renderers.Length;
    foreach (Renderer renderer in renderers) {
      if (renderer == null) {
        continue;
      }
      RendererState rendererState = CaptureRenderer(root.transform, renderer);
      state.Renderers.Add(rendererState);
      state.MaterialCount += rendererState.Materials.Count;
      foreach (MaterialState material in rendererState.Materials) {
        state.EmissionSignals += material.EmissionEvidence.Count;
      }
      foreach (PropertyBlockState block in rendererState.PropertyBlocks) {
        foreach (PropertyState property in block.Properties) {
          if (property.ActiveSignal) {
            state.PropertyBlockEmissionSignals++;
          }
        }
      }
    }

    foreach (Light light in root.GetComponentsInChildren<Light>(true)) {
      if (light == null) {
        continue;
      }
      state.LightCount++;
      if (light.enabled && light.gameObject.activeInHierarchy) {
        state.EnabledLights++;
      }
      state.Lights.Add(new LightState {
        Path = RelativePath(root.transform, light.transform),
        Type = light.type.ToString(),
        Enabled = light.enabled && light.gameObject.activeInHierarchy,
        Intensity = light.intensity,
        Range = light.range,
        Color = light.color,
        Shadows = light.shadows.ToString(),
      });
    }

    state.Digest = Digest(Signature(state));
    return state;
  }

  static RendererState CaptureRenderer(Transform root, Renderer renderer) {
    var state = new RendererState {
      Path = RelativePath(root, renderer.transform),
      Type = renderer.GetType().Name,
      Enabled = renderer.enabled && renderer.gameObject.activeInHierarchy,
      HasPropertyBlock = renderer.HasPropertyBlock(),
      LightmapIndex = renderer.lightmapIndex,
      RealtimeLightmapIndex = renderer.realtimeLightmapIndex,
      ShadowCastingMode = renderer.shadowCastingMode.ToString(),
      ReceiveShadows = renderer.receiveShadows,
    };

    Material[] materials = renderer.sharedMaterials;
    for (int slot = 0; slot < materials.Length; slot++) {
      state.Materials.Add(CaptureMaterial(materials[slot], slot));
    }

    if (renderer.HasPropertyBlock()) {
      var global = new MaterialPropertyBlock();
      renderer.GetPropertyBlock(global);
      PropertyBlockState globalState = CapturePropertyBlock(global, -1, state.Materials);
      if (globalState.Properties.Count > 0) {
        state.PropertyBlocks.Add(globalState);
      }
      for (int slot = 0; slot < materials.Length; slot++) {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block, slot);
        PropertyBlockState slotState = CapturePropertyBlock(block, slot, state.Materials);
        if (slotState.Properties.Count > 0) {
          state.PropertyBlocks.Add(slotState);
        }
      }
    }
    return state;
  }

  static MaterialState CaptureMaterial(Material material, int slot) {
    var state = new MaterialState { Slot = slot };
    if (material == null) {
      state.Name = "null";
      state.Shader = "null";
      state.GiFlags = "unknown";
      return state;
    }

    state.InstanceId = material.GetInstanceID();
    state.Name = material.name;
    state.Shader = material.shader == null ? "null" : material.shader.name;
    state.GiFlags = material.globalIlluminationFlags.ToString();
    state.RenderQueue = material.renderQueue;
    foreach (string keyword in material.shaderKeywords ?? new string[0]) {
      state.Keywords.Add(keyword);
      if (IlluminationName(keyword)) {
        state.EmissionEvidence.Add("keyword " + keyword);
      }
    }
    state.Keywords.Sort(StringComparer.Ordinal);

    Shader shader = material.shader;
    if (shader == null) {
      return state;
    }
    for (int index = 0; index < shader.GetPropertyCount(); index++) {
      string name = shader.GetPropertyName(index);
      int id = shader.GetPropertyNameId(index);
      ShaderPropertyType type = shader.GetPropertyType(index);
      var property = new PropertyState {
        Id = id,
        Name = name,
        Type = type.ToString(),
        IlluminationRelated = IlluminationName(name),
      };
      try {
        switch (type) {
          case ShaderPropertyType.Color:
            Color color = material.GetColor(id);
            property.Value = ColorText(color);
            property.ActiveSignal = property.IlluminationRelated && ColorSignal(color);
            break;
          case ShaderPropertyType.Vector:
            Vector4 vector = material.GetVector(id);
            property.Value = VectorText(vector);
            property.ActiveSignal = property.IlluminationRelated && VectorSignal(vector);
            break;
          case ShaderPropertyType.Float:
          case ShaderPropertyType.Range:
            float number = material.GetFloat(id);
            property.Value = Num(number);
            property.ActiveSignal = property.IlluminationRelated && Mathf.Abs(number) > 0.0001f;
            break;
          case ShaderPropertyType.Int:
            int integer = material.GetInteger(id);
            property.Value = integer.ToString(CultureInfo.InvariantCulture);
            property.ActiveSignal = property.IlluminationRelated && integer != 0;
            break;
          case ShaderPropertyType.Texture:
            Texture texture = material.GetTexture(id);
            property.Value = texture == null ? "null" : texture.name;
            property.ActiveSignal = property.IlluminationRelated && texture != null;
            break;
          default:
            property.Value = "unsupported";
            break;
        }
      } catch (Exception ex) {
        property.Value = "unreadable: " + ex.GetType().Name;
      }
      state.Properties.Add(property);
      if (property.ActiveSignal) {
        state.EmissionEvidence.Add(property.Name + "=" + property.Value);
      }
    }
    return state;
  }

  static PropertyBlockState CapturePropertyBlock(
      MaterialPropertyBlock block, int slot, List<MaterialState> materials) {
    var state = new PropertyBlockState { Slot = slot };
    var seen = new HashSet<int>();
    foreach (MaterialState material in materials) {
      foreach (PropertyState candidate in material.Properties) {
        if (!candidate.IlluminationRelated || !seen.Add(candidate.Id)
            || !block.HasProperty(candidate.Id)) {
          continue;
        }
        var property = new PropertyState {
          Id = candidate.Id,
          Name = candidate.Name,
          Type = candidate.Type,
          IlluminationRelated = true,
        };
        try {
          if (block.HasColor(candidate.Id)) {
            Color color = block.GetColor(candidate.Id);
            property.Type = "Color";
            property.Value = ColorText(color);
            property.ActiveSignal = ColorSignal(color);
          } else if (block.HasTexture(candidate.Id)) {
            Texture texture = block.GetTexture(candidate.Id);
            property.Type = "Texture";
            property.Value = texture == null ? "null" : texture.name;
            property.ActiveSignal = texture != null;
          } else if (block.HasVector(candidate.Id)) {
            Vector4 vector = block.GetVector(candidate.Id);
            property.Type = "Vector";
            property.Value = VectorText(vector);
            property.ActiveSignal = VectorSignal(vector);
          } else if (block.HasFloat(candidate.Id)) {
            float number = block.GetFloat(candidate.Id);
            property.Type = "Float";
            property.Value = Num(number);
            property.ActiveSignal = Mathf.Abs(number) > 0.0001f;
          } else {
            property.Value = "present (type unavailable)";
            property.ActiveSignal = true;
          }
        } catch (Exception ex) {
          property.Value = "unreadable: " + ex.GetType().Name;
        }
        state.Properties.Add(property);
      }
    }
    return state;
  }

  static void AppendEmissionSummary(StringBuilder sb, RootState state) {
    var lines = new List<string>();
    foreach (RendererState renderer in state.Renderers) {
      foreach (MaterialState material in renderer.Materials) {
        if (material.EmissionEvidence.Count == 0) {
          continue;
        }
        lines.Add(renderer.Path + " / " + material.Name + " / " + material.Shader
            + ": " + string.Join(", ", material.EmissionEvidence));
      }
      foreach (PropertyBlockState block in renderer.PropertyBlocks) {
        foreach (PropertyState property in block.Properties) {
          if (property.ActiveSignal) {
            lines.Add(renderer.Path + " / property block "
                + (block.Slot < 0 ? "all" : block.Slot.ToString(CultureInfo.InvariantCulture))
                + ": " + property.Name + "=" + property.Value);
          }
        }
      }
    }
    if (lines.Count == 0) {
      sb.AppendLine("illumination evidence: none in material keywords/properties or renderer blocks");
      return;
    }
    sb.AppendLine("illumination evidence:");
    for (int i = 0; i < lines.Count && i < 8; i++) {
      sb.AppendLine("  " + lines[i]);
    }
    if (lines.Count > 8) {
      sb.AppendLine("  … " + (lines.Count - 8) + " more in the artifact");
    }
  }

  static string ToJson(
      string prefabName,
      RootState prefab,
      Baseline startup,
      Dictionary<string, LiveGroup> groups) {
    var sb = new StringBuilder(64 * 1024);
    sb.Append("{\n  \"schema\": ").Append(Quote(Schema));
    sb.Append(",\n  \"pluginRelease\": ").Append(Quote(ComfyQuestLab.ReleaseId));
    sb.Append(",\n  \"generatedAt\": ").Append(Quote(DateTime.UtcNow.ToString(
        "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
    sb.Append(",\n  \"prefabName\": ").Append(Quote(prefabName));
    sb.Append(",\n  \"startupBaseline\": ");
    if (startup == null) {
      sb.Append("null");
    } else {
      sb.Append("{\"digest\": ").Append(Quote(startup.Digest))
        .Append(", \"illuminationSignals\": ").Append(startup.EmissionSignals)
        .Append(", \"enabledLights\": ").Append(startup.EnabledLights).Append('}');
    }
    sb.Append(",\n  \"prefab\": ");
    AppendRootJson(sb, prefab, "  ");
    sb.Append(",\n  \"liveGroups\": [");
    int groupIndex = 0;
    foreach (LiveGroup group in groups.Values) {
      if (groupIndex++ > 0) sb.Append(',');
      sb.Append("\n    {\"count\": ").Append(group.Count)
        .Append(", \"questLabMarked\": ").Append(group.MarkedCount)
        .Append(", \"samplePositions\": [");
      for (int i = 0; i < group.SamplePositions.Count; i++) {
        if (i > 0) sb.Append(", ");
        sb.Append(Vector3Json(group.SamplePositions[i]));
      }
      sb.Append("], \"state\": ");
      AppendRootJson(sb, group.State, "    ");
      sb.Append('}');
    }
    if (groupIndex > 0) sb.Append('\n').Append("  ");
    sb.Append("]\n}\n");
    return sb.ToString();
  }

  static void AppendRootJson(StringBuilder sb, RootState state, string indent) {
    sb.Append("{\"name\": ").Append(Quote(state.Name))
      .Append(", \"digest\": ").Append(Quote(state.Digest))
      .Append(", \"rendererCount\": ").Append(state.RendererCount)
      .Append(", \"materialCount\": ").Append(state.MaterialCount)
      .Append(", \"materialIlluminationSignals\": ").Append(state.EmissionSignals)
      .Append(", \"propertyBlockIlluminationSignals\": ")
      .Append(state.PropertyBlockEmissionSignals)
      .Append(", \"lightCount\": ").Append(state.LightCount)
      .Append(", \"enabledLights\": ").Append(state.EnabledLights)
      .Append(", \"renderers\": [");
    for (int i = 0; i < state.Renderers.Count; i++) {
      if (i > 0) sb.Append(',');
      AppendRendererJson(sb, state.Renderers[i]);
    }
    sb.Append("], \"lights\": [");
    for (int i = 0; i < state.Lights.Count; i++) {
      if (i > 0) sb.Append(',');
      LightState light = state.Lights[i];
      sb.Append("{\"path\": ").Append(Quote(light.Path))
        .Append(", \"type\": ").Append(Quote(light.Type))
        .Append(", \"enabled\": ").Append(Bool(light.Enabled))
        .Append(", \"intensity\": ").Append(Num(light.Intensity))
        .Append(", \"range\": ").Append(Num(light.Range))
        .Append(", \"color\": ").Append(ColorJson(light.Color))
        .Append(", \"shadows\": ").Append(Quote(light.Shadows)).Append('}');
    }
    sb.Append("]}");
  }

  static void AppendRendererJson(StringBuilder sb, RendererState renderer) {
    sb.Append("{\"path\": ").Append(Quote(renderer.Path))
      .Append(", \"type\": ").Append(Quote(renderer.Type))
      .Append(", \"enabled\": ").Append(Bool(renderer.Enabled))
      .Append(", \"hasPropertyBlock\": ").Append(Bool(renderer.HasPropertyBlock))
      .Append(", \"lightmapIndex\": ").Append(renderer.LightmapIndex)
      .Append(", \"realtimeLightmapIndex\": ").Append(renderer.RealtimeLightmapIndex)
      .Append(", \"shadowCastingMode\": ").Append(Quote(renderer.ShadowCastingMode))
      .Append(", \"receiveShadows\": ").Append(Bool(renderer.ReceiveShadows))
      .Append(", \"materials\": [");
    for (int i = 0; i < renderer.Materials.Count; i++) {
      if (i > 0) sb.Append(',');
      AppendMaterialJson(sb, renderer.Materials[i]);
    }
    sb.Append("], \"propertyBlocks\": [");
    for (int i = 0; i < renderer.PropertyBlocks.Count; i++) {
      if (i > 0) sb.Append(',');
      PropertyBlockState block = renderer.PropertyBlocks[i];
      sb.Append("{\"slot\": ").Append(block.Slot).Append(", \"properties\": [");
      for (int p = 0; p < block.Properties.Count; p++) {
        if (p > 0) sb.Append(',');
        AppendPropertyJson(sb, block.Properties[p]);
      }
      sb.Append("]}");
    }
    sb.Append("]}");
  }

  static void AppendMaterialJson(StringBuilder sb, MaterialState material) {
    sb.Append("{\"slot\": ").Append(material.Slot)
      .Append(", \"instanceId\": ").Append(material.InstanceId)
      .Append(", \"name\": ").Append(Quote(material.Name))
      .Append(", \"shader\": ").Append(Quote(material.Shader))
      .Append(", \"globalIlluminationFlags\": ").Append(Quote(material.GiFlags))
      .Append(", \"renderQueue\": ").Append(material.RenderQueue)
      .Append(", \"keywords\": ").Append(StringArrayJson(material.Keywords))
      .Append(", \"illuminationEvidence\": ").Append(StringArrayJson(material.EmissionEvidence))
      .Append(", \"properties\": [");
    for (int i = 0; i < material.Properties.Count; i++) {
      if (i > 0) sb.Append(',');
      AppendPropertyJson(sb, material.Properties[i]);
    }
    sb.Append("]}");
  }

  static void AppendPropertyJson(StringBuilder sb, PropertyState property) {
    sb.Append("{\"id\": ").Append(property.Id)
      .Append(", \"name\": ").Append(Quote(property.Name))
      .Append(", \"type\": ").Append(Quote(property.Type))
      .Append(", \"value\": ").Append(Quote(property.Value))
      .Append(", \"illuminationRelated\": ").Append(Bool(property.IlluminationRelated))
      .Append(", \"activeSignal\": ").Append(Bool(property.ActiveSignal)).Append('}');
  }

  static string Signature(RootState state) {
    var sb = new StringBuilder();
    foreach (RendererState renderer in state.Renderers) {
      sb.Append(renderer.Path).Append('|').Append(renderer.Type).Append('|')
        .Append(renderer.Enabled).Append('|').Append(renderer.HasPropertyBlock).Append('|')
        .Append(renderer.LightmapIndex).Append('|').Append(renderer.RealtimeLightmapIndex)
        .Append('|').Append(renderer.ShadowCastingMode).Append('|').Append(renderer.ReceiveShadows);
      foreach (MaterialState material in renderer.Materials) {
        // Instance id is intentionally absent. A live renderer may carry a byte-for-byte
        // equivalent material instance; the digest describes rendered state, not identity.
        sb.Append("|M:").Append(material.Slot).Append('|').Append(material.Name)
          .Append('|').Append(material.Shader).Append('|').Append(material.GiFlags)
          .Append('|').Append(material.RenderQueue).Append('|')
          .Append(string.Join(",", material.Keywords));
        foreach (PropertyState property in material.Properties) {
          sb.Append('|').Append(property.Name).Append('=').Append(property.Value);
        }
      }
      foreach (PropertyBlockState block in renderer.PropertyBlocks) {
        sb.Append("|PB:").Append(block.Slot);
        foreach (PropertyState property in block.Properties) {
          sb.Append('|').Append(property.Name).Append('=').Append(property.Value);
        }
      }
    }
    foreach (LightState light in state.Lights) {
      sb.Append("|L:").Append(light.Path).Append('|').Append(light.Type).Append('|')
        .Append(light.Enabled).Append('|').Append(Num(light.Intensity)).Append('|')
        .Append(Num(light.Range)).Append('|').Append(ColorText(light.Color)).Append('|')
        .Append(light.Shadows);
    }
    return sb.ToString();
  }

  static bool IlluminationName(string value) {
    if (string.IsNullOrEmpty(value)) {
      return false;
    }
    value = value.ToLowerInvariant();
    return value.Contains("emiss") || value.Contains("emission")
        || value.Contains("glow") || value.Contains("illum") || value.Contains("bloom");
  }

  static bool ColorSignal(Color color) {
    return Mathf.Max(Mathf.Abs(color.r), Mathf.Abs(color.g), Mathf.Abs(color.b)) > 0.0001f;
  }

  static bool VectorSignal(Vector4 vector) {
    return Mathf.Max(Mathf.Abs(vector.x), Mathf.Abs(vector.y),
        Mathf.Abs(vector.z), Mathf.Abs(vector.w)) > 0.0001f;
  }

  static bool SafePrefabName(string value) {
    if (string.IsNullOrWhiteSpace(value) || value.Length > MaxNameLength) {
      return false;
    }
    foreach (char c in value) {
      if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) {
        return false;
      }
    }
    return true;
  }

  static string RelativePath(Transform root, Transform child) {
    if (root == null || child == null) {
      return "unknown";
    }
    if (root == child) {
      return ".";
    }
    var parts = new List<string>();
    Transform cursor = child;
    while (cursor != null && cursor != root) {
      parts.Add(cursor.name);
      cursor = cursor.parent;
    }
    parts.Reverse();
    return "./" + string.Join("/", parts);
  }

  static string Digest(string value) {
    using (SHA256 sha = SHA256.Create()) {
      byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
      var sb = new StringBuilder(bytes.Length * 2);
      foreach (byte b in bytes) {
        sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
      }
      return sb.ToString();
    }
  }

  static string StringArrayJson(List<string> values) {
    var sb = new StringBuilder("[");
    for (int i = 0; i < values.Count; i++) {
      if (i > 0) sb.Append(", ");
      sb.Append(Quote(values[i]));
    }
    return sb.Append(']').ToString();
  }

  static string Vector3Json(Vector3 value) {
    return "[" + Num(value.x) + ", " + Num(value.y) + ", " + Num(value.z) + "]";
  }

  static string ColorJson(Color value) {
    return "[" + Num(value.r) + ", " + Num(value.g) + ", " + Num(value.b) + ", "
        + Num(value.a) + "]";
  }

  static string ColorText(Color value) {
    return Num(value.r) + "," + Num(value.g) + "," + Num(value.b) + "," + Num(value.a);
  }

  static string VectorText(Vector4 value) {
    return Num(value.x) + "," + Num(value.y) + "," + Num(value.z) + "," + Num(value.w);
  }

  static string Num(float value) {
    return value.ToString("R", CultureInfo.InvariantCulture);
  }

  static string Bool(bool value) {
    return value ? "true" : "false";
  }

  static string Quote(string value) {
    if (value == null) {
      return "null";
    }
    var sb = new StringBuilder(value.Length + 2).Append('"');
    foreach (char c in value) {
      if (c == '"' || c == '\\') {
        sb.Append('\\').Append(c);
      } else if (c == '\n') {
        sb.Append("\\n");
      } else if (c == '\r') {
        sb.Append("\\r");
      } else if (c == '\t') {
        sb.Append("\\t");
      } else if (c < ' ') {
        sb.Append(' ');
      } else {
        sb.Append(c);
      }
    }
    return sb.Append('"').ToString();
  }
}

/// <summary>Capture prefab material defaults at the earliest useful point. A postfix on
/// ZNetScene.Awake runs after the exact prefab table exists and before saved ZDO pieces
/// are streamed into the world.</summary>
public static class LabRenderInspectorPatches {
  public static void Apply(Harmony harmony) {
    try {
      var target = AccessTools.Method(typeof(ZNetScene), "Awake");
      var postfix = AccessTools.Method(
          typeof(LabRenderInspectorPatches), nameof(CaptureStartupPostfix));
      if (target == null || postfix == null) {
        ComfyQuestLab.LogInfo("render inspector: ZNetScene.Awake unavailable; live inspection "
            + "still works, but startup comparison will be absent.");
        return;
      }
      harmony.Patch(target, postfix: new HarmonyMethod(postfix));
      if (ZNetScene.instance != null) {
        LabRenderInspector.CaptureStartupBaselines();
      }
    } catch (Exception ex) {
      ComfyQuestLab.LogInfo("render inspector startup hook failed: " + ex.Message);
    }
  }

  static void CaptureStartupPostfix(ZNetScene __instance) {
    LabRenderInspector.CaptureStartupBaselines(__instance);
  }
}

namespace ComfyQuestLab;

using System;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

/// <summary>Writes what this game build actually contains: every ZNetScene prefab with
/// its stable hash, piece-ness, wear component, snap points, and approximate mesh size.
///
/// This is the runtime half the component atlas cannot provide — the assembly knows
/// which components exist but not which prefabs carry them (component-packets README
/// names this exact artifact as the missing one). The offline blueprint generators
/// validate their palettes against this file, which turns "I think stone_floor_2x2
/// exists and is 2 m square" into a checked fact before a single piece is placed.
///
/// Snap points are the piece's own statement of its footprint: wood_floor carries four
/// at (±1, 0, ±1), which is how a generator learns the grid without a tape measure.
/// Mesh bounds are best-effort local-space sizes and are labeled approximate because a
/// prefab's visual can overhang its buildable extent.</summary>
public static class LabPrefabDump {
  public static string Write() {
    if (ZNetScene.instance == null) {
      return "not in a world yet — load a world first.";
    }

    string dir = Path.Combine(BepInEx.Paths.ConfigPath, "comfy-quest-lab");
    string path = Path.Combine(dir, "prefab-dump.json");
    try {
      Directory.CreateDirectory(dir);

      var sb = new StringBuilder(1 << 20);
      sb.Append("{\n");
      sb.Append("  \"schema\": \"comfy-prefab-dump/v1\",\n");
      sb.Append("  \"gameVersion\": ").Append(Quote(GameVersion())).Append(",\n");
      sb.Append("  \"generatedAt\": ")
        .Append(Quote(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ",
                                               CultureInfo.InvariantCulture)))
        .Append(",\n");

      int count = 0;
      var body = new StringBuilder(1 << 20);
      foreach (GameObject prefab in ZNetScene.instance.m_prefabs) {
        if (prefab == null) continue;
        if (count > 0) body.Append(",\n");
        AppendPrefab(body, prefab);
        count++;
      }

      sb.Append("  \"prefabCount\": ").Append(count).Append(",\n");
      sb.Append("  \"prefabs\": [\n").Append(body).Append("\n  ]\n}\n");
      File.WriteAllText(path, sb.ToString());
      return "prefab dump: " + count + " prefabs -> " + path;
    } catch (Exception ex) {
      return "prefab dump failed: " + ex.Message;
    }
  }

  static void AppendPrefab(StringBuilder sb, GameObject prefab) {
    sb.Append("    {\"name\": ").Append(Quote(prefab.name));
    sb.Append(", \"hash\": ").Append(prefab.name.GetStableHashCode());
    sb.Append(", \"netView\": ").Append(prefab.GetComponent<ZNetView>() != null ? "true" : "false");

    var piece = prefab.GetComponent<Piece>();
    sb.Append(", \"piece\": ").Append(piece != null ? "true" : "false");
    if (piece != null) {
      sb.Append(", \"category\": ").Append(Quote(piece.m_category.ToString()));
    }
    sb.Append(", \"wearNTear\": ").Append(prefab.GetComponent<WearNTear>() != null ? "true" : "false");

    // Snap points are plain child transforms tagged "snappoint"; a prefab asset's
    // children are enumerable without instantiating it.
    int snaps = 0;
    var snapSb = new StringBuilder();
    try {
      Transform t = prefab.transform;
      for (int i = 0; i < t.childCount; i++) {
        Transform child = t.GetChild(i);
        if (child == null || !child.CompareTag("snappoint")) continue;
        if (snaps > 0) snapSb.Append(", ");
        Vector3 p = child.localPosition;
        snapSb.Append('[').Append(Num(p.x)).Append(", ").Append(Num(p.y)).Append(", ")
              .Append(Num(p.z)).Append(']');
        snaps++;
      }
    } catch (Exception) {
      // A prefab whose tag table misbehaves loses its snap list, not the dump.
    }
    if (snaps > 0) {
      sb.Append(", \"snapPoints\": [").Append(snapSb).Append(']');
    }

    try {
      var mesh = prefab.GetComponentInChildren<MeshFilter>(true);
      if (mesh != null && mesh.sharedMesh != null) {
        Vector3 size = mesh.sharedMesh.bounds.size;
        sb.Append(", \"meshBoundsApprox\": [").Append(Num(size.x)).Append(", ")
          .Append(Num(size.y)).Append(", ").Append(Num(size.z)).Append(']');
      }
    } catch (Exception) {
    }

    sb.Append('}');
  }

  static string GameVersion() {
    // The game's Version type is not public on every build, so ask by reflection and
    // settle for "unknown" rather than losing the dump over a label.
    try {
      foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
        Type t = asm.GetType("Version");
        if (t == null) continue;
        var m = t.GetMethod("GetVersionString",
                            System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Static);
        if (m == null) continue;
        object v = m.GetParameters().Length == 0
            ? m.Invoke(null, null)
            : m.Invoke(null, new object[] { false });
        if (v != null) return v.ToString();
      }
    } catch (Exception) {
    }
    return "unknown";
  }

  static string Num(float f) {
    return f.ToString("R", CultureInfo.InvariantCulture);
  }

  static string Quote(string s) {
    var sb = new StringBuilder(s.Length + 2);
    sb.Append('"');
    foreach (char c in s) {
      if (c == '"' || c == '\\') sb.Append('\\').Append(c);
      else if (c < ' ') sb.Append(' ');
      else sb.Append(c);
    }
    return sb.Append('"').ToString();
  }
}

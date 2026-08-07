namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>One parsed piece line. Plain floats, no Unity types — see the file note
/// on <see cref="BlueprintFile"/> for why that restriction is load-bearing.</summary>
public sealed class BpPiece {
  public string Prefab;
  public string Category;
  public float PosX, PosY, PosZ;
  public float RotX, RotY, RotZ, RotW;
  public string Info = "";
  /// <summary>True when the line carried a non-unit scale. Vanilla pieces do not honor
  /// scale, so building this piece unscaled would produce a silently wrong building —
  /// the builder skips it and check reports the count instead.</summary>
  public bool ScaleRejected;
}

/// <summary>Parser for the PlanBuild community .blueprint format.
///
/// Unity-free by construction: no UnityEngine, BepInEx, or game types may appear here.
/// This file compiles into the mod (net48) and is source-linked into the host-run
/// ComfyNetworkSense.Tests project (net8.0), which is what keeps parser changes provable
/// in seconds without a game install. The builder converts to Vector3/Quaternion at its
/// own call site.
///
/// The format, confirmed against PlanBuild's PieceEntry source and its checked-in test
/// blueprints (PlanBuildTest/resources): header lines "#Key:Value"; bare "#Key" lines
/// open a section — "#Pieces" holds the piece lines, "#SnapPoints" and "#Terrain" hold
/// data this lab deliberately ignores (we do not shape terrain), and anything else
/// ("#Description" in the wild) is free text running to the next "#" line. Piece lines
/// are semicolon-separated:
///   prefab;category;posX;posY;posZ;rotX;rotY;rotZ;rotW;additionalInfo[;scaleX;scaleY;scaleZ]
/// Floats are invariant-culture and may use scientific notation; PlanBuild itself
/// tolerates comma decimals by rewriting the whole line, which would corrupt an
/// additionalInfo containing commas, so the retry here is per-field instead.
///
/// Nothing in here throws for bad input: a bad line is recorded and skipped, because a
/// blueprint downloaded from the community is exactly the input this exists for.</summary>
public sealed class BlueprintFile {
  const int MaxRecordedErrors = 40;

  public readonly Dictionary<string, string> Headers =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
  public readonly List<BpPiece> Pieces = new List<BpPiece>();
  public int SnapPointCount;
  public int TerrainOpCount;
  public int ScaleRejectedCount;
  public int BadLineCount;

  /// <summary>Footprint of the buildable (non-scale-rejected) pieces, blueprint-local
  /// metres. MinY is what the builder subtracts so the lowest piece sits on the ground.</summary>
  public float MinX, MaxX, MinY, MaxY, MinZ, MaxZ;

  public string Name {
    get {
      string v;
      return Headers.TryGetValue("Name", out v) ? v : null;
    }
  }

  public int BuildablePieceCount { get { return Pieces.Count - ScaleRejectedCount; } }

  /// <summary>Parse blueprint text. Returns false only when no buildable piece came out
  /// the other side — partial success is success, with the damage itemized in
  /// <paramref name="errors"/> (capped, with a final "and N more" line).</summary>
  public static bool TryParse(IEnumerable<string> lines, out BlueprintFile bp,
                              out List<string> errors) {
    bp = new BlueprintFile();
    errors = new List<string>();

    // What the current line belongs to. Header lines with a colon are recorded from any
    // mode; a bare "#Key" switches mode. Before any section marker, stray data is an
    // error — after one, it belongs to that section.
    const int ModeTop = 0, ModePieces = 1, ModeSnap = 2, ModeTerrain = 3, ModeText = 4;
    int mode = ModeTop;
    string textKey = null;

    bool any = false;
    float minX = 0, maxX = 0, minY = 0, maxY = 0, minZ = 0, maxZ = 0;

    int lineNo = 0;
    foreach (string raw in lines ?? new string[0]) {
      lineNo++;
      if (raw == null) continue;
      string line = raw.TrimEnd('\r', '\n', ' ', '\t');
      if (lineNo == 1 && line.Length > 0 && line[0] == '﻿') line = line.Substring(1);
      if (line.Length == 0) continue;

      if (line[0] == '#') {
        int colon = line.IndexOf(':');
        if (colon > 1) {
          string key = line.Substring(1, colon - 1).Trim();
          string value = line.Substring(colon + 1).Trim();
          if (key.Length > 0) bp.Headers[key] = value;
          continue;
        }
        string section = line.Substring(1).Trim();
        if (string.Equals(section, "Pieces", StringComparison.OrdinalIgnoreCase)) {
          mode = ModePieces;
        } else if (string.Equals(section, "SnapPoints", StringComparison.OrdinalIgnoreCase)) {
          mode = ModeSnap;
        } else if (string.Equals(section, "Terrain", StringComparison.OrdinalIgnoreCase)) {
          mode = ModeTerrain;
        } else {
          // "#Description" and friends: free text until the next # line. Kept, not
          // dropped — check prints the name and description back at the operator.
          mode = ModeText;
          textKey = section.Length > 0 ? section : "Text";
          if (!bp.Headers.ContainsKey(textKey)) bp.Headers[textKey] = "";
        }
        continue;
      }

      switch (mode) {
        case ModePieces: {
          BpPiece piece = ParsePieceLine(line, lineNo, bp, errors);
          if (piece == null) break;
          bp.Pieces.Add(piece);
          if (piece.ScaleRejected) {
            bp.ScaleRejectedCount++;
            break;
          }
          if (!any) {
            any = true;
            minX = maxX = piece.PosX;
            minY = maxY = piece.PosY;
            minZ = maxZ = piece.PosZ;
          } else {
            if (piece.PosX < minX) minX = piece.PosX;
            if (piece.PosX > maxX) maxX = piece.PosX;
            if (piece.PosY < minY) minY = piece.PosY;
            if (piece.PosY > maxY) maxY = piece.PosY;
            if (piece.PosZ < minZ) minZ = piece.PosZ;
            if (piece.PosZ > maxZ) maxZ = piece.PosZ;
          }
          break;
        }
        case ModeSnap:
          bp.SnapPointCount++;
          break;
        case ModeTerrain:
          bp.TerrainOpCount++;
          break;
        case ModeText:
          bp.Headers[textKey] = bp.Headers[textKey].Length == 0
              ? line
              : bp.Headers[textKey] + "\n" + line;
          break;
        default:
          Record(bp, errors, "line " + lineNo + ": data before any #section — skipped");
          break;
      }
    }

    bp.MinX = minX; bp.MaxX = maxX;
    bp.MinY = minY; bp.MaxY = maxY;
    bp.MinZ = minZ; bp.MaxZ = maxZ;
    if (bp.BadLineCount > MaxRecordedErrors) {
      errors.Add("…and " + (bp.BadLineCount - MaxRecordedErrors) + " more bad line(s)");
    }
    return bp.BuildablePieceCount > 0;
  }

  static BpPiece ParsePieceLine(string line, int lineNo, BlueprintFile bp,
                                List<string> errors) {
    string[] parts = line.Split(';');
    if (parts.Length < 9) {
      Record(bp, errors, "line " + lineNo + ": " + parts.Length
          + " field(s), expected 9+ — skipped");
      return null;
    }

    // additionalInfo may itself contain semicolons in hand-edited files (PlanBuild strips
    // them on export, so its own files never overflow). If the tail parses as three
    // floats it is a scale; everything between rotW and that tail is info.
    string info;
    bool hasScale;
    float sx = 1f, sy = 1f, sz = 1f;
    int n = parts.Length;
    if (n <= 9) {
      info = "";
      hasScale = false;
    } else if (n == 10) {
      info = parts[9];
      hasScale = false;
    } else if (n >= 13
               && TryFloat(parts[n - 3], out sx)
               && TryFloat(parts[n - 2], out sy)
               && TryFloat(parts[n - 1], out sz)) {
      info = string.Join(";", parts, 9, n - 12);
      hasScale = true;
    } else {
      // 11–12 fields, or a 13+ line whose tail is not numeric: everything past rotW is
      // info that contained semicolons. Scale absent.
      info = string.Join(";", parts, 9, n - 9);
      hasScale = false;
      sx = sy = sz = 1f;
    }

    var piece = new BpPiece();
    piece.Prefab = parts[0].Trim();
    piece.Category = parts[1].Trim();
    if (piece.Prefab.Length == 0) {
      Record(bp, errors, "line " + lineNo + ": empty prefab name — skipped");
      return null;
    }
    if (!TryFloat(parts[2], out piece.PosX) || !TryFloat(parts[3], out piece.PosY)
        || !TryFloat(parts[4], out piece.PosZ) || !TryFloat(parts[5], out piece.RotX)
        || !TryFloat(parts[6], out piece.RotY) || !TryFloat(parts[7], out piece.RotZ)
        || !TryFloat(parts[8], out piece.RotW)) {
      Record(bp, errors, "line " + lineNo + ": unparseable position/rotation — skipped");
      return null;
    }
    piece.Info = CleanInfo(info);
    if (hasScale
        && (Math.Abs(sx - 1f) > 0.01f || Math.Abs(sy - 1f) > 0.01f
            || Math.Abs(sz - 1f) > 0.01f)) {
      piece.ScaleRejected = true;
    }
    return piece;
  }

  /// <summary>PlanBuild JSON-serializes additionalInfo, so an empty one arrives as
  /// nothing, as two literal quotes, or as the word null; a real one arrives wrapped in
  /// quotes. Unwrap to the text a human typed.</summary>
  static string CleanInfo(string info) {
    if (string.IsNullOrEmpty(info)) return "";
    string t = info.Trim();
    if (t == "\"\"" || t == "null") return "";
    if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"') {
      t = t.Substring(1, t.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
    return t;
  }

  /// <summary>Invariant parse with scientific notation, then a per-field comma-decimal
  /// retry. Per-field, not per-line: PlanBuild's own line-wide comma rewrite would eat
  /// commas inside additionalInfo.</summary>
  static bool TryFloat(string s, out float value) {
    if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) {
      return true;
    }
    if (s != null && s.IndexOf(',') >= 0) {
      return float.TryParse(s.Replace(',', '.'), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out value);
    }
    return false;
  }

  static void Record(BlueprintFile bp, List<string> errors, string message) {
    bp.BadLineCount++;
    if (errors.Count < MaxRecordedErrors) errors.Add(message);
  }
}

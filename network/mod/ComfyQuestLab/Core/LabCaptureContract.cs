namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

/// <summary>One portable, Unity-free description of a captured world piece.</summary>
[DataContract]
public sealed class LabCapturePiece {
  [DataMember(Name = "Prefab", Order = 1)] public string Prefab;
  [DataMember(Name = "Category", Order = 2)] public string Category;
  [DataMember(Name = "X", Order = 3)] public float X;
  [DataMember(Name = "Y", Order = 4)] public float Y;
  [DataMember(Name = "Z", Order = 5)] public float Z;
  [DataMember(Name = "Qx", Order = 6)] public float Qx;
  [DataMember(Name = "Qy", Order = 7)] public float Qy;
  [DataMember(Name = "Qz", Order = 8)] public float Qz;
  [DataMember(Name = "Qw", Order = 9)] public float Qw;
  [DataMember(Name = "HasSignText", Order = 10)] public bool HasSignText;
  [DataMember(Name = "SignText", Order = 11)] public string SignText;
  [DataMember(Name = "HasItemStand", Order = 12)] public bool HasItemStand;
  [DataMember(Name = "ItemPrefab", Order = 13)] public string ItemPrefab;
  [DataMember(Name = "ItemVariant", Order = 14)] public int ItemVariant;
  [DataMember(Name = "ItemQuality", Order = 15)] public int ItemQuality;
  [DataMember(Name = "ItemType", Order = 16)] public int ItemType;
  [DataMember(Name = "RuneSchool", Order = 17)] public string RuneSchool;
  [DataMember(Name = "RuneStyle", Order = 18)] public string RuneStyle;
  [DataMember(Name = "TextGlowSchool", Order = 19)] public string TextGlowSchool;
}

/// <summary>Deterministic sidecar for a PlanBuild projection.

/// Coordinates are translated so the minimum X/Y/Z is zero and pieces are sorted by
/// their complete portable state. The same selection therefore produces the same bytes
/// regardless of where it stands in a world. The companion .blueprint is deliberately a
/// lowest-common-denominator PlanBuild file; this sidecar carries metadata PlanBuild
/// cannot represent portably.</summary>
[DataContract]
public sealed class LabCaptureArtifact {
  [DataMember(Name = "Schema", Order = 1)] public string Schema;
  [DataMember(Name = "Name", Order = 2)] public string Name;
  [DataMember(Name = "Selection", Order = 3)] public string Selection;
  [DataMember(Name = "RadiusMetres", Order = 4)] public float RadiusMetres;
  [DataMember(Name = "PieceCount", Order = 5)] public int PieceCount;
  [DataMember(Name = "PiecesSha256", Order = 6)] public string PiecesSha256;
  [DataMember(Name = "Pieces", Order = 7)]
  public LabCapturePiece[] Pieces = new LabCapturePiece[0];
}

public sealed class LabCaptureDiff {
  public int ExpectedCount;
  public int ActualCount;
  public int MissingCount;
  public int ExtraCount;
  public readonly List<string> Examples = new List<string>();
  public bool Equal { get { return MissingCount == 0 && ExtraCount == 0; } }
}

/// <summary>Validation, serialization, PlanBuild projection, and structural diff for
/// capture artifacts. Unity-free so the executable host test suite drives the exact
/// contract shipped in the plugin.</summary>
public static class LabCaptureContract {
  public const string Schema = "comfy-questlab-capture/v1";
  public const int MaxPieces = 2048;
  public const float MinRadius = 1f;
  public const float MaxRadius = 40f;

  static readonly DataContractJsonSerializer Serializer =
      new DataContractJsonSerializer(typeof(LabCaptureArtifact));

  public static LabCaptureArtifact Create(string name, string selection, float radius,
                                          IEnumerable<LabCapturePiece> pieces) {
    var artifact = new LabCaptureArtifact {
      Schema = Schema,
      Name = CanonicalName(name),
      Selection = CanonicalSelection(selection),
      RadiusMetres = Round(radius, 3),
      Pieces = NormalizeAndSort(pieces).ToArray(),
    };
    artifact.PieceCount = artifact.Pieces.Length;
    artifact.PiecesSha256 = ComputePiecesSha256(artifact.Pieces);
    string error;
    if (!TryValidate(artifact, out error)) {
      throw new InvalidDataException(error);
    }
    return artifact;
  }

  public static string Serialize(LabCaptureArtifact artifact) {
    string error;
    if (!TryValidate(artifact, out error)) {
      throw new InvalidDataException(error);
    }
    using (var stream = new MemoryStream()) {
      Serializer.WriteObject(stream, artifact);
      return Encoding.UTF8.GetString(stream.ToArray());
    }
  }

  public static LabCaptureArtifact Deserialize(string json) {
    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty))) {
      return Serializer.ReadObject(stream) as LabCaptureArtifact;
    }
  }

  public static bool TryValidate(LabCaptureArtifact artifact, out string error) {
    error = null;
    if (artifact == null) return Fail("capture is null", out error);
    if (!string.Equals(artifact.Schema, Schema, StringComparison.Ordinal)) {
      return Fail("unsupported capture schema: " + (artifact.Schema ?? "<missing>"), out error);
    }
    if (string.IsNullOrEmpty(artifact.Name)
        || CanonicalName(artifact.Name) != (artifact.Name ?? string.Empty)) {
      return Fail("capture name must use 1-64 lowercase letters, digits, '-' or '_'", out error);
    }
    if (artifact.Selection != "mine" && artifact.Selection != "lab") {
      return Fail("selection must be 'mine' or 'lab'", out error);
    }
    if (!Finite(artifact.RadiusMetres) || artifact.RadiusMetres < MinRadius
        || artifact.RadiusMetres > MaxRadius) {
      return Fail("radius must be between 1 and 40 metres", out error);
    }
    LabCapturePiece[] pieces = artifact.Pieces ?? new LabCapturePiece[0];
    if (pieces.Length == 0 || pieces.Length > MaxPieces) {
      return Fail("capture must contain 1-" + MaxPieces + " pieces", out error);
    }
    if (artifact.PieceCount != pieces.Length) {
      return Fail("PieceCount does not match Pieces", out error);
    }
    for (int i = 0; i < pieces.Length; i++) {
      LabCapturePiece p = pieces[i];
      if (p == null) return Fail("piece " + i + " is null", out error);
      if (!SafeToken(p.Prefab, 128)) return Fail("piece " + i + " has an unsafe prefab", out error);
      if (!SafeToken(p.Category, 64)) return Fail("piece " + i + " has an unsafe category", out error);
      if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z)
          || !Finite(p.Qx) || !Finite(p.Qy) || !Finite(p.Qz) || !Finite(p.Qw)) {
        return Fail("piece " + i + " has a non-finite transform", out error);
      }
      float maxSpan = MaxRadius * 2f + 0.1f;
      if (p.X < 0f || p.Y < 0f || p.Z < 0f
          || p.X > maxSpan || p.Y > maxSpan || p.Z > maxSpan) {
        return Fail("piece " + i + " lies outside the bounded capture span", out error);
      }
      double norm = p.Qx * p.Qx + p.Qy * p.Qy + p.Qz * p.Qz + p.Qw * p.Qw;
      if (norm < 0.90 || norm > 1.10) {
        return Fail("piece " + i + " has a non-unit rotation", out error);
      }
      if ((p.SignText ?? string.Empty).Length > 1024) {
        return Fail("piece " + i + " sign text exceeds 1024 characters", out error);
      }
      if (!SafeOptionalToken(p.ItemPrefab, 128)
          || !SafeOptionalToken(p.RuneSchool, 32)
          || !SafeOptionalToken(p.RuneStyle, 32)
          || !SafeOptionalToken(p.TextGlowSchool, 32)) {
        return Fail("piece " + i + " has unsafe metadata", out error);
      }
      if (!p.HasItemStand && !string.IsNullOrEmpty(p.ItemPrefab)) {
        return Fail("piece " + i + " carries an item without HasItemStand", out error);
      }
      if (p.ItemVariant < 0 || p.ItemVariant > 255 || p.ItemQuality < 0
          || p.ItemQuality > 100 || p.ItemType < 0 || p.ItemType > 255) {
        return Fail("piece " + i + " has out-of-range item-stand metadata", out error);
      }
    }
    List<LabCapturePiece> canonical = NormalizeAndSort(pieces);
    for (int i = 0; i < pieces.Length; i++) {
      if (!string.Equals(Signature(pieces[i]), Signature(canonical[i]), StringComparison.Ordinal)) {
        return Fail("pieces are not in deterministic normalized order", out error);
      }
    }
    string hash = ComputePiecesSha256(pieces);
    if (!string.Equals(hash, artifact.PiecesSha256, StringComparison.OrdinalIgnoreCase)) {
      return Fail("PiecesSha256 does not match the captured records", out error);
    }
    return true;
  }

  public static string ToBlueprintText(LabCaptureArtifact artifact) {
    string error;
    if (!TryValidate(artifact, out error)) throw new InvalidDataException(error);
    var sb = new StringBuilder();
    sb.Append("#Name:").Append(artifact.Name).Append('\n');
    sb.Append("#Creator:ComfyQuestLab capture\n");
    sb.Append("#Description:Deterministic projection of ").Append(artifact.PiecesSha256)
      .Append("; metadata is in the .capture.json sidecar.\n");
    sb.Append("#Pieces\n");
    foreach (LabCapturePiece p in artifact.Pieces) {
      sb.Append(p.Prefab).Append(';').Append(p.Category).Append(';')
        .Append(Number(p.X, 4)).Append(';').Append(Number(p.Y, 4)).Append(';')
        .Append(Number(p.Z, 4)).Append(';').Append(Number(p.Qx, 6)).Append(';')
        .Append(Number(p.Qy, 6)).Append(';').Append(Number(p.Qz, 6)).Append(';')
        .Append(Number(p.Qw, 6)).Append(';').Append('\n');
    }
    return sb.ToString();
  }

  public static bool BlueprintMatches(LabCaptureArtifact artifact, BlueprintFile blueprint,
                                      out string error) {
    error = null;
    if (blueprint == null) return Fail("blueprint projection is missing", out error);
    if (blueprint.BuildablePieceCount != artifact.PieceCount) {
      return Fail("blueprint has " + blueprint.BuildablePieceCount + " pieces; sidecar has "
          + artifact.PieceCount, out error);
    }
    int at = 0;
    foreach (BpPiece bp in blueprint.Pieces) {
      if (bp.ScaleRejected) continue;
      LabCapturePiece p = artifact.Pieces[at++];
      if (p.Prefab != bp.Prefab || p.Category != bp.Category
          || Math.Abs(p.X - bp.PosX) > 0.0002f || Math.Abs(p.Y - bp.PosY) > 0.0002f
          || Math.Abs(p.Z - bp.PosZ) > 0.0002f || Math.Abs(p.Qx - bp.RotX) > 0.000002f
          || Math.Abs(p.Qy - bp.RotY) > 0.000002f || Math.Abs(p.Qz - bp.RotZ) > 0.000002f
          || Math.Abs(p.Qw - bp.RotW) > 0.000002f) {
        return Fail("blueprint projection differs from sidecar at piece " + (at - 1), out error);
      }
    }
    return true;
  }

  public static LabCaptureDiff Diff(IEnumerable<LabCapturePiece> expected,
                                    IEnumerable<LabCapturePiece> actual) {
    List<LabCapturePiece> left = NormalizeAndSort(expected);
    List<LabCapturePiece> right = NormalizeAndSort(actual);
    var result = new LabCaptureDiff { ExpectedCount = left.Count, ActualCount = right.Count };
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (LabCapturePiece p in left) {
      string key = Signature(p); int n; counts.TryGetValue(key, out n); counts[key] = n + 1;
    }
    foreach (LabCapturePiece p in right) {
      string key = Signature(p); int n;
      if (counts.TryGetValue(key, out n) && n > 0) counts[key] = n - 1;
      else { result.ExtraCount++; AddExample(result, "+ " + Summary(p)); }
    }
    var rightCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (LabCapturePiece p in right) {
      string key = Signature(p); int n; rightCounts.TryGetValue(key, out n); rightCounts[key] = n + 1;
    }
    foreach (LabCapturePiece p in left) {
      string key = Signature(p); int n;
      if (rightCounts.TryGetValue(key, out n) && n > 0) rightCounts[key] = n - 1;
      else { result.MissingCount++; AddExample(result, "- " + Summary(p)); }
    }
    return result;
  }

  public static List<LabCapturePiece> NormalizeAndSort(IEnumerable<LabCapturePiece> source) {
    var pieces = (source ?? new LabCapturePiece[0]).Where(p => p != null).Select(Clone).ToList();
    if (pieces.Count == 0) return pieces;
    float minX = pieces.Min(p => p.X), minY = pieces.Min(p => p.Y), minZ = pieces.Min(p => p.Z);
    foreach (LabCapturePiece p in pieces) {
      p.X = Round(p.X - minX, 4); p.Y = Round(p.Y - minY, 4); p.Z = Round(p.Z - minZ, 4);
      CanonicalQuaternion(p);
      p.Category = string.IsNullOrEmpty(p.Category) ? "Building" : p.Category;
      p.SignText = p.SignText ?? string.Empty;
      p.ItemPrefab = p.ItemPrefab ?? string.Empty;
      p.RuneSchool = p.RuneSchool ?? string.Empty;
      p.RuneStyle = p.RuneStyle ?? string.Empty;
      p.TextGlowSchool = p.TextGlowSchool ?? string.Empty;
    }
    pieces.Sort((a, b) => string.CompareOrdinal(Signature(a), Signature(b)));
    return pieces;
  }

  public static string ComputePiecesSha256(IEnumerable<LabCapturePiece> pieces) {
    string canonical = string.Join("\n", (pieces ?? new LabCapturePiece[0]).Select(Signature));
    using (SHA256 sha = SHA256.Create()) {
      byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
      var sb = new StringBuilder(hash.Length * 2);
      foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
      return sb.ToString();
    }
  }

  public static string CanonicalName(string name) {
    string value = (name ?? string.Empty).Trim().ToLowerInvariant();
    if (value.Length == 0 || value.Length > 64) return string.Empty;
    foreach (char c in value) {
      if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '_') {
        return string.Empty;
      }
    }
    return value;
  }

  static string CanonicalSelection(string selection) {
    string value = (selection ?? "mine").Trim().ToLowerInvariant();
    return value == "lab" ? "lab" : value == "mine" ? "mine" : value;
  }

  static LabCapturePiece Clone(LabCapturePiece p) {
    return new LabCapturePiece {
      Prefab = p.Prefab, Category = p.Category, X = p.X, Y = p.Y, Z = p.Z,
      Qx = p.Qx, Qy = p.Qy, Qz = p.Qz, Qw = p.Qw,
      HasSignText = p.HasSignText, SignText = p.SignText,
      HasItemStand = p.HasItemStand, ItemPrefab = p.ItemPrefab,
      ItemVariant = p.ItemVariant, ItemQuality = p.ItemQuality, ItemType = p.ItemType,
      RuneSchool = p.RuneSchool, RuneStyle = p.RuneStyle,
      TextGlowSchool = p.TextGlowSchool,
    };
  }

  static void CanonicalQuaternion(LabCapturePiece p) {
    double n = Math.Sqrt(p.Qx * p.Qx + p.Qy * p.Qy + p.Qz * p.Qz + p.Qw * p.Qw);
    if (n < 0.000001) { p.Qx = p.Qy = p.Qz = 0f; p.Qw = 1f; return; }
    p.Qx = (float)(p.Qx / n); p.Qy = (float)(p.Qy / n);
    p.Qz = (float)(p.Qz / n); p.Qw = (float)(p.Qw / n);
    bool flip = p.Qw < 0f || (p.Qw == 0f && (p.Qz < 0f
        || (p.Qz == 0f && (p.Qy < 0f || (p.Qy == 0f && p.Qx < 0f)))));
    if (flip) { p.Qx = -p.Qx; p.Qy = -p.Qy; p.Qz = -p.Qz; p.Qw = -p.Qw; }
    p.Qx = Round(p.Qx, 6); p.Qy = Round(p.Qy, 6);
    p.Qz = Round(p.Qz, 6); p.Qw = Round(p.Qw, 6);
  }

  static string Signature(LabCapturePiece p) {
    return string.Join("\t", new[] {
      p.Prefab ?? "", p.Category ?? "", Number(p.X, 4), Number(p.Y, 4), Number(p.Z, 4),
      Number(p.Qx, 6), Number(p.Qy, 6), Number(p.Qz, 6), Number(p.Qw, 6),
      p.HasSignText ? "1" : "0", Escape(p.SignText), p.HasItemStand ? "1" : "0",
      p.ItemPrefab ?? "", p.ItemVariant.ToString(CultureInfo.InvariantCulture),
      p.ItemQuality.ToString(CultureInfo.InvariantCulture),
      p.ItemType.ToString(CultureInfo.InvariantCulture), p.RuneSchool ?? "",
      p.RuneStyle ?? "", p.TextGlowSchool ?? "",
    });
  }

  static string Summary(LabCapturePiece p) {
    return (p.Prefab ?? "?") + " @ " + Number(p.X, 2) + "," + Number(p.Y, 2)
        + "," + Number(p.Z, 2);
  }

  static string Escape(string value) {
    return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\t", "\\t")
        .Replace("\r", "\\r").Replace("\n", "\\n");
  }

  static bool SafeToken(string value, int max) {
    if (string.IsNullOrEmpty(value) || value.Length > max) return false;
    foreach (char c in value) if (c == ';' || c == '\r' || c == '\n' || char.IsControl(c)) return false;
    return true;
  }

  static bool SafeOptionalToken(string value, int max) {
    return string.IsNullOrEmpty(value) || SafeToken(value, max);
  }

  static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

  static float Round(float value, int digits) {
    float rounded = (float)Math.Round(value, digits, MidpointRounding.AwayFromZero);
    return rounded == 0f ? 0f : rounded;
  }

  static string Number(float value, int digits) {
    string format = "0." + new string('#', digits);
    return (value == 0f ? 0f : value).ToString(format, CultureInfo.InvariantCulture);
  }

  static void AddExample(LabCaptureDiff result, string value) {
    if (result.Examples.Count < 8) result.Examples.Add(value);
  }

  static bool Fail(string message, out string error) { error = message; return false; }
}

namespace ComfyQuestLab;

using System.Collections.Generic;

using UnityEngine;

/// <summary>One rune per category, drawn rather than shipped.
///
/// A spellbook wants sigils, not labels. These are Elder Futhark, chosen because the
/// meanings are genuinely apt rather than decorative — Jera is literally the harvest
/// rune, Fehu is property, Othala is the homestead, Ansuz is speech. Somebody who knows
/// the alphabet gets a free mnemonic; somebody who does not still gets eight shapes they
/// can tell apart at a glance, which is the actual job.
///
/// Drawn procedurally from line segments because runes ARE line segments. No PNGs to
/// ship, no atlas to keep in sync with a game update, no font that might not have the
/// glyph — and the source stays readable, since a rune is three lines of coordinates
/// rather than a base64 blob.
///
/// The same texture is used for the spellbook tab and for the console's category filter.
/// That is the point: one mark means one category everywhere, so learning the book
/// teaches you the console for free.</summary>
public static class LabRunes {
  const int Size = 14;

  struct Seg {
    public float X1, Y1, X2, Y2;
    public Seg(float x1, float y1, float x2, float y2) { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; }
  }

  /// <summary>Strokes in a 0..1 box, y measured downward. Kept deliberately chunky:
  /// these render at about 18 pixels and fine detail does not survive that.</summary>
  static readonly Dictionary<string, Seg[]> _strokes = new Dictionary<string, Seg[]> {
    // Tiwaz — the battle rune. Stave with an arrowhead.
    { LabCategory.Combat, new[] {
        new Seg(0.5f, 0.10f, 0.5f, 0.95f),
        new Seg(0.5f, 0.10f, 0.15f, 0.42f),
        new Seg(0.5f, 0.10f, 0.85f, 0.42f) } },

    // Jera — the harvest rune. Two opposed hooks.
    { LabCategory.Harvest, new[] {
        new Seg(0.12f, 0.20f, 0.50f, 0.42f),
        new Seg(0.50f, 0.42f, 0.12f, 0.62f),
        new Seg(0.88f, 0.80f, 0.50f, 0.58f),
        new Seg(0.50f, 0.58f, 0.88f, 0.38f) } },

    // Fehu — cattle, property, the things you carry.
    { LabCategory.Inventory, new[] {
        new Seg(0.28f, 0.05f, 0.28f, 0.95f),
        new Seg(0.28f, 0.28f, 0.80f, 0.08f),
        new Seg(0.28f, 0.55f, 0.80f, 0.35f) } },

    // Othala — the homestead. Diamond over two legs.
    { LabCategory.Building, new[] {
        new Seg(0.50f, 0.08f, 0.18f, 0.38f),
        new Seg(0.50f, 0.08f, 0.82f, 0.38f),
        new Seg(0.18f, 0.38f, 0.50f, 0.58f),
        new Seg(0.82f, 0.38f, 0.50f, 0.58f),
        new Seg(0.32f, 0.50f, 0.12f, 0.95f),
        new Seg(0.68f, 0.50f, 0.88f, 0.95f) } },

    // Kaunan — the torch, and the forge.
    { LabCategory.Crafting, new[] {
        new Seg(0.78f, 0.08f, 0.25f, 0.50f),
        new Seg(0.25f, 0.50f, 0.78f, 0.92f) } },

    // Sowilo — the sun. Achievement, and things going up.
    { LabCategory.Progression, new[] {
        new Seg(0.80f, 0.10f, 0.32f, 0.10f),
        new Seg(0.32f, 0.10f, 0.68f, 0.50f),
        new Seg(0.68f, 0.50f, 0.20f, 0.50f),
        new Seg(0.20f, 0.50f, 0.56f, 0.90f) } },

    // Raido — the journey.
    { LabCategory.World, new[] {
        new Seg(0.25f, 0.05f, 0.25f, 0.95f),
        new Seg(0.25f, 0.05f, 0.75f, 0.22f),
        new Seg(0.75f, 0.22f, 0.25f, 0.48f),
        new Seg(0.35f, 0.48f, 0.80f, 0.95f) } },

    // Mannaz — man, and by extension the community.
    //
    // Ansuz is the speech rune and would have been the apter choice, but Ansuz and Fehu
    // are the same shape with the arms at a different angle, and at eighteen pixels that
    // is no shape at all. Legible beats faithful: two marks a reader cannot tell apart
    // are worse than one slightly loose meaning, and Mannaz still lands on "people".
    { LabCategory.Social, new[] {
        new Seg(0.15f, 0.08f, 0.15f, 0.92f),
        new Seg(0.85f, 0.08f, 0.85f, 0.92f),
        new Seg(0.15f, 0.12f, 0.85f, 0.58f),
        new Seg(0.85f, 0.12f, 0.15f, 0.58f) } },
  };

  /// <summary>Each category also gets a colour, because eight line drawings at 18 pixels
  /// are easier to tell apart when they are not all the same hue.</summary>
  static readonly Dictionary<string, Color> _colors = new Dictionary<string, Color> {
    { LabCategory.Combat,      new Color(0.93f, 0.45f, 0.40f) },   // rust
    { LabCategory.Harvest,     new Color(0.48f, 0.80f, 0.52f) },   // green
    { LabCategory.Inventory,   new Color(0.85f, 0.72f, 0.42f) },   // amber
    { LabCategory.Building,    new Color(0.78f, 0.60f, 0.38f) },   // timber
    { LabCategory.Crafting,    new Color(0.92f, 0.62f, 0.30f) },   // forge
    { LabCategory.Progression, new Color(0.96f, 0.84f, 0.42f) },   // sun
    { LabCategory.World,       new Color(0.46f, 0.76f, 0.92f) },   // sky
    { LabCategory.Social,      new Color(0.76f, 0.66f, 0.94f) },   // violet
  };

  static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

  /// <summary>The rune for a category, built once and kept. Safe to call every frame
  /// from OnGUI.</summary>
  public static Texture2D For(string category) {
    Texture2D cached;
    if (_cache.TryGetValue(category, out cached) && cached != null) {
      return cached;
    }

    Color color;
    if (!_colors.TryGetValue(category, out color)) {
      color = Color.white;
    }
    Seg[] strokes;
    if (!_strokes.TryGetValue(category, out strokes)) {
      strokes = new Seg[0];
    }

    var pixels = new Color[Size * Size];
    for (int i = 0; i < pixels.Length; i++) {
      pixels[i] = new Color(0f, 0f, 0f, 0f);
    }
    foreach (Seg seg in strokes) {
      DrawLine(pixels, seg, color);
    }

    var texture = new Texture2D(Size, Size, TextureFormat.ARGB32, false);
    texture.hideFlags = HideFlags.HideAndDontSave;
    texture.filterMode = FilterMode.Point;   // crisp at small sizes; no blur
    texture.wrapMode = TextureWrapMode.Clamp;
    texture.SetPixels(pixels);
    texture.Apply();

    _cache[category] = texture;
    return texture;
  }

  public static Color ColorFor(string category) {
    Color color;
    return _colors.TryGetValue(category, out color) ? color : Color.white;
  }

  /// <summary>Rasterise one stroke. Sampled along the segment rather than Bresenham'd
  /// because a rune needs some weight to read at this size, and plotting a 2x2 block per
  /// sample is both simpler and heavier than a one-pixel line.</summary>
  static void DrawLine(Color[] pixels, Seg seg, Color color) {
    float x1 = seg.X1 * (Size - 1), y1 = seg.Y1 * (Size - 1);
    float x2 = seg.X2 * (Size - 1), y2 = seg.Y2 * (Size - 1);
    float dx = x2 - x1, dy = y2 - y1;
    int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) * 3f) + 1;

    for (int i = 0; i <= steps; i++) {
      float t = (float) i / steps;
      int px = Mathf.RoundToInt(x1 + dx * t);
      int py = Mathf.RoundToInt(y1 + dy * t);
      Plot(pixels, px, py, color);
      Plot(pixels, px + 1, py, color);
      Plot(pixels, px, py + 1, color);
    }
  }

  static void Plot(Color[] pixels, int x, int y, Color color) {
    if (x < 0 || y < 0 || x >= Size || y >= Size) {
      return;
    }
    // Texture rows run bottom-up; the stroke coordinates read top-down.
    pixels[(Size - 1 - y) * Size + x] = color;
  }
}

namespace ComfyQuestLab;

using System;

/// <summary>Unity-free panel geometry policy.
///
/// The visible panel uses logical (pre-zoom) coordinates. Keeping the arithmetic here makes
/// low-resolution and high-zoom recovery executable without launching Valheim, and prevents a
/// persisted desktop-sized window from becoming unreachable after a resolution change.</summary>
public static class LabPanelLayout {
  public const float DefaultX = 80f;
  public const float DefaultY = 90f;
  public const float DefaultWidth = 900f;
  public const float DefaultHeight = 620f;
  public const float PreferredMinimumWidth = 700f;
  public const float PreferredMinimumHeight = 440f;
  public const float MinimumScale = 0.65f;
  public const float MaximumScale = 2f;
  public const float ViewportGutter = 24f;

  public static LabPanelBounds Clamp(
      float x,
      float y,
      float width,
      float height,
      float scale,
      float viewportWidth,
      float viewportHeight) {
    scale = Finite(scale) ? Between(scale, MinimumScale, MaximumScale) : 1f;
    float logicalWidth = Math.Max(1f, Finite(viewportWidth) ? viewportWidth / scale : 1f);
    float logicalHeight = Math.Max(1f, Finite(viewportHeight) ? viewportHeight / scale : 1f);
    float maxWidth = Math.Max(1f, logicalWidth - ViewportGutter);
    float maxHeight = Math.Max(1f, logicalHeight - ViewportGutter);
    float minWidth = Math.Min(PreferredMinimumWidth, maxWidth);
    float minHeight = Math.Min(PreferredMinimumHeight, maxHeight);

    x = Finite(x) ? x : DefaultX;
    y = Finite(y) ? y : DefaultY;
    width = Finite(width) ? width : DefaultWidth;
    height = Finite(height) ? height : DefaultHeight;

    width = Between(width, minWidth, maxWidth);
    height = Between(height, minHeight, maxHeight);
    x = Between(x, 0f, Math.Max(0f, logicalWidth - width));
    y = Between(y, 0f, Math.Max(0f, logicalHeight - height));
    return new LabPanelBounds(x, y, width, height, minWidth, minHeight, maxWidth, maxHeight);
  }

  static bool Finite(float value) {
    return !float.IsNaN(value) && !float.IsInfinity(value);
  }

  static float Between(float value, float minimum, float maximum) {
    return Math.Max(minimum, Math.Min(maximum, value));
  }
}

public readonly struct LabPanelBounds {
  public float X { get; }
  public float Y { get; }
  public float Width { get; }
  public float Height { get; }
  public float MinimumWidth { get; }
  public float MinimumHeight { get; }
  public float MaximumWidth { get; }
  public float MaximumHeight { get; }

  public LabPanelBounds(
      float x,
      float y,
      float width,
      float height,
      float minimumWidth,
      float minimumHeight,
      float maximumWidth,
      float maximumHeight) {
    X = x;
    Y = y;
    Width = width;
    Height = height;
    MinimumWidth = minimumWidth;
    MinimumHeight = minimumHeight;
    MaximumWidth = maximumWidth;
    MaximumHeight = maximumHeight;
  }
}

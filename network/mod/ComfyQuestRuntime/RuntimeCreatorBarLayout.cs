namespace ComfyQuestRuntime;

using System;

/// <summary>Pure geometry for the always-present creator bar. Keeping this free of
/// Unity lets the exact live viewport that exposed a host-HUD collision stay in the
/// executable test suite.</summary>
public static class RuntimeCreatorBarLayout {
  public const float SafeTop = 92f;
  public const float EdgeInset = 8f;
  public const float CollapsedHeight = 116f;
  public const float ExpandedHeight = 196f;
  public static float Scale(float screenHeight) => Math.Max(1f, Math.Min(2f, screenHeight / 1080f));

  public static RuntimeCreatorBarBounds Place(float screenWidth, float screenHeight, bool expanded) {
    screenWidth = Math.Max(0f, screenWidth);
    screenHeight = Math.Max(0f, screenHeight);

    float scale = Scale(screenHeight);
    float width = Math.Min(Math.Max(720f, screenWidth - 400f * scale), 1100f * scale);
    width = Math.Min(width, Math.Max(0f, screenWidth - EdgeInset * 2f));
    float height = Math.Min((expanded ? ExpandedHeight : CollapsedHeight) * scale,
        Math.Max(0f, screenHeight - EdgeInset * 2f));
    float highestTopThatFits = Math.Max(EdgeInset, screenHeight - height - EdgeInset);
    float top = Math.Min(SafeTop, highestTopThatFits);

    return new RuntimeCreatorBarBounds(
        Math.Max(0f, (screenWidth - width) / 2f), top, width, height);
  }
}

public readonly struct RuntimeCreatorBarBounds {
  public RuntimeCreatorBarBounds(float x, float y, float width, float height) {
    X = x;
    Y = y;
    Width = width;
    Height = height;
  }

  public float X { get; }
  public float Y { get; }
  public float Width { get; }
  public float Height { get; }
}

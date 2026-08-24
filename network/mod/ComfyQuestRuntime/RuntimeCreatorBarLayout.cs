namespace ComfyQuestRuntime;

using System;

/// <summary>Pure geometry for the always-present creator bar. Keeping this free of
/// Unity lets the exact live viewport that exposed a host-HUD collision stay in the
/// executable test suite.</summary>
public static class RuntimeCreatorBarLayout {
  public const float SafeTop = 92f;
  public const float EdgeInset = 8f;
  public const float CollapsedHeight = 36f;
  public const float ExpandedHeight = 116f;

  public static RuntimeCreatorBarBounds Place(float screenWidth, float screenHeight, bool expanded) {
    screenWidth = Math.Max(0f, screenWidth);
    screenHeight = Math.Max(0f, screenHeight);

    float width = Math.Min(Math.Max(720f, screenWidth - 80f), 1440f);
    width = Math.Min(width, Math.Max(0f, screenWidth - EdgeInset * 2f));
    float height = expanded ? ExpandedHeight : CollapsedHeight;
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

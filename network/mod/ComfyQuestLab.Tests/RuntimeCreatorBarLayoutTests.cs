namespace ComfyNetworkSense.Tests;

using ComfyQuestRuntime;
using Xunit;

public sealed class RuntimeCreatorBarLayoutTests {
  [Theory]
  [InlineData(false, 116f)]
  [InlineData(true, 196f)]
  public void LiveViewportClearsObservedHostHudAndRemainsOnScreen(bool expanded, float expectedHeight) {
    RuntimeCreatorBarBounds bounds = RuntimeCreatorBarLayout.Place(1026f, 740f, expanded);

    Assert.Equal(92f, bounds.Y);
    Assert.True(bounds.Y > 84f, "Creator bar must clear the observed host HUD bottom.");
    Assert.Equal(expectedHeight, bounds.Height);
    Assert.True(bounds.Y + bounds.Height <= 740f - RuntimeCreatorBarLayout.EdgeInset);
    Assert.Equal(720f, bounds.Width);
    Assert.Equal(153f, bounds.X);
  }

  [Fact]
  public void SmallViewportClampsWithoutLeavingTheScreen() {
    RuntimeCreatorBarBounds bounds = RuntimeCreatorBarLayout.Place(640f, 180f, expanded: true);

    Assert.Equal(RuntimeCreatorBarLayout.EdgeInset, bounds.X);
    Assert.Equal(624f, bounds.Width);
    Assert.Equal(8f, bounds.Y);
    Assert.Equal(172f, bounds.Y + bounds.Height);
  }
}

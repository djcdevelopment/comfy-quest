namespace ComfyQuestLab.Tests;

using System.Collections.Generic;

using Xunit;

public sealed class LabGalleryOriginContractTests {
  [Fact]
  public void Empty_world_keeps_first_build_at_the_player() {
    var result = LabGalleryOriginContract.Decide(
        false, false, true, 2f, 0f, new List<LabGalleryOriginContract.Portal>());

    Assert.True(result.Succeeded);
    Assert.False(result.Found);
  }

  [Fact]
  public void Lower_complete_ascent_portal_recovers_the_absolute_origin() {
    var result = LabGalleryOriginContract.Decide(
        true, false, true, 2f, 0f, new[] {
          Portal("build-1", -31.442f, 62.32f, 16.093f),
          Portal("build-1", -32.942f, 36.12f, 16.093f),
        });

    Assert.True(result.Succeeded);
    Assert.True(result.Found);
    Assert.Equal(-34.942f, result.X, 3);
    Assert.Equal(36.12f, result.Y, 3);
    Assert.Equal(16.093f, result.Z, 3);
  }

  [Theory]
  [InlineData("missing")]
  [InlineData("partial")]
  [InlineData("multiple")]
  [InlineData("flat")]
  [InlineData("mixed")]
  public void Ambiguous_or_partial_sites_fail_before_clear(string shape) {
    bool otherProfile = shape == "mixed";
    var rows = new List<LabGalleryOriginContract.Portal>();
    if (shape != "missing") rows.Add(Portal("build-1", 2f, 10f, 0f));
    if (shape != "missing" && shape != "partial") {
      rows.Add(Portal(shape == "multiple" ? "build-2" : "build-1",
                      3.5f, shape == "flat" ? 10.2f : 20f, 0f));
    }

    var result = LabGalleryOriginContract.Decide(
        true, otherProfile, true, 2f, 0f, rows);

    Assert.False(result.Succeeded);
    Assert.False(result.Found);
    Assert.False(string.IsNullOrWhiteSpace(result.Error));
  }

  static LabGalleryOriginContract.Portal Portal(
      string build, float x, float y, float z) {
    return new LabGalleryOriginContract.Portal {
      BuildId = build,
      X = x,
      Y = y,
      Z = z,
    };
  }
}

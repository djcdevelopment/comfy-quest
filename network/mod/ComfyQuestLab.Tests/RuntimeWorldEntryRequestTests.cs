namespace ComfyNetworkSense.Tests;

using System;
using ComfyQuestContracts;
using Xunit;

public sealed class RuntimeWorldEntryRequestTests {
  static RuntimeWorldEntryRequest Request(DateTimeOffset now) => new() {
    Schema = RuntimeWorldEntryRequest.CurrentSchema,
    RequestId = "world-entry-abcd1234",
    CreatedUtc = now.ToString("o"),
    ExpiresUtc = now.AddMinutes(15).ToString("o"),
    ExpectedMachine = "OMEN",
    ExpectedWorldUid = "-7600395338659582326",
    WorldName = "ComfyQuestDemo",
    WorldDisplayName = "Comfy Quest Demo",
    CharacterProfile = "questyfour",
    CreatorSessionId = "creator-session-abcd1234",
  };

  [Fact]
  public void ExactLocalWorldRequestCarriesEveryIdentityPin() {
    DateTimeOffset now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");
    Assert.True(RuntimeWorldEntryRequestPolicy.Validate(Request(now), now, out string error), error);
    Assert.Null(error);
    Assert.True(RuntimeWorldEntryRequestPolicy.CanAddressReceipt(Request(now)));
  }

  [Theory]
  [InlineData("profile", "")]
  [InlineData("profile", "questy four")]
  [InlineData("world", "")]
  [InlineData("world", "ComfyQuestDemo;quit")]
  [InlineData("display", "")]
  [InlineData("display", "Comfy Quest Demo;quit")]
  [InlineData("uid", "0")]
  [InlineData("uid", "not-a-world")]
  public void MissingOrUnsafeTargetIdentityIsRejected(string field, string value) {
    DateTimeOffset now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");
    RuntimeWorldEntryRequest request = Request(now);
    if (field == "profile") request.CharacterProfile = value;
    if (field == "world") request.WorldName = value;
    if (field == "display") request.WorldDisplayName = value;
    if (field == "uid") request.ExpectedWorldUid = value;
    Assert.False(RuntimeWorldEntryRequestPolicy.Validate(request, now, out string error));
    Assert.Equal("world_entry_identity_invalid", error);
  }

  [Fact]
  public void ExpiredAndUnboundedRequestsAreRejected() {
    DateTimeOffset now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");
    RuntimeWorldEntryRequest expired = Request(now.AddMinutes(-20));
    expired.ExpiresUtc = now.AddMinutes(-1).ToString("o");
    Assert.False(RuntimeWorldEntryRequestPolicy.Validate(expired, now, out string expiredError));
    Assert.Equal("world_entry_request_expired", expiredError);

    RuntimeWorldEntryRequest far = Request(now);
    far.ExpiresUtc = now.AddHours(1).ToString("o");
    Assert.False(RuntimeWorldEntryRequestPolicy.Validate(far, now, out string farError));
    Assert.Equal("world_entry_expiry_too_far", farError);
  }
}

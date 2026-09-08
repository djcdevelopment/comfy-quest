using System;
using System.Linq;
using System.Text.Json;

using ComfyQuestLab;
using Xunit;

namespace ComfyNetworkSense.Tests;

public sealed class LabSignatureHuntContractTests {
  [Fact]
  public void PlanIsOneReviewedDemoWorldFixtureRatherThanSpawnAuthority() {
    Assert.Equal("slayers-signature-hunt", LabSignatureHuntContract.FixtureId);
    Assert.Equal("ComfyQuestDemo", LabSignatureHuntContract.ExpectedWorldName);
    Assert.Equal("-7600395338659582326", LabSignatureHuntContract.ExpectedWorldUid);
    Assert.Equal("CreatorOSBeta1", LabSignatureHuntContract.CreatorOsBetaWorldName);
    Assert.Equal("4257656027", LabSignatureHuntContract.CreatorOsBetaWorldUid);
    Assert.True(LabSignatureHuntContract.SupportsWorld(
        LabSignatureHuntContract.ExpectedWorldName,
        LabSignatureHuntContract.ExpectedWorldUid));
    Assert.True(LabSignatureHuntContract.SupportsWorld(
        LabSignatureHuntContract.CreatorOsBetaWorldName,
        LabSignatureHuntContract.CreatorOsBetaWorldUid));
    Assert.False(LabSignatureHuntContract.SupportsWorld("CreatorOSBeta1", "4257656028"));
    Assert.Equal(20, LabSignatureHuntContract.Placements.Length);
    Assert.Equal(LabSignatureHuntContract.Placements.Length,
        LabSignatureHuntContract.Placements.Select(value => value.Role).Distinct().Count());

    LabSignatureHuntPlacement[] targets = LabSignatureHuntContract.Placements
        .Where(value => value.Kind == LabSignatureHuntContract.TargetKind)
        .ToArray();
    Assert.Collection(targets,
        deathsquito => {
          Assert.Equal("target-deathsquito", deathsquito.Role);
          Assert.Equal("Deathsquito", deathsquito.Prefab);
          Assert.Equal("Deathsquito", deathsquito.CreatureLabel);
        },
        drake => {
          Assert.Equal("target-drake", drake.Role);
          Assert.Equal("Hatchling", drake.Prefab);
          Assert.Equal("Drake", drake.CreatureLabel);
        });

    double dx = targets[0].LocalX - targets[1].LocalX;
    double dz = targets[0].LocalZ - targets[1].LocalZ;
    Assert.Equal(LabSignatureHuntContract.ArenaSeparationMetres,
        Math.Sqrt(dx * dx + dz * dz), 3);
    Assert.Equal(5, LabSignatureHuntContract.Placements.Count(value =>
        value.Kind == LabSignatureHuntContract.MarkerKind && value.Area == "deathsquito"));
    Assert.Equal(5, LabSignatureHuntContract.Placements.Count(value =>
        value.Kind == LabSignatureHuntContract.MarkerKind && value.Area == "drake"));

    LabSignatureHuntPlacement[] loadout = LabSignatureHuntContract.Placements
        .Where(value => value.Kind == LabSignatureHuntContract.LoadoutKind)
        .ToArray();
    Assert.Equal(4, loadout.Length);
    Assert.All(loadout, value => {
      Assert.Equal("SpearCarapace", value.Prefab);
      Assert.Equal(1, value.Stack);
    });
  }

  [Fact]
  public void RequestSurfaceAcceptsOnlyThreeParameterFreeOperations() {
    Assert.Contains(LabSignatureHuntContract.PrepareOperation, LabBatchRequestPolicy.Operations);
    Assert.Contains(LabSignatureHuntContract.StatusOperation, LabBatchRequestPolicy.Operations);
    Assert.Contains(LabSignatureHuntContract.ClearOperation, LabBatchRequestPolicy.Operations);

    foreach (string operation in LabSignatureHuntContract.Operations) {
      Assert.True(LabBatchRequestPolicy.Validate(
          operation, null, null, null, null, out string accepted), accepted);
      Assert.False(LabBatchRequestPolicy.Validate(
          operation, "Deathsquito", null, null, null, out string extra));
      Assert.Equal("request_argument_not_allowed", extra);
      Assert.False(LabSignatureHuntContract.ValidateRequest(
          operation, null, null, null, null, "slayers", 0, 0, 0,
          out string corpus));
      Assert.Equal("request_argument_not_allowed", corpus);
      Assert.False(LabSignatureHuntContract.ValidateRequest(
          operation, null, null, null, null, null, 1, 0, 0,
          out string step));
      Assert.Equal("request_argument_not_allowed", step);
      Assert.False(LabSignatureHuntContract.ValidateRequest(
          operation, null, null, null, null, null, 0, 1, 0,
          out string seed));
      Assert.Equal("request_argument_not_allowed", seed);
    }

    Assert.False(LabBatchRequestPolicy.Validate(
        "signature_hunt_spawn", null, null, null, null, out string arbitrary));
    Assert.Equal("operation_not_allowlisted", arbitrary);
    Assert.DoesNotContain(LabBatchRequestPolicy.Operations,
        operation => string.Equals(operation, "spawn", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void CleanupOwnershipRequiresTheExactFixtureMark() {
    Assert.True(LabSignatureHuntContract.OwnsMark(LabSignatureHuntContract.MarkValue));
    Assert.False(LabSignatureHuntContract.OwnsMark(null));
    Assert.False(LabSignatureHuntContract.OwnsMark(string.Empty));
    Assert.False(LabSignatureHuntContract.OwnsMark("slayers-signature-hunt"));
    Assert.False(LabSignatureHuntContract.OwnsMark("slayers-signature-hunt/v2"));
    Assert.False(LabSignatureHuntContract.OwnsMark("comfyQuestLabGallery"));
  }

  [Fact]
  public void ReceiptPreservesRawLiveCharacterNamesAndMatcherTargets() {
    var receipt = new LabSignatureHuntFixtureReceipt {
      RequestId = "signature-hunt-test",
      PreparationId = "signature-hunt-prep-test",
      PreparedUtc = "2026-08-29T12:00:00.0000000+00:00",
      Machine = "test-machine",
      WorldName = LabSignatureHuntContract.ExpectedWorldName,
      WorldUid = LabSignatureHuntContract.ExpectedWorldUid,
      ExpectedObjectCount = LabSignatureHuntContract.Placements.Length,
      StandingObjectCount = LabSignatureHuntContract.Placements.Length,
      OriginX = 1f,
      OriginY = 2f,
      OriginZ = 3f,
      BindingAnchor = new LabSignatureHuntBindingEvidence {
        Role = "marker-loadout-sign", TargetKind = "sign", ZdoId = "1:9",
        X = 0f, Y = 1.2f, Z = 7f,
      },
      Targets = new[] {
        new LabSignatureHuntTargetEvidence {
          Role = "target-deathsquito", CreatureLabel = "Deathsquito",
          Prefab = "Deathsquito", GameObjectName = "Deathsquito(Clone)",
          RawMName = "$enemy_deathsquito", MatcherTarget = "$enemy_deathsquito",
          ZdoId = "1:10", X = -20f, Y = 4f, Z = 30f,
        },
        new LabSignatureHuntTargetEvidence {
          Role = "target-drake", CreatureLabel = "Drake",
          Prefab = "Hatchling", GameObjectName = "Hatchling(Clone)",
          RawMName = "$enemy_drake", MatcherTarget = "$enemy_drake",
          ZdoId = "1:11", X = 20f, Y = 6f, Z = 30f,
        },
      },
    };

    using JsonDocument parsed = JsonDocument.Parse(receipt.ToJson());
    JsonElement root = parsed.RootElement;
    Assert.Equal(LabSignatureHuntContract.ReceiptSchema,
        root.GetProperty("schema").GetString());
    Assert.Equal("fixture-preparation", root.GetProperty("proof_level").GetString());
    Assert.Contains("not live kill or completion proof",
        root.GetProperty("disclaimer").GetString(), StringComparison.OrdinalIgnoreCase);
    JsonElement anchor = root.GetProperty("binding_anchor");
    Assert.Equal("marker-loadout-sign", anchor.GetProperty("role").GetString());
    Assert.Equal("sign", anchor.GetProperty("target_kind").GetString());
    Assert.Equal("1:9", anchor.GetProperty("zdo_id").GetString());
    JsonElement[] targets = root.GetProperty("targets").EnumerateArray().ToArray();
    Assert.Equal("$enemy_deathsquito", targets[0].GetProperty("raw_m_name").GetString());
    Assert.Equal("$enemy_deathsquito", targets[0].GetProperty("matcher_target").GetString());
    Assert.Equal("$enemy_drake", targets[1].GetProperty("raw_m_name").GetString());
    Assert.Equal("Character.m_name", targets[1].GetProperty("captured_from").GetString());
  }
}

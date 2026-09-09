namespace ComfyQuestLab;

using System;
using System.Globalization;
using System.Text;

/// <summary>The reviewed, parameter-free shape of the Slayers Signature Hunt proof fixture.
///
/// This is deliberately data, not a spawn API. The mailbox can ask for prepare, status, or
/// clear, but it cannot choose a prefab, count, position, item, world, or ownership mark. The
/// Unity provider consumes this exact plan and records the live <c>Character.m_name</c> values it
/// actually finds after instantiation.</summary>
public static class LabSignatureHuntContract {
  public const string FixtureId = "slayers-signature-hunt";
  public const int FixtureRevision = 5;
  public const string ReceiptSchema = "comfy-questlab-signature-hunt-fixture/v2";
  public const string ProofLevel = "fixture-preparation";
  public const string Disclaimer =
      "Fixed fixture preparation only; not live kill or completion proof.";

  public const string ExpectedWorldName = "ComfyQuestDemo";
  public const string ExpectedWorldUid = "-7600395338659582326";
  public const string CreatorOsBetaWorldName = "CreatorOSBeta1";
  public const string CreatorOsBetaWorldUid = "4257656027";

  /// <summary>The fixture remains closed over two reviewed world identities. The original
  /// authoring world keeps its proof lane; the fresh beta world is the only release seed that
  /// may reuse the same fixed, parameter-free hunt plan.</summary>
  public static bool SupportsWorld(string worldName, string worldUid) {
    return string.Equals(worldName, ExpectedWorldName, StringComparison.Ordinal)
        && string.Equals(worldUid, ExpectedWorldUid, StringComparison.Ordinal)
      || string.Equals(worldName, CreatorOsBetaWorldName, StringComparison.Ordinal)
        && string.Equals(worldUid, CreatorOsBetaWorldUid, StringComparison.Ordinal);
  }

  public const string MarkKey = "comfyQuestLabSignatureHunt";
  public const string MarkValue = FixtureId + "/v1";
  public const string RoleMarkKey = "comfyQuestLabSignatureHuntRole";
  public const string PreparationMarkKey = "comfyQuestLabSignatureHuntPreparation";

  public const string PrepareOperation = "signature_hunt_prepare";
  public const string StatusOperation = "signature_hunt_status";
  public const string ClearOperation = "signature_hunt_clear";
  public static readonly string[] Operations = {
    PrepareOperation, StatusOperation, ClearOperation,
  };

  public const string MarkerKind = "marker";
  public const string LoadoutKind = "loadout";
  public const string TargetKind = "target";

  public const string SupplyKind = "supply";
  public const int SupplyCount = 4;
  public static readonly LabSignatureHuntPlacement[] Placements = {
    // One briefing/binding sign beside the chest, left of the approach path.
    // Runtime stages the active creature twelve metres east of this sign,
    // placing it about seven metres from arrival with no poles between them.
    Place("marker-loadout-sign-post", MarkerKind, "briefing", "wood_pole2", -5f, 0f, 2f),
    Place("marker-loadout-sign", MarkerKind, "briefing", "sign", -5f, 1.2f, 2f, 180f, 1,
        "FIELD LODGE\nREAD YOUR CURRENT OBJECTIVE\nPRACTICE SUPPLIES BESIDE THIS SIGN [E]"),
    Place("supply-chest", SupplyKind, "loadout", "piece_chest_wood", -6f, .1f, -1f, 180f),
  };

  public static bool OwnsMark(string value) {
    return string.Equals(value, MarkValue, StringComparison.Ordinal);
  }

  public static bool IsOperation(string operation) {
    foreach (string allowed in Operations) {
      if (string.Equals(operation, allowed, StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
  }

  /// <summary>Validate the fixture's entire request surface. All values beyond the operation are
  /// forbidden; adding a general fixture name or selector here would create the spawn authority
  /// this slice explicitly excludes.</summary>
  public static bool ValidateRequest(
      string operation,
      string suite,
      string profile,
      string compareProfile,
      string selector,
      out string error) {
    return ValidateRequest(
        operation, suite, profile, compareProfile, selector,
        null, 0, 0, 0, out error);
  }

  public static bool ValidateRequest(
      string operation,
      string suite,
      string profile,
      string compareProfile,
      string selector,
      string corpus,
      int step,
      int seed,
      int expectedPreviousStep,
      out string error) {
    if (!IsOperation(operation)) {
      error = "operation_not_allowlisted";
      return false;
    }
    if (!string.IsNullOrWhiteSpace(suite)
        || !string.IsNullOrWhiteSpace(profile)
        || !string.IsNullOrWhiteSpace(compareProfile)
        || !string.IsNullOrWhiteSpace(selector)
        || !string.IsNullOrWhiteSpace(corpus)
        || step != 0
        || seed != 0
        || expectedPreviousStep != 0) {
      error = "request_argument_not_allowed";
      return false;
    }
    error = string.Empty;
    return true;
  }

  static LabSignatureHuntPlacement Place(
      string role,
      string kind,
      string area,
      string prefab,
      float localX,
      float localY,
      float localZ,
      float yaw = 0f,
      int stack = 1,
      string text = null,
      string creatureLabel = null) {
    return new LabSignatureHuntPlacement {
      Role = role,
      Kind = kind,
      Area = area,
      Prefab = prefab,
      LocalX = localX,
      LocalY = localY,
      LocalZ = localZ,
      Yaw = yaw,
      Stack = stack,
      Text = text,
      CreatureLabel = creatureLabel,
    };
  }
}

public sealed class LabSignatureHuntPlacement {
  public string Role;
  public string Kind;
  public string Area;
  public string Prefab;
  public float LocalX;
  public float LocalY;
  public float LocalZ;
  public float Yaw;
  public int Stack;
  public string Text;
  public string CreatureLabel;
}

public sealed class LabSignatureHuntTargetEvidence {
  public string Role;
  public string CreatureLabel;
  public string Prefab;
  public string GameObjectName;
  public string RawMName;
  public string MatcherTarget;
  public string ZdoId;
  public float X;
  public float Y;
  public float Z;
}

/// <summary>The one fixture-owned Charm target near the player start. Campaign play binds by
/// this durable identity instead of guessing among arbitrary nearby pieces.</summary>
public sealed class LabSignatureHuntBindingEvidence {
  public string Role;
  public string TargetKind;
  public string ZdoId;
  public float X;
  public float Y;
  public float Z;
}

/// <summary>Machine-readable preparation evidence. It makes a deliberately smaller claim than a
/// gameplay receipt: these exact creatures and supplies existed, and these were the two live
/// <c>m_name</c> values the kill matcher would receive at that moment.</summary>
public sealed class LabSignatureHuntFixtureReceipt {
  public string RequestId;
  public string PreparationId;
  public string PreparedUtc;
  public string Machine;
  public string PluginVersion;
  public string ReleaseId;
  public string WorldName;
  public string WorldUid;
  public int ExpectedObjectCount;
  public int StandingObjectCount;
  public float OriginX;
  public float OriginY;
  public float OriginZ;
  public LabSignatureHuntBindingEvidence BindingAnchor;
  public LabSignatureHuntTargetEvidence[] Targets = Array.Empty<LabSignatureHuntTargetEvidence>();

  public string ToJson() {
    var sb = new StringBuilder();
    sb.AppendLine("{");
    Field(sb, "schema", LabSignatureHuntContract.ReceiptSchema, true);
    Field(sb, "fixture_id", LabSignatureHuntContract.FixtureId, true);
    Number(sb, "fixture_revision", LabSignatureHuntContract.FixtureRevision, true);
    Field(sb, "state", "ready", true);
    Field(sb, "proof_level", LabSignatureHuntContract.ProofLevel, true);
    Field(sb, "disclaimer", LabSignatureHuntContract.Disclaimer, true);
    Field(sb, "target_lifecycle", "runtime-stage-entry", true);
    Field(sb, "request_id", RequestId, true);
    Field(sb, "preparation_id", PreparationId, true);
    Field(sb, "prepared_utc", PreparedUtc, true);
    Field(sb, "machine", Machine, true);
    Field(sb, "plugin_version", PluginVersion, true);
    Field(sb, "release_id", ReleaseId, true);
    Field(sb, "world_name", WorldName, true);
    Field(sb, "world_uid", WorldUid, true);
    sb.AppendLine("  \"ownership\": {");
    Field(sb, "mark_key", LabSignatureHuntContract.MarkKey, true, 4);
    Field(sb, "mark_value", LabSignatureHuntContract.MarkValue, false, 4);
    sb.AppendLine("  },");
    sb.AppendLine("  \"objects\": {");
    Number(sb, "expected", ExpectedObjectCount, true, 4);
    Number(sb, "standing_at_capture", StandingObjectCount, false, 4);
    sb.AppendLine("  },");
    sb.AppendLine("  \"origin\": {");
    Decimal(sb, "x", OriginX, true, 4);
    Decimal(sb, "y", OriginY, true, 4);
    Decimal(sb, "z", OriginZ, false, 4);
    sb.AppendLine("  },");
    sb.AppendLine("  \"loadout\": [");
    sb.AppendLine("    {");
    Field(sb, "prefab", "SpearCarapace", true, 6);
    Number(sb, "count", 4, true, 6);
    Field(sb, "delivery", "marked supply chest", false, 6);
    sb.AppendLine("    }");
    sb.AppendLine("  ],");
    LabSignatureHuntBindingEvidence anchor = BindingAnchor
        ?? new LabSignatureHuntBindingEvidence();
    sb.AppendLine("  \"binding_anchor\": {");
    Field(sb, "role", anchor.Role, true, 4);
    Field(sb, "target_kind", anchor.TargetKind, true, 4);
    Field(sb, "zdo_id", anchor.ZdoId, true, 4);
    sb.AppendLine("    \"position\": {");
    Decimal(sb, "x", anchor.X, true, 6);
    Decimal(sb, "y", anchor.Y, true, 6);
    Decimal(sb, "z", anchor.Z, false, 6);
    sb.AppendLine("    }");
    sb.AppendLine("  },");
    sb.AppendLine("  \"targets\": [");
    LabSignatureHuntTargetEvidence[] targets = Targets ?? Array.Empty<LabSignatureHuntTargetEvidence>();
    for (int i = 0; i < targets.Length; i++) {
      LabSignatureHuntTargetEvidence target = targets[i] ?? new LabSignatureHuntTargetEvidence();
      sb.AppendLine("    {");
      Field(sb, "role", target.Role, true, 6);
      Field(sb, "creature_label", target.CreatureLabel, true, 6);
      Field(sb, "prefab", target.Prefab, true, 6);
      Field(sb, "game_object_name", target.GameObjectName, true, 6);
      Field(sb, "raw_m_name", target.RawMName, true, 6);
      Field(sb, "matcher_target", target.MatcherTarget, true, 6);
      Field(sb, "captured_from", "Character.m_name", true, 6);
      Field(sb, "zdo_id", target.ZdoId, true, 6);
      sb.AppendLine("      \"position\": {");
      Decimal(sb, "x", target.X, true, 8);
      Decimal(sb, "y", target.Y, true, 8);
      Decimal(sb, "z", target.Z, false, 8);
      sb.AppendLine("      }");
      sb.Append("    }").AppendLine(i + 1 == targets.Length ? string.Empty : ",");
    }
    sb.AppendLine("  ],");
    sb.AppendLine("  \"limitations\": [");
    sb.AppendLine("    \"Creature identities were captured at preparation; live kill and quest receipts remain separate evidence.\",");
    sb.AppendLine("    \"Picked-up spears and Valheim-created death loot or ragdolls are not fixture-owned and are not removed by fixture cleanup.\"");
    sb.AppendLine("  ]");
    sb.AppendLine("}");
    return sb.ToString();
  }

  static void Field(StringBuilder sb, string name, string value, bool comma, int indent = 2) {
    sb.Append(' ', indent).Append('"').Append(name).Append("\": \"")
      .Append(LabBatchContract.Json(value)).Append('"')
      .AppendLine(comma ? "," : string.Empty);
  }

  static void Number(StringBuilder sb, string name, int value, bool comma, int indent = 2) {
    sb.Append(' ', indent).Append('"').Append(name).Append("\": ")
      .Append(value.ToString(CultureInfo.InvariantCulture))
      .AppendLine(comma ? "," : string.Empty);
  }

  static void Decimal(StringBuilder sb, string name, float value, bool comma, int indent = 2) {
    sb.Append(' ', indent).Append('"').Append(name).Append("\": ")
      .Append(value.ToString("0.###", CultureInfo.InvariantCulture))
      .AppendLine(comma ? "," : string.Empty);
  }
}

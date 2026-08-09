namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ComfyNetworkSense;

/// <summary>
/// One bounded, creator-facing rehearsal for a canonical quest event.
///
/// A scenario is intentionally not a second event producer. Its example envelope and ordinary
/// schema-1 quest come from <see cref="QuestEventCatalog"/> and <see cref="QuestAuthoring"/>, and
/// its run is decided by <see cref="QuestTriggerEvaluator"/>. The surrounding metadata only
/// explains what was rehearsed and how a creator can witness the same event in a private world.
/// </summary>
public sealed class LabScenarioDefinition {
  public string Id { get; internal set; }
  public string EventName { get; internal set; }
  public string School { get; internal set; }
  public string Profile { get; internal set; }
  public string TargetKind { get; internal set; }
  public string TargetDescription { get; internal set; }
  public string ExampleTarget { get; internal set; }
  public bool SupportsWeaponSkill { get; internal set; }
  public bool SupportsProjectile { get; internal set; }
  public IReadOnlyList<QuestEventCatalog.FieldDefinition> Fields { get; internal set; }
  public string LiveAction { get; internal set; }
  public string Safety { get; internal set; }
  public bool CourseWitnessAvailable { get; internal set; }
  public LabBatchSuite Suite { get; internal set; }

  public QuestEvent ExampleEvent() {
    return QuestAuthoring.ExampleEvent(EventName);
  }

  /// <summary>The exact schema-1 draft used by the rehearsal, already round-tripped through the
  /// shipping loader and proven against its example by the shared evaluator.</summary>
  public string BuildQuestView() {
    QuestDraft draft = QuestAuthoring.FromEvent(
        ExampleEvent(),
        Suite.Expectations[0].QuestId,
        Suite.Expectations[0].QuestName,
        "Quest Lab Scenario",
        category: "Quest Lab " + Title(School));
    return draft.Json;
  }

  /// <summary>Deterministic, machine-readable preparation manifest. This sits beside the quest
  /// draft rather than inside the live quest directory, so preparing a rehearsal cannot arm,
  /// edit, or delete a creator's work.</summary>
  public string BuildManifest(string questView) {
    QuestEvent example = ExampleEvent();
    var sb = new StringBuilder();
    sb.AppendLine("{");
    Field(sb, "schema", LabScenarioCatalog.ManifestSchema, true);
    Field(sb, "scenario_id", Id, true);
    Field(sb, "school", School, true);
    Field(sb, "event", EventName, true);
    Field(sb, "profile", Profile, true);
    Field(sb, "evidence_kind", Suite.EvidenceKind, true);
    Field(sb, "claim", "exact shared-evaluator rehearsal; not a live gameplay witness", true);
    Field(sb, "safety", Safety, true);
    Bool(sb, "course_witness_available", CourseWitnessAvailable, true);
    Field(sb, "live_action", LiveAction, true);
    Field(sb, "quest_view_sha256", Sha256(questView), true);
    sb.AppendLine("  \"example\": {");
    Field(sb, "target_kind", TargetKind, true, 4);
    Field(sb, "target_description", TargetDescription, true, 4);
    Field(sb, "target", example == null ? null : example.Target, true, 4);
    Field(sb, "weapon_skill", example == null ? null : example.WeaponSkill, true, 4);
    Bool(sb, "projectile", example != null && example.Projectile, true, 4);
    sb.AppendLine("    \"where\": {");
    int fieldIndex = 0;
    if (example != null) {
      foreach (QuestEventCatalog.FieldDefinition field in Fields) {
        example.TryGetField(field.Name, out string value);
        Field(sb, field.Name, value, fieldIndex + 1 < Fields.Count, 6);
        fieldIndex++;
      }
    }
    sb.AppendLine("    }");
    sb.AppendLine("  },");
    sb.AppendLine("  \"lifecycle\": [");
    String(sb, "prepare writes this owned manifest and quest-view without loading it", true, 4);
    String(sb, "run feeds the example and generated quest to the exact shared evaluator", true, 4);
    String(sb, "report or export preserves a machine-readable suite receipt", true, 4);
    String(sb, "reset clears volatile evaluator evidence and leaves creator files untouched", false, 4);
    sb.AppendLine("  ]");
    sb.AppendLine("}");
    return sb.ToString();
  }

  internal static string Title(string value) {
    if (string.IsNullOrWhiteSpace(value)) return string.Empty;
    return char.ToUpperInvariant(value[0]) + value.Substring(1).Replace('_', ' ');
  }

  static string Sha256(string value) {
    using (SHA256 sha = SHA256.Create()) {
      byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
      var text = new StringBuilder(digest.Length * 2);
      foreach (byte b in digest) {
        text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
      }
      return text.ToString();
    }
  }

  static void Field(
      StringBuilder sb, string name, string value, bool comma, int indent = 2) {
    sb.Append(' ', indent).Append('"').Append(name).Append("\": \"")
      .Append(LabBatchContract.Json(value)).Append('"')
      .AppendLine(comma ? "," : string.Empty);
  }

  static void Bool(
      StringBuilder sb, string name, bool value, bool comma, int indent = 2) {
    sb.Append(' ', indent).Append('"').Append(name).Append("\": ")
      .Append(value ? "true" : "false").AppendLine(comma ? "," : string.Empty);
  }

  static void String(StringBuilder sb, string value, bool comma, int indent) {
    sb.Append(' ', indent).Append('"').Append(LabBatchContract.Json(value)).Append('"')
      .AppendLine(comma ? "," : string.Empty);
  }
}

/// <summary>All 34 creator-safe events as deterministic one-event rehearsals.</summary>
public static class LabScenarioCatalog {
  public const string ManifestSchema = "comfy-questlab-scenario/v1";

  static readonly LabScenarioDefinition[] _all = BuildAll();
  static readonly Dictionary<string, LabScenarioDefinition> _byId = Index(_all);

  public static IReadOnlyList<LabScenarioDefinition> All { get { return _all; } }

  public static LabScenarioDefinition Find(string id) {
    if (string.IsNullOrWhiteSpace(id)) return null;
    _byId.TryGetValue(id.Trim(), out LabScenarioDefinition scenario);
    return scenario;
  }

  public static LabScenarioDefinition FindByEvent(string eventName) {
    string canonical = QuestEventCatalog.CanonicalName(eventName);
    return canonical == null ? null : Find("scenario-" + canonical);
  }

  public static LabScenarioDefinition FirstForSchool(string school) {
    foreach (LabScenarioDefinition scenario in _all) {
      if (string.Equals(scenario.School, school, StringComparison.OrdinalIgnoreCase)) {
        return scenario;
      }
    }
    return null;
  }

  /// <summary>Run one prepared example through the same loader and evaluator source as both mods.
  /// The source and evidence kind remain explicit so this can never masquerade as gameplay.</summary>
  public static LabBatchSession Run(
      LabScenarioDefinition scenario, string runId, string nowUtc) {
    if (scenario == null) throw new ArgumentNullException(nameof(scenario));
    LabBatchExpectation expected = scenario.Suite.Expectations[0];
    QuestEvent catalogExample = scenario.ExampleEvent();
    string actionKey = "scenario:" + scenario.EventName;
    var witnessed = new QuestEvent(
        catalogExample.Name,
        catalogExample.Target,
        catalogExample.WeaponSkill,
        catalogExample.Projectile,
        actionKey,
        catalogExample.Fields);
    QuestDraft draft = QuestAuthoring.FromEvent(
        witnessed,
        expected.QuestId,
        expected.QuestName,
        "Quest Lab Scenario",
        category: "Quest Lab " + LabScenarioDefinition.Title(scenario.School));

    // This explicit call is the receipt-producing decision. FromEvent also demands a match as a
    // generation invariant, but the scenario run does not treat that prior validation as evidence.
    var evaluator = new QuestTriggerEvaluator(0.0, 0.0);
    IReadOnlyList<QuestCompletion> completions = evaluator.OnEvent(
        new[] { draft.Quest }, witnessed, 1.0);

    var session = new LabBatchSession(scenario.Suite, runId, nowUtc);
    session.Observe(
        scenario.School,
        scenario.EventName,
        "shared-evaluator-preview",
        witnessed.Target,
        actionKey,
        true,
        true,
        "synthetic-scenario",
        nowUtc);
    foreach (QuestCompletion completion in completions) {
      session.Complete(
          completion.QuestId,
          completion.EventName,
          actionKey,
          "synthetic-scenario",
          nowUtc);
    }
    session.Finish(nowUtc);
    return session;
  }

  static LabScenarioDefinition[] BuildAll() {
    var result = new List<LabScenarioDefinition>();
    foreach (string school in LabCategory.All) {
      var names = new List<string>();
      foreach (string eventName in QuestEventCatalog.AllEventNames) {
        if (QuestEventCatalog.TryGet(eventName, out QuestEventCatalog.Definition definition)
            && string.Equals(definition.Category, school, StringComparison.OrdinalIgnoreCase)) {
          names.Add(definition.Name);
        }
      }
      names.Sort(StringComparer.Ordinal);
      foreach (string eventName in names) {
        QuestEventCatalog.TryGet(eventName, out QuestEventCatalog.Definition definition);
        string id = "scenario-" + eventName;
        string instruction = LiveAction(eventName);
        var expectation = new LabBatchExpectation {
          School = school,
          EventName = eventName,
          QuestId = "questlab_" + id.Replace('-', '_'),
          QuestName = "Rehearse " + eventName,
          Instruction = instruction,
        };
        result.Add(new LabScenarioDefinition {
          Id = id,
          EventName = eventName,
          School = school,
          Profile = definition.Profile,
          TargetKind = definition.TargetKind,
          TargetDescription = definition.TargetDescription,
          ExampleTarget = definition.ExampleTarget,
          SupportsWeaponSkill = definition.SupportsWeaponSkill,
          SupportsProjectile = definition.SupportsProjectile,
          Fields = definition.Fields,
          LiveAction = instruction,
          Safety = Safety(eventName),
          CourseWitnessAvailable = CourseWitness(eventName),
          Suite = new LabBatchSuite {
            Id = id,
            Name = "Scenario: " + eventName,
            Description = "One exact-evaluator rehearsal generated from shared authoring metadata.",
            EvidenceKind = "synthetic-scenario",
            Expectations = new[] { expectation },
          },
        });
      }
    }
    return result.ToArray();
  }

  static Dictionary<string, LabScenarioDefinition> Index(
      IEnumerable<LabScenarioDefinition> scenarios) {
    var index = new Dictionary<string, LabScenarioDefinition>(StringComparer.OrdinalIgnoreCase);
    foreach (LabScenarioDefinition scenario in scenarios) index[scenario.Id] = scenario;
    return index;
  }

  static bool CourseWitness(string eventName) {
    LabBatchSuite suite = LabBatchContract.FindBuiltinSuite("all-schools");
    foreach (LabBatchExpectation expected in suite.Expectations) {
      if (string.Equals(expected.EventName, eventName, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }
    return false;
  }

  static string Safety(string eventName) {
    switch (eventName) {
      case "global_key_removed":
      case "global_key_set": return "world-state-change";
      case "player_died":
      case "skills_lowered": return "player-risk";
      case "station_output_produced": return "timed-station";
      default: return "ordinary-private-world-action";
    }
  }

  static string LiveAction(string eventName) {
    switch (eventName) {
      case "attack_blocked": return "Block a creature's attack with a shield or weapon.";
      case "character_healed": return "After losing health, receive healing on the local player.";
      case "character_staggered": return "Parry or damage a creature until it staggers.";
      case "chat_sent": return "Send a normal chat message; its text remains redacted.";
      case "container_emptied": return "Remove the final item from a container.";
      case "damage_dealt": return "Hit a creature and deal damage.";
      case "global_key_removed": return "In a disposable world, remove a known global key.";
      case "global_key_set": return "In a disposable world, set a known global key.";
      case "item_consumed": return "Consume food, mead, or another usable inventory item.";
      case "item_crafted": return "Craft an item at a crafting station.";
      case "item_dropped": return "Drop an inventory item into the world.";
      case "item_equipped": return "Equip a tool, weapon, shield, or armor item.";
      case "item_picked_up": return "Pick up an item from the ground.";
      case "item_unequipped": return "Unequip an equipped item.";
      case "kill": return "Kill a creature; the compact course provides a Greyling.";
      case "max_health_changed": return "Change maximum health by eating or letting food expire.";
      case "piece_damaged": return "Damage a player-built structure piece.";
      case "piece_destroyed": return "Destroy a structure piece completely.";
      case "piece_placed": return "Place any structure with the Hammer.";
      case "piece_removed": return "Remove a structure with the Hammer.";
      case "piece_repaired": return "Repair a damaged structure with the Hammer.";
      case "player_died": return "Die only in a disposable character/world test.";
      case "player_teleported": return "Travel through either paired gallery portal.";
      case "resource_damaged": return "Strike the staged Birch or another harvest resource.";
      case "resource_picked": return "Pick a berry, mushroom, crop, or other pickable resource.";
      case "sign_written": return "Edit and save the hub sign; its text remains redacted.";
      case "skill_raised": return "Use a skill until it gains progress.";
      case "skills_lowered": return "Use only a disposable character death test.";
      case "stamina_gained": return "Spend stamina, then let it recover.";
      case "stamina_spent": return "Sprint, jump, attack, or perform another stamina action.";
      case "station_fuel_added": return "Add fuel to a smelter, kiln, hearth, or other station.";
      case "station_input_added": return "Add ore, food, or fermentable input to a station.";
      case "station_output_collected": return "Collect a finished item from a station.";
      case "station_output_produced": return "Wait for a loaded station to produce its output.";
      default: return "Perform the matching action in a private test world.";
    }
  }
}

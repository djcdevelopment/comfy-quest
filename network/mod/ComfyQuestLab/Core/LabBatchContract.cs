namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using ComfyNetworkSense;

/// <summary>Pure data contract for bounded Quest Lab suites and their receipts.
///
/// No Unity or BepInEx references: the runtime controller owns files and coroutines, while
/// this file links into the headless test project. That keeps suite definitions, example
/// quests, completion rules, and JSON evidence testable without pretending a headless probe
/// is an in-game witness.</summary>
public static class LabBatchContract {
  public const string ReceiptSchema = "comfy-questlab-suite-receipt/v1";
  public const string RequestSchema = "comfy-questlab-batch-request/v1";
  public const int MaxWitnesses = 256;
  static readonly LabBatchSuite AllSchoolsSuite = new LabBatchSuite {
    Id = "all-schools",
    Name = "All schools live witness",
    Description = "One bindable example quest and one real canonical witness in every school.",
    EvidenceKind = "live-gameplay",
    Expectations = new[] {
      Expect(LabCategory.Combat, "kill",
          "Pick up the bow and arrows at the combat spoke mouth; shoot the Greyling at its rune."),
      Expect(LabCategory.Harvest, "resource_damaged",
          "Pick up the bronze axe beside the arrival portal and strike the adjacent birch."),
      Expect(LabCategory.Inventory, "item_picked_up",
          "Pick up any staged tool, supply, fuel, or food."),
      Expect(LabCategory.Building, "piece_placed",
          "Pick up the Hammer and Wood in front of the building bench, then place any piece."),
      Expect(LabCategory.Crafting, "station_fuel_added",
          "Pick up the Coal directly in front of the crafting smelter and add it as fuel."),
      Expect(LabCategory.Progression, "skill_raised",
          "The bow, axe, or building lap normally raises a skill; otherwise use any skill once."),
      Expect(LabCategory.World, "player_teleported", "Take either paired gallery portal."),
      Expect(LabCategory.Social, "sign_written", "Edit the hub sign that says sign here."),
    },
  };

  static readonly LabBatchSuite CreatorEventsSuite = BuildCreatorEventsSuite();

  public static readonly LabBatchSuite[] Suites = { AllSchoolsSuite, CreatorEventsSuite };

  static LabBatchExpectation Expect(string school, string eventName, string instruction) {
    return new LabBatchExpectation {
      School = school,
      EventName = eventName,
      QuestId = "questlab_suite_" + school,
      QuestName = "Quest Lab " + Title(school) + " Witness",
      Instruction = instruction,
    };
  }

  static LabBatchSuite BuildCreatorEventsSuite() {
    var names = new List<string>(QuestEventCatalog.AllEventNames);
    names.Sort(StringComparer.Ordinal);
    var expectations = new List<LabBatchExpectation>(names.Count);
    foreach (string eventName in names) {
      QuestEventCatalog.Definition definition;
      string category = QuestEventCatalog.TryGet(eventName, out definition)
          ? definition.Category
          : "unknown";
      expectations.Add(new LabBatchExpectation {
        School = category,
        EventName = eventName,
        QuestId = "questlab_contract_" + eventName,
        QuestName = "Contract " + eventName,
        Instruction = "Synthetic shared-evaluator probe; not an in-game witness.",
      });
    }
    return new LabBatchSuite {
      Id = "creator-events",
      Name = "Creator event contract",
      Description = "All safe canonical events bind through the source-shared evaluator.",
      EvidenceKind = "synthetic-contract",
      Expectations = expectations.ToArray(),
    };
  }

  public static LabBatchSuite FindSuite(string id) {
    string wanted = string.IsNullOrWhiteSpace(id) ? AllSchoolsSuite.Id : id.Trim();
    LabBatchSuite builtin = FindBuiltinSuite(wanted);
    if (builtin != null) return builtin;
    LabScenarioDefinition scenario = LabScenarioCatalog.Find(wanted);
    return scenario == null ? null : scenario.Suite;
  }

  /// <summary>The two release-verification suites, excluding one-event creator rehearsals.
  /// Kept separate so the scenario catalog can classify course-backed events without a static
  /// initialization loop.</summary>
  internal static LabBatchSuite FindBuiltinSuite(string id) {
    foreach (LabBatchSuite suite in Suites) {
      if (string.Equals(suite.Id, id, StringComparison.OrdinalIgnoreCase)) return suite;
    }
    return null;
  }

  public static string SuiteRoster() {
    var sb = new StringBuilder("bounded Quest Lab suites:\n");
    foreach (LabBatchSuite suite in Suites) {
      sb.Append("  ").Append(suite.Id).Append(" — ").Append(suite.Name).Append(": ")
        .Append(suite.Expectations.Length.ToString(CultureInfo.InvariantCulture))
        .Append(" required event(s), ").Append(suite.EvidenceKind).AppendLine();
      sb.AppendLine("    " + suite.Description);
    }
    sb.Append("34 one-event rehearsals are also allowlisted as scenario-<event>. ")
      .Append("Browse them in the Scenarios tab, then use the same prepare/run/reset/report/export ")
      .Append("verbs. Use questlab_batch prepare all-schools before its live run.");
    return sb.ToString();
  }

  /// <summary>A complete, ordinary schema-1 quest view. It uses no lab-only fields, so each
  /// example can be copied to ComfyNetworkSense unchanged.</summary>
  public static string BuildQuestView(LabBatchSuite suite) {
    if (suite == null) {
      throw new ArgumentNullException(nameof(suite));
    }
    var sb = new StringBuilder();
    sb.AppendLine("{");
    sb.AppendLine("  \"schema_version\": 1,");
    sb.AppendLine("  \"player\": { \"name\": \"Quest Lab suite\", \"discord\": null },");
    sb.AppendLine("  \"created_at\": \"2026-08-08T00:00:00Z\",");
    sb.AppendLine("  \"picker_version\": 1,");
    sb.AppendLine("  \"quests\": [");
    for (int i = 0; i < suite.Expectations.Length; i++) {
      LabBatchExpectation expected = suite.Expectations[i];
      sb.AppendLine("    {");
      sb.AppendLine("      \"quest_id\": \"" + Json(expected.QuestId) + "\",");
      sb.AppendLine("      \"name\": \"" + Json(expected.QuestName) + "\",");
      sb.AppendLine("      \"category\": \"Quest Lab " + Json(Title(expected.School)) + "\",");
      sb.AppendLine("      \"requirements\": \"" + Json(expected.Instruction) + "\",");
      sb.AppendLine("      \"bot_command\": \"/comfy test summons_type:"
          + Json(expected.QuestName) + " image:\",");
      sb.AppendLine("      \"auto_checked\": false,");
      sb.AppendLine("      \"venue\": \"in_game\",");
      sb.AppendLine("      \"trigger\": { \"event\": \"" + Json(expected.EventName)
          + "\", \"target\": \"any\" },");
      sb.AppendLine("      \"guild\": \"Quest Lab\",");
      sb.AppendLine("      \"era\": 17");
      sb.Append("    }").AppendLine(i + 1 == suite.Expectations.Length ? string.Empty : ",");
    }
    sb.AppendLine("  ]");
    sb.AppendLine("}");
    return sb.ToString();
  }

  /// <summary>Exercise every catalog event against a matching quest through the exact evaluator
  /// source linked into both mods. The resulting receipt says synthetic-contract explicitly.</summary>
  public static LabBatchSession RunCreatorEventContract(string runId, string nowUtc) {
    LabBatchSuite suite = CreatorEventsSuite;
    var session = new LabBatchSession(suite, runId, nowUtc);
    var evaluator = new QuestTriggerEvaluator(0.0, 1.0);
    double now = 1.0;
    foreach (LabBatchExpectation expected in suite.Expectations) {
      var quest = new TrackedQuest {
        QuestId = expected.QuestId,
        Name = expected.QuestName,
        Guild = "Quest Lab",
        Category = expected.School,
        Venue = "in_game",
        TriggerEvent = expected.EventName,
        TriggerTarget = "any",
      };
      string actionKey = "contract:" + expected.EventName;
      var gameplayEvent = new QuestEvent(
          expected.EventName, "sample", dedupeKey: actionKey);
      IReadOnlyList<QuestCompletion> completions = evaluator.OnEvent(
          new[] { quest }, gameplayEvent, now++);
      session.Observe(
          expected.School,
          expected.EventName,
          "contract:" + expected.EventName,
          "sample",
          actionKey,
          true,
          true,
          "synthetic-contract",
          nowUtc);
      foreach (QuestCompletion completion in completions) {
        session.Complete(
            completion.QuestId,
            completion.EventName,
            actionKey,
            "synthetic-contract",
            nowUtc);
      }
    }
    session.Finish(nowUtc);
    return session;
  }

  internal static string Json(string value) {
    if (value == null) {
      return string.Empty;
    }
    var sb = new StringBuilder(value.Length + 8);
    foreach (char c in value) {
      switch (c) {
        case '\\': sb.Append("\\\\"); break;
        case '"': sb.Append("\\\""); break;
        case '\b': sb.Append("\\b"); break;
        case '\f': sb.Append("\\f"); break;
        case '\n': sb.Append("\\n"); break;
        case '\r': sb.Append("\\r"); break;
        case '\t': sb.Append("\\t"); break;
        default:
          if (c < 0x20) {
            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
          } else {
            sb.Append(c);
          }
          break;
      }
    }
    return sb.ToString();
  }

  static string Title(string value) {
    if (string.IsNullOrWhiteSpace(value)) {
      return string.Empty;
    }
    return char.ToUpperInvariant(value[0]) + value.Substring(1).Replace('_', ' ');
  }
}

/// <summary>The complete remote operation vocabulary. Validation is pure and shared with
/// headless tests; adding a command requires changing this policy rather than accidentally
/// widening a string-to-console bridge.</summary>
public static class LabBatchRequestPolicy {
  public static readonly string[] Operations = {
    "prepare", "run", "reset", "report", "export",
    "gallery_build", "gallery_compare", "gallery_identify", "gallery_evidence",
    "gallery_clear", "gallery_rebuild",
  };

  public static bool Validate(
      string operation,
      string suite,
      string profile,
      string compareProfile,
      string selector,
      out string error) {
    error = string.Empty;
    operation = (operation ?? string.Empty).Trim().ToLowerInvariant();
    if (operation == "prepare" || operation == "run") {
      if (LabBatchContract.FindSuite(suite) == null) {
        error = "suite_not_allowlisted";
        return false;
      }
      return NoExtras(profile, compareProfile, selector, out error);
    }
    if (operation == "reset" || operation == "report" || operation == "export"
        || operation == "gallery_identify") {
      return NoExtras(suite, profile, compareProfile, selector, out error);
    }
    if (operation == "gallery_evidence") {
      if (string.IsNullOrWhiteSpace(selector)
          || (!string.Equals(selector, "all", StringComparison.OrdinalIgnoreCase)
              && LabGalleryPlan.Find(selector) == null
              && !SafeToken(selector, 80))) {
        error = "gallery_selector_invalid";
        return false;
      }
      return NoExtras(suite, profile, compareProfile, out error);
    }
    if (operation == "gallery_build" || operation == "gallery_rebuild") {
      if (string.IsNullOrWhiteSpace(profile) || LabGalleryPlan.Find(profile) == null) {
        error = "gallery_profile_not_allowlisted";
        return false;
      }
      return NoExtras(suite, compareProfile, selector, out error);
    }
    if (operation == "gallery_compare") {
      if (string.IsNullOrWhiteSpace(profile)
          || string.IsNullOrWhiteSpace(compareProfile)
          || LabGalleryPlan.Find(profile) == null
          || LabGalleryPlan.Find(compareProfile) == null) {
        error = "gallery_profile_not_allowlisted";
        return false;
      }
      return NoExtras(suite, selector, out error);
    }
    if (operation == "gallery_clear") {
      if (string.IsNullOrWhiteSpace(selector)
          || (!string.Equals(selector, "all", StringComparison.OrdinalIgnoreCase)
              && LabGalleryPlan.Find(selector) == null
              && !SafeToken(selector, 80))) {
        error = "gallery_selector_invalid";
        return false;
      }
      return NoExtras(suite, profile, compareProfile, out error);
    }
    error = "operation_not_allowlisted";
    return false;
  }

  static bool NoExtras(string one, string two, string three, out string error) {
    return NoExtras(new[] { one, two, three }, out error);
  }

  static bool NoExtras(string one, string two, out string error) {
    return NoExtras(new[] { one, two }, out error);
  }

  static bool NoExtras(string one, string two, string three, string four, out string error) {
    return NoExtras(new[] { one, two, three, four }, out error);
  }

  static bool NoExtras(string[] values, out string error) {
    foreach (string value in values) {
      if (!string.IsNullOrWhiteSpace(value)) {
        error = "request_argument_not_allowed";
        return false;
      }
    }
    error = string.Empty;
    return true;
  }

  static bool SafeToken(string value, int maxLength) {
    if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength) return false;
    foreach (char c in value) {
      if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.') return false;
    }
    return true;
  }
}

public sealed class LabBatchSuite {
  public string Id;
  public string Name;
  public string Description;
  public string EvidenceKind;
  public LabBatchExpectation[] Expectations;
}

public sealed class LabBatchExpectation {
  public string School;
  public string EventName;
  public string QuestId;
  public string QuestName;
  public string Instruction;
}

public sealed class LabBatchExpectationResult {
  public string School;
  public string EventName;
  public string QuestId;
  public string Instruction;
  public bool Witnessed;
  public bool QuestCompleted;
  public int CanonicalActionCount;
  public int QuestCompletionCount;
  public string FirstSignature;
  public string FirstTarget;
  public string FirstActionKey;
  public string FirstWitnessUtc;
  public string FirstCompletionUtc;
}

public sealed class LabBatchWitness {
  public string School;
  public string EventName;
  public string SignatureId;
  public string Target;
  public string ActionKey;
  public string Source;
  public string AtUtc;
  public bool Evaluated;
  public int RawWitnessCount;
}

public sealed class LabBatchReceiptContext {
  public string Machine;
  public string PluginVersion;
  public string ReleaseId;
  public string RuntimeProfile;
  public string GeneratedUtc;
}

/// <summary>Bounded state machine shared by live and synthetic suites.</summary>
public sealed class LabBatchSession {
  readonly LabBatchSuite _suite;
  readonly Dictionary<string, LabBatchExpectationResult> _byEvent;
  readonly Dictionary<string, LabBatchExpectationResult> _byQuest;
  readonly HashSet<string> _completionKeys = new HashSet<string>(StringComparer.Ordinal);
  readonly List<LabBatchWitness> _witnesses = new List<LabBatchWitness>();

  public string RunId { get; }
  public string StartedUtc { get; }
  public string FinishedUtc { get; private set; }
  public string State { get; private set; }
  public int RawWitnessCount { get; private set; }
  public int CanonicalActionCount { get; private set; }
  public int CoalescedWitnessCount { get; private set; }
  public int DoubleCompletionCount { get; private set; }
  public int UnexpectedCanonicalActions { get; private set; }

  public LabBatchSuite Suite { get { return _suite; } }
  public IReadOnlyList<LabBatchWitness> Witnesses { get { return _witnesses; } }

  public LabBatchSession(LabBatchSuite suite, string runId, string startedUtc) {
    _suite = suite ?? throw new ArgumentNullException(nameof(suite));
    RunId = runId ?? throw new ArgumentNullException(nameof(runId));
    StartedUtc = startedUtc ?? string.Empty;
    State = "running";
    _byEvent = new Dictionary<string, LabBatchExpectationResult>(StringComparer.OrdinalIgnoreCase);
    _byQuest = new Dictionary<string, LabBatchExpectationResult>(StringComparer.OrdinalIgnoreCase);
    foreach (LabBatchExpectation expected in suite.Expectations) {
      var result = new LabBatchExpectationResult {
        School = expected.School,
        EventName = expected.EventName,
        QuestId = expected.QuestId,
        Instruction = expected.Instruction,
      };
      _byEvent[expected.EventName] = result;
      _byQuest[expected.QuestId] = result;
    }
  }

  public void Observe(
      string school,
      string eventName,
      string signatureId,
      string target,
      string actionKey,
      bool firstCreatorWitness,
      bool evaluated,
      string source,
      string atUtc) {
    if (State != "running" && State != "complete") {
      return;
    }
    RawWitnessCount++;

    LabBatchWitness existing = null;
    if (!string.IsNullOrWhiteSpace(actionKey)) {
      foreach (LabBatchWitness witness in _witnesses) {
        if (string.Equals(witness.EventName, eventName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(witness.ActionKey, actionKey, StringComparison.Ordinal)) {
          existing = witness;
          break;
        }
      }
    }
    if (existing != null) {
      existing.RawWitnessCount++;
      CoalescedWitnessCount++;
      return;
    }
    if (!firstCreatorWitness) {
      CoalescedWitnessCount++;
    } else {
      CanonicalActionCount++;
    }

    if (_witnesses.Count < LabBatchContract.MaxWitnesses) {
      _witnesses.Add(new LabBatchWitness {
        School = school,
        EventName = eventName,
        SignatureId = signatureId,
        Target = target ?? string.Empty,
        ActionKey = actionKey ?? string.Empty,
        Source = source ?? string.Empty,
        AtUtc = atUtc ?? string.Empty,
        Evaluated = evaluated,
        RawWitnessCount = 1,
      });
    }

    if (!firstCreatorWitness) {
      return;
    }
    LabBatchExpectationResult expected;
    if (!_byEvent.TryGetValue(eventName ?? string.Empty, out expected)) {
      UnexpectedCanonicalActions++;
      return;
    }
    expected.Witnessed = true;
    expected.CanonicalActionCount++;
    if (string.IsNullOrEmpty(expected.FirstWitnessUtc)) {
      expected.FirstSignature = signatureId ?? string.Empty;
      expected.FirstTarget = target ?? string.Empty;
      expected.FirstActionKey = actionKey ?? string.Empty;
      expected.FirstWitnessUtc = atUtc ?? string.Empty;
    }
    UpdateState(atUtc);
  }

  public void Complete(
      string questId,
      string eventName,
      string actionKey,
      string source,
      string atUtc) {
    if (State != "running" && State != "complete") {
      return;
    }
    LabBatchExpectationResult expected;
    if (!_byQuest.TryGetValue(questId ?? string.Empty, out expected)) {
      return;
    }
    if (string.IsNullOrWhiteSpace(actionKey)) {
      actionKey = expected.FirstActionKey;
    }
    expected.QuestCompleted = true;
    expected.QuestCompletionCount++;
    if (string.IsNullOrEmpty(expected.FirstCompletionUtc)) {
      expected.FirstCompletionUtc = atUtc ?? string.Empty;
    }
    if (!string.IsNullOrWhiteSpace(actionKey)) {
      string completionKey = questId + "\n" + eventName + "\n" + actionKey;
      if (!_completionKeys.Add(completionKey)) {
        DoubleCompletionCount++;
      }
    }
    UpdateState(atUtc);
  }

  public void Finish(string atUtc) {
    UpdateState(atUtc);
    if (State == "running") {
      FinishedUtc = atUtc ?? string.Empty;
      State = "incomplete";
    }
  }

  public int WitnessedCount {
    get {
      int count = 0;
      foreach (LabBatchExpectationResult expected in _byEvent.Values) {
        if (expected.Witnessed) count++;
      }
      return count;
    }
  }

  public int CompletedQuestCount {
    get {
      int count = 0;
      foreach (LabBatchExpectationResult expected in _byEvent.Values) {
        if (expected.QuestCompleted) count++;
      }
      return count;
    }
  }

  public string Verdict {
    get {
      if (DoubleCompletionCount > 0) return "fail_double_completion";
      if (WitnessedCount == _suite.Expectations.Length
          && CompletedQuestCount == _suite.Expectations.Length) return "pass";
      return "incomplete";
    }
  }

  public IReadOnlyList<LabBatchExpectationResult> Results() {
    var results = new List<LabBatchExpectationResult>(_suite.Expectations.Length);
    foreach (LabBatchExpectation expected in _suite.Expectations) {
      results.Add(_byEvent[expected.EventName]);
    }
    return results;
  }

  public string Summary() {
    var sb = new StringBuilder();
    sb.Append(_suite.Id).Append(" ").Append(Verdict).Append(": ")
      .Append(WitnessedCount).Append('/').Append(_suite.Expectations.Length)
      .Append(" events witnessed, ").Append(CompletedQuestCount).Append('/')
      .Append(_suite.Expectations.Length).Append(" example quests completed; ")
      .Append(RawWitnessCount).Append(" raw witness(es) → ")
      .Append(CanonicalActionCount).Append(" canonical action(s), ")
      .Append(CoalescedWitnessCount).Append(" coalesced.");
    foreach (LabBatchExpectationResult expected in Results()) {
      if (!expected.Witnessed || !expected.QuestCompleted) {
        sb.AppendLine().Append("  [ ] ").Append(expected.School).Append(" / ")
          .Append(expected.EventName).Append(" — ").Append(expected.Instruction);
      }
    }
    return sb.ToString();
  }

  public string ToJson(LabBatchReceiptContext context) {
    context ??= new LabBatchReceiptContext();
    var sb = new StringBuilder();
    sb.AppendLine("{");
    Field(sb, "schema", LabBatchContract.ReceiptSchema, true);
    Field(sb, "run_id", RunId, true);
    Field(sb, "suite", _suite.Id, true);
    Field(sb, "suite_name", _suite.Name, true);
    Field(sb, "evidence_kind", _suite.EvidenceKind, true);
    Field(sb, "machine", context.Machine, true);
    Field(sb, "plugin_version", context.PluginVersion, true);
    Field(sb, "release_id", context.ReleaseId, true);
    Field(sb, "runtime_profile", context.RuntimeProfile, true);
    Field(sb, "started_utc", StartedUtc, true);
    Field(sb, "finished_utc", FinishedUtc, true);
    Field(sb, "generated_utc", context.GeneratedUtc, true);
    Field(sb, "state", State, true);
    Field(sb, "verdict", Verdict, true);
    Number(sb, "required_events", _suite.Expectations.Length, true);
    Number(sb, "witnessed_events", WitnessedCount, true);
    Number(sb, "completed_example_quests", CompletedQuestCount, true);
    Number(sb, "raw_witnesses", RawWitnessCount, true);
    Number(sb, "canonical_actions", CanonicalActionCount, true);
    Number(sb, "coalesced_witnesses", CoalescedWitnessCount, true);
    Number(sb, "double_completions", DoubleCompletionCount, true);
    Number(sb, "unexpected_canonical_actions", UnexpectedCanonicalActions, true);
    sb.AppendLine("  \"expectations\": [");
    IReadOnlyList<LabBatchExpectationResult> results = Results();
    for (int i = 0; i < results.Count; i++) {
      LabBatchExpectationResult result = results[i];
      sb.AppendLine("    {");
      Field(sb, "school", result.School, true, 6);
      Field(sb, "event", result.EventName, true, 6);
      Field(sb, "quest_id", result.QuestId, true, 6);
      Field(sb, "instruction", result.Instruction, true, 6);
      Bool(sb, "witnessed", result.Witnessed, true, 6);
      Bool(sb, "quest_completed", result.QuestCompleted, true, 6);
      Number(sb, "canonical_action_count", result.CanonicalActionCount, true, 6);
      Number(sb, "quest_completion_count", result.QuestCompletionCount, true, 6);
      Field(sb, "first_signature", result.FirstSignature, true, 6);
      Field(sb, "first_target", result.FirstTarget, true, 6);
      Field(sb, "first_action_key", result.FirstActionKey, true, 6);
      Field(sb, "first_witness_utc", result.FirstWitnessUtc, true, 6);
      Field(sb, "first_completion_utc", result.FirstCompletionUtc, false, 6);
      sb.Append("    }").AppendLine(i + 1 == results.Count ? string.Empty : ",");
    }
    sb.AppendLine("  ],");
    sb.AppendLine("  \"witnesses\": [");
    for (int i = 0; i < _witnesses.Count; i++) {
      LabBatchWitness witness = _witnesses[i];
      sb.AppendLine("    {");
      Field(sb, "school", witness.School, true, 6);
      Field(sb, "event", witness.EventName, true, 6);
      Field(sb, "signature", witness.SignatureId, true, 6);
      Field(sb, "target", witness.Target, true, 6);
      Field(sb, "action_key", witness.ActionKey, true, 6);
      Field(sb, "source", witness.Source, true, 6);
      Field(sb, "at_utc", witness.AtUtc, true, 6);
      Bool(sb, "evaluated", witness.Evaluated, true, 6);
      Number(sb, "raw_witness_count", witness.RawWitnessCount, false, 6);
      sb.Append("    }").AppendLine(i + 1 == _witnesses.Count ? string.Empty : ",");
    }
    sb.AppendLine("  ]");
    sb.AppendLine("}");
    return sb.ToString();
  }

  void UpdateState(string atUtc) {
    if (State == "failed" || State == "incomplete") return;
    if (DoubleCompletionCount > 0) {
      State = "failed";
      FinishedUtc = atUtc ?? string.Empty;
      return;
    }
    if (State != "running") return;
    if (WitnessedCount == _suite.Expectations.Length
        && CompletedQuestCount == _suite.Expectations.Length) {
      State = "complete";
      FinishedUtc = atUtc ?? string.Empty;
    }
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

  static void Bool(StringBuilder sb, string name, bool value, bool comma, int indent = 2) {
    sb.Append(' ', indent).Append('"').Append(name).Append("\": ")
      .Append(value ? "true" : "false").AppendLine(comma ? "," : string.Empty);
  }
}

namespace ComfyQuestLab;

using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

/// <summary>Unity/filesystem half of the bounded suite contract.
///
/// The only remote surface is one fixed request file containing an expiring schema, a safe
/// request id, and an operation from a closed allowlist. It cannot invoke a console command,
/// inject a key, choose a path, name a prefab, or carry arbitrary arguments.</summary>
public sealed class LabBatchController {
  const string SuiteQuestFileName = "questlab-batch-all-schools.json";
  const string RequestFileName = "questlab-batch-request.json";
  const string RequestReceiptSchema = "comfy-questlab-batch-request-receipt/v1";
  const float RequestPollSeconds = 0.75f;
  const int MaxRequestBytes = 4096;

  readonly LabGalleryBuilder _gallery;
  readonly LabBlueprintBuilder _blueprints;
  readonly LabHistoryScenarioRunner _history;
  readonly LabSignatureHuntProvider _signatureHunt;
  LabBatchSession _session;
  bool _preparing;
  string _preparedSuiteId;
  string _preparedArtifactPath;
  string _preparedQuestPath;
  string _lastExportPath;
  string _lastAutoExportState;
  float _nextRequestPoll;
  int _runSequence;

  public LabBatchController(
      LabGalleryBuilder gallery,
      LabBlueprintBuilder blueprints = null,
      LabSignatureHuntProvider signatureHunt = null) {
    _gallery = gallery ?? throw new ArgumentNullException(nameof(gallery));
    _blueprints = blueprints;
    _history = new LabHistoryScenarioRunner();
    _signatureHunt = signatureHunt;
  }

  public LabBatchSession Session { get { return _session; } }
  public string PreparedSuiteId { get { return _preparedSuiteId; } }
  public string PreparedArtifactPath { get { return _preparedArtifactPath; } }
  public string PreparedQuestPath { get { return _preparedQuestPath; } }
  public bool IsPreparing { get { return _preparing || _gallery.IsRunning; } }
  public string LastExportPath { get { return _lastExportPath; } }
  public bool IsLiveCaptureRunning {
    get {
      return _session != null && _session.State == "running"
          && _session.Suite.EvidenceKind == "live-gameplay";
    }
  }

  static string RootDir {
    get { return Path.Combine(BepInEx.Paths.ConfigPath, "comfy-quest-lab"); }
  }

  static string SuiteQuestPath {
    get { return Path.Combine(Path.Combine(RootDir, "quests"), SuiteQuestFileName); }
  }

  static string RequestPath {
    get { return Path.Combine(Path.Combine(RootDir, "requests"), RequestFileName); }
  }

  static string SuiteReceiptsDir {
    get { return Path.Combine(Path.Combine(RootDir, "receipts"), "suites"); }
  }

  static string ScenariosDir {
    get { return Path.Combine(RootDir, "scenarios"); }
  }

  static string RequestReceiptsDir {
    get { return Path.Combine(Path.Combine(RootDir, "receipts"), "requests"); }
  }

  public IEnumerator Prepare(MonoBehaviour host, string suiteId) {
    if (_preparing || _gallery.IsRunning
        || (_blueprints != null && _blueprints.IsRunning)
        || (_signatureHunt != null && _signatureHunt.IsRunning)) {
      ComfyQuestLab.Report("Another Quest Lab world mutation is running; wait before preparing a suite.");
      yield break;
    }
    if (_session != null && _session.State == "running") {
      ComfyQuestLab.Report("A suite is running. Export or reset it before preparing another.");
      yield break;
    }

    LabBatchSuite suite = LabBatchContract.FindSuite(suiteId);
    if (suite == null) {
      ComfyQuestLab.Report(UnknownSuite(suiteId));
      yield break;
    }
    if (suite.EvidenceKind == "synthetic-scenario") {
      LabScenarioDefinition scenario = LabScenarioCatalog.Find(suite.Id);
      string prepared = WriteScenarioArtifacts(
          scenario, out string questPath, out string manifestPath);
      if (prepared != null) {
        ComfyQuestLab.Report(prepared);
        yield break;
      }
      _preparedSuiteId = suite.Id;
      _preparedArtifactPath = manifestPath;
      _preparedQuestPath = questPath;
      ComfyQuestLab.Report("prepared " + suite.Id + " without changing the world or loading "
          + "creator quests. Exact schema-1 draft and scenario manifest: " + manifestPath
          + "\nquestlab_batch run " + suite.Id + " rehearses it through the shared evaluator.");
      yield break;
    }
    if (IsSynthetic(suite)) {
      _preparedSuiteId = suite.Id;
      _preparedArtifactPath = null;
      _preparedQuestPath = null;
      ComfyQuestLab.Report(suite.Id + " needs no world setup; questlab_batch run " + suite.Id
          + " executes the source-shared evaluator probe.");
      yield break;
    }
    _preparedSuiteId = null;
    _preparedArtifactPath = null;
    _preparedQuestPath = null;
    if (host == null || Player.m_localPlayer == null || ZNetScene.instance == null) {
      ComfyQuestLab.Report("not in a world yet — load a private world before preparing the live suite.");
      yield break;
    }

    _preparing = true;
    string write = WriteSuiteQuestFile(suite);
    if (write != null) {
      _preparing = false;
      ComfyQuestLab.Report(write);
      yield break;
    }
    string reload = LabQuestEngine.Reload();
    int armed = CountPreparedQuests(suite, out int duplicateIds);
    if (armed != suite.Expectations.Length || duplicateIds > 0) {
      _preparing = false;
      ComfyQuestLab.Report("suite quest preparation failed: " + armed + "/"
          + suite.Expectations.Length + " examples armed, " + duplicateIds
          + " duplicate id(s). The creator's other quest files were left untouched; see Quests.");
      yield break;
    }

    ComfyQuestLab.Report("Resetting marked Gallery objects and raising one fresh compact course "
        + "with targets, tools, fuel, building materials, and food staged in place.");
    IEnumerator reset = _gallery.ResetSite(host, LabGalleryPlan.DefaultProfileId);
    while (true) {
      bool moved;
      object current = null;
      try {
        moved = reset.MoveNext();
        if (moved) current = reset.Current;
      } catch (Exception ex) {
        _preparing = false;
        ComfyQuestLab.Report("gallery preparation failed: " + ex.Message);
        yield break;
      }
      if (!moved) break;
      yield return current;
    }
    if (!_gallery.LastLifecycleSucceeded || _gallery.StandingPieceCount() == 0) {
      _preparing = false;
      ComfyQuestLab.Report("gallery preparation did not produce a verified fresh course.");
      yield break;
    }

    _preparedSuiteId = suite.Id;
    _preparing = false;
    ComfyQuestLab.Report("prepared " + suite.Id + ": " + reload
        + " Fresh compact course verified.\n"
        + Instructions(suite));
  }

  public string Run(string suiteId) {
    if (_preparing || _gallery.IsRunning) {
      return "gallery preparation/build is still running — wait before starting evidence capture.";
    }
    if (_session != null && _session.State == "running") {
      return "suite " + _session.RunId + " is already running; report, export, or reset it.";
    }

    LabBatchSuite suite = LabBatchContract.FindSuite(suiteId);
    if (suite == null) {
      return UnknownSuite(suiteId);
    }

    string now = NowUtc();
    string runId = NextRunId(suite.Id);
    _lastAutoExportState = null;
    if (IsSynthetic(suite)) {
      LabScenarioDefinition scenario = LabScenarioCatalog.Find(suite.Id);
      _session = scenario == null
          ? LabBatchContract.RunCreatorEventContract(runId, now)
          : LabScenarioCatalog.Run(scenario, runId, now);
      if (scenario == null) {
        _preparedSuiteId = suite.Id;
      } else if (!string.Equals(
          _preparedSuiteId, suite.Id, StringComparison.OrdinalIgnoreCase)) {
        // A direct run is allowed, but it did not create a draft. Do not let an earlier
        // scenario's path masquerade as this one's prepared creator handoff.
        _preparedSuiteId = null;
        _preparedArtifactPath = null;
        _preparedQuestPath = null;
      }
      string exported = SaveReceipt();
      return _session.Summary() + "\n" + exported;
    }

    if (Player.m_localPlayer == null || ZNetScene.instance == null) {
      return "not in a world yet — load a private world before running live evidence.";
    }
    if (_gallery.StandingPieceCount() == 0) {
      return "no gallery is standing here. Run questlab_batch prepare " + suite.Id
          + " first; it raises the default practice ground in one batch.";
    }
    if (LabConfig.QuestsEnabled == null || !LabConfig.QuestsEnabled.Value) {
      return "questsEnabled is false. Turn the quest lane on before starting a completion suite.";
    }

    string write = WriteSuiteQuestFile(suite);
    if (write != null) {
      return write;
    }
    LabQuestEngine.Reload();
    int armed = CountPreparedQuests(suite, out int duplicateIds);
    if (armed != suite.Expectations.Length || duplicateIds > 0) {
      return "cannot start: " + armed + "/" + suite.Expectations.Length
          + " suite examples are armed and " + duplicateIds + " duplicate id(s) exist. "
          + "The creator's other files were not changed; see the Quests tab.";
    }

    LabEventRouter.BeginBatchCapture();
    if (ComfyQuestLab.Ring != null) {
      ComfyQuestLab.Ring.Clear();
    }
    // Volatile zero-cooldown evaluator: it never writes config, and reset/reload restores the
    // creator's setting. This is what lets the receipt expose a failed local/RPC dedupe instead
    // of having the ordinary 60-second cooldown hide it.
    LabQuestEngine.BeginBatchCapture();
    _session = new LabBatchSession(suite, runId, now);
    _preparedSuiteId = suite.Id;
    _lastExportPath = null;
    return "running " + runId + " with volatile zero cooldown. " + Instructions(suite)
        + " Use questlab_batch report at any point; a passing run exports automatically.";
  }

  public string Reset() {
    string preserved = null;
    if (_session != null) {
      preserved = SaveReceipt();
    }
    _session = null;
    _lastAutoExportState = null;
    LabEventRouter.Reset();
    if (ComfyQuestLab.Ring != null) {
      ComfyQuestLab.Ring.Clear();
    }
    string reload = LabQuestEngine.Reload();
    return "batch evidence reset; prepared quest/gallery assets remain. " + reload
        + (string.IsNullOrWhiteSpace(preserved) ? string.Empty : "\n" + preserved);
  }

  public string Report() {
    if (_session == null) {
      return "no active batch run. "
          + (string.IsNullOrWhiteSpace(_preparedSuiteId)
              ? "questlab_batch suites lists the bounded choices."
              : _preparedSuiteId + " is prepared; run it when ready.")
          + (string.IsNullOrWhiteSpace(_lastExportPath)
              ? string.Empty
              : " Last receipt: " + _lastExportPath);
    }
    return _session.Summary()
        + (string.IsNullOrWhiteSpace(_lastExportPath)
            ? string.Empty
            : "\nreceipt: " + _lastExportPath);
  }

  public string Export() {
    if (_session == null) {
      return "no batch run to export.";
    }
    return SaveReceipt();
  }

  public void Observe(
      string school,
      string eventName,
      string signatureId,
      string target,
      string actionKey,
      bool firstCreatorWitness,
      bool evaluated) {
    if (_session == null || _session.Suite.EvidenceKind != "live-gameplay") {
      return;
    }
    _session.Observe(
        school, eventName, signatureId, target, actionKey, firstCreatorWitness, evaluated,
        "gameplay", NowUtc());
    ExportTerminalState();
  }

  public void ObserveCompletion(
      string questId, string eventName, string actionKey) {
    if (_session == null || _session.Suite.EvidenceKind != "live-gameplay") {
      return;
    }
    _session.Complete(questId, eventName, actionKey, "gameplay", NowUtc());
    ExportTerminalState();
  }

  void ExportTerminalState() {
    if (_session == null
        || (_session.State != "complete" && _session.State != "failed")
        || string.Equals(_lastAutoExportState, _session.State, StringComparison.Ordinal)) {
      return;
    }
    _lastAutoExportState = _session.State;
    string receipt = SaveReceipt();
    ComfyQuestLab.Report(_session.Summary() + "\n" + receipt);
  }

  string SaveReceipt() {
    try {
      Directory.CreateDirectory(SuiteReceiptsDir);
      string path = Path.Combine(SuiteReceiptsDir, _session.RunId + ".json");
      string json = _session.ToJson(new LabBatchReceiptContext {
        Machine = Environment.MachineName,
        PluginVersion = ComfyQuestLab.PluginVersion,
        ReleaseId = ComfyQuestLab.ReleaseId,
        RuntimeProfile = _session.Suite.EvidenceKind == "live-gameplay"
            ? LabRuntimeProfile.Extended + " (volatile batch override)"
            : _session.Suite.EvidenceKind.Replace('-', ' '),
        GeneratedUtc = NowUtc(),
      });
      WriteAtomic(path, json);
      _lastExportPath = path;
      return "suite receipt exported: " + path;
    } catch (Exception ex) {
      return "could not export suite receipt: " + ex.Message;
    }
  }

  static string WriteSuiteQuestFile(LabBatchSuite suite) {
    if (suite == null || suite.EvidenceKind != "live-gameplay") {
      return null;
    }
    try {
      Directory.CreateDirectory(Path.GetDirectoryName(SuiteQuestPath));
      // This exact, namespaced file is owned by the batch lane. No creator-authored file is
      // enumerated, edited, moved, or deleted.
      WriteAtomic(SuiteQuestPath, LabBatchContract.BuildQuestView(suite));
      return null;
    } catch (Exception ex) {
      return "could not write the owned suite quest file: " + ex.Message;
    }
  }

  static string WriteScenarioArtifacts(
      LabScenarioDefinition scenario, out string questPath, out string manifestPath) {
    questPath = null;
    manifestPath = null;
    if (scenario == null) return "scenario is not allowlisted.";
    try {
      string directory = Path.Combine(ScenariosDir, scenario.Id);
      Directory.CreateDirectory(directory);
      questPath = Path.Combine(directory, "quest-view.json");
      manifestPath = Path.Combine(directory, "scenario.json");
      string questView = scenario.BuildQuestView();
      string manifest = scenario.BuildManifest(questView);

      // Generated paths are Lab-owned, but a creator may reasonably open the draft in place.
      // Never erase that experiment: identical output is reusable; changed output is a clear,
      // bounded refusal with the exact file named.
      string conflict = ExistingContentConflict(questPath, questView)
          ?? ExistingContentConflict(manifestPath, manifest);
      if (conflict != null) {
        questPath = null;
        manifestPath = null;
        return conflict;
      }
      WriteIfMissing(questPath, questView);
      WriteIfMissing(manifestPath, manifest);
      return null;
    } catch (Exception ex) {
      questPath = null;
      manifestPath = null;
      return "could not prepare the owned scenario artifacts: " + ex.Message;
    }
  }

  static string ExistingContentConflict(string path, string expected) {
    if (!File.Exists(path)) return null;
    return string.Equals(File.ReadAllText(path, Encoding.UTF8), expected, StringComparison.Ordinal)
        ? null
        : "scenario prepare refused to overwrite a changed generated file: " + path
            + ". Copy or rename your edit, then remove only that generated file and prepare again.";
  }

  static void WriteIfMissing(string path, string content) {
    if (File.Exists(path)) return;
    string temporary = path + ".tmp";
    File.WriteAllText(temporary, content, new UTF8Encoding(false));
    File.Move(temporary, path);
  }

  static int CountPreparedQuests(LabBatchSuite suite, out int duplicates) {
    int armed = 0;
    duplicates = 0;
    foreach (LabBatchExpectation expected in suite.Expectations) {
      int matches = 0;
      bool matchArmed = false;
      foreach (LabQuest quest in LabQuestEngine.Set.Quests) {
        if (string.Equals(quest.QuestId, expected.QuestId, StringComparison.OrdinalIgnoreCase)) {
          matches++;
          matchArmed |= quest.IsArmed;
        }
      }
      if (matches > 1) duplicates += matches - 1;
      if (matches == 1 && matchArmed) armed++;
    }
    return armed;
  }

  static string Instructions(LabBatchSuite suite) {
    var sb = new StringBuilder();
    for (int i = 0; i < suite.Expectations.Length; i++) {
      LabBatchExpectation expected = suite.Expectations[i];
      if (i > 0) sb.Append("  ");
      sb.Append(expected.School).Append(": ").Append(expected.Instruction);
    }
    return sb.ToString();
  }

  static string UnknownSuite(string suiteId) {
    return "unknown batch suite '" + (suiteId ?? string.Empty)
        + "'. Use all-schools, creator-events, or an exact scenario-<event> from the panel.";
  }

  static bool IsSynthetic(LabBatchSuite suite) {
    return suite != null && (suite.EvidenceKind ?? string.Empty).StartsWith(
        "synthetic-", StringComparison.Ordinal);
  }

  string NextRunId(string suiteId) {
    _runSequence++;
    return suiteId + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
        + "-" + _runSequence.ToString("00", CultureInfo.InvariantCulture);
  }

  static string NowUtc() {
    return DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
  }

  static void WriteAtomic(string path, string content) {
    string temporary = path + ".tmp";
    File.WriteAllText(temporary, content, new UTF8Encoding(false));
    if (File.Exists(path)) {
      File.Delete(path);
    }
    File.Move(temporary, path);
  }

  // ---- bounded request mailbox ------------------------------------------------------

  public void Poll(MonoBehaviour host) {
    float now = Time.realtimeSinceStartup;
    if (now < _nextRequestPoll) {
      return;
    }
    _nextRequestPoll = now + RequestPollSeconds;
    try {
      if (!File.Exists(RequestPath)) {
        return;
      }
      FileInfo info = new FileInfo(RequestPath);
      // Let the SHA-verified deploy finish before reading its final path.
      if (DateTime.UtcNow - info.LastWriteTimeUtc < TimeSpan.FromMilliseconds(500)) {
        return;
      }

      LabBatchRequest request;
      string error;
      bool valid = TryReadRequest(info, out request, out error);
      ConsumeRequest();
      if (!valid) {
        if (request != null && IsSafeToken(request.request_id, 80)) {
          WriteRequestReceipt(request, "rejected", error);
        }
        ComfyQuestLab.LogInfo("[batch-request] rejected: " + error);
        return;
      }
      Dispatch(host, request);
    } catch (Exception ex) {
      ComfyQuestLab.LogInfo("[batch-request] poll failed: " + ex.Message);
    }
  }

  void Dispatch(MonoBehaviour host, LabBatchRequest request) {
    string operation = request.operation.ToLowerInvariant();
    string identityError = CreatorIdentityError(request);
    if (identityError != null) {
      WriteRequestReceipt(request, "rejected", identityError);
      return;
    }
    if (LabSignatureHuntContract.IsOperation(operation)) {
      DispatchSignatureHunt(host, request, operation);
      return;
    }
    if (operation.StartsWith("blueprint_", StringComparison.Ordinal)) {
      DispatchBlueprint(host, request, operation);
      return;
    }
    if (operation == "prepare") {
      if (_preparing || _gallery.IsRunning
          || (_blueprints != null && _blueprints.IsRunning)
          || (_signatureHunt != null && _signatureHunt.IsRunning)) {
        WriteRequestReceipt(request, "rejected", "world_mutation_busy");
        return;
      }
      host.StartCoroutine(RequestRoutine(
          request,
          Prepare(host, request.suite),
          () => string.Equals(_preparedSuiteId, request.suite, StringComparison.OrdinalIgnoreCase)
              ? "prepared " + request.suite
              : "prepare did not reach ready state",
          () => string.Equals(_preparedSuiteId, request.suite, StringComparison.OrdinalIgnoreCase)));
      return;
    }
    if (operation == "run") {
      string previousRunId = _session == null ? null : _session.RunId;
      string detail = Run(request.suite);
      bool started = _session != null
          && !string.Equals(_session.RunId, previousRunId, StringComparison.Ordinal);
      string state = !started
          ? "rejected"
          : (_session.State == "running" ? "accepted" : "completed");
      WriteRequestReceipt(request, state, detail);
      return;
    }
    if (operation == "reset") {
      WriteRequestReceipt(request, "completed", Reset());
      return;
    }
    if (operation == "reload") {
      string detail = LabQuestEngine.Reload();
      WriteRequestReceipt(request, "completed", detail);
      return;
    }
    if (operation == "report") {
      WriteRequestReceipt(request, "completed", Report());
      return;
    }
    if (operation == "export") {
      string detail = Export();
      WriteRequestReceipt(request, _session == null ? "rejected" : "completed", detail);
      return;
    }
    if (operation == "gallery_identify") {
      WriteRequestReceipt(request, "completed", _gallery.Identify());
      return;
    }
    if (operation == "gallery_evidence") {
      LabTruthLens.CaptureResult result = LabTruthLens.Capture(
          request.selector, request.request_id);
      WriteRequestReceipt(
          request,
          result.Succeeded ? "completed" : "failed",
          result.Summary,
          result.Path);
      return;
    }
    if (operation == "gallery_clear") {
      if (_gallery.IsRunning || _preparing
          || (_blueprints != null && _blueprints.IsRunning)
          || (_signatureHunt != null && _signatureHunt.IsRunning)) {
        WriteRequestReceipt(request, "rejected", "world_mutation_busy");
        return;
      }
      host.StartCoroutine(RequestRoutine(
          request,
          _gallery.ClearSafely(request.selector),
          () => _gallery.LastLifecycleResult,
          () => _gallery.LastLifecycleSucceeded
              && _gallery.StandingPieceCount(request.selector) == 0));
      return;
    }
    if (_gallery.IsRunning || _preparing
        || (_blueprints != null && _blueprints.IsRunning)
        || (_signatureHunt != null && _signatureHunt.IsRunning)) {
      WriteRequestReceipt(request, "rejected", "world_mutation_busy");
      return;
    }
    if (operation == "gallery_build") {
      int before = _gallery.StandingPieceCount(request.profile);
      host.StartCoroutine(RequestRoutine(
          request,
          _gallery.Build(host, request.profile),
          () => _gallery.Identify(),
          () => _gallery.StandingPieceCount(request.profile) > before));
      return;
    }
    if (operation == "gallery_rebuild") {
      host.StartCoroutine(RequestRoutine(
          request,
          _gallery.Rebuild(host, request.profile),
          () => _gallery.Identify(),
          () => _gallery.StandingPieceCount(request.profile) > 0));
      return;
    }
    if (operation == "gallery_compare") {
      host.StartCoroutine(RequestRoutine(
          request,
          _gallery.Compare(host, request.profile, request.compare_profile),
          () => _gallery.Identify(),
          () => _gallery.StandingPieceCount(request.profile) > 0
              && _gallery.StandingPieceCount(request.compare_profile) > 0));
    }
  }

  void DispatchSignatureHunt(
      MonoBehaviour host, LabBatchRequest request, string operation) {
    if (_signatureHunt == null) {
      WriteRequestReceipt(request, "rejected", "signature_hunt_surface_unavailable");
      return;
    }
    if (operation == LabSignatureHuntContract.StatusOperation) {
      WriteRequestReceipt(
          request,
          "completed",
          _signatureHunt.Status(),
          evidencePath: _signatureHunt.LastReceiptPath);
      return;
    }
    if (_gallery.IsRunning || _preparing
        || (_blueprints != null && _blueprints.IsRunning)
        || _signatureHunt.IsRunning) {
      WriteRequestReceipt(request, "rejected", "world_mutation_busy");
      return;
    }
    if (operation == LabSignatureHuntContract.PrepareOperation) {
      string priorReceiptPath = _signatureHunt.LastReceiptPath;
      host.StartCoroutine(RequestRoutine(
          request,
          _signatureHunt.Prepare(request.request_id),
          () => _signatureHunt.LastResult,
          () => _signatureHunt.LastLifecycleSucceeded
              && _signatureHunt.StandingObjectCount()
                  == LabSignatureHuntContract.Placements.Length,
          () => _signatureHunt.LastLifecycleSucceeded
                  && !string.Equals(_signatureHunt.LastReceiptPath, priorReceiptPath,
                      StringComparison.Ordinal)
              ? _signatureHunt.LastReceiptPath
              : null));
      return;
    }
    if (operation == LabSignatureHuntContract.ClearOperation) {
      host.StartCoroutine(RequestRoutine(
          request,
          _signatureHunt.Clear(),
          () => _signatureHunt.LastResult,
          () => _signatureHunt.LastLifecycleSucceeded
              && _signatureHunt.StandingObjectCount() == 0));
    }
  }

  void DispatchBlueprint(MonoBehaviour host, LabBatchRequest request, string operation) {
    if (_blueprints == null) {
      WriteRequestReceipt(request, "rejected", "blueprint_surface_unavailable");
      return;
    }
    if (_gallery.IsRunning || _preparing || _blueprints.IsRunning
        || (_signatureHunt != null && _signatureHunt.IsRunning)) {
      WriteRequestReceipt(request, "rejected", "world_mutation_busy");
      return;
    }
    string capturePath = LabBlueprintBuilder.CaptureArtifactPath(request.blueprint_name);
    string blueprintPath = LabBlueprintBuilder.BlueprintArtifactPath(request.blueprint_name);
    if (operation == "blueprint_capture") {
      string detail = _blueprints.Capture(
          request.blueprint_name, request.radius_metres, request.selection, request.replace);
      bool captured = File.Exists(capturePath) && File.Exists(blueprintPath)
          && detail.StartsWith("captured ", StringComparison.Ordinal);
      WriteRequestReceipt(request, captured ? "completed" : "failed", detail,
          artifactPath: captured ? capturePath : null,
          blueprintPath: captured ? blueprintPath : null);
      return;
    }
    if (operation == "blueprint_inspect") {
      string detail = _blueprints.Inspect(request.blueprint_name);
      WriteRequestReceipt(request,
          detail.StartsWith("capture ", StringComparison.Ordinal) ? "completed" : "failed",
          detail, artifactPath: File.Exists(capturePath) ? capturePath : null,
          blueprintPath: File.Exists(blueprintPath) ? blueprintPath : null);
      return;
    }
    if (operation == "blueprint_diff") {
      bool diffAt = string.Equals(request.build_mode, "at", StringComparison.Ordinal);
      string detail = diffAt
          ? _blueprints.DiffAt(request.blueprint_name, request.radius_metres,
              request.selection, ParseInvariantFloat(request.world_x),
              ParseInvariantFloat(request.world_y), ParseInvariantFloat(request.world_z),
              ParseInvariantFloat(request.yaw_degrees))
          : _blueprints.Diff(
              request.blueprint_name, request.radius_metres, request.selection);
      WriteRequestReceipt(request,
          detail.StartsWith("capture diff ", StringComparison.Ordinal) ? "completed" : "failed",
          detail, artifactPath: File.Exists(capturePath) ? capturePath : null);
      return;
    }
    if (operation == "blueprint_check") {
      string detail = _blueprints.Check(request.blueprint_name);
      WriteRequestReceipt(request,
          detail.StartsWith("blueprint check ", StringComparison.Ordinal)
              ? "completed" : "failed",
          detail, artifactPath: File.Exists(capturePath) ? capturePath : null,
          blueprintPath: File.Exists(blueprintPath) ? blueprintPath : null);
      return;
    }
    if (operation == "blueprint_count") {
      WriteRequestReceipt(request, "completed", _blueprints.Count(request.blueprint_name));
      return;
    }
    if (operation == "blueprint_clear") {
      string detail = null;
      host.StartCoroutine(RequestRoutine(
          request,
          ClearBlueprintAndAwaitRemoval(request.blueprint_name, value => detail = value),
          () => detail,
          () => BlueprintClearAccepted(detail)
              && _blueprints.StandingPieceCount(request.blueprint_name) == 0));
      return;
    }
    if (operation == "blueprint_build") {
      string check = _blueprints.Check(request.blueprint_name);
      if (!check.Contains("Ready. questlab_blueprint build ")) {
        WriteRequestReceipt(request, "rejected", check,
            artifactPath: File.Exists(capturePath) ? capturePath : null,
            blueprintPath: File.Exists(blueprintPath) ? blueprintPath : null);
        return;
      }
      int before = _blueprints.StandingPieceCount(request.blueprint_name);
      bool buildAt = string.Equals(request.build_mode, "at", StringComparison.Ordinal);
      IEnumerator work = buildAt
          ? _blueprints.BuildAt(host, request.blueprint_name,
              ParseInvariantFloat(request.world_x), ParseInvariantFloat(request.world_y),
              ParseInvariantFloat(request.world_z), ParseInvariantFloat(request.yaw_degrees))
          : _blueprints.Build(host, request.blueprint_name,
              string.Equals(request.build_mode, "sky", StringComparison.Ordinal));
      host.StartCoroutine(RequestRoutine(
          request,
          work,
          () => buildAt ? _blueprints.LastBuildResult : _blueprints.Count(request.blueprint_name),
          () => _blueprints.StandingPieceCount(request.blueprint_name) > before));
      return;
    }
    WriteRequestReceipt(request, "rejected", "operation_not_allowlisted");
  }

  IEnumerator ClearBlueprintAndAwaitRemoval(string blueprintName, Action<string> detail) {
    detail(_blueprints.Clear(blueprintName));
    // ZNetScene.Destroy removes the object immediately but Valheim can retire its ZDO on a
    // later frame. The installed AM4 lap observed all twelve pieces removed while the
    // same-frame count still read non-zero and emitted a false failed receipt. Wait only for
    // the bounded authoritative predicate; RequestRoutine still fails closed after the cap.
    const int maxRetirementFrames = 120;
    for (int frame = 0;
         frame < maxRetirementFrames
             && _blueprints.StandingPieceCount(blueprintName) > 0;
         frame++) {
      yield return null;
    }
  }

  static bool BlueprintClearAccepted(string detail) {
    return !string.IsNullOrWhiteSpace(detail)
        && (detail.StartsWith("cleared ", StringComparison.Ordinal)
            || detail.StartsWith("no pieces of \"", StringComparison.Ordinal));
  }

  static string CreatorIdentityError(LabBatchRequest request) {
    if (string.IsNullOrWhiteSpace(request.creator_session_id)) return null;
    if (!string.Equals(request.expected_machine, Environment.MachineName,
        StringComparison.OrdinalIgnoreCase)) return "creator_machine_mismatch";
    if (ZNet.instance == null || Player.m_localPlayer == null) return "creator_world_not_loaded";
    string actual;
    try {
      actual = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);
    } catch {
      return "creator_world_unreadable";
    }
    return string.Equals(actual, request.expected_world_uid, StringComparison.Ordinal)
        ? null : "creator_world_mismatch";
  }

  IEnumerator RequestRoutine(
      LabBatchRequest request,
      IEnumerator work,
      Func<string> detail,
      Func<bool> success = null,
      Func<string> evidencePath = null) {
    string failure = null;
    while (true) {
      bool moved;
      object current = null;
      try {
        moved = work.MoveNext();
        if (moved) current = work.Current;
      } catch (Exception ex) {
        moved = false;
        failure = ex.GetType().Name + ": " + ex.Message;
      }
      if (!moved) break;
      yield return current;
    }
    bool completed = failure == null && (success == null || success());
    WriteRequestReceipt(
        request,
        completed ? "completed" : "failed",
        failure ?? (detail == null ? string.Empty : detail()),
        evidencePath: evidencePath == null ? null : evidencePath());
  }

  static bool TryReadRequest(
      FileInfo info, out LabBatchRequest request, out string error) {
    request = null;
    error = string.Empty;
    try {
      if (info.Length <= 0 || info.Length > MaxRequestBytes) {
        error = "request_size_invalid";
        return false;
      }
      request = JsonUtility.FromJson<LabBatchRequest>(File.ReadAllText(info.FullName));
      if (request == null || request.schema != LabBatchContract.RequestSchema) {
        error = "request_schema_invalid";
        return false;
      }
      if (!IsSafeToken(request.request_id, 80)) {
        error = "request_id_invalid";
        return false;
      }
      if (!DateTimeOffset.TryParse(
              request.expires_utc ?? string.Empty,
              CultureInfo.InvariantCulture,
              DateTimeStyles.RoundtripKind,
              out DateTimeOffset expires)) {
        error = "request_expiry_invalid";
        return false;
      }
      DateTimeOffset now = DateTimeOffset.UtcNow;
      if (!DateTimeOffset.TryParse(
              request.created_utc ?? string.Empty,
              CultureInfo.InvariantCulture,
              DateTimeStyles.RoundtripKind,
              out DateTimeOffset created)
          || created < now.AddMinutes(-30)
          || created > now.AddMinutes(1)
          || expires <= created) {
        error = "request_created_invalid";
        return false;
      }
      if (expires <= now || expires > now.AddMinutes(30)) {
        error = expires <= now ? "request_expired" : "request_expiry_too_far";
        return false;
      }
      if (!ValidRequestArguments(request, out error)) {
        return false;
      }
      return true;
    } catch (Exception ex) {
      error = "request_read_failed:" + ex.GetType().Name;
      return false;
    }
  }

  static bool ValidRequestArguments(LabBatchRequest request, out string error) {
    if (!LabBatchRequestPolicy.ValidateCreatorIdentity(
            request.expected_machine, request.expected_world_uid,
            request.creator_session_id, out error)) {
      return false;
    }
    if (string.Equals(request.operation, "history_step", StringComparison.OrdinalIgnoreCase)) {
      return LabBatchRequestPolicy.ValidateHistory(request.corpus, request.step,
          request.expected_previous_step, request.suite, request.profile,
          request.compare_profile, request.selector, out error);
    }
    if ((request.operation ?? string.Empty).StartsWith(
            "blueprint_", StringComparison.OrdinalIgnoreCase)) {
      if (!string.IsNullOrWhiteSpace(request.suite)
          || !string.IsNullOrWhiteSpace(request.profile)
          || !string.IsNullOrWhiteSpace(request.compare_profile)
          || !string.IsNullOrWhiteSpace(request.selector)) {
        error = "request_argument_not_allowed";
        return false;
      }
      return LabBatchRequestPolicy.ValidateBlueprint(
          request.operation, request.blueprint_name, request.radius_metres,
          request.selection, request.replace, request.build_mode,
          request.world_x, request.world_y, request.world_z, request.yaw_degrees,
          out error);
    }
    if (!string.IsNullOrWhiteSpace(request.blueprint_name)
        || !string.IsNullOrWhiteSpace(request.radius_metres)
        || !string.IsNullOrWhiteSpace(request.selection)
        || request.replace
        || !string.IsNullOrWhiteSpace(request.build_mode)
        || !string.IsNullOrWhiteSpace(request.world_x)
        || !string.IsNullOrWhiteSpace(request.world_y)
        || !string.IsNullOrWhiteSpace(request.world_z)
        || !string.IsNullOrWhiteSpace(request.yaw_degrees)) {
      error = "request_argument_not_allowed";
      return false;
    }
    if (LabSignatureHuntContract.IsOperation(request.operation)) {
      return LabSignatureHuntContract.ValidateRequest(
          request.operation, request.suite, request.profile, request.compare_profile,
          request.selector, request.corpus, request.step, request.seed,
          request.expected_previous_step, out error);
    }
    return LabBatchRequestPolicy.Validate(
        request.operation,
        request.suite,
        request.profile,
        request.compare_profile,
        request.selector,
        out error);
  }

  static bool IsSafeToken(string value, int maxLength) {
    if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength) {
      return false;
    }
    foreach (char c in value) {
      if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.') {
        return false;
      }
    }
    return true;
  }

  static float ParseInvariantFloat(string value) {
    return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
  }

  static void ConsumeRequest() {
    try {
      if (File.Exists(RequestPath)) File.Delete(RequestPath);
    } catch (Exception ex) {
      ComfyQuestLab.LogInfo("[batch-request] cleanup failed: " + ex.Message);
    }
  }

  void WriteRequestReceipt(
      LabBatchRequest request, string state, string detail, string evidencePath = null,
      string artifactPath = null, string blueprintPath = null) {
    try {
      Directory.CreateDirectory(RequestReceiptsDir);
      string path = Path.Combine(RequestReceiptsDir, request.request_id + ".json");
      var sb = new StringBuilder();
      sb.AppendLine("{");
      sb.AppendLine("  \"schema\": \"" + RequestReceiptSchema + "\",");
      sb.AppendLine("  \"request_id\": \"" + LabBatchContract.Json(request.request_id) + "\",");
      sb.AppendLine("  \"operation\": \"" + LabBatchContract.Json(request.operation) + "\",");
      sb.AppendLine("  \"state\": \"" + LabBatchContract.Json(state) + "\",");
      sb.AppendLine("  \"machine\": \"" + LabBatchContract.Json(Environment.MachineName) + "\",");
      sb.AppendLine("  \"plugin_version\": \"" + ComfyQuestLab.PluginVersion + "\",");
      sb.AppendLine("  \"release_id\": \"" + ComfyQuestLab.ReleaseId + "\",");
      sb.AppendLine("  \"creator_session_id\": \""
          + LabBatchContract.Json(request.creator_session_id ?? string.Empty) + "\",");
      sb.AppendLine("  \"world_uid\": \"" + LabBatchContract.Json(CurrentWorldUid()) + "\",");
      sb.AppendLine("  \"completed_utc\": \"" + NowUtc() + "\",");
      sb.AppendLine("  \"detail\": \"" + LabBatchContract.Json(detail) + "\",");
      sb.AppendLine("  \"evidence_path\": \""
          + LabBatchContract.Json(evidencePath ?? string.Empty) + "\",");
      sb.AppendLine("  \"artifact_path\": \""
          + LabBatchContract.Json(artifactPath ?? string.Empty) + "\",");
      sb.AppendLine("  \"blueprint_path\": \""
          + LabBatchContract.Json(blueprintPath ?? string.Empty) + "\",");
      if ((string.Equals(request.operation, "blueprint_build", StringComparison.Ordinal)
              || string.Equals(request.operation, "blueprint_diff", StringComparison.Ordinal))
          && string.Equals(request.build_mode, "at", StringComparison.Ordinal)
          && TryCanonicalNumber(request.world_x, out string canonicalX)
          && TryCanonicalNumber(request.world_y, out string canonicalY)
          && TryCanonicalNumber(request.world_z, out string canonicalZ)
          && TryCanonicalNumber(request.yaw_degrees, out string canonicalYaw)) {
        sb.AppendLine("  \"placement\": {");
        sb.AppendLine("    \"x\": " + canonicalX + ",");
        sb.AppendLine("    \"y\": " + canonicalY + ",");
        sb.AppendLine("    \"z\": " + canonicalZ + ",");
        sb.AppendLine("    \"yaw_degrees\": " + canonicalYaw);
        sb.AppendLine("  },");
      }
      string suiteReceiptPath = RequestExposesSuiteReceipt(request.operation)
          ? _lastExportPath
          : string.Empty;
      sb.AppendLine("  \"suite_receipt_path\": \""
          + LabBatchContract.Json(suiteReceiptPath) + "\"");
      sb.AppendLine("}");
      WriteAtomic(path, sb.ToString());
      ComfyQuestLab.LogInfo("[batch-request] " + request.request_id + " " + state
          + " — " + request.operation);
    } catch (Exception ex) {
      ComfyQuestLab.LogInfo("[batch-request] receipt failed: " + ex.Message);
    }
  }

  static bool RequestExposesSuiteReceipt(string operation) {
    return operation == "run" || operation == "report" || operation == "export"
        || operation == "reset";
  }

  static bool TryCanonicalNumber(string value, out string canonical) {
    canonical = string.Empty;
    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
        out float number) || float.IsNaN(number) || float.IsInfinity(number)) {
      return false;
    }
    canonical = number.ToString("0.######", CultureInfo.InvariantCulture);
    return true;
  }

  static string CurrentWorldUid() {
    try {
      return ZNet.instance == null ? string.Empty
          : ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);
    } catch {
      return string.Empty;
    }
  }
}

[Serializable]
public sealed class LabBatchRequest {
  public string schema;
  public string request_id;
  public string operation;
  public string suite;
  public string profile;
  public string compare_profile;
  public string selector;
  public string blueprint_name;
  public string radius_metres;
  public string selection;
  public bool replace;
  public string build_mode;
  public string world_x;
  public string world_y;
  public string world_z;
  public string yaw_degrees;
  public string expected_machine;
  public string expected_world_uid;
  public string creator_session_id;
  public string created_utc;
  public string expires_utc;
  public string corpus;
  public int step;
  public int seed;
  public int expected_previous_step;
}

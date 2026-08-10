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
  readonly LabHistoryScenarioRunner _history;
  LabBatchSession _session;
  bool _preparing;
  string _preparedSuiteId;
  string _lastExportPath;
  string _lastAutoExportState;
  float _nextRequestPoll;
  int _runSequence;

  public LabBatchController(LabGalleryBuilder gallery) {
    _gallery = gallery ?? throw new ArgumentNullException(nameof(gallery));
    _history = new LabHistoryScenarioRunner();
  }

  public LabBatchSession Session { get { return _session; } }
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

  static string RequestReceiptsDir {
    get { return Path.Combine(Path.Combine(RootDir, "receipts"), "requests"); }
  }

  public IEnumerator Prepare(MonoBehaviour host, string suiteId) {
    if (_preparing || _gallery.IsRunning) {
      ComfyQuestLab.Report("Quest Lab preparation is already changing the gallery; wait for it.");
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
    if (suite.EvidenceKind == "synthetic-contract") {
      _preparedSuiteId = suite.Id;
      ComfyQuestLab.Report(suite.Id + " needs no world setup; questlab_batch run " + suite.Id
          + " executes the source-shared evaluator probe.");
      yield break;
    }
    _preparedSuiteId = null;
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
    if (suite.EvidenceKind == "synthetic-contract") {
      _session = LabBatchContract.RunCreatorEventContract(runId, now);
      _preparedSuiteId = suite.Id;
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
            : "synthetic contract",
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
        + "'. One of: all-schools, creator-events.";
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
    if (operation == "prepare") {
      if (_preparing || _gallery.IsRunning) {
        WriteRequestReceipt(request, "rejected", "gallery_or_prepare_busy");
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
    if (operation == "history_step") {
      host.StartCoroutine(_history.Run(host, request.corpus, request.step, request.seed,
          request.expected_previous_step,
          detail => WriteRequestReceipt(request, detail == "completed" ? "completed" : "failed", detail)));
      return;
    }
    if (operation == "gallery_clear") {
      if (_gallery.IsRunning || _preparing) {
        WriteRequestReceipt(request, "rejected", "gallery_or_prepare_busy");
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
    if (_gallery.IsRunning || _preparing) {
      WriteRequestReceipt(request, "rejected", "gallery_or_prepare_busy");
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

  IEnumerator RequestRoutine(
      LabBatchRequest request,
      IEnumerator work,
      Func<string> detail,
      Func<bool> success = null) {
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
        failure ?? (detail == null ? string.Empty : detail()));
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
    if (string.Equals(request.operation, "history_step", StringComparison.OrdinalIgnoreCase)) {
      return LabBatchRequestPolicy.ValidateHistory(request.corpus, request.step,
          request.expected_previous_step, request.suite, request.profile,
          request.compare_profile, request.selector, out error);
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

  static void ConsumeRequest() {
    try {
      if (File.Exists(RequestPath)) File.Delete(RequestPath);
    } catch (Exception ex) {
      ComfyQuestLab.LogInfo("[batch-request] cleanup failed: " + ex.Message);
    }
  }

  void WriteRequestReceipt(LabBatchRequest request, string state, string detail) {
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
      sb.AppendLine("  \"completed_utc\": \"" + NowUtc() + "\",");
      sb.AppendLine("  \"detail\": \"" + LabBatchContract.Json(detail) + "\",");
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
  public string created_utc;
  public string expires_utc;
  public string corpus;
  public int step;
  public int seed;
  public int expected_previous_step;
}

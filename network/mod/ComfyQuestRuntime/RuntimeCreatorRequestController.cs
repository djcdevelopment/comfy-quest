namespace ComfyQuestRuntime;

using System;
using System.IO;
using ComfyQuestContracts;
using Newtonsoft.Json;

/// <summary>Filesystem half of the Runtime Creator Session mailbox. One fixed request
/// file, one fixed receipt directory, one expiring allowlist. World identity and safety
/// are checked inside the game process before the only mutation (dev-channel arm).</summary>
public sealed class RuntimeCreatorRequestController {
  const int MaxRequestBytes = 4096;
  const double PollSeconds = .5;

  readonly string requestPath;
  readonly string receiptDirectory;
  readonly RuntimeDevChannelCoordinator devChannel;
  readonly RuntimeDevChannelStatusStore statusStore;
  readonly Func<bool> privateConfirmed;
  readonly Func<bool> worldLoaded;
  readonly Func<string> worldUid;
  readonly Action<string> log;
  double nextPoll;

  public RuntimeCreatorRequestController(
      string runtimeRoot,
      RuntimeDevChannelCoordinator coordinator,
      Func<bool> privateWorldConfirmed,
      Func<bool> isWorldLoaded,
      Func<string> currentWorldUid,
      Action<string> logger = null) {
    string root = Path.GetFullPath(runtimeRoot ?? throw new ArgumentNullException(nameof(runtimeRoot)));
    devChannel = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    privateConfirmed = privateWorldConfirmed ?? (() => false);
    worldLoaded = isWorldLoaded ?? (() => false);
    worldUid = currentWorldUid ?? (() => string.Empty);
    log = logger ?? (_ => { });
    requestPath = Path.Combine(root, "requests", "creator-request.json");
    receiptDirectory = Path.Combine(root, "receipts", "creator-requests");
    statusStore = new RuntimeDevChannelStatusStore(root);
  }

  public void Poll(double realtimeSeconds, string currentStageId) {
    if (realtimeSeconds < nextPoll) return;
    nextPoll = realtimeSeconds + PollSeconds;
    if (!File.Exists(requestPath)) return;
    RuntimeCreatorRequest request = null;
    string error = null;
    try {
      var info = new FileInfo(requestPath);
      if (DateTime.UtcNow - info.LastWriteTimeUtc < TimeSpan.FromMilliseconds(300)) return;
      if (info.Length <= 0 || info.Length > MaxRequestBytes) {
        error = "request_size_invalid";
      } else {
        request = JsonConvert.DeserializeObject<RuntimeCreatorRequest>(
            File.ReadAllText(requestPath));
        if (!RuntimeCreatorRequestPolicy.Validate(
                request, DateTimeOffset.UtcNow, out error)) request ??= null;
      }
    } catch (Exception exception) {
      error = "request_read_failed:" + exception.GetType().Name;
    }
    Consume();
    if (error != null) {
      if (request != null && SafeReceiptId(request.RequestId))
        Write(request, "rejected", error, currentStageId);
      log("[creator-request] rejected: " + error);
      return;
    }
    Dispatch(request, currentStageId);
  }

  void Dispatch(RuntimeCreatorRequest request, string currentStageId) {
    string actualWorld = worldLoaded() ? worldUid() : string.Empty;
    if (!string.Equals(request.ExpectedMachine, Environment.MachineName,
        StringComparison.OrdinalIgnoreCase)) {
      Write(request, "rejected", "creator_machine_mismatch", currentStageId);
      return;
    }
    if (string.IsNullOrWhiteSpace(actualWorld)) {
      Write(request, "rejected", "creator_world_not_loaded", currentStageId);
      return;
    }
    if (!string.Equals(request.ExpectedWorldUid, actualWorld, StringComparison.Ordinal)) {
      Write(request, "rejected", "creator_world_mismatch", currentStageId);
      return;
    }
    string operation = request.Operation.ToLowerInvariant();
    if (operation == "arm") {
      if (!privateConfirmed()) {
        Write(request, "rejected", "private_world_confirmation_required", currentStageId);
        return;
      }
      devChannel.Arm(DateTimeOffset.UtcNow);
      devChannel.Heartbeat(DateTimeOffset.UtcNow, currentStageId);
      Write(request, "completed", "dev_channel_armed", currentStageId);
      return;
    }
    if (operation == "disarm") {
      devChannel.Disarm(DateTimeOffset.UtcNow);
      Write(request, "completed", "dev_channel_disarmed", currentStageId);
      return;
    }
    if (operation == "status") {
      devChannel.Heartbeat(DateTimeOffset.UtcNow, currentStageId);
      Write(request, "completed", devChannel.Armed
          ? "dev_channel_armed" : "dev_channel_disarmed", currentStageId);
      return;
    }
    Write(request, "rejected", "operation_not_allowlisted", currentStageId);
  }

  void Write(
      RuntimeCreatorRequest request, string state, string detail, string currentStageId) {
    try {
      RuntimeDevChannelStatus status = statusStore.Read();
      var receipt = new RuntimeCreatorRequestReceipt {
        RequestId = request.RequestId,
        Operation = request.Operation,
        State = state,
        Detail = detail,
        Machine = Environment.MachineName,
        WorldUid = worldLoaded() ? worldUid() : string.Empty,
        CreatorSessionId = request.CreatorSessionId,
        CompletedUtc = DateTimeOffset.UtcNow,
        DevSessionId = status?.SessionId ?? devChannel.SessionId,
        DevArmed = status?.Armed ?? devChannel.Armed,
        DevState = status?.State,
        ActivePackId = status?.ActivePackId,
        ActiveVersion = status?.ActiveVersion,
        ActiveContentHash = status?.ActiveContentHash,
        ActiveActivationId = status?.ActiveActivationId,
        CurrentStageId = status?.CurrentStageId ?? currentStageId,
      };
      Directory.CreateDirectory(receiptDirectory);
      string path = Path.Combine(receiptDirectory, request.RequestId + ".json");
      string temp = path + ".tmp";
      File.WriteAllText(temp, JsonConvert.SerializeObject(receipt, Formatting.Indented));
      if (File.Exists(path)) File.Replace(temp, path, null);
      else File.Move(temp, path);
      log("[creator-request] " + request.RequestId + " " + state
          + " - " + request.Operation);
    } catch (Exception exception) {
      log("[creator-request] receipt failed: " + exception.Message);
    }
  }

  void Consume() {
    try {
      if (File.Exists(requestPath)) File.Delete(requestPath);
    } catch (Exception exception) {
      log("[creator-request] cleanup failed: " + exception.Message);
    }
  }

  static bool SafeReceiptId(string value) {
    if (string.IsNullOrWhiteSpace(value) || value.Length > 80) return false;
    foreach (char c in value)
      if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.') return false;
    return true;
  }
}

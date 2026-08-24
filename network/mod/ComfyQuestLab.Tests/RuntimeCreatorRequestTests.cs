namespace ComfyNetworkSense.Tests;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using ComfyQuestContracts;
using ComfyQuestRuntime;
using Newtonsoft.Json;
using Xunit;

public sealed class RuntimeCreatorRequestTests {
  static RuntimeCreatorRequest Request(string operation = "status") => new() {
    Schema = RuntimeCreatorRequest.CurrentSchema,
    RequestId = "runtime-status-20260824-abcd1234",
    Operation = operation,
    CreatedUtc = "2026-08-24T12:00:00.0000000+00:00",
    ExpiresUtc = "2026-08-24T12:10:00.0000000+00:00",
    ExpectedMachine = "OMEN",
    ExpectedWorldUid = "-7600395338659582326",
    CreatorSessionId = "creator-20260824-abcd1234",
  };

  [Theory]
  [InlineData("status")]
  [InlineData("arm")]
  [InlineData("disarm")]
  public void RuntimeCreatorVocabularyStopsAtSessionControl(string operation) {
    Assert.True(RuntimeCreatorRequestPolicy.Validate(
        Request(operation), DateTimeOffset.Parse("2026-08-24T12:00:01Z"), out string error),
        error);
    Assert.Null(error);
  }

  [Theory]
  [InlineData("cast")]
  [InlineData("load")]
  [InlineData("keypress")]
  [InlineData("console")]
  public void RuntimeCreatorVocabularyCannotActAsThePlayer(string operation) {
    Assert.False(RuntimeCreatorRequestPolicy.Validate(
        Request(operation), DateTimeOffset.Parse("2026-08-24T12:00:01Z"), out string error));
    Assert.Equal("operation_not_allowlisted", error);
  }

  [Fact]
  public void RuntimeCreatorRequestRequiresFreshCompleteIdentityPins() {
    RuntimeCreatorRequest partial = Request();
    partial.ExpectedWorldUid = null;
    Assert.False(RuntimeCreatorRequestPolicy.Validate(
        partial, DateTimeOffset.Parse("2026-08-24T12:00:01Z"), out string identityError));
    Assert.Equal("request_identity_invalid", identityError);

    RuntimeCreatorRequest expired = Request();
    Assert.False(RuntimeCreatorRequestPolicy.Validate(
        expired, DateTimeOffset.Parse("2026-08-24T12:11:00Z"), out string expiryError));
    Assert.Equal("request_expired", expiryError);

    RuntimeCreatorRequest far = Request();
    far.ExpiresUtc = "2026-08-24T13:00:00Z";
    Assert.False(RuntimeCreatorRequestPolicy.Validate(
        far, DateTimeOffset.Parse("2026-08-24T12:00:01Z"), out string farError));
    Assert.Equal("request_expiry_too_far", farError);
  }

  [Fact]
  public void ControllerConsumesRealMailboxFilesAcrossStatusArmAndDisarm() {
    string root = Path.Combine(Path.GetTempPath(), "comfy-runtime-request-" + Guid.NewGuid().ToString("N"));
    try {
      var coordinator = new RuntimeDevChannelCoordinator(root);
      var logs = new System.Collections.Generic.List<string>();
      var controller = new RuntimeCreatorRequestController(
          root, coordinator, () => true, () => true,
          () => "-7600395338659582326", logs.Add);

      RuntimeCreatorRequestReceipt status = Dispatch(
          root, controller, RequestNow("status", "request-status"), 1d);
      Assert.Equal("completed", status.State);
      Assert.Equal("dev_channel_disarmed", status.Detail);
      Assert.False(status.DevArmed);
      Assert.Equal("stage-one", status.CurrentStageId);

      RuntimeCreatorRequestReceipt armed = Dispatch(
          root, controller, RequestNow("arm", "request-arm"), 2d);
      Assert.Equal("completed", armed.State);
      Assert.Equal("dev_channel_armed", armed.Detail);
      Assert.True(armed.DevArmed);
      Assert.True(coordinator.Armed);

      RuntimeCreatorRequestReceipt watching = Dispatch(
          root, controller, RequestNow("status", "request-watching"), 3d);
      Assert.Equal("completed", watching.State);
      Assert.Equal("dev_channel_armed", watching.Detail);
      Assert.True(watching.DevArmed);

      RuntimeCreatorRequestReceipt disarmed = Dispatch(
          root, controller, RequestNow("disarm", "request-disarm"), 4d);
      Assert.Equal("completed", disarmed.State);
      Assert.Equal("dev_channel_disarmed", disarmed.Detail);
      Assert.False(disarmed.DevArmed);
      Assert.False(coordinator.Armed);
      Assert.Contains(logs, line => line.Contains("request-arm completed - arm"));
    } finally {
      if (Directory.Exists(root)) Directory.Delete(root, true);
    }
  }

  [Fact]
  public void ControllerReturnsEveryLivePreconditionDiagnosticInsteadOfBlankRejections() {
    AssertDispatchRejected(
        RequestNow("arm", "wrong-machine", expectedMachine: "definitely-not-this-machine"),
        privateConfirmed: true, worldLoaded: true, actualWorld: "-7600395338659582326",
        expected: "creator_machine_mismatch");
    AssertDispatchRejected(
        RequestNow("arm", "world-not-loaded"),
        privateConfirmed: true, worldLoaded: false, actualWorld: "-7600395338659582326",
        expected: "creator_world_not_loaded");
    AssertDispatchRejected(
        RequestNow("arm", "wrong-world", expectedWorld: "-42"),
        privateConfirmed: true, worldLoaded: true, actualWorld: "-7600395338659582326",
        expected: "creator_world_mismatch");
    AssertDispatchRejected(
        RequestNow("arm", "private-required"),
        privateConfirmed: false, worldLoaded: true, actualWorld: "-7600395338659582326",
        expected: "private_world_confirmation_required");

    RuntimeCreatorRequest expired = RequestNow("status", "expired-request");
    expired.CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-20).ToString("o");
    expired.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("o");
    AssertDispatchRejected(
        expired, privateConfirmed: true, worldLoaded: true,
        actualWorld: "-7600395338659582326", expected: "request_expired");
  }

  [Fact]
  public void CreatorSessionAndPowerShellSenderDriveTheShippingControllerEndToEnd() {
    string root = Path.Combine(Path.GetTempPath(), "comfy-creator-controller-" + Guid.NewGuid().ToString("N"));
    string evidence = Path.Combine(root, "evidence");
    string sessionId = "creator-controller-e2e";
    string repo = FindRepoRoot();
    string script = Path.Combine(repo, "tools", "creator-session", "Invoke-CreatorSession.ps1");
    try {
      Directory.CreateDirectory(Path.Combine(root, "fixture-source"));
      File.WriteAllText(Path.Combine(root, ".comfy-quest-creator-fixture"), "owned\n");
      foreach (string name in new[] {
          "ComfyQuestLab.dll", "ComfyQuestRuntime.dll",
          "ComfyQuestContracts.dll", "Newtonsoft.Json.dll" }) {
        File.WriteAllText(Path.Combine(root, "fixture-source", name), "fixture-" + name + "\n");
      }

      string[] common = {
        "-ValheimRoot", root,
        "-FixtureMode",
        "-ExpectedMachine", Environment.MachineName,
        "-WorldUid", "-7600395338659582326",
        "-SessionId", sessionId,
      };
      ProcessResult prepared = RunPowerShell(
          repo, script, new[] { "Prepare", "-NoBuild", "-EvidenceRoot", evidence }
              .Concat(common).ToArray());
      Assert.Equal(0, prepared.ExitCode);

      string runtimeRoot = Path.Combine(root, "BepInEx", "config", "comfy-quest-runtime");
      string config = Path.Combine(
          root, "BepInEx", "config", "djcdevelopment.valheim.comfyquestruntime.cfg");
      var coordinator = new RuntimeDevChannelCoordinator(runtimeRoot);
      var controller = new RuntimeCreatorRequestController(
          runtimeRoot, coordinator,
          () => File.ReadAllText(config).Contains("PrivateWorldConfirmed = true"),
          () => true, () => "-7600395338659582326");

      ProcessResult armed = RunPowerShell(
          repo, script, new[] { "Arm", "-WaitSeconds", "10" }.Concat(common).ToArray(),
          controller);
      Assert.Equal(0, armed.ExitCode);
      Assert.Contains("Runtime request state: completed", armed.Output);
      Assert.True(coordinator.Armed);

      ProcessResult disarmed = RunPowerShell(
          repo, script, new[] { "Disarm", "-WaitSeconds", "10" }.Concat(common).ToArray(),
          controller);
      Assert.Equal(0, disarmed.ExitCode);
      Assert.Contains("Runtime request state: completed", disarmed.Output);
      Assert.False(coordinator.Armed);

      ProcessResult closed = RunPowerShell(
          repo, script, new[] { "Close", "-Restore" }.Concat(common).ToArray());
      Assert.Equal(0, closed.ExitCode);
      Assert.False(File.Exists(Path.Combine(root, "BepInEx", "plugins", "ComfyQuestRuntime.dll")));
      Assert.False(File.Exists(config));
      Assert.True(File.Exists(Path.Combine(evidence, "session.closed.json")));
    } finally {
      if (Directory.Exists(root)) Directory.Delete(root, true);
    }
  }

  static RuntimeCreatorRequest RequestNow(
      string operation, string requestId, string expectedMachine = null,
      string expectedWorld = "-7600395338659582326") {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    return new RuntimeCreatorRequest {
      Schema = RuntimeCreatorRequest.CurrentSchema,
      RequestId = requestId,
      Operation = operation,
      CreatedUtc = now.ToString("o"),
      ExpiresUtc = now.AddMinutes(10).ToString("o"),
      ExpectedMachine = expectedMachine ?? Environment.MachineName,
      ExpectedWorldUid = expectedWorld,
      CreatorSessionId = "creator-controller-test",
    };
  }

  static RuntimeCreatorRequestReceipt Dispatch(
      string root, RuntimeCreatorRequestController controller,
      RuntimeCreatorRequest request, double realtime) {
    string requestDirectory = Path.Combine(root, "requests");
    Directory.CreateDirectory(requestDirectory);
    string path = Path.Combine(requestDirectory, "creator-request.json");
    File.WriteAllText(path, JsonConvert.SerializeObject(request));
    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-1));
    controller.Poll(realtime, "stage-one");
    Assert.False(File.Exists(path));
    string receiptPath = Path.Combine(
        root, "receipts", "creator-requests", request.RequestId + ".json");
    Assert.True(File.Exists(receiptPath), receiptPath);
    return JsonConvert.DeserializeObject<RuntimeCreatorRequestReceipt>(
        File.ReadAllText(receiptPath));
  }

  static void AssertDispatchRejected(
      RuntimeCreatorRequest request, bool privateConfirmed,
      bool worldLoaded, string actualWorld, string expected) {
    string root = Path.Combine(Path.GetTempPath(), "comfy-runtime-rejection-" + Guid.NewGuid().ToString("N"));
    try {
      var coordinator = new RuntimeDevChannelCoordinator(root);
      var controller = new RuntimeCreatorRequestController(
          root, coordinator, () => privateConfirmed, () => worldLoaded,
          () => actualWorld);
      RuntimeCreatorRequestReceipt receipt = Dispatch(root, controller, request, 1d);
      Assert.Equal("rejected", receipt.State);
      Assert.Equal(expected, receipt.Detail);
      Assert.False(string.IsNullOrWhiteSpace(receipt.Detail));
      Assert.False(coordinator.Armed);
    } finally {
      if (Directory.Exists(root)) Directory.Delete(root, true);
    }
  }

  static string FindRepoRoot() {
    DirectoryInfo directory = new(AppContext.BaseDirectory);
    while (directory != null) {
      if (File.Exists(Path.Combine(directory.FullName, "tools", "Assert-RepoIdentity.ps1")))
        return directory.FullName;
      directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("comfy-quest repository root was not found");
  }

  static ProcessResult RunPowerShell(
      string workingDirectory, string script, string[] arguments,
      RuntimeCreatorRequestController controller = null) {
    var start = new ProcessStartInfo("powershell.exe") {
      WorkingDirectory = workingDirectory,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
    };
    foreach (string value in new[] {
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(arguments))
      start.ArgumentList.Add(value);
    using Process process = Process.Start(start);
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    DateTime deadline = DateTime.UtcNow.AddSeconds(30);
    while (!process.HasExited && DateTime.UtcNow < deadline) {
      controller?.Poll(
          Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, "stage-one");
      Thread.Sleep(25);
    }
    if (!process.HasExited) {
      process.Kill(true);
      throw new TimeoutException("PowerShell Creator Session integration timed out");
    }
    string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
    return new ProcessResult(process.ExitCode, output);
  }

  sealed record ProcessResult(int ExitCode, string Output);
}

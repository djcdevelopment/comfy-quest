namespace ComfyNetworkSense.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ComfyQuestContracts;
using Xunit;

/// <summary>Retention for a store that is read back as authority.
/// <para><c>NFR-OBS-001</c>: "Receipt and evidence stores have explicit size/age retention with
/// archive/export before deletion. Reads stay bounded, and pruning one run cannot break another
/// run's audit chain." Audit C3 found both halves violated from opposite ends —
/// <c>RuntimeReceiptStore</c> had no cap at all, and run-control receipts sat in one flat
/// directory whose prune ordered every run's files together and deleted the tail.</para>
/// <para>The two properties these tests exist to hold: <b>the retention boundary never destroys
/// anything</b>, and <b>one run's activity cannot evict another's.</b></para></summary>
public sealed class ReceiptRetentionTests {

  // --- the live boundary moves; it does not delete -----------------------------------------

  [Fact] public void RetentionArchivesRatherThanDeletes() {
    Run(root => {
      var store = new RuntimeReceiptStore(root);
      var written = Enumerable.Range(0, RuntimeReceiptStore.MaxLiveReceipts + 40)
        .Select(index => store.Write(Receipt("gameplay", index))).ToArray();
      store.Sweep(DateTimeOffset.UtcNow);

      var live = Directory.GetFiles(Path.Combine(root, "receipts"), "*.json");
      var archived = Directory.GetFiles(Path.Combine(root, "receipts", "archive"), "*.json");
      Assert.True(live.Length <= RuntimeReceiptStore.MaxLiveReceipts, $"live={live.Length}");
      Assert.NotEmpty(archived);
      // Nothing was destroyed: every receipt is still exactly one file somewhere.
      Assert.Equal(written.Length, live.Length + archived.Length);
      Assert.Equal(
        written.Select(Path.GetFileName).OrderBy(value => value, StringComparer.Ordinal),
        live.Concat(archived).Select(Path.GetFileName).OrderBy(value => value, StringComparer.Ordinal));
    });
  }

  /// <summary>Archiving moves the bytes. A store read back as authority may not change the shape
  /// of its evidence at a retention boundary.</summary>
  [Fact] public void ArchivedReceiptsAreTheSameBytes() {
    Run(root => {
      var store = new RuntimeReceiptStore(root);
      var first = store.Write(Receipt("bind", 0));
      var before = File.ReadAllBytes(first);
      for (var index = 1; index <= RuntimeReceiptStore.MaxLiveReceipts; index++) store.Write(Receipt("bind", index));
      store.Sweep(DateTimeOffset.UtcNow);

      Assert.False(File.Exists(first));
      var archived = Path.Combine(root, "receipts", "archive", Path.GetFileName(first));
      Assert.True(File.Exists(archived), "the oldest receipt should have moved, not vanished");
      Assert.Equal(before, File.ReadAllBytes(archived));
    });
  }

  [Fact] public void AgeRetentionIsExplicitAndArchivesToo() {
    Run(root => {
      var store = new RuntimeReceiptStore(root);
      Assert.Equal(30, RuntimeReceiptStore.MaxLiveAgeDays);
      var stale = store.Write(Receipt("gameplay", 0));
      File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-RuntimeReceiptStore.MaxLiveAgeDays - 1));
      var fresh = store.Write(Receipt("gameplay", 1));
      store.Sweep(DateTimeOffset.UtcNow);
      Assert.False(File.Exists(stale));
      Assert.True(File.Exists(fresh));
      Assert.True(File.Exists(Path.Combine(root, "receipts", "archive", Path.GetFileName(stale))));
    });
  }

  /// <summary>Evidence that aged out stays reachable. "Archived" has to mean readable, or it is
  /// just a slower way of losing it.</summary>
  [Fact] public void ArchivedEvidenceStaysEnumerableAndReadsStayBounded() {
    Run(root => {
      var store = new RuntimeReceiptStore(root);
      for (var index = 0; index < RuntimeReceiptStore.MaxLiveReceipts + 10; index++) store.Write(Receipt("gameplay", index));
      store.Sweep(DateTimeOffset.UtcNow);

      Assert.NotEmpty(store.ListArchived(50));
      var parsed = JsonConvert.DeserializeObject<RuntimeReceipt>(File.ReadAllText(store.ListArchived(1)[0]));
      Assert.Equal("gameplay", parsed.Operation);
      // Reads are bounded by the cap, not by whatever accumulated.
      Assert.True(store.List(10_000).Count <= RuntimeReceiptStore.MaxListLimit);
      Assert.True(store.ListArchived(10_000).Count <= RuntimeReceiptStore.MaxListLimit);
      // The live listing never leaks archived paths back into the window it claims to describe.
      Assert.All(store.List(200), path => Assert.Equal(Path.Combine(root, "receipts"), Path.GetDirectoryName(path)));
    });
  }

  /// <summary>The archive bound is the only place evidence is destroyed, and it says so. A reader
  /// has to be able to tell a pruned chain from an incomplete one.</summary>
  [Fact] public void DestroyingEvidenceWritesAReceiptSayingSo() {
    Run(root => {
      var archive = Directory.CreateDirectory(Path.Combine(root, "receipts", "archive")).FullName;
      for (var index = 0; index < RuntimeReceiptStore.MaxArchivedReceipts + 5; index++) {
        var path = Path.Combine(archive, "2026010" + index.ToString("D6") + ".json");
        File.WriteAllText(path, "{}");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-index));
      }
      var store = new RuntimeReceiptStore(root);
      store.Sweep(DateTimeOffset.UtcNow);

      Assert.Equal(RuntimeReceiptStore.MaxArchivedReceipts, Directory.GetFiles(archive, "*.json").Length);
      var notice = store.List(20)
        .Select(path => JsonConvert.DeserializeObject<RuntimeReceipt>(File.ReadAllText(path)))
        .Single(receipt => receipt.Operation == "receipt_archive_evicted");
      Assert.Equal(5, notice.CandidateCount);
      Assert.Contains(notice.Diagnostics, value => value.Code == "receipts.archive_evicted");
      Assert.Contains("no longer complete", notice.Diagnostics.Single().Message);
    });
  }

  // --- one run cannot evict another ---------------------------------------------------------

  /// <summary>The clause NFR-OBS-001 names as forbidden, and the shape audit C3 found: a burst of
  /// resets on one run used to order every run's receipts together and delete the tail.</summary>
  [Fact] public void ABurstOnOneRunCannotEvictAnotherRunsChain() {
    Run(root => {
      var quiet = Enumerable.Range(0, 4).Select(index => WriteRunControl(root, "run-quiet", "quiet-" + index)).ToArray();
      var busy = Enumerable.Range(0, RuntimeRunControlReceipts.MaxPerScope + 20)
        .Select(index => WriteRunControl(root, "run-busy", "busy-" + index.ToString("D3"))).ToArray();

      Prune(root, "run-busy");

      // The busy run is bounded, and its overflow was archived rather than dropped.
      var busyLive = Directory.GetFiles(Path.Combine(root, "receipts", "run-control", "run-busy"), "*.json");
      Assert.Equal(RuntimeRunControlReceipts.MaxPerScope, busyLive.Length);
      Assert.Equal(20, Directory.GetFiles(Path.Combine(root, "receipts", "run-control", "archive", "run-busy"), "*.json").Length);
      Assert.Equal(busy.Length, busyLive.Length + 20);

      // The quiet run is untouched. That is the whole finding.
      Assert.All(quiet, path => Assert.True(File.Exists(path), path));
    });
  }

  [Fact] public void SelectExperienceReceiptsGetTheirOwnBoundedScope() {
    Run(root => {
      // select_experience addresses the pack, not a run, so it has no run id to partition by.
      Assert.Equal("pack", RuntimeRunControlReceipts.Scope(null));
      Assert.Equal("pack", RuntimeRunControlReceipts.Scope("   "));
      Assert.Equal("run-7", RuntimeRunControlReceipts.Scope("run-7"));
      var pack = Enumerable.Range(0, RuntimeRunControlReceipts.MaxPerScope + 3)
        .Select(index => WriteRunControl(root, null, "select-" + index.ToString("D3"))).ToArray();
      var run = WriteRunControl(root, "run-1", "reset-1");
      Prune(root, null);
      Assert.Equal(RuntimeRunControlReceipts.MaxPerScope,
        Directory.GetFiles(Path.Combine(root, "receipts", "run-control", "pack"), "*.json").Length);
      Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "receipts", "run-control", "archive", "pack"), "*.json").Length);
      // The reset receipt in its own scope is untouched by the pack scope filling up.
      Assert.True(File.Exists(run));
      Assert.Equal(RuntimeRunControlReceipts.MaxPerScope + 3, pack.Length);
    });
  }

  /// <summary>A run id is a safe identifier, and the policy that admits one also admits "." and
  /// "..". Safe as a name is not the same as safe as a path segment.</summary>
  [Fact] public void AScopeCannotEscapeItsDirectory() {
    Assert.False(ReceiptRetention.SafeScope(".."));
    Assert.False(ReceiptRetention.SafeScope("."));
    Assert.False(ReceiptRetention.SafeScope("a/b"));
    Assert.False(ReceiptRetention.SafeScope("a\\b"));
    Assert.False(ReceiptRetention.SafeScope(new string('r', 97)));
    Assert.True(ReceiptRetention.SafeScope("run-2026-08-25_01.a"));
    // And the layout refuses them rather than sanitising them into something surprising.
    Assert.Equal("pack", RuntimeRunControlReceipts.Scope(".."));
    Assert.Equal("pack", RuntimeRunControlReceipts.Scope("../../etc"));
  }

  [Fact] public void TheNumberOfPartitionsIsBoundedAndRetiredOnesMoveWhole() {
    Run(root => {
      for (var index = 0; index < RuntimeRunControlReceipts.MaxScopes + 3; index++) {
        var scope = "run-" + index.ToString("D3");
        var path = WriteRunControl(root, scope, "only-1");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(index - 200));
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(path), DateTime.UtcNow.AddMinutes(index - 200));
      }
      Prune(root, "run-066");
      var liveScopes = Directory.GetDirectories(Path.Combine(root, "receipts", "run-control"))
        .Select(Path.GetFileName).Where(name => name != "archive").ToArray();
      Assert.True(liveScopes.Length <= RuntimeRunControlReceipts.MaxScopes, $"scopes={liveScopes.Length}");
      // Retired partitions moved whole, so a finished run's chain is intact in the archive rather
      // than missing its tail.
      var archivedScopes = Directory.GetDirectories(Path.Combine(root, "receipts", "run-control", "archive"));
      Assert.Equal(3, archivedScopes.Length);
      Assert.All(archivedScopes, directory => Assert.Single(Directory.GetFiles(directory, "*.json")));
    });
  }

  [Fact] public void RetentionBoundsAreDeclaredPerRunRatherThanShared() {
    // The old cap was 128 receipts across every run at once, while the registry keeps up to 512
    // runs with predecessor lineage — so runs whose reset receipts were gone but whose lineage
    // still pointed at them were guaranteed, not merely possible.
    Assert.Equal(32, RuntimeRunControlReceipts.MaxPerScope);
    Assert.Equal(64, RuntimeRunControlReceipts.MaxScopes);
    Assert.Equal(1024, RuntimeRunControlReceipts.MaxArchived);
    Assert.True(RuntimeRunControlReceipts.MaxPerScope * RuntimeRunControlReceipts.MaxScopes >= 128);
  }

  // --- helpers -----------------------------------------------------------------------------

  static RuntimeReceipt Receipt(string operation, int index) => new() {
    Operation = operation,
    Status = "completed",
    CorrelationId = "corr-" + (index / 4).ToString("D4"),
    RunId = "run-" + (index / 8).ToString("D4"),
    Diagnostics = Array.Empty<ContractDiagnostic>(),
  };

  static string WriteRunControl(string root, string runId, string requestId) {
    var scope = RuntimeRunControlReceipts.Scope(runId);
    var directory = RuntimeRunControlReceipts.ScopeDirectory(root, scope);
    Directory.CreateDirectory(directory);
    var path = RuntimeRunControlReceipts.ReceiptPath(root, scope, requestId);
    File.WriteAllText(path, JsonConvert.SerializeObject(new RuntimeRunControlReceipt {
      RequestId = requestId, Operation = "apply_reset", State = "completed", Machine = "OMEN", WorldUid = "1",
    }));
    return path;
  }

  /// <summary>The controller's retention, exercised through the same contract surface it uses.
  /// The controller itself needs the game; this does not, which is the point of putting the
  /// policy in Contracts.</summary>
  static void Prune(string root, string runId) {
    var now = DateTimeOffset.UtcNow;
    var scope = RuntimeRunControlReceipts.Scope(runId);
    ReceiptRetention.Archive(
      RuntimeRunControlReceipts.ScopeDirectory(root, scope),
      Path.Combine(RuntimeRunControlReceipts.ArchiveRoot(root), scope),
      RuntimeRunControlReceipts.MaxPerScope, TimeSpan.Zero, now);
    var partitions = new DirectoryInfo(RuntimeRunControlReceipts.Root(root)).GetDirectories()
      .Where(directory => !string.Equals(directory.Name, "archive", StringComparison.OrdinalIgnoreCase))
      .OrderByDescending(directory => directory.LastWriteTimeUtc).ToArray();
    foreach (var retired in partitions.Skip(RuntimeRunControlReceipts.MaxScopes)) {
      ReceiptRetention.Archive(retired.FullName,
        Path.Combine(RuntimeRunControlReceipts.ArchiveRoot(root), retired.Name), 0, TimeSpan.Zero, now);
      try { if (retired.GetFiles().Length == 0) retired.Delete(); } catch { }
    }
    ReceiptRetention.Evict(RuntimeRunControlReceipts.ArchiveRoot(root), RuntimeRunControlReceipts.MaxArchived);
  }

  static void Run(Action<string> body) {
    var root = Path.Combine(Path.GetTempPath(), "comfy-retention-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try { body(root); } finally { try { Directory.Delete(root, true); } catch { } }
  }
}

namespace ComfyNetworkSense.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ComfyQuestContracts;
using Xunit;

/// <summary>Guild scale, and the one check that blocked it.
/// <para>QuestPackStore has always validated, hashed, and compiled <em>N</em> experience documents.
/// Both Runtime call sites then refused the second one — <c>RuntimeCharmBinding.TryActive</c> and
/// <c>RuntimeExperienceEngine.TryLoad</c> — so a guild could be authored and published but never
/// bound or run as a unit. Resolution now lives in one place, and this is where it is proven,
/// because neither call site can be reached without the game.</para>
/// <para>The rule these tests exist to hold: <b>an absent selector must behave exactly as it did
/// before the selector existed</b>, including the diagnostic it fails with.</para></summary>
public sealed class RuntimeExperienceSelectionTests {
  const string Hello = """{"schema":"comfy-quest-experience/v2","id":"hello","entry_stage":"start","stages":[{"id":"start","transitions":[{"id":"done","priority":1,"when":{"op":"EVENT","event":"kill","target":"Troll"},"actions":[{"id":"say","type":"message","text":"Skal!"}],"outcome":"complete"}]}],"bindings":[{"id":"default","experience_id":"hello","target_kinds":["sign"]}]}""";
  static string Named(string id) => Hello.Replace("\"id\":\"hello\"", "\"id\":\"" + id + "\"").Replace("\"experience_id\":\"hello\"", "\"experience_id\":\"" + id + "\"");
  static HashSet<string> Events => new() { "kill" };

  // --- an absent selector is the old behaviour, exactly -----------------------------------

  [Fact] public void OneExperienceResolvesWithoutASelector() {
    Run(root => {
      WritePack(Path.Combine(root, "inbox", "solo.questpack"), "1.0.0", ("experiences/hello.json", Hello));
      var store = new QuestPackStore(root);
      store.LoadLatest(Events);
      var active = ReadActive(root);
      Assert.Null(active.ExperienceId);
      Assert.True(TryResolve(root, active, out var chosen, out var error), error);
      Assert.Equal("hello", chosen.Id);
      Assert.Equal(new[] { "hello" }, chosen.Available);
      // The additive field serialises away entirely when unset, so an active set written before
      // the selector existed and one written after are byte-identical.
      Assert.DoesNotContain("experience_id", File.ReadAllText(Path.Combine(root, "active", "active-set.json")));
    });
  }

  [Fact] public void ManyExperiencesWithoutASelectorStillRefuseWithTheSameDiagnostic() {
    Run(root => {
      WritePack(Path.Combine(root, "inbox", "guild.questpack"), "1.0.0",
        ("experiences/hello.json", Hello), ("experiences/second.json", Named("second")));
      var store = new QuestPackStore(root);
      store.LoadLatest(Events);
      Assert.False(TryResolve(root, ReadActive(root), out _, out var error));
      Assert.Equal("active_experience_ambiguous", error);
    });
  }

  [Fact] public void AnOldActiveSetWithNoSelectorFieldStillDeserialises() {
    var legacy = JsonConvert.DeserializeObject<ActiveSet>(
      """{"schema":"comfy-quest-active-set/v1","pack_id":"demo","version":"1.0.0","source":"demo.questpack"}""");
    Assert.Null(legacy.ExperienceId);
  }

  // --- selection ---------------------------------------------------------------------------

  [Fact] public void SelectingBindsOneExperienceOfAGuildPack() {
    Run(root => {
      WritePack(Path.Combine(root, "inbox", "guild.questpack"), "1.0.0",
        ("experiences/a.json", Named("alpha")),
        ("experiences/b.json", Named("beta")),
        ("experiences/c.json", Named("gamma")));
      var store = new QuestPackStore(root);
      store.LoadLatest(Events);
      Assert.Equal(new[] { "alpha", "beta", "gamma" }, store.ActiveExperienceIds());

      Assert.Equal("beta", store.SelectExperience("beta").ExperienceId);
      Assert.True(TryResolve(root, ReadActive(root), out var chosen, out var error), error);
      Assert.Equal("beta", chosen.Id);

      // Selecting again moves the binding rather than accumulating one.
      store.SelectExperience("gamma");
      Assert.True(TryResolve(root, ReadActive(root), out chosen, out _));
      Assert.Equal("gamma", chosen.Id);
      Assert.Equal(new[] { "alpha", "beta", "gamma" }, chosen.Available);
    });
  }

  [Fact] public void SelectingAnExperienceThePackDoesNotHoldChangesNothing() {
    Run(root => {
      WritePack(Path.Combine(root, "inbox", "guild.questpack"), "1.0.0",
        ("experiences/a.json", Named("alpha")), ("experiences/b.json", Named("beta")));
      var store = new QuestPackStore(root);
      store.LoadLatest(Events);
      store.SelectExperience("alpha");
      var before = File.ReadAllText(Path.Combine(root, "active", "active-set.json"));
      Assert.Equal("active_experience_not_in_pack",
        Assert.Throws<InvalidOperationException>(() => store.SelectExperience("delta")).Message);
      Assert.Equal(before, File.ReadAllText(Path.Combine(root, "active", "active-set.json")));
      Assert.Equal("select_experience_id_required",
        Assert.Throws<InvalidOperationException>(() => store.SelectExperience("  ")).Message);
    });
  }

  /// <summary>Selection is not an activation. It mints no activation id and archives no history,
  /// because nothing about the installed content changed — only which experience answers.</summary>
  [Fact] public void SelectionIsNotAnActivation() {
    Run(root => {
      WritePack(Path.Combine(root, "inbox", "guild.questpack"), "1.0.0",
        ("experiences/a.json", Named("alpha")), ("experiences/b.json", Named("beta")));
      var store = new QuestPackStore(root);
      store.LoadLatest(Events);
      var before = ReadActive(root);
      var history = store.ActivationHistory().Count;
      store.SelectExperience("beta");
      var after = ReadActive(root);
      Assert.Equal(before.ActivationId, after.ActivationId);
      Assert.Equal(before.ActivatedUtc, after.ActivatedUtc);
      Assert.Equal(before.ContentHash, after.ContentHash);
      Assert.Equal(history, store.ActivationHistory().Count);
      Assert.False(File.Exists(Path.Combine(root, "active", "active-set.json.tmp")));
    });
  }

  /// <summary>The engine invalidates its cached active set on the file's write time, so a repeated
  /// selection that rewrote the file would drop a compiled document for no reason.</summary>
  [Fact] public void SelectingWhatIsAlreadySelectedDoesNotTouchTheFile() {
    Run(root => {
      WritePack(Path.Combine(root, "inbox", "guild.questpack"), "1.0.0",
        ("experiences/a.json", Named("alpha")), ("experiences/b.json", Named("beta")));
      var store = new QuestPackStore(root);
      store.LoadLatest(Events);
      store.SelectExperience("beta");
      var path = Path.Combine(root, "active", "active-set.json");
      var marker = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
      File.SetLastWriteTimeUtc(path, marker);
      Assert.Equal("beta", store.SelectExperience("beta").ExperienceId);
      Assert.Equal(marker, File.GetLastWriteTimeUtc(path));
    });
  }

  [Fact] public async Task SelectionRetriesAcrossABoundedWindowsReader() {
    var root = Path.Combine(Path.GetTempPath(), "comfy-select-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "inbox"));
    try {
      WritePack(Path.Combine(root, "inbox", "guild.questpack"), "1.0.0",
        ("experiences/a.json", Named("alpha")), ("experiences/b.json", Named("beta")));
      var store = new QuestPackStore(root);
      store.LoadLatest(Events);
      var path = Path.Combine(root, "active", "active-set.json");
      var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
      var replacement = Task.Run(() => store.SelectExperience("beta"));
      await Task.Delay(15);
      reader.Dispose();
      Assert.Equal("beta", (await replacement).ExperienceId);
      Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp-*"));
    } finally { try { Directory.Delete(root, true); } catch { } }
  }

  [Fact] public void FailedBindingRecoveryCanRestoreAnAbsentSelectorButNotANewerActivation() {
    Run(root => {
      WritePack(Path.Combine(root, "inbox", "guild.questpack"), "1.0.0",
        ("experiences/a.json", Named("alpha")), ("experiences/b.json", Named("beta")));
      var store = new QuestPackStore(root);
      store.LoadLatest(Events);
      var activation = ReadActive(root).ActivationId;
      store.SelectExperience("beta");
      Assert.Null(store.RestoreExperienceSelection(activation, null).ExperienceId);
      store.SelectExperience("alpha");
      Assert.Equal("active_activation_mismatch", Assert.Throws<InvalidOperationException>(
        () => store.RestoreExperienceSelection("act-20260825T120000000Z-deadbeef", "beta")).Message);
      Assert.Equal("alpha", ReadActive(root).ExperienceId);
    });
  }

  /// <summary>A new revision may not contain the selected experience, so activation clears the
  /// selector rather than carrying a stale one forward. An explicit re-selection is cheap; a
  /// silently wrong binding is not.</summary>
  [Fact] public void ActivatingANewRevisionClearsTheSelector() {
    Run(root => {
      WritePack(Path.Combine(root, "inbox", "one.questpack"), "1.0.0",
        ("experiences/a.json", Named("alpha")), ("experiences/b.json", Named("beta")));
      WritePack(Path.Combine(root, "inbox", "two.questpack"), "2.0.0",
        ("experiences/a.json", Named("alpha")), ("experiences/c.json", Named("gamma")));
      var store = new QuestPackStore(root);
      store.LoadVersion("demo", "1.0.0", Events);
      store.SelectExperience("beta");
      Assert.Equal("beta", ReadActive(root).ExperienceId);
      store.LoadVersion("demo", "2.0.0", Events);
      Assert.Null(ReadActive(root).ExperienceId);
      Assert.False(TryResolve(root, ReadActive(root), out _, out var error));
      Assert.Equal("active_experience_ambiguous", error);
    });
  }

  /// <summary>Rollback is another same-pack activation. It preserves an explicit selector only
  /// when the restored archive still resolves that exact experience once; the removed-id case
  /// above proves the complementary fail-closed path.</summary>
  [Fact] public void RollbackPreservesASelectorThatTheRestoredRevisionStillResolves() {
    Run(root => {
      WritePack(Path.Combine(root, "inbox", "one.questpack"), "1.0.0", ("experiences/a.json", Named("alpha")));
      WritePack(Path.Combine(root, "inbox", "two.questpack"), "2.0.0", ("experiences/a.json", Named("alpha")));
      var store = new QuestPackStore(root);
      store.LoadVersion("demo", "1.0.0", Events);
      store.SelectExperience("alpha");
      store.LoadVersion("demo", "2.0.0", Events);
      Assert.Equal("1.0.0", store.Rollback(Events).Manifest.Version);
      Assert.Equal("alpha", ReadActive(root).ExperienceId);
    });
  }

  // --- the resolver's own edges -------------------------------------------------------------

  [Fact] public void APackWithNoExperiencesIsMissingRatherThanAmbiguous() {
    using var zip = OpenTemp(("manifest.json", "{}"));
    Assert.False(ActiveExperienceResolver.TryResolve(zip.Archive, null, out _, out var error));
    Assert.Equal("active_experience_missing", error);
  }

  [Fact] public void TwoDocumentsUnderOneIdStayAmbiguousEvenWhenSelected() {
    using var zip = OpenTemp(("experiences/a.json", Named("twin")), ("experiences/b.json", Named("twin")));
    Assert.False(ActiveExperienceResolver.TryResolve(zip.Archive, "twin", out _, out var error));
    Assert.Equal("active_experience_ambiguous", error);
  }

  [Fact] public void AnUnreadableDocumentFailsClosed() {
    using var zip = OpenTemp(("experiences/a.json", "{ not json"));
    Assert.False(ActiveExperienceResolver.TryResolve(zip.Archive, null, out _, out var error));
    Assert.Equal("active_experience_unreadable", error);
    using var untitled = OpenTemp(("experiences/a.json", """{"schema":"comfy-quest-experience/v2"}"""));
    Assert.False(ActiveExperienceResolver.TryResolve(untitled.Archive, null, out _, out error));
    Assert.Equal("active_experience_unreadable", error);
  }

  /// <summary>NFR-BOUND-001: a new limit is declared and executable-tested before its feature
  /// ships. Selection reads every entry to match an id, so the read is bounded by a number.</summary>
  [Fact] public void TheSelectableExperienceCountIsBounded() {
    Assert.Equal(64, ActiveExperienceResolver.MaxSelectableExperiences);
    var many = Enumerable.Range(0, ActiveExperienceResolver.MaxSelectableExperiences)
      .Select(index => ("experiences/e" + index.ToString("D3") + ".json", Named("e" + index.ToString("D3")))).ToArray();
    using (var atLimit = OpenTemp(many))
      Assert.True(ActiveExperienceResolver.TryResolve(atLimit.Archive, "e042", out var chosen, out _) && chosen.Id == "e042");
    using var over = OpenTemp(many.Append(("experiences/one-too-many.json", Named("extra"))).ToArray());
    Assert.False(ActiveExperienceResolver.TryResolve(over.Archive, "e042", out _, out var error));
    Assert.Equal("active_experience_count_exceeded", error);
  }

  // --- the bounded operation that drives it ---------------------------------------------------

  [Fact] public void SelectExperienceIsAllowlistedAndCarriesNoRun() {
    var now = DateTimeOffset.Parse("2026-08-25T12:00:00Z");
    Assert.Equal(new[] { "preview_reset", "apply_reset", "select_experience", "list_binding_candidates", "bind_selected_experience", "restore_binding" },
      RuntimeRunControlRequestPolicy.Operations);

    // It addresses the activated pack, not a run, as do the bounded binding operations below.
    Assert.True(RuntimeRunControlRequestPolicy.Validate(Request(now, "select_experience", experienceId: "beta"), now, out var error), error);
    Assert.False(RuntimeRunControlRequestPolicy.Validate(Request(now, "select_experience"), now, out error));
    Assert.Equal("experience_selection_required", error);

    // And the reset operations may not smuggle one through.
    Assert.False(RuntimeRunControlRequestPolicy.Validate(Request(now, "preview_reset", runId: "run-1", experienceId: "beta"), now, out error));
    Assert.Equal("experience_selection_not_allowed", error);
    Assert.True(RuntimeRunControlRequestPolicy.Validate(Request(now, "preview_reset", runId: "run-1"), now, out error), error);
    Assert.False(RuntimeRunControlRequestPolicy.Validate(Request(now, "preview_reset"), now, out error));
    Assert.Equal("request_identity_invalid", error);
    Assert.False(RuntimeRunControlRequestPolicy.Validate(Request(now, "install_pack", runId: "run-1"), now, out error));
    Assert.Equal("operation_not_allowlisted", error);
    Assert.True(RuntimeRunControlRequestPolicy.Validate(Request(now, "list_binding_candidates"), now, out error), error);
    var bind = Request(now, "bind_selected_experience", experienceId: "beta");
    bind.BindingZdo = "10:20";
    Assert.True(RuntimeRunControlRequestPolicy.Validate(bind, now, out error), error);
    var restore = Request(now, "restore_binding");
    restore.BindingZdo = "10:20";
    restore.BindingChangeId = "binding-20260825T120000000Z-deadbeef";
    Assert.True(RuntimeRunControlRequestPolicy.Validate(restore, now, out error), error);
    restore.ExperienceId = "beta";
    Assert.False(RuntimeRunControlRequestPolicy.Validate(restore, now, out error));
    Assert.Equal("experience_selection_not_allowed", error);
  }

  static RuntimeRunControlRequest Request(DateTimeOffset now, string operation, string runId = null, string experienceId = null) => new() {
    RequestId = "studio-" + operation.Replace('_', '-') + "-1",
    Operation = operation,
    CreatedUtc = now.ToString("O"),
    ExpiresUtc = now.AddMinutes(2).ToString("O"),
    ExpectedMachine = "OMEN",
    ExpectedWorldUid = "918273645",
    CreatorSessionId = "creator-session-selection-test",
    RunId = runId,
    ExperienceId = experienceId,
  };

  // --- helpers ----------------------------------------------------------------------------

  static void Run(Action<string> body) {
    var root = Path.Combine(Path.GetTempPath(), "comfy-select-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "inbox"));
    try { body(root); } finally { try { Directory.Delete(root, true); } catch { } }
  }

  static bool TryResolve(string root, ActiveSet active, out ActiveExperienceResolver.ResolvedExperience chosen, out string error) {
    using var zip = ZipFile.OpenRead(Path.Combine(root,
      string.Equals(active.SourceChannel, "dev", StringComparison.OrdinalIgnoreCase) ? "inbox-dev" : "inbox", active.Source));
    return ActiveExperienceResolver.TryResolve(zip, active.ExperienceId, out chosen, out error);
  }

  static ActiveSet ReadActive(string root) =>
    JsonConvert.DeserializeObject<ActiveSet>(File.ReadAllText(Path.Combine(root, "active", "active-set.json")));

  static void WritePack(string path, string version, params (string Name, string Body)[] entries) {
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
      writer.Write($$"""{"schema":"comfy-quest-pack/v2","pack_id":"demo","version":"{{version}}"}""");
    foreach (var entry in entries)
      using (var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open()))
        writer.Write(entry.Body);
  }

  sealed class TempArchive : IDisposable {
    public ZipArchive Archive; public string Path;
    public void Dispose() { Archive?.Dispose(); try { File.Delete(Path); } catch { } }
  }

  static TempArchive OpenTemp(params (string Name, string Body)[] entries) {
    var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "comfy-zip-" + Guid.NewGuid().ToString("N") + ".questpack");
    using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
      foreach (var entry in entries)
        using (var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open()))
          writer.Write(entry.Body);
    return new TempArchive { Archive = ZipFile.OpenRead(path), Path = path };
  }
}

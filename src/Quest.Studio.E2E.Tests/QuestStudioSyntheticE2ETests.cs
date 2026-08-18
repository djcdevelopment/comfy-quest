using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using ComfyQuestContracts;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Comfy.Quest.Studio.E2E.Tests;

public sealed class QuestStudioSyntheticE2ETests
{
    const string SentinelName = ".quest-studio-synthetic-e2e";
    const string SentinelContents = "comfy-quest synthetic e2e owned root\n";
    readonly ITestOutputHelper _output;

    public QuestStudioSyntheticE2ETests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Creator_journey_reaches_the_runtime_cockpit_with_synthetic_contract_receipts()
    {
        var repoRoot = FindRepoRoot();
        var run = SyntheticRun.Create(repoRoot, SentinelName, SentinelContents);
        StudioHost? host = null;
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IBrowserContext? context = null;
        IPage? page = null;
        var consoleErrors = new ConcurrentQueue<string>();
        var failedRequests = new ConcurrentQueue<string>();
        var httpErrors = new ConcurrentQueue<string>();
        var traceStopped = false;
        var succeeded = false;

        try
        {
            host = await StudioHost.StartAsync(repoRoot, run);
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = Environment.GetEnvironmentVariable("COMFY_QUEST_E2E_HEADED") != "1"
            });
            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1600, Height = 1000 }
            });
            await context.Tracing.StartAsync(new TracingStartOptions
            {
                Screenshots = true,
                Snapshots = true,
                Sources = true
            });
            page = await context.NewPageAsync();
            page.SetDefaultTimeout(15_000);
            page.SetDefaultNavigationTimeout(30_000);
            page.Console += (_, message) =>
            {
                if (message.Type == "error") consoleErrors.Enqueue($"console: {message.Text}");
            };
            page.PageError += (_, error) => consoleErrors.Enqueue($"pageerror: {error}");
            page.RequestFailed += (_, request) =>
                failedRequests.Enqueue($"{request.Method} {request.Url}: {request.Failure}");
            page.Response += (_, response) =>
            {
                if (response.Status >= 400) httpErrors.Enqueue($"{response.Status} {response.Request.Method} {response.Url}");
            };

            await page.GotoAsync(host.StudioUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await WaitForTextAsync(page.Locator("#project-list"), "No quests yet", "empty project library");
            Assert.True(await page.Locator("#author-next").IsDisabledAsync());
            Assert.True(await page.Locator("#publish-project").IsDisabledAsync());

            await page.Locator("#template").SelectOptionAsync("signal-circuit");
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New quest", Exact = true }).ClickAsync();
            await WaitForCountAsync(page.Locator(".beat-card"), 8, "eight Signal Circuit beats");
            await WaitForTextAsync(page.Locator("#author-state"), "8 beats ready", "author readiness");
            Assert.Equal("R&D Signal Circuit", await page.Locator("#title").InputValueAsync());

            await page.Locator("#browse-events").ClickAsync();
            await WaitUntilAsync(() => page.Locator("#event-picker").IsVisibleAsync(), "Grimoire picker");
            Assert.True(await page.Locator("#event-result-count").TextContentAsync() is { } coreCount && coreCount.Contains("creator events shown", StringComparison.Ordinal));
            await WaitUntilAsync(
                async () => await page.EvaluateAsync<string>("() => document.activeElement?.id || ''") == "event-search",
                "keyboard focus in the Grimoire search");
            await page.Locator("#event-search").PressAsync("ArrowDown");
            await WaitUntilAsync(
                async () => (await page.EvaluateAsync<string>("() => document.activeElement?.dataset?.pickerEvent || ''")).Length > 0,
                "keyboard focus in the Grimoire result list");
            var firstPickerEvent = await page.EvaluateAsync<string>("() => document.activeElement?.dataset?.pickerEvent || ''");
            await page.Keyboard.PressAsync("ArrowDown");
            var secondPickerEvent = await page.EvaluateAsync<string>("() => document.activeElement?.dataset?.pickerEvent || ''");
            Assert.NotEqual(firstPickerEvent, secondPickerEvent);
            await page.Locator("#include-extended").CheckAsync();
            await WaitForTextAsync(page.Locator("#event-result-count"), "34 of 34", "full creator catalog");
            await page.Locator("#event-search").FillAsync("item unequipped");
            await page.Locator("#event-results .event-row").ClickAsync();
            Assert.True(await page.Locator("#event-preview .availability-badge.production").IsVisibleAsync());
            Assert.False(await page.Locator("#event-preview [data-picker-choose]").IsDisabledAsync());
            await page.Locator("#event-search").FillAsync("stamina");
            await WaitForTextAsync(page.Locator("#event-result-count"), "of 34", "filtered creator catalog");
            Assert.True(await page.Locator("#event-results .event-row").CountAsync() > 0);
            Assert.True(await page.Locator("#event-preview .availability-badge.research").IsVisibleAsync());
            Assert.True(await page.Locator("#event-preview [data-picker-choose]").IsDisabledAsync());
            await page.Locator("#event-picker-close").ClickAsync();

            await page.Locator("#title").FillAsync("Synthetic Signal Circuit");
            await page.Locator(".beat-card[data-beat='3']").ClickAsync();
            Assert.Equal("2", await page.Locator("#beat-repeat").InputValueAsync());
            Assert.Equal("30", await page.Locator("#beat-window").InputValueAsync());
            await FillAndBlurAsync(page.Locator("#beat-repeat"), "3");
            await FillAndBlurAsync(page.Locator("#beat-window"), "45");
            await FillAndBlurAsync(page.Locator("#beat-message"), "Synthetic drop checkpoint.");
            await WaitForExactTextAsync(page.Locator("#save-label"), "Saved", "autosave completion");

            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await WaitForInputValueAsync(page.Locator("#title"), "Synthetic Signal Circuit", "persisted title");
            await WaitForCountAsync(page.Locator(".beat-card"), 8, "persisted Signal Circuit beats");
            await page.Locator(".beat-card[data-beat='3']").ClickAsync();
            Assert.Equal("3", await page.Locator("#beat-repeat").InputValueAsync());
            Assert.Equal("45", await page.Locator("#beat-window").InputValueAsync());
            Assert.Equal("Synthetic drop checkpoint.", await page.Locator("#beat-message").InputValueAsync());
            await FillAndBlurAsync(page.Locator("#beat-repeat"), "2");
            await WaitForExactTextAsync(page.Locator("#save-label"), "Saved", "rehearsal repeat reset");

            await page.Locator("[data-stage='rehearse']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Rehearse this quest", Exact = true }).ClickAsync();
            await WaitForExactTextAsync(page.Locator("#rehearsal-badge"), "Complete", "completed rehearsal", 30_000);
            await WaitForTextAsync(page.Locator("#rehearsal-result"), "1/2", "partial rehearsal progress");
            await WaitForTextAsync(page.Locator("#rehearsal-result .disclaimer"), "does not prove a Valheim adapter", "rehearsal evidence disclaimer");

            await page.Locator("[data-stage='publish']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish this iteration", Exact = true }).ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Published to Runtime inbox", "first publish", 30_000);
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-phase"), "published", "published Runtime phase");
            await WaitForFileCountAsync(run.InboxRoot, "*.questpack", 1, "published questpack");

            var fixture = SyntheticRuntimeFixture.Open(run, SentinelName, SentinelContents);
            Assert.Equal("1.0.0", fixture.Version);

            fixture.WriteCheckAccepted();
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-phase"), "checked", "checked Runtime phase");
            await WaitForTextAsync(page.Locator("#runtime-next"), "F11", "load instruction");

            fixture.ActivateAndWriteLoad();
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-phase"), "active", "active Runtime phase");
            await WaitForTextAsync(page.Locator("#runtime-next"), "backtick twice", "binding instruction");

            fixture.WriteBound();
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-phase"), "bound", "bound Runtime phase");
            await WaitForTextAsync(page.Locator("#runtime-next"), "Say something in normal chat", "first live beat");

            fixture.WritePartialProgress("item_dropped", 1, 2);
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-phase"), "bound", "partial-progress Runtime phase");
            await WaitForExactTextAsync(page.Locator("#runtime-next"), "Drop any item from your inventory. (1/2)", "partial live count");
            await WaitForTextAsync(page.Locator("#runtime-receipts"), "1/2", "partial receipt rendering");

            fixture.WriteAdvanced("item_dropped");
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-next"), "Pick up any item from the ground.", "advanced live beat");

            fixture.WriteComplete();
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-phase"), "complete", "complete Runtime phase");
            await WaitForTextAsync(page.Locator("#runtime-next"), "reports this quest complete", "completion instruction");

            await page.Locator("#tools-menu summary").ClickAsync();
            await page.Locator("[data-tool='graph']").ClickAsync();
            await WaitForCountAsync(page.Locator("#nodes .graph-node"), 8, "eight preserved graph nodes");
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add branch", Exact = true }).ClickAsync();
            await WaitForCountAsync(page.Locator("#nodes .graph-node"), 9, "new branch node");
            await WaitForExactTextAsync(page.Locator("#save-label"), "Saved", "advanced graph autosave");
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Back to creator workflow", Exact = true }).ClickAsync();
            await page.Locator("[data-stage='author']").ClickAsync();
            await WaitUntilAsync(() => page.Locator("#advanced-only").IsVisibleAsync(), "advanced-only creator message");
            await WaitForTextAsync(page.Locator("#advanced-only"), "branches and Runtime actions are preserved", "advanced graph preservation message");

            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await WaitForInputValueAsync(page.Locator("#title"), "Synthetic Signal Circuit", "advanced project reload");
            await WaitForTextAsync(page.Locator("#author-state"), "9 advanced steps", "advanced project readiness");
            await page.Locator("#tools-menu summary").ClickAsync();
            await page.Locator("[data-tool='graph']").ClickAsync();
            await WaitForCountAsync(page.Locator("#nodes .graph-node"), 9, "reloaded advanced graph nodes");
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Back to creator workflow", Exact = true }).ClickAsync();

            await page.Locator("[data-stage='publish']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish this iteration", Exact = true }).ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Version already published", "immutable version collision", 30_000);
            await WaitForTextAsync(page.Locator("#status-detail"), "Start a new iteration", "collision recovery instruction");

            await page.Locator("#library-toggle").ClickAsync();
            await page.Locator(".library-actions > summary").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Start new iteration", Exact = true }).ClickAsync();
            await WaitForInputValueAsync(page.Locator("#version"), "1.0.1", "patch version bump");
            if (await page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"))
                await page.Locator("#library-close").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish this iteration", Exact = true }).ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Published to Runtime inbox", "new iteration publish", 30_000);
            await WaitForFileCountAsync(run.InboxRoot, "*.questpack", 2, "two immutable questpack versions");

            // Extraction remains deliberately outside the everyday journey. Exercise it only
            // after the complete author/rehearse/publish path and inspect the browser bytes.
            Assert.True(await page.Locator("#advanced-tools").IsHiddenAsync());
            Assert.True(await page.Locator("#tool-data").IsHiddenAsync());
            await page.Locator("#tools-menu summary").ClickAsync();
            await page.Locator("[data-tool='data']").ClickAsync();
            await WaitUntilAsync(() => page.Locator("#tool-data").IsVisibleAsync(), "Data and history tool");
            await WaitForCountAsync(page.Locator("#history-list .history-row"), 3, "three certified history entries");
            Assert.False(await page.Locator("#include-live-evidence").IsCheckedAsync());
            Assert.True(await page.Locator("#live-evidence-warning").IsHiddenAsync());
            var packId = await page.EvaluateAsync<string>("() => project.pack_id");

            var bundleDownload = await page.RunAndWaitForDownloadAsync(
                () => page.Locator("#download-bundle").ClickAsync());
            Assert.Null(await bundleDownload.FailureAsync());
            Assert.Equal($"{packId}-1.0.1.queststudio.zip", bundleDownload.SuggestedFilename);
            var bundlePath = Path.Combine(run.DownloadRoot, bundleDownload.SuggestedFilename);
            await bundleDownload.SaveAsAsync(bundlePath);
            var bundleHash = ValidateStudioBundle(bundlePath, expectedVersion: "1.0.1", expectedHistoryCount: 3);
            await WaitForTextAsync(page.Locator("#extract-status"), bundleDownload.SuggestedFilename, "bundle download status");

            var questpackDownload = await page.RunAndWaitForDownloadAsync(
                () => page.Locator("#download-questpack").ClickAsync());
            Assert.Null(await questpackDownload.FailureAsync());
            Assert.Equal($"{packId}-1.0.1.questpack", questpackDownload.SuggestedFilename);
            var questpackPath = Path.Combine(run.DownloadRoot, questpackDownload.SuggestedFilename);
            await questpackDownload.SaveAsAsync(questpackPath);
            ValidateDownloadedQuestpack(run, questpackPath, packId, bundleHash, SentinelName, SentinelContents);
            await WaitForTextAsync(page.Locator("#extract-status"), questpackDownload.SuggestedFilename, "questpack download status");

            // Opt-out is verified across a semantic operation: downloading a questpack is
            // counted while enabled, then the same operation must leave the aggregate bytes
            // unchanged after the local setting is disabled.
            Assert.True(await page.Locator("#usage-enabled").IsCheckedAsync());
            var usagePath = UsageAggregatePath(run);
            await WaitUntilAsync(
                () => Task.FromResult(File.Exists(usagePath) && File.ReadAllText(usagePath).Contains("questpack_download", StringComparison.Ordinal)),
                "enabled questpack download usage record");
            await page.Locator("#usage-enabled").UncheckAsync();
            await WaitForTextAsync(page.Locator("#usage-status"), "Stored locally only", "usage opt-out saved");
            var aggregateBeforeOptedOutOperation = File.ReadAllBytes(usagePath);
            var optedOutDownload = await page.RunAndWaitForDownloadAsync(
                () => page.Locator("#download-questpack").ClickAsync());
            Assert.Null(await optedOutDownload.FailureAsync());
            await WaitForTextAsync(page.Locator("#extract-status"), optedOutDownload.SuggestedFilename, "opted-out questpack download status");
            Assert.Equal(aggregateBeforeOptedOutOperation, File.ReadAllBytes(usagePath));

            // A representative phone-size smoke verifies the responsive primary path is
            // usable without re-running or mutating the journey.
            await page.SetViewportSizeAsync(430, 780);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Back to creator workflow", Exact = true }).ClickAsync();
            await page.Locator("[data-stage='author']").ClickAsync();
            Assert.True(await page.Locator("#stage-author").IsVisibleAsync());
            Assert.False(await page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"));
            await page.Locator("#library-toggle").ClickAsync();
            await WaitUntilAsync(
                () => page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"),
                "narrow quest library drawer");
            Assert.Equal("true", await page.Locator("#library-toggle").GetAttributeAsync("aria-expanded"));
            await page.Locator("#library-close").ClickAsync();
            await WaitUntilAsync(
                async () => !await page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"),
                "closed narrow quest library drawer");
            Assert.Equal("false", await page.Locator("#library-toggle").GetAttributeAsync("aria-expanded"));

            Assert.DoesNotContain(consoleErrors, value =>
                !value.Contains("Failed to load resource: the server responded with a status of 400", StringComparison.Ordinal));
            Assert.Empty(failedRequests);
            Assert.Collection(httpErrors, value =>
            {
                Assert.StartsWith("400 POST ", value);
                Assert.EndsWith("/publish", value);
            });
            Assert.False(host.HasExited, "Quest Studio host exited before the browser journey completed.");

            await context.Tracing.StopAsync(new TracingStopOptions { Path = run.TracePath });
            traceStopped = true;
            succeeded = true;
        }
        catch
        {
            await CaptureFailureArtifactsAsync(page, context, run, consoleErrors, failedRequests, httpErrors, traceStopped);
            traceStopped = true;
            throw;
        }
        finally
        {
            if (!traceStopped && context is not null)
            {
                try { await context.Tracing.StopAsync(new TracingStopOptions { Path = run.TracePath }); }
                catch { }
            }
            if (context is not null) await context.DisposeAsync();
            if (browser is not null) await browser.DisposeAsync();
            playwright?.Dispose();
            if (host is not null) await host.DisposeAsync();

            var keep = Environment.GetEnvironmentVariable("COMFY_QUEST_E2E_KEEP_ARTIFACTS") == "1";
            if (succeeded && !keep)
            {
                TryDelete(run.Root);
            }
            else
            {
                _output.WriteLine($"SYNTHETIC E2E artifacts preserved: {run.Root}");
            }
        }
    }

    static async Task FillAndBlurAsync(ILocator locator, string value)
    {
        await locator.FillAsync(value);
        await locator.PressAsync("Tab");
    }

    static async Task RefreshRuntimeAsync(IPage page)
    {
        await page.Locator("[data-stage='publish']").ClickAsync();
        await page.EvaluateAsync("() => refreshRuntime()");
    }

    static async Task CaptureFailureArtifactsAsync(
        IPage? page,
        IBrowserContext? context,
        SyntheticRun run,
        ConcurrentQueue<string> consoleErrors,
        ConcurrentQueue<string> failedRequests,
        ConcurrentQueue<string> httpErrors,
        bool traceStopped)
    {
        Directory.CreateDirectory(run.Root);
        if (page is not null)
        {
            try { await page.ScreenshotAsync(new PageScreenshotOptions { Path = run.ScreenshotPath, FullPage = true }); }
            catch { }
            try { await File.WriteAllTextAsync(run.DomPath, await page.ContentAsync()); }
            catch { }
        }
        try
        {
            await File.WriteAllLinesAsync(run.BrowserLogPath, consoleErrors.Concat(failedRequests).Concat(httpErrors));
        }
        catch { }
        if (!traceStopped && context is not null)
        {
            try { await context.Tracing.StopAsync(new TracingStopOptions { Path = run.TracePath }); }
            catch { }
        }
    }

    static async Task WaitForTextAsync(ILocator locator, string expected, string description, int timeoutMs = 15_000) =>
        await WaitUntilAsync(async () => (await locator.TextContentAsync() ?? string.Empty).Contains(expected, StringComparison.Ordinal), description, timeoutMs);

    static async Task WaitForExactTextAsync(ILocator locator, string expected, string description, int timeoutMs = 15_000) =>
        await WaitUntilAsync(async () => string.Equals((await locator.TextContentAsync() ?? string.Empty).Trim(), expected, StringComparison.Ordinal), description, timeoutMs);

    static async Task WaitForInputValueAsync(ILocator locator, string expected, string description, int timeoutMs = 15_000) =>
        await WaitUntilAsync(async () => string.Equals(await locator.InputValueAsync(), expected, StringComparison.Ordinal), description, timeoutMs);

    static async Task WaitForCountAsync(ILocator locator, int expected, string description, int timeoutMs = 15_000) =>
        await WaitUntilAsync(async () => await locator.CountAsync() == expected, description, timeoutMs);

    static async Task WaitForFileCountAsync(string directory, string pattern, int expected, string description, int timeoutMs = 15_000) =>
        await WaitUntilAsync(() => Task.FromResult(Directory.Exists(directory) && Directory.GetFiles(directory, pattern).Length == expected), description, timeoutMs);

    static async Task WaitUntilAsync(Func<Task<bool>> condition, string description, int timeoutMs = 15_000)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastError = null;
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                if (await condition()) return;
            }
            catch (Exception error)
            {
                lastError = error;
            }
            await Task.Delay(100);
        }
        throw new XunitException($"Timed out waiting for {description}.{(lastError is null ? string.Empty : " Last error: " + lastError.Message)}");
    }

    static string FindRepoRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))
                && File.Exists(Path.Combine(current.FullName, "src", "Quest.Studio.Host", "Quest.Studio.Host.csproj")))
                return current.FullName;
        }
        throw new InvalidOperationException("Could not locate the comfy-quest repository root from the E2E test output directory.");
    }

    static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }

    static string ValidateStudioBundle(string path, string expectedVersion, int expectedHistoryCount)
    {
        using var archive = ZipFile.OpenRead(path);
        Assert.InRange(archive.Entries.Count, 4, 512);
        Assert.NotNull(archive.GetEntry("project/draft.json"));
        Assert.NotNull(archive.GetEntry("compiled/experience.json"));
        Assert.Null(archive.GetEntry("evidence/runtime-status.json"));
        Assert.Equal(expectedHistoryCount, archive.Entries.Count(entry =>
            entry.FullName.StartsWith("project/history/", StringComparison.Ordinal)
            && entry.FullName.EndsWith(".json", StringComparison.Ordinal)));
        Assert.DoesNotContain(archive.Entries, entry =>
            entry.FullName.Contains('\\') || entry.FullName.Contains("../", StringComparison.Ordinal));

        using var manifest = JsonDocument.Parse(ReadZipEntry(archive, "manifest.json"));
        var root = manifest.RootElement;
        Assert.Equal("comfy-quest-studio-export/v1", root.GetProperty("schema").GetString());
        Assert.Equal(expectedVersion, root.GetProperty("version").GetString());
        var options = root.GetProperty("options");
        Assert.False(options.GetProperty("live_evidence_requested").GetBoolean());
        Assert.False(options.GetProperty("live_status_included").GetBoolean());
        Assert.False(root.GetProperty("privacy").GetProperty("absolute_paths").GetBoolean());
        Assert.False(root.GetProperty("privacy").GetProperty("browser_token").GetBoolean());
        Assert.False(root.GetProperty("privacy").GetProperty("usage_aggregate").GetBoolean());
        Assert.Equal(root.GetProperty("payload_entry_count").GetInt32(), root.GetProperty("entries").GetArrayLength());
        foreach (var item in root.GetProperty("entries").EnumerateArray())
        {
            var entryPath = item.GetProperty("path").GetString();
            Assert.False(string.IsNullOrWhiteSpace(entryPath));
            var bytes = ReadZipEntry(archive, entryPath!);
            Assert.Equal(bytes.LongLength, item.GetProperty("byte_count").GetInt64());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), item.GetProperty("sha256").GetString());
        }

        var contentHash = root.GetProperty("content_hash").GetString();
        Assert.False(string.IsNullOrWhiteSpace(contentHash));
        Assert.Equal(64, contentHash!.Length);
        return contentHash;
    }

    static void ValidateDownloadedQuestpack(
        SyntheticRun run,
        string path,
        string expectedPackId,
        string expectedContentHash,
        string sentinelName,
        string sentinelContents)
    {
        SyntheticRuntimeFixture.AssertOwnedRoot(run, sentinelName, sentinelContents);
        var validationRoot = Path.Combine(run.Root, "download-validation-runtime");
        var inbox = Path.Combine(validationRoot, "inbox");
        Directory.CreateDirectory(inbox);
        var candidatePath = Path.Combine(inbox, Path.GetFileName(path));
        File.Copy(path, candidatePath, overwrite: false);
        var candidate = new QuestPackStore(validationRoot).Inspect(candidatePath);
        Assert.True(candidate.IsValid, string.Join("; ", candidate.Diagnostics.Select(value => value.Code + ": " + value.Message)));
        Assert.Equal("comfy-quest-pack/v2", candidate.Manifest.Schema);
        Assert.Equal(expectedPackId, candidate.Manifest.PackId);
        Assert.Equal("1.0.1", candidate.Manifest.Version);
        Assert.Equal(expectedContentHash, candidate.ContentHash, ignoreCase: true);
        Assert.Equal(expectedContentHash, candidate.Manifest.ContentHash, ignoreCase: true);
    }

    static byte[] ReadZipEntry(ZipArchive archive, string path)
    {
        var entry = Assert.Single(archive.Entries, value => value.FullName == path);
        using var input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    static string UsageAggregatePath(SyntheticRun run) =>
        Path.Combine(run.StateRoot, "quest-studio", "usage", "aggregate.json");

    sealed record SyntheticRun(
        string Root,
        string StateRoot,
        string ValheimRoot,
        string RuntimeRoot,
        string InboxRoot,
        string DownloadRoot,
        string TracePath,
        string ScreenshotPath,
        string DomPath,
        string BrowserLogPath,
        string HostStdoutPath,
        string HostStderrPath)
    {
        public static SyntheticRun Create(string repoRoot, string sentinelName, string sentinelContents)
        {
            var artifactRoot = Environment.GetEnvironmentVariable("COMFY_QUEST_E2E_ARTIFACT_ROOT");
            if (string.IsNullOrWhiteSpace(artifactRoot))
                artifactRoot = Path.Combine(repoRoot, "artifacts", "quest-studio-e2e");
            var root = Path.Combine(Path.GetFullPath(artifactRoot), DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N"));
            var state = Path.Combine(root, "studio-state");
            var valheim = Path.Combine(root, "Valheim");
            var runtime = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
            Directory.CreateDirectory(state);
            Directory.CreateDirectory(valheim);
            var downloads = Path.Combine(root, "browser-downloads");
            Directory.CreateDirectory(downloads);
            File.WriteAllText(Path.Combine(root, sentinelName), sentinelContents);
            return new(root, state, valheim, runtime, Path.Combine(runtime, "inbox"),
                downloads,
                Path.Combine(root, "playwright-trace.zip"), Path.Combine(root, "failure.png"),
                Path.Combine(root, "failure-dom.html"), Path.Combine(root, "browser-errors.log"),
                Path.Combine(root, "studio.stdout.log"), Path.Combine(root, "studio.stderr.log"));
        }
    }

    sealed class SyntheticRuntimeFixture
    {
        readonly SyntheticRun _run;
        readonly PackCandidate _candidate;
        readonly ExperienceDocument _document;
        readonly RuntimeReceiptStore _receipts;
        DateTimeOffset _nextReceiptAt = DateTimeOffset.UtcNow.AddSeconds(1);

        SyntheticRuntimeFixture(SyntheticRun run, PackCandidate candidate)
        {
            _run = run;
            _candidate = candidate;
            _document = ReadExperience(candidate);
            _receipts = new RuntimeReceiptStore(run.RuntimeRoot);
        }

        public string Version => _candidate.Manifest.Version;

        public static SyntheticRuntimeFixture Open(SyntheticRun run, string sentinelName, string sentinelContents)
        {
            AssertOwnedRoot(run, sentinelName, sentinelContents);
            var candidates = new QuestPackStore(run.RuntimeRoot).CheckInbox();
            var candidate = Assert.Single(candidates);
            Assert.True(candidate.IsValid, string.Join("; ", candidate.Diagnostics.Select(value => value.Code)));
            Assert.NotNull(candidate.Manifest);
            return new SyntheticRuntimeFixture(run, candidate);
        }

        public void WriteCheckAccepted() => WriteForPack(new RuntimeReceipt
        {
            Operation = "check",
            Status = "accepted",
            CandidateCount = 1,
            ValidCount = 1,
            Diagnostics = Array.Empty<ContractDiagnostic>()
        });

        public void ActivateAndWriteLoad()
        {
            AssertOwnedRoot(_run, SentinelName, SentinelContents);
            var activated = new QuestPackStore(_run.RuntimeRoot).LoadLatest();
            Assert.NotNull(activated);
            Assert.Equal(_candidate.Manifest.PackId, activated.Manifest.PackId);
            Assert.Equal(_candidate.Manifest.Version, activated.Manifest.Version);
            Assert.Equal(_candidate.ContentHash, activated.ContentHash);
            WriteForPack(new RuntimeReceipt { Operation = "load", Status = "activated" });
        }

        public void WriteBound() => WriteForPack(new RuntimeReceipt
        {
            Operation = "bind",
            Status = "inscribed",
            BindingZdo = "synthetic:0:1"
        });

        public void WritePartialProgress(string eventName, int current, int required)
        {
            var (stage, _) = RouteForEvent(eventName);
            WriteForPack(new RuntimeReceipt
            {
                Operation = "event",
                Status = "ignored",
                EventName = eventName,
                EventTarget = "Wood",
                CurrentStageId = stage.Id,
                CurrentCount = current,
                RequiredCount = required
            });
        }

        public void WriteAdvanced(string eventName)
        {
            var (stage, transition) = RouteForEvent(eventName);
            Assert.False(string.IsNullOrWhiteSpace(transition.NextStage));
            WriteForPack(new RuntimeReceipt
            {
                Operation = "transition",
                Status = "advanced",
                StageId = stage.Id,
                CurrentStageId = stage.Id,
                NextStageId = transition.NextStage,
                TransitionId = transition.Id
            });
        }

        public void WriteComplete()
        {
            var matches = _document.Stages
                .SelectMany(stage => (stage.Transitions ?? []).Select(transition => (stage, transition)))
                .Where(value => value.transition.Outcome is "complete" or "fail")
                .ToArray();
            var (stage, transition) = Assert.Single(matches);
            WriteForPack(new RuntimeReceipt
            {
                Operation = "transition",
                Status = transition.Outcome,
                StageId = stage.Id,
                CurrentStageId = stage.Id,
                TransitionId = transition.Id
            });
        }

        (ExperienceStage Stage, ExperienceTransition Transition) RouteForEvent(string eventName)
        {
            var matches = _document.Stages
                .SelectMany(stage => (stage.Transitions ?? []).Select(transition => (stage, transition)))
                .Where(value => TriggerContains(value.transition.When, eventName))
                .ToArray();
            return Assert.Single(matches);
        }

        static bool TriggerContains(TriggerExpression? trigger, string eventName) =>
            trigger is not null &&
            (string.Equals(trigger.Event, eventName, StringComparison.Ordinal) ||
             (trigger.Children ?? []).Any(child => TriggerContains(child, eventName)));

        static ExperienceDocument ReadExperience(PackCandidate candidate)
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(candidate.Path);
            var entry = Assert.Single(archive.Entries, value =>
                value.FullName.StartsWith("experiences/", StringComparison.Ordinal) &&
                value.FullName.EndsWith(".json", StringComparison.Ordinal));
            using var reader = new StreamReader(entry.Open());
            var compiled = ExperienceCompiler.CompileProductionJson(reader.ReadToEnd());
            Assert.Empty(compiled.Diagnostics);
            Assert.NotNull(compiled.Document);
            return compiled.Document;
        }

        void WriteForPack(RuntimeReceipt receipt)
        {
            receipt.PackId = _candidate.Manifest.PackId;
            receipt.Version = _candidate.Manifest.Version;
            receipt.ContentHash = _candidate.ContentHash;
            receipt.Diagnostics = Array.Empty<ContractDiagnostic>();
            Write(receipt);
        }

        void Write(RuntimeReceipt receipt)
        {
            AssertOwnedRoot(_run, SentinelName, SentinelContents);
            receipt.AtUtc = _nextReceiptAt;
            _nextReceiptAt = _nextReceiptAt.AddMilliseconds(10);
            _receipts.Write(receipt);
        }

        internal static void AssertOwnedRoot(SyntheticRun run, string sentinelName, string sentinelContents)
        {
            var root = Path.GetFullPath(run.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var runtime = Path.GetFullPath(run.RuntimeRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var expectedRuntime = Path.Combine(root, "Valheim", "BepInEx", "config", "comfy-quest-runtime");
            var sentinel = Path.Combine(root, sentinelName);
            if (!File.Exists(sentinel) || File.ReadAllText(sentinel) != sentinelContents)
                throw new InvalidOperationException("synthetic_e2e_sentinel_missing");
            if (!string.Equals(runtime, expectedRuntime, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("synthetic_e2e_runtime_root_mismatch");
            var relative = Path.GetRelativePath(root, runtime);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("synthetic_e2e_runtime_root_escape");
        }
    }

    sealed class StudioHost : IAsyncDisposable
    {
        readonly Process _process;
        readonly Task<string> _stdout;
        readonly Task<string> _stderr;
        readonly SyntheticRun _run;

        StudioHost(Process process, Task<string> stdout, Task<string> stderr, SyntheticRun run, int port)
        {
            _process = process;
            _stdout = stdout;
            _stderr = stderr;
            _run = run;
            StudioUrl = $"http://127.0.0.1:{port}/quest-studio";
        }

        public string StudioUrl { get; }
        public bool HasExited => _process.HasExited;

        public static async Task<StudioHost> StartAsync(string repoRoot, SyntheticRun run)
        {
            var dotnet = Environment.GetEnvironmentVariable("COMFY_QUEST_E2E_DOTNET");
            if (string.IsNullOrWhiteSpace(dotnet) || !File.Exists(dotnet))
                throw new InvalidOperationException("Run tools/quest-studio/Test-QuestStudioE2E.ps1 so the E2E test receives a verified .NET 9 executable.");
            var hostDll = Environment.GetEnvironmentVariable("COMFY_QUEST_E2E_HOST_DLL");
            if (string.IsNullOrWhiteSpace(hostDll))
                hostDll = Path.Combine(repoRoot, "src", "Quest.Studio.Host", "bin", "Release", "net9.0", "Comfy.Quest.Studio.Host.dll");
            if (!File.Exists(hostDll))
                throw new InvalidOperationException("Quest Studio isolated host is not built. Run tools/quest-studio/Test-QuestStudioE2E.ps1.");
            var port = ReserveLoopbackPort();
            var start = new ProcessStartInfo
            {
                FileName = dotnet,
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(hostDll);
            start.ArgumentList.Add("--port");
            start.ArgumentList.Add(port.ToString());
            start.Environment["COMFY_QUEST_STUDIO_STATE"] = run.StateRoot;
            start.Environment["COMFY_VALHEIM_DIR"] = run.ValheimRoot;
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start Quest Studio host.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var host = new StudioHost(process, stdout, stderr, run, port);
            try
            {
                await host.WaitForHealthAsync(port);
                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }

        async Task WaitForHealthAsync(int port)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            Exception? lastError = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_process.HasExited)
                    throw new InvalidOperationException($"Quest Studio host exited during startup with code {_process.ExitCode}.");
                try
                {
                    using var response = await client.GetAsync($"http://127.0.0.1:{port}/health");
                    if (response.StatusCode == HttpStatusCode.OK) return;
                }
                catch (Exception error)
                {
                    lastError = error;
                }
                await Task.Delay(100);
            }
            throw new InvalidOperationException("Timed out waiting for Quest Studio health." + (lastError is null ? string.Empty : " " + lastError.Message));
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); }
                catch { }
            }
            try { await _process.WaitForExitAsync(); }
            catch { }
            try { await File.WriteAllTextAsync(_run.HostStdoutPath, await _stdout); }
            catch { }
            try { await File.WriteAllTextAsync(_run.HostStderrPath, await _stderr); }
            catch { }
            _process.Dispose();
        }

        static int ReserveLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }
    }
}

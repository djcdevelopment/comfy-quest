using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using ComfyQuestContracts;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Comfy.Quest.Studio.E2E.Tests;

public sealed class InstalledGuildFactAttribute : FactAttribute
{
    public InstalledGuildFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("COMFY_QUEST_INSTALLED_GUILD_E2E") != "1")
            Skip = "Set COMFY_QUEST_INSTALLED_GUILD_E2E=1 through Invoke-QuestStudioGuildJourney.ps1 to use the installed game.";
    }
}

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
            Assert.True(await page.Locator("#event-preview .availability-badge.production").IsVisibleAsync());
            Assert.False(await page.Locator("#event-preview [data-picker-choose]").IsDisabledAsync());
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

            await page.Locator(".workspace").EvaluateAsync("element => element.scrollTop = element.scrollHeight");
            Assert.True(await page.Locator(".workspace").EvaluateAsync<double>("element => element.scrollTop") > 0);
            await page.Locator("[data-stage='rehearse']").ClickAsync();
            await WaitUntilAsync(
                async () => await page.Locator(".workspace").EvaluateAsync<double>("element => element.scrollTop") == 0,
                "stage navigation resets workspace scroll");
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Run guided rehearsal", Exact = true }).ClickAsync();
            await WaitForExactTextAsync(page.Locator("#rehearsal-badge"), "Complete", "completed rehearsal", 30_000);
            await WaitForTextAsync(page.Locator("#rehearsal-result"), "1/2", "partial rehearsal progress");
            // A repeated beat's first attempt must read as progress under its owning beat, never as a miss.
            await WaitForCountAsync(page.Locator("#rehearsal-result .trace-beat .trace-attempts .trace-row.attempt.partial"), 1,
                "repeated attempts group beneath their beat with an explicit partial state");
            await WaitForTextAsync(page.Locator("#rehearsal-result .trace-row.attempt.partial"), "partial 1/2", "amber partial attempt row");
            await WaitForTextAsync(page.Locator("#rehearsal-result .disclaimer"), "does not prove a Valheim adapter", "rehearsal evidence disclaimer");

            await page.Locator("[data-stage='play']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish immutable version", Exact = true }).ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Published to Runtime inbox", "first publish", 30_000);
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-phase"), "published", "published Runtime phase");
            await WaitForTextAsync(page.Locator("#active-revision"), "Nothing active in game", "empty active revision");
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
            await WaitForTextAsync(page.Locator("#active-revision"), fixture.Version, "active version");
            await WaitForTextAsync(page.Locator("#active-revision"), fixture.ShortActivationId, "short activation id");
            await WaitForTextAsync(page.Locator("#active-revision"), "matches this draft", "active revision relation");

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
            await WaitForCountAsync(page.Locator("#runtime-receipts .receipt-row.kind-story"), 1, "story-kind receipt row");
            await WaitForExactTextAsync(page.Locator("#quest-status-state"), "Now playing", "status card state");
            await WaitForExactTextAsync(page.Locator("#quest-status-title"), "Synthetic Signal Circuit", "status card title");

            fixture.WriteComplete();
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-phase"), "complete", "complete Runtime phase");
            await WaitForTextAsync(page.Locator("#runtime-next"), "reports this quest complete", "completion instruction");
            await page.Locator("[data-stage='observe']").ClickAsync();
            Assert.True(await page.Locator("#stage-observe").IsVisibleAsync());
            await WaitForExactTextAsync(page.Locator("#observe-runtime"), "Live proof complete", "Observe completion state");
            await WaitForTextAsync(page.Locator("#runtime-receipts"), "Runtime observed", "Observe live proof trail");

            await page.Locator("#tools-menu summary").ClickAsync();
            await page.Locator("[data-tool='graph']").ClickAsync();
            await WaitForCountAsync(page.Locator("#nodes .graph-node"), 8, "eight preserved graph nodes");
            await WaitForTextAsync(page.Locator("#advanced-active-revision"), fixture.ShortActivationId, "advanced active revision");
            await WaitForTextAsync(page.Locator("#advanced-runtime-receipts"), "Show message", "server-labeled Runtime effect");
            var hoverReceipt = page.Locator("#advanced-runtime-receipts .receipt-row[data-live-node]").First;
            var hoverNodeId = await hoverReceipt.GetAttributeAsync("data-live-node");
            Assert.False(string.IsNullOrWhiteSpace(hoverNodeId));
            await hoverReceipt.HoverAsync();
            await WaitUntilAsync(
                async () => await page.Locator($"#nodes .graph-node[data-node='{hoverNodeId}'].live").CountAsync() == 1,
                "receipt hover flashes its graph node");
            var graphWorkspaceWidth = await page.Locator("#tool-graph").EvaluateAsync<double>("element => element.getBoundingClientRect().width");
            Assert.True(graphWorkspaceWidth > 1450, $"Expected the desktop graph workspace to use the viewport; got {graphWorkspaceWidth}px.");
            var graphResizer = page.Locator("#graph-resizer");
            var initialInspectorWidth = int.Parse((await graphResizer.GetAttributeAsync("aria-valuenow"))!);
            var resizerBox = await graphResizer.BoundingBoxAsync();
            Assert.NotNull(resizerBox);
            await page.Mouse.MoveAsync(resizerBox.X + resizerBox.Width / 2, resizerBox.Y + 80);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync(resizerBox.X - 80, resizerBox.Y + 80);
            await page.Mouse.UpAsync();
            await WaitUntilAsync(
                async () => int.Parse((await graphResizer.GetAttributeAsync("aria-valuenow"))!) > initialInspectorWidth,
                "drag-resized graph inspector");
            var draggedInspectorWidth = int.Parse((await graphResizer.GetAttributeAsync("aria-valuenow"))!);
            await graphResizer.PressAsync("ArrowRight");
            Assert.True(int.Parse((await graphResizer.GetAttributeAsync("aria-valuenow"))!) < draggedInspectorWidth);
            await graphResizer.DblClickAsync();
            Assert.Equal("420", await graphResizer.GetAttributeAsync("aria-valuenow"));
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

            await page.Locator("[data-stage='play']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish immutable version", Exact = true }).ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Version already published", "immutable version collision", 30_000);
            await WaitForTextAsync(page.Locator("#status-detail"), "Start a new iteration", "collision recovery instruction");

            await page.Locator("#library-toggle").ClickAsync();
            await page.Locator(".library-actions > summary").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Start new iteration", Exact = true }).ClickAsync();
            await WaitForInputValueAsync(page.Locator("#version"), "1.0.1", "patch version bump");
            if (await page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"))
                await page.Locator("#library-close").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish immutable version", Exact = true }).ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Published to Runtime inbox", "new iteration publish", 30_000);
            await WaitForFileCountAsync(run.InboxRoot, "*.questpack", 2, "two immutable questpack versions");

            // Extraction remains deliberately outside the everyday journey. Exercise it only
            // after the complete Author/Rehearse/Play/Observe path and inspect the browser bytes.
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

            // Before involving a player, drive the exact three-beat Slice 1.4 authoring
            // path through the real browser and shipping questpack contract. The broader
            // Signal Circuit above remains intact; this is the one observed-need live-lap pin.
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Back to creator workflow", Exact = true }).ClickAsync();
            await page.Locator("[data-stage='author']").ClickAsync();
            await page.Locator("#library-toggle").ClickAsync();
            await page.Locator("#template").SelectOptionAsync("blank");
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New quest", Exact = true }).ClickAsync();
            await WaitForCountAsync(page.Locator(".beat-card"), 1, "one blank Woodbound beat");
            if (await page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"))
                await page.Locator("#library-close").ClickAsync();
            await FillAndBlurAsync(page.Locator("#title"), "The Woodbound Signal");
            await page.Locator(".beat-card[data-beat='0']").ClickAsync();
            await FillAndBlurAsync(page.Locator("#beat-message"), "The charm wakes. Two offerings of wood, before the moment passes.");
            await WaitForExactTextAsync(page.Locator("#save-label"), "Saved", "wake-the-charm settle");
            await ShotAsync(page, "01-wake-the-charm");

            await AddPickerBeatAsync(page, "item_dropped", "drop Wood beat", "02-browse-player-actions");
            await OpenSpecificAsync(page);
            await FillAndBlurAsync(page.Locator("[data-beat-target]"), "Wood");
            await FillAndBlurAsync(page.Locator("#beat-repeat"), "2");
            await FillAndBlurAsync(page.Locator("#beat-window"), "30");
            await FillAndBlurAsync(page.Locator("#beat-message"), "The offering is heard. Reclaim one piece to seal the rite.");
            await WaitForExactTextAsync(page.Locator("#save-label"), "Saved", "two-offerings settle");
            await ShotAsync(page, "03-two-offerings");

            await AddPickerBeatAsync(page, "item_picked_up", "reclaim Wood beat");
            await OpenSpecificAsync(page);
            await FillAndBlurAsync(page.Locator("[data-beat-target]"), "Wood");
            await FillAndBlurAsync(page.Locator("#beat-message"), "The circuit closes. The charm remembers this telling.");
            await WaitForExactTextAsync(page.Locator("#save-label"), "Saved", "Woodbound autosave");
            await ShotAsync(page, "04-seal-the-rite");
            await page.Locator("[data-stage='rehearse']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Run guided rehearsal", Exact = true }).ClickAsync();
            await WaitForExactTextAsync(page.Locator("#rehearsal-badge"), "Complete", "Woodbound rehearsal", 30_000);
            await WaitForTextAsync(page.Locator("#rehearsal-result"), "1/2", "Woodbound partial offering");
            await ShotAsync(page, "05-rehearse-the-rite");

            var devChannel = new RuntimeDevChannelCoordinator(run.RuntimeRoot, (active, correlation) => new[]
            {
                new RuntimeReceipt
                {
                    Operation = "dev_rebind", Status = "rebound", PackId = active.PackId,
                    Version = active.Version, ContentHash = active.ContentHash, ActivationId = active.ActivationId,
                    CorrelationId = correlation, BindingZdo = "synthetic:dev:1",
                    Diagnostics = Array.Empty<ContractDiagnostic>()
                }
            });
            devChannel.Arm(DateTimeOffset.UtcNow);
            await page.Locator("[data-stage='play']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Play this revision", Exact = true }).ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Playing The Woodbound Signal", "Woodbound r1 dev transfer", 30_000);
            await WaitForFileCountAsync(Path.Combine(run.RuntimeRoot, "inbox-dev"), "*.questpack", 1, "Woodbound r1 dev questpack");
            var r1 = devChannel.Poll(DateTimeOffset.UtcNow, "start");
            Assert.True(r1.Activated, r1.Message);
            new RuntimeReceiptStore(run.RuntimeRoot).Write(new RuntimeReceipt
            {
                Operation = "event", Status = "matched", PackId = r1.ActiveSet.PackId,
                Version = r1.ActiveSet.Version, ContentHash = r1.ActiveSet.ContentHash,
                ActivationId = r1.ActiveSet.ActivationId, CorrelationId = "evt-woodbound-r1",
                CurrentStageId = "start", EventName = "chat_sent", Diagnostics = Array.Empty<ContractDiagnostic>()
            });
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-phase"), "bound", "Woodbound r1 bound through dev channel");
            foreach (var proof in new[] { "Validation", "Transfer", "Activation", "Rebind", "Runtime observed" })
                await WaitForTextAsync(page.Locator("#runtime-receipts"), proof, $"Woodbound r1 {proof} proof");
            await ShotAsync(page, "06-live-proof");

            await page.Locator("[data-stage='author']").ClickAsync();
            await page.Locator(".beat-card[data-beat='2']").ClickAsync();
            await FillAndBlurAsync(page.Locator("#beat-message"), "The circuit closes. The revised charm remembers this telling.");
            await WaitForExactTextAsync(page.Locator("#save-label"), "Saved", "Woodbound r2 autosave");
            await page.Locator("[data-stage='play']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Play this revision", Exact = true }).ClickAsync();
            await WaitForFileCountAsync(Path.Combine(run.RuntimeRoot, "inbox-dev"), "*.questpack", 2, "Woodbound r2 dev questpack");
            var r2 = devChannel.Poll(DateTimeOffset.UtcNow, "start");
            Assert.True(r2.Activated, r2.Message);
            Assert.NotEqual(r1.ActiveSet.ActivationId, r2.ActiveSet.ActivationId);
            Assert.Equal(r1.ActiveSet.ActivationId, r2.ActiveSet.PreviousActivationId);
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-phase"), "bound", "Woodbound r2 rebound without CAST");
            await WaitForTextAsync(page.Locator("#active-revision"), r2.ActiveSet.ActivationId[^8..], "Woodbound r2 activation identity");

            var store = new QuestPackStore(run.RuntimeRoot);
            var rolledCandidate = store.Rollback(r1.ActiveSet.ActivationId);
            Assert.NotNull(rolledCandidate);
            var rolled = store.ReadActive();
            Assert.NotNull(rolled);
            Assert.Equal(r1.ActiveSet.ContentHash, rolled.ContentHash);
            Assert.NotEqual(r1.ActiveSet.ActivationId, rolled.ActivationId);
            devChannel.Heartbeat(DateTimeOffset.UtcNow, "start");
            await RefreshRuntimeAsync(page);
            await WaitForExactTextAsync(page.Locator("#runtime-phase"), "other active", "Woodbound addressed rollback");
            await WaitForTextAsync(page.Locator("#active-revision"), "differs from this draft", "Woodbound rollback relation");

            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish immutable version", Exact = true }).ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Published to Runtime inbox", "Woodbound publish", 30_000);
            await WaitForFileCountAsync(run.InboxRoot, "*.questpack", 3, "Woodbound plus two Signal Circuit packs");
            ValidateWoodboundBrowserPack(run);

            // Runtime opens Studio at the exact active telling. Prove the real host consumes
            // that deep link and lands in Observe without asking the creator to find the
            // project, revision, or current beat again.
            var handoffPage = await context.NewPageAsync();
            var handoffUrl = host.StudioUrl
                + "?stage=observe&pack_id=" + Uri.EscapeDataString(rolled.PackId)
                + "&version=" + Uri.EscapeDataString(rolled.Version)
                + "&runtime_stage=start";
            await handoffPage.GotoAsync(
                handoffUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await WaitForInputValueAsync(
                handoffPage.Locator("#title"), "The Woodbound Signal", "Runtime project handoff");
            await WaitUntilAsync(
                () => handoffPage.Locator("#stage-observe").IsVisibleAsync(),
                "Runtime Observe handoff");
            await WaitForExactTextAsync(
                handoffPage.Locator("#observe-beat"), "start", "Runtime current-beat handoff");
            await handoffPage.CloseAsync();

            // A representative phone-size smoke verifies the responsive primary path is
            // usable without re-running or mutating the journey.
            await page.SetViewportSizeAsync(430, 780);
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

    [Fact]
    public async Task Guild_journey_drives_locked_B_then_A_to_B_to_A_scoped_reset_and_retention()
    {
        var repoRoot = FindRepoRoot();
        var run = SyntheticRun.Create(repoRoot, SentinelName, SentinelContents);
        StudioHost? host = null;
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IBrowserContext? context = null;
        IPage? page = null;
        SyntheticGuildRuntime? runtime = null;
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
            await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });
            page = await context.NewPageAsync();
            page.SetDefaultTimeout(15_000);
            page.Console += (_, message) =>
            {
                if (message.Type == "error") consoleErrors.Enqueue($"console: {message.Text}");
            };
            page.PageError += (_, error) => consoleErrors.Enqueue($"pageerror: {error}");
            page.RequestFailed += (_, request) => failedRequests.Enqueue($"{request.Method} {request.Url}: {request.Failure}");
            page.Response += (_, response) =>
            {
                if (response.Status >= 400) httpErrors.Enqueue($"{response.Status} {response.Request.Method} {response.Url}");
            };
            page.Dialog += async (_, dialog) => await dialog.AcceptAsync("Synthetic Guild");

            await page.GotoAsync(host.StudioUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await WaitForTextAsync(page.Locator("#project-list"), "No quests yet", "empty guild portfolio");
            if (!await page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"))
                await page.Locator("#library-toggle").ClickAsync();
            await WaitUntilAsync(
                () => page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"),
                "open guild library");
            await page.Locator("#create-guild").ClickAsync();
            await WaitForTextAsync(page.Locator("#selected-guild-name"), "Synthetic Guild settings", "selected guild editor");
            var guildId = await page.EvaluateAsync<string>("() => selectedGuildId");

            var a = await CreateGuildJourneyStepAsync(page, guildId, "Guild Quest A");
            var b = await CreateGuildJourneyStepAsync(page, guildId, "Guild Quest B");
            await page.Locator($"[data-select-guild='{guildId}']").ClickAsync();
            var prerequisite = page.Locator($"[data-guild-prerequisite='{b.ProjectId}']");
            await WaitForCountAsync(prerequisite, 1, "B prerequisite control");
            await prerequisite.SelectOptionAsync(new[] { new SelectOptionValue { Value = a.ProjectId } });
            await WaitForExactTextAsync(page.Locator("#status-title"), "Guild progression saved", "saved A before B prerequisite");

            await page.Locator("#certify-guild").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Guild pack certified", "multi-experience guild certification");
            await WaitForTextAsync(page.Locator("#status-detail"), "2 experiences", "two certified experiences");
            await page.Locator("#publish-guild").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Guild pack published", "multi-experience guild publication", 30_000);
            await WaitForFileCountAsync(run.InboxRoot, "*.questpack", 1, "one guild questpack");
            await page.Locator("#library-close").ClickAsync();
            await WaitUntilAsync(
                async () => !await page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"),
                "closed guild library before Runtime controls");

            runtime = SyntheticGuildRuntime.Start(run, a.ExperienceId, b.ExperienceId);
            Assert.Equal(a.ExperienceId, runtime.PrerequisiteOf(b.ExperienceId));
            await page.EvaluateAsync("() => refreshRunState()");
            await page.Locator("[data-stage='observe']").ClickAsync();
            await WaitUntilAsync(async () => !await page.Locator("#find-bindings").IsDisabledAsync(), "fresh synthetic world binding control");
            await page.Locator("#find-bindings").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Binding candidates ready", "bounded candidate scan");
            Assert.Equal(1, await page.Locator("#binding-candidate option").CountAsync());

            // B is still the selected project. The same control that will later accept it must
            // first expose Runtime's fail-closed prerequisite receipt.
            await page.Locator("#bind-experience").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Binding rejected", "locked B refusal");
            await WaitForTextAsync(page.Locator("#status-detail"), "experience_prerequisite_incomplete", "exact prerequisite diagnostic");
            Assert.Null(runtime.CurrentBinding);
            Assert.Empty(runtime.RunsFor(b.ExperienceId));

            await OpenProjectAsync(page, a.ProjectId);
            await page.Locator("#bind-experience").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Quest bound and started", "A bind and start");
            var firstA = Assert.Single(runtime.RunsFor(a.ExperienceId));
            Assert.Equal("complete", firstA.Outcome);

            await OpenProjectAsync(page, b.ProjectId);
            await page.Locator("#bind-experience").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Quest bound and started", "B unlock and start");
            var firstB = Assert.Single(runtime.RunsFor(b.ExperienceId));
            Assert.Equal("complete", firstB.Outcome);
            Assert.NotEqual(firstA.RunId, firstB.RunId);

            await OpenProjectAsync(page, a.ProjectId);
            await page.Locator("#bind-experience").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Quest bound and started", "return from B to A");
            Assert.Equal(firstA.RunId, Assert.Single(runtime.RunsFor(a.ExperienceId)).RunId);
            Assert.Equal(firstB.RunId, Assert.Single(runtime.RunsFor(b.ExperienceId)).RunId);
            runtime.ThrowIfFaulted();

            await WaitUntilAsync(async () => !await page.Locator("#preview-reset").IsDisabledAsync(), "A scoped reset control");
            await page.Locator("#preview-reset").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Reset preview ready", "A reset preview");
            await page.Locator("#confirm-reset").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Clean rerun ready", "A reset successor");
            var aRuns = runtime.RunsFor(a.ExperienceId);
            Assert.Equal(2, aRuns.Count);
            var successorA = Assert.Single(aRuns, value => value.Status == "active");
            Assert.Equal(firstA.RunId, successorA.PredecessorRunId);
            Assert.NotEqual(firstA.RunId, successorA.RunId);

            await OpenProjectAsync(page, b.ProjectId);
            Assert.Equal(firstB.RunId, Assert.Single(runtime.RunsFor(b.ExperienceId)).RunId);
            var browserBRun = await page.EvaluateAsync<string>("() => runState.runs[0].run_id");
            Assert.Equal(firstB.RunId, browserBRun);

            await OpenProjectAsync(page, a.ProjectId);
            await page.Locator("#bind-experience").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Quest bound and started", "successor A rerun");
            successorA = Assert.Single(runtime.RunsFor(a.ExperienceId), value => value.Status == "active");
            Assert.Equal("complete", successorA.Outcome);

            var retained = runtime.CrossRetentionBoundary(successorA.RunId, firstB.RunId);
            var archivedVisible = await page.EvaluateAsync<bool>(
                "async x => (await api(`/api/v2/quest-studio/projects/${x.projectId}/runs/control/${x.requestId}?runId=${x.runId}`)).receipt.request_id === x.requestId",
                new { projectId = a.ProjectId, requestId = retained.ArchivedRequestId, runId = successorA.RunId });
            var otherExperienceVisible = await page.EvaluateAsync<bool>(
                "async x => (await api(`/api/v2/quest-studio/projects/${x.projectId}/runs/control/${x.requestId}?runId=${x.runId}`)).receipt.request_id === x.requestId",
                new { projectId = b.ProjectId, requestId = retained.OtherExperienceRequestId, runId = firstB.RunId });
            Assert.True(archivedVisible, "Studio could not retrieve A's exact archived receipt.");
            Assert.True(otherExperienceVisible, "A's retention burst broke B's correlated receipt chain.");
            Assert.Equal(retained.ArchivedBytes, File.ReadAllBytes(retained.ArchivedPath));

            while (await page.EvaluateAsync<int>("() => bindingChanges.length") > 0)
            {
                var before = await page.EvaluateAsync<int>("() => bindingChanges.length");
                await page.Locator("#restore-binding").ClickAsync();
                await WaitUntilAsync(async () => await page.EvaluateAsync<int>("() => bindingChanges.length") == before - 1,
                    "LIFO binding restoration");
            }
            Assert.Null(runtime.CurrentBinding);
            runtime.ThrowIfFaulted();

            Assert.DoesNotContain(consoleErrors, value =>
                !value.Contains("Failed to load resource: the server responded with a status of 400", StringComparison.Ordinal));
            Assert.Empty(failedRequests);
            Assert.Collection(httpErrors, value =>
            {
                Assert.StartsWith("400 POST ", value);
                Assert.EndsWith("/runs/bind", value);
            });
            Assert.False(host.HasExited);

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
            if (runtime is not null) await runtime.DisposeAsync();
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
            if (succeeded && !keep) TryDelete(run.Root);
            else _output.WriteLine($"SYNTHETIC GUILD E2E artifacts preserved: {run.Root}");
        }
    }

    [InstalledGuildFact]
    public async Task Installed_guild_journey_proves_A_to_B_to_A_reset_retention_and_recovery()
    {
        var repoRoot = FindRepoRoot();
        var run = SyntheticRun.CreateInstalled(repoRoot);
        var sessionId = RequiredEnvironment("COMFY_QUEST_CREATOR_SESSION_ID");
        var expectedWorld = RequiredEnvironment("COMFY_QUEST_E2E_WORLD_UID");
        var expectedMachine = RequiredEnvironment("COMFY_QUEST_E2E_MACHINE");
        StudioHost? host = null;
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IBrowserContext? context = null;
        IPage? page = null;
        var consoleErrors = new ConcurrentQueue<string>();
        var failedRequests = new ConcurrentQueue<string>();
        var httpErrors = new ConcurrentQueue<string>();
        var traceStopped = false;

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
            await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });
            page = await context.NewPageAsync();
            page.SetDefaultTimeout(15_000);
            page.Console += (_, message) =>
            {
                if (message.Type == "error") consoleErrors.Enqueue($"console: {message.Text}");
            };
            page.PageError += (_, error) => consoleErrors.Enqueue($"pageerror: {error}");
            page.RequestFailed += (_, request) => failedRequests.Enqueue($"{request.Method} {request.Url}: {request.Failure}");
            page.Response += (_, response) =>
            {
                if (response.Status >= 400) httpErrors.Enqueue($"{response.Status} {response.Request.Method} {response.Url}");
            };
            page.Dialog += async (_, dialog) => await dialog.AcceptAsync("Full-width Journey");

            await page.GotoAsync(host.StudioUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await WaitUntilAsync(() => page.EvaluateAsync<bool>("() => catalog !== null && portfolio !== null"), "installed Studio boot", 30_000);
            if (!await page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"))
                await page.Locator("#library-toggle").ClickAsync();
            await page.Locator("#create-guild").ClickAsync();
            await WaitForTextAsync(page.Locator("#selected-guild-name"), "Full-width Journey settings", "installed guild editor");
            var guildId = await page.EvaluateAsync<string>("() => selectedGuildId");

            var a = await CreateGuildJourneyStepAsync(page, guildId, "Installed Guild Quest A");
            var b = await CreateGuildJourneyStepAsync(page, guildId, "Installed Guild Quest B");
            await page.Locator($"[data-select-guild='{guildId}']").ClickAsync();
            var prerequisite = page.Locator($"[data-guild-prerequisite='{b.ProjectId}']");
            await WaitForCountAsync(prerequisite, 1, "installed B prerequisite control");
            await prerequisite.SelectOptionAsync(new[] { new SelectOptionValue { Value = a.ProjectId } });
            await WaitForExactTextAsync(page.Locator("#status-title"), "Guild progression saved", "installed A before B prerequisite");
            await page.Locator("#certify-guild").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Guild pack certified", "installed guild certification");
            await page.Locator("#publish-guild").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Guild pack published", "installed immutable guild publication", 30_000);
            var production = WaitForPack(run.RuntimeRoot, QuestPackLane.Production, guildId, TimeSpan.FromSeconds(30));
            await EvidenceShotAsync(page, run, "01-authored-published");

            var automatedWorldEntry = Environment.GetEnvironmentVariable(
                "COMFY_QUEST_E2E_AUTOMATE_WORLD_ENTRY") == "1";
            if (automatedWorldEntry)
            {
                WriteJson(Path.Combine(run.Root, "machine-world-entry-requested.json"), new
                {
                    schema = "comfy-quest-machine-world-entry/v1",
                    state = "requested",
                    session_id = sessionId,
                    expected_machine = expectedMachine,
                    expected_world_uid = expectedWorld,
                    ready_utc = DateTimeOffset.UtcNow,
                    guild_id = guildId,
                    experience_a = a.ExperienceId,
                    experience_b = b.ExperienceId,
                });
                _output.WriteLine("MACHINE WORLD ENTRY: launching Valheim into the pinned Creator Session world.");
                await InvokeCreatorSessionAsync(repoRoot, "Launch", sessionId, run.ValheimRoot,
                    expectedMachine, expectedWorld, run.Root);
            }
            else
            {
                WriteJson(Path.Combine(run.Root, "human-action-required.json"), new
                {
                    schema = "comfy-quest-human-action/v1",
                    action = "Launch Valheim and enter the pinned ComfyQuestDemo authoring world.",
                    count = 1,
                    session_id = sessionId,
                    expected_machine = expectedMachine,
                    expected_world_uid = expectedWorld,
                    ready_utc = DateTimeOffset.UtcNow,
                    guild_id = guildId,
                    experience_a = a.ExperienceId,
                    experience_b = b.ExperienceId,
                });
                _output.WriteLine("HUMAN ACTION READY: launch Valheim and enter ComfyQuestDemo. Everything after world entry is automatic.");
            }

            await WaitForInstalledWorldAsync(run.RuntimeRoot, expectedMachine, expectedWorld,
                automatedWorldEntry ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(120));
            await InvokeCreatorSessionAsync(repoRoot, "Arm", sessionId, run.ValheimRoot, expectedMachine, expectedWorld, run.Root);
            await WaitUntilAsync(() => Task.FromResult(FreshArmed(run.RuntimeRoot, expectedMachine)), "armed installed dev channel", 30_000);

            await page.Locator($"[data-select-guild='{guildId}']").ClickAsync();
            await page.Locator("#play-guild").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Guild pack sent to the game", "installed guild dev transfer", 30_000);
            await WaitUntilAsync(() => Task.FromResult(ActiveGuild(run.RuntimeRoot, guildId)), "installed guild activation", 30_000);
            var active = new QuestPackStore(run.RuntimeRoot).ReadActive();
            Assert.Equal(production.ContentHash, active.ContentHash);
            Assert.Equal("dev", active.SourceChannel);
            var dev = WaitForPack(run.RuntimeRoot, QuestPackLane.Dev, guildId, TimeSpan.FromSeconds(10));
            Assert.Equal(production.ContentHash, dev.ContentHash);

            await page.Locator("#library-close").ClickAsync();
            await page.Locator("[data-stage='observe']").ClickAsync();
            await WaitUntilAsync(async () => !await page.Locator("#find-bindings").IsDisabledAsync(), "installed binding controls", 30_000);
            await page.Locator("#find-bindings").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Binding candidates ready", "installed bounded candidate scan", 30_000);
            Assert.InRange(await page.Locator("#binding-candidate option").CountAsync(), 1, RuntimeBindingCoordinator.MaxCandidates);
            var bindingZdo = await page.Locator("#binding-candidate").InputValueAsync();

            await page.Locator("#bind-experience").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Binding rejected", "installed locked B refusal", 30_000);
            await WaitForTextAsync(page.Locator("#status-detail"), "experience_prerequisite_incomplete", "installed exact prerequisite diagnostic");
            Assert.Empty(RunsFor(run.RuntimeRoot, b.ExperienceId));
            var prerequisiteRefusal = await WaitForRuntimeReceiptAsync(run.RuntimeRoot,
                value => value.Operation == "bind_selected_experience" && value.Status == "rejected"
                    && value.ExperienceId == b.ExperienceId && value.BindingZdo == bindingZdo,
                "content-correlated prerequisite refusal");
            Assert.Equal(active.ActivationId, prerequisiteRefusal.Receipt.ActivationId);
            Assert.Equal(active.ContentHash, prerequisiteRefusal.Receipt.ContentHash);
            Assert.Equal(expectedWorld, prerequisiteRefusal.Receipt.WorldId);
            Assert.StartsWith("experience_prerequisite_incomplete:", prerequisiteRefusal.Receipt.Error);
            await EvidenceShotAsync(page, run, "02-locked-b");

            await OpenProjectAsync(page, a.ProjectId);
            await page.Locator("#bind-experience").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Quest bound and started", "installed A start", 30_000);
            var firstA = await WaitForRunAsync(run.RuntimeRoot, a.ExperienceId, value => value.Outcome == "complete", "installed A completion");
            Assert.Equal(bindingZdo, firstA.Scope.BindingZdo);
            Assert.Equal(expectedWorld, firstA.Scope.WorldId);
            await EvidenceShotAsync(page, run, "03-a-complete");

            await OpenProjectAsync(page, b.ProjectId);
            await page.Locator("#bind-experience").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Quest bound and started", "installed B unlock", 30_000);
            var firstB = await WaitForRunAsync(run.RuntimeRoot, b.ExperienceId, value => value.Outcome == "complete", "installed B completion");
            Assert.NotEqual(firstA.RunId, firstB.RunId);
            var bPreviewRequest = await RequestPreviewAsync(page, b.ProjectId, firstB.RunId);
            await EvidenceShotAsync(page, run, "04-b-complete");

            await OpenProjectAsync(page, a.ProjectId);
            await page.Locator("#bind-experience").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Quest bound and started", "installed return to A", 30_000);
            Assert.Equal(firstA.RunId, Assert.Single(RunsFor(run.RuntimeRoot, a.ExperienceId)).RunId);
            Assert.Equal(firstB.RunId, Assert.Single(RunsFor(run.RuntimeRoot, b.ExperienceId)).RunId);
            await EvidenceShotAsync(page, run, "05-return-a");

            await WaitUntilAsync(async () => !await page.Locator("#preview-reset").IsDisabledAsync(), "installed A reset control", 30_000);
            await page.Locator("#preview-reset").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Reset preview ready", "installed A reset preview", 30_000);
            await page.Locator("#confirm-reset").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Clean rerun ready", "installed A reset successor", 30_000);
            var successorA = await WaitForRunAsync(run.RuntimeRoot, a.ExperienceId,
                value => value.Status == "active" && value.PredecessorRunId == firstA.RunId, "installed A successor identity");
            Assert.NotEqual(firstA.RunId, successorA.RunId);
            Assert.Equal(firstB.RunId, Assert.Single(RunsFor(run.RuntimeRoot, b.ExperienceId)).RunId);
            await EvidenceShotAsync(page, run, "06-a-reset");

            await page.Locator("#bind-experience").ClickAsync();
            await WaitForExactTextAsync(page.Locator("#status-title"), "Quest bound and started", "installed A successor rerun", 30_000);
            successorA = await WaitForRunAsync(run.RuntimeRoot, a.ExperienceId,
                value => value.RunId == successorA.RunId && value.Outcome == "complete", "installed A successor completion");
            await EvidenceShotAsync(page, run, "07-a-rerun");

            var successorScope = RuntimeRunControlReceipts.ScopeDirectory(run.RuntimeRoot, successorA.RunId);
            var initialReceipts = Directory.Exists(successorScope) ? Directory.GetFiles(successorScope, "*.json").Length : 0;
            Assert.Equal(0, initialReceipts);
            string? archivedRequest = null;
            byte[]? archivedBytes = null;
            for (var index = 0; index <= RuntimeRunControlReceipts.MaxPerScope; index++)
            {
                var requestId = await RequestPreviewAsync(page, a.ProjectId, successorA.RunId);
                if (archivedRequest is null)
                {
                    archivedRequest = requestId;
                    archivedBytes = File.ReadAllBytes(RuntimeRunControlReceipts.ReceiptPath(run.RuntimeRoot, successorA.RunId, requestId));
                }
            }
            var archivedPath = RuntimeRunControlReceipts.ArchivedReceiptPath(run.RuntimeRoot, successorA.RunId, archivedRequest!);
            await WaitUntilAsync(() => Task.FromResult(File.Exists(archivedPath)), "installed archived reset receipt", 30_000);
            Assert.Equal(archivedBytes, File.ReadAllBytes(archivedPath));
            Assert.Equal(RuntimeRunControlReceipts.MaxPerScope, Directory.GetFiles(successorScope, "*.json").Length);
            Assert.True(await ReceiptVisibleAsync(page, a.ProjectId, successorA.RunId, archivedRequest!));
            Assert.True(await ReceiptVisibleAsync(page, b.ProjectId, firstB.RunId, bPreviewRequest));
            Assert.Equal(firstB.RunId, Assert.Single(RunsFor(run.RuntimeRoot, b.ExperienceId)).RunId);
            await EvidenceShotAsync(page, run, "08-retained-proof");

            var bindingChangeIds = await page.EvaluateAsync<string[]>("() => bindingChanges.map(value => value.change_id)");
            Assert.Equal(4, bindingChangeIds.Length);
            while (await page.EvaluateAsync<int>("() => bindingChanges.length") > 0)
            {
                var before = await page.EvaluateAsync<int>("() => bindingChanges.length");
                await page.Locator("#restore-binding").ClickAsync();
                await WaitUntilAsync(async () => await page.EvaluateAsync<int>("() => bindingChanges.length") == before - 1,
                    "installed LIFO binding restoration", 30_000);
            }
            Assert.All(bindingChangeIds, changeId =>
                Assert.Equal("restored", JsonConvert.DeserializeObject<RuntimeBindingChange>(File.ReadAllText(
                    Path.Combine(run.RuntimeRoot, "state", "binding-changes", changeId + ".json")))?.State));

            CaptureInstalledEvidence(run, production, dev, active, a, b, firstA, firstB, successorA,
                prerequisiteRefusal, archivedRequest!, archivedPath, bPreviewRequest, bindingChangeIds);
            Assert.Empty(failedRequests);
            Assert.Contains(httpErrors, value => value.EndsWith("/runs/bind", StringComparison.Ordinal));
            Assert.DoesNotContain(httpErrors, value => !value.EndsWith("/runs/bind", StringComparison.Ordinal));
            Assert.DoesNotContain(consoleErrors, value =>
                !value.Contains("Failed to load resource: the server responded with a status of 400", StringComparison.Ordinal));
            Assert.False(host.HasExited);

            await context.Tracing.StopAsync(new TracingStopOptions { Path = run.TracePath });
            traceStopped = true;
            WriteJson(Path.Combine(run.Root, "machine-lap-complete.json"), new
            {
                schema = "comfy-quest-installed-guild-proof/v1",
                completed_utc = DateTimeOffset.UtcNow,
                session_id = sessionId,
                guild_id = guildId,
                content_hash = active.ContentHash,
                world_uid = expectedWorld,
                binding_zdo = bindingZdo,
                prerequisite_refusal = true,
                a_to_b_to_a = true,
                selective_reset = true,
                predecessor_run_id = firstA.RunId,
                successor_run_id = successorA.RunId,
                other_experience_run_id = firstB.RunId,
                retained_receipt = Path.GetRelativePath(run.Root, Path.Combine(run.Root, "proof", "archived-a-reset.json")),
                binding_changes_restored = bindingChangeIds.Length,
                world_entry = automatedWorldEntry ? "machine_owned" : "human_boundary",
            });
            _output.WriteLine($"INSTALLED GUILD E2E proof preserved: {run.Root}");
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
        }
    }

    static async Task<(string ProjectId, string ExperienceId)> CreateGuildJourneyStepAsync(IPage page, string guildId, string title)
    {
        await page.Locator("#template").SelectOptionAsync("guild-journey");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New quest", Exact = true }).ClickAsync();
        await WaitForInputValueAsync(page.Locator("#title"), "Guild Journey Step", title + " template");
        await WaitForTextAsync(page.Locator("#author-state"), "1 advanced steps", title + " engine-start beat");
        await FillAndBlurAsync(page.Locator("#title"), title);
        await WaitForExactTextAsync(page.Locator("#save-label"), "Saved", title + " autosave");
        var projectId = await page.EvaluateAsync<string>("() => project.project_id");
        var experienceId = await page.EvaluateAsync<string>("() => project.experience_id");
        if (!await page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"))
        {
            await page.Locator("#library-toggle").ClickAsync();
            await WaitUntilAsync(
                () => page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"),
                title + " library reopen");
        }
        var guildEditor = page.Locator("#guild-editor");
        if (await guildEditor.EvaluateAsync<bool>("element => element.open"))
            await page.Locator("#guild-editor > summary").ClickAsync();
        var actions = page.Locator(".library-actions");
        if (!await actions.EvaluateAsync<bool>("element => element.open"))
        {
            await page.Locator("#library-panel").EvaluateAsync("element => element.scrollTop = element.scrollHeight");
            await page.Locator(".library-actions > summary").ClickAsync();
        }
        await page.Locator("#project-placement").SelectOptionAsync(guildId + "|standalone||quest");
        await page.Locator("#place-project").ClickAsync();
        await WaitUntilAsync(
            () => page.EvaluateAsync<bool>($"() => placements.some(x => x.project_id === '{projectId}' && x.guild_id === '{guildId}')"),
            title + " guild placement");
        return (projectId, experienceId);
    }

    static async Task OpenProjectAsync(IPage page, string projectId)
    {
        if (!await page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"))
        {
            await page.Locator("#library-toggle").ClickAsync();
            await WaitUntilAsync(
                () => page.Locator("#library-panel").EvaluateAsync<bool>("element => element.classList.contains('open')"),
                "library for project " + projectId);
        }
        await page.Locator($"[data-project='{projectId}']").ClickAsync();
        await WaitUntilAsync(() => page.EvaluateAsync<bool>($"() => project?.project_id === '{projectId}'"), "project " + projectId);
        await page.EvaluateAsync("() => refreshRunState()");
        await page.Locator("[data-stage='observe']").ClickAsync();
        await WaitUntilAsync(async () => !await page.Locator("#find-bindings").IsDisabledAsync(), "fresh run controls for " + projectId);
    }

    static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Installed guild E2E requires {name}.")
            : value;
    }

    static async Task EvidenceShotAsync(IPage page, SyntheticRun run, string name) =>
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(run.Root, name + ".png"),
            FullPage = true,
        });

    static PackCandidate WaitForPack(string runtimeRoot, QuestPackLane lane, string packId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            var store = new QuestPackStore(runtimeRoot);
            var candidates = lane == QuestPackLane.Dev ? store.CheckDevInbox() : store.CheckInbox();
            var candidate = candidates.Where(value => value.Manifest?.PackId == packId)
                .OrderByDescending(value => File.GetLastWriteTimeUtc(value.Path)).FirstOrDefault();
            if (candidate is not null)
            {
                Assert.True(candidate.IsValid, string.Join("; ", candidate.Diagnostics.Select(value => value.Code + ":" + value.Message)));
                return candidate;
            }
            Thread.Sleep(100);
        } while (DateTimeOffset.UtcNow < deadline);
        throw new XunitException($"Timed out waiting for {lane} pack {packId}.");
    }

    static async Task WaitForInstalledWorldAsync(string runtimeRoot, string expectedMachine, string expectedWorld, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var store = new RuntimeRunStatusStore(runtimeRoot);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = store.Read();
            var fresh = status is not null && status.ObservedUtc >= DateTimeOffset.UtcNow.AddSeconds(-3)
                && status.ObservedUtc <= DateTimeOffset.UtcNow.AddSeconds(1);
            if (fresh && string.Equals(status!.Machine, expectedMachine, StringComparison.OrdinalIgnoreCase)
                && status.WorldUid == expectedWorld) return;
            await Task.Delay(250);
        }
        throw new XunitException($"Timed out waiting for {expectedMachine}/{expectedWorld} world entry.");
    }

    static bool FreshArmed(string runtimeRoot, string expectedMachine)
    {
        var status = new RuntimeDevChannelStatusStore(runtimeRoot).Read();
        return status is not null && status.Armed
            && string.Equals(Environment.MachineName, expectedMachine, StringComparison.OrdinalIgnoreCase)
            && status.ObservedUtc >= DateTimeOffset.UtcNow.AddSeconds(-3)
            && status.ObservedUtc <= DateTimeOffset.UtcNow.AddSeconds(1);
    }

    static bool ActiveGuild(string runtimeRoot, string guildId)
    {
        var status = new RuntimeDevChannelStatusStore(runtimeRoot).Read();
        return status is not null && status.Armed
            && status.ActivePackId == guildId && !string.IsNullOrWhiteSpace(status.ActiveContentHash);
    }

    static async Task InvokeCreatorSessionAsync(string repoRoot, string action, string sessionId,
        string valheimRoot, string expectedMachine, string expectedWorld, string evidenceRoot)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var value in new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(repoRoot, "tools", "creator-session", "Invoke-CreatorSession.ps1"),
            action, "-SessionId", sessionId, "-ValheimRoot", valheimRoot,
            "-ExpectedMachine", expectedMachine, "-WorldUid", expectedWorld,
            "-WaitSeconds", action == "Launch" ? "780" : "60"
        }) start.ArgumentList.Add(value);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Creator Session arm request.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdoutText = await stdout;
        var stderrText = await stderr;
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot, "creator-session-" + action.ToLowerInvariant() + ".stdout.log"), stdoutText);
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot, "creator-session-" + action.ToLowerInvariant() + ".stderr.log"), stderrText);
        if (process.ExitCode != 0)
            throw new XunitException($"Creator Session {action} failed with exit code {process.ExitCode}: {stderrText.Trim()}");
    }

    static IReadOnlyList<RuntimeRunRecord> RunsFor(string runtimeRoot, string experienceId) =>
        new RuntimeRunRegistry(runtimeRoot).List().Where(value => value.Scope.ExperienceId == experienceId)
            .OrderBy(value => value.StartedUtc).ToArray();

    static async Task<RuntimeRunRecord> WaitForRunAsync(string runtimeRoot, string experienceId,
        Func<RuntimeRunRecord, bool> predicate, string description)
    {
        RuntimeRunRecord? found = null;
        await WaitUntilAsync(() =>
        {
            found = RunsFor(runtimeRoot, experienceId).SingleOrDefault(predicate);
            return Task.FromResult(found is not null);
        }, description, 30_000);
        return found!;
    }

    static async Task<RuntimeReceiptEvidence> WaitForRuntimeReceiptAsync(string runtimeRoot,
        Func<RuntimeReceipt, bool> predicate, string description)
    {
        RuntimeReceiptEvidence? found = null;
        await WaitUntilAsync(() =>
        {
            foreach (var path in new RuntimeReceiptStore(runtimeRoot).List(RuntimeReceiptStore.MaxListLimit))
            {
                try
                {
                    var receipt = JsonConvert.DeserializeObject<RuntimeReceipt>(File.ReadAllText(path));
                    if (receipt is not null && predicate(receipt)) { found = new(receipt, path); break; }
                }
                catch { }
            }
            return Task.FromResult(found is not null);
        }, description, 30_000);
        return found!;
    }

    static async Task<string> RequestPreviewAsync(IPage page, string projectId, string runId)
    {
        var response = await page.EvaluateAsync<JsonElement>(
            "async x => await api(`/api/v2/quest-studio/projects/${x.projectId}/runs/reset`,{method:'POST',body:JSON.stringify({run_id:x.runId})})",
            new { projectId, runId });
        Assert.True(response.GetProperty("ok").GetBoolean(), response.TryGetProperty("error", out var error) ? error.GetString() : "preview rejected");
        var requestId = response.GetProperty("request_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(requestId));
        if (response.GetProperty("queued").GetBoolean())
            await WaitUntilAsync(() => ReceiptVisibleAsync(page, projectId, runId, requestId!), "queued reset preview receipt", 30_000);
        return requestId!;
    }

    static Task<bool> ReceiptVisibleAsync(IPage page, string projectId, string runId, string requestId) =>
        page.EvaluateAsync<bool>(
            "async x => {let d=await api(`/api/v2/quest-studio/projects/${x.projectId}/runs/control/${x.requestId}?runId=${x.runId}`);return d.receipt?.request_id===x.requestId}",
            new { projectId, runId, requestId });

    static void CaptureInstalledEvidence(SyntheticRun run, PackCandidate production, PackCandidate dev, ActiveSet active,
        (string ProjectId, string ExperienceId) a, (string ProjectId, string ExperienceId) b,
        RuntimeRunRecord firstA, RuntimeRunRecord firstB, RuntimeRunRecord successorA,
        RuntimeReceiptEvidence prerequisiteRefusal, string archivedRequest, string archivedPath,
        string bPreviewRequest, IReadOnlyList<string> bindingChangeIds)
    {
        var proof = Path.Combine(run.Root, "proof");
        Directory.CreateDirectory(proof);
        Copy(production.Path, "guild.production.questpack");
        Copy(dev.Path, "guild.dev.questpack");
        Copy(Path.Combine(run.RuntimeRoot, "active", "active-set.json"), "active-set.json");
        Copy(Path.Combine(run.RuntimeRoot, "state", "runs.json"), "run-registry.json");
        Copy(Path.Combine(run.RuntimeRoot, "status", "runs.json"), "run-status.json");
        Copy(Path.Combine(run.RuntimeRoot, "status", "dev-channel.json"), "dev-channel-status.json");
        Copy(prerequisiteRefusal.Path, "prerequisite-refusal.json");
        Copy(archivedPath, "archived-a-reset.json");
        Copy(RuntimeRunControlReceipts.ReceiptPath(run.RuntimeRoot, firstB.RunId, bPreviewRequest), "b-correlated-preview.json");
        foreach (var changeId in bindingChangeIds)
            Copy(Path.Combine(run.RuntimeRoot, "state", "binding-changes", changeId + ".json"), "binding-" + changeId + ".json");

        var receiptRoot = Path.Combine(run.RuntimeRoot, "receipts");
        var receiptIndex = 0;
        foreach (var path in Directory.Exists(receiptRoot)
                     ? Directory.GetFiles(receiptRoot, "*.json", SearchOption.TopDirectoryOnly)
                     : Array.Empty<string>())
        {
            RuntimeReceipt? receipt;
            try { receipt = JsonConvert.DeserializeObject<RuntimeReceipt>(File.ReadAllText(path)); }
            catch { continue; }
            if (receipt is null || receipt.ContentHash != active.ContentHash
                || receipt.ExperienceId != a.ExperienceId && receipt.ExperienceId != b.ExperienceId) continue;
            Copy(path, $"runtime-receipt-{receiptIndex++:D3}-{Path.GetFileName(path)}");
        }
        Assert.True(receiptIndex > 0, "No content-correlated Runtime receipts were captured.");
        WriteJson(Path.Combine(proof, "identity-summary.json"), new
        {
            schema = "comfy-quest-installed-guild-identity/v1",
            active.PackId,
            active.Version,
            active.ContentHash,
            active.ActivationId,
            active.Source,
            active.SourceChannel,
            experience_a = a.ExperienceId,
            experience_b = b.ExperienceId,
            prerequisite = new { experience = b.ExperienceId, requires = a.ExperienceId },
            prerequisite_refusal_receipt = prerequisiteRefusal.Receipt.Id,
            first_a_run = firstA.RunId,
            first_b_run = firstB.RunId,
            successor_a_run = successorA.RunId,
            successor_a_predecessor = successorA.PredecessorRunId,
            archived_a_request = archivedRequest,
            b_correlated_request = bPreviewRequest,
            binding_changes = bindingChangeIds,
        });

        void Copy(string source, string name)
        {
            Assert.True(File.Exists(source), "Evidence source missing: " + source);
            File.Copy(source, Path.Combine(proof, name), overwrite: false);
        }
    }

    static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        }) + Environment.NewLine);
    }

    static async Task FillAndBlurAsync(ILocator locator, string value)
    {
        await locator.FillAsync(value);
        await locator.PressAsync("Tab");
    }

    // Tutorial capture lane: when QUEST_STUDIO_TUTORIAL_SHOTS names a directory, the one
    // journey also saves labeled screenshots of the Woodbound authoring path — the
    // screenshot-led tutorial is generated from the same synthetic browser that proves the
    // path, so the player is never asked to recreate captures. Unset, this is a no-op.
    static readonly string? TutorialShots =
        Environment.GetEnvironmentVariable("QUEST_STUDIO_TUTORIAL_SHOTS");

    static async Task ShotAsync(IPage page, string name)
    {
        if (string.IsNullOrWhiteSpace(TutorialShots)) return;
        Directory.CreateDirectory(TutorialShots);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(TutorialShots, name + ".png"),
        });
    }

    static async Task AddPickerBeatAsync(IPage page, string eventName, string description,
        string? shotName = null)
    {
        var before = await page.Locator(".beat-card").CountAsync();
        await page.Locator("#browse-events").ClickAsync();
        await WaitUntilAsync(() => page.Locator("#event-picker").IsVisibleAsync(), description + " picker");
        var row = page.Locator($"#event-results .event-row[data-picker-event='{eventName}']");
        await WaitForCountAsync(row, 1, description + " result");
        await row.ClickAsync();
        if (shotName != null) await ShotAsync(page, shotName);
        await page.Locator($"#event-preview [data-picker-choose='{eventName}']").ClickAsync();
        await WaitForCountAsync(page.Locator(".beat-card"), before + 1, description);
        await WaitUntilAsync(
            () => page.EvaluateAsync<bool>($"() => !dirty && selectedBeat === {before} && beatOpen"),
            description + " autosave and selection");
    }

    static async Task OpenSpecificAsync(IPage page)
    {
        var more = page.Locator("#beat-more");
        if (!await more.EvaluateAsync<bool>("element => element.open"))
            await page.Locator("#beat-more > summary").ClickAsync();
        await WaitUntilAsync(
            () => more.EvaluateAsync<bool>("element => element.open"),
            "beat More options");
        var details = page.Locator("#beat-specific details");
        if (!await details.EvaluateAsync<bool>("element => element.open"))
            await page.Locator("#beat-specific summary").ClickAsync();
        await WaitUntilAsync(
            () => details.EvaluateAsync<bool>("element => element.open"),
            "specific target controls");
    }

    static async Task RefreshRuntimeAsync(IPage page)
    {
        await page.Locator("[data-stage='play']").ClickAsync();
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

    static void ValidateWoodboundBrowserPack(SyntheticRun run)
    {
        SyntheticRuntimeFixture.AssertOwnedRoot(run, SentinelName, SentinelContents);
        var candidates = new QuestPackStore(run.RuntimeRoot).CheckInbox();
        Assert.All(candidates, candidate =>
            Assert.True(candidate.IsValid, string.Join("; ", candidate.Diagnostics.Select(value => value.Code + ": " + value.Message))));
        var matches = candidates.Select(candidate => (Candidate: candidate, Document: ReadExperienceDocument(candidate)))
            .Where(value => value.Document.Title == "The Woodbound Signal").ToArray();
        var (candidate, document) = Assert.Single(matches);
        Assert.Equal("1.0.0", candidate.Manifest.Version);
        Assert.Equal(candidate.ContentHash, candidate.Manifest.ContentHash, ignoreCase: true);
        Assert.Collection(document.Stages,
            speak =>
            {
                var route = Assert.Single(speak.Transitions);
                Assert.Equal(("EVENT", "chat_sent", "normal"), (route.When.Op, route.When.Event, route.When.Target));
                Assert.Equal("The charm wakes. Two offerings of wood, before the moment passes.", RuntimeMessage(route));
            },
            offer =>
            {
                var route = Assert.Single(offer.Transitions);
                Assert.Equal(("COUNT", 2, 30), (route.When.Op, route.When.Count, route.When.WithinSeconds));
                var leaf = Assert.Single(route.When.Children!);
                Assert.Equal(("item_dropped", "Wood"), (leaf.Event, leaf.Target));
                Assert.Equal("The offering is heard. Reclaim one piece to seal the rite.", RuntimeMessage(route));
            },
            reclaim =>
            {
                var route = Assert.Single(reclaim.Transitions);
                Assert.Equal(("EVENT", "item_picked_up", "Wood"), (route.When.Op, route.When.Event, route.When.Target));
                Assert.Equal("complete", route.Outcome);
                Assert.Equal("The circuit closes. The revised charm remembers this telling.", RuntimeMessage(route));
            });
    }

    static ExperienceDocument ReadExperienceDocument(PackCandidate candidate)
    {
        using var archive = ZipFile.OpenRead(candidate.Path);
        var entry = Assert.Single(archive.Entries, value =>
            value.FullName.StartsWith("experiences/", StringComparison.Ordinal) &&
            value.FullName.EndsWith(".json", StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());
        var compiled = ExperienceCompiler.CompileProductionJson(reader.ReadToEnd());
        Assert.Empty(compiled.Diagnostics);
        return Assert.IsType<ExperienceDocument>(compiled.Document);
    }

    static string RuntimeMessage(ExperienceTransition route)
    {
        var action = Assert.Single(route.Actions);
        Assert.Equal("message", action.Type);
        return action.Parameters["text"].ToString();
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

        public static SyntheticRun CreateInstalled(string repoRoot)
        {
            var root = Path.GetFullPath(RequiredEnvironment("COMFY_QUEST_E2E_EVIDENCE_ROOT"));
            var valheim = Path.GetFullPath(RequiredEnvironment("COMFY_QUEST_E2E_VALHEIM_ROOT"));
            if (!File.Exists(Path.Combine(valheim, "valheim.exe")))
                throw new InvalidOperationException("Installed guild E2E Valheim root has no valheim.exe.");
            var state = Path.Combine(root, "studio-state");
            var runtime = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
            var downloads = Path.Combine(root, "browser-downloads");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(state);
            Directory.CreateDirectory(downloads);
            return new(root, state, valheim, runtime, Path.Combine(runtime, "inbox"), downloads,
                Path.Combine(root, "playwright-trace.zip"), Path.Combine(root, "failure.png"),
                Path.Combine(root, "failure-dom.html"), Path.Combine(root, "browser-errors.log"),
                Path.Combine(root, "studio.stdout.log"), Path.Combine(root, "studio.stderr.log"));
        }
    }

    sealed record RetentionProof(
        string ArchivedRequestId,
        string ArchivedPath,
        byte[] ArchivedBytes,
        string OtherExperienceRequestId);

    sealed record RuntimeReceiptEvidence(RuntimeReceipt Receipt, string Path);

    sealed class SyntheticGuildRuntime : IAsyncDisposable
    {
        const string WorldUid = "424242";
        const string BindingZdo = "42:7";
        readonly SyntheticRun _run;
        readonly ActiveSet _active;
        readonly IReadOnlyDictionary<string, ExperienceDocument> _documents;
        readonly RuntimeRunCoordinator _runs;
        readonly RuntimeBindingCoordinator _bindings;
        readonly SyntheticBindingAdapter _adapter = new();
        readonly RuntimeRunStatusStore _status;
        readonly RuntimeReceiptStore _receipts;
        readonly CancellationTokenSource _stop = new();
        readonly Task _pump;
        Exception? _fault;

        SyntheticGuildRuntime(SyntheticRun run, string experienceA, string experienceB)
        {
            _run = run;
            SyntheticRuntimeFixture.AssertOwnedRoot(run, SentinelName, SentinelContents);
            var store = new QuestPackStore(run.RuntimeRoot);
            var candidate = Assert.Single(store.CheckInbox());
            Assert.True(candidate.IsValid, string.Join("; ", candidate.Diagnostics.Select(value => value.Code)));
            store.LoadLatest();
            _active = store.ReadActive();
            _documents = ReadExperiences(candidate);
            Assert.Equal(new[] { experienceA, experienceB }.OrderBy(value => value, StringComparer.Ordinal),
                _documents.Keys.OrderBy(value => value, StringComparer.Ordinal));
            _runs = new RuntimeRunCoordinator(run.RuntimeRoot);
            _bindings = new RuntimeBindingCoordinator(run.RuntimeRoot, _adapter, _runs.Registry);
            _status = new RuntimeRunStatusStore(run.RuntimeRoot);
            _receipts = new RuntimeReceiptStore(run.RuntimeRoot);
            WriteStatus();
            _pump = Task.Run(PumpAsync);
        }

        public static SyntheticGuildRuntime Start(SyntheticRun run, string experienceA, string experienceB) =>
            new(run, experienceA, experienceB);

        public RuntimeBindingReference? CurrentBinding
        {
            get
            {
                var value = _adapter.Read(BindingZdo);
                return string.IsNullOrWhiteSpace(value?.ExperienceId) ? null : value;
            }
        }

        public IReadOnlyList<RuntimeRunRecord> RunsFor(string experienceId) =>
            _runs.Registry.List().Where(value => value.Scope.ExperienceId == experienceId)
                .OrderBy(value => value.StartedUtc).ToArray();

        public string PrerequisiteOf(string experienceId) =>
            Assert.Single(_documents[experienceId].Prerequisites);

        public void ThrowIfFaulted()
        {
            if (_fault is not null) throw new XunitException("Synthetic Runtime mailbox failed: " + _fault);
        }

        async Task PumpAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    AnswerMailbox();
                    WriteStatus();
                    await Task.Delay(40, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception error)
            {
                _fault = error;
                try { File.WriteAllText(Path.Combine(_run.Root, "synthetic-runtime-error.log"), error.ToString()); }
                catch { }
            }
        }

        void AnswerMailbox()
        {
            var mailbox = Path.Combine(_run.RuntimeRoot, "requests", "run-control.json");
            if (!File.Exists(mailbox)) return;
            RuntimeRunControlRequest? request = null;
            try
            {
                request = JsonConvert.DeserializeObject<RuntimeRunControlRequest>(File.ReadAllText(mailbox));
                File.Delete(mailbox);
                RuntimeRunControlRequestPolicy.Validate(request, DateTimeOffset.UtcNow, out var validationError);
                if (validationError is not null) throw new InvalidOperationException(validationError);
                if (!string.Equals(request!.ExpectedMachine, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("runtime_machine_mismatch");
                if (request.ExpectedWorldUid != WorldUid) throw new InvalidOperationException("runtime_world_mismatch");

                switch (request.Operation)
                {
                    case "list_binding_candidates":
                        WriteReceipt(request, "completed", "binding_candidates_ready", candidates: _bindings.Candidates());
                        break;
                    case "bind_selected_experience":
                        Bind(request);
                        break;
                    case "restore_binding":
                        WriteReceipt(request, "completed", "binding_restored",
                            change: _bindings.Restore(request.BindingZdo, request.BindingChangeId, WorldUid, DateTimeOffset.UtcNow));
                        break;
                    case "preview_reset":
                        WriteReceipt(request, "previewed", "reset_preview_ready",
                            preview: _runs.Preview(request.RunId, DateTimeOffset.UtcNow, SyntheticSpawnResetAdapter.Instance));
                        break;
                    case "apply_reset":
                        var result = _runs.Apply(request.RunId, request.PreviewToken, DateTimeOffset.UtcNow,
                            SyntheticSpawnResetAdapter.Instance);
                        WriteReceipt(request, result.State == "completed" ? "completed" : "failed", result.Detail ?? result.State,
                            result: result);
                        break;
                    default:
                        throw new InvalidOperationException("run_control_operation_invalid");
                }
            }
            catch (Exception error)
            {
                if (request is not null && RuntimeRunControlRequestPolicy.CanAddressReceipt(request))
                    WriteReceipt(request, "rejected", error.Message);
            }
        }

        void Bind(RuntimeRunControlRequest request)
        {
            if (!_documents.TryGetValue(request.ExperienceId ?? string.Empty, out var document))
                throw new InvalidOperationException("active_experience_not_in_pack");
            _active.ExperienceId = document.Id;
            var change = _bindings.Bind(request.BindingZdo, WorldUid, _active, document, DateTimeOffset.UtcNow);
            var run = _runs.Resolve(new RuntimeRunScope
            {
                WorldId = WorldUid,
                ExperienceId = document.Id,
                BindingZdo = request.BindingZdo,
                ParticipantIds = new List<string> { "synthetic-player" },
                ContentHash = _active.ContentHash,
            }, DateTimeOffset.UtcNow);
            _runs.Registry.MarkOutcome(run.RunId, "complete", DateTimeOffset.UtcNow);
            WriteReceipt(request, "completed", "experience_bound:" + document.Id, change: change);
        }

        void WriteStatus()
        {
            _status.Write(new RuntimeRunStatusDocument
            {
                ObservedUtc = DateTimeOffset.UtcNow,
                Machine = Environment.MachineName,
                WorldUid = WorldUid,
                Runs = _runs.Registry.List().Select(value => new RuntimeRunStatusEntry
                {
                    RunId = value.RunId,
                    ScopeId = value.ScopeId,
                    ExperienceId = value.Scope.ExperienceId,
                    BindingZdo = value.Scope.BindingZdo,
                    ParticipantIds = value.Scope.ParticipantIds,
                    ContentHash = value.Scope.ContentHash,
                    StageId = value.Outcome is null ? "start" : null,
                    Outcome = value.Outcome,
                    RewardPolicy = value.RewardPolicy,
                }).ToArray(),
            });
        }

        void WriteReceipt(RuntimeRunControlRequest request, string state, string detail,
            RuntimeResetPreview? preview = null, RuntimeResetResult? result = null,
            IReadOnlyList<RuntimeBindingCandidate>? candidates = null, RuntimeBindingChange? change = null)
        {
            var receipt = new RuntimeRunControlReceipt
            {
                RequestId = request.RequestId,
                Operation = request.Operation,
                State = state,
                Detail = detail,
                Machine = Environment.MachineName,
                WorldUid = WorldUid,
                CompletedUtc = DateTimeOffset.UtcNow,
                Preview = preview,
                Result = result,
                BindingCandidates = candidates,
                BindingChange = change,
            };
            WriteControlReceipt(RuntimeRunControlReceipts.Scope(request.RunId), receipt, prune: true);
            var addressed = result?.NewRunId ?? request.RunId;
            var run = string.IsNullOrWhiteSpace(addressed) ? null : _runs.Registry.Find(addressed);
            _receipts.Write(new RuntimeReceipt
            {
                Operation = request.Operation,
                Status = state,
                Error = state is "completed" or "previewed" ? null : detail,
                RunId = run?.RunId ?? addressed,
                WorldId = run?.Scope.WorldId ?? change?.WorldId,
                ExperienceId = run?.Scope.ExperienceId ?? request.ExperienceId ?? change?.Applied?.ExperienceId,
                ContentHash = run?.Scope.ContentHash ?? change?.Applied?.ContentHash,
                BindingZdo = run?.Scope.BindingZdo ?? request.BindingZdo,
                CorrelationId = change?.ChangeId ?? result?.ResetId ?? preview?.PreviewToken ?? request.RequestId,
                CandidateCount = candidates?.Count ?? 0,
                Diagnostics = Array.Empty<ContractDiagnostic>(),
            });
            WriteStatus();
        }

        string WriteControlReceipt(string scope, RuntimeRunControlReceipt receipt, bool prune)
        {
            var path = RuntimeRunControlReceipts.ReceiptPath(_run.RuntimeRoot, scope, receipt.RequestId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(receipt, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            if (prune)
                ReceiptRetention.Archive(
                    RuntimeRunControlReceipts.ScopeDirectory(_run.RuntimeRoot, scope),
                    RuntimeRunControlReceipts.ArchiveScopeDirectory(_run.RuntimeRoot, scope),
                    RuntimeRunControlReceipts.MaxPerScope, TimeSpan.Zero, DateTimeOffset.UtcNow);
            return path;
        }

        public RetentionProof CrossRetentionBoundary(string runA, string runB)
        {
            var scopeA = RuntimeRunControlReceipts.Scope(runA);
            var liveA = RuntimeRunControlReceipts.ScopeDirectory(_run.RuntimeRoot, scopeA);
            var initial = Directory.Exists(liveA) ? Directory.GetFiles(liveA, "*.json").Length : 0;
            var toWrite = Math.Max(1, RuntimeRunControlReceipts.MaxPerScope - initial + 1);
            string? firstId = null;
            byte[]? firstBytes = null;
            for (var index = 0; index < toWrite; index++)
            {
                var requestId = $"retention-a-{index:D3}";
                var receipt = RetentionReceipt(requestId, runA, index);
                var path = WriteControlReceipt(scopeA, receipt, prune: false);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10).AddMilliseconds(index * 10));
                if (index == 0)
                {
                    firstId = requestId;
                    firstBytes = File.ReadAllBytes(path);
                }
                ReceiptRetention.Archive(liveA, RuntimeRunControlReceipts.ArchiveScopeDirectory(_run.RuntimeRoot, scopeA),
                    RuntimeRunControlReceipts.MaxPerScope, TimeSpan.Zero, DateTimeOffset.UtcNow);
            }
            var archivedPath = RuntimeRunControlReceipts.ArchivedReceiptPath(_run.RuntimeRoot, scopeA, firstId!);
            Assert.True(File.Exists(archivedPath), "The boundary-crossing A receipt was not archived.");
            Assert.Equal(RuntimeRunControlReceipts.MaxPerScope, Directory.GetFiles(liveA, "*.json").Length);

            const string otherId = "retention-b-still-readable";
            var bPath = WriteControlReceipt(RuntimeRunControlReceipts.Scope(runB), RetentionReceipt(otherId, runB, 0), prune: true);
            Assert.True(File.Exists(bPath));
            return new(firstId!, archivedPath, firstBytes!, otherId);
        }

        static RuntimeRunControlReceipt RetentionReceipt(string requestId, string runId, int sequence) => new()
        {
            RequestId = requestId,
            Operation = "preview_reset",
            State = "previewed",
            Detail = "retention_boundary_proof",
            Machine = Environment.MachineName,
            WorldUid = WorldUid,
            CompletedUtc = DateTimeOffset.UtcNow.AddMilliseconds(sequence),
            Preview = new RuntimeResetPreview
            {
                PreviewToken = "retention-proof-" + sequence,
                RunId = runId,
                ScopeId = "retention-scope",
                CreatedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                SnapshotHash = "retention",
                Snapshot = new RuntimeResetSnapshot(),
            },
        };

        static IReadOnlyDictionary<string, ExperienceDocument> ReadExperiences(PackCandidate candidate)
        {
            using var archive = ZipFile.OpenRead(candidate.Path);
            return archive.Entries
                .Where(value => value.FullName.StartsWith("experiences/", StringComparison.Ordinal)
                    && value.FullName.EndsWith(".json", StringComparison.Ordinal))
                .Select(value =>
                {
                    using var reader = new StreamReader(value.Open());
                    var compiled = ExperienceCompiler.CompileProductionJson(reader.ReadToEnd());
                    Assert.Empty(compiled.Diagnostics);
                    return Assert.IsType<ExperienceDocument>(compiled.Document);
                })
                .ToDictionary(value => value.Id, StringComparer.Ordinal);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try { await _pump; }
            catch (OperationCanceledException) { }
            _stop.Dispose();
        }

        sealed class SyntheticBindingAdapter : IRuntimeBindingAdapter
        {
            readonly object _gate = new();
            RuntimeBindingReference _current = new();

            public IReadOnlyList<RuntimeBindingCandidate> ListCandidates() => new[]
            {
                new RuntimeBindingCandidate { BindingZdo = BindingZdo, TargetKind = "sign", Label = "Synthetic authoring sign", DistanceMetres = 2.5 },
            };

            public RuntimeBindingReference Read(string bindingZdo)
            {
                lock (_gate) return Clone(_current);
            }

            public bool TryWrite(string bindingZdo, RuntimeBindingReference reference, out string error)
            {
                lock (_gate) _current = Clone(reference);
                error = string.Empty;
                return true;
            }

            static RuntimeBindingReference Clone(RuntimeBindingReference? value) => new()
            {
                PackId = value?.PackId,
                ExperienceId = value?.ExperienceId,
                BindingId = value?.BindingId,
                Version = value?.Version,
                ContentHash = value?.ContentHash,
            };
        }

        sealed class SyntheticSpawnResetAdapter : IRuntimeSpawnResetAdapter
        {
            public static readonly SyntheticSpawnResetAdapter Instance = new();
            public RuntimeSpawnResetObservation Inspect(SpawnedObject value) => new() { State = "already_absent" };
            public RuntimeSpawnResetObservation Cleanup(SpawnedObject value) => new() { State = "already_absent" };
        }
    }

    sealed class SyntheticRuntimeFixture
    {
        readonly SyntheticRun _run;
        readonly PackCandidate _candidate;
        readonly ExperienceDocument _document;
        readonly RuntimeReceiptStore _receipts;
        DateTimeOffset _nextReceiptAt = DateTimeOffset.UtcNow.AddSeconds(1);
        string? _activationId;
        DateTimeOffset? _stageEnteredUtc;
        int _correlationSequence;

        SyntheticRuntimeFixture(SyntheticRun run, PackCandidate candidate)
        {
            _run = run;
            _candidate = candidate;
            _document = ReadExperience(candidate);
            _receipts = new RuntimeReceiptStore(run.RuntimeRoot);
        }

        public string Version => _candidate.Manifest.Version;
        public string ActivationId => _activationId ?? throw new InvalidOperationException("synthetic_activation_missing");
        public string ShortActivationId => ActivationId[^8..];

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
            using var active = JsonDocument.Parse(File.ReadAllText(Path.Combine(_run.RuntimeRoot, "active", "active-set.json")));
            _activationId = active.RootElement.GetProperty("activation_id").GetString();
            _stageEnteredUtc = active.RootElement.GetProperty("activated_utc").GetDateTimeOffset();
            Assert.Matches("^act-[0-9]{8}T[0-9]{9}Z-[0-9a-f]{8}$", ActivationId);
            WriteForPack(new RuntimeReceipt { Operation = "load", Status = "activated", CorrelationId = "corr-synthetic-load" });
        }

        public void WriteBound() => WriteForPack(new RuntimeReceipt
        {
            Operation = "bind",
            Status = "inscribed",
            BindingZdo = "synthetic:0:1",
            CorrelationId = "corr-synthetic-bind"
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
                RequiredCount = required,
                CorrelationId = "corr-synthetic-" + eventName
            });
        }

        public void WriteAdvanced(string eventName)
        {
            var (stage, transition) = RouteForEvent(eventName);
            Assert.False(string.IsNullOrWhiteSpace(transition.NextStage));
            var correlationId = "corr-synthetic-" + transition.Id;
            var eventAt = _nextReceiptAt;
            var evidence = EvidenceFor(transition, eventAt);
            WriteForPack(new RuntimeReceipt
            {
                Operation = "event", Status = "matched", StageId = stage.Id, CurrentStageId = stage.Id,
                NextStageId = transition.NextStage, EventName = eventName, TransitionId = transition.Id,
                CorrelationId = correlationId, Evidence = evidence,
                EvidenceKind = CreatorEvidenceLine.KindName(CreatorEvidenceKind.Story)
            });
            foreach (var action in transition.Actions ?? [])
                WriteForPack(new RuntimeReceipt
                {
                    Operation = "action", Status = "applied", StageId = stage.Id, CurrentStageId = stage.Id,
                    TransitionId = transition.Id, ActionId = action.Id, CorrelationId = correlationId
                });
            WriteForPack(new RuntimeReceipt
            {
                Operation = "transition",
                Status = "advanced",
                StageId = stage.Id,
                CurrentStageId = stage.Id,
                NextStageId = transition.NextStage,
                TransitionId = transition.Id,
                EventName = eventName,
                CorrelationId = correlationId,
                StageEnteredUtc = eventAt,
                Evidence = evidence
            });
            _stageEnteredUtc = eventAt;
        }

        public void WriteComplete()
        {
            var matches = _document.Stages
                .SelectMany(stage => (stage.Transitions ?? []).Select(transition => (stage, transition)))
                .Where(value => value.transition.Outcome is "complete" or "fail")
                .ToArray();
            var (stage, transition) = Assert.Single(matches);
            var eventName = FirstEvent(transition.When).Event;
            var correlationId = "corr-synthetic-" + transition.Id;
            var eventAt = _nextReceiptAt;
            var evidence = EvidenceFor(transition, eventAt);
            WriteForPack(new RuntimeReceipt
            {
                Operation = "event", Status = "matched", StageId = stage.Id, CurrentStageId = stage.Id,
                EventName = eventName, TransitionId = transition.Id, CorrelationId = correlationId, Evidence = evidence
            });
            foreach (var action in transition.Actions ?? [])
                WriteForPack(new RuntimeReceipt
                {
                    Operation = "action", Status = "applied", StageId = stage.Id, CurrentStageId = stage.Id,
                    TransitionId = transition.Id, ActionId = action.Id, CorrelationId = correlationId
                });
            WriteForPack(new RuntimeReceipt
            {
                Operation = "transition",
                Status = transition.Outcome,
                StageId = stage.Id,
                CurrentStageId = stage.Id,
                TransitionId = transition.Id,
                EventName = eventName,
                CorrelationId = correlationId,
                StageEnteredUtc = _stageEnteredUtc,
                Evidence = evidence
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

        static TriggerExpression FirstEvent(TriggerExpression trigger) =>
            string.Equals(trigger.Op, "EVENT", StringComparison.OrdinalIgnoreCase)
                ? trigger
                : FirstEvent(Assert.Single(trigger.Children ?? []));

        static TriggerClauseTrace EvidenceFor(ExperienceTransition transition, DateTimeOffset eventAt)
        {
            var history = EvidenceEvents(transition.When, eventAt).ToArray();
            var evidence = TriggerEvaluator.Explain(transition.When, history);
            Assert.True(evidence.Satisfied);
            return evidence;
        }

        static IEnumerable<RuntimeEvent> EvidenceEvents(TriggerExpression trigger, DateTimeOffset eventAt)
        {
            if (string.Equals(trigger.Op, "EVENT", StringComparison.OrdinalIgnoreCase))
            {
                yield return new RuntimeEvent
                {
                    Name = trigger.Event, Target = trigger.Target, At = eventAt,
                    Fields = trigger.Where ?? new Dictionary<string, string>()
                };
                yield break;
            }
            var repetitions = string.Equals(trigger.Op, "COUNT", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(1, trigger.Count.GetValueOrDefault()) : 1;
            for (var repeat = 0; repeat < repetitions; repeat++)
                foreach (var child in trigger.Children ?? [])
                    foreach (var evt in EvidenceEvents(child, eventAt.AddMilliseconds(repeat)))
                        yield return evt;
        }

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
            receipt.ActivationId ??= _activationId;
            receipt.CorrelationId ??= $"corr-synthetic-{++_correlationSequence:D3}";
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

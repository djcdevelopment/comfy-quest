using System.Text.Json;
using ComfyQuestContracts;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Xunit;

namespace Comfy.Quest.Studio.Tests;

public sealed class QuestStudioCampaignPlayTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "quest-studio-campaign-play-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PrerequisiteReceiptRequiresTheExactFixtureOwnedBindingAnchor()
    {
        const string operationId = "campaign-play-20260829T120000000Z-deadbeef";
        var valid = PrerequisiteJson(operationId, "1:9", "sign");
        var parsed = StudioCampaignPlayPrerequisiteRunner.Parse(operationId, valid);
        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal("1:9", parsed.Value!.BindingAnchorZdo);
        Assert.Equal("fixture-preparation", parsed.Value.FixtureProofLevel);

        var wrongKind = StudioCampaignPlayPrerequisiteRunner.Parse(operationId,
            PrerequisiteJson(operationId, "1:9", "player_built_piece"));
        Assert.False(wrongKind.Ok);
        Assert.Equal("campaign_play_fixture_evidence_invalid", wrongKind.Error);

        var wrongMatcher = StudioCampaignPlayPrerequisiteRunner.Parse(operationId,
            PrerequisiteJson(operationId, "1:9", "sign").Replace("$enemy_drake", "$enemy_troll"));
        Assert.False(wrongMatcher.Ok);
        Assert.Equal("campaign_play_fixture_evidence_invalid", wrongMatcher.Error);
    }

    [Fact]
    public async Task OnePlayOperationArmsThenPublishesExactBytesAndBindsStartsTheUniqueRoot()
    {
        var valheim = Path.Combine(_root, "valheim");
        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        Directory.CreateDirectory(runtimeRoot);
        var host = new FakeHost(_root, valheim);
        var dev = new RuntimeDevChannelCoordinator(runtimeRoot);
        var prerequisites = new FakePrerequisites(runtimeRoot, dev);
        var service = new QuestStudioService(host, new QuestPackPublisher(host), prerequisites);
        var built = BuildSignatureCampaign(service);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var requests = new List<string>();
        var runtime = SimulateRuntimeAsync(runtimeRoot, dev, requests, stop.Token);
        StudioCampaignPublishResult played;
        try
        {
            played = await service.PlayCampaignAsync(built.GuildId, built.CampaignId,
                new StudioCampaignPublishRequest(built.CampaignRevision), stop.Token);
        }
        finally
        {
            stop.Cancel();
            try { await runtime; } catch (OperationCanceledException) { }
        }

        Assert.True(played.Ok, played.Error);
        Assert.Equal("started", played.Status);
        Assert.NotNull(played.PlayReceipt);
        Assert.Equal(new[]
        {
            "certify", "prepare_launch", "arm", "fixture_prepare", "publish", "activation",
            "binding_candidates", "bind_start",
        }, played.PlayReceipt!.Stages.Select(value => value.Stage));
        Assert.Equal(new[] { "list_binding_candidates", "bind_selected_experience" }, requests);
        Assert.Equal("1:9", played.PlayReceipt.BindingAnchorZdo);
        Assert.Equal(built.FirstExperienceId, played.PlayReceipt.FirstExperienceId);
        Assert.Equal("applied-change", played.PlayReceipt.BindingChangeId);
        Assert.Equal("binding-instance", played.PlayReceipt.BindingInstanceId);
        Assert.Equal(new[] { "$enemy_deathsquito", "$enemy_drake" },
            played.PlayReceipt.FixtureTargets.Select(value => value.MatcherTarget));
        Assert.Equal(new string('a', 64), played.PlayReceipt.FixtureReceiptSha256);
        Assert.Contains("not kill or completion proof", played.PlayReceipt.Limitations[0], StringComparison.OrdinalIgnoreCase);

        using var evidence = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            service.CampaignEvidence(built.GuildId, built.CampaignId), HostJson()));
        var stored = Assert.Single(evidence.RootElement.GetProperty("play_receipts").EnumerateArray());
        Assert.Equal("started", stored.GetProperty("state").GetString());
    }

    [Fact]
    public async Task AmbiguousCampaignEntryStopsBeforeMachineOrWorldWork()
    {
        var valheim = Path.Combine(_root, "valheim-ambiguous");
        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        Directory.CreateDirectory(runtimeRoot);
        var host = new FakeHost(_root, valheim);
        var prerequisites = new FakePrerequisites(runtimeRoot, new RuntimeDevChannelCoordinator(runtimeRoot));
        var service = new QuestStudioService(host, new QuestPackPublisher(host), prerequisites);
        var built = BuildSignatureCampaign(service, linkSuccessor: false);

        var played = await service.PlayCampaignAsync(built.GuildId, built.CampaignId,
            new StudioCampaignPublishRequest(built.CampaignRevision), CancellationToken.None);

        Assert.False(played.Ok);
        Assert.Equal("campaign_entry_ambiguous", played.Error);
        Assert.Equal(0, prerequisites.Calls);
        Assert.False(Directory.Exists(Path.Combine(runtimeRoot, "inbox-dev")));
    }

    [Fact]
    public async Task FixedFixtureRefusesDifferentOrDuplicateConfiguredTargetsBeforeWorldWork()
    {
        var valheim = Path.Combine(_root, "valheim-target-mismatch");
        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        Directory.CreateDirectory(runtimeRoot);
        var host = new FakeHost(_root, valheim);
        var prerequisites = new FakePrerequisites(runtimeRoot, new RuntimeDevChannelCoordinator(runtimeRoot));
        var service = new QuestStudioService(host, new QuestPackPublisher(host), prerequisites);
        var built = BuildSignatureCampaign(service, firstTarget: "$enemy_troll", secondTarget: "$enemy_troll");

        var played = await service.PlayCampaignAsync(built.GuildId, built.CampaignId,
            new StudioCampaignPublishRequest(built.CampaignRevision), CancellationToken.None);

        Assert.False(played.Ok);
        Assert.Equal("signature_hunt_fixture_target_mismatch", played.Error);
        Assert.Equal(0, prerequisites.Calls);
    }

    [Fact]
    public async Task WorldOrSessionIdentityChangeStopsBeforeAnyBindingRequest()
    {
        var valheim = Path.Combine(_root, "valheim-identity-change");
        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        Directory.CreateDirectory(runtimeRoot);
        var host = new FakeHost(_root, valheim);
        var dev = new RuntimeDevChannelCoordinator(runtimeRoot);
        var prerequisites = new FakePrerequisites(runtimeRoot, dev);
        var service = new QuestStudioService(host, new QuestPackPublisher(host), prerequisites);
        var built = BuildSignatureCampaign(service);
        var requests = new List<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var runtime = SimulateRuntimeAsync(runtimeRoot, dev, requests, stop.Token, "OTHER-MACHINE");
        StudioCampaignPublishResult played;
        try
        {
            played = await service.PlayCampaignAsync(built.GuildId, built.CampaignId,
                new StudioCampaignPublishRequest(built.CampaignRevision), stop.Token);
        }
        finally
        {
            stop.Cancel();
            try { await runtime; } catch (OperationCanceledException) { }
        }

        Assert.False(played.Ok);
        Assert.Equal("campaign_runtime_identity_changed", played.Error);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task StartedClaimRequiresExactAppliedBindingEvidence()
    {
        var valheim = Path.Combine(_root, "valheim-binding-mismatch");
        var runtimeRoot = Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime");
        Directory.CreateDirectory(runtimeRoot);
        var host = new FakeHost(_root, valheim);
        var dev = new RuntimeDevChannelCoordinator(runtimeRoot);
        var prerequisites = new FakePrerequisites(runtimeRoot, dev);
        var service = new QuestStudioService(host, new QuestPackPublisher(host), prerequisites);
        var built = BuildSignatureCampaign(service);
        var requests = new List<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var runtime = SimulateRuntimeAsync(runtimeRoot, dev, requests, stop.Token, exactBinding: false);
        StudioCampaignPublishResult played;
        try
        {
            played = await service.PlayCampaignAsync(built.GuildId, built.CampaignId,
                new StudioCampaignPublishRequest(built.CampaignRevision), stop.Token);
        }
        finally
        {
            stop.Cancel();
            try { await runtime; } catch (OperationCanceledException) { }
        }

        Assert.False(played.Ok);
        Assert.Equal("campaign_bind_start_evidence_mismatch", played.Error);
        Assert.Equal(new[] { "list_binding_candidates", "bind_selected_experience" }, requests);
        Assert.Equal("bind_start", played.PlayReceipt?.FailedStage);
    }

    [Fact]
    public void PrerequisiteEdgesDriveContinuationEvenWhenDisplayOrderIsReversed()
    {
        var valheim = Path.Combine(_root, "valheim-reversed-order");
        Directory.CreateDirectory(valheim);
        var host = new FakeHost(_root, valheim);
        var service = new QuestStudioService(host, new QuestPackPublisher(host));
        var built = BuildSignatureCampaign(service, reverseOrder: true);

        var certified = service.CertifyCampaign(built.GuildId, built.CampaignId);

        Assert.True(certified.Ok, certified.Error);
        var entry = Assert.Single(certified.CompilationReceipt!.Experiences,
            value => value.ExperienceId == built.FirstExperienceId);
        Assert.Equal(built.SecondExperienceId, Assert.Single(entry.SuccessorExperienceIds));
    }

    async Task SimulateRuntimeAsync(string runtimeRoot, RuntimeDevChannelCoordinator dev,
        List<string> operations, CancellationToken cancellationToken, string statusMachine = "TEST-MACHINE",
        bool exactBinding = true)
    {
        var requestPath = Path.Combine(runtimeRoot, "requests", "run-control.json");
        var status = new RuntimeRunStatusStore(runtimeRoot);
        while (!cancellationToken.IsCancellationRequested)
        {
            dev.Poll(DateTimeOffset.UtcNow);
            status.Write(new RuntimeRunStatusDocument
            {
                ObservedUtc = DateTimeOffset.UtcNow,
                Machine = statusMachine,
                WorldUid = "-7600395338659582326",
                Runs = Array.Empty<RuntimeRunStatusEntry>(),
            });
            if (File.Exists(requestPath))
            {
                RuntimeRunControlRequest? request = null;
                try { request = JsonConvert.DeserializeObject<RuntimeRunControlRequest>(File.ReadAllText(requestPath)); }
                catch (IOException) { }
                if (request is not null)
                {
                    File.Delete(requestPath);
                    operations.Add(request.Operation);
                    var receipt = new RuntimeRunControlReceipt
                    {
                        RequestId = request.RequestId,
                        Operation = request.Operation,
                        State = "completed",
                        Detail = request.Operation == "list_binding_candidates"
                            ? "binding_candidates_ready" : "experience_bound:" + request.ExperienceId,
                        Machine = request.ExpectedMachine,
                        WorldUid = request.ExpectedWorldUid,
                        CreatorSessionId = request.CreatorSessionId,
                        CompletedUtc = DateTimeOffset.UtcNow,
                        BindingCandidates = request.Operation == "list_binding_candidates"
                            ? new[] { new RuntimeBindingCandidate { BindingZdo = "1:9", TargetKind = "sign", Label = "Spear loadout", DistanceMetres = 7 } }
                            : null,
                        BindingChange = request.Operation == "bind_selected_experience"
                            ? AppliedBinding(runtimeRoot, request, exactBinding)
                            : null,
                    };
                    var receiptPath = RuntimeRunControlReceipts.ReceiptPath(runtimeRoot,
                        RuntimeRunControlReceipts.PackScope, request.RequestId);
                    Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
                    File.WriteAllText(receiptPath, JsonConvert.SerializeObject(receipt, Formatting.Indented));
                }
            }
            await Task.Delay(25, cancellationToken);
        }
    }

    static RuntimeBindingChange AppliedBinding(
        string runtimeRoot, RuntimeRunControlRequest request, bool exactBinding)
    {
        var active = new QuestPackStore(runtimeRoot).ReadActive();
        return new RuntimeBindingChange
        {
            ChangeId = "applied-change",
            State = "applied",
            BindingZdo = request.BindingZdo,
            WorldId = request.ExpectedWorldUid,
            Applied = new RuntimeBindingReference
            {
                PackId = active.PackId,
                Version = active.Version,
                ExperienceId = exactBinding ? request.ExperienceId : "wrong-experience",
                BindingId = "default",
                ContentHash = active.ContentHash,
                BindingInstanceId = "binding-instance",
            },
        };
    }

    BuiltCampaign BuildSignatureCampaign(QuestStudioService service, bool linkSuccessor = true,
        string firstTarget = "$enemy_deathsquito", string secondTarget = "$enemy_drake",
        bool reverseOrder = false)
    {
        var guild = service.CreateGuild(new StudioGuildCreateRequest("Slayers Creative System", "Derek"));
        var absorbed = service.ImportSource(guild.GuildId, new StudioSourceImportRequest(guild.Revision,
            "slayers-e17", "Slayers Era 17", """
            {"schema_version":1,"guild":"Slayers","era":17,"source":{"kind":"sheet-xlsx"},"quests":[
              {"quest_id":"air_drop"},{"quest_id":"cold_shot"},{"quest_id":"can_i_pick_your_brain_3"}]}
            """, """
            {"schema_version":1,"mode":"sheet","source":{"id":"slayers"},"anomalies":[],"counts":{"quests":3,"anomalies":0}}
            """, "# none\n"));
        Assert.True(absorbed.Ok, absorbed.Error);

        var canonical = service.CreateProject("blank");
        canonical.Title = "Slayers Signature Hunt";
        var route = canonical.Nodes[0].Routes[0];
        route.Event = "kill";
        route.Target = "$enemy_deathsquito";
        route.Where = new() { ["weapon_skill"] = "Spears", ["projectile"] = "true" };
        Assert.True(service.SaveDraft(canonical.ProjectId, new(canonical.Revision, canonical)).Ok);
        canonical = service.ReadProject(canonical.ProjectId)!;
        var promoted = service.PromoteAbstraction(guild.GuildId, new StudioAbstractionPromoteRequest(
            absorbed.Guild!.Revision, "slayers-signature-hunt", "Slayers Signature Hunt", "Thrown spear finish.",
            "slayers-e17", new[] { "air_drop", "cold_shot", "can_i_pick_your_brain_3" }, canonical.ProjectId,
            route.Id, route.Actions[0].Id, "Slayers Era 17", "runtime", "Local Runtime only.",
            new[]
            {
                new StudioAbstractionTargetChoice { Id = "deathsquito", Label = "Deathsquito", RuntimeTarget = firstTarget, SourceQuestId = "air_drop" },
                new StudioAbstractionTargetChoice { Id = "drake", Label = "Drake", RuntimeTarget = secondTarget, SourceQuestId = "cold_shot" },
            }));
        Assert.True(promoted.Ok, promoted.Error);
        var air = service.InstantiateAbstraction(guild.GuildId, "slayers-signature-hunt", new(
            promoted.Guild!.Revision, 1, "Air Drop", "deathsquito", "Drop it.", "Done.", "Creator"));
        var cold = service.InstantiateAbstraction(guild.GuildId, "slayers-signature-hunt", new(
            air.Guild!.Revision, 1, "Cold Shot", "drake", "Drop it cold.", "Done cold.", "Creator"));
        Assert.True(air.Ok && cold.Ok, air.Error ?? cold.Error);
        var campaign = cold.Guild!.Campaigns.Single(value => value.CampaignId == "campaign-default");
        var placedAir = service.PlaceInCampaign(guild.GuildId, campaign.CampaignId,
            new(campaign.Revision, air.Project!.ProjectId, "quest", "questline", "main", null));
        var placedCold = service.PlaceInCampaign(guild.GuildId, campaign.CampaignId,
            new(placedAir.Campaign!.Revision, cold.Project!.ProjectId, "quest", "questline", "main", null));
        campaign = Clone(placedCold.Campaign!);
        if (linkSuccessor) campaign.Questlines[0].Quests[1].PrerequisiteProjectIds.Add(air.Project.ProjectId);
        if (reverseOrder) campaign.Questlines[0].Quests.Reverse();
        var saved = service.SaveCampaign(guild.GuildId, campaign.CampaignId, new(campaign.Revision, campaign));
        Assert.True(saved.Ok, saved.Error);
        return new(guild.GuildId, campaign.CampaignId, saved.Campaign!.Revision,
            air.Project.ExperienceId, cold.Project.ExperienceId);
    }

    static string PrerequisiteJson(string operationId, string zdo, string kind) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = "comfy-quest-studio-campaign-play-prerequisites/v1",
            operation_id = operationId,
            state = "ready",
            creator_session_id = "creator-test",
            resumed_running_session = false,
            machine = "TEST-MACHINE",
            world_uid = "-7600395338659582326",
            fixture_request_id = "fixture-request",
            fixture_receipt_path = "fixture.json",
            fixture_receipt_sha256 = new string('a', 64),
            fixture = new
            {
                schema = "comfy-questlab-signature-hunt-fixture/v1",
                fixture_id = "slayers-signature-hunt",
                fixture_revision = 2,
                state = "ready",
                proof_level = "fixture-preparation",
                disclaimer = "Fixed fixture preparation only; not live kill or completion proof.",
                request_id = "fixture-request",
                preparation_id = "fixture-preparation-test",
                machine = "TEST-MACHINE",
                world_name = "ComfyQuestDemo",
                world_uid = "-7600395338659582326",
                objects = new { expected = 20, standing_at_capture = 20 },
                targets = new[]
                {
                    new { role = "target-deathsquito", prefab = "Deathsquito", raw_m_name = "$enemy_deathsquito", matcher_target = "$enemy_deathsquito", captured_from = "Character.m_name", zdo_id = "1:10" },
                    new { role = "target-drake", prefab = "Hatchling", raw_m_name = "$enemy_drake", matcher_target = "$enemy_drake", captured_from = "Character.m_name", zdo_id = "1:11" },
                },
                binding_anchor = new { role = "marker-loadout-sign", target_kind = kind, zdo_id = zdo },
            },
        }, HostJson());

    static T Clone<T>(T value) => System.Text.Json.JsonSerializer.Deserialize<T>(
        System.Text.Json.JsonSerializer.Serialize(value, HostJson()), HostJson())!;
    static JsonSerializerOptions HostJson() => new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    sealed record BuiltCampaign(
        string GuildId, string CampaignId, int CampaignRevision,
        string FirstExperienceId, string SecondExperienceId);

    sealed class FakePrerequisites(string runtimeRoot, RuntimeDevChannelCoordinator dev) : IStudioCampaignPlayPrerequisiteRunner
    {
        public int Calls { get; private set; }

        public Task<StudioCampaignPlayPrerequisiteResult> EnsureAsync(string operationId, CancellationToken cancellationToken)
        {
            Calls++;
            dev.Arm(DateTimeOffset.UtcNow);
            var worldEntry = new RuntimeWorldEntryReceipt
            {
                RequestId = "world-entry-test", CreatorSessionId = "creator-test", State = "entered",
                Machine = "TEST-MACHINE", ExpectedWorldUid = "-7600395338659582326", WorldUid = "-7600395338659582326",
                WorldName = "ComfyQuestDemo", WorldDisplayName = "ComfyQuestDemo", CharacterProfile = "questyfour",
                CompletedUtc = DateTimeOffset.UtcNow,
            };
            var path = Path.Combine(runtimeRoot, "status", "world-entry.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(worldEntry, Formatting.Indented));
            return Task.FromResult(StudioCampaignPlayPrerequisiteResult.Success(new(
                operationId, "creator-test", false, "TEST-MACHINE", "-7600395338659582326",
                "fixture-request", "fixture-preparation-test", "fixture.json", new string('a', 64),
                "fixture-preparation", "Fixed fixture preparation only; not live kill or completion proof.",
                new[]
                {
                    new StudioCampaignFixtureTarget { Role = "target-deathsquito", MatcherTarget = "$enemy_deathsquito", ZdoId = "1:10" },
                    new StudioCampaignFixtureTarget { Role = "target-drake", MatcherTarget = "$enemy_drake", ZdoId = "1:11" },
                }, "1:9")));
        }
    }

    sealed class FakeHost(string stateDirectory, string valheim) : IQuestStudioHost
    {
        public string StateDirectory { get; } = stateDirectory;
        public string? FindValheim() => valheim;
        public bool Authorize(HttpRequest request) => true;
        public JsonSerializerOptions Json { get; } = HostJson();
    }
}

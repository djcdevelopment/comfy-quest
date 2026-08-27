using System.Text.Json;
using Comfy.Quest.Studio;
using ComfyQuestContracts;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Comfy.Quest.Studio.Tests;

public sealed class QuestStudioRunControlTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "comfy-quest-run-control-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void StatusReturnsOnlyFreshRunsForTheSelectedExperience()
    {
        var (service, runtimeRoot, project) = CreateService();
        WriteStatus(runtimeRoot, project.ExperienceId, includeOther: true);

        var status = service.RunStatus(project.ProjectId);
        Assert.True(status.Available);
        Assert.True(status.Connected);
        Assert.Null(status.Error);
        Assert.Equal("OMEN", status.Machine);
        Assert.Equal("123", status.WorldUid);
        Assert.Equal("run-exact", Assert.Single(status.Runs).RunId);
    }

    [Fact]
    public async Task StaleStatusCannotQueueAStateChangingRequest()
    {
        var (service, runtimeRoot, project) = CreateService();
        WriteStatus(runtimeRoot, project.ExperienceId, observedUtc: DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await service.PreviewResetAsync(project.ProjectId, new StudioRunResetRequest("run-exact"), CancellationToken.None);
        Assert.False(result.Ok);
        Assert.False(result.Queued);
        Assert.Equal("runtime_run_status_stale", result.Error);
        Assert.False(File.Exists(Path.Combine(runtimeRoot, "requests", "run-control.json")));
    }

    [Fact]
    public async Task FreshMainMenuHeartbeatIsNotMistakenForALoadedWorld()
    {
        var (service, runtimeRoot, project) = CreateService();
        WriteStatus(runtimeRoot, project.ExperienceId, worldUid: string.Empty);

        var status = service.RunStatus(project.ProjectId);
        Assert.True(status.Available);
        Assert.False(status.Connected);
        Assert.Equal("runtime_world_not_loaded", status.Error);
        var result = await service.PreviewResetAsync(project.ProjectId, new StudioRunResetRequest("run-exact"), CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal("runtime_world_not_loaded", result.Error);
        Assert.False(File.Exists(Path.Combine(runtimeRoot, "requests", "run-control.json")));
    }

    [Fact]
    public async Task MissingCompletedCreatorSessionCannotReachRunControlMailbox()
    {
        var (service, runtimeRoot, project) = CreateService();
        WriteStatus(runtimeRoot, project.ExperienceId, includeWorldEntry: false);

        var result = await service.BindingCandidatesAsync(project.ProjectId, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.False(result.Queued);
        Assert.Equal("creator_session_unavailable", result.Error);
        Assert.False(File.Exists(Path.Combine(runtimeRoot, "requests", "run-control.json")));
    }

    [Fact]
    public async Task PreviewMailboxPinsMachineWorldAndRunThenReturnsTheExactSnapshot()
    {
        var (service, runtimeRoot, project) = CreateService();
        WriteStatus(runtimeRoot, project.ExperienceId);
        var preview = new RuntimeResetPreview
        {
            PreviewToken = "rstp-proof",
            RunId = "run-exact",
            ScopeId = "scope-exact",
            CreatedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            SnapshotHash = "snapshot-hash",
            Snapshot = new RuntimeResetSnapshot { WorkflowPresent = true, TimerIds = new[] { "clock" }, ActionClaimCount = 2 },
        };
        var watcher = AnswerOnce(runtimeRoot, request => new RuntimeRunControlReceipt
        {
            RequestId = request.RequestId,
            Operation = request.Operation,
            State = "previewed",
            Machine = "OMEN",
            WorldUid = "123",
            CreatorSessionId = "creator-session-test",
            CompletedUtc = DateTimeOffset.UtcNow,
            Preview = preview,
        });

        var result = await service.PreviewResetAsync(project.ProjectId, new StudioRunResetRequest("run-exact"), CancellationToken.None);
        var request = await watcher;
        Assert.True(result.Ok, result.Error);
        Assert.False(result.Queued);
        Assert.Equal("previewed", result.Receipt!.State);
        Assert.Equal("rstp-proof", result.Receipt.Preview!.PreviewToken);
        Assert.Equal("preview_reset", request.Operation);
        Assert.Equal("OMEN", request.ExpectedMachine);
        Assert.Equal("123", request.ExpectedWorldUid);
        Assert.Equal("creator-session-test", request.CreatorSessionId);
        Assert.Equal("run-exact", request.RunId);
        Assert.False(request.ConfirmReset);
    }

    [Fact]
    public async Task ReceiptFromAnotherCreatorSessionIsNotAcceptedAsProof()
    {
        var (service, runtimeRoot, project) = CreateService();
        WriteStatus(runtimeRoot, project.ExperienceId);
        var watcher = AnswerOnce(runtimeRoot, request => new RuntimeRunControlReceipt
        {
            RequestId = request.RequestId, Operation = request.Operation, State = "previewed",
            Machine = "OMEN", WorldUid = "123", CreatorSessionId = "different-session",
            CompletedUtc = DateTimeOffset.UtcNow,
            Preview = new RuntimeResetPreview
            {
                PreviewToken = "rstp-forged", RunId = "run-exact", ScopeId = "scope-exact",
                CreatedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                SnapshotHash = "snapshot-hash", Snapshot = new RuntimeResetSnapshot(),
            },
        });

        var result = await service.PreviewResetAsync(project.ProjectId,
            new StudioRunResetRequest("run-exact"), CancellationToken.None);
        await watcher;

        Assert.False(result.Ok);
        Assert.False(result.Queued);
        Assert.Equal("run_control_receipt_identity_mismatch", result.Error);
        Assert.Null(result.Receipt);
    }

    [Fact]
    public async Task ApplyRequiresConfirmationAndReturnsSuccessorIdentity()
    {
        var (service, runtimeRoot, project) = CreateService();
        WriteStatus(runtimeRoot, project.ExperienceId);
        var refused = await service.ApplyResetAsync(project.ProjectId, new StudioRunResetRequest("run-exact", "rstp-proof", false), CancellationToken.None);
        Assert.False(refused.Ok);
        Assert.Equal("reset_confirmation_required", refused.Error);

        var watcher = AnswerOnce(runtimeRoot, request => new RuntimeRunControlReceipt
        {
            RequestId = request.RequestId,
            Operation = request.Operation,
            State = "completed",
            Machine = "OMEN",
            WorldUid = "123",
            CreatorSessionId = "creator-session-test",
            CompletedUtc = DateTimeOffset.UtcNow,
            Result = new RuntimeResetResult { ResetId = "reset-one", PreviewToken = "rstp-proof", State = "completed", PriorRunId = "run-exact", NewRunId = "run-successor" },
        });
        var accepted = await service.ApplyResetAsync(project.ProjectId, new StudioRunResetRequest("run-exact", "rstp-proof", true), CancellationToken.None);
        var request = await watcher;
        Assert.True(accepted.Ok, accepted.Error);
        Assert.Equal("run-successor", accepted.Receipt!.Result!.NewRunId);
        Assert.Equal("apply_reset", request.Operation);
        Assert.Equal("rstp-proof", request.PreviewToken);
        Assert.True(request.ConfirmReset);
    }

    [Fact]
    public async Task BindingControlsPinTheCandidateExperienceAndRecoveryChange()
    {
        var (service, runtimeRoot, project) = CreateService();
        WriteStatus(runtimeRoot, project.ExperienceId);
        var candidateWatcher = AnswerOnce(runtimeRoot, request => new RuntimeRunControlReceipt
        {
            RequestId = request.RequestId, Operation = request.Operation, State = "completed",
            Machine = "OMEN", WorldUid = "123", CompletedUtc = DateTimeOffset.UtcNow,
            CreatorSessionId = "creator-session-test",
            BindingCandidates = new[] { new RuntimeBindingCandidate { BindingZdo = "10:20", TargetKind = "sign", Label = "Runestone", DistanceMetres = 3 } },
        });
        var candidates = await service.BindingCandidatesAsync(project.ProjectId, CancellationToken.None);
        var candidateRequest = await candidateWatcher;
        Assert.True(candidates.Ok, candidates.Error);
        Assert.Equal("10:20", Assert.Single(candidates.Receipt!.BindingCandidates!).BindingZdo);
        Assert.Equal("list_binding_candidates", candidateRequest.Operation);
        Assert.Null(candidateRequest.RunId);
        Assert.Null(candidateRequest.ExperienceId);

        var change = new RuntimeBindingChange
        {
            ChangeId = "binding-20260825T120000000Z-deadbeef", BindingZdo = "10:20", WorldId = "123", State = "applied",
            CreatedUtc = DateTimeOffset.UtcNow, Previous = new(), Applied = new RuntimeBindingReference
            {
                PackId = "guild", ExperienceId = project.ExperienceId, BindingId = "default", Version = "1.0.0",
                ContentHash = new string('a', 64),
            },
        };
        var bindWatcher = AnswerOnce(runtimeRoot, request => new RuntimeRunControlReceipt
        {
            RequestId = request.RequestId, Operation = request.Operation, State = "completed",
            Machine = "OMEN", WorldUid = "123", CompletedUtc = DateTimeOffset.UtcNow, BindingChange = change,
            CreatorSessionId = "creator-session-test",
        });
        var bound = await service.BindExperienceAsync(project.ProjectId,
            new StudioBindExperienceRequest(project.ExperienceId, "10:20"), CancellationToken.None);
        var bindRequest = await bindWatcher;
        Assert.True(bound.Ok, bound.Error);
        Assert.Equal(change.ChangeId, bound.Receipt!.BindingChange!.ChangeId);
        Assert.Equal("bind_selected_experience", bindRequest.Operation);
        Assert.Equal(project.ExperienceId, bindRequest.ExperienceId);
        Assert.Equal("10:20", bindRequest.BindingZdo);
        Assert.Null(bindRequest.RunId);

        change.State = "restored";
        var restoreWatcher = AnswerOnce(runtimeRoot, request => new RuntimeRunControlReceipt
        {
            RequestId = request.RequestId, Operation = request.Operation, State = "completed",
            Machine = "OMEN", WorldUid = "123", CompletedUtc = DateTimeOffset.UtcNow, BindingChange = change,
            CreatorSessionId = "creator-session-test",
        });
        var restored = await service.RestoreBindingAsync(project.ProjectId,
            new StudioRestoreBindingRequest("10:20", change.ChangeId), CancellationToken.None);
        var restoreRequest = await restoreWatcher;
        Assert.True(restored.Ok, restored.Error);
        Assert.Equal("restore_binding", restoreRequest.Operation);
        Assert.Equal(change.ChangeId, restoreRequest.BindingChangeId);
        Assert.Null(restoreRequest.ExperienceId);
    }

    [Fact]
    public async Task ARunOutsideTheSelectedProjectCannotReachTheMailbox()
    {
        var (service, runtimeRoot, project) = CreateService();
        WriteStatus(runtimeRoot, project.ExperienceId);
        var result = await service.PreviewResetAsync(project.ProjectId, new StudioRunResetRequest("run-other"), CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal("run_scope_not_loaded", result.Error);
        Assert.False(File.Exists(Path.Combine(runtimeRoot, "requests", "run-control.json")));
    }

    [Fact]
    public void QueuedReceiptPollingRequiresTheExactRequestAndRunIdentity()
    {
        var (service, runtimeRoot, project) = CreateService();
        const string requestId = "studio-preview-reset-proof";
        var directory = Path.Combine(runtimeRoot, "receipts", "run-control");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, requestId + ".json"), JsonSerializer.Serialize(new RuntimeRunControlReceipt
        {
            RequestId = requestId,
            Operation = "preview_reset",
            State = "previewed",
            Machine = "OMEN",
            WorldUid = "123",
            CompletedUtc = DateTimeOffset.UtcNow,
            Preview = new RuntimeResetPreview
            {
                PreviewToken = "rstp-proof",
                RunId = "run-exact",
                ScopeId = "scope-exact",
                CreatedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                SnapshotHash = "snapshot-hash",
                Snapshot = new RuntimeResetSnapshot(),
            },
        }, HostJson()));

        var exact = service.RunControlReceipt(project.ProjectId, requestId, "run-exact");
        Assert.True(exact.Ok, exact.Error);
        Assert.False(exact.Queued);
        Assert.Equal("rstp-proof", exact.Receipt!.Preview!.PreviewToken);

        var wrongRun = service.RunControlReceipt(project.ProjectId, requestId, "run-other");
        Assert.False(wrongRun.Ok);
        Assert.Equal("run_control_scope_mismatch", wrongRun.Error);

        var traversal = service.RunControlReceipt(project.ProjectId, "../runs", "run-exact");
        Assert.False(traversal.Ok);
        Assert.Equal("run_control_identity_invalid", traversal.Error);
    }

    [Fact]
    public void QueuedReceiptPollingFindsTheExactArchivedRunPartition()
    {
        var (service, runtimeRoot, project) = CreateService();
        const string requestId = "studio-preview-reset-archived";
        var path = RuntimeRunControlReceipts.ArchivedReceiptPath(runtimeRoot, "run-exact", requestId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var exactBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new RuntimeRunControlReceipt
        {
            RequestId = requestId,
            Operation = "preview_reset",
            State = "previewed",
            Machine = "OMEN",
            WorldUid = "123",
            CompletedUtc = DateTimeOffset.UtcNow,
            Preview = new RuntimeResetPreview
            {
                PreviewToken = "rstp-archived",
                RunId = "run-exact",
                ScopeId = "scope-exact",
                CreatedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                SnapshotHash = "snapshot-hash",
                Snapshot = new RuntimeResetSnapshot(),
            },
        }, HostJson()));
        File.WriteAllBytes(path, exactBytes);

        var result = service.RunControlReceipt(project.ProjectId, requestId, "run-exact");

        Assert.True(result.Ok, result.Error);
        Assert.False(result.Queued);
        Assert.Equal("rstp-archived", result.Receipt!.Preview!.PreviewToken);
        Assert.Equal(exactBytes, File.ReadAllBytes(path));
    }

    (QuestStudioService Service, string RuntimeRoot, StudioProjectDocument Project) CreateService()
    {
        var valheim = Path.Combine(_root, "valheim");
        Directory.CreateDirectory(valheim);
        var host = new FakeHost(Path.Combine(_root, "state"), valheim);
        var service = new QuestStudioService(host, new QuestPackPublisher(host));
        var project = service.CreateProject("blank");
        return (service, Path.Combine(valheim, "BepInEx", "config", "comfy-quest-runtime"), project);
    }

    static void WriteStatus(string runtimeRoot, string experienceId, bool includeOther = false,
        DateTimeOffset? observedUtc = null, string worldUid = "123", bool includeWorldEntry = true)
    {
        var runs = new List<RuntimeRunStatusEntry>
        {
            new() { RunId = "run-exact", ScopeId = "scope-exact", ExperienceId = experienceId, BindingZdo = "10:20", ParticipantIds = new[] { "hero" }, ContentHash = "content", StageId = "start" },
        };
        if (includeOther) runs.Add(new() { RunId = "run-other", ScopeId = "scope-other", ExperienceId = "another-experience", BindingZdo = "30:40", ParticipantIds = new[] { "hero" }, ContentHash = "other", StageId = "start" });
        new RuntimeRunStatusStore(runtimeRoot).Write(new RuntimeRunStatusDocument
        {
            ObservedUtc = observedUtc ?? DateTimeOffset.UtcNow,
            Machine = "OMEN",
            WorldUid = worldUid,
            Runs = runs,
        });
        if (includeWorldEntry)
        {
            var path = Path.Combine(runtimeRoot, "status", "world-entry.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new RuntimeWorldEntryReceipt
            {
                RequestId = "world-entry-test", CreatorSessionId = "creator-session-test",
                State = "entered", Detail = "world_entry_complete", Machine = "OMEN",
                ExpectedWorldUid = worldUid, WorldUid = worldUid,
                WorldName = "TestWorld", WorldDisplayName = "TestWorld",
                CharacterProfile = "tester", CharacterName = "Tester",
                CompletedUtc = DateTimeOffset.UtcNow,
            }, HostJson()));
        }
    }

    static async Task<RuntimeRunControlRequest> AnswerOnce(string runtimeRoot, Func<RuntimeRunControlRequest, RuntimeRunControlReceipt> answer)
    {
        var mailbox = Path.Combine(runtimeRoot, "requests", "run-control.json");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(mailbox))
            {
                var request = JsonSerializer.Deserialize<RuntimeRunControlRequest>(await File.ReadAllTextAsync(mailbox), HostJson())!;
                File.Delete(mailbox);
                var receipt = answer(request);
                var directory = Path.Combine(runtimeRoot, "receipts", "run-control");
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, request.RequestId + ".json"), JsonSerializer.Serialize(receipt, HostJson()));
                return request;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("run-control request was not written");
    }

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

    sealed class FakeHost(string stateDirectory, string valheim) : IQuestStudioHost
    {
        public string StateDirectory { get; } = stateDirectory;
        public string? FindValheim() => valheim;
        public bool Authorize(HttpRequest request) => true;
        public JsonSerializerOptions Json { get; } = HostJson();
    }
}

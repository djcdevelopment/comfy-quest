using Comfy.Quest.Studio;
using Xunit;

namespace Quest.Studio.Tests;

public sealed class CreatorOperationJournalTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "quest-creator-journal-" + Guid.NewGuid().ToString("N"));
    static StudioCreatorOperationRequest Request(string command = "command-one") => new(
        "comfy-quest-creator-operation-request/v1", command, "activate", "project-one", 1,
        "am4", "-7600395338659582326", "creator-test", TargetId: "target-one");

    [Fact]
    public async Task RetryReplaysOneDispatchEvenAfterRestart()
    {
        var calls = 0;
        var journal = new CreatorOperationJournal(root);
        Task<StudioCreatorOperationOutcome> Execute(StudioCreatorOperationRequest _) {
            Interlocked.Increment(ref calls); return Task.FromResult(new StudioCreatorOperationOutcome(true, null, "activated")); }
        Assert.Equal(202, journal.Submit(Request(), _ => null, Execute).StatusCode);
        await journal.DrainAsync();
        Assert.True(journal.Submit(Request(), _ => "must-not-revalidate-replay", Execute).Replayed);
        var reopened = new CreatorOperationJournal(root);
        Assert.True(reopened.Submit(Request(), _ => null, Execute).Replayed);
        Assert.Equal(1, calls);
        Assert.Equal("completed", reopened.Get("command-one")!.State);
        Assert.Equal("activated", reopened.Get("command-one")!.Outcome!.RuntimeState);
    }

    [Fact]
    public async Task ChangedPayloadCannotReuseCommandId()
    {
        var journal = new CreatorOperationJournal(root);
        journal.Submit(Request(), _ => null, _ => Task.FromResult(new StudioCreatorOperationOutcome(true, null)));
        await journal.DrainAsync();
        var conflict = journal.Submit(Request() with { ExpectedRevision = 2 }, _ => null,
            _ => throw new Exception("must not dispatch"));
        Assert.Equal("creator_command_id_conflict", conflict.Error);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task BusyJournalDoesNotQueueAnotherMutation()
    {
        var release = new TaskCompletionSource<StudioCreatorOperationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var journal = new CreatorOperationJournal(root);
        journal.Submit(Request(), _ => null, _ => release.Task);
        try {
            Assert.Equal("creator_operation_busy", journal.Submit(Request("command-two"), _ => null,
                _ => throw new Exception("must not dispatch")).Error);
        } finally { release.SetResult(new(true, null)); await journal.DrainAsync(); }
    }

    [Fact]
    public async Task UnknownOutcomeIsRetainedAndNeverReexecuted()
    {
        var journal = new CreatorOperationJournal(root);
        journal.Submit(Request(), _ => null, _ => throw new IOException("response lost"));
        await journal.DrainAsync();
        var reopened = new CreatorOperationJournal(root);
        Assert.Equal("recovery_required", reopened.Get("command-one")!.State);
        Assert.True(reopened.Submit(Request(), _ => null, _ => throw new Exception("must not dispatch")).Replayed);
        Assert.Equal("creator_operation_busy", reopened.Submit(Request("command-two"), _ => null,
            _ => throw new Exception("must not dispatch")).Error);
    }

    [Fact]
    public void StaleAuthorityAndUnconfirmedResetAreRejectedBeforeDispatch()
    {
        var journal = new CreatorOperationJournal(root);
        Assert.Equal("revision_conflict", journal.Submit(Request(), _ => "revision_conflict",
            _ => throw new Exception("must not dispatch")).Error);
        Assert.Null(journal.Get("command-one"));
        Assert.Equal("reset_confirmation_required", journal.Submit(Request() with {
            Operation = "reset", RunId = "run-one" }, _ => null,
            _ => throw new Exception("must not dispatch")).Error);
    }

    [Fact]
    public async Task PendingReceiptCanCompleteWithoutAnotherDispatch()
    {
        var journal = new CreatorOperationJournal(root);
        journal.Submit(Request(), _ => null, _ => Task.FromResult(new StudioCreatorOperationOutcome(
            true, null, RequestId: "runtime-one", Pending: true)));
        await journal.DrainAsync();
        Assert.Equal("awaiting_runtime", journal.Get("command-one")!.State);
        journal.CompletePending("command-one", new(true, null, "completed", RunId: "run-next"));
        Assert.Equal("run-next", new CreatorOperationJournal(root).Get("command-one")!.Outcome!.RunId);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ComfyQuestContracts;
using Newtonsoft.Json;

sealed class RuntimeExperienceEngine {
  const string Prefix = "comfyQuestRuntime.";
  const int MaxRecentEvidence = 8;
  const int MaxIdenticalRejections = 3;
  const int RecheckTicks = 5;
  const int MaxArmedRechecks = 32;
  const int MaxSpawnRecoveries = 32;
  const int MaxPendingReplays = 32;
  const int MaxPendingContinuations = 32;
  readonly string root;
  readonly RuntimeReceiptStore receipts;
  readonly ActionExecutionLedger ledger;
  readonly SpawnExecutionStore spawned;
  readonly WorkflowStateStore workflows;
  readonly DurableTimerStore timers;
  readonly RuntimeRunCoordinator runs;
  readonly RuntimeContinuationStore continuations;
  readonly RuntimeBindingCoordinator continuationBindings;
  readonly Func<bool> privateConfirmed;
  readonly Func<DedicatedPersonalProgressionProfile> personalProfile;
  readonly Dictionary<string, DateTimeOffset> recentEventKeys =
      new(StringComparer.Ordinal);
  /// <summary>Bindings owed a bounded re-read, and how many ticks each is still owed (ADR 0006).</summary>
  readonly Dictionary<string, int> rechecks = new(StringComparer.Ordinal);
  readonly Dictionary<string, SpawnRecovery> spawnRecoveries = new(StringComparer.Ordinal);
  readonly Dictionary<string, PendingReplay> pendingReplays = new(StringComparer.Ordinal);
  readonly object evidenceGate = new();
  readonly List<CreatorEvidenceLine> recentEvidence = new();
  readonly Dictionary<string, CreatorEvidenceLine> activeAlerts =
      new(StringComparer.Ordinal);
  readonly List<string> activeAlertOrder = new();
  string deadlineLine;
  bool deadlineUrgent;
  string deadlineError;
  string timerPollError;
  int lastOrphanCount;
  string rejectionKey;
  int rejectionRepeats;
  string countedKey;
  int countedCurrent;
  string unboundReported;
  DateTimeOffset nextTimerPoll;
  Active cachedActive;
  DateTime cachedActiveWriteUtc;
  DateTime cachedPackageWriteUtc;
  long cachedPackageLength;
  WearNTear[] cachedBindings = Array.Empty<WearNTear>();
  string cachedBindingContentHash;
  string cachedBindingResolutionKey;
  string cachedBindingSelectionKey;
  BindingSelection cachedBindingSelection;
  double nextBindingRefresh;
  double nextBindingSelectionRefresh;

  enum BindingSelectionState { Unavailable, Legacy, Selected }
  sealed class BindingSelection {
    public BindingSelectionState State;
    public string BindingInstanceId;
    public string Error;
    public string ResolutionKey => State + "\n" + (BindingInstanceId ?? "") + "\n" + (Error ?? "");
    public static BindingSelection Unavailable(string error) => new() {
      State = BindingSelectionState.Unavailable, Error = error,
    };
    public static BindingSelection Legacy() => new() { State = BindingSelectionState.Legacy };
    public static BindingSelection Selected(string bindingInstanceId) => new() {
      State = BindingSelectionState.Selected, BindingInstanceId = bindingInstanceId,
    };
  }

  sealed class SpawnRecovery {
    public string ActionKey;
    public WorkflowIdentity Identity;
    public ExperienceAction Action;
    public ZDOMan Manager;
    public long NetworkSessionId;
    public IReadOnlyList<SpawnedObject> Records;
  }

  sealed class PendingReplay {
    public string ActionKey;
    public WorkflowIdentity Identity;
    public string StageId;
    public string TransitionId;
    public ExperienceAction Action;
    public DateTimeOffset NextUtc;
  }

  public RuntimeExperienceEngine(
      string runtimeRoot,
      RuntimeReceiptStore receiptStore,
      Func<bool> isPrivateConfirmed,
      Func<DedicatedPersonalProgressionProfile> dedicatedPersonalProfile) {
    root = runtimeRoot;
    receipts = receiptStore;
    ledger = new ActionExecutionLedger(root);
    spawned = new SpawnExecutionStore(root);
    workflows = new WorkflowStateStore(root);
    timers = new DurableTimerStore(root);
    runs = new RuntimeRunCoordinator(root);
    continuations = new RuntimeContinuationStore(root);
    continuationBindings = new RuntimeBindingCoordinator(
        root, new RuntimeBindingWorldAdapter(), runs.Registry);
    privateConfirmed = isPrivateConfirmed;
    personalProfile = dedicatedPersonalProfile ?? (() => new DedicatedPersonalProgressionProfile());
  }

  public void OnEasyEvent(RuntimeEvent evt) {
    if (evt != null) OnEvent(evt);
  }

  public IReadOnlyList<string> RecentEvidence() {
    lock (evidenceGate) return recentEvidence.Select(value => value.Text).ToArray();
  }

  /// <summary>The same bounded evidence with each row's kind — a fact tagged where the
  /// line was composed, so the creator surface never classifies by parsing rendered copy.</summary>
  public IReadOnlyList<CreatorEvidenceLine> RecentEvidenceLines() {
    lock (evidenceGate) return recentEvidence.ToArray();
  }

  /// <summary>The newest still-actionable warning for the single configured alert anchor.
  /// Historical warning rows have no key and therefore never become present-tense alerts.</summary>
  public CreatorEvidenceLine CurrentAlert() {
    lock (evidenceGate) {
      for (var index = activeAlertOrder.Count - 1; index >= 0; index--)
        if (activeAlerts.TryGetValue(activeAlertOrder[index], out var alert)) return alert;
      return null;
    }
  }

  /// <summary>Expire an actionable warning when the condition it describes clears. The
  /// stable key is supplied at the emission site; warning copy is never parsed.</summary>
  public void ResolveAlert(string key) {
    if (string.IsNullOrWhiteSpace(key)) return;
    lock (evidenceGate) {
      activeAlerts.Remove(key);
      activeAlertOrder.RemoveAll(value => string.Equals(value, key, StringComparison.Ordinal));
      recentEvidence.RemoveAll(value => string.Equals(value.Key, key, StringComparison.Ordinal));
    }
  }

  public void Tick() {
    var now = DateTimeOffset.UtcNow;
    if (now < nextTimerPoll) return;
    nextTimerPoll = now.AddSeconds(1);
    try {
      TryLoad(out var loaded, out _);
      // Resume the first stage after a process loss between binding and its initial effects.
      // Existing run identity and the action ledger make this a recovery, never a fresh replay.
      if (loaded != null && CharmPolicy.CanMutate(World()).Allowed) {
        foreach (var wear in Bindings(loaded)) {
          var zdo = wear?.GetComponent<ZNetView>()?.GetZDO();
          if (zdo != null && TryActiveIdentity(zdo, loaded, out var initialIdentity, out _)
              && workflows.Get(initialIdentity) == null)
            EnsureStarted(loaded, zdo, initialIdentity, out _);
        }
      }
      foreach (var timer in timers.Due(now, identity => IsCurrentRun(identity, loaded))) {
        if (!TryCurrentBinding(timer.Identity, loaded, out var binding)) continue;
        var elapsed = new RuntimeEvent {
          Name = ExperienceSchema.TimerElapsedEvent,
          SourceId = binding.m_uid.ToString(),
          At = now,
          Fields = new Dictionary<string, string> { ["timer_id"] = timer.TimerId },
        };
        RuntimeObservation.StampLocalPlayer(elapsed);
        OnEvent(elapsed);
        timers.Acknowledge(timer.Key);
      }
      timerPollError = null;
    } catch (Exception e) {
      if (!string.Equals(timerPollError, e.Message, StringComparison.Ordinal)) {
        timerPollError = e.Message;
        try { Write("transition", "runtime_timer_failed", null, null, e.Message, null); }
        catch { }
      }
    }
    RunSpawnRecoveries(now);
    RunPendingReplays(now);
    RunPendingContinuations(now);
    RecoverCompletedContinuation(now);
    RunRechecks(now);
    RefreshDeadline(now);
  }

  /// <summary>The bounded catch-up (ADR 0006). Evaluation is event-driven, but a route gated on a
  /// tally the world supplies fresh can turn true just after the event that should have satisfied
  /// it: session 2's eighth kill was evaluated inside Character.OnDeath's own call stack, a frame
  /// before ZDOMan let the corpse go, so the wave read "7 cleared" and — with nothing to re-read it —
  /// the win only landed nine minutes later, when the deadline event happened to evaluate the same
  /// route again. Each armed binding re-runs the same observation pass against the same routes for a
  /// few ticks and then goes quiet; no event is fabricated and no fact gains a second source.</summary>
  void RunRechecks(DateTimeOffset now) {
    if (rechecks.Count == 0) return;
    try {
      if (!TryLoad(out var active, out _)) { rechecks.Clear(); return; }
      foreach (var wear in Bindings(active)) {
        if (rechecks.Count == 0) break;
        var view = wear == null ? null : wear.GetComponent<ZNetView>();
        var zdo = view == null ? null : view.GetZDO();
        if (zdo == null || !view.IsOwner()) continue;
        var reference = Read(zdo);
        if (reference == null || reference.ContentHash != active.ContentHash) continue;
        if (!TryActiveIdentity(zdo, active, out var identity, out _)) continue;
        if (!rechecks.TryGetValue(identity.Key, out var remaining)) continue;
        var observed = RuntimeObservation.Facts(
            zdo, spawned, identity.Key, active.ContentHash);
        var decision = workflows.Recheck(
            identity, active.Document, now, observed.Spatial, observed.Encounter);
        if (decision != null) {
          rechecks.Remove(identity.Key);
          Apply(active, zdo, decision, LastEvent(identity), NewRecheckId());
          continue;
        }
        if (remaining > 1) { rechecks[identity.Key] = remaining - 1; continue; }
        rechecks.Remove(identity.Key);
        ReportRecheckExpired(active, zdo, identity, observed, now);
      }
    } catch (Exception e) {
      Write("action", "runtime_recheck_failed", null, null, e.Message, null);
    }
  }

  /// <summary>One receipt when an armed window closes with the route still unmet, carrying what the
  /// last read actually saw. Session 2's ledger row existed because nothing ever said "still 7 of 8".</summary>
  void ReportRecheckExpired(
      Active active, ZDO zdo, WorkflowIdentity identity, ObservedFacts observed, DateTimeOffset now) {
    var state = workflows.Get(identity);
    var stage = active.Document.Stages.FirstOrDefault(value => value.Id == state?.StageId);
    var route = stage?.Transitions?.OrderByDescending(value => value.Priority)
        .ThenBy(value => value.Id, StringComparer.Ordinal).FirstOrDefault();
    var context = Context(active.Document, state, observed, now);
    var trace = TriggerEvaluator.Explain(route?.When, state?.History, context);
    WriteReceipt(EventReceipt(
        "recheck_expired", active, zdo.m_uid.ToString(), state?.History?.LastOrDefault(),
        state?.StageId, state?.StageId,
        Counted(trace, TriggerEvaluator.Measure(route?.When, state?.History, context)),
        NewRecheckId(), trace, null, identity));
  }

  /// <summary>Arm a binding for the bounded re-read. Re-arming on every ignored event is deliberate:
  /// the window a settle needs starts at the last event, not the first.</summary>
  void Arm(string ownerKey) {
    if (string.IsNullOrWhiteSpace(ownerKey)) return;
    if (rechecks.Count >= MaxArmedRechecks && !rechecks.ContainsKey(ownerKey)) return;
    rechecks[ownerKey] = RecheckTicks;
  }

  /// <summary>A stage worth re-reading after its own event: one whose routes are gated on a tally the
  /// world supplies fresh at evaluation time. Deaths and elapsed time do not race their own event.</summary>
  static bool Rechecks(ExperienceStage stage) =>
      (stage?.Transitions ?? new()).Any(value => AdaptiveEvaluator.ReadsWorldTally(value?.When));

  RuntimeEvent LastEvent(WorkflowIdentity identity) {
    try { return workflows.Get(identity)?.History?.LastOrDefault(); }
    catch { return null; }
  }

  TriggerEvaluationContext Context(
      ExperienceDocument document, WorkflowProgress state, ObservedFacts observed, DateTimeOffset at) => new() {
    At = at,
    StageEnteredUtc = state?.StageEnteredUtc,
    LastProgressUtc = state?.LastProgressUtc,
    BindingPosition = observed?.Spatial?.BindingPosition,
    SpawnedPositions = observed?.Spatial?.SpawnedPositions,
    SpatialAreas = SpatialEvaluator.AreaMap(document),
    DeathsInStage = state?.DeathsInStage,
    SpawnsByAction = observed?.Encounter?.SpawnsByAction,
  };

  /// <summary>The authored deadline a player is currently racing, or null. Recomputed once a second
  /// beside the timer poll and cached, because the surface that draws it runs every frame.</summary>
  public string Deadline() {
    lock (evidenceGate) return deadlineLine;
  }

  /// <summary>True while the cached deadline has five seconds or fewer left. A fact beside the
  /// line rather than parsed back out of it, so a copy change can never kill the red state.</summary>
  public bool DeadlineUrgent() {
    lock (evidenceGate) return deadlineUrgent;
  }

  /// <summary>How many loaded bindings the most recent activation left on an earlier content
  /// hash. The count comes from the same single bounded scan that writes the orphaned_bindings
  /// receipt, so the keypress that caused the activation can carry its own consequence.</summary>
  public int OrphanedBindingsAfterActivation() {
    try { if (!TryLoad(out _, out _)) return 0; } catch { return 0; }
    lock (evidenceGate) return lastOrphanCount;
  }


  public void OnEvent(RuntimeEvent evt) => OnEvent(evt, false);

  internal void OnEvent(RuntimeEvent evt, bool locallyWitnessed) {
    evt = RuntimeEventPolicy.Normalize(evt);
    if (evt == null) return;
    Active active = null;
    string correlationId = null;
    try {
      if (!TryLoad(out active, out var diagnostic)) {
        // With no active set, every world event lands here before the subscription filter
        // and duplicate window — session 1 of the Phase 3 exit lap wrote 47 identical
        // active_set_missing receipts before the first check, 39 of them in one second.
        // The diagnostic is worth MaxIdenticalRejections receipts; one more names the
        // suppression, then the series stays quiet until the error changes or clears.
        if (!string.Equals(diagnostic, rejectionKey, StringComparison.Ordinal)) {
          rejectionKey = diagnostic;
          rejectionRepeats = 0;
        }
        rejectionRepeats++;
        if (rejectionRepeats <= MaxIdenticalRejections)
          Write("transition", diagnostic, null, null, null, null);
        else if (rejectionRepeats == MaxIdenticalRejections + 1)
          Write("transition", diagnostic, null, null, "suppressed_after_"
              + MaxIdenticalRejections.ToString(
                  System.Globalization.CultureInfo.InvariantCulture), null);
        return;
      }
      rejectionKey = null;
      rejectionRepeats = 0;
      // The high-frequency lane stops here: no scene walk, receipt, or workflow write.
      if (!active.Subscriptions.Contains(evt.Name) || IsDuplicate(evt)) return;
      correlationId = NewCorrelationId();
      var authority = CharmPolicy.CanMutate(World());
      var personal = false;
      if (!authority.Allowed) {
        var personalDecision = PersonalEventDecision(active, evt, locallyWitnessed);
        if (!personalDecision.Allowed) {
          Write("transition", personalDecision.Diagnostic, active, null, null, correlationId);
          return;
        }
        personal = true;
      }

      var foundBinding = false;
      foreach (var wear in Bindings(active)) {
        if (wear == null) continue;
        var view = wear.GetComponent<ZNetView>();
        var zdo = view == null ? null : view.GetZDO();
        if (zdo == null || (!personal && !view.IsOwner())
            || (!string.IsNullOrWhiteSpace(evt.SourceId)
                && evt.SourceId != zdo.m_uid.ToString())) continue;
        var reference = Read(zdo);
        if (!ReferenceMatches(reference, active, personal)) continue;

        foundBinding = true;
        var identity = ResolveIdentity(zdo, active);
        if (identity == null) continue;
        var observed = RuntimeObservation.Facts(
            zdo, spawned, identity.Key, active.ContentHash);
        var before = workflows.Get(identity);
        var evaluationContext = new TriggerEvaluationContext {
          At = evt.At,
          StageEnteredUtc = before?.StageEnteredUtc ?? (before == null ? evt.At : (DateTimeOffset?)null),
          LastProgressUtc = before?.LastProgressUtc ?? (before == null ? evt.At : (DateTimeOffset?)null),
          BindingPosition = observed.Spatial.BindingPosition,
          SpawnedPositions = observed.Spatial.SpawnedPositions,
          SpatialAreas = SpatialEvaluator.AreaMap(active.Document),
          DeathsInStage = before?.DeathsInStage,
          SpawnsByAction = observed.Encounter?.SpawnsByAction,
        };
        var decision = workflows.Begin(
            identity, active.Document, evt, observed.Spatial, observed.Encounter);
        if (decision == null) {
          var state = workflows.Get(identity);
          var stage = active.Document.Stages.FirstOrDefault(value => value.Id == state?.StageId);
          var route = stage?.Transitions?.OrderByDescending(value => value.Priority)
              .ThenBy(value => value.Id, StringComparer.Ordinal).FirstOrDefault();
          // An ignored event now says why it was ignored. Session 2's eight kills every one read
          // "0/1" — the top-level ALL's bare pass/fail — while the wave stood at seven of eight.
          var trace = TriggerEvaluator.Explain(route?.When, state?.History, evaluationContext);
          var progress = Counted(
              trace, TriggerEvaluator.Measure(route?.When, state?.History, evaluationContext));
          var line = ProgressLine(identity.Key, state?.StageId, route, progress);
          var finishHelp = string.IsNullOrWhiteSpace(state?.Outcome)
              ? SignatureHuntFinishHelp(route, evt) : null;
          var ignored = EventReceipt(
              "ignored", active, zdo.m_uid.ToString(), evt, state?.StageId,
              state?.StageId, progress, correlationId, trace,
              UnmetRoutes(stage, null, state?.History, evaluationContext), identity);
          if (finishHelp != null) {
            ignored.Diagnostics = new[] { new ContractDiagnostic("hunt.finish_requirement", "$", finishHelp) };
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, finishHelp);
          }
          WriteReceipt(ignored, finishHelp ?? line,
              finishHelp == null ? CreatorEvidenceKind.Story : CreatorEvidenceKind.Warning);
          if (Rechecks(stage)) Arm(identity.Key);
          continue;
        }
        rechecks.Remove(identity.Key);
        Apply(active, zdo, decision, evt, correlationId, personal);
      }

      if (!foundBinding) {
        WriteReceipt(EventReceipt("unbound", active, null, evt, null, null,
            new TriggerProgress { Current = 0, Required = 1 }, correlationId),
            UnboundLine(active), CreatorEvidenceKind.Warning, "charm_unbound");
      }
    } catch (Exception e) {
      Write("action", "runtime_event_failed", active, null, e.Message, correlationId);
    }
  }

  /// <summary>Everything a matched route does: the matched receipt with its evidence, the transition's
  /// actions, the next stage's entry actions, and the transition receipt. Shared by the event path and
  /// the bounded recheck, so an arriving event and a settled tally advance a stage the same way and
  /// leave the same trail. The cause is the event that earned the match — for a recheck, the last one
  /// in history, because a recheck never invents an event of its own.</summary>
  void Apply(
      Active active, ZDO zdo, WorkflowDecision decision, RuntimeEvent cause, string correlationId,
      bool personal = false) {
    var currentStage = active.Document.Stages.FirstOrDefault(value => value.Id == decision.StageId);
    if (currentStage == null) return;
    var currentState = workflows.Get(decision.Identity);
    var matchedProgress = TriggerEvaluator.Measure(decision.Transition.When, currentState?.History, decision.EvaluationContext);
    var evidence = decision.IsPendingReplay
        ? null : TriggerEvaluator.Explain(decision.Transition.When, currentState?.History, decision.EvaluationContext);
    var rejectedEvidence = decision.IsPendingReplay
        ? null : ExplainRejected(currentStage, decision.Transition, currentState?.History, decision.EvaluationContext);
    WriteReceipt(EventReceipt(
        "matched", active, zdo.m_uid.ToString(), cause, currentStage.Id,
        decision.Transition.NextStage, matchedProgress, correlationId,
        evidence, rejectedEvidence, decision.Identity),
        MatchedLine(currentStage.Id, decision.Transition, matchedProgress, rejectedEvidence),
        CreatorEvidenceKind.Story);

    var succeeded = true;
    foreach (var action in decision.Transition.Actions ?? new())
      succeeded &= Execute(
          active, zdo, currentStage.Id, decision.Transition.Id, action, correlationId, personal);
    if (succeeded && !string.IsNullOrWhiteSpace(decision.Transition.NextStage)) {
      var next = active.Document.Stages.FirstOrDefault(
          value => value.Id == decision.Transition.NextStage);
      foreach (var action in next?.EntryActions ?? new())
        succeeded &= Execute(active, zdo, next.Id, "entry", action, correlationId, personal);
    }
    if (!succeeded) return;
    if (workflows.Complete(decision)) {
      WriteReceipt(new RuntimeReceipt {
        Operation = "transition",
        Status = string.IsNullOrWhiteSpace(decision.Transition.Outcome)
            ? "advanced" : decision.Transition.Outcome,
        PackId = active.PackId,
        Version = active.Version,
        ContentHash = active.ContentHash,
        ExperienceId = active.Document.Id,
        RunId = decision.Identity.RunId,
        WorldId = decision.Identity.WorldId,
        ActivationId = active.ActivationId,
        CorrelationId = correlationId,
        BindingZdo = zdo.m_uid.ToString(),
        BindingInstanceId = decision.Identity.BindingInstanceId,
        StageId = currentStage.Id,
        TransitionId = decision.Transition.Id,
        EventName = cause?.Name,
        EventTarget = cause?.Target,
        ActorRole = cause == null ? null : CooperativeEventContract.ActorRole(cause),
        CurrentStageId = currentStage.Id,
        NextStageId = decision.Transition.NextStage,
        CurrentCount = matchedProgress.Current,
        RequiredCount = matchedProgress.Required,
        StageEnteredUtc = currentState?.StageEnteredUtc,
        Evidence = evidence,
        RejectedEvidence = rejectedEvidence,
        Diagnostics = Array.Empty<ContractDiagnostic>(),
      }, TransitionLine(currentStage.Id, decision.Transition), CreatorEvidenceKind.Story);
      if (!string.IsNullOrWhiteSpace(decision.Transition.Outcome)) {
        var ended = DateTimeOffset.UtcNow;
        runs.Registry.MarkOutcome(decision.Identity.RunId, decision.Transition.Outcome, ended);
        if (string.Equals(decision.Transition.Outcome, "complete", StringComparison.Ordinal)
            && (active.Document.SuccessorExperienceIds?.Count ?? 0) > 0)
          BeginContinuation(active, zdo, decision.Identity, ended);
      }
    }
  }

  void BeginContinuation(Active active,ZDO zdo,WorkflowIdentity identity,DateTimeOffset now) {
    try {
      var source=runs.Registry.Find(identity?.RunId);
      if(source?.Outcome!="complete")throw new InvalidOperationException("continuation_predecessor_incomplete");
      var successor=ExperienceContinuationSelector.FirstEligible(
          active.Document,active.Documents,runs.Registry.List(),source.Scope);
      if(string.IsNullOrWhiteSpace(successor)) {
        receipts.WriteOnce(ContinuationReceipt("ended",active,source,null,null,
            "continuation-end-"+source.RunId,null,now));
        return;
      }
      var handoff=continuations.Begin(source,successor,active.PackId,active.Version,
          active.ContentHash,active.ActivationId,now);
      receipts.WriteOnce(ContinuationReceipt("pending",active,source,handoff,null,
          handoff.HandoffId+"-pending",null,now));
      ResumeContinuation(handoff,now);
    } catch(Exception error) {
      Write("continuation","continuation_begin_failed",active,"pending",error.Message,null);
    }
  }

  void RunPendingContinuations(DateTimeOffset now) {
    try {
      foreach(var handoff in continuations.Pending().Take(MaxPendingContinuations))
        ResumeContinuation(handoff,now);
    } catch(Exception error) {
      Write("continuation","continuation_recovery_failed",null,"pending",error.Message,null);
    }
  }

  /// <summary>Close the only pre-journal crash window: workflow completion and run outcome are
  /// durable writes made just before the handoff decision. A cold retry reconstructs that decision
  /// from the exact selected binding, but never revisits a source already journaled or ended.</summary>
  void RecoverCompletedContinuation(DateTimeOffset now) {
    try {
      if(!TryLoad(out var active,out _)||(active.Document.SuccessorExperienceIds?.Count??0)==0)return;
      foreach(var wear in Bindings(active)) {
        var zdo=wear?.GetComponent<ZNetView>()?.GetZDO();if(zdo==null)continue;
        if(!TryActiveIdentity(zdo,active,out var identity,out var run))continue;
        var progress=workflows.Get(identity);
        if(progress?.Outcome is not ("complete" or "fail"))continue;
        if(string.IsNullOrWhiteSpace(run.Outcome)){
          runs.Registry.MarkOutcome(run.RunId,progress.Outcome,now);
          run=runs.Registry.Find(run.RunId);
        }
        if(run?.Outcome!="complete")return;
        if(continuations.FindBySourceRun(run.RunId)!=null
            ||receipts.ContainsId("continuation-end-"+run.RunId))return;
        BeginContinuation(active,zdo,identity,now);return;
      }
    } catch(Exception error) {
      Write("continuation","continuation_completion_recovery_failed",null,"pending",error.Message,null);
    }
  }

  void ResumeContinuation(RuntimeContinuationRecord handoff,DateTimeOffset now) {
    Active active=null;
    try {
      if(handoff==null||handoff.State!="pending")return;
      receipts.WriteOnce(ContinuationReceipt("pending",null,runs.Registry.Find(handoff.SourceRunId),
          handoff,runs.Registry.Find(handoff.SuccessorRunId),handoff.HandoffId+"-pending",null,
          handoff.CreatedUtc));
      if(!TryLoad(out active,out var loadError))throw new InvalidOperationException(loadError);
      if(active.PackId!=handoff.PackId||active.Version!=handoff.Version
          ||!string.Equals(active.ContentHash,handoff.ContentHash,StringComparison.OrdinalIgnoreCase)
          ||active.ActivationId!=handoff.ActivationId)
        throw new InvalidOperationException("continuation_active_content_changed");
      if(!active.Documents.TryGetValue(handoff.SourceExperienceId,out _)
          ||!active.Documents.TryGetValue(handoff.SuccessorExperienceId,out var successor))
        throw new InvalidOperationException("continuation_experience_missing");
      var personal=PersonalDecision(active).Allowed;
      if(!TryContinuationBinding(handoff,personal,out var wear,out var zdo,out var targetKind,out var bindingError))
        throw new InvalidOperationException(bindingError);
      var reference=Read(zdo);
      if(reference==null||reference.PackId!=handoff.PackId||reference.BindingId!="default"
          ||reference.Version!=handoff.Version
          ||!string.Equals(reference.ContentHash,handoff.ContentHash,StringComparison.OrdinalIgnoreCase)
          ||personal&&!active.Documents.ContainsKey(reference.ExperienceId)
          ||!personal&&reference.ExperienceId!=handoff.SourceExperienceId
              &&reference.ExperienceId!=handoff.SuccessorExperienceId)
        throw new InvalidOperationException("continuation_binding_changed");
      var targetSet=new ActiveSet{Schema=active.Set.Schema,PackId=handoff.PackId,
        Version=handoff.Version,ContentHash=handoff.ContentHash,
        PackageSha256=active.Set.PackageSha256,Source=active.Set.Source,
        ActivatedUtc=active.Set.ActivatedUtc,ActivationId=handoff.ActivationId,
        ExperienceId=handoff.SuccessorExperienceId,SourceChannel=active.Set.SourceChannel,
        PreviousActivationId=active.Set.PreviousActivationId};
      if(!personal) {
        var change=continuationBindings.Continue(zdo.m_uid.ToString(),handoff.WorldId,
            targetSet,handoff.SourceExperienceId,successor,handoff.BindingInstanceId,targetKind,now);
        if(change!=null)continuations.SetBindingChange(handoff.HandoffId,change.ChangeId);
      }
      new QuestPackStore(root).SelectExperienceForActivation(
          handoff.ActivationId,handoff.SuccessorExperienceId);
      InvalidateActive();
      if(!TryLoad(out active,out loadError))throw new InvalidOperationException(loadError);
      if(active.Document.Id!=handoff.SuccessorExperienceId)
        throw new InvalidOperationException("continuation_selection_mismatch");
      var successorRun=runs.Registry.StartContinuation(handoff.SourceRunId,handoff.HandoffId,
          handoff.SuccessorExperienceId,zdo.m_uid.ToString(),now);
      continuations.SetSuccessorRun(handoff.HandoffId,successorRun.RunId);
      if(!EnsureRunStarted(successorRun.RunId,out var startError))
        throw new InvalidOperationException(startError??"continuation_start_failed");
      var sourceRun=runs.Registry.Find(handoff.SourceRunId);
      handoff=continuations.Find(handoff.HandoffId)??handoff;
      receipts.WriteOnce(ContinuationReceipt("started",active,sourceRun,handoff,successorRun,
          handoff.HandoffId+"-started",null,now));
      continuations.Complete(handoff.HandoffId,now);
    } catch(Exception error) {
      try {
        if(handoff!=null&&continuations.RecordError(handoff.HandoffId,error.Message))
          receipts.Write(ContinuationReceipt("pending",active,runs.Registry.Find(handoff.SourceRunId),
              handoff,runs.Registry.Find(handoff.SuccessorRunId),null,error.Message,now));
      } catch { }
    }
  }

  bool TryContinuationBinding(RuntimeContinuationRecord handoff,bool personal,out WearNTear wear,out ZDO zdo,
      out string targetKind,out string error) {
    wear=null;zdo=null;targetKind=null;error="continuation_binding_not_loaded";
    try {
      if(ZNet.instance==null||ZNet.instance.GetWorldUID().ToString()!=handoff.WorldId) {
        error="continuation_world_changed";return false;
      }
      var matches=WearNTear.GetAllInstances().Where(value=>{
        var candidate=value?.GetComponent<ZNetView>()?.GetZDO();
        if(candidate==null||!candidate.Persistent)return false;
        return !string.IsNullOrWhiteSpace(handoff.BindingInstanceId)
          ?candidate.GetString(Prefix+"bindingInstanceId","")==handoff.BindingInstanceId
          :candidate.m_uid.ToString()==handoff.BindingZdo;
      }).ToArray();
      if(matches.Length!=1){error=matches.Length==0?"continuation_binding_not_loaded":"continuation_binding_ambiguous";return false;}
      wear=matches[0];var view=wear.GetComponent<ZNetView>();zdo=view?.GetZDO();
      if(zdo==null||!personal&&!view.IsOwner()){error="continuation_binding_not_owned";return false;}
      targetKind=wear.GetComponent<Sign>()!=null?"sign":wear.GetComponent<ItemStand>()!=null
        ?"item_stand":"player_built_piece";
      return true;
    } catch(Exception exception) {error="continuation_binding_unreadable:"+exception.GetType().Name;return false;}
  }

  static RuntimeReceipt ContinuationReceipt(string status,Active active,RuntimeRunRecord source,
      RuntimeContinuationRecord handoff,RuntimeRunRecord successor,string id,string error,
      DateTimeOffset at)=>new(){Id=id,AtUtc=at,Operation="continuation",Status=status,Error=error,
        PackId=handoff?.PackId??active?.PackId,Version=handoff?.Version??active?.Version,
        ContentHash=handoff?.ContentHash??active?.ContentHash,
        ActivationId=handoff?.ActivationId??active?.ActivationId,
        ExperienceId=handoff?.SourceExperienceId??source?.Scope?.ExperienceId,
        RunId=handoff?.SourceRunId??source?.RunId,HandoffId=handoff?.HandoffId,
        SuccessorExperienceId=handoff?.SuccessorExperienceId,
        SuccessorRunId=successor?.RunId??handoff?.SuccessorRunId,
        ParticipantIds=(IReadOnlyList<string>)(handoff?.ParticipantIds??source?.Scope?.ParticipantIds),
        WorldId=handoff?.WorldId??source?.Scope?.WorldId,
        BindingZdo=handoff?.BindingZdo??source?.Scope?.BindingZdo,
        BindingInstanceId=handoff?.BindingInstanceId??source?.Scope?.BindingInstanceId,
        EvidenceKind=CreatorEvidenceLine.KindName(error==null?CreatorEvidenceKind.Story:CreatorEvidenceKind.Warning),
        Diagnostics=Array.Empty<ContractDiagnostic>()};

  /// <summary>The counted clause the player is actually working on: the unmet node with the most left
  /// to do. Without this an ignored receipt reports the top-level ALL's pass/fail, which is 0/1 no
  /// matter how close the beat is.</summary>
  static TriggerProgress Counted(TriggerClauseTrace trace, TriggerProgress fallback) {
    var counted = Nodes(trace)
        .Where(value => !value.Satisfied && value.Required > 1)
        .OrderByDescending(value => value.Required)
        .ThenByDescending(value => value.Current).FirstOrDefault();
    return counted == null ? fallback
        : new TriggerProgress { Current = counted.Current, Required = counted.Required };
  }

  static IEnumerable<TriggerClauseTrace> Nodes(TriggerClauseTrace trace) {
    if (trace == null) yield break;
    yield return trace;
    foreach (var child in trace.Children ?? new List<TriggerClauseTrace>())
      foreach (var found in Nodes(child)) yield return found;
  }

  /// <summary>The one line a player needs while a counted beat is still open — the beat, and how far
  /// along it is — written only when the count actually moves. Session 2 killed eight of eight and the
  /// screen said nothing at all: the receipts knew, no surface did.</summary>
  string ProgressLine(
      string ownerKey, string stage, ExperienceTransition route, TriggerProgress progress) {
    if (progress == null || progress.Required <= 1 || progress.Current <= 0) return null;
    var key = string.Join("|", ownerKey, stage, route?.Id);
    lock (evidenceGate) {
      if (string.Equals(countedKey, key, StringComparison.Ordinal)
          && countedCurrent == progress.Current) return null;
      countedKey = key;
      countedCurrent = progress.Current;
    }
    return Describe(route?.When) + " — " + progress.Current + "/" + progress.Required + ".";
  }

  /// <summary>Said once per activation, at player altitude. Session 2 spoke in chat to start a quest,
  /// the event arrived and matched the running content, and nothing answered because no Charm had been
  /// cast. event/unbound is honest machinery; silence is what the player got.</summary>
  string UnboundLine(Active active) {
    lock (evidenceGate) {
      if (string.Equals(unboundReported, active.ContentHash, StringComparison.Ordinal)) return null;
      unboundReported = active.ContentHash;
    }
    return (string.IsNullOrWhiteSpace(active.Document?.Title) ? "This quest" : active.Document.Title)
        + " has no Charm yet — open F9, aim the fixed center crosshair at an allowed sign or player-built object, press ` to CHECK, then ` again to CAST.";
  }

  bool IsDuplicate(RuntimeEvent evt) {
    if (string.IsNullOrWhiteSpace(evt.DedupeKey)) return false;
    var now = evt.At == default ? DateTimeOffset.UtcNow : evt.At;
    if (recentEventKeys.TryGetValue(evt.DedupeKey, out var prior)
        && now >= prior && now - prior < TimeSpan.FromSeconds(1)) return true;
    if (recentEventKeys.Count >= 512) {
      foreach (var key in recentEventKeys.OrderBy(value => value.Value)
          .Take(recentEventKeys.Count - 256).Select(value => value.Key).ToArray())
        recentEventKeys.Remove(key);
    }
    recentEventKeys[evt.DedupeKey] = now;
    return false;
  }

  static string NewCorrelationId() =>
      "evt-" + Guid.NewGuid().ToString("N").Substring(0, 12);

  /// <summary>A recheck's own correlation prefix, so a receipt says plainly whether an arriving
  /// event or a settled tally advanced the stage.</summary>
  static string NewRecheckId() =>
      "rck-" + Guid.NewGuid().ToString("N").Substring(0, 12);

  IReadOnlyList<WearNTear> Bindings(Active active) {
    var now = UnityEngine.Time.realtimeSinceStartup;
    var personal = PersonalDecision(active).Allowed;
    if (personal) {
      const string personalResolution = "dedicated-personal-read-only";
      if (!string.Equals(cachedBindingContentHash, active.ContentHash, StringComparison.Ordinal)
          || !string.Equals(cachedBindingResolutionKey, personalResolution, StringComparison.Ordinal)
          || now >= nextBindingRefresh) {
        var candidates = WearNTear.GetAllInstances()
            .Where(value => IsActiveBinding(value, active, true)).ToArray();
        // The beta profile never guesses which shared venue is authoritative. A missing or
        // duplicated exact reference is unavailable until the world presents exactly one.
        cachedBindings = candidates.Length == 1 ? candidates : Array.Empty<WearNTear>();
        cachedBindingContentHash = active.ContentHash;
        cachedBindingResolutionKey = personalResolution;
        nextBindingRefresh = now + 1.0;
      }
      return cachedBindings;
    }
    var selection = SelectedBinding(active);
    if (!string.Equals(cachedBindingContentHash, active.ContentHash, StringComparison.Ordinal)
        || !string.Equals(cachedBindingResolutionKey, selection.ResolutionKey, StringComparison.Ordinal)
        || now >= nextBindingRefresh) {
      var candidates = selection.State == BindingSelectionState.Unavailable
          ? Array.Empty<WearNTear>()
          : WearNTear.GetAllInstances().Where(value => IsActiveBinding(value, active, false)).ToArray();
      if (selection.State == BindingSelectionState.Selected) {
        var matches = candidates.Where(value => string.Equals(
            BindingInstanceIdentity(value), selection.BindingInstanceId,
            StringComparison.Ordinal)).ToArray();
        cachedBindings = matches.Length == 1 ? matches : Array.Empty<WearNTear>();
      } else if (selection.State == BindingSelectionState.Legacy) {
        // A marker without a journal is modern but ambiguous, not legacy. The old physical-ZDO
        // fallback remains only for one marker-free binding; multiple old Charms fail closed.
        cachedBindings = candidates.Length == 1
            && string.IsNullOrWhiteSpace(BindingInstanceIdentity(candidates[0]))
                ? candidates : Array.Empty<WearNTear>();
      } else cachedBindings = Array.Empty<WearNTear>();
      cachedBindingContentHash = active.ContentHash;
      cachedBindingResolutionKey = selection.ResolutionKey;
      nextBindingRefresh = now + 1.0;
    }
    return cachedBindings;
  }

  bool IsActiveBinding(WearNTear wear, Active active, bool personal) {
    try {
      var zdo = wear?.GetComponent<ZNetView>()?.GetZDO();
      if (zdo == null || !zdo.Persistent) return false;
      var reference = Read(zdo);
      return ReferenceMatches(reference, active, personal);
    } catch { return false; }
  }

  static string BindingInstanceIdentity(WearNTear wear) {
    try {
      return wear?.GetComponent<ZNetView>()?.GetZDO()
          ?.GetString(Prefix + "bindingInstanceId", "");
    }
    catch { return null; }
  }

  /// <summary>The binding journal selects a durable marker, never a physical ZDO id. ZDO ids can
  /// change across a cold load; the marker is written into the persistent object's own ZDO fields.
  /// A journal-free, marker-free world gets the narrowly bounded legacy fallback. Ambiguous or
  /// incomplete modern history fails closed.</summary>
  BindingSelection SelectedBinding(Active active, bool force = false) {
    if (active?.Document == null)
      return BindingSelection.Unavailable("binding_selection_active_missing");
    string world;
    try {
      if (ZNet.instance == null)
        return BindingSelection.Unavailable("binding_selection_world_not_loaded");
      world = ZNet.instance.GetWorldUID().ToString();
      if (world == "0")
        return BindingSelection.Unavailable("binding_selection_world_not_loaded");
    } catch { return BindingSelection.Unavailable("binding_selection_world_unreadable"); }
    var key = string.Join("\n", world, active.PackId, active.Document.Id);
    var now = UnityEngine.Time.realtimeSinceStartup;
    if (!force && string.Equals(cachedBindingSelectionKey, key, StringComparison.Ordinal)
        && cachedBindingSelection != null
        && now < nextBindingSelectionRefresh) return cachedBindingSelection;

    var selected = BindingSelection.Legacy();
    DateTimeOffset? selectedAt = null;
    string selectedChange = null;
    DateTimeOffset? markerlessAt = null;
    string markerlessChange = null;
    DateTimeOffset? pendingCreatedAt = null;
    string pendingChange = null;
    DateTimeOffset? finalCreatedAt = null;
    string finalChange = null;
    var relevant = false;
    try {
      var directory = Path.Combine(root, "state", "binding-changes");
      if (Directory.Exists(directory)) {
        var paths = Directory.GetFiles(directory, "binding-*.json");
        if (paths.Length > RuntimeBindingCoordinator.MaxChanges)
          selected = BindingSelection.Unavailable("binding_selection_history_limit");
        foreach (var path in paths.OrderByDescending(Path.GetFileName, StringComparer.Ordinal)) {
          if (selected.State == BindingSelectionState.Unavailable) break;
          var info = new FileInfo(path);
          if (info.Length <= 0 || info.Length > RuntimeBindingCoordinator.MaxChangeBytes) {
            selected = BindingSelection.Unavailable("binding_selection_history_invalid");
            break;
          }
          RuntimeBindingChange change;
          using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                     FileShare.ReadWrite | FileShare.Delete))
          using (var reader = new StreamReader(stream))
            change = JsonConvert.DeserializeObject<RuntimeBindingChange>(reader.ReadToEnd());
          if (change == null || string.IsNullOrWhiteSpace(change.WorldId)) {
            selected = BindingSelection.Unavailable("binding_selection_history_invalid");
            break;
          }
          if (change.WorldId != world) continue;
          if (change.Applied == null) {
            selected = BindingSelection.Unavailable("binding_selection_history_invalid");
            break;
          }
          if (change.Applied.PackId != active.PackId
              || change.Applied.ExperienceId != active.Document.Id) continue;
          relevant = true;
          if (change.Schema != RuntimeBindingChange.CurrentSchema
              || change.ChangeId != Path.GetFileNameWithoutExtension(path)
              || change.CreatedUtc == default
              || !ValidBindingIdentity(change.BindingZdo)
              || change.Previous == null
              || change.Applied.BindingId != "default"
              || change.State is not ("pending" or "applied" or "restored")
              || !ValidBindingReference(change.Applied)) {
            selected = BindingSelection.Unavailable("binding_selection_history_invalid");
            break;
          }
          if (change.State == "pending") {
            if (!pendingCreatedAt.HasValue || change.CreatedUtc > pendingCreatedAt.Value
                || change.CreatedUtc == pendingCreatedAt.Value
                    && string.CompareOrdinal(change.ChangeId, pendingChange) > 0) {
              pendingCreatedAt = change.CreatedUtc;
              pendingChange = change.ChangeId;
            }
            continue;
          }
          if (!change.CompletedUtc.HasValue
              || change.CompletedUtc.Value < change.CreatedUtc) {
            selected = BindingSelection.Unavailable("binding_selection_history_invalid");
            break;
          }
          if (!finalCreatedAt.HasValue || change.CreatedUtc > finalCreatedAt.Value
              || change.CreatedUtc == finalCreatedAt.Value
                  && string.CompareOrdinal(change.ChangeId, finalChange) > 0) {
            finalCreatedAt = change.CreatedUtc;
            finalChange = change.ChangeId;
          }
          if (change.State == "restored") continue;
          var completed = change.CompletedUtc.Value;
          if (!ValidBindingInstanceIdentity(change.Applied.BindingInstanceId)) {
            if (!markerlessAt.HasValue || completed > markerlessAt.Value
                || completed == markerlessAt.Value
                    && string.CompareOrdinal(change.ChangeId, markerlessChange) > 0) {
              markerlessAt = completed;
              markerlessChange = change.ChangeId;
            }
            continue;
          }
          if (selectedAt.HasValue && (completed < selectedAt.Value
              || completed == selectedAt.Value
                  && string.CompareOrdinal(change.ChangeId, selectedChange) <= 0)) continue;
          selected = BindingSelection.Selected(change.Applied.BindingInstanceId);
          selectedAt = completed;
          selectedChange = change.ChangeId;
        }
      }
      if (selected.State != BindingSelectionState.Unavailable) {
        var pendingWins = pendingCreatedAt.HasValue && (!finalCreatedAt.HasValue
            || pendingCreatedAt.Value > finalCreatedAt.Value
            || pendingCreatedAt.Value == finalCreatedAt.Value
                && string.CompareOrdinal(pendingChange, finalChange) > 0);
        var markerlessWins = markerlessAt.HasValue && (!selectedAt.HasValue
            || markerlessAt.Value > selectedAt.Value
            || markerlessAt.Value == selectedAt.Value
                && string.CompareOrdinal(markerlessChange, selectedChange) >= 0);
        if (pendingWins)
          selected = BindingSelection.Unavailable("binding_selection_history_pending");
        else if (markerlessWins)
          selected = BindingSelection.Unavailable("binding_selection_marker_missing");
        else if (!selectedAt.HasValue && relevant)
          selected = BindingSelection.Unavailable("binding_selection_marker_missing");
      }
    } catch { selected = BindingSelection.Unavailable("binding_selection_history_unreadable"); }
    cachedBindingSelectionKey = key;
    cachedBindingSelection = selected;
    nextBindingSelectionRefresh = now + 1.0;
    return selected;
  }

  static bool ValidBindingIdentity(string value) {
    var parts = (value ?? string.Empty).Split(':');
    return parts.Length == 2 && long.TryParse(parts[0], out _)
        && uint.TryParse(parts[1], out _);
  }

  static bool ValidBindingInstanceIdentity(string value) =>
      !string.IsNullOrWhiteSpace(value) && value.Length == 36
      && value.StartsWith("binding-", StringComparison.Ordinal)
      && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or 'T' or 'Z');

  static string Null(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

  static bool ValidBindingReference(RuntimeBindingReference value) =>
      value != null && CharmPolicy.ValidateReference(new CharmReference {
        PackId = value.PackId,
        ExperienceId = value.ExperienceId,
        BindingId = value.BindingId,
        Version = value.Version,
        ContentHash = value.ContentHash,
      }).Allowed;

  static RuntimeReceipt EventReceipt(
      string status,
      Active active,
      string bindingZdo,
      RuntimeEvent evt,
      string currentStage,
      string nextStage,
      TriggerProgress progress,
      string correlationId,
      TriggerClauseTrace evidence = null,
      IReadOnlyList<RejectedTransitionEvidence> rejectedEvidence = null,
      WorkflowIdentity identity = null) => new() {
    Operation = "event",
    Status = status,
    PackId = active.PackId,
    Version = active.Version,
    ContentHash = active.ContentHash,
    ActivationId = active.ActivationId,
    CorrelationId = correlationId,
    BindingZdo = bindingZdo,
    BindingInstanceId = identity?.BindingInstanceId,
    ExperienceId = identity?.ExperienceId ?? active.Document.Id,
    RunId = identity?.RunId,
    WorldId = identity?.WorldId,
    EventName = evt?.Name,
    EventTarget = evt?.Target,
    ActorRole = evt == null ? null : CooperativeEventContract.ActorRole(evt),
    CurrentStageId = currentStage,
    NextStageId = nextStage,
    CurrentCount = progress?.Current,
    RequiredCount = progress?.Required,
    Evidence = evidence,
    RejectedEvidence = rejectedEvidence,
    Diagnostics = Array.Empty<ContractDiagnostic>(),
  };

  /// <summary>Recomputed once a second from the cached binding set, well off the event hot path.</summary>
  void RefreshDeadline(DateTimeOffset now) {
    string line = null;
    var seconds = -1;
    try {
      if (TryLoad(out var active, out _)) {
        var soonest = timers.Pending(now, identity => IsCurrentRun(identity, active), 1).FirstOrDefault();
        if (soonest != null) {
          seconds = (int)Math.Ceiling((soonest.DueUtc - now).TotalSeconds);
          line = Countdown(seconds, null);
        } else {
          foreach (var wear in Bindings(active)) {
            var zdo = wear?.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) continue;
            var reference = Read(zdo);
            if (reference == null || reference.ContentHash != active.ContentHash) continue;
            if (!TryActiveIdentity(zdo, active, out var identity, out _)) continue;
            var progress = workflows.Get(identity);
            if (progress == null || !string.IsNullOrWhiteSpace(progress.Outcome)) break;
            var stage = active.Document.Stages.FirstOrDefault(value => value.Id == progress.StageId);
            foreach (var transition in (stage?.Transitions ?? new())
                .OrderByDescending(value => value.Priority)
                .ThenBy(value => value.Id, StringComparer.Ordinal)) {
              var deadline = TriggerCountdown.Read(transition.When, progress.History, now);
              if (!deadline.Running) continue;
              seconds = deadline.RemainingSeconds;
              line = Countdown(seconds, deadline.Current + "/" + deadline.Required);
              break;
            }
            break;
          }
        }
      }
      deadlineError = null;
    } catch (Exception e) {
      // Session 2 could not tell "no deadline is running" from "reading the deadline threw":
      // this branch was silent. One receipt per distinct failure, then quiet until it changes.
      line = null;
      if (!string.Equals(deadlineError, e.Message, StringComparison.Ordinal)) {
        deadlineError = e.Message;
        try { Write("transition", "deadline_unreadable", null, null, e.Message, null); }
        catch { }
      }
    }
    lock (evidenceGate) {
      deadlineLine = line;
      deadlineUrgent = line != null && seconds >= 0 && seconds <= 5;
    }
  }

  // One separator convention with TriggerDeadline.Label: "1/2, 6 seconds remaining".
  static string Countdown(int seconds, string progress) =>
      seconds < 0 ? null
      : (progress == null ? "" : progress + ", ") + TriggerCountdown.Seconds(seconds) + " remaining";

  static IReadOnlyList<RejectedTransitionEvidence> ExplainRejected(
      ExperienceStage stage,
      ExperienceTransition selected,
      IReadOnlyList<RuntimeEvent> history,
      TriggerEvaluationContext context) =>
      UnmetRoutes(stage, selected.Priority, history, context);

  /// <summary>Why branches did not take this event, in their own words. With a rank, only the branches
  /// that outrank the winner; without one, every branch — nothing was chosen, so nothing outranks.</summary>
  static IReadOnlyList<RejectedTransitionEvidence> UnmetRoutes(
      ExperienceStage stage,
      int? abovePriority,
      IReadOnlyList<RuntimeEvent> history,
      TriggerEvaluationContext context) {
    var rejected = new List<RejectedTransitionEvidence>();
    foreach (var candidate in (stage?.Transitions ?? new())
        .Where(value => value != null
            && (!abovePriority.HasValue || value.Priority > abovePriority.Value))
        .OrderByDescending(value => value.Priority)
        .ThenBy(value => value.Id, StringComparer.Ordinal)) {
      var trace = TriggerEvaluator.Explain(candidate.When, history, context);
      if (trace.Satisfied) continue;
      rejected.Add(new RejectedTransitionEvidence {
        TransitionId = candidate.Id,
        Evidence = trace,
      });
      if (rejected.Count == 3) break;
    }
    return rejected.Count == 0 ? null : rejected;
  }

  static string MatchedLine(
      string stage, ExperienceTransition transition, TriggerProgress progress,
      IReadOnlyList<RejectedTransitionEvidence> rejected = null) {
    var count = progress?.Required > 1
        ? " — " + progress.Current + "/" + progress.Required : "";
    return "Matched " + Describe(transition?.When) + count + "; "
        + stage + " → " + Destination(transition) + "." + RejectedLine(rejected);
  }

  /// <summary>Why the branches that outrank the winner did not take it, in their own words.</summary>
  static string RejectedLine(IReadOnlyList<RejectedTransitionEvidence> rejected) {
    if (rejected == null || rejected.Count == 0) return "";
    var reasons = rejected.Select(value => value.TransitionId + " needs " + Unmet(value.Evidence))
        .Take(2).ToArray();
    return " Not " + string.Join("; not ", reasons) + ".";
  }

  static string Unmet(TriggerClauseTrace trace) {
    if (trace == null) return "an unmet condition";
    var where = Where(trace).FirstOrDefault(value => !value.Satisfied
        && !string.IsNullOrWhiteSpace(value.Expected));
    if (where != null)
      return where.Expected + (string.IsNullOrWhiteSpace(where.Actual) ? "" : " (" + where.Actual + ")");
    return trace.Required > 1 ? trace.Current + "/" + trace.Required : "its player action";
  }

  static IEnumerable<TriggerWhereTrace> Where(TriggerClauseTrace trace) {
    if (trace == null) yield break;
    foreach (var entry in trace.Where ?? new List<TriggerWhereTrace>()) yield return entry;
    foreach (var child in trace.Children ?? new List<TriggerClauseTrace>())
      foreach (var found in Where(child)) yield return found;
  }

  static string TransitionLine(string stage, ExperienceTransition transition) =>
      "Advanced " + stage + " → " + Destination(transition) + ".";

  static string Destination(ExperienceTransition transition) {
    if (!string.IsNullOrWhiteSpace(transition?.Outcome))
      return "outcome " + transition.Outcome;
    return string.IsNullOrWhiteSpace(transition?.NextStage)
        ? "current stage" : transition.NextStage;
  }

  static string ActionLine(ExperienceAction action, string result) =>
      ActionName(action) + " — " + result + ".";

  static string ActionName(ExperienceAction action) {
    switch (action?.Type) {
      case "message": return "Show the message";
      case "timer_start": return "Start the timer";
      case "timer_cancel": return "Cancel the timer";
      case "grant_item": return "Give the item reward";
      case "spawn": return "Create the staged object";
      case "clear_spawned": return "Clear the staged objects";
      default: return "Run " + (action?.Id ?? "the action");
    }
  }

  bool Execute(
      Active active,
      ZDO zdo,
      string stage,
      string transition,
      ExperienceAction action,
      string correlationId,
      bool personal) {
    WorkflowIdentity identity = null;
    var zdoId = zdo.m_uid.ToString();
    string key = null;
    try {
      if (personal && !DedicatedPersonalProgressionPolicy.Message(action))
        throw new InvalidOperationException("dedicated_personal_progression_action_denied");
      identity = ResolveIdentity(zdo, active);
      if (identity == null) throw new InvalidOperationException("runtime_player_missing");
      key = string.Join("|", identity.Key, stage, transition, action.Id);
      var prior = ledger.ExecutionState(key);
      if (prior == ActionExecutionState.Committed) {
        pendingReplays.Remove(key);
        WriteReceipt(ActionReceipt(
            "suppressed", "duplicate_suppressed", active, zdoId, stage,
            transition, action.Id, correlationId, identity), ActionLine(action, "duplicate suppressed"));
        return true;
      }
      if (prior == ActionExecutionState.Pending) {
        WriteReceipt(ActionReceipt(
            "rejected", "action_execution_pending", active, zdoId, stage,
            transition, action.Id, correlationId, identity),
            ActionLine(action, "blocked: prior execution is unresolved"), CreatorEvidenceKind.Warning);
        return false;
      }

      UnityEngine.GameObject grantPrefab = null;
      var grantCount = 0;
      if (action.Type == "grant_item"
          && !TryPrepareGrant(action, out grantPrefab, out grantCount, out var grantError)) {
        if (grantError == "grant_inventory_full")
          SchedulePendingReplay(key, identity, action, DateTimeOffset.UtcNow.AddSeconds(1));
        WriteReceipt(ActionReceipt(
            "rejected", grantError, active, zdoId, stage,
            transition, action.Id, correlationId, identity),
            ActionLine(action, "blocked: " + grantError), CreatorEvidenceKind.Warning);
        return false;
      }

      UnityEngine.GameObject spawnPrefab = null;
      string spawnKind = null, spawnPrefabName = null;
      var spawnCount = 0;
      var spawnRadius = 0;
      if (action.Type == "spawn"
          && !TryPrepareSpawn(action, out spawnPrefab, out spawnKind, out spawnPrefabName,
              out spawnCount, out spawnRadius, out var spawnError)) {
        WriteReceipt(ActionReceipt(
            "rejected", spawnError, active, zdoId, stage,
            transition, action.Id, correlationId, identity),
            ActionLine(action, "blocked: " + spawnError), CreatorEvidenceKind.Warning);
        return false;
      }

      if (!ledger.TryReserve(key)) {
        var raced = ledger.ExecutionState(key);
        if (raced == ActionExecutionState.Committed) {
          pendingReplays.Remove(key);
          WriteReceipt(ActionReceipt(
              "suppressed", "duplicate_suppressed", active, zdoId, stage,
              transition, action.Id, correlationId, identity), ActionLine(action, "duplicate suppressed"));
          return true;
        }
        throw new InvalidOperationException("action_execution_pending");
      }

      switch (action.Type) {
        case "message":
          Say(Param(action, "text"));
          break;
        case "timer_start":
          timers.Start(identity, Param(action, "timer_id"),
              DateTimeOffset.UtcNow.AddSeconds(IntParam(action, "seconds")));
          break;
        case "timer_cancel":
          timers.Cancel(identity, Param(action, "timer_id"));
          break;
        case "grant_item":
          // AddItem can mutate a partial stack and still return false. Once the reservation exists,
          // any false/throw is deliberately left pending rather than risking a duplicate reward.
          if (Player.m_localPlayer == null
              || !Player.m_localPlayer.GetInventory().AddItem(grantPrefab, grantCount))
            throw new InvalidOperationException("grant_commit_ambiguous");
          break;
        case "spawn":
          if (!Spawn(active, zdo, action, key, identity, spawnPrefab, spawnKind,
              spawnPrefabName, spawnCount, spawnRadius, out var spawnRetrySafe,
              out var spawnCommitError)) {
            if (spawnRetrySafe) {
              try {
                if (!ledger.Release(key)) spawnRetrySafe = false;
              } catch { spawnRetrySafe = false; }
            }
            if (spawnRetrySafe)
              SchedulePendingReplay(key, identity, action, DateTimeOffset.UtcNow.AddSeconds(1));
            throw new InvalidOperationException(spawnRetrySafe
                ? spawnCommitError ?? "spawn_failed_retriable"
                : spawnCommitError ?? "spawn_recovery_failed");
          }
          break;
        case "clear_spawned":
          Clear(active, Param(action, "action_id"), identity, stage, transition);
          break;
        default:
          throw new InvalidOperationException("action_not_implemented");
      }
      // Commit before evidence. If receipt I/O fails after a successful mutation, the durable
      // action remains complete and a replay suppresses it instead of duplicating the effect.
      ledger.Commit(key);
      pendingReplays.Remove(key);
    } catch (Exception e) {
      try {
        WriteReceipt(ActionReceipt(
            "rejected", e.Message, active, zdoId, stage, transition, action?.Id,
            correlationId, identity), ActionLine(action, "failed: " + e.Message), CreatorEvidenceKind.Warning);
      } catch { }
      return false;
    }
    try {
      var executedReceipt = ActionReceipt(
          "executed", null, active, zdoId, stage, transition, action.Id,
          correlationId, identity);
      if (action.Type == "message") executedReceipt.DisplayMessage = Param(action, "text");
      WriteReceipt(executedReceipt, ActionLine(action, "executed"));
    } catch { }
    return true;
  }

  /// <summary>The authored story speaks twice: Center carries the moment, the chat log keeps it.
  /// Session 2 praised the story text and named the gap in the same breath — "we should also post it
  /// in chat … so there's history of it not just the glimpse". The chat write is client-local; a
  /// missing or unavailable chat window costs the moment nothing.</summary>
  static void Say(string text) {
    if (string.IsNullOrWhiteSpace(text)) return;
    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, text);
    try { Chat.instance?.AddString(text); } catch { }
  }

  static string Param(ExperienceAction action, string name) =>
      action.Parameters != null && action.Parameters.TryGetValue(name, out var value)
          ? value.ToString() : "";

  static int IntParam(ExperienceAction action, string name) =>
      action.Parameters != null && action.Parameters.TryGetValue(name, out var value)
          ? value.ToObject<int>() : 0;

  static bool TryPrepareGrant(
      ExperienceAction action,
      out UnityEngine.GameObject prefab,
      out int count,
      out string error) {
    prefab = null;
    var item = Param(action, "item");
    count = IntParam(action, "quantity");
    if (!MutationRegistry.TryGrant(item, count, out error)) return false;
    prefab = ZNetScene.instance?.GetPrefab(item);
    if (prefab == null || prefab.GetComponent<ItemDrop>() == null
        || Player.m_localPlayer == null) { error = "grant_prefab_unavailable"; return false; }
    if (!Player.m_localPlayer.GetInventory().CanAddItem(prefab, count)) {
      error = "grant_inventory_full";
      return false;
    }
    error = null;
    return true;
  }

  bool TryPrepareSpawn(
      ExperienceAction action,
      out UnityEngine.GameObject prefab,
      out string kind,
      out string prefabName,
      out int count,
      out int radius,
      out string error) {
    kind = Param(action, "kind");
    prefabName = Param(action, "prefab");
    count = IntParam(action, "count");
    radius = IntParam(action, "radius");
    prefab = null;
    if (!MutationRegistry.CanSpawn(kind, prefabName)
        || count < 1 || count > 16 || radius < 0 || radius > 30) {
      error = "spawn_parameters_invalid";
      return false;
    }
    prefab = ZNetScene.instance?.GetPrefab(prefabName);
    if (prefab == null || !PrefabMatches(prefab, kind)) {
      error = "spawn_prefab_unavailable";
      return false;
    }
    if (!spawned.CanRecord(count, out error)) return false;
    error = null;
    return true;
  }

  bool Spawn(
      Active active,
      ZDO binding,
      ExperienceAction action,
      string key,
      WorkflowIdentity identity,
      UnityEngine.GameObject prefab,
      string kind,
      string prefabName,
      int count,
      int radius,
      out bool retrySafe,
      out string error) {
    retrySafe = false;
    error = null;
    var created = new List<UnityEngine.GameObject>();
    var records = new List<SpawnedObject>();
    try {
      var center = binding.GetPosition();
      for (var i = 0; i < count; i++) {
        var angle = (float)(i * Math.PI * 2 / Math.Max(1, count));
        var distance = radius;
        var position = center + new UnityEngine.Vector3(
            (float)Math.Cos(angle) * distance, 0.5f, (float)Math.Sin(angle) * distance);
        var go = UnityEngine.Object.Instantiate(
            prefab, position, UnityEngine.Quaternion.identity);
        if (go == null) throw new InvalidOperationException("spawn_instantiate_failed");
        created.Add(go);
        var view = go.GetComponent<ZNetView>();
        var zdo = view?.GetZDO();
        if (zdo == null) throw new InvalidOperationException("spawn_identity_unavailable");
        zdo.Set(Prefix + "spawnedContentHash", active.ContentHash);
        zdo.Set(Prefix + "spawnedActionId", action.Id);
        zdo.Set(Prefix + "spawnedActionKey", key);
        RuntimeSpawnIdentity.Mark(zdo);
        var piece = go.GetComponent<Piece>();
        if (piece != null && Player.m_localPlayer != null)
          piece.SetCreator(Player.m_localPlayer.GetPlayerID(),
              Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
        var record = new SpawnedObject {
          ActionKey = key,
          RunId = identity.RunId,
          WorldId = identity.WorldId,
          ExperienceId = active.Document.Id,
          ContentHash = active.ContentHash,
          ActionId = action.Id,
          Kind = kind,
          Prefab = prefabName,
          X = position.x,
          Y = position.y,
          Z = position.z,
          UserId = zdo.m_uid.UserID,
          ObjectId = zdo.m_uid.ID,
        };
        records.Add(record);
        // Record each exact tagged ZDO before creating the next one. A process loss can leave at
        // most the narrow gap between tag and row, rather than an entire untracked batch.
        spawned.Record(record);
      }
      return true;
    } catch (Exception failure) {
      error = failure.Message;
      retrySafe = BeginSpawnRecovery(created, records, key, identity, action, out var queued);
      if (!retrySafe) error = queued ? "spawn_recovery_pending" : "spawn_recovery_failed";
      return false;
    }
  }

  bool BeginSpawnRecovery(
      IReadOnlyList<UnityEngine.GameObject> created,
      IReadOnlyList<SpawnedObject> records,
      string key,
      WorkflowIdentity identity,
      ExperienceAction action,
      out bool queued) {
    queued = false;
    try {
      foreach (var go in created) {
        if (go == null) continue;
        var view = go.GetComponent<ZNetView>();
        var zdo = view?.GetZDO();
        if (zdo == null) {
          UnityEngine.Object.Destroy(go);
          continue;
        }
        var record = records.FirstOrDefault(value => value.UserId == zdo.m_uid.UserID
            && value.ObjectId == zdo.m_uid.ID);
        if (record == null
            || !string.Equals(zdo.GetString(Prefix + "spawnedActionKey", ""), key,
                StringComparison.Ordinal)
            || !string.Equals(zdo.GetString(Prefix + "spawnedContentHash", ""),
                record.ContentHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(zdo.GetString(Prefix + "spawnedActionId", ""),
                record.ActionId, StringComparison.Ordinal)) return false;
        if (view != null) {
          view.ClaimOwnership();
          view.Destroy();
        } else {
          zdo.SetOwner(ZDOMan.GetSessionID());
          ZDOMan.instance.DestroyZDO(zdo);
        }
      }
      if (TryFinishSpawnRecovery(key, records)) return true;
      if (!spawnRecoveries.ContainsKey(key) && spawnRecoveries.Count >= MaxSpawnRecoveries)
        return false;
      spawnRecoveries[key] = new SpawnRecovery {
        ActionKey = key,
        Identity = identity,
        Action = action,
        Manager = ZDOMan.instance,
        NetworkSessionId = ZDOMan.GetSessionID(),
        Records = records.ToArray(),
      };
      queued = true;
      return false;
    } catch { return false; }
  }

  bool TryFinishSpawnRecovery(string key, IReadOnlyList<SpawnedObject> records) {
    try {
      if (ZDOMan.instance == null) return false;
      if ((records ?? Array.Empty<SpawnedObject>()).Any(value =>
          RuntimeSpawnIdentity.Resolve(value) != null)) return false;
      spawned.Remove(records ?? Array.Empty<SpawnedObject>());
      return spawned.ForAction(key).Count == 0;
    } catch { return false; }
  }

  void RunSpawnRecoveries(DateTimeOffset now) {
    if (spawnRecoveries.Count == 0) return;
    foreach (var recovery in spawnRecoveries.Values.ToArray()) {
      // Absence is evidence only while the addressed world/run is actually loaded. During unload
      // ZDOMan is null; treating that as absence would erase the durable rows while leaving the
      // objects in the world save.
      if (ZDOMan.instance == null || !TryLoad(out var active, out _)) continue;
      if (!ReferenceEquals(ZDOMan.instance, recovery.Manager)
          || ZDOMan.GetSessionID() != recovery.NetworkSessionId) {
        // Physical ZDO ids may be reassigned across a cold load. Keep the durable pending claim
        // and rows for explicit reset; never infer absence in a different network generation.
        spawnRecoveries.Remove(recovery.ActionKey);
        continue;
      }
      if (!IsCurrentRun(recovery.Identity, active)) {
        // A reset owns cleanup through its durable reset transaction. Never mutate rows or replay
        // an in-memory recovery after that run is retired.
        spawnRecoveries.Remove(recovery.ActionKey);
        continue;
      }
      if (!TryFinishSpawnRecovery(recovery.ActionKey, recovery.Records)) continue;
      try {
        var state = ledger.ExecutionState(recovery.ActionKey);
        if (state == ActionExecutionState.Committed) {
          spawnRecoveries.Remove(recovery.ActionKey);
          continue;
        }
        if (state == ActionExecutionState.Pending && !ledger.Release(recovery.ActionKey))
          continue;
      } catch { continue; }
      // Keep the recovery until its automatic replay is durably represented in the bounded
      // in-memory queue. A full queue or transient workflow read gets another tick.
      if (SchedulePendingReplay(recovery.ActionKey, recovery.Identity, recovery.Action, now))
        spawnRecoveries.Remove(recovery.ActionKey);
    }
  }

  bool SchedulePendingReplay(
      string actionKey,
      WorkflowIdentity identity,
      ExperienceAction action,
      DateTimeOffset nextUtc) {
    if (string.IsNullOrWhiteSpace(actionKey) || identity == null || action == null
        || !TryPendingRoute(identity, out var stageId, out var transitionId)) return false;
    if (pendingReplays.TryGetValue(actionKey, out var existing)) {
      if (nextUtc < existing.NextUtc) existing.NextUtc = nextUtc;
      return true;
    }
    if (pendingReplays.Count >= MaxPendingReplays) return false;
    pendingReplays[actionKey] = new PendingReplay {
      ActionKey = actionKey,
      Identity = identity,
      StageId = stageId,
      TransitionId = transitionId,
      Action = action,
      NextUtc = nextUtc,
    };
    return true;
  }

  void RunPendingReplays(DateTimeOffset now) {
    if (pendingReplays.Count == 0) return;
    foreach (var group in pendingReplays.Values.Where(value => value.NextUtc <= now)
        .GroupBy(value => value.Identity.Key, StringComparer.Ordinal).ToArray()) {
      foreach (var stale in group.Where(value => !IsExpectedPending(value)).ToArray())
        pendingReplays.Remove(stale.ActionKey);
      var replay = group.Where(IsExpectedPending)
          .OrderBy(value => value.ActionKey, StringComparer.Ordinal).FirstOrDefault();
      if (replay == null) continue;
      if (!ReplayPreconditionReady(replay.Action)) {
        foreach (var pending in group.Where(IsExpectedPending))
          pending.NextUtc = now.AddSeconds(2);
        continue;
      }
      var accepted = ReplayPending(
          replay.Identity, now, replay.StageId, replay.TransitionId);
      var matching = pendingReplays.Where(value => value.Value.Identity.Key == group.Key
          && string.Equals(value.Value.StageId, replay.StageId, StringComparison.Ordinal)
          && string.Equals(value.Value.TransitionId, replay.TransitionId, StringComparison.Ordinal))
          .Select(value => value.Key).ToArray();
      if (accepted && !IsExpectedPending(replay)) {
        foreach (var key in matching) pendingReplays.Remove(key);
      } else {
        foreach (var key in matching) pendingReplays[key].NextUtc = now.AddSeconds(2);
      }
    }
  }

  bool TryPendingRoute(
      WorkflowIdentity identity,
      out string stageId,
      out string transitionId) {
    stageId = null;
    transitionId = null;
    if (identity == null) return false;
    var progress = workflows.Get(identity);
    if (progress == null || string.IsNullOrWhiteSpace(progress.PendingTransitionId)
        || !string.Equals(progress.StageId, progress.PendingFromStageId, StringComparison.Ordinal))
      return false;
    stageId = progress.StageId;
    transitionId = progress.PendingTransitionId;
    return true;
  }

  bool IsExpectedPending(PendingReplay replay) {
    if (replay?.Identity == null) return false;
    var progress = workflows.Get(replay.Identity);
    return progress != null
        && string.Equals(progress.StageId, replay.StageId, StringComparison.Ordinal)
        && string.Equals(progress.PendingFromStageId, replay.StageId, StringComparison.Ordinal)
        && string.Equals(progress.PendingTransitionId, replay.TransitionId, StringComparison.Ordinal);
  }

  bool ReplayPreconditionReady(ExperienceAction action) {
    if (action?.Type == "grant_item")
      return TryPrepareGrant(action, out _, out _, out _);
    if (action?.Type == "spawn")
      return TryPrepareSpawn(action, out _, out _, out _, out _, out _, out _);
    return true;
  }

  bool ReplayPending(
      WorkflowIdentity identity,
      DateTimeOffset now,
      string expectedStageId = null,
      string expectedTransitionId = null) {
    try {
      if (!TryLoad(out var active, out _) || !IsCurrentRun(identity, active)
          || !TryCurrentBinding(identity, active, out var binding)) return false;
      var progress = workflows.Get(identity);
      if (progress == null || string.IsNullOrWhiteSpace(progress.PendingTransitionId)
          || !string.Equals(progress.StageId, progress.PendingFromStageId, StringComparison.Ordinal)
          || (!string.IsNullOrWhiteSpace(expectedStageId)
              && !string.Equals(progress.StageId, expectedStageId, StringComparison.Ordinal))
          || (!string.IsNullOrWhiteSpace(expectedTransitionId)
              && !string.Equals(progress.PendingTransitionId, expectedTransitionId,
                  StringComparison.Ordinal))) return false;
      var observed = RuntimeObservation.Facts(
          binding, spawned, identity.Key, active.ContentHash);
      // A pending transition was earned by a real prior event. Never fabricate a replay event:
      // after another path has completed, such an event could be evaluated against the next stage.
      var cause = LastEvent(identity);
      if (cause == null) return false;
      var decision = workflows.Begin(
          identity, active.Document, cause, observed.Spatial, observed.Encounter);
      if (decision != null
          && string.Equals(decision.StageId, progress.StageId, StringComparison.Ordinal)
          && string.Equals(decision.Transition?.Id, progress.PendingTransitionId,
              StringComparison.Ordinal)) {
        Apply(active, binding, decision, cause, NewRecheckId());
        return true;
      }
    } catch (Exception e) {
      try { Write("action", "action_replay_failed", active: null, status: null,
          detail: e.Message, correlationId: null); } catch { }
    }
    return false;
  }

  static bool PrefabMatches(UnityEngine.GameObject prefab, string kind) =>
      kind == "creature"
          ? prefab.GetComponent<Character>() != null && prefab.GetComponent<Player>() == null
          : kind == "item"
              ? prefab.GetComponent<ItemDrop>() != null
              : kind == "piece" && prefab.GetComponent<Piece>() != null;

  void Clear(
      Active active,
      string targetAction,
      WorkflowIdentity identity,
      string stage,
      string transition) {
    var records = spawned.ForOwnerAction(identity.Key, targetAction);
    var removed = new List<SpawnedObject>();
    foreach (var record in records) {
      var zdo = RuntimeSpawnIdentity.Resolve(record);
      if (zdo == null) {
        removed.Add(record);
        continue;
      }
      if (record.ContentHash != active.ContentHash
          || zdo.GetString(Prefix + "spawnedContentHash", "") != record.ContentHash
          || zdo.GetString(Prefix + "spawnedActionId", "") != record.ActionId
          || zdo.GetString(Prefix + "spawnedActionKey", "") != record.ActionKey) continue;
      var view = ZNetScene.instance?.FindInstance(zdo);
      if (view != null) {
        view.ClaimOwnership();
        view.Destroy();
      } else {
        zdo.SetOwner(ZDOMan.GetSessionID());
        ZDOMan.instance.DestroyZDO(zdo);
      }
      removed.Add(record);
    }
    spawned.Remove(removed);
  }

  static RuntimeReceipt ActionReceipt(
      string status,
      string error,
      Active active,
      string zdo,
      string stage,
      string transition,
      string action,
      string correlationId,
      WorkflowIdentity identity = null) => new() {
    Operation = "action",
    Status = status,
    Error = error,
    PackId = active.PackId,
    Version = active.Version,
    ContentHash = active.ContentHash,
    ActivationId = active.ActivationId,
    CorrelationId = correlationId,
    BindingZdo = zdo,
    BindingInstanceId = identity?.BindingInstanceId,
    ExperienceId = identity?.ExperienceId ?? active.Document.Id,
    RunId = identity?.RunId,
    WorldId = identity?.WorldId,
    StageId = stage,
    CurrentStageId = stage,
    TransitionId = transition,
    ActionId = action,
    Diagnostics = Array.Empty<ContractDiagnostic>(),
  };

  CharmReference Read(ZDO zdo) {
    var reference = new CharmReference {
      PackId = zdo.GetString(Prefix + "packId", ""),
      ExperienceId = zdo.GetString(Prefix + "experienceId", ""),
      BindingId = zdo.GetString(Prefix + "bindingId", ""),
      Version = zdo.GetString(Prefix + "version", ""),
      ContentHash = zdo.GetString(Prefix + "contentHash", ""),
    };
    return CharmPolicy.ValidateReference(reference).Allowed ? reference : null;
  }

  bool TryRunScope(ZDO zdo, Active active, out RuntimeRunScope scope) {
    scope = null;
    try {
      var player = Player.m_localPlayer;
      if (zdo == null || active?.Document == null || player == null || ZNet.instance == null)
        return false;
      var participant = player.GetPlayerID();
      var world = ZNet.instance.GetWorldUID();
      if (participant == 0L || world == 0L) return false;
      scope = new RuntimeRunScope {
        WorldId = world.ToString(),
        ExperienceId = active.Document.Id,
        BindingZdo = zdo.m_uid.ToString(),
        BindingInstanceId = Null(zdo.GetString(Prefix + "bindingInstanceId", "")),
        ContentHash = active.ContentHash,
        ParticipantIds = new List<string> { participant.ToString() },
      };
      return true;
    } catch { return false; }
  }

  /// <summary>Run creation belongs only to an authored event (including the explicit
  /// experience-started event). Status and progress callers use TryActiveIdentity instead.</summary>
  WorkflowIdentity ResolveIdentity(ZDO zdo, Active active) {
    if (!TryRunScope(zdo, active, out var scope)) return null;
    return CurrentIdentity(runs.Resolve(scope, DateTimeOffset.UtcNow), scope);
  }

  bool TryActiveIdentity(ZDO zdo, Active active, out WorkflowIdentity identity,
      out RuntimeRunRecord record) {
    identity = null;
    record = null;
    if (!TryRunScope(zdo, active, out var scope)) return false;
    record = runs.Registry.Active(scope);
    if (record == null) return false;
    identity = CurrentIdentity(record, scope);
    return true;
  }

  /// <summary>The registry owns stable run identity. The loaded object owns today's physical ZDO
  /// identity; never leak the pre-cold-load ZDO retained in the original run record.</summary>
  static WorkflowIdentity CurrentIdentity(RuntimeRunRecord record, RuntimeRunScope current) {
    var identity = record?.Identity();
    if (identity == null) return null;
    identity.BindingZdo = current.BindingZdo;
    identity.BindingInstanceId = current.BindingInstanceId;
    return identity;
  }

  bool TryCurrentBinding(WorkflowIdentity identity, Active active, out ZDO zdo) {
    zdo = null;
    if (identity == null || active?.Document == null) return false;
    var personal = PersonalDecision(active).Allowed;
    if (!personal) {
      var selection = SelectedBinding(active);
      if (selection.State == BindingSelectionState.Unavailable) return false;
      if (selection.State == BindingSelectionState.Selected
          && (!string.Equals(identity.BindingInstanceId, selection.BindingInstanceId,
                  StringComparison.Ordinal))) return false;
      if (selection.State == BindingSelectionState.Legacy
          && !string.IsNullOrWhiteSpace(identity.BindingInstanceId)) return false;
    }
    foreach (var wear in Bindings(active)) {
      var candidate = wear?.GetComponent<ZNetView>()?.GetZDO();
      if (candidate == null) continue;
      var matches = !string.IsNullOrWhiteSpace(identity.BindingInstanceId)
          ? string.Equals(candidate.GetString(Prefix + "bindingInstanceId", ""),
              identity.BindingInstanceId, StringComparison.Ordinal)
          : string.Equals(candidate.m_uid.ToString(), identity.BindingZdo,
              StringComparison.Ordinal);
      if (!matches) continue;
      zdo = candidate;
      return true;
    }
    return false;
  }

  bool IsCurrentRun(WorkflowIdentity identity, Active active) {
    if (identity == null || active?.Document == null || !runs.Registry.IsActive(identity)
        || identity.CharacterId == "0"
        || !string.Equals(identity.ContentHash, active.ContentHash, StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrWhiteSpace(identity.ExperienceId)
            && !string.Equals(identity.ExperienceId, active.Document.Id, StringComparison.Ordinal)) return false;
    try {
      var player = Player.m_localPlayer;
      if (ZNet.instance == null || player == null) return false;
      var participant = player.GetPlayerID();
      var world = ZNet.instance.GetWorldUID();
      if (participant == 0L || world == 0L
          || !string.Equals(identity.WorldId, world.ToString(),
              StringComparison.Ordinal)
          || !string.Equals(identity.CharacterId, participant.ToString(),
              StringComparison.Ordinal)) return false;
      return TryCurrentBinding(identity, active, out _);
    } catch { return false; }
  }

  /// <summary>What the running telling is doing right now, without re-announcing its name: the status
  /// card owns the title, and session 2 read the same title stacked in three places as a loss of
  /// section boundaries. An ending is reported as the player's outcome, never the raw token.</summary>
  public string DescribeProgress() {
    try {
      if (!TryLoad(out var active, out _)) return "not ready";
      foreach (var wear in Bindings(active)) {
        var zdo = wear?.GetComponent<ZNetView>()?.GetZDO();
        if (zdo == null) continue;
        var reference = Read(zdo);
        if (reference == null || reference.ContentHash != active.ContentHash) continue;
        if (!TryRunScope(zdo, active, out _)) return "not ready";
        if (!TryActiveIdentity(zdo, active, out var identity, out _)) {
          var first = active.Document.Stages.FirstOrDefault(
              value => value.Id == active.Document.EntryStage);
          return "Not started - "
              + Describe(first?.Transitions?.FirstOrDefault()?.When);
        }
        var progress = workflows.Get(identity);
        if (progress == null) {
          var first = active.Document.Stages.FirstOrDefault(
              value => value.Id == active.Document.EntryStage);
          return "Not started - "
              + Describe(first?.Transitions?.FirstOrDefault()?.When);
        }
        if (!string.IsNullOrWhiteSpace(progress.Outcome))
          return Outcome(progress.Outcome);
        var stage = active.Document.Stages.FirstOrDefault(value => value.Id == progress.StageId);
        var transition = stage?.Transitions?.OrderByDescending(value => value.Priority)
            .ThenBy(value => value.Id, StringComparer.Ordinal).FirstOrDefault();
        var observed = RuntimeObservation.Facts(
            zdo, spawned, identity.Key, active.ContentHash);
        var measured = TriggerEvaluator.Measure(transition?.When, progress.History, new TriggerEvaluationContext {
          At = progress.LastEventUtc,
          StageEnteredUtc = progress.StageEnteredUtc,
          LastProgressUtc = progress.LastProgressUtc,
          BindingPosition = observed.Spatial.BindingPosition,
          SpawnedPositions = observed.Spatial.SpawnedPositions,
          SpatialAreas = SpatialEvaluator.AreaMap(active.Document),
          DeathsInStage = progress.DeathsInStage,
          SpawnsByAction = observed.Encounter?.SpawnsByAction,
        });
        var count = measured.Required > 1
            ? " - " + measured.Current + "/" + measured.Required : "";
        var running = Deadline();
        return progress.StageId + " - "
            + Describe(transition?.When) + count + DescribeStageElapsed(progress.StageEnteredUtc)
            + (string.IsNullOrWhiteSpace(running) ? "" : " - " + running);
      }
      return "No Charm cast yet - open F9, aim the fixed center crosshair at an allowed sign or player-built object, press ` to CHECK, then ` again to CAST.";
    } catch {
      return "unavailable";
    }
  }

  public string CurrentTitle() {
    return TryLoad(out var active, out _) ? active.Document?.Title : null;
  }

  public string CurrentObjective() {
    try {
      if (!TryLoad(out var active, out _)) return "Open Studio to choose your next quest.";
      var progress = DescribeProgress();
      if (progress == "Completed.") return "Hunt complete. Open DMos to review your result or replay the campaign.";
      if (progress == "Failed.") return "Hunt ended. Open DMos to review what happened and replay the campaign.";
      var stage = active.Document.Stages.FirstOrDefault(value => value.Id == CurrentStageId());
      var route = stage?.Transitions?.OrderByDescending(value => value.Priority).FirstOrDefault();
      foreach (var wear in Bindings(active)) {
        var zdo=wear?.GetComponent<ZNetView>()?.GetZDO();
        if(zdo==null||!TryActiveIdentity(zdo,active,out var identity,out _))continue;
        var state=workflows.Get(identity);
        var lastKill=state?.History?.LastOrDefault(evt=>evt.Name=="kill" && evt.Target==EventLeaf(route?.When)?.Target);
        var finishHelp=SignatureHuntFinishHelp(route,lastKill);
        if(finishHelp!=null)return finishHelp;
      }
      var leaf = EventLeaf(route?.When);
      if (leaf?.Event == "kill" && leaf.Where != null
          && leaf.Where.TryGetValue("weapon_skill", out var skill) && skill == "Spears"
          && leaf.Where.TryGetValue("projectile", out var projectile) && projectile == "true")
        return (string.IsNullOrWhiteSpace(stage.Instructions) ? "" : stage.Instructions.Trim() + "\n")
            + "Finish the " + (Localization.instance?.Localize(leaf.Target) ?? leaf.Target)
            + " with a thrown spear. Middle mouse to throw; replacement spears in the lodge chest.";
      return string.IsNullOrWhiteSpace(stage?.Instructions) ? progress : stage.Instructions;
    } catch { return "Quest status unavailable. Open creator details for diagnostics."; }
  }

  static string SignatureHuntFinishHelp(ExperienceTransition route, RuntimeEvent evt) {
    var leaf=EventLeaf(route?.When);
    if(evt?.Name!="kill" || leaf?.Event!="kill" || evt.Target!=leaf.Target || leaf.Where==null
        || !leaf.Where.TryGetValue("weapon_skill",out var expectedSkill) || expectedSkill!="Spears"
        || !leaf.Where.TryGetValue("projectile",out var expectedProjectile) || expectedProjectile!="true")return null;
    var spear=evt.Fields!=null && evt.Fields.TryGetValue("weapon_skill",out var skill) && skill=="Spears";
    var thrown=evt.Fields!=null && evt.Fields.TryGetValue("projectile",out var projectile) && projectile=="true";
    if(spear&&thrown)return null;
    var reason=spear&&!thrown?"That was a melee finish.":"The finishing hit was not a thrown spear.";
    return reason+" This hunt needs a thrown-spear finish. Replay the campaign in DMos for a fresh target.";
  }

  /// <summary>The authored ending in the player's words. "complete" and "fail" are the contract's
  /// closed outcome vocabulary; a row that prints the token is showing machinery.</summary>
  static string Outcome(string value) =>
      string.Equals(value, "complete", StringComparison.OrdinalIgnoreCase) ? "Completed."
      : string.Equals(value, "fail", StringComparison.OrdinalIgnoreCase) ? "Failed."
      : value;

  public string CurrentStageId() {
    try {
      if (!TryLoad(out var active, out _)) return null;
      foreach (var wear in Bindings(active)) {
        var zdo = wear?.GetComponent<ZNetView>()?.GetZDO();
        if (zdo == null) continue;
        var reference = Read(zdo);
        if (reference == null || reference.ContentHash != active.ContentHash) continue;
        if (!TryRunScope(zdo, active, out _)) return null;
        return !TryActiveIdentity(zdo, active, out var identity, out _)
            ? active.Document.EntryStage
            : workflows.Get(identity)?.StageId ?? active.Document.EntryStage;
      }
    } catch { }
    return null;
  }

  public RuntimeRunCoordinator RunCoordinator() => runs;

  /// <summary>Emit the one engine-owned lifecycle fact after a recoverable binding write. The
  /// exact ZDO source pins evaluation to the object the request named; ordinary experiences that
  /// do not subscribe to this fact remain bound and wait for their authored player event.</summary>
  public bool StartBoundExperience(string bindingZdo, out string error) {
    error = null;
    try {
      if (!TryLoad(out var active, out error)) return false;
      var parts = (bindingZdo ?? string.Empty).Split(':');
      if (parts.Length != 2 || !long.TryParse(parts[0], out var user)
          || !uint.TryParse(parts[1], out var objectId)) { error = "binding_identity_invalid"; return false; }
      var zdo = ZDOMan.instance?.GetZDO(new ZDOID(user, objectId));
      var view = zdo == null ? null : ZNetScene.instance?.FindInstance(zdo);
      var reference = zdo == null ? null : Read(zdo);
      if (zdo == null || !zdo.Persistent || view == null || !view.IsOwner() || reference == null
          || reference.PackId != active.PackId || reference.ExperienceId != active.Document.Id
          || reference.Version != active.Version
          || !string.Equals(reference.ContentHash, active.ContentHash, StringComparison.OrdinalIgnoreCase)) {
        error = "binding_start_scope_mismatch";
        return false;
      }
      InvalidateBindingSelection();
      if (!Bindings(active).Any(wear => string.Equals(
              wear?.GetComponent<ZNetView>()?.GetZDO()?.m_uid.ToString(), bindingZdo,
              StringComparison.Ordinal))) {
        error = "binding_start_selection_mismatch";
        return false;
      }
      // Binding is an explicit run boundary even when the authored graph does not consume an
      // experience_started event. Workflow state, rather than merely an active run record, is
      // the durable proof that a subscribing experience received its one lifecycle fact.
      var identity = ResolveIdentity(zdo, active);
      if (identity == null) {
        error = "binding_start_identity_unavailable";
        return false;
      }
      return EnsureStarted(active, zdo, identity, out error);
    } catch (Exception e) { error = "experience_start_failed:" + e.GetType().Name; return false; }
  }

  /// <summary>Finish the lifecycle stage of a reset transaction for one exact successor. The
  /// durable binding-instance selection resolves today's physical ZDO after a cold load; the
  /// expected run id prevents a retry from starting a newer run in the same scope.</summary>
  public bool EnsureRunStarted(string expectedRunId, out string error) {
    error = null;
    try {
      if (string.IsNullOrWhiteSpace(expectedRunId)) {
        error = "experience_start_run_invalid";
        return false;
      }
      if (!TryLoad(out var active, out error)) return false;
      var expected = runs.Registry.Find(expectedRunId);
      if (expected == null || expected.Status != "active") {
        error = "experience_start_run_missing";
        return false;
      }
      InvalidateBindingSelection();
      foreach (var wear in Bindings(active)) {
        var zdo = wear?.GetComponent<ZNetView>()?.GetZDO();
        if (zdo == null) continue;
        var reference = Read(zdo);
        if (reference == null || reference.ContentHash != active.ContentHash) continue;
        if (!TryActiveIdentity(zdo, active, out var identity, out var current)) continue;
        if (!string.Equals(current.RunId, expectedRunId, StringComparison.Ordinal)) {
          error = "experience_start_run_mismatch";
          return false;
        }
        return EnsureStarted(active, zdo, identity, out error);
      }
      error = "binding_start_selection_mismatch";
      return false;
    } catch (Exception e) {
      error = "experience_start_failed:" + e.GetType().Name;
      return false;
    }
  }

  bool EnsureStarted(Active active, ZDO zdo, WorkflowIdentity identity, out string error) {
    error = null;
    if (active?.Document == null || zdo == null || identity == null
        || !runs.Registry.IsActive(identity)) {
      error = "binding_start_identity_unavailable";
      return false;
    }
    if (workflows.Get(identity) != null) return true;
    var entry = active.Document.Stages.FirstOrDefault(stage => stage.Id == active.Document.EntryStage);
    if (entry == null) { error = "experience_entry_stage_missing"; return false; }
    var correlationId = NewCorrelationId();
    foreach (var action in entry.EntryActions ?? new()) {
      if (!Execute(active, zdo, entry.Id, "entry", action, correlationId, false)) {
        error = "experience_entry_action_failed";
        return false;
      }
    }
    var started = new RuntimeEvent {
      Name = ExperienceSchema.ExperienceStartedEvent,
      SourceId = zdo.m_uid.ToString(),
      At = DateTimeOffset.UtcNow,
    };
    RuntimeObservation.StampLocalPlayer(started);
    if (active.Subscriptions.Contains(ExperienceSchema.ExperienceStartedEvent)) OnEvent(started, true);
    else workflows.Begin(identity, active.Document, started);
    if (workflows.Get(identity) != null) return true;
    error = "experience_start_not_observed";
    return false;
  }

  public void ClearRunEphemera(string stateKey) {
    if(string.IsNullOrWhiteSpace(stateKey))return;
    rechecks.Remove(stateKey);
    foreach(var key in spawnRecoveries.Where(value=>value.Value.Identity?.Key==stateKey)
        .Select(value=>value.Key).ToArray())spawnRecoveries.Remove(key);
    foreach(var key in pendingReplays.Where(value=>value.Value.Identity?.Key==stateKey)
        .Select(value=>value.Key).ToArray())pendingReplays.Remove(key);
  }

  public IReadOnlyList<RuntimeRunStatusEntry> CurrentRuns() {
    var values=new List<RuntimeRunStatusEntry>();
    if(!TryLoad(out var active,out _))return values;
    foreach(var wear in Bindings(active)) {
      var zdo=wear?.GetComponent<ZNetView>()?.GetZDO();
      if(zdo==null)continue;
      var reference=Read(zdo);
      if(reference==null||reference.ContentHash!=active.ContentHash)continue;
      if(!TryActiveIdentity(zdo,active,out var identity,out var record))continue;
      var progress=workflows.Get(identity);
      values.Add(new RuntimeRunStatusEntry{BindingAvailable=true,RunId=record.RunId,ScopeId=record.ScopeId,ExperienceId=active.Document.Id,BindingZdo=zdo.m_uid.ToString(),BindingInstanceId=identity.BindingInstanceId,ParticipantIds=record.Scope.ParticipantIds.ToArray(),ContentHash=identity.ContentHash,StageId=progress?.StageId??active.Document.EntryStage,Outcome=progress?.Outcome,RewardPolicy=record.RewardPolicy});
      if(values.Count>=64)break;
    }
    // A completed predecessor or lost anchor must remain addressable for inspection and
    // retirement. Physical availability is separate from the durable run's existence.
    var world=ZNet.instance?.GetWorldUID().ToString();
    var participant=Player.m_localPlayer?.GetPlayerID().ToString();
    foreach(var record in runs.Registry.List().Where(record=>record.Status=="active"
        && record.Scope.WorldId==world && record.Scope.ParticipantIds.Contains(participant)
        && !values.Any(value=>value.RunId==record.RunId)).Take(Math.Max(0,64-values.Count))){
      var progress=workflows.Get(record.Identity());
      values.Add(new RuntimeRunStatusEntry{BindingAvailable=false,RunId=record.RunId,
        ScopeId=record.ScopeId,ExperienceId=record.Scope.ExperienceId,BindingZdo=record.Scope.BindingZdo,
        BindingInstanceId=record.Scope.BindingInstanceId,ParticipantIds=record.Scope.ParticipantIds.ToArray(),
        ContentHash=record.Scope.ContentHash,StageId=progress?.StageId,Outcome=progress?.Outcome??record.Outcome,
        RewardPolicy=record.RewardPolicy});
    }
    return values;
  }

  static string Describe(TriggerExpression trigger) {
    var leaf = EventLeaf(trigger);
    if (leaf != null
        && CreatorSignalCatalog.TryDescribe(leaf.Event, leaf.Target, out var signal))
      return signal.Instruction + ThresholdSuffix(trigger) + SpatialSuffix(trigger);
    return (leaf?.Event ?? "perform the current beat").Replace('_', ' ')
        + ThresholdSuffix(trigger) + SpatialSuffix(trigger);
  }

  static TriggerExpression EventLeaf(TriggerExpression trigger) {
    if (trigger == null) return null;
    if (string.Equals(trigger.Op, "EVENT", StringComparison.OrdinalIgnoreCase)) return trigger;
    foreach (var child in trigger.Children ?? new List<TriggerExpression>()) {
      var found = EventLeaf(child);
      if (found != null) return found;
    }
    return null;
  }

  static string ThresholdSuffix(TriggerExpression trigger) {
    var thresholds = Thresholds(trigger).Select(value => {
      var amount = value.Value.GetValueOrDefault();
      AdaptiveMeasureCatalog.TryGet(value.Measure, out var measure);
      var unit = measure == null ? "" : " " + measure.UnitFor(amount);
      if (value.Measure == "time_since_stage_entered") return "after " + amount + "s in this stage";
      if (value.Measure == "time_since_progress") return "after " + amount + "s without quest progress";
      if (value.Measure == AdaptiveMeasureCatalog.DeathsMeasure) return "after " + amount + unit;
      if (value.Measure == AdaptiveMeasureCatalog.ClearedMeasure) return "after clearing " + amount + unit;
      if (value.Measure == AdaptiveMeasureCatalog.RemainingMeasure)
        return "with " + amount + " or more still standing";
      return "when " + (measure?.Label ?? value.Measure) + " reaches " + amount;
    }).ToArray();
    return thresholds.Length == 0 ? "" : " " + string.Join(" and ", thresholds);
  }

  static IEnumerable<TriggerExpression> Thresholds(TriggerExpression trigger) {
    if (trigger == null) yield break;
    if (string.Equals(trigger.Op, "THRESHOLD", StringComparison.OrdinalIgnoreCase)) yield return trigger;
    foreach (var child in trigger.Children ?? new List<TriggerExpression>())
      foreach (var found in Thresholds(child)) yield return found;
  }

  static string SpatialSuffix(TriggerExpression trigger) {
    var predicates = Spatials(trigger).Select(value => {
      var label = value.AreaId ?? "the area";
      if (value.Spatial == "within_radius") return "while inside area " + label;
      if (value.Spatial == "entered") return "after entering area " + label;
      if (value.Spatial == "left") return "after leaving area " + label;
      if (value.Spatial == "remained")
        return "after " + value.Value.GetValueOrDefault() + "s in area " + label;
      return "with " + value.Value.GetValueOrDefault() + " objects inside area " + label;
    }).ToArray();
    return predicates.Length == 0 ? "" : " " + string.Join(" and ", predicates);
  }

  static IEnumerable<TriggerExpression> Spatials(TriggerExpression trigger) {
    if (trigger == null) yield break;
    if (string.Equals(trigger.Op, "SPATIAL", StringComparison.OrdinalIgnoreCase)) yield return trigger;
    foreach (var child in trigger.Children ?? new List<TriggerExpression>())
      foreach (var found in Spatials(child)) yield return found;
  }

  static string DescribeStageElapsed(DateTimeOffset? entered) {
    if (!entered.HasValue) return "";
    var seconds = Math.Max(
        0L, (long)(DateTimeOffset.UtcNow - entered.Value).TotalSeconds);
    return " - in stage " + seconds / 60 + "m " + seconds % 60 + "s";
  }

  bool TryLoad(out Active active, out string error) {
    active = null;
    error = "active_set_missing";
    try {
      var activePath = Path.Combine(root, "active", "active-set.json");
      if (!File.Exists(activePath)) { InvalidateActive(); return false; }
      var activeWrite = File.GetLastWriteTimeUtc(activePath);
      if (cachedActive != null && activeWrite == cachedActiveWriteUtc
          && File.Exists(cachedActive.PackagePath)) {
        var currentPackage = new FileInfo(cachedActive.PackagePath);
        if (currentPackage.LastWriteTimeUtc == cachedPackageWriteUtc
            && currentPackage.Length == cachedPackageLength) {
          active = cachedActive;
          error = null;
          return true;
        }
      }
      var set = JsonConvert.DeserializeObject<ActiveSet>(File.ReadAllText(activePath));
      if (set == null || set.Source != Path.GetFileName(set.Source)) {
        InvalidateActive();
        error = "active_source_invalid";
        return false;
      }
      var dev = string.Equals(set.SourceChannel, "dev", StringComparison.OrdinalIgnoreCase);
      var package = Path.Combine(root, dev ? "inbox-dev" : "inbox", set.Source);
      var store = new QuestPackStore(root);
      var inspected = dev ? store.InspectDev(package) : store.Inspect(package);
      if (!inspected.IsValid
          || inspected.ContentHash != set.ContentHash
          || inspected.Manifest.PackId != set.PackId
          || inspected.Manifest.Version != set.Version) {
        InvalidateActive();
        error = "active_content_mismatch";
        return false;
      }
      using var zip = ZipFile.OpenRead(package);
      // A guild ships as one pack holding several experiences. Which of them this Runtime is
      // running is the active set's selector, resolved in one place so this site and the charm
      // binding cannot disagree about what "active" means.
      if (!ActiveExperienceResolver.TryResolve(zip, set.ExperienceId, out var chosen, out var selection)) {
        InvalidateActive();
        error = selection;
        return false;
      }
      var compiled = ExperienceCompiler.CompileProductionJson(chosen.Json);
      if (!compiled.IsValid) {
        InvalidateActive();
        error = "active_experience_invalid";
        return false;
      }
      if(!ActiveExperienceResolver.TryResolveAll(zip,out var available,out selection)) {
        InvalidateActive();
        error=selection;
        return false;
      }
      var documents=new Dictionary<string,ExperienceDocument>(StringComparer.Ordinal);
      foreach(var candidate in available) {
        var member=ExperienceCompiler.CompileProductionJson(candidate.Json);
        if(!member.IsValid||member.Document==null) {
          InvalidateActive();
          error="active_experience_invalid";
          return false;
        }
        documents[member.Document.Id]=member.Document;
      }
      var packageInfo = new FileInfo(package);
      var previousContentHash = cachedActive?.ContentHash;
      cachedActive = new Active {
        PackId = set.PackId,
        Version = set.Version,
        ContentHash = set.ContentHash,
        ActivationId = set.ActivationId,
        Set = set,
        Document = compiled.Document,
        Documents = documents,
        Subscriptions = RuntimeSubscriptionIndex.Create(compiled.Document),
        PackagePath = package,
      };
      cachedActiveWriteUtc = activeWrite;
      cachedPackageWriteUtc = packageInfo.LastWriteTimeUtc;
      cachedPackageLength = packageInfo.Length;
      active = cachedActive;
      error = null;
      if (!string.IsNullOrWhiteSpace(previousContentHash)
          && !string.Equals(previousContentHash, cachedActive.ContentHash,
              StringComparison.OrdinalIgnoreCase))
        ReportOrphanedBindings(cachedActive);
      else lock (evidenceGate) lastOrphanCount = 0;
      return true;
    } catch {
      InvalidateActive();
      error = "active_set_unreadable";
      return false;
    }
  }

  void InvalidateActive() {
    cachedActive = null;
    InvalidateBindingSelection();
    recentEventKeys.Clear();
    rechecks.Clear();
    lock (evidenceGate) {
      countedKey = null;
      countedCurrent = 0;
      unboundReported = null;
      activeAlerts.Clear();
      activeAlertOrder.Clear();
      recentEvidence.RemoveAll(value => !string.IsNullOrWhiteSpace(value.Key));
    }
  }

  void InvalidateBindingCache() {
    cachedBindings = Array.Empty<WearNTear>();
    cachedBindingContentHash = null;
    cachedBindingResolutionKey = null;
    nextBindingRefresh = 0;
  }

  /// <summary>A binding writer calls this after changing either the journal, durable marker, or
  /// physical reference fields. It invalidates observation only; it never resolves or creates a run.</summary>
  internal void InvalidateBindingSelection() {
    InvalidateBindingCache();
    cachedBindingSelectionKey = null;
    cachedBindingSelection = null;
    nextBindingSelectionRefresh = 0;
  }

  void ReportOrphanedBindings(Active active) {
    try {
      var count = 0;
      foreach (var wear in WearNTear.GetAllInstances()) {
        var zdo = wear?.GetComponent<ZNetView>()?.GetZDO();
        if (zdo == null) continue;
        var reference = Read(zdo);
        if (reference != null
            && !string.Equals(reference.ContentHash, active.ContentHash,
                StringComparison.OrdinalIgnoreCase)) count++;
      }
      lock (evidenceGate) lastOrphanCount = count;
      if (count == 0) { ResolveAlert("binding_version"); return; }
      WriteReceipt(new RuntimeReceipt {
        Operation = "activation",
        Status = "orphaned_bindings",
        PackId = active.PackId,
        Version = active.Version,
        ContentHash = active.ContentHash,
        ActivationId = active.ActivationId,
        CandidateCount = count,
        Diagnostics = Array.Empty<ContractDiagnostic>(),
      }, count + " bindings now OTHER VERSION — re-CAST or roll back",
          CreatorEvidenceKind.Warning, "binding_version");
    } catch {
      // Loaded-scene diagnostics must not make otherwise valid active content unusable.
    }
  }

  PolicyDecision PersonalDecision(Active active) =>
      DedicatedPersonalProgressionPolicy.CanUse(
          personalProfile(), World(), CurrentWorldUid(), active?.ContentHash,
          active?.Documents?.Values);

  PolicyDecision PersonalEventDecision(
      Active active, RuntimeEvent runtimeEvent, bool locallyWitnessed) =>
      DedicatedPersonalProgressionPolicy.CanAcceptEvent(
          personalProfile(), World(), CurrentWorldUid(), active?.ContentHash,
          active?.Documents?.Values, runtimeEvent, locallyWitnessed);

  static bool ReferenceMatches(CharmReference reference, Active active, bool personal) =>
      reference != null && active?.Document != null
      && reference.PackId == active.PackId
      && reference.BindingId == "default"
      && reference.Version == active.Version
      && string.Equals(reference.ContentHash, active.ContentHash,
          StringComparison.OrdinalIgnoreCase)
      && (personal
          ? active.Documents?.ContainsKey(reference.ExperienceId) == true
          : reference.ExperienceId == active.Document.Id);

  static string CurrentWorldUid() {
    try {
      var value = ZNet.instance?.GetWorldUID() ?? 0L;
      return value == 0L ? null : value.ToString();
    } catch { return null; }
  }

  WorldAuthority World() {
    try {
      var dedicated = ZNet.instance != null && ZNet.instance.IsDedicated();
      var host = ZNet.instance != null && ZNet.instance.IsServer() && !dedicated;
      return new WorldAuthority {
        IsPrivateWorld = privateConfirmed(),
        IsSolo = host,
        IsListenHost = host,
        IsDedicated = dedicated,
        IsPeerClient = ZNet.instance != null && !ZNet.instance.IsServer(),
      };
    } catch {
      return new WorldAuthority { IsPrivateWorld = privateConfirmed() };
    }
  }

  void WriteReceipt(RuntimeReceipt receipt, string evidenceLine = null,
      CreatorEvidenceKind kind = CreatorEvidenceKind.Plumbing, string key = null) {
    receipt.EvidenceKind = CreatorEvidenceLine.KindName(kind);
    receipts.Write(receipt);
    if (string.IsNullOrWhiteSpace(evidenceLine)) return;
    if (evidenceLine.Length > 220) evidenceLine = evidenceLine.Substring(0, 219) + "…";
    var line = new CreatorEvidenceLine {
      Kind = kind,
      Key = key,
      Stamp = receipt.AtUtc.ToLocalTime().ToString("HH:mm:ss"),
      Text = evidenceLine,
    };
    lock (evidenceGate) {
      if (!string.IsNullOrWhiteSpace(key)) {
        activeAlerts[key] = line;
        activeAlertOrder.RemoveAll(value => string.Equals(value, key, StringComparison.Ordinal));
        activeAlertOrder.Add(key);
      }
      recentEvidence.Add(line);
      if (recentEvidence.Count > MaxRecentEvidence)
        recentEvidence.RemoveRange(0, recentEvidence.Count - MaxRecentEvidence);
    }
  }

  void Write(
      string operation,
      string error,
      Active active,
      string status,
      string detail,
      string correlationId) =>
      WriteReceipt(new RuntimeReceipt {
        Operation = operation,
        Status = status ?? "rejected",
        Error = detail == null ? error : error + ":" + detail,
        PackId = active?.PackId,
        Version = active?.Version,
        ContentHash = active?.ContentHash,
        ActivationId = active?.ActivationId,
        CorrelationId = correlationId,
        Diagnostics = Array.Empty<ContractDiagnostic>(),
      });

  sealed class Active {
    public string PackId;
    public string Version;
    public string ContentHash;
    public string ActivationId;
    public ActiveSet Set;
    public ExperienceDocument Document;
    public IReadOnlyDictionary<string, ExperienceDocument> Documents;
    public RuntimeSubscriptionIndex Subscriptions;
    public string PackagePath;
  }
}

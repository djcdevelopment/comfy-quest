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
  readonly string root;
  readonly RuntimeReceiptStore receipts;
  readonly ActionExecutionLedger ledger;
  readonly SpawnExecutionStore spawned;
  readonly WorkflowStateStore workflows;
  readonly DurableTimerStore timers;
  readonly Func<bool> privateConfirmed;
  readonly Dictionary<string, DateTimeOffset> recentEventKeys =
      new(StringComparer.Ordinal);
  /// <summary>Bindings owed a bounded re-read, and how many ticks each is still owed (ADR 0006).</summary>
  readonly Dictionary<string, int> rechecks = new(StringComparer.Ordinal);
  readonly object evidenceGate = new();
  readonly List<CreatorEvidenceLine> recentEvidence = new();
  string deadlineLine;
  bool deadlineUrgent;
  string deadlineError;
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
  double nextBindingRefresh;

  public RuntimeExperienceEngine(
      string runtimeRoot,
      RuntimeReceiptStore receiptStore,
      Func<bool> isPrivateConfirmed) {
    root = runtimeRoot;
    receipts = receiptStore;
    ledger = new ActionExecutionLedger(root);
    spawned = new SpawnExecutionStore(root);
    workflows = new WorkflowStateStore(root);
    timers = new DurableTimerStore(root);
    privateConfirmed = isPrivateConfirmed;
  }

  public void OnEasyEvent(RuntimeEvent evt) {
    if (evt != null) OnEvent(evt);
  }

  public IReadOnlyList<string> RecentEvidence() {
    lock (evidenceGate) return recentEvidence.Select(value => value.Text).ToArray();
  }

  /// <summary>The same bounded evidence with each row's kind — a fact tagged where the
  /// line was composed, so the drawer never classifies by parsing rendered copy.</summary>
  public IReadOnlyList<CreatorEvidenceLine> RecentEvidenceLines() {
    lock (evidenceGate) return recentEvidence.ToArray();
  }

  public void Tick() {
    var now = DateTimeOffset.UtcNow;
    if (now < nextTimerPoll) return;
    nextTimerPoll = now.AddSeconds(1);
    foreach (var timer in timers.Due(now)) {
      var elapsed = new RuntimeEvent {
        Name = ExperienceSchema.TimerElapsedEvent,
        SourceId = timer.Identity.BindingZdo,
        At = now,
        Fields = new Dictionary<string, string> { ["timer_id"] = timer.TimerId },
      };
      RuntimeObservation.StampLocalPlayer(elapsed);
      OnEvent(elapsed);
      timers.Acknowledge(timer.Key);
    }
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
        var identity = Identity(zdo, active);
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
        NewRecheckId(), trace));
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
    AuthoredAnchors = SpatialEvaluator.AnchorMap(document),
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


  public void OnEvent(RuntimeEvent evt) {
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
      if (!authority.Allowed) {
        Write("transition", authority.Diagnostic, active, null, null, correlationId);
        return;
      }

      var foundBinding = false;
      foreach (var wear in Bindings(active)) {
        if (wear == null) continue;
        var view = wear.GetComponent<ZNetView>();
        var zdo = view == null ? null : view.GetZDO();
        if (zdo == null || !view.IsOwner()
            || (!string.IsNullOrWhiteSpace(evt.SourceId)
                && evt.SourceId != zdo.m_uid.ToString())) continue;
        var reference = Read(zdo);
        if (reference == null
            || reference.PackId != active.PackId
            || reference.ExperienceId != active.Document.Id
            || reference.Version != active.Version
            || reference.ContentHash != active.ContentHash) continue;

        foundBinding = true;
        var identity = Identity(zdo, active);
        var observed = RuntimeObservation.Facts(
            zdo, spawned, identity.Key, active.ContentHash);
        var before = workflows.Get(identity);
        var evaluationContext = new TriggerEvaluationContext {
          At = evt.At,
          StageEnteredUtc = before?.StageEnteredUtc ?? (before == null ? evt.At : (DateTimeOffset?)null),
          LastProgressUtc = before?.LastProgressUtc ?? (before == null ? evt.At : (DateTimeOffset?)null),
          BindingPosition = observed.Spatial.BindingPosition,
          SpawnedPositions = observed.Spatial.SpawnedPositions,
          AuthoredAnchors = SpatialEvaluator.AnchorMap(active.Document),
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
          WriteReceipt(EventReceipt(
              "ignored", active, zdo.m_uid.ToString(), evt, state?.StageId,
              state?.StageId, progress, correlationId, trace,
              UnmetRoutes(stage, null, state?.History, evaluationContext)),
              line, CreatorEvidenceKind.Story);
          if (Rechecks(stage)) Arm(identity.Key);
          continue;
        }
        rechecks.Remove(identity.Key);
        Apply(active, zdo, decision, evt, correlationId);
      }

      if (!foundBinding) {
        WriteReceipt(EventReceipt("unbound", active, null, evt, null, null,
            new TriggerProgress { Current = 0, Required = 1 }, correlationId),
            UnboundLine(active), CreatorEvidenceKind.Warning);
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
      Active active, ZDO zdo, WorkflowDecision decision, RuntimeEvent cause, string correlationId) {
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
        evidence, rejectedEvidence),
        MatchedLine(currentStage.Id, decision.Transition, matchedProgress, rejectedEvidence),
        CreatorEvidenceKind.Story);

    var succeeded = true;
    foreach (var action in decision.Transition.Actions ?? new())
      succeeded &= Execute(
          active, zdo, currentStage.Id, decision.Transition.Id, action, correlationId);
    if (succeeded && !string.IsNullOrWhiteSpace(decision.Transition.NextStage)) {
      var next = active.Document.Stages.FirstOrDefault(
          value => value.Id == decision.Transition.NextStage);
      foreach (var action in next?.EntryActions ?? new())
        succeeded &= Execute(active, zdo, next.Id, "entry", action, correlationId);
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
        ActivationId = active.ActivationId,
        CorrelationId = correlationId,
        BindingZdo = zdo.m_uid.ToString(),
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
    }
  }

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
        + " has no Charm yet — aim the fixed center crosshair at an allowed sign or player-built object, press ` to CHECK, then ` again to CAST.";
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
    if (!string.Equals(cachedBindingContentHash, active.ContentHash, StringComparison.Ordinal)
        || now >= nextBindingRefresh) {
      cachedBindings = WearNTear.GetAllInstances().Where(value => value != null).ToArray();
      cachedBindingContentHash = active.ContentHash;
      nextBindingRefresh = now + 1.0;
    }
    return cachedBindings;
  }

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
      IReadOnlyList<RejectedTransitionEvidence> rejectedEvidence = null) => new() {
    Operation = "event",
    Status = status,
    PackId = active.PackId,
    Version = active.Version,
    ContentHash = active.ContentHash,
    ActivationId = active.ActivationId,
    CorrelationId = correlationId,
    BindingZdo = bindingZdo,
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
        var soonest = timers.Pending(now, 1).FirstOrDefault();
        if (soonest != null) {
          seconds = (int)Math.Ceiling((soonest.DueUtc - now).TotalSeconds);
          line = Countdown(seconds, null);
        } else {
          foreach (var wear in Bindings(active)) {
            var zdo = wear?.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) continue;
            var reference = Read(zdo);
            if (reference == null || reference.ContentHash != active.ContentHash) continue;
            var progress = workflows.Get(Identity(zdo, active));
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
      string correlationId) {
    var zdoId = zdo.m_uid.ToString();
    try {
      var identity = Identity(zdo, active);
      var key = string.Join("|", identity.Key, stage, transition, action.Id);
      if (!ledger.TryClaim(key)) {
        WriteReceipt(ActionReceipt(
            "suppressed", "duplicate_suppressed", active, zdoId, stage,
            transition, action.Id, correlationId), ActionLine(action, "duplicate suppressed"));
        return true;
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
          if (!Grant(action)) throw new InvalidOperationException("grant_failed");
          break;
        case "spawn":
          if (!Spawn(active, zdo, action, key))
            throw new InvalidOperationException("spawn_failed");
          break;
        case "clear_spawned":
          Clear(active, Param(action, "action_id"), identity, stage, transition);
          break;
        default:
          throw new InvalidOperationException("action_not_implemented");
      }
      WriteReceipt(ActionReceipt(
          "executed", null, active, zdoId, stage, transition, action.Id,
          correlationId), ActionLine(action, "executed"));
      return true;
    } catch (Exception e) {
      WriteReceipt(ActionReceipt(
          "rejected", e.Message, active, zdoId, stage, transition, action?.Id,
          correlationId), ActionLine(action, "failed: " + e.Message), CreatorEvidenceKind.Warning);
      return false;
    }
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

  static bool Grant(ExperienceAction action) {
    var item = Param(action, "item");
    var count = IntParam(action, "quantity");
    if (!MutationRegistry.TryGrant(item, count, out _)) return false;
    var prefab = ZNetScene.instance?.GetPrefab(item);
    if (prefab == null || prefab.GetComponent<ItemDrop>() == null
        || Player.m_localPlayer == null) return false;
    return Player.m_localPlayer.GetInventory().AddItem(prefab, count);
  }

  bool Spawn(Active active, ZDO binding, ExperienceAction action, string key) {
    var kind = Param(action, "kind");
    var prefabName = Param(action, "prefab");
    var count = IntParam(action, "count");
    var radius = IntParam(action, "radius");
    if (!MutationRegistry.CanSpawn(kind, prefabName)
        || count < 1 || count > 16 || radius < 0 || radius > 30) return false;
    var prefab = ZNetScene.instance?.GetPrefab(prefabName);
    if (prefab == null || !PrefabMatches(prefab, kind)) return false;
    var center = binding.GetPosition();
    for (var i = 0; i < count; i++) {
      var angle = (float)(i * Math.PI * 2 / Math.Max(1, count));
      var distance = Math.Min(radius, 2 + i / 4);
      var position = center + new UnityEngine.Vector3(
          (float)Math.Cos(angle) * distance, 0.5f, (float)Math.Sin(angle) * distance);
      var go = UnityEngine.Object.Instantiate(
          prefab, position, UnityEngine.Quaternion.identity);
      var view = go?.GetComponent<ZNetView>();
      var zdo = view?.GetZDO();
      if (zdo == null) return false;
      zdo.Set(Prefix + "spawnedContentHash", active.ContentHash);
      zdo.Set(Prefix + "spawnedActionId", action.Id);
      zdo.Set(Prefix + "spawnedActionKey", key);
      var piece = go.GetComponent<Piece>();
      if (piece != null && Player.m_localPlayer != null)
        piece.SetCreator(Player.m_localPlayer.GetPlayerID());
      spawned.Record(new SpawnedObject {
        ActionKey = key,
        ContentHash = active.ContentHash,
        ActionId = action.Id,
        UserId = zdo.m_uid.UserID,
        ObjectId = zdo.m_uid.ID,
      });
    }
    return true;
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
      var zdo = ZDOMan.instance?.GetZDO(new ZDOID(record.UserId, record.ObjectId));
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
      string correlationId) => new() {
    Operation = "action",
    Status = status,
    Error = error,
    PackId = active.PackId,
    Version = active.Version,
    ContentHash = active.ContentHash,
    ActivationId = active.ActivationId,
    CorrelationId = correlationId,
    BindingZdo = zdo,
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

  WorkflowIdentity Identity(ZDO zdo, Active active) => new() {
    WorldId = ZNet.instance.GetWorldUID().ToString(),
    CharacterId = Player.m_localPlayer == null
        ? "0" : Player.m_localPlayer.GetPlayerID().ToString(),
    BindingZdo = zdo.m_uid.ToString(),
    ContentHash = active.ContentHash,
  };

  /// <summary>What the running telling is doing right now, without re-announcing its name: the status
  /// card owns the title, and session 2 read the same title stacked in three places as a loss of
  /// section boundaries. An ending is reported as the player's outcome, never the raw token.</summary>
  public string DescribeProgress() {
    try {
      if (!TryLoad(out var active, out _)) return "not ready";
      foreach (var wear in WearNTear.GetAllInstances()) {
        var zdo = wear?.GetComponent<ZNetView>()?.GetZDO();
        if (zdo == null) continue;
        var reference = Read(zdo);
        if (reference == null || reference.ContentHash != active.ContentHash) continue;
        var identity = Identity(zdo, active);
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
          AuthoredAnchors = SpatialEvaluator.AnchorMap(active.Document),
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
      return "No Charm cast yet - aim the fixed center crosshair at an allowed sign or player-built object, press ` to CHECK, then ` again to CAST.";
    } catch {
      return "unavailable";
    }
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
      foreach (var wear in WearNTear.GetAllInstances()) {
        var zdo = wear?.GetComponent<ZNetView>()?.GetZDO();
        if (zdo == null) continue;
        var reference = Read(zdo);
        if (reference == null || reference.ContentHash != active.ContentHash) continue;
        return workflows.Get(Identity(zdo, active))?.StageId ?? active.Document.EntryStage;
      }
    } catch { }
    return null;
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
      var label = SpatialEvaluator.Label(value.Anchor);
      var radius = value.Radius.GetValueOrDefault();
      if (value.Spatial == "within_radius") return "while within " + radius + " m of " + label;
      if (value.Spatial == "entered") return "after entering the area " + radius + " m around " + label;
      if (value.Spatial == "left") return "after leaving the area " + radius + " m around " + label;
      if (value.Spatial == "remained")
        return "after " + value.Value.GetValueOrDefault() + "s in the area " + radius + " m around " + label;
      return "with " + value.Value.GetValueOrDefault() + " objects within " + radius + " m of " + label;
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
      var entries = zip.Entries.Where(value =>
          value.FullName.StartsWith("experiences/", StringComparison.Ordinal)
          && value.FullName.EndsWith(".json", StringComparison.Ordinal)).ToArray();
      if (entries.Length != 1) {
        InvalidateActive();
        error = "active_experience_ambiguous";
        return false;
      }
      using var reader = new StreamReader(entries[0].Open());
      var compiled = ExperienceCompiler.CompileProductionJson(reader.ReadToEnd());
      if (!compiled.IsValid) {
        InvalidateActive();
        error = "active_experience_invalid";
        return false;
      }
      var packageInfo = new FileInfo(package);
      var previousContentHash = cachedActive?.ContentHash;
      cachedActive = new Active {
        PackId = set.PackId,
        Version = set.Version,
        ContentHash = set.ContentHash,
        ActivationId = set.ActivationId,
        Document = compiled.Document,
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
    cachedBindings = Array.Empty<WearNTear>();
    cachedBindingContentHash = null;
    recentEventKeys.Clear();
    rechecks.Clear();
    lock (evidenceGate) { countedKey = null; countedCurrent = 0; unboundReported = null; }
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
          CreatorEvidenceKind.Warning);
    } catch {
      // Loaded-scene diagnostics must not make otherwise valid active content unusable.
    }
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
      CreatorEvidenceKind kind = CreatorEvidenceKind.Plumbing) {
    receipt.EvidenceKind = CreatorEvidenceLine.KindName(kind);
    receipts.Write(receipt);
    if (string.IsNullOrWhiteSpace(evidenceLine)) return;
    if (evidenceLine.Length > 220) evidenceLine = evidenceLine.Substring(0, 219) + "…";
    var line = new CreatorEvidenceLine {
      Kind = kind,
      Stamp = receipt.AtUtc.ToLocalTime().ToString("HH:mm:ss"),
      Text = evidenceLine,
    };
    lock (evidenceGate) {
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
    public ExperienceDocument Document;
    public RuntimeSubscriptionIndex Subscriptions;
    public string PackagePath;
  }
}

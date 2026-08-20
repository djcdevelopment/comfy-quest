# Phase 3 close strategy — from session 2's findings to the exit verdicts

Phase 3 exits when the three defense questions (countdown tension, escalation,
mercy) get seat answers and no "can't answer why" survives unexplained. Session 2
(`five-intent-validation-lap-backlog.md`, "Phase 3 exit — session 2") left two
ledger rows open and a set of seat directions. This document sequences the work.
Root causes below are researched, not conjectured — evidence lives in the run
capture (`captures/five-intent-slice1/phase3-exit-2026-08-20-s2/`) and the ADRs.

**Status, 2026-08-20: W1, W2, W3 and W4 have landed.** What remains is session 3
and the W5 packet. Two things changed after this document was first written, and
both are recorded in place below: reading the run's receipts turned W1's root
cause from a ranked hypothesis into a proof, and disproved one W3 item outright.

## W1 — The kill tally races destruction (exit blocker) — **landed**

**Root cause.** Spawn records were perfect (8 rows, `action_id: wave`, real ZDO
ids under the right owner). Kill events carry no ZDO correlation *by design* —
the tally is a live `ZDOMan.GetZDO` poll per spawned record
(`RuntimeObservation.Facts`). But `kill` is emitted by a Harmony postfix inside
`Character.OnDeath()` and evaluated synchronously in that call stack, before the
dying creature's ZDO leaves `ZDOMan` — so the eighth kill read "cleared ≤ 7", and
with no idle re-evaluation anywhere in the engine, the wrong answer stood for nine
minutes. (`LabGalleryBuilder.DestroySettleTimeout = 5f` is the repo's own prior
proof that destroys don't settle same-frame.)

**The run proves this against itself.** At 12:17:17 the `timer_elapsed` event
re-evaluated the same `hold` stage and the route that matched was `held` — the
victory route, priority 40, outranking `overrun` — because the corpses were gone by
then, the tally read 8 cleared, and the eight kills were still in history. The
receipts read `event/matched` on `timer_elapsed`, the victory message, the reward,
`transition/complete` on `held`. The tally was never broken; it was stale at the
only instants anything read it, and nothing read it twice.

**Fix (ADR 0006), two parts:**
1. **Bounded recheck** (`RuntimeExperienceEngine`): when a subscribed event
   leaves a THRESHOLD-gated route unmet, mark the binding for re-evaluation and
   rerun the same observation pass + route evaluation a bounded number of times
   (a few ticks) on the existing 1s `Tick` cadence; the bound expires and the
   stage returns to purely event-driven evaluation. No new fact semantics, no
   fabricated events.
2. **Ignored receipts explain themselves**: the `"ignored"` `EventReceipt` call
   currently drops evidence — the `0/1` on session 2's receipts was the top-level
   ALL's bare pass/fail. Wire `TriggerEvaluator.Explain`/`Measure` into the
   ignored path the way the matched path already does, so the receipt carries the
   THRESHOLD's actual current/required (e.g. `7/8`) and its unmet clause.

**Proof.** Landed with `WorkflowStateStore.Recheck`, `AdaptiveEvaluator.ReadsWorldTally`,
and the engine's five-tick arming. xUnit walks session 2's exact shape: a kill
evaluated at 8 staged / 1 live is ignored, a recheck at the same tally still
decides nothing and writes nothing, and one tick later at 8/0 the same history wins
`held` — with the history still exactly one event, because a recheck fabricates
none. Python pins hold the arming, the bound, and the ignored-receipt wiring. What
only session 3 can confirm: the live settle-race closing (the recheck firing `held`
within seconds of the final kill). The lap's new `kill_partial` and `wave_cleared`
expectations are the machine form of that verdict — session 2 would have failed
both.

## W2 — The deadline was invisible by presentation, not code (exit blocker) — **landed**

**Root cause.** The banner rendered "594 seconds remaining" once a second for the
full ten minutes — `DurableTimerStore.Pending` has no proximity cap, nothing
clips, and it draws with the drawer closed. It went unseen because: it sits 4px
under the NetworkSense band in the same dark-strip treatment (camouflage); its
fixed-pixel rect shrinks at high resolution (no `GUI.matrix` scaling, unlike
`LabPanel`); "594 seconds remaining" is log copy, not a countdown; and
`RefreshDeadline`'s `catch { line = null; }` is silent. The whole path had zero
behavioral coverage — only source-text pins.

**Fix:**
1. **Clock copy for long deadlines**: `m:ss remaining` at ≥ 60s (`9:54
   remaining`), the existing seconds form below — composed in the same
   `Countdown`/`TriggerCountdown` facts, xUnit-pinned including the large-value
   case the suite never had.
2. **Un-camouflage as an interim, subordinate to ADR 0005**: lower anchor plus
   the canvas's bordered amber-pill treatment (red under 5s unchanged) so it
   cannot read as part of a host HUD band; no new fixed position that the Phase 4
   anchor would have to unwind.
3. **Scale with resolution** via the `LabPanel` `GUI.matrix` precedent.
4. **Evidence**: log the swallowed `RefreshDeadline` exception; add the missing
   behavioral xUnit for `Countdown(594, null)` and `Pending` with far-future
   timers.

**Proof.** Landed: `TriggerCountdown.Seconds` reads `9:54` past a minute and keeps
the seconds form below it; the banner is a bordered nine-sliced pill sized to its
own copy, scaled through `GUI.matrix`, and anchored by the player-set
`Presentation/DeadlineAnchor` fraction rather than any fixed y; the swallowed read
writes one `deadline_unreadable` receipt per distinct failure. xUnit now covers the
clock copy (including 594 verbatim), the composed label across a 600-second window,
and `Pending` with a far-future timer — cover this path never had. What only
session 3 answers: does the pill carry tension in combat (verdict 3a, still the
product question).

## W3 — Copy and legibility fixes — **landed**

- **Same-pack update copy**: `CreatorLoopNotice.Check`/`Card` count distinct pack
  ids, not candidates — one quest in two versions composes the update sentence
  ("〈title〉 〈version〉 is ready. Press F11 to play it.") and `UpdateReady` card
  state; "choose" is reserved for genuinely distinct quests. Re-pin xUnit copy
  matrix + python.
- ~~**Outcome, not machinery**: a `fail` ending never reads "complete".~~ **Dropped:
  the defect did not exist.** The seat had already said so at the keyboard ("i
  slaughtered them"); the receipts confirm it. No overrun fired, the victory route
  won, and "complete" was truthful. What was actually wrong — the row printing a raw outcome
  token, under a card already showing the title — is fixed instead: the EXPERIENCE
  row drops the title and reads "Completed." / "Failed.", and says plainly when no
  Charm is cast.
- **Story history**: authored `message` actions also land in chat scrollback
  ("history of it not just the glimpse") — Center stays the moment, chat becomes
  the memory.
- **Pre-cast target legibility**: CHECK marks the captured object in-world with a
  pending-treatment highlight (same machinery as the cast glow) so the player
  confirms *what* before CAST — session 2 captured a stone through a wall and
  only the glow's location revealed it; the strip also gets a landed state
  instead of silently re-arming to READY.
- **Unbound quests say so**: `event/unbound` gains a player-altitude surface
  ("this quest needs a charm cast on an object") — the card or a notice, not
  silence.

## W4 — Harness generalization — **landed** (and session 3 needs it)

`ValidateRevision`/`ConfirmActivation` asserted Woodbound structure by name, so a
defense lap got no machine gate at all. They are now keyed to a content profile
(ADR 0007): title, stage count, routes in priority order, and per route the trigger
shape, the effects, the destination, the ending and the copy — with ids left
unpinned because Studio generates them. `Prepare` records the profile and later
steps resolve it from the run, so content cannot be switched mid-lap. The self-test
gained three checks: the defense profile validating, a refused profile switch, and
refused content drift (a six-Greyling wave cannot answer a verdict about eight).

This was scoped as "unblocks future laps, not session 3" and that was wrong —
session 3 *is* a defense lap, so without it the staging gate would again be proved
by hand at the keyboard.

## W5 — Phase 4 scoping packet (no code) — **written, awaiting the call**

The seat's composition thesis (horizontal top bar minimizing to four dots), the
alert-anchor requirement (ADR 0005), the Studio reading-order direction, and the
existing F9 switch-cost brief travel together into the Phase 4 scope decision.
Nothing from this list is implemented piecemeal beforehand.

Landed 2026-08-20 as `docs/phase-4-scope-packet.md`: the four inputs with their
evidence, the Lab ownership map the notebook forces, three priced shapes of Phase 4
(notebook as planned / presentation first / notebook shipped on the new composition),
a recommendation, and the three answers the call needs. Written before session 3
deliberately — session 3's verdicts aim the composition work inside whichever shape
is chosen, they do not change which shape is right.

## Sequence and session 3

1. ~~W1 + W2, W3 riding the same build; one interim-package refresh at the end.~~
   **Done 2026-08-20**, in one Runtime/Contracts build with the package refreshed.
2. ~~W4 next (harness only; no game bytes).~~ **Done the same day**, once it became
   clear session 3 depends on it.
3. **Session 3 — the next thing that happens.** Short and targeted: fresh Prepare
   on the `defense` profile, cast the ward's anchor, one honest ten-minute defense
   run. Entry criteria are met: the blockers are landed and gated, the runbook
   carries the CAST beat, and the lap can gate defense content by machine. Exit:
   the three verdicts answered, both ledger rows closed by live observation, Phase
   3 exits.
   - What the machine takes: `kill_partial` (a counted kill reports the count the
     player is working on — session 2's read 0/1) and `wave_cleared` (the win is
     carried by the kills, not by the deadline — session 2's was carried by the
     deadline nine minutes late).
   - What only the seat can take: verdict 3a, whether the clock carries tension in
     combat; escalation and mercy, both unreachable last time behind the tally.
4. W5 packet review with Derek → Phase 4 scope decision.

## Verification (every landing)

xUnit (Lab + Studio via the hash-keyed interim cache), python suite, harness
self-test, generator-drift/identity/boundary gates, then push. Live claims stay
in the ledger until session 3 observes them.

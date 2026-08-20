# Phase 3 close strategy — from session 2's findings to the exit verdicts

Phase 3 exits when the three defense questions (countdown tension, escalation,
mercy) get seat answers and no "can't answer why" survives unexplained. Session 2
(`five-intent-validation-lap-backlog.md`, "Phase 3 exit — session 2") left two
ledger rows open and a set of seat directions. This document sequences the work.
Root causes below are researched, not conjectured — evidence lives in the run
capture (`captures/five-intent-slice1/phase3-exit-2026-08-20-s2/`) and the ADRs.

## W1 — The kill tally races destruction (exit blocker)

**Root cause.** Spawn records were perfect (8 rows, `action_id: wave`, real ZDO
ids under the right owner). Kill events carry no ZDO correlation *by design* —
the tally is a live `ZDOMan.GetZDO` poll per spawned record
(`RuntimeObservation.Facts`). But `kill` is emitted by a Harmony postfix inside
`Character.OnDeath()` and evaluated synchronously in that call stack, before the
dying creature's ZDO leaves `ZDOMan` — so the eighth kill read "cleared ≤ 7", and
with no idle re-evaluation anywhere in the engine, the wrong answer stood until
the overrun timer fired. (`LabGalleryBuilder.DestroySettleTimeout = 5f` is the
repo's own prior proof that destroys don't settle same-frame.)

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

**Proof.** Pure-Contracts xUnit: `Matches` flips false→true when the
`SpawnsByAction` tally changes between two evaluations of the same route; the
ignored-receipt evidence carries the THRESHOLD trace. Python pins for the recheck
scheduling and evidence wiring. What only session 3 can confirm: the live
settle-race actually closing (the recheck firing `held` within seconds of the
final kill).

## W2 — The deadline was invisible by presentation, not code (exit blocker)

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

**Proof.** All copy/`Pending` behavior is game-free testable. What only session 3
answers: does the pill carry tension in combat (verdict 3a, still the product
question).

## W3 — Copy and legibility fixes (small, high-confidence, ride along)

- **Same-pack update copy**: `CreatorLoopNotice.Check`/`Card` count distinct pack
  ids, not candidates — one quest in two versions composes the update sentence
  ("〈title〉 〈version〉 is ready. Press F11 to play it.") and `UpdateReady` card
  state; "choose" is reserved for genuinely distinct quests. Re-pin xUnit copy
  matrix + python.
- **Outcome, not machinery**: the EXPERIENCE row (`DescribeProgress`) surfaces
  the authored outcome — a `fail` ending never reads "complete".
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

## W4 — Harness generalization (unblocks future laps, not session 3)

`ValidateRevision`/`ConfirmActivation` assert Woodbound structure by name
(`Assert-WoodboundCandidate`). Generalize to content profiles (expected title /
stage-shape per lap, Woodbound as one profile, desperate-defense as another) so
non-Woodbound laps get the machine gates session 2 had to prove by hand. Extend
the self-test with a second profile.

## W5 — Phase 4 scoping packet (no code)

The seat's composition thesis (horizontal top bar minimizing to four dots), the
alert-anchor requirement (ADR 0005), the Studio reading-order direction, and the
existing F9 switch-cost brief travel together into the Phase 4 scope decision.
Nothing from this list is implemented piecemeal beforehand.

## Sequence and session 3

1. W1 + W2 (the blockers), W3 riding the same Runtime/Contracts build; one
   interim-package refresh at the end.
2. W4 next (harness only; no game bytes).
3. **Session 3** — short, targeted: fresh Prepare, cast, one honest defense run to
   take the three exit verdicts with a working tally and a visible clock. Entry
   criteria: W1+W2 landed and gated; runbook updated with the CAST beat (done)
   and the recheck expectation. Exit: the three verdicts answered, both ledger
   rows closed with live observations, Phase 3 exits.
4. W5 packet review with Derek → Phase 4 scope decision.

## Verification (every landing)

xUnit (Lab + Studio via the hash-keyed interim cache), python suite, harness
self-test, generator-drift/identity/boundary gates, then push. Live claims stay
in the ledger until session 3 observes them.

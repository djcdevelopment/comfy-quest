# 0006 — Bounded recheck for adaptive routes

Status: accepted — landed 2026-08-20 with strategy workstream W1; awaiting live
confirmation in lap session 3.

## Context

Trigger evaluation is event-driven: a stage's routes are evaluated only when a
subscribed event arrives. Adaptive THRESHOLD facts, however, are read fresh from
the world at evaluation time — the spawn tally is a live `ZDOMan` poll
(`RuntimeObservation.Facts`), relying on "an unresolvable record means the object
was destroyed." Session 2 proved these two truths race each other: the `kill`
event is emitted by a Harmony postfix *inside* `Character.OnDeath()` and
evaluated in that same call stack, before the dying creature's ZDO has left
`ZDOMan`. The eighth kill therefore read "cleared ≤ 7", the `held` route stayed
unmet, and — with no further subscribed events arriving — the wrong answer stood
for nine minutes at a player standing on eight corpses.

**The product said this first.** Studio's guided rehearsal emits a per-run `limitations[]`
(`QuestStudioWorkspace.BuildGuidedSteps`) and for this quest it prints: "Route held waits
for staged objects to be cleared; rehearsal removes 8 of them on request, while play
removes one when the object itself is gone." That is this defect, named by the machine,
before the lap that found it — and already rendered in Studio as a "Coverage limits" card.
The evidence below was gathered the expensive way after the fact.

The run proves it against itself. At 12:17:17 the `timer_elapsed` event evaluated
that same `hold` stage, and the route that matched was not `overrun` — it was
`held`, the victory route, because by then the corpses were gone, the tally read
8 cleared, and the eight kills were still sitting in history. The receipts record
`event/matched` on `timer_elapsed`, the victory message, the reward, and
`transition/complete` on `held`. Nothing about the tally was broken; it was stale
at the only instants anything ever read it, and no second read existed. The Lab
already knew destroys don't settle same-frame
(`LabGalleryBuilder.DestroySettleTimeout`), but that knowledge was gallery-local.

## Decision

When a subscribed event leaves unmet a route gated on a tally the world supplies
fresh, the engine re-evaluates that binding's routes a bounded number of times
over the following seconds (five, on the existing once-per-second `Tick`
cadence). The recheck reruns the same single observation pass (`Facts`) and the
same route evaluation against the history the binding already holds — no new
fact semantics, no event fabrication, no perpetual polling: the bound expires and
the stage returns to purely event-driven evaluation.

Only world-supplied tallies arm it (`AdaptiveEvaluator.ReadsWorldTally`: the
spawn-ledger measures). Deaths come from persisted state and the elapsed measures
from the clock; neither can race the event that just arrived, and arming on them
would be a poll wearing a bound.

## Consequences

- A settle-race can delay a transition by seconds; it can no longer lose one.
- Facts keep exactly one source of truth (the observation pass); the rejected
  alternative — event-sourced kill attribution — would have created a second
  tally needing reconciliation.
- Paired with explaining ignored receipts (the THRESHOLD's real current/required
  instead of a bare 0/1), a future miss is diagnosable from a single receipt.
- A receipt says which mechanism advanced a stage: correlation ids are `evt-` for
  an arriving event and `rck-` for a settled tally, and an armed window that
  closes without a match writes one `event/recheck_expired` carrying what the
  last read saw.

# 0006 — Bounded recheck for adaptive routes

Status: proposed — accepted when strategy workstream W1 lands.

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
for nine minutes until the overrun timer fired at a player standing on eight
corpses. The Lab already knew destroys don't settle same-frame
(`LabGalleryBuilder.DestroySettleTimeout`), but that knowledge was gallery-local.

## Decision

When a subscribed event leaves a THRESHOLD-gated route unmet, the engine
re-evaluates that binding's routes a bounded number of times over the following
seconds, on the existing once-per-second `Tick` cadence. The recheck reruns the
same single observation pass (`Facts`) — no new fact semantics, no event
fabrication, no perpetual polling: the bound expires and the stage returns to
purely event-driven evaluation.

## Consequences

- A settle-race can delay a transition by seconds; it can no longer lose one.
- Facts keep exactly one source of truth (the observation pass); the rejected
  alternative — event-sourced kill attribution — would have created a second
  tally needing reconciliation.
- Paired with explaining ignored receipts (the THRESHOLD's real current/required
  instead of a bare 0/1), a future miss is diagnosable from a single receipt.

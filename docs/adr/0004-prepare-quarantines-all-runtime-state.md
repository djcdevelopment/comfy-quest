# 0004 — Prepare quarantines all runtime state

Status: accepted — proven live in lap session 2 (12 packs + 2 active + 8 state
files quarantined and restored byte-exact).

## Context

Lap preparation originally quarantined only the inbox (`*.questpack`) and
`active/`. Session 1 showed the gap twice: `state/` (pending workflow
transitions, timers, spawn and action ledgers — plus the atomic writer's
`.previous`/`.tmp` siblings) survived into the lap and replayed against the
quarantined active set, and `inbox-dev/` — the lane the defense publishes
through — was never swept at all.

## Decision

`Prepare` quarantines every runtime-owned store: `inbox`, `inbox-dev`, `active`,
and `state` (recursive, unfiltered — the atomic-writer siblings ride along). The
mechanism is uniform: manifest persisted into the run context *before* any move,
move-not-delete into the run's quarantine, hash-verified restore of every file on
Cleanup, lap-created files swept to `post-run/`. `state`'s manifest travels as
`state_files` because `state` is the run context's own lifecycle field.

## Consequences

- A lap begins from a genuinely empty runtime world and ends with the install
  byte-identical to how it was found; session 2 confirmed both directions live.
- Interrupted preparation stays recoverable for the new kinds through the same
  manifest-first ordering and fault seams the original kinds had.
- World-resident artifacts (charm inscriptions on ZDOs) are deliberately out of
  scope: they belong to the world, read as OTHER VERSION under a fresh
  activation, and quarantining them would mean mutating a player's world.

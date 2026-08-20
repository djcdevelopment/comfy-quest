# 0003 — Bounded identical rejection receipts

Status: accepted.

## Context

With the active set absent (as during lap preparation), the engine's
pre-subscription branch rejected every normalized world event: session 1 wrote 47
identical `transition/rejected · active_set_missing` receipts between world load
and the first check, 39 of them in one second. The branch sits before the
subscription filter and the duplicate window, so it was unbounded by
construction. The repo's ethos holds that receipts are explanation, not logging —
a firehose of identical receipts explains nothing and buries everything.

## Decision

An identical `(operation, error)` rejection is worth `MaxIdenticalRejections`
receipts (three) plus one final receipt naming the suppression
(`suppressed_after_3`); further identical rejections stay silent until the error
changes or clears, at which point the counter resets
(`RuntimeExperienceEngine.OnEvent`).

## Consequences

- A missing active set is still diagnosed — in three receipts, not a burst that
  drowns the run's evidence.
- The suppression receipt itself is evidence that more rejections occurred; no
  reader can mistake silence for health.
- `Prepare` separately quarantines `state/` (ADR 0004), so the historical trigger
  for the burst cannot recur either; the bound is defense in depth for any future
  identical-rejection source.

# 0008 — The overhead bar replaces the F9 drawer

Status: accepted — extends [0005](0005-channel-taxonomy-is-not-an-anchor.md); verified live
2026-08-24 in Creator Session `creator-20260824T105854Z-b55c8b18`.

## Context

[0005](0005-channel-taxonomy-is-not-an-anchor.md) decided that alerts get **one known,
player-configurable anchor**, and that every channel speaks through it rather than forking its
own fixed position. It did not decide what composition carries that anchor. Its amendment note
records the seat's design direction from session 2 — a horizontal top bar minimising to four
dots — and records that converting that direction into a scheduling question was a mistake.

This record closes the composition question, and it is written because the decision had been
living only in `docs/quest-mission-control.json` as a `decisions[]` entry. That file is
mutable; a reversal there leaves no trace. The ADR index is append-only and is the right home.

The live evidence that forced the geometry: a direct Valheim window-buffer capture found that
the 36-pixel compact bar at `y=48` was wholly behind the host diagnostic band ending at `y=84`
in the 1026x740 viewport. No creator relayed that; automation found it. Production geometry now
uses the executable-tested `RuntimeCreatorBarLayout` with a `y=92` safe top and on-screen
clamping, and a subsequent no-activate buffer capture showed the complete compact bar
immediately below the host band at the same viewport.

## Decision

**F9 expands or minimizes one always-present overhead bar. It does not open a drawer.**

The bar is the composition that carries 0005's single configurable alert anchor. A single
clamped alert anchor carries deadline and actionable-warning state; channels do not fork their
own positions.

## Consequences

- The old F9 drawer is gone. Any procedure that instructs a creator to "open F9" and interact
  with drawer contents is describing a UI that no longer exists.
- **This invalidates existing seat choreography.**
  `docs/runbooks/OMEN-LAP-PHASE3-EXIT-S3.md` still directs the seat to open the F9 drawer to
  CHECK and CAST. That runbook passes structural validation — the renderer confirms it yields
  five sequence steps and three verdicts — while describing controls that were replaced. It
  must be re-derived against the current overhead-bar interaction before it is run, and must
  not be presented as seat-ready in the meantime.
- Syntactic runbook validation is therefore insufficient for cold seat procedures. Roadmap and
  runbook authority needs freshness or supersession state, not just structural checks.
- Bar geometry is a pure, executable-tested concern (`RuntimeCreatorBarLayout`), so a
  regression is a unit-test failure rather than a seat discovery.

# 0015 — The requirements ledger records lane authority, it cannot confer it

Status: accepted 2026-08-25.

## Context

Audit C6 found that work items had no machine-enforced relationship to requirements or lanes:
the manifest's queue carried `id`, `title`, `detail`, `state`, `source` and `source_contains`
and nothing else. The validator therefore could not detect a work item belonging to no lane
(audit C4) or a requirement no work item claimed (audit C2), which is why both happened.

Closing it meant adding `docs/creator-requirements-ledger.json`, giving all 47 `FR-`/`NFR-`
identifiers a disposition. That immediately raises the question this record answers: **a file
that says which lane owns a requirement is one edit away from being the file that decides it.**

The first version was defensible and illegible. A reader seeing `FR-RESET-001 · active · 4A`
had no way to tell whether the ledger had decided that or mirrored a decision made elsewhere —
and a later agent, finding a `parked` requirement whose destination looked obvious, would have
had nothing stopping it from being helpful.

## Decision

**Authority runs one way, and the ledger sits in the middle of it as a recorder:**

> requirements → phase authority → ledger recording → queue realization → evidence

`docs/creator-portfolio-requirements.md` states what is required.
[`docs/creator-os-phases.json`](../creator-os-phases.json) is the phase and scheduling
authority; **it alone decides which lane owns a requirement** (ADR
[0013](0013-one-numbering-authority-for-lane-vocabulary.md)). The ledger records the resulting
disposition. `docs/quest-mission-control.json` realizes it as work items. Evidence closes it.

Three rules make that mechanical rather than aspirational:

1. **Every lane the ledger names points at where it came from**, through `lane_authority` —
   `phase-authority` where the vocabulary names the requirement under that lane, or
   `queue-realization` where a work item already scheduled in that lane claims it. Neither
   value is "the ledger". Claiming phase authority the vocabulary did not grant fails
   validation; so does demoting a vocabulary-owned requirement to a queue reading.
2. **A future lane can only come from the phase authority.** `deferred` may not read a lane off
   a queue item, because a future placement is exactly the guess this exists to prevent.
3. **`parked` and `met` carry no lane at all.** That is what stops a parked requirement from
   drifting toward its apparently obvious destination.

`parked` also gets one meaning, narrowly: *the requirement remains recognized and relevant, but
scheduling or advancement requires an explicit ruling that has not yet been made.* Every parked
entry names the missing decision in `pending_ruling`, and the ledger records the six states
`parked` must not absorb — future-but-decided work, externally blocked work, abandoned or
superseded requirements, implementation-complete-but-unproven work, and work merely outside the
current lane — each pointing at the representation that already covers it.

`met` stays deliberately hard to earn: gated evidence at the requirement's own proof standard,
with no unresolved audit finding against it. Code existing is not evidence, and neither is an
end-to-end path that looks wired.

## Consequences

- A requirement the phase authority has not placed **cannot be placed by this ledger**, however
  obvious its destination looks. That is how a shadow scheduler starts, and it is refused at the
  gate rather than by convention.
- Ten requirements are `parked` rather than quietly scheduled, each naming the ruling it waits
  on. That is more visible open work than the roadmap showed before, and the visibility is the
  product.
- Scheduling can still move without a lane changing. `NFR-OBS-001` went from `deferred` to
  `active` when `queue.receipt-retention` claimed it; the lane stayed 4B's, and
  `lane_authority` stayed `phase-authority`.
- Widening this — letting the ledger place a requirement, or adding a fifth disposition —
  requires superseding this record. Adding a *non-lane assignment state* for work items does
  not, because an assignment state cannot schedule anything.

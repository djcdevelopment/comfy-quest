# 0013 — One numbering authority for lane vocabulary

Status: accepted 2026-08-24.

## Context

Three incompatible meanings for "Phase 3" and "Phase 4" were live simultaneously:

- `docs/quest-mission-control.json` phase 3 = "Adaptive event semantics";
- `docs/creator-os.md` product-roadmap item 3 = "Make autonomous live integration routine";
- `docs/five-intent-program-plan.md` phase 4 = "Quest Lab spellbook".

The mission-control page stitched its phase list from two of those families at once — phases
1-2 citing the program plan, 3-5 citing creator-os and the requirements document.

Each of those numberings was locally reasonable when written. The problem is not that any one
is wrong; it is that every document was allowed to *define* phases rather than *reference*
them, so correcting one does not correct the others, and nothing detects the divergence.

The consequence is concrete and agent-shaped. A bare ordinal carries almost no meaning. An
agent told to "do Phase 4 work" can read three documents, get three answers, and execute the
wrong one with complete confidence — which is exactly the class of failure
`docs/creator-os-audit-2026-08-24.md` finding B2 describes, where the stale program-plan table
would have pulled deferred world packaging back onto the active path.

## Decision

**There is exactly one definition of Creator OS lane vocabulary:
`docs/creator-os-phases.json`.**

Other documents may **reference** a lane. They may not define one, renumber one, or restate
what one contains. A prose description that disagrees with that file is stale prose, not a
competing definition.

The canonical lanes are:

| id | slug | question |
| --- | --- | --- |
| 4A | `guild-scale-runtime` | Can Creator OS operate a guild? |
| 4B | `sustained-creator-campaign` | Can I actually use Creator OS to build a guild over time? |
| 4C | `optional-acceleration` | Which repetitions have earned tooling? |
| 5 | `distribution-release` | Can someone else receive, inspect, and run what was built? |

**Cite a lane as `4A / guild-scale-runtime`, not as "Phase 4".** The slug is the part that
resists reinterpretation; the ordinal alone is the part that caused this record to be written.

Historical numbered plans are demoted to historical or superseded status and keep their own
numbering as a record of what was believed at the time. They are not a source of current lane
meaning.

## Consequences

- `docs/creator-os-phases.json` is machine-readable on purpose. Lane membership, exit
  criteria, and requirement lineage become checkable rather than narrated, and the file is the
  natural place for the program invariant's lane-disposition checks to resolve against.
- The requirements document's 4A/4B prose is now a **reference**, not an authority. It must be
  brought into agreement, but until it is, this file wins — so a stale sentence there is a
  documentation defect rather than a live contradiction.
- `docs/quest-mission-control.json` keeps its `phases[]` array as a program snapshot with
  historical numbering. It is not lane vocabulary, and its phase 3 is not lane 4A's neighbour.
- Adding a lane, renaming a slug, or changing what a lane contains is a change to this
  vocabulary and needs the same sign-off any adopted baseline change needs.
- Prose that redefines a lane should eventually fail a check rather than merely being wrong.
  That check belongs with the program invariant machinery, not in this record.

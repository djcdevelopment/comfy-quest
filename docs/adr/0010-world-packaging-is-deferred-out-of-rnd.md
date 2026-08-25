# 0010 — Versioned world packaging is deferred out of R&D

Status: accepted 2026-08-24, as part of the adoption-path pivot.

## Context

[0009](0009-the-saved-world-is-the-playable-release-authority.md) makes the saved world the
playable authority. The obvious next inference is that the saved world therefore needs a
versioned, inspectable, installable bundle — and `FR-WORLD-003` through `FR-WORLD-005` specify
exactly that: a `.db`/`.fwl` pair with canonical hashes, stable world uid, compatibility facts,
entry points, an anchor catalog, a linked guild release, declared exclusions, and atomic
install/upgrade/rollback/uninstall.

That inference is correct about the eventual destination and wrong about the ordering.

The adoption bottleneck is not distribution. It is that Derek has not yet created and
repeatedly run a substantial body of guild quests and events. Until that happens, the world,
anchor, content, and compatibility contracts a bundle would freeze are not yet stable — so a
bundle built now would be versioning a guess.

Ordinary recoverable world copies are sufficient for local R&D. Creator Session already backs
up the closed-game `.db`/`.fwl` pair at Prepare and records its hashes in the session manifest.

There is a second, blunter reason. Building packaging early means maintaining it through every
contract change the dogfood campaign forces — paying the migration cost repeatedly for a
capability nobody is consuming yet.

## Decision

**Do not build versioned `.db`/`.fwl` packaging, inspection, installation, or rollback during
active R&D.** Use ordinary recoverable local world copies. Activate this work only when
repeated guild authoring has stabilized the contracts and community distribution becomes the
next result — that is, Phase 5.

## Consequences

- `FR-WORLD-003`, `FR-WORLD-004`, and `FR-WORLD-005` remain specified and carry a `deferred`
  disposition owned by Phase 5. They are not gaps; they are scheduled elsewhere.
- **A stale table contradicts this.** `docs/five-intent-program-plan.md:110-111` still lists
  saved-world bundles inside lane 4A, superseded by the pivot twelve lines below it but never
  corrected. Someone working from that table would build exactly what this record forbids.
  Reconciling it requires sign-off because it changes the meaning of 4A.
- `FR-REL-001` makes a guild release bind a world bundle. Until Phase 5, a "release" in the
  R&D loop cannot mean the full release artifact — so any 4A checklist whose terminal stage is
  *Release* is asserting something that is not built.
- The world association may remain local during R&D. Community distribution eventually binds
  it to a bundle.

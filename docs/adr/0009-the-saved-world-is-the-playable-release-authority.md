# 0009 — The saved world is the playable release authority

Status: accepted — forced by the first live Godbuild capture, 2026-08-24.

## Context

The first real Godbuild target completed on the exact `ComfyQuestDemo` world in Creator Session
`creator-first-godbuild-buildmode-20260824`. Capture and Inspect recorded 12 creator-owned
pieces within 12 metres as `first-portal-progression-shelter`, source-pieces SHA256
`e1e01ff675017bc6ed1d83868b3dcd5f9bb9a2f84089721dfa032fb79c537dfd`, with the capture and the
PlanBuild projection internally consistent.

That lap proved two things at once, and the second matters more than the first.

Capture *works*: human spacing became reviewable source, a typed plan, an SVG preview, and a
stable module. Ordinary wood, roof, fire, and sign language locates a player in Valheim
progression.

But the capture's own manifest
(`examples/worldbuild/first-portal-progression-shelter/manifest.json`) declares what it cannot
carry. Its `unsupported` list names terrain and vegetation, portal links, container contents,
door state, arbitrary ZDO fields, and non-unit scale. The uphill clearing observed from the
shelter — the thing that supplies the next authored stage — is terrain. It is not in the
capture and cannot be.

A generator that excludes terrain and portal topology cannot be the source of a playable
authored environment. The manifest is the proof, and it is machine-checked
(`tools/blueprints/import_capture.py` recomputes and matches `PiecesSha256`, and `--check`
raises on Godbuild output drift).

## Decision

**The versioned saved world owns terrain, vegetation, portal topology, and authored spatial
context. Godbuild capture is optional reusable module, source, diff, and replay authority — and
only for the fields its manifest declares.**

Godbuild generation does not replace the saved guild world. A capture may not claim to
reproduce fields its manifest excludes.

## Consequences

- Auto-generation is not on the adoption critical path. It is an accelerator that uses the same
  artifact and validator; it cannot replace the saved world, the authored source, or acceptance
  evidence.
- Replay stays available but unscheduled. It earns use when a reusable module needs cloning,
  not as a routine step. Any checklist that mandates `Replay` on every lap is overstating the
  program's own position.
- The saved world becomes a thing that must eventually be distributable — which is a real
  dependency, and deliberately deferred by
  [0010](0010-world-packaging-is-deferred-out-of-rnd.md).
- `Close -Restore` restores prior install bytes, not world state. Creator Session does not
  claim otherwise, and the distinction must stay visible wherever restoration is described.

# 0012 — Creator-session world entry is not field-lab orchestration

Status: **proposed** — awaiting sign-off. This record changes a repository ownership boundary
and must not be treated as in force until accepted.

## Context

Lane 4A's exit requires that automation "launches and closes the installed game through the
standalone harness". There is no launcher in this repository. The only `valheim.exe` reference
anywhere is an existence check at `tools/creator-session/Invoke-CreatorSession.ps1:84`.

Investigation of the sibling repositories established four facts:

1. The mature lifecycle harness in the field-lab repository is a **server-join** lane: it
   launches Steam with a `+connect` argument, stops the process gracefully then forcibly, and
   detects readiness from a file marker. It has no path to main-menu-to-named-local-world.
2. The harness that *does* enter a named local world lives in a different repository again, and
   its own description explicitly rejects the server-join harness for this purpose — that
   harness races the main menu and has no path for loading a local world.
3. **Neither is consumable.** The field-lab repository has zero git tags and zero releases; its
   only packable project is a transport-contracts package; the harness is a `.ps1` with no
   project file, and it dot-sources a repo-identity assertion that throws in any consumer.
4. A packaged script would still be inert. Character select depends on a mod DLL owned by a
   third repository with no release, and local world entry additionally needs a plugin whose
   source sits in a retired checkout with no release and no build lane.

Consuming the existing capability therefore means creating **three publishing lanes in three
repositories** for a capability this repository needs in one bounded form. That is
disproportionate, and `BOUNDARY.md` currently declares six artifact contracts, all outbound —
this repository consumes nothing from a sibling today.

The alternative is not "comfy-quest may launch Valheim", which is too broad to be a boundary at
all. It is a semantic distinction.

One constraint is already satisfied and must stay that way: **no synthetic input.**
`tests/test_creator_session.py:98-116` asserts `Invoke-Expression`, `SendKeys`, `keybd_event`,
`Console.instance`, and `ZInput.Simulate` appear nowhere here, and every sibling repository
independently holds the same line by design. World entry is a mod calling the game's own APIs —
enumerate saved worlds, match by name, refuse if absent, set the server, load the scene, wait
for a real in-world player — not a keyboard macro.

## Decision

**Creator-session world entry** — placing the already-owned creator Runtime into a
*specifically pinned local authoring world* as part of the Author → Validate → Play → Observe
workflow — **belongs to comfy-quest.**

**Field-lab orchestration** — launching, acquiring, coordinating, connecting, or managing
*arbitrary clients against external or server environments* — **remains outside comfy-quest**,
at the field-lab boundary.

The existing sibling harness is a server-join lane, which is field-lab orchestration under this
definition. The capability this repository needs is not.

The implementation is bounded. Every constraint is a requirement, not a guideline:

- named local world only;
- exact world match;
- refuse if absent or ambiguous;
- pinned world UID where available;
- existing machine, world, and session identity checks;
- bounded expiry;
- single consumption;
- correlated receipt;
- no synthetic keyboard, mouse, or console input;
- no arbitrary server joining;
- no general-purpose client orchestration.

**Borrow the proven mechanism, not sibling-repository code or dependencies.**

## Consequences

- This is the boundary's purpose: `Launch` and world entry **cannot grow into a second
  field-lab orchestration system**. Without a recorded boundary, "just add a `+connect`
  parameter" is a one-line change that nobody notices crossing the line. With it, that change
  requires superseding this record.
- Do not create the three sibling publishing lanes for this. Revisit only if a second consumer
  needs the server-join harness.
- This record does **not** gate lane 4A. The truthful current baseline is `NFR-SEAT-001`'s
  existing allowance: one human launch and world-entry step is permitted, and after that point
  the Creator Session owns the mechanical workflow. Automating world entry is a *subsequent
  reduction of the remaining human boundary*, not something that retroactively invalidates the
  machine-owned workflow that already exists.
- `BOUNDARY.md` needs no inbound artifact row under this decision, because nothing is consumed.
  If sign-off goes the other way, it needs its first one.

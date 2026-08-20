# Retrospective — the product already told us

Written the day it happened, after three seat sessions were burned and the program came
close to changing tools. Deliberately short: the previous retro's process changes invented
a manual ritual that duplicated a shipped feature, which is the exact failure mode being
recorded here.

## The defect

**Writing a document asserting what I believe instead of consulting the artifact that
already answers it.** One defect, five instances, all on 2026-08-20.

| # | The artifact that already knew | What I did instead |
| --- | --- | --- |
| 1 | `RuntimeCharmBinding.InscribeAim → TryActive` returns `active_set_missing` without an active set | Read that method the same session, then wrote a seat script that casts before loading |
| 2 | `LabBlueprintBuilder.TrySelect` — a bounded-radius ZDO walk with cap-and-refuse | Planned a new world-walker for the scanner |
| 3 | Studio rehearsal's per-run `limitations[]`, rendered on screen by `renderRehearsal()` | Root-caused the same defect overnight from receipts; wrote ADR 0006, a retro lesson and a strategy workstream, none citing it |
| 4 | `memory/guard-live-seat-time.md`, written that morning: "rehearse the runbook beat-by-beat before any seat session" | Never opened it |
| 5 | `docs/runbooks/I2-QUESTPACK-OMEN.md` — the correct beat order, already proven in a lap | Wrote the session-3 script from memory, in the same directory |

Instance 3 is the one worth keeping: rehearsal printed *"Route held waits for staged
objects to be cleared; rehearsal removes 8 of them on request, while play removes one when
the object itself is gone"* on every run, before the lap that found it. The seat's own
words when he finally said it: **"we literally have a rehearse feature for this."**

## Why it repeated

Because the corrections were instance-shaped. After session 2's missing CAST beat the
lesson was "runbook sequencing changes get pinned like code" — and the pin written for
session 3 asserted the CAST phrase that had broken, checked that it preceded the chat
beat, and never checked that LOAD preceded CAST. It made the hole look covered.

And because **recall failed at the moment of authoring.** The `MEMORY.md` index line for
instance 4 read "verify observed state before executing any cue", which does not fire when
the task is *writing* a runbook. The rule was one file away; the summary hid it.

## What changed (mechanical, not resolutions)

- `MEMORY.md` index lines now name the **trigger situation** and the **rule**, not the
  anecdote. A summary that does not fire is the bug.
- `AGENTS.md` → Verification now covers **choreography**: a human-facing sequence is
  verified by execution and derivation, never by review. That file is read at session
  start, so the rule binds there.
- `tests/test_quest_runtime_validation_lap.py` checks the **class**: no runbook step may
  require a precondition (`active-set.json` via F11, a bound charm via CHECK/CAST) that no
  earlier step establishes, traced from the code's own diagnostics. It carries a self-test
  built from the three scripts we actually shipped, and it failed the live S3 runbook
  before that runbook was fixed.
- `OMEN-LAP-PHASE3-EXIT-S3.md` is now *derived* — from the proven I2 order, a rehearsal
  run, and the precondition chain — and its human checklist is exactly what rehearsal
  declares it cannot prove. Thirteen checkboxes became three verdicts.
- The retro's "injected-fact inventory" ritual is **withdrawn**: `limitations[]` already
  emits it. Consume it, cite it.
- Two machine-produced explanations that were being discarded are now rendered: the
  `/runtime-status` `diagnostics` array (fetched and ignored) and the itemized
  `ContractDiagnostic` list on rejection receipts (only the coarse `Error` was shown).
- ADR 0005's deferral clause is struck. It turned a seat design decision into a scheduling
  question, and I quoted it back at the seat as the reason his design was unbuilt.

## Known instance-pins, not yet generalised

Recorded so they are not mistaken for coverage. Generalise when next touched:

- **Overlay geometry** — the deadline-pill test pins that one widget's scaling and anchor.
  The rule: no player-facing overlay uses an unscaled fixed rect that can collide with the
  host HUD.
- **Unbounded diagnostic branches** — the rejection-bound test pins the one branch that
  flooded. The rule: any diagnostic branch running before the subscription filter is
  rate-bounded.
- **Repeatable no-ops** — the idle-HUD test pins F10/F11. The rule: any repeatable action
  with no state change still yields visibly distinct feedback.

## The cost, recorded plainly

Three sessions, each ending in the first few steps, each costing a full context switch
away from about a dozen parallel projects — the scarcest resource here, and one I had been
treating as fifteen minutes. None of the failures were in the code; the code held every
time it was tested. All of them were in the artifact I handed over, and every one of them
was already answered somewhere in the repository.

# Retrospective — a gate you walk yourself

Written at the end of the session that closed Lane 0 and shipped Lanes 1 and 2. Short, and
about one defect, because the previous retro is right that a retro which invents ritual is
worse than none.

## The defect

**Declaring a property holds because I checked it, rather than building the check that fails
without me.** Six instances in one session. The three I found myself, I found by building
something that could fail. The three I missed, I missed by inspecting.

| # | What was wrong | What found it |
| --- | --- | --- |
| 1 | The Lane 0 exit gate was walked by a reader who already knew which files to open. `README.md` and the previous handoff both pointed a cold reader at `creator-os.md`, which mentioned neither the strategy, nor the lane vocabulary, nor the ledger. | Derek asking "where is the updated plan written to disc?" |
| 2 | The requirements ledger was defensible but illegible: `FR-RESET-001 · active · 4A` gave a reader no way to tell whether the ledger decided that or mirrored it. | Derek's review |
| 3 | A field added to the run-control controller shadowed a constructor parameter of the same name, so the assignment hit the parameter. | `CS0649`, first build |
| 4 | Retention sorted newest-first by write time and broke ties on name **ascending** — opposite directions. Inside one filesystem tick it kept the oldest of a tied group. | CI, on the push |
| 5 | The handoff named Lane 2 as the next task while the queue held no work item for it. | A test written twenty minutes earlier |
| 6 | Two assertions that could not fail: one tautological, one so convoluted it asserted nothing. | The xUnit analyzer, and re-reading |

Rows 1 and 2 are the same defect as rows 3–6, one level up. In 3–6 a machine held the check.
In 1 and 2 I held it, and I passed.

## Why row 4 is the sharpest instance

The tiebreak bug was invisible on this workstation and immediate on a hosted runner: writing
513 files fast enough that their timestamps collide is normal there and not here. That is
**exactly** the shape of finding D7 from the day before — `Get-FileHash` healthy on a
workstation, unresolvable on the runner — and of finding D6, where a warm package cache
serves stale bytes to everyone except CI.

Three findings, one week, one shape: **a workstation is a machine with a specific history,
and agreement with it is not evidence.** The repository already knew this; punch item 18
exists because ~20 more `Get-FileHash` call sites carry the same latency, in scripts CI never
runs.

## What actually worked

Not process — mechanism. Every good outcome this session came from something that could fail
without being asked:

- The five invariant checks each got a negative test. Row 5 was caught by one of them, one day
  after it was written, against drift I had introduced myself.
- The reading-order pointers are pinned to the manifest, so the failure in row 1 is now a
  drift-gate error rather than a reader's disappointment. I proved it by aiming README's first
  pointer back at the superseded handoff and watching the gate refuse.
- The retention regression test was verified by reverting the fix and watching it fail.

That last one is the whole lesson in one action. A check that has never been observed failing
is a claim, not a check.

## The one change

Before reporting that a property is enforced, **make the negative case and watch it fail.**
Not "add a negative test" — run it against the broken version, once, and see red. It costs a
minute and it is the difference between a gate and a decoration.

No new ritual, no new document, no new review step. The repository already has the machinery;
this is about not skipping the last five seconds of using it.

## What this does not say

It does not say I should have caught rows 1 and 2 myself. Both were found by a reader arriving
cold, which is the position those surfaces exist to serve and the position I cannot occupy. A
fresh reader is a real instrument, and the fix for row 1 was to make the drift gate hold what
the fresh reader had to hold instead.

It also does not say the fenced findings should have been fixed. D2, D4, D5, C5, D6, item 16,
item 18 and the ungated E2E suite were left alone on purpose, and Lane 1's contracts change
deliberately reproduced D6 rather than smuggling in item 16. Naming the hazard louder — the
handoff now records the live instance — is the correct move when the repair is parked.

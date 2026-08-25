# Working agreements

These were an agent's private notes. They are published here because none of the lessons are
personal — they are about how this kind of work fails, and every one of them was paid for.

Each rule carries the incident that produced it. That is deliberate: a rule with its cost
attached survives a rewrite, and a rule without one gets optimised away by the next person who
finds it inconvenient. Where a rule names a specific defect, the receipt is in
[`retros/`](retros/) or [`creator-os-audit-2026-08-24.md`](creator-os-audit-2026-08-24.md).

`AGENTS.md` is the short operating contract and takes precedence. This file is the why.

---

## Consult the artifact first

**Before asserting a sequence, a precondition, a limitation, or a root cause, open the surface
that already reports it.**

This product declares a great deal about itself: rehearsal publishes `proof_level`, a
`disclaimer`, and a per-run `limitations[]`; receipts carry `Evidence`, `RejectedEvidence`, and
`CurrentCount`/`RequiredCount`; failures carry a `ContractDiagnostic` code;
`TriggerEvaluator.Explain` produces a trace. Prose that re-derives any of it is both wasted and
unreliable.

**The cost.** Five instances in one day, 2026-08-20. A seat script that cast a charm before
loading the quest, when the method returning `active_set_missing` had been read that same
session. A new world-walker planned when `LabBlueprintBuilder.TrySelect` already did a
bounded-radius walk with cap-and-refuse. An ADR, a retro lesson, and a strategy workstream that
each re-derived overnight the exact exit blocker Studio's rehearsal had already printed on
screen — none of them citing it. A runbook written from memory with a correct one in the same
directory.

**How to apply.** Ask "what already knows this?" and open it. Sequences and preconditions → the
code path and its diagnostic strings, or an existing runbook already proven in a lap. What a
proof does *not* cover → the emitter's own declaration, never your summary of it. Why something
failed → the receipts. Whether a capability exists → search before designing; this repository
usually has it.

Two corollaries. Never invent a manual ritual that duplicates a machine output. And if a
surface produces an explanation nobody displays, wire it up rather than writing prose about it.

## Never hand over a sequence you have not executed

**A human-facing sequence is verified by execution, not by review.**

Derive it from machine sources — a rehearsal run, the precondition chain in the code and the
diagnostic each stage fails with, an existing correct runbook — then check that every step's
precondition is established by an earlier step in the same document. Then spend real effort
attacking it: *find the step where this fails.*

**The cost.** Three seat sessions were burned on sequences that had never been executed, and
the code was never at fault. Session 1 pre-staged a revision and collapsed the update beat.
Session 2 omitted the CAST beat, so no charm was ever bound and every player action produced an
`event/unbound` receipt. Session 3 omitted the LOAD beat.

**Why it is expensive out of proportion to its size.** Seat time is a context switch, not
minutes. Someone running a dozen parallel efforts has to unload all of them and page this one
in; a trip on step 3 of 30 costs the whole switch, then another to retry.

**The root cause worth keeping.** The verification was self-referential — elaborate,
self-consistent structures whose tests all pass, aimed at the artifact and never at the
choreography. The defense quest had all four branches rehearsed; the seat session itself had
zero. **Greater capability makes this worse, not better**: a stronger model builds more
convincing self-consistent structures. Problem selection, frame-auditing and self-distrust are
the work, not a preamble to it.

**How to apply.** A human's script contains only irreducibly human judgments — does it feel
tense, does this read as escalation, did the win land. Anything a machine can establish or read
from a receipt never becomes a checkbox for a person. Ask once, for one contiguous block, with
everything else already done. And isolate every shared resource: **a git worktree isolates the
repository, not the game install.** Agree who owns `<Valheim>/BepInEx/` for a lap's duration
before the lap; that has been paid for twice.

## Prove the check can fail

**Before reporting that a gate, test, or invariant is enforced, break the thing it guards and
watch it refuse.**

Not "add a negative test" — run it against the broken version once, see it go red, then fix and
re-run. A check that has never been *observed* failing is a claim, not a check.

**The cost.** Six defects in one session, 2026-08-25. The three found by machines — a `CS0649`
shadowed field, a retention tiebreak caught by CI, a queue/document disagreement caught by a
test written twenty minutes earlier — were all found by something that could fail unasked. The
three missed were verified by inspection: an exit gate walked by a reader who already knew which
files to open, a ledger that was defensible but illegible, and two assertions that could not
fail. Full account in
[`retros/2026-08-25-a-gate-you-walk-yourself.md`](retros/2026-08-25-a-gate-you-walk-yourself.md).

**How to apply.** "Verified by reverting the fix and watching it fail" is the sentence that
turns a passing run into evidence. Report the observed failure, not only the green.

## Agreement with your workstation is not evidence

**A development machine is a machine with a specific history. Passing there is the beginning of
verification, not the end.**

**The cost.** Three findings in one week, one shape. `Get-FileHash` was unresolvable on the
hosted runner while healthy locally, and it sat on the precondition path for every later Creator
Session operation (audit D7). The interim contracts package is version-pinned with mutable
bytes, so a warm NuGet cache serves stale contracts to every developer and not to CI (audit D6).
A retention sweep broke ties in the wrong direction, which is invisible until a machine writes
files fast enough that their timestamps collide.

**How to apply.** When something depends on the environment — a cmdlet, a cache, a filesystem
timestamp, a path length — assume the runner disagrees and make the check say so. Roughly twenty
`Get-FileHash` call sites remain in scripts CI never runs; they carry the same latency and look
healthy on a workstation.

## Take the L plainly

**On being shown a failure: state the mechanical cause in one or two plain sentences, with no
value-salvage, before any fix or plan.**

**The cost.** The most damning feedback of a lap post-mortem was not about the misses. It was
that extracting each concession took escalating anger, and every acknowledgment arrived wrapped
in a framework that partially flattered the model. *"You don't take an L. You'll gaslight, and
wiggle around and look for an angle."*

**Why.** A wrapped concession is worse than the original miss, because it makes every future
assurance suspect. Essays, new frameworks and process ceremonies come later, or not at all.

## A first-hand report outranks your inference

**When someone reports what happened during live play, that report is evidence. Your mechanism
story is the suspect.**

**The cost.** After a lap session: *"ahh well i slaughtered them, but it was fun :]"* — an
unambiguous report that the wave was dead and the fight was won. The lap record captured it
verbatim, then reasoned from a stale tally that the deadline could only be a loss, and wrote the
inference down as fact. A recorded victory and an invented defeat sat in one file for a day. The
receipts agreed with the human: the victory route matched at the deadline event. The inverted
write-up cost a workstream aimed at a bug that never existed and nearly buried the cleanest
proof of the real one.

**How to apply.** If your mechanism story requires the person's account to be mistaken, stop and
read the preserved capture. If you genuinely believe the evidence contradicts them, say so
explicitly and show it — never write past them.

## Surface parked work before proposing new work

**A hold is a scheduling state, not an exclusion.**

**The cost.** A UI that had been designed by hand, in a dedicated session, was held "awaiting the
lap verdict" — and across five separate "what else can we build?" conversations, no session
resurfaced it. Someone's own parked work is the most expensive thing to forget: it reads as
their time being discarded.

**How to apply.** On any "what next / what else" question, first list every parked, held or
deferred item with its hold reason and whether that hold's condition has since been met. Only
then propose anything new. An expired hold is buildable now, and saying so is the answer.

In this repository that list is mechanical:
[`creator-requirements-ledger.json`](creator-requirements-ledger.json) gives every `parked`
requirement a `pending_ruling` naming the decision it waits on.

## Refine plans; do not expand them

**Feedback on a plan is a request to sharpen it, not to enlarge it.**

Preserve the approved sequencing and stop conditions, fold the correction into the existing
steps, and report back only what materially changed — including any changed success criteria,
called out explicitly. Do not answer a critique with new workstreams, new documents, or by
starting to design the future system the critique warns about.

**How to apply.** Edit the plan in place, keep the approved spine visible, and re-present a
short diff rather than the whole plan again. When someone identifies the strongest finding in
your own assessment, promote it to the starting point instead of leaving it a footnote.

## Lead with the player experience

**State what the creator or player experiences, and why it is dramatic, before contracts,
catalogs, or runtime mechanics.**

A design pitch that opens with schema fields buries the thing being built. Answer a
human-reported symptom at the reporter's altitude first, with maintainer evidence underneath.

**Corollary on live laps.** Established contract behaviour belongs in synthetic regression, never
in a repeated human lap. Ask for hands-on time only when a novel uncertainty can change a
product decision or the next build.

## Capability enters from observed need

Capability joins the default palette **only** because an authored quest idea demanded it, never
because a hook exists. Receipts are explanation, not logging — beginner prose with expert
drill-down, and the prose is designed at the same time as the field. One artifact serves the
whole beginner-to-agent ladder. AI composes against the same contract as a human, and is never
its own architectural layer.

**Why.** This repository holds far more capability than it exposes, and the largest product risk
is exposing it too quickly — recreating the worst part of powerful modding tools.

Applied concretely: a new limit is **declared and executable-tested before its feature ships**
(`NFR-BOUND-001`), and a new mutation is admitted only with its safety contract, rehearsal model,
and live receipt proof (`FR-AUTH-004`).

---

## The interim contracts package, in practice

`Comfy.Quest.Contracts` keeps a fixed local version while its bytes evolve, so a warm NuGet
cache silently serves stale contracts after a repack. This is audit finding D6, and it is a
known open item rather than a solved one.

Until it is fixed, anything that consumes that package must restore against a cache keyed to the
package's own hash — set `NUGET_PACKAGES` to a directory named for the first 16 hex of the
nupkg's SHA-256, the way `tools/quest-studio/Start-QuestStudio.ps1` already does. The exact
sequence is in [`../README.md`](../README.md) under *Local verification*. CI is unaffected: it
restores clean, which is precisely why this stays invisible until it bites a developer.

Two toolchains coexist. Studio and its tests target net9.0 and need a .NET 9 SDK; the Quest Lab
tests target net8.0 and need the system install, because the side-by-side .NET 9 SDK carries no
8.0 runtime. Code lands first; refreshing the interim package is its own commit.

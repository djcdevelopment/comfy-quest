# Creator OS build strategy

Status: adopted execution order, 2026-08-24. Derived from
[`creator-os-audit-2026-08-24.md`](creator-os-audit-2026-08-24.md). All four sign-off items
were ruled on the same day — see Sign-off record below. Lane vocabulary is defined in
[`creator-os-phases.json`](creator-os-phases.json), which is the sole authority
(ADR [0013](adr/0013-one-numbering-authority-for-lane-vocabulary.md)).

## The conclusion this rests on

> **The remaining work is smaller than the roadmap makes it look, because the contract layer
> is consistently ahead of the runtime layer.**

Three verified examples:

- `QuestPackStore.InspectLane` already validates, hashes, and compiles *N* experience
  documents. Only `RuntimeCharmBinding.TryActive` refuses them (audit D1). Multi-experience
  guilds are one selector away, not a rebuild.
- Reset and rerun are wired end to end — endpoints, service, store, browser UI, file mailbox,
  Runtime poller (audit B8). Only live evidence is missing.
- The bounded request/receipt mailbox pattern already exists in three tested flavours
  (`creator-request.json`, `questlab-batch-request.json`, `run-control.json`) with identity
  pins, expiry, allowlists, and correlated receipts. New machine-owned operations are cheap to
  add and hard to get wrong.

So the strategy is not "build `4A / guild-scale-runtime`". It is: **stop the roadmap from misreporting itself, remove
the single check that blocks guild scale, make the evidence loop trustworthy, and resolve the
one dependency this repository cannot satisfy on its own.**

The transition being aimed at is deliberately narrow:

> untrustworthy roadmap + healthy implementation
> → **trustworthy roadmap + known implementation gaps**
> → implementation.

That separation is the point. Repairing the roadmap and repairing the implementation in the
same pass destroys the property that makes the first one worth doing.

---

## The program invariant

Audit C6 identified the structural cause behind C1, C2, and C4: work items have no
machine-enforced relationship to requirements or roadmap lanes. That is a standing rule, not a
one-time repair:

> **No executable roadmap item may exist without requirement/decision lineage, and no active
> requirement may exist without an explicit roadmap disposition.**

The goal is not to reconnect today's broken links. It is to make this class of drift
mechanically difficult to reintroduce. Translated into executable validation, the machinery
must detect all five of:

1. an active (non-deferred) requirement claimed by **no** work item;
2. a queue item referencing a **nonexistent** requirement id;
3. a queue item with **no lane/phase disposition**;
4. an active requirement deliberately unscheduled but lacking an explicit
   `parked` / `deferred` / equivalent **disposition**;
5. requirements **added to or removed from** the authoritative requirements document without
   a corresponding ledger change.

Condition 5 is the anti-reintroduction clause, and it is what makes the rest durable: because
the ledger stores the extracted id set, editing the requirements document without touching the
ledger fails the gate — exactly the way a `source_contains` pin already fails when prose moves.

**Mechanism** — built 2026-08-24 on machinery that already existed:

- `requirements: []` and a `lane` field on every queue item in
  `docs/quest-mission-control.json`;
- a tracked [`docs/creator-requirements-ledger.json`](creator-requirements-ledger.json) mapping
  all 47 ids to disposition, lane, and evidence;
- `validate_program_invariant` in `tools/render_quest_mission_control.py`, called from
  `validate_manifest`, so the checks run in the Python suite and therefore in CI.

**Authority runs one way**, and the ledger sits in the middle of it as a recorder:

> requirements → phase authority → ledger recording → queue realization → evidence

[`creator-os-phases.json`](creator-os-phases.json) is the phase and scheduling authority; it
alone decides which lane owns a requirement. Where it names one, the ledger mirrors it and
disagreeing fails the gate. Where it does not, the ledger may record a disposition but may
**not** invent a lane. Every lane the ledger names carries a `lane_authority` saying where it
came from — `phase-authority`, or `queue-realization` where a work item already scheduled in
that lane claims it — and neither source is the ledger. A future lane can only come from the
phase authority, so `deferred` may not read one off a queue item. `parked` and `met` carry no
lane at all, which is what stops a parked requirement from drifting toward its apparently
obvious destination.

`parked` has exactly one meaning: **the requirement is still recognized and relevant, but
scheduling or advancement needs a ruling nobody has made.** It therefore requires a
`pending_ruling` naming the missing decision. The ledger also records what `parked` is *not* —
future-but-decided work, externally blocked work, abandoned or superseded requirements,
implementation-complete-but-unproven work, and work merely outside the current lane — each with
the representation that already covers it. Without that list, `parked` becomes "not now", and
the ledger stops meaning anything.

`met` stays deliberately hard to earn: gated evidence at the requirement's own proof standard,
with no unresolved audit finding against it. Code existing is not evidence, and neither is an
end-to-end path that looks wired. `NFR-INTEGRITY-001` stays parked while D6 is open, and
`FR-RESET-001` / `FR-RESET-002` stay active in 4A even though the path is built, because 4A's
exit is what collects their proof.

A work item with no lane says so explicitly, with a note naming who must rule. `pre-lane` and
`unassigned` are **lane assignment states, not lanes**: they carry no scope, no exit, no
requirements, and no place in any order of work, and the validator refuses them anywhere a lane
id is expected. Readiness and lane assignment stay independent dimensions — `queue.workbench-boundary`
is `ready` **and** `unassigned` **and** annotated with the ruling it waits on, which is three
useful facts that a single `blocked` would have destroyed.

**Disposition vocabulary** — a closed, validated set, mirroring `ALLOWED_QUEUE_STATES`:
`active` (claimed by a queue item in a named lane) · `parked` (deliberately unscheduled, reason
required) · `deferred` (explicitly out of R&D, owning phase required) · `met` (evidence
reference required).

---

## Lane 0 — Make the roadmap tell the truth

**Status: complete, 2026-08-24.** Exit gate walked below, then reviewed and closed the
same day. The review changed no disposition and repaired no fenced finding; it tightened
what the vocabulary *means* so that later lanes consuming the ledger cannot widen it by
reading it loosely.

Repairs **authority surfaces only**: traceability, CI and drift machinery, ADR ownership, stale
documentation, and roadmap truthfulness. Establishes the program invariant as executable
validation.

### Scope fence

Lane 0 is not permission to fix implementation issues in files it happens to touch. Audit D2,
D5, and C5 are real, small, and tempting; they get **ledger entries and lane dispositions here,
and fixes later.** Mixing them in destroys the property that makes Lane 0 worth doing — that
afterwards the roadmap can be trusted *and the implementation gaps are known and named*, rather
than partially and invisibly repaired.

### Stale seat choreography (audit B7)

A stale seat procedure is the failure mode this program has already paid for three times, so it
gets specific treatment:

- mark `docs/runbooks/OMEN-LAP-PHASE3-EXIT-S3.md` **stale/blocked**, using the existing "Do not
  run it" convention;
- require **re-derivation against the current overhead-bar interaction** before it is run, per
  the `AGENTS.md` rule that choreography is verified by execution;
- ensure it **cannot be presented as seat-ready** — give `phase3_lap` an explicit state in the
  manifest and have the renderer refuse to render a stale lap as an actionable sequence;
- record it as **the concrete example** motivating freshness/supersession state in roadmap and
  runbook authority generally.

**Explicit non-goal:** do not build a generalized UI-testing framework. The lesson — syntactic
validation is insufficient for cold seat procedures — is recorded; the remedy here is freshness
state, not a new test harness.

### Lane 0 exit gate

A fresh agent entering *only* through the authoritative Creator OS surfaces must be able to
answer all ten of these without archaeology:

1. What is being built now?
2. Why is it next?
3. Which requirements does it satisfy?
4. What decision authorized its shape?
5. What is explicitly deferred?
6. What evidence proves the preceding capability exists?
7. What exact evidence will close the current lane?
8. Which actions require Derek?
9. Which operations are machine-owned?
10. What is the next executable task?

**If any answer still requires reconciling multiple contradictory documents by hand, Lane 0 is
not complete.** The deliverable is not more documentation — it is a self-consistent,
mechanically checked control plane.

### Lane 0 exit gate — walked 2026-08-24

Walked cold, through the authoritative surfaces only. Every answer resolves from one surface;
where a second is listed it agrees mechanically rather than by reading.

| # | Question | Answer | Surface |
| --- | --- | --- | --- |
| 1 | What is being built now? | Lane 1: a bounded `select_experience` selector | Lane 1 below; `queue.guild-runtime` (`ready`, lane `4A`) |
| 2 | Why is it next? | It is the single check blocking guild scale — the contract layer already compiles *N* experiences | "The conclusion this rests on" above; audit D1 |
| 3 | Which requirements does it satisfy? | `FR-RUN-001`, `FR-AUTH-005` | `queue.guild-runtime.requirements`; both `active` / `4A` in the ledger |
| 4 | What decision authorized its shape? | Lane 1 below, with explicit non-goals; vocabulary fixed by [ADR 0013](adr/0013-one-numbering-authority-for-lane-vocabulary.md) | this file; `queue.guild-runtime.source` |
| 5 | What is explicitly deferred? | 10 `deferred` and 10 `parked` requirements, each with an owning lane or a reason | [`creator-requirements-ledger.json`](creator-requirements-ledger.json) |
| 6 | What evidence proves the preceding capability exists? | The live Creator Session proof chain, plus 9 `met` requirements whose evidence paths are checked to exist | `creator-os-expected.json`; ledger `met` entries |
| 7 | What exact evidence will close the current lane? | Lane 4A's `exit` | [`creator-os-phases.json`](creator-os-phases.json); the requirements-document 4A exit now agrees |
| 8 | Which actions require Derek? | One launch and world entry per session, and nothing else | `human_boundary` in the vocabulary; [ADR 0014](adr/0014-one-human-launch-and-entry-is-the-baseline.md) |
| 9 | Which operations are machine-owned? | All ten Creator Session verbs, checked against the script's own `ValidateSet` | `commands` in the manifest; `not_permitted_under_this_allowance` |
| 10 | What is the next executable task? | `queue.guild-runtime` | the manifest queue |

Question 10 is the one that would have been ambiguous. Two items sit in state `ready`:
`queue.guild-runtime` and `queue.workbench-boundary`. The second now carries lane `unassigned`
with a `lane_note` saying so and naming who must rule on it, so "ready but not next" is a
recorded fact rather than something the reader has to infer. That is the shape of the whole
lane: gaps are named, not smoothed.

**Gate result: passed.** No answer required reconciling contradictory documents by hand.

### What Lane 0 changed, and what it deliberately did not

Applied: the 4A exit prose now matches [ADR 0014](adr/0014-one-human-launch-and-entry-is-the-baseline.md);
the program invariant is executable, with a tracked ledger of all 47 requirement ids, `lane`
and `requirements` on every queue item, five checks in `validate_manifest`, and a negative test
per check; the Phase-3 lap is stale in the manifest and in its runbook, and the renderer emits
no sequence for a stale lap; `cautions[7]` says what `environment[4]` says; the command
reference lists all ten verbs; the post-render replacement table pins its match counts and has
lost six dead groups; `machines[].state` and `environment[].state` are validated.

Not applied, on purpose: audit D2 (duplicated allowlist with different comparers), D4 (Creator
Session cannot drive run control), D5 (asymmetric schema acceptance), C5 (advisory
`MaxProjects`), D6 / punch item 16 (content-mutable interim package), punch item 18 (~20
`Get-FileHash` call sites CI never runs), and the ungated Playwright suite. Each carries a
ledger entry naming the finding. They are small and tempting, which is exactly why they were
left: the property worth having after Lane 0 is that the roadmap can be trusted *and* the
implementation gaps are known and named — not partially and invisibly repaired.


---

## Lane 1 — Unblock guild scale

The one product change that matters. Preferred shape, deliberately small:

- additive nullable `experience_id` on the active state — the same additive-field pattern
  `activation_id` already uses, and which `Rollback`'s schema check tolerates;
- a bounded `select_experience` operation on the existing run-control allowlist;
- update **both** known ambiguity/refusal sites (`RuntimeCharmBinding.cs:45`,
  `RuntimeExperienceEngine.cs:1094`);
- preserve backward compatibility for single-experience packs — an absent selector must behave
  exactly as today;
- build independent run state and prerequisite/unlock behaviour **on top of** the selector, not
  alongside it.

**Explicit non-goals.** This is a runtime-selection problem, not an architecture problem. Do
**not** introduce a new pack format (v2 already validates N documents), a new orchestration
system, or a broad Runtime rewrite. If implementation evidence proves one of those necessary,
stop and record the finding rather than expanding scope in flight.

---

## Lane 2 — Make the evidence loop trustworthy

Audit C3 is high severity on evidence-integrity grounds, which changes *why* this lane matters,
not where it sits.

Give `RuntimeReceiptStore` explicit retention with archive-or-export before deletion, and make
`RuntimeRunControlController.PruneReceipts` archive rather than discard — scoping its pruning
so one run's activity cannot evict another run's audit chain.

**Ordering consequence worth naming:** Lane 2 must land before Lane 4 collects the
`4A / guild-scale-runtime` evidence.
Otherwise the evidence that closes 4A is itself written to a store that can silently drop
members of a correlated proof set.

---

## Lane 3 — Resolve the autonomy boundary

### Lane 3a — The human boundary *(APPROVED 2026-08-24 — ADR 0014)*

**Exactly one human action is permitted per creator session: launching the game and entering
the pinned authoring world. Everything after entry is machine-owned.**

The allowance is **strictly scoped to launch and world entry**. It is not a generic "one human
intervention" budget — read that way it becomes an escape hatch for whichever automation is
missing that week. See [ADR 0014](adr/0014-one-human-launch-and-entry-is-the-baseline.md) for
the full non-permitted list.

Automating world entry is a **subsequent reduction of the human boundary**, not a prerequisite
for proving anything else. Lane 3a was independent of Lane 3b and did not wait on it.

### Lane 3b — Creator-world entry belongs to comfy-quest *(APPROVED 2026-08-24 — ADR 0012 accepted)*

Signed off with every constraint intact. The boundary is defined **semantically**, not as
"comfy-quest may launch Valheim":

> **Creator-session world entry** — placing the already-owned creator Runtime into a
> *specifically pinned local authoring world* as part of the Author → Validate → Play → Observe
> workflow. **This belongs to comfy-quest.**
>
> **Field-lab orchestration** — launching, acquiring, coordinating, connecting, or managing
> *arbitrary clients against external or server environments*. **This remains outside
> comfy-quest**, at the field-lab boundary.

That distinction is what makes the recommendation defensible rather than special pleading: the
existing sibling harness is a server-join lane, which is field-lab orchestration by this
definition, and the capability comfy-quest needs is not.

The implementation must remain narrow. Every constraint below is a requirement, not a guideline:

- named local world only;
- exact world match;
- refuse if absent or ambiguous;
- pinned world UID where available;
- existing machine / world / session identity checks;
- bounded expiry;
- single consumption;
- correlated receipt;
- no synthetic keyboard, mouse, or console input;
- no arbitrary server joining;
- no general-purpose client orchestration.

**Borrow the proven world-entry mechanism, not sibling-repository code or dependencies.**

**Capture this boundary in an ADR** so that `Launch` / world entry cannot gradually expand into
a second field-lab orchestration system. Without a recorded boundary, "just add a `+connect`
parameter" is a one-line change nobody will notice crossing the line.

**Do not create the three publishing lanes** across the sibling repositories for this. Revisit
only if a second consumer needs the server-join harness.

---

## Lane 4 — Drive the real `4A / guild-scale-runtime` exit and call the seat

Only after Lanes 1-3, and only with the evidence the readiness gate names.

---

## Explicitly off the critical path

Not to be pulled forward, and to be recorded with explicit `deferred` or `parked` dispositions
in the ledger so the next agent does not re-derive them:

- versioned `.db` / `.fwl` world packaging, inspection, installation, rollback (Phase 5);
- pattern notebooks, auto-generation, and other optional accelerators (4C);
- the parked Phase-3 combat-feel verdicts — batched, **and blocked pending re-derivation per
  audit B7**;
- named anchors, unless and until an authored slice needs spatial references.

---

## Sign-off record

Audit recommendations do not silently become adopted architectural decisions. Four required a
ruling, and all four were signed off on **2026-08-24**:

| Decision | Ruling | Recorded in |
| --- | --- | --- |
| Human-entry allowance | Approved — exactly one launch/world-entry action, strictly scoped | [ADR 0014](adr/0014-one-human-launch-and-entry-is-the-baseline.md) |
| 4A / 4B reconciliation | Approved — 4A is guild-scale capability, 4B is sustained campaign | [`creator-os-phases.json`](creator-os-phases.json) |
| Canonical phase vocabulary | Approved, plus **one numbering authority** | [ADR 0013](adr/0013-one-numbering-authority-for-lane-vocabulary.md) |
| Creator-world-entry ownership | Approved and accepted, all constraints intact | [ADR 0012](adr/0012-creator-session-world-entry-is-not-field-lab-orchestration.md) |

The four reinforce each other. 4A no longer waits on launch automation, so it can concentrate
on guild-scale runtime; 4B concentrates on sustained use; world-entry automation can later move
into comfy-quest without becoming a 4A gate; and field-lab stays cleanly outside the boundary.

Future changes to the meaning of a lane, the lane vocabulary, the human boundary, or a
repository ownership boundary need the same sign-off, and widening ADR 0012 or 0014 requires
superseding them.

---

## Verification

```
python tools/render_quest_mission_control.py --check
python -m unittest discover -s tests
python tools/assert_no_reach_in.py
```

- **Render/drift gate** must print `OK` after any ADR repointing or schema addition. A
  stale-pin failure means the ADR text lacks the quoted marker — fix the marker, not the check.
- **The five invariant checks** each get a negative test proving they fire: an orphaned
  requirement, a dangling requirement id, a lane-less queue item, an unscheduled requirement
  with no disposition, and a requirements-document edit with no ledger change. A check that
  cannot fail is decoration.
- **Lane 0 exit gate** — walk the ten questions against the authoritative surfaces only.
- **Lane vocabulary** — `docs/creator-os-phases.json` must parse, ids and slugs must be unique,
  and every requirement id it cites must exist in the requirements document. Prose that
  *redefines* a lane rather than referencing it should eventually fail a check.

Lanes 1-4 are not verified by this pass, by design. Their verification is the integration-first
ladder the roadmap already specifies, and it starts with the real Studio-to-Valheim journey
rather than with new unit tests.

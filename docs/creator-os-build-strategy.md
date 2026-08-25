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

**Mechanism**, built on machinery that already exists:

- add `requirements: []` and a `lane` field to queue items in
  `docs/quest-mission-control.json`;
- add a tracked `docs/creator-requirements-ledger.json` mapping each of the 47 ids to
  disposition, lane, and evidence;
- extend `validate_manifest` in `tools/render_quest_mission_control.py` with the five checks;
- run it in the existing drift gate, alongside `--check`.

**Disposition vocabulary** — a closed, validated set, mirroring `ALLOWED_QUEUE_STATES`:
`active` (claimed by a queue item in a named lane) · `parked` (deliberately unscheduled, reason
required) · `deferred` (explicitly out of R&D, owning phase required) · `met` (evidence
reference required).

---

## Lane 0 — Make the roadmap tell the truth

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

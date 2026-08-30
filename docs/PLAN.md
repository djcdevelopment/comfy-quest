# The plan

Quest Creator OS. Updated 2026-08-29.

**This is the plan of record.** Read it alone and you know the goal, where we are, and what
happens next. Everything else in `docs/` is detail this document points at; if this document and
a cited surface disagree, the surface wins and this document is stale.

---

## 1. What we are building, and why

A creator ecosystem for Valheim: Derek builds community-level configuration tools, a
community steward uses them to define a Guild's bounded creative language and progression,
creators turn that language into artifacts and campaigns, and players live inside the result.

> Say, drop, pickup, wait are individually unimpressive. `Say → within 10s Drop ×2 → Pickup →
> Equip → Consume → Heal` is an authored ritual. The ecosystem effect is people combining
> constructs neither the system designers nor the original pattern authors anticipated.
> **Design for composition, not for impressive primitives.**

The program is judged against the WeakAuras ecosystem ethos — small composable primitives,
portable artifacts, inspectability, immediate feedback, progressive complexity, community reuse —
with **one inversion: the machine absorbs the complexity WeakAuras historically forced onto the
human.**

Six product guardrails govern every phase. In full: `five-intent-program-plan.md` §*Program
ethos and guardrails*. In short:

1. **The primitive is small; the composition is powerful.**
2. **Capability enters the palette only from an observed authored need**, never because a hook
   exists.
3. **One artifact, attenuated** — beginner composes verbs, expert reads canonical JSON, agents
   use the same contract. Don't remove power; attenuate how much must be understood at once.
4. **Receipts are explanation, not logging** — beginner prose with drill-down to the expression.
5. **Multiplayer is a separate validation dimension**, not a content concern.
6. **AI composes against the same contract as humans**, never as its own layer.

One communication guardrail governs how those results are reported: **answer at the reporter's
altitude.**

**The named danger is not missing capability. It is exposing too much of it too quickly.**

**What this repository does not hold:** the five source design intents live in the *baseline*
repository. The plan cites their immutable published revision and does not restate them. Read the
source, or say plainly you did not.

<!-- source-intents:begin -->
Pinned source authority: [`djcdevelopment/baseline@ed45c98fd5dfe8f92214fd67180937dc10163fee`](https://github.com/djcdevelopment/baseline/commit/ed45c98fd5dfe8f92214fd67180937dc10163fee).

| Intent | Immutable source | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| 01 | [Arcane Sight observability](https://github.com/djcdevelopment/baseline/blob/ed45c98fd5dfe8f92214fd67180937dc10163fee/docs/arch/01_arcane_sight_runtime_observability.md) | 2771 | `025eddf14cec353b476938e682e75519629d208fd3040c54da55f7c7c163d610` |
| 02 | [Quest Lab apprenticeship](https://github.com/djcdevelopment/baseline/blob/ed45c98fd5dfe8f92214fd67180937dc10163fee/docs/arch/02_quest_lab_apprenticeship_spellbook.md) | 3089 | `8a6abcb6aa4e6a1b20aaf622f779773b9d628acf91f574714f42c52321c11ea5` |
| 03 | [Studio-Live closed loop](https://github.com/djcdevelopment/baseline/blob/ed45c98fd5dfe8f92214fd67180937dc10163fee/docs/arch/03_studio_live_valheim_creator_loop.md) | 3176 | `34ea44ce4009a534ecd212d78af83aed4c18cab96fdd3c39d2fdf358e6c328b4` |
| 04 | [Community artifact ecosystem](https://github.com/djcdevelopment/baseline/blob/ed45c98fd5dfe8f92214fd67180937dc10163fee/docs/arch/04_community_artifact_ecosystem.md) | 3481 | `84787fa8b99a978d6be0388b929f363c815f5da63047963f4949bc182761f807` |
| 05 | [Adaptive event semantics](https://github.com/djcdevelopment/baseline/blob/ed45c98fd5dfe8f92214fd67180937dc10163fee/docs/arch/05_adaptive_event_semantics.md) | 4015 | `6fafa4e79fed6af7dcac016c73670e2e580a0cd69cfabf85731ff805f90a7075` |
<!-- source-intents:end -->

**And the constraint behind the whole shape of this:** Derek runs roughly a dozen efforts in
parallel. Seat time is a context switch, not minutes. The machine builds, drives, observes,
captures and proves; a person is asked for judgment that only a person can give, once, in one
contiguous block.

## 2. What "done" looks like

Four questions. Each is a lane; each fails one specific way. Cite a lane as
`4A / guild-scale-runtime`, never as "Phase 4".

| Lane | The question | Fails if |
| --- | --- | --- |
| **4A** guild-scale-runtime | Can Creator OS operate a guild? | The architecture cannot express or execute a guild |
| **4B** sustained-creator-campaign | Can a steward configure a Guild that creators use to make distinct player experiences over time? | Guild remains a creator-owned pack, the abstraction yields one disguised copy, or sustained use degrades |
| **4C** optional-acceleration | Which repetitions have earned tooling? | Tooling is built for imagined rather than observed need |
| **5** distribution-release | Can someone else receive, inspect, and run what was built? | The work is only runnable on the machine that made it |

If a piece of work does not move one of those questions, it is not on the path — however green
its gates are. Definitions are in `creator-os-phases.json`, which is the only place a lane may be
defined.

**The human boundary, for all of it:** at most one human action per creator session — launching
the game and entering the pinned authoring world. Everything after entry is machine-owned. The
installed 4A driver now defaults to spending zero actions through bounded exact-world entry; the
ADR 0014 allowance remains a fallback ceiling, not a quota to consume and not a generic "one
intervention" budget.

## 3. Where we are

The conclusion the whole plan rests on, and it is good news:

> **The remaining work is smaller than the roadmap makes it look, because the contract layer is
> consistently ahead of the runtime layer.**

The 4A machine gate is complete. Installed session
`queue-full-width-journey-20260827-r9` exercised the real product and promoted the work that had
previously been implementation-only.

The first 4B opening is no longer merely staged. Derek played **The Name in Ash** through its
sign, Resin offering, and Greyling trial to `pilgrim-choice`. His product signal was "pretty
inspiring"; his machine signal was a distracting hard freeze about every three seconds. ERA17
session r13 restored and proved the world-specific NetworkSense connection caches that the
earlier Quest-only cleanup had removed, then parked the 1.30 GB world for batched acceptance and
scale questions. Routine mechanics now run first on the 3.1 MB `ComfyQuestDemo` world. The
retained AM4 lap replayed the reviewed sign fixture, bound the same guild 0.1.3 opening, and
proved reset-successor startup plus exact retry without using Derek's OMEN seat. The fifth
provider tool then executed the entire reviewed Lab precondition as one bounded call: first run
built and matched 12 pieces, immediate retry was a no-op exact match, a live clear exposed and
corrected deferred Valheim object retirement, and the provider repeated the build/no-op pair.
Runtime finished with build authority disabled, and AM4 saved and stopped cleanly; the session is
ready on demand rather than consuming a foreground seat. The final combined Studio browser gate
also exposed an order-dependent atomic heartbeat read at reset/restore. Studio now retries only a
bounded filesystem replacement window while stale, invalid, corrupt, or exhausted status still
fails before mailbox dispatch; both journeys pass together.

That evidence proves the creator-to-Runtime loop, not the corrected product hierarchy. The next
slice is **Slayers Signature Hunt**: a community steward owns Guild configuration, palette, and
progression; a creator must make two meaningfully distinct hunt artifacts from one typed
abstraction and arrange them in a campaign; the installed player run must then produce
correlated live evidence.
The steward/configuration, typed-abstraction, two-instance, campaign, evidence-lens, and
one-operation campaign-start surfaces are implemented and contract-tested. The release-seed lane
has now driven the same exact content through installed Valheim on a fresh `CreatorOSBeta1` world:
the 40-piece Field Lodge returned `MATCH`, all 17 fixture objects stood, Air Drop was activated,
bound to the exact sign, and started, the world saved gracefully, and AM4 byte-restored. That is
installed start-boundary evidence, not live kills, successor continuation, or campaign completion.
This remains the narrow 4B -> 4C bridge, not authorization for a generic abstraction framework.

| What | State | What is actually missing |
| --- | --- | --- |
| Portfolio hierarchy | complete for the 4A creator-owned Guild journey | installed proof of the newer steward configuration, abstraction lineage, creator artifacts, and campaign children |
| Guild creative-system control plane | implemented and contract-tested | installed execution of the steward -> abstraction -> two instances -> campaign lineage |
| Signature Hunt start boundary | implemented and simulated through Studio; installed-proven through the release-seed path on world UID `4257656027` with exact venue, fixture, activation, binding, and run identity | external player install/join, live kills, successor transition, campaign completion, reset/rerun, and cleanup/restoration evidence |
| CreatorOSBeta1 saved-world release | fresh world pair and content-addressed candidate implemented; graceful save and producer-host rollback proven | clean frozen-source cut, P7 activation, cold boot, and external install/restore proof |
| Scoped reset / rerun | installed-proven, including successor start and idempotent confirmation retry | human clarity and hundredth-use judgment in 4B |
| Guild-scale runtime selector | installed-proven | real authored campaign breadth in 4B |
| Receipt retention | installed-proven across its per-run bound | sustained campaign volume in 4B |

Lanes of the repair program: **0 closed** (the roadmap tells the truth and is machine-checked),
**1 implemented** (multi-experience selector), **2 implemented** (evidence retention), **3 ruled
and recorded** (ADR 0012, 0014), and **4 completed** by the installed game journey. Laps r6-r8
did what integration-first proof is supposed to do: they found candidate-selection drift, a
preview/apply endpoint error, and a fresh-run status race before Derek entered the seat. Each was
fixed, regression-covered, and rerun. r9 then passed the full real Studio -> artifact -> Runtime
-> installed Valheim -> evidence -> recovery chain with no human action.

The authoritative 4A proof index is
[`docs/evidence/queue-full-width-journey-20260827-r9.json`](evidence/queue-full-width-journey-20260827-r9.json).
The full capture bundle remains at
`C:\work\comfy-quest\captures\queue.full-width-journey\queue-full-width-journey-20260827-r9`.
The current AM4 mechanics proof is indexed at
[`docs/evidence/quest-am4-fastlane-20260828-r1.json`](evidence/quest-am4-fastlane-20260828-r1.json).
There is no remaining mechanical Quest task that should be discovered by putting Derek in-game;
visible machine-only client work belongs on AM4 while OMEN remains released.

## 4. What happens next, in order

### Completed ruthless slice — architectural capsule → Studio Build → Quest Lab staging

The baseline `tn0304` envelope now exports an immutable, SHA-256-pinned
`creator-os-architectural-build-capsule/v0`. Studio imports it into the separate Build
workspace, shows the solved architectural intent beside the canonical 40-piece game
adaptation, permits only `x/y/z/yaw` placement intent, and stages the exact capture/blueprint
pair. The acceptance receipt must retain the `7.953375 × 7.4676 m` footprint, `2.2225 m`
wall datum, `5.8166 m` ridge, `43.907838°` pitch, `−0.029171 m` reconciliation, and
`16/16/8` piece split. AM4 accepted capsule
`f509aa2a201fdb3495c0f8aa3656ca156421524b476d12d4f0d45aa3cd9a21e9` and staged capture
`5d466cdaa5a213ef958d07636325b9398eee9a74584019da8dc16a5603654251` plus blueprint
`02201382e57635f4e945229836443d2fdbf75e243973281d1f9806cd770ece5f` while reusing the same
running Valheim process and leaving world/character bytes unchanged. Only append-only Lab event
archives were permitted to advance; Creator Session state, mailboxes, and every other control
file remained strict. The immutable evidence index is
`docs/evidence/architectural-build-tn0304-20260829-r1.json`. This stops before Creator Session,
mailbox, check/build/diff/clear, or world mutation; the following attack consumes this exact
staged pair and saved placement intent.

### Completed live proof — exact staged pair → apply → diff → clear → restore

The unattended AM4 harness consumed that exact pair without regeneration. It entered the pinned
`ComfyQuestDemo` world as `questyfour`, passed Lab check, placed all 40 pieces at the Studio-saved
`x=12.5, y=1.25, z=-3.75, yaw=22.5` transform, observed the exact `16/16/8` prefab split, and
returned `MATCH` for 40 selected pieces with zero missing and zero extra. It then cleared all 40
marked pieces, disabled creator build mode, stopped Valheim gracefully, and restored the exact
prior Lab plugin, runtime, world, and character bytes. The canonical staged capture and blueprint
remain on AM4 for the next deliberate creator lap. The immutable evidence index is
`docs/evidence/architectural-live-tn0304-20260829-r1.json`; a retained world build remains gated on
human spatial and aesthetic review of the saved placement.

For continuing R&D, AM4 now uses a warm-lap contract rather than replaying that disposable
acceptance transaction. The first warm lap installed the pinned Lab candidate and created the
exact build once. The second lap reattached to the running client, reused the same 40 marked
pieces, and ran only `status → check → count → diff → status`; it did not deploy, enter, build,
clear, stop, or restore. Valheim remains running, the proved structure remains standing, and
creator build mode is off. Identity drift fails closed. Full rollback is retained as the explicit
`Invoke-ArchitecturalWarmLap.ps1 -Close` operation, not paid on every loop. The active-state
evidence index is `docs/evidence/architectural-warm-tn0304-20260829-r1.json`.

The operator-facing closeout is now one bounded command:
`tools\quest-studio\Invoke-ArchitecturalDemo.ps1 -Action Open`. It verifies the accepted source
and AM4 identities, runs only `status → check → count → diff → status`, reuses the existing
40-piece build, idempotently reopens the canonical Studio build and stage, and opens the direct
Architecture workspace alongside an exact 1920×1080 Valheim-window capture. Two consecutive
final laps proved both the standalone Studio host and SSH tunnel were reused. Neither lap launched
the game, opened a Creator Session, wrote a mailbox, built or cleared pieces, stopped Valheim, or
mutated/restored world state. The immutable operator-demo index is
`docs/evidence/architectural-demo-tn0304-20260829-r1.json`. `-Action StopStudio` stops only Studio
and the tunnel; teardown remains the separate explicit `Invoke-ArchitecturalWarmLap.ps1 -Close`.

### Active ruthless slice — accepted venue → Guild composition → community evidence

The next integration surface is the living
[`Creator OS composition workbook`](creator-os-composition-workbook.html). It accepts the
`tn0304` architectural values, canonical 40-piece adaptation, live MATCH, rollback, and warm
reuse as settled inputs. It drives the connection that is still missing: promote the structure
as a steward-owned Guild venue primitive, let a creator make the two-artifact **Slayers Signature
Hunt** inhabit it, activate the exact venue/campaign composition, and return installed player
evidence through campaign, artifact, primitive, and capsule lineage.

The workbook's eight human actions ask only for meaning, steward authority, creator freedom,
composition quality, and the next R&D verdict. Screenshot notes and exact observations stay in
the browser until an explicit bounded `creator-os-composition-review/v1` export. Checkmarks and
notes are not receipts. Mechanical preparation, identity proof, world mutation, cleanup, and
evidence collection remain machine-owned.

### Step 1 — Completed: installed vertical slice `queue.full-width-journey`

The first step that cannot be taken without Valheim installed. Its authoritative journey is
defined under 4A in `creator-os-phases.json` and projected here:

<!-- 4a-journey:begin -->
**Precondition:** One operator owns <Valheim>/BepInEx/ for the lap. NFR-SEAT-003 is satisfied before Derek is called: exact artifacts are installed, every applicable machine step has been driven, evidence and recovery are staged, and mechanical failures have been fixed and rerun.

1. **publish-guild-pack** — Drive the real Studio GUI to author and publish one guild pack containing experiences A and B, with B locked behind completion of A.
2. **locked-b-refusal** — Attempt B before completing A and retain the fail-closed prerequisite receipt.
3. **run-a** — Select A, run it to completion, and retain A's first run identity and correlated evidence.
4. **run-b** — Confirm A's completion unlocks B, then select and advance B under its own run identity.
5. **return-a** — Select A again and prove the accepted A -> B -> A sequence preserved independent state for both experiences.
6. **reset-a** — Preview and apply a scoped reset to completed experience A only.
7. **verify-b-unchanged** — Prove B's run identity and progress are unchanged by A's reset.
8. **rerun-a** — Rerun A under a new successor run identity linked to the reset and predecessor run.
9. **retention-boundary** — Use bounded automation to cross the configured per-scope receipt-retention bound by one, then prove both experiences' correlated evidence remains retrievable across live and archived storage.

**Required correlated evidence:**
- **artifact-identity** — Guild, pack, immutable revision, activation, installed content hash, machine, and world identity.
- **experience-state** — Experience A and B selectors, prerequisite refusal and unlock, independent progress, and B-unchanged-after-A-reset proof.
- **run-lineage** — A's original run, reset, predecessor link, successor run, and B's unaffected run identity.
- **runtime-correlation** — Accepted and rejected transition/action receipts correlated to activation, experience, run, reset, and world.
- **retention-proof** — Exact receipt bytes remain retrievable across live and archive stores after the per-scope boundary is crossed; no activity in A breaks B's chain.
<!-- 4a-journey:end -->

- **Claims:** `FR-LOOP-001`, `NFR-SEAT-001`, `NFR-SEAT-003`, `NFR-TEST-001`
- **Human cost:** zero by default. `-HumanWorldEntry` retains the one-action ADR fallback while
  the default autonomous path is available.
- **Passing evidence:** r9 authored and production-published A plus prerequisite-locked B through
  the real Studio GUI; launched Steam into the exact profile/world; replayed the reviewed 12-piece
  fixture to 12/12 `MATCH`; bound its nearby sign; proved locked-B refusal and A -> B -> A under
  independent runs; selectively reset and completed a linked successor A; crossed the 32-receipt
  bound by one without breaking B's chain; restored four bindings LIFO; stopped gracefully; and
  restored the exact `.db`, `.fwl`, and `.fch` hashes. Studio stderr and game error-signature
  counts are zero, as are remaining plug-in files and Valheim processes.
- **Evidence:** the committed index above pins the local proof bundle, identities, run lineage,
  cleanup verdict, trace, logs, and key artifact hashes.
- **Result:** 4A / guild-scale-runtime is complete. The technical lap used generated A/B content,
  so it deliberately does not claim human acceptance of authorship or play feel.

### Step 2 — Retained precursor: machine-lock the first creator-authored campaign

The 4A lap is technical integration evidence; it does not manufacture Derek's creative answer.
The first 4B sitting has already supplied more than the old plan recorded: Derek selected premise
C, **Pilgrimage of Ash and Ice**, then played its five-stage opening through `pilgrim-choice`.
That sitting produced one creative verdict and one mechanical defect. The experience was otherwise
"pretty inspiring"; the recurring stall was distracting.

The machine-owned sequence before another sitting is now:

1. keep ordinary mechanics, reset/retry, provider, and Studio correlation work on
   `ComfyQuestDemo` / `questyfour` using the unattended AM4 client;
2. require reviewed fixture `MATCH`, exact binding/run identity, an automatically started reset
   successor, and idempotent retry evidence before promoting immutable content bytes;
3. keep ERA17 parked until the remaining question actually depends on its topology, scale,
   spatial pacing, or human perception; and
4. when that question is ready, stage one pinned ERA17 batch with world support, evidence, and
   rollback before asking Derek to enter OMEN.

Derek's remaining contribution is one bounded set of judgments:

1. choose what the pilgrim carries at `pilgrim-choice`: Resin for memory or Stone for oath;
2. say whether the recurring distraction is gone on the supported ERA17 build;
3. judge which in-world compositions and cues read clearly and which do not;
4. judge whether each authored beat responds and explains itself at player altitude; and
5. say what should be kept, revised, or rejected before the next cycle.

Using Studio to express those choices and playing the resulting content are product use, not KVM
work. Launch, exact world/profile selection, deployment, navigation performed only for setup,
retries, screenshots, receipts, logs, reset/rerun plumbing, shutdown, and recovery remain
machine-owned. Derek is not asked to enter the game until that seat packet is staged.

The exact progression and its remaining uncertainty are recorded in
[`docs/evidence/era17-pilgrimage-20260827-r12.json`](evidence/era17-pilgrimage-20260827-r12.json)
and the world-support correction in
[`docs/evidence/era17-pilgrimage-20260827-r13.json`](evidence/era17-pilgrimage-20260827-r13.json).
The AM4 mechanics lock is recorded separately because immutable content may move between worlds,
but binding, run, receipt, and saved-world state may not be equated across them.

### Step 3 — Installed start boundary; awaiting completion lap: the 4B Guild creative-system bridge `queue.guild-campaign`

Build **Slayers Signature Hunt** through one full visible edge:

1. a community steward configures one typed Signature Hunt abstraction and Guild progression;
2. a creator makes two meaningfully distinct hunt artifacts from it;
3. the creator arranges both in one campaign; and
4. the campaign crosses Studio, Runtime, installed Valheim, and correlated live evidence.

Preserve triggerless and manual Slayers source rows without inventing completion behavior. Record
the abstraction, both instances, campaign, missing evidence, tooling changes, and eventual rerun
result in one context. Stop at this typed slice: no generic schema framework, pattern registry,
notebook, generator, rank engine, Discord workflow, or world package.

The implementation now connects those authoring surfaces to one bounded **Play campaign**
operation for the exact two-instance Signature Hunt shape. It validates and freezes campaign
bytes before mutation, requires one unambiguous prerequisite root, prepares or resumes the pinned
Creator Session, enters ComfyQuestDemo, arms Runtime, obtains a `fixture-preparation` receipt for
the fixed Quest Lab hunt, publishes and verifies the exact activation, finds the fixture-owned
sign in Runtime's bounded candidates, and atomically binds and starts the first experience. The
stored `comfy-quest-studio-campaign-play/v1` receipt stops at state `started` and declares that
fixture preparation is not kill/completion proof and bind/start is not campaign-completion proof.

The simulated path closes its declared identity chain before that `started` claim. It verifies
the exact fixture request, schema, id, revision, world, 17/17-object receipt hash/path, raw
Deathsquito/Drake targets, and matcher targets against the two compiled instances; preserves the
prerequisite machine, world, and Creator Session pins through candidate and bind dispatch; requires
the compiled entry's explicit successor to be the other experience; and checks the full applied
binding reference. Campaign progression is editable on every campaign, and successor emission
follows explicit prerequisite edges. Those are implemented and simulated contract facts, not
installed evidence by themselves.

The release-seed pass then crossed installed Valheim on fresh world UID `4257656027`. It placed and
exact-diffed the 40-piece Field Lodge, prepared all 17 Signature Hunt objects, captured the raw
Deathsquito and Drake identities, activated content hash `ad94f708efa9...`, bound the exact loadout
sign, started Air Drop, disabled creator authority, saved the world gracefully, and restored AM4.
The retained index is
[`docs/evidence/creatoros-beta1-world-20260830-r1.json`](evidence/creatoros-beta1-world-20260830-r1.json).

The next R&D move is a clean external install and native P7 join, followed by both live kills,
automatic Cold Shot continuation, terminal campaign evidence, reset/rerun, and owned cleanup.
Until that chain exists, `queue.guild-campaign` is `implemented`, not complete.

- **Claims:** `FR-PORT-001`, `FR-PORT-004`, `FR-PORT-005`, `FR-EVID-001`, `FR-EVID-002`,
  `FR-EVID-003`, `FR-OPT-001`, `NFR-USE-001`

### Then, and not before

**4C** may generalize only what the two Signature Hunt instances and later campaign work actually
repeat; `FR-OPT-002` generation remains there. **5** is release and distribution. Generic reuse
machinery and distribution are explicitly out of scope until the connected slice produces live
evidence, because both are the kind of work that feels productive while proving nothing.

## 5. What needs Derek, and nothing else can

**Ten requirements are parked.** Parked has exactly one meaning here: still recognized and
relevant, but scheduling needs a ruling nobody has made. Every one names the missing decision in
`pending_ruling` in `creator-requirements-ledger.json`. An agent may not resolve one by picking
the obvious lane — the ledger is built so it cannot.

| Requirement | The ruling that is missing |
| --- | --- |
| `NFR-MCP-001` | Which lane, if any, owns the standalone Workbench boundary (audit C4) |
| `NFR-INTEGRITY-001` | Whether punch item 16 is scheduled — a hash-keyed package cache for build and test, or a version bump on every repack (D6) |
| `NFR-BOUND-001` | Whether declared bounds become executable-tested on every path that can breach them (C5) |
| `FR-LOOP-003` | Which lane owns clean-versus-resume as a pre-play choice (C2) |
| `FR-RUN-002` | Which lane owns authority and participant scope (C2) |
| `FR-LOOP-002` | Whether Creator Session gains run-control verbs (D4) |
| `FR-AUTH-001` | Whether the v2-on-read / v3-on-import asymmetry is repaired (D5) |
| `FR-WORLD-001/002` | Whether the authored slice needs spatial references at all |
| `NFR-TEST-002` | Whether evidence-over-counts earns an executable gate |

Outside those rulings, Derek builds the community-level tool and is needed only for product
judgment that cannot be delegated; he does not silently stand in for the Slayers steward, creator,
and player as one collapsed persona. No person owes an in-game mechanical step while a seat packet
is being staged. The machine driver owns the game install, lifecycle, evidence, and recovery.

## 6. What we are deliberately not doing

Recorded so nobody re-derives them as new ideas, and so nobody quietly fixes them mid-task:

| | Why it is parked |
| --- | --- |
| Additional saved-world packaging | CreatorOSBeta1 is the deadline-driven exception: one fresh, content-addressed pair is implemented for the Valheim 1.0 beta window. Do not generalize it into a world-packaging framework. |
| Named anchors | Only if an authored slice needs spatial references |
| Generic pattern notebooks / generation | 4C, and only after the typed Signature Hunt bridge produces observed repetition; the one typed abstraction is the narrow active exception |
| Audit D2, D4, D5, C5 | Real, small, tempting. Each has a ledger entry; none gets fixed as collateral |
| Punch items 16 and 18 | The interim package and ~20 `Get-FileHash` sites CI never runs |
| Playwright E2E in CI | Still ungated |
| General client/server orchestration | Outside Quest. Its local-world contract accepts no server address, console command, input, or arbitrary launch argument (ADR 0012) |

The rule that makes this list worth keeping: **fixing one of these while doing something else is
the problem, not fixing it at all.**

## 7. How this document stays true

Prose rots. These do not:

- `quest-mission-control.json` — every work item with its lane and requirement lineage.
- `creator-requirements-ledger.json` — all 47 requirements, each with a disposition, and each
  lane pointing at the authority that granted it. The ledger records; it cannot schedule.
- `creator-os-phases.json` — the only definition of a lane, and the human boundary.
- The drift gate runs all of it in CI. Adding a requirement without a ledger entry fails. A work
  item with no lane fails. An active requirement no work item claims fails.

**Check your checkout before trusting any of this.** On 2026-08-25 the primary checkout sat nine
commits behind `origin/main` while work was being pushed, so an agent spawned there would have
read a plan that stopped being true nine commits earlier and had no way to notice:

```
git -C <repo> fetch && git -C <repo> status -sb
```

Further reading is declared once in `quest-mission-control.json` and projected here:

<!-- reading-order:begin -->
1. `docs/PLAN.md` — **The plan.** Goal, where we are, what happens next, and what needs Derek. Read this alone and you know the program.
2. `docs/handoff-2026-08-24.md` — **Session record.** What the 2026-08-24/25 sessions did, the environment traps, and the cold-start checks.
3. `docs/five-intent-program-plan.md` — **Why this exists.** The ethos the whole program is judged against: six product guardrails and one communication guardrail.
4. `docs/creator-os-build-strategy.md` — **The plan.** Lane order, the program invariant, and what each lane may not do.
5. `docs/creator-os-phases.json` — **Lane vocabulary.** The only definition of a lane, and the human boundary.
6. `docs/creator-requirements-ledger.json` — **Requirement dispositions.** Who is accountable for each of the 47 requirements right now.
7. `docs/quest-mission-control.json` — **Work queue.** Every work item with its lane and requirement lineage.
8. `docs/creator-os.md` — **Operating strategy.** Why the loop is shaped this way, and the golden rule.
9. `docs/creator-portfolio-requirements.md` — **What is required.** The 47 FR-/NFR- requirements themselves.
10. `docs/adr/README.md` — **Decisions.** Append-only; a reversal needs a superseding record.
11. `docs/creator-os-audit-2026-08-24.md` — **Findings.** What was wrong on 2026-08-24, with `file:line` receipts.
<!-- reading-order:end -->

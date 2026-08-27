# The plan

Quest Creator OS. Updated 2026-08-27.

**This is the plan of record.** Read it alone and you know the goal, where we are, and what
happens next. Everything else in `docs/` is detail this document points at; if this document and
a cited surface disagree, the surface wins and this document is stale.

---

## 1. What we are building, and why

A creator system for authoring Valheim quests and guilds — where a person composes small,
unimpressive primitives into something nobody designed for them.

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
| **4B** sustained-creator-campaign | Can I actually use it to build a guild over time? | It works once and degrades, or authoring it is intolerable |
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

Four things are **built and unproven.** `implemented` means the code exists and is
contract-tested, and **no installed game has ever exercised it.** That is not a synonym for done,
and reporting it as done is the specific failure this program has already paid for.

| What | State | What is actually missing |
| --- | --- | --- |
| Portfolio hierarchy | implemented | live evidence |
| Scoped reset / rerun | implemented | live evidence |
| Guild-scale runtime selector | implemented | live evidence |
| Receipt retention | implemented | live evidence |

Lanes of the repair program: **0 closed** (the roadmap tells the truth and is machine-checked),
**1 implemented** (multi-experience selector), **2 implemented** (evidence retention), **3 ruled
and recorded** (ADR 0012, 0014). **Lane 4 is active on the installed game.** Its autonomous
launch, exact world entry, nearby reviewed binding fixture, and byte-exact cleanup preflight are
live-proven. Installed lap `queue-full-width-journey-20260827-r6` then reached the real candidate
selector and exposed a Studio re-render replacing the chosen sign with the nearest wood pole;
the rejected Runtime receipt pinned both ZDO and `binding_target_incompatible`. Studio now
preserves the exact candidate across polling, with a browser regression that re-renders between
selection and bind. The full corrected guild journey remains the gate.

Nothing is in flight anywhere else: no open PRs, one branch on the remote, and the other agent's
worktree (`scanner-slice1`) is fully merged and idle since 2026-08-20.

## 4. What happens next, in order

### Step 1 — Drive the installed vertical slice `queue.full-width-journey`

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
- **Current live evidence:** `binding-fixture-smoke-20260827-r5` entered the exact pinned local
  world, replayed the reviewed 12-piece fixture to a 12/12 `MATCH`, exposed its sign 5.6 metres
  away through Runtime's bounded candidates, stopped gracefully, and restored the exact world
  pair, character profile, and empty prior plugin install. Installed lap
  `queue-full-width-journey-20260827-r6` reached binding, proved that a polling re-render had sent
  the nearest wood pole instead of the selected sign, and cleaned up byte-exactly. That Studio
  selection defect is repaired and regression-covered; the complete browser-to-A -> B -> A
  journey still needs its corrected rerun, and no seat is requested before it passes.
- **Before it starts:** agree who owns `<Valheim>/BepInEx/` for the lap's duration. A worktree
  isolates the repository, not the game install; another agent has clobbered a live inbox and
  deployed plugin DLLs mid-lap, twice.
- **Before Derek is called at all** (`NFR-SEAT-003`): automation has driven every applicable
  step, collected the screenshots/logs/receipts, and **fixed and rerun the mechanical failures.**
  What is left must be one bounded batch of human judgments.
- **Done when:** the four implemented-unproven rows above have live receipts, and the 4A exit in
  `creator-os-phases.json` is satisfied end to end.

### Step 2 — Prepare the 4B creative sitting

The 4A lap is technical integration evidence and does not manufacture a reason to put Derek in
the game. His next contiguous session starts from a prepared guild premise and contains only
authorship and perception that a machine cannot provide: composition, narrative tone, clarity,
responsiveness, and play feel. Any launch, navigation, retry, log, or proof request that reaches
him in that session is a defect with a receipt.

### Step 3 — 4B, the sustained campaign `queue.guild-campaign`

Author a top-to-bottom guild for real. This is where the portfolio is tested at density and where
friction gets recorded in context rather than remembered.

- **Claims:** `FR-PORT-001`, `FR-PORT-004`, `FR-PORT-005`, `FR-EVID-001`, `FR-EVID-002`,
  `NFR-USE-001`

### Then, and not before

**4C** promotes tooling only for repetitions the campaign actually produced. **5** is release and
distribution. Both are explicitly out of scope until 4A and 4B have happened, because both are
the kind of work that feels productive while proving nothing.

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

Outside those rulings, Derek is needed for the prepared 4B guild's authorship and play-feel
judgments. The machine driver owns the game install for each technical lap.

## 6. What we are deliberately not doing

Recorded so nobody re-derives them as new ideas, and so nobody quietly fixes them mid-task:

| | Why it is parked |
| --- | --- |
| Saved-world `.db`/`.fwl` packaging | Lane 5. Ordinary recoverable copies are enough for R&D (ADR 0010) |
| Named anchors | Only if an authored slice needs spatial references |
| Pattern notebooks / generation | 4C, and only for observed repetition |
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

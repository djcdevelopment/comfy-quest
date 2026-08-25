# The plan

Quest Creator OS. Updated 2026-08-25.

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

Six rules govern every phase. In full: `five-intent-program-plan.md` §*Program ethos and
guardrails*. In short:

1. **The primitive is small; the composition is powerful.**
2. **Capability enters the palette only from an observed authored need**, never because a hook
   exists.
3. **One artifact, attenuated** — beginner composes verbs, expert reads canonical JSON, agents
   use the same contract. Don't remove power; attenuate how much must be understood at once.
4. **Receipts are explanation, not logging** — beginner prose with drill-down to the expression.
5. **Multiplayer is a separate validation dimension**, not a content concern.
6. **AI composes against the same contract as humans**, never as its own layer.

**The named danger is not missing capability. It is exposing too much of it too quickly.**

**What this repository does not hold:** the five source design intents live in the *baseline*
repository at `docs/arch/01..05_*.md` — Arcane Sight observability, Quest Lab apprenticeship,
Studio↔Live closed loop, community artifact ecosystem, adaptive event semantics. The plan cites
them and does not restate them. Read the source, or say plainly you did not.

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

**The human boundary, for all of it:** exactly one human action per creator session — launching
the game and entering the pinned authoring world. Everything after entry is machine-owned. Not a
generic "one intervention" budget (ADR 0014).

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
and recorded** (ADR 0012, 0014). **Lane 4 is next and needs the installed game.**

Nothing is in flight anywhere else: no open PRs, one branch on the remote, and the other agent's
worktree (`scanner-slice1`) is fully merged and idle since 2026-08-20.

## 4. What happens next, in order

### Step 1 — Drive the installed vertical slice `queue.full-width-journey`

The first step that cannot be taken without Valheim installed. Automation drives the real Studio
GUI through authoring and publication, publishes a multi-experience guild pack, selects one
experience, plays it, resets it, reruns it under a new run identity, and keeps the correlated
evidence.

- **Claims:** `FR-LOOP-001`, `NFR-SEAT-001`, `NFR-SEAT-003`, `NFR-TEST-001`
- **Human cost:** one launch and world entry. Nothing else.
- **Before it starts:** agree who owns `<Valheim>/BepInEx/` for the lap's duration. A worktree
  isolates the repository, not the game install; another agent has clobbered a live inbox and
  deployed plugin DLLs mid-lap, twice.
- **Before Derek is called at all** (`NFR-SEAT-003`): automation has driven every applicable
  step, collected the screenshots/logs/receipts, and **fixed and rerun the mechanical failures.**
  What is left must be one bounded batch of human judgments.
- **Done when:** the four implemented-unproven rows above have live receipts, and the 4A exit in
  `creator-os-phases.json` is satisfied end to end.

### Step 2 — Call the 4A seat gate

One contiguous session. Derek's script contains only judgments a machine cannot make. Any
mechanical question that reaches him in that session is a defect with a receipt.

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

Plus the two that are not requirements: **the seat lap in step 2**, and **who owns the game
install** for its duration.

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
| Automating world entry | A later reduction of the human boundary, never a gate (ADR 0014) |

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

Further reading, in order: `handoff-2026-08-24.md` (session record and environment traps),
`five-intent-program-plan.md` (the ethos in full), `creator-os-build-strategy.md` (why the lanes
are ordered this way), `adr/` (decisions, append-only), `working-agreements.md` (how this
repository expects to be worked on), `creator-os-audit-2026-08-24.md` (what was wrong, with
receipts).

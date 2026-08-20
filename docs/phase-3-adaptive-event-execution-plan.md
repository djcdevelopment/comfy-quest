# Phase 3 adaptive event semantics — execution plan and status

Status date: 2026-08-19 (updated after the Phase 3.4 presentation slice)
Temporal foundation landed in `665838a` (`Add adaptive temporal predicates`); the
spatial substrate landed in `6ef6d62` (`Add spatial anchors and predicates`); the
encounter facts landed in the following `Add encounter and performance facts` commit.

This document records the working plan behind Phase 3, what the first implementation
slice delivered, and what remains. It is subordinate to
`docs/five-intent-program-plan.md`, which remains canonical. Its seven program
guardrails govern every decision here.

## Intended outcome

Phase 3 makes quests respond to meaningful context without turning Studio into a
wall of engine hooks. A creator should be able to compose a small player action with
a reviewed fact—time in a stage, lack of progress, presence in an area, or measured
encounter performance—and receive a clear explanation of both the authored condition
and the observed value. Each primitive starts in the extended/advanced palette. It
earns beginner exposure only when an observed quest idea needs it.

The phase exit is the canonical **ten-minute desperate defense** Event: every adaptive
branch evaluates from authoritative facts, rehearses synthetically, runs from a dev
revision, and explains why it did or did not fire in Arcane Sight.

## Non-negotiable design decisions

1. Adaptive behavior extends the existing trigger expression and workflow artifact;
   it does not introduce a parallel rule format.
2. Runtime facts are named, bounded, and sourced. Missing facts fail closed.
3. Temporal evaluation uses the triggering event's timestamp, never wall-clock time.
4. Every adaptive transition remains event-driven. A threshold without an `EVENT`
   cannot compile.
5. New operators move together across schema validation, evaluator, `Explain`,
   workflow persistence, rehearsal, Studio catalog, and tests. The schema-gate test is
   written first.
6. Spatial semantics are event-side, not binding-side. Loaded-scene binding discovery
   retains no fixed radius, `RuntimeCharmBinding.cs` remains outside the spatial
   implementation, and no sixth ZDO string is added.
7. `Explain` and receipts are product explanation, not a debug dump: actual and
   expected values must be available for prose, structured inspection, canonical
   JSON, and Arcane Sight.
8. Manual player time is requested only when a novel uncertainty can change a product
   decision or the next build. Established adapter behavior stays in synthetic
   regression coverage.

## Delivered — Phase 3.1 temporal foundation

Phase 3.1 is complete and landed on `main` in `665838a`.

### Fact inventory

`docs/adaptive-event-fact-inventory.md` admits two authoritative temporal facts:

- `time_since_stage_entered`: seconds between persisted stage entry and the
  triggering event;
- `time_since_progress`: seconds between the last structural trigger-progress event
  and the triggering event.

Both use the triggering `RuntimeEvent.at`. Both fail closed when their persisted
anchor is absent. The inventory also records why continuous combat duration is not
yet an admitted fact.

### Contracts and compiler

`network/mod/ComfyQuestContracts/ExperienceContract.cs` now provides:

- one small `THRESHOLD` trigger operator;
- additive `measure`, `comparison`, and `value` expression fields;
- a closed `AdaptiveMeasureCatalog` containing exactly the two temporal facts;
- only the `gte` comparison and values from 1 through 86400 seconds;
- compiler rejection for unknown facts, unsupported comparisons, invalid bounds,
  threshold children, and adaptive expressions with no event driver.

The existing experience schema remains the shared artifact. Both measures are marked
`extended`; neither entered the beginner beat palette.

### Pure evaluation and explanation

`network/mod/ComfyQuestContracts/RuntimeContract.cs` now provides:

- `TriggerEvaluationContext`, carrying event time and the persisted temporal anchors;
- context-aware overloads of `Matches`, `Measure`, and `Explain`;
- `EventProgress`, which measures structural event progress without treating elapsed
  time as player progress;
- deterministic threshold traces with the measured seconds and authored threshold;
- the existing 8 KiB serialized trace limit and eight-entry where cap;
- fail-closed behavior when a temporal anchor is unavailable.

The original context-free `Matches` path remains intact and fails a `THRESHOLD`
closed. Composite trace progress uses the same context as the decision, so the
top-level explanation cannot disagree with its threshold children.

### Workflow persistence and Runtime

`network/mod/ComfyQuestContracts/WorkflowRuntime.cs` now persists additive nullable
`last_progress_utc` alongside `stage_entered_utc` and `last_event_utc` in
`comfy-quest-workflow-state/v1`.

- New workflow state initializes both temporal anchors from the first event.
- Structural trigger progress updates `last_progress_utc` from event time.
- Ignored events that do not advance trigger structure do not reset it.
- A stage advance resets stage entry and progress anchors to the deciding event.
- Pending replay reconstructs the original evaluation context rather than reading the
  wall clock.
- Old v1 state files deserialize missing anchors as null.

`network/mod/ComfyQuestRuntime/RuntimeExperienceEngine.cs` passes that context to
matching, measurement, selected evidence, and rejected-branch evidence. Runtime prose
can describe temporal requirements while receipts retain actual-versus-expected data.

### Studio and rehearsal

`src/Quest.Studio/QuestStudioWorkspace.cs` and
`src/Quest.Studio/QuestStudioPage.cs` now provide:

- a catalog-driven adaptive-condition editor inside Advanced Graph tools only;
- one condition per registered measure, with closed bounds and comparison;
- compilation to the same `ALL(EVENT-or-COUNT, THRESHOLD...)` contract tree;
- preservation of a repeated beat's authored time window on the composite root;
- event-time rehearsal with stage and progress anchors;
- guided no-progress rehearsal that establishes partial structural progress, advances
  synthetic event time, and then supplies the required triggering event;
- prose, graph notation, and certified JSON for the new conditions.

Routes containing adaptive conditions intentionally stop projecting into the
beginner beat editor. The artifact is preserved and remains editable in Advanced
tools rather than being flattened or silently altered.

### Package and compatibility work

- `Comfy.Quest.Contracts` and `Comfy.Quest.Studio` moved from `0.3.0-local` to
  `0.4.0-local`.
- Every local consumer and launch/E2E pin moved deliberately to the exact 0.4 version.
- The future public publication and repin instructions now consistently target 0.4.0.
- The regenerated local packages carry Studio's exact `[0.4.0-local]` Contracts
  dependency.
- `RuntimeCharmBinding.cs` and `CharmReference` were not changed.

## What the completed verification proves

The landing passed the complete repository gate set:

- licensed Quest Lab Release build;
- 240 Contracts/Lab xUnit tests;
- 269 Python pin, boundary, and drift tests;
- 74 Studio xUnit tests;
- loopback browser Studio E2E through host, questpack, activation, and receipts;
- licensed Runtime Release build;
- repository identity and no-reach-in checks;
- generated Quest Lab, seam-catalog, and patch-coverage checks;
- exact interim-package and release-verifier checks;
- validation-harness parser, safety, quarantine, timeout, and byte-exact restoration
  self-test;
- full-history secret scan across 135 commits.

Focused temporal coverage proves closed schema admission, event-time-only thresholds,
deterministic actual-versus-expected traces, old-state null compatibility, stage-entry
reset, no-progress reset only on structural progress, pending replay behavior, and
repeat-window preservation under an adaptive composite.

## Current “can't answer why” item

Runtime cannot yet answer why a player has been in one continuous combat session for
a specific duration. Inspection found isolated action timers and presentation
timeouts, but no authoritative continuous combat-state start/end fact. Treating gaps
between damage events as uninterrupted combat would invent continuity.

Therefore `combat_duration` is deferred. It may be admitted only after a dedicated
observation module defines authoritative start, end, reload, death, teleport, and
disconnect semantics and covers them with tests. This is a missing fact, not a reason
to broaden the current threshold registry.

## Delivered — Phase 3.2 spatial substrate

Phase 3.2 is complete. The seven planned steps landed together behind the schema-gate
test, and the exit is met: every spatial primitive runs as a synthetic Event through
guided Studio rehearsal with bounded actual-versus-expected evidence, and nothing
entered the beginner palette.

### Fact inventory

The position-fact inventory confirmed **no normalized event carried any position**;
the privacy boundary would have stripped one, and every `Vector3` in Runtime patches
was a Harmony signature type, never a captured value. `docs/adaptive-event-fact-inventory.md`
now admits three spatial facts — the stamped witness position, the bound Charm's
position, and this workflow's tracked spawned-object positions resolved from the
spawn ledger — plus authored anchor positions from the document itself. Interpretation
limits (no invented continuity for `remained`, observed transitions only for
`entered`/`left`, live facts at replay) are stated in the inventory.

### Contracts

`ExperienceContract.cs` provides one `SPATIAL` leaf operator mirroring `THRESHOLD`:
a closed `SpatialPredicateCatalog` (`within_radius`, `entered`, `left`, `remained`
>= 1..86400 seconds, `count_in_area` >= 1..128 objects, all `extended` palette), a
canonical `AreaAnchor` (`authored` | `binding` | `player` | `coordinates`), a
required radius of 1..100 whole meters, and an additive document-level `anchors`
list (<= 32, bounded coordinates). The compiler rejects unknown predicates, missing
or malformed anchors, dangling authored references, out-of-bounds radii and values,
spatial children, and spatial-only triggers without an `EVENT` driver. The `player`
anchor is admitted only for `count_in_area`; the other predicates already take the
player as their subject.

`SpatialEvaluator.cs` is the pure evaluator: all distance math (3D Euclidean over a
Unity-free `SpatialPoint`) lives here. `RuntimeEvent` gains additive nullable
`PosX/PosY/PosZ`; the privacy boundary passes a position only complete, finite,
inside ±10500, rounded to 0.1 m. `TriggerEvaluationContext` resolves anchors from
authored maps, the binding position, or the triggering event; missing anything fails
closed. `Explain` emits one actual-versus-expected row per predicate ("within 20 m
of camp" / "15 m") without retaining raw player coordinates in traces.
`WorkflowStateStore.Begin` accepts optional live `SpatialFacts` and threads document
anchors into every evaluation context, including pending replay.

### Runtime

`RuntimeSpatialObservation.cs` is the single new observation seam: it stamps the
local player's position onto every routed witness event and engine timer event, and
resolves binding plus still-matching spawned-object positions at evaluation time
(`SpawnExecutionStore.ForOwner` is the supporting ledger query). Binding discovery
is untouched, `RuntimeCharmBinding.cs` never learns about positions, `Vector3.Distance`
still appears nowhere in the engine, and the arcane-sight python pin was extended —
not weakened — to enforce all of that plus the README clarification that "no fixed
radius" describes binding discovery only. Drawer prose now speaks spatial
requirements ("while within 20 m of the bound Charm").

### Studio

Advanced Graph tools gain a catalog-driven spatial-condition editor beside the
adaptive one (one condition per predicate, closed anchor kinds, bounded radius and
value), compiling into the same `ALL(EVENT-or-COUNT, THRESHOLD..., SPATIAL...)`
tree with the repeat window preserved on the composite root. Routes with spatial
conditions stop projecting into the beginner beat editor, and the graph canvas marks
them. Guided rehearsal shapes synthetic paths per primitive — an outside-then-inside
walk for `entered`, inside-then-outside for `left`, an anchored wait for `remained`,
and origin-staged spawned objects for `count_in_area` (with an explicit limitation
line) — and custom rehearsal steps accept positions. Prose, graph notation, and
certified JSON all render the new conditions; live-Runtime values ride the existing
evidence path.

### Versions and verification

`Comfy.Quest.Contracts` and `Comfy.Quest.Studio` moved to `0.5.0-local` with exact
repins everywhere (the future public publication targets 0.5.0). The full gate set
passed: licensed Lab and Runtime Release builds, 246 Contracts/Lab xUnit tests, 270
Python pin/boundary/drift tests, 75 Studio xUnit tests, loopback browser Studio E2E,
interim-package and no-reach-in checks, generated-tome check, and the full-history
secret scan.

### Deliberate scope holds

- Authored (`authored` kind) anchors are contract-complete but have no Studio editor
  yet; creators reach them through coordinates or the JSON surface until a validation
  lap demonstrates the authoring need (palette-admission rule applied to UI).
- Positions for ambient creatures, other players, and world objects remain
  unadmitted; `count_in_area` counts only this workflow's tracked spawns.
- Timer-driven routes rehearse without positions and say so in a limitation line.

## Delivered — Phase 3.3 encounter and performance facts

Phase 3.3 is complete. Quests can now respond to how a fight went: how many of a staged
wave are gone, how many still stand, and how many times the player fell. Nothing entered
the beginner palette, and no new game observation was added.

### Facts

`AdaptiveMeasureCatalog` gains three `extended` measures, all using the existing `gte`
comparison: `player_deaths_in_stage` (1..64 deaths), `spawned_enemies_remaining` and
`spawned_enemies_cleared` (1..128 enemies each). The two spawn measures name their wave
through an additive `action_id` on `THRESHOLD`, validated against the document's authored
`spawn` actions exactly as `clear_spawned` is. `docs/adaptive-event-fact-inventory.md`
records each source, its failure behavior, and the interpretation limits.

The slice added **no new normalized event**: `player_died` and `kill` were already in the
closed 34-event catalog, so the generated seam catalogs and their count pins are
untouched. Because a document can count deaths without naming the event in any clause,
`RuntimeSubscriptionIndex` now subscribes a measure's declared `SourceEvent`; without
that the engine would drop the event before evaluation and the condition would silently
never fire.

### Why they are honest

The load-bearing question was whether `spawned_enemies_cleared` can fail open. It cannot:
evaluation runs only where `CharmPolicy.CanMutate` allows (solo or listen-host private
worlds, never a dedicated server or peer client), and that process holds every ZDO
regardless of zone loading — so an unresolvable ledger row means the object was
destroyed, not merely distant. Staged and live counts come from one ledger read, so an
absent ledger reports zero staged and fails every predicate closed. `SpawnExecutionStore`
now reports `Unreadable` the way `WorkflowStateStore` already did, keeping "unknown"
distinct from "none".

Wave-clear *time* is deliberately composed rather than predicated: a triumph route driven
by the clearing event wins its race against a lower-priority timer route, and the elapsed
time is reported from `stage_entered_utc` and the deciding event time. No fact claims to
know the moment the last object fell, because nothing observes it. `time_since_spawned`
joins combat duration on the deferred list with its admission rule written down.

### Contracts, Runtime, and Studio

`AdaptiveEvaluator.cs` is the new pure evaluator mirroring `SpatialEvaluator`; the
`THRESHOLD` trace row's hardcoded `" seconds"` became a catalog-driven unit with singular
forms (`">= 8 enemies"` / `"1 enemy"`), leaving the pinned temporal strings byte-identical.
`WorkflowProgress` gains additive nullable `deaths_in_stage`, incremented on the append
path only and reset when the stage advances, not when a same-stage transition clears
history.

`RuntimeSpatialObservation` was renamed `RuntimeObservation`: it now resolves positions
and tallies in one ledger pass, and the invariant is "one observation seam," so the name
should say so. The arcane-sight python pin was extended — not weakened — to cover the new
name, to forbid the retired one, and to keep tally math out of the engine and the Charm
binding surface.

Studio's advanced adaptive editor gained a wave selector for the counting measures, a
catalog-driven default (a seconds-flavored 30 would have seeded a nonsense death count),
and bounds that re-clamp when the measure changes. The two hardcoded measure ternaries in
Studio prose and the one in Runtime drawer prose became catalog-driven, so a future
measure can never silently render as "without quest progress". Guided rehearsal filters
its wait to seconds-unit measures, supplies the extra falls a death condition needs, and
stages a wave then removes objects from it through a new `despawn` step — honest
precisely because it is not dressed up as a `kill` event, and carrying a limitation line
that says so.

### Versions and verification

`Comfy.Quest.Contracts` and `Comfy.Quest.Studio` moved to `0.6.0-local` with exact repins
everywhere. The full gate set passed: licensed Lab and Runtime Release builds, 250
Contracts/Lab xUnit tests, 270 Python pin/boundary/drift tests, 76 Studio xUnit tests,
loopback browser Studio E2E, interim-package, no-reach-in, repo-identity, and
generated-tome checks, and the full-history secret scan.

### Deliberate scope holds

- No `lte` comparison: every desperate-defense branch is expressible with `gte` plus
  route priority. Admit it when a lap produces a condition that genuinely needs it.
- No per-wave clock and no cross-stage death total; both have written admission rules.
- `count_in_area` still counts only this workflow's tracked spawns, and authored anchors
  still have no Studio editor (Phase 3.2 holds, unchanged).

## Delivered — Phase 3.4 the five-level presentation

Phase 3.4 is complete. The attenuation ladder now carries every admitted adaptive
primitive from beginner prose to live Runtime values, and the lap's loudest
player-experience finding — "a contract-only deadline is not player tension" — is closed.

### A deadline you can see

`TriggerCountdown` is the new pure fact. A repeat window is a sliding window anchored on
the most recent contributing event, so the deadline a player actually faces is the moment
their earliest still-counted attempt leaves it. It reports as running only once an attempt
has started it and while it is unsatisfied, because before the first attempt there is
nothing to race. It renders exactly the sentence the receipts guardrail always promised:
`1/2, 6 seconds remaining`.

Runtime draws that on an always-on banner. `OnGUI` previously returned early unless the F9
drawer was open, so the mod had no player surface at all; the banner now draws before that
return and turns red under five seconds. The line is recomputed once a second beside the
existing timer poll and cached, because the surface that draws it runs every frame — the
countdown never repeats `DescribeProgress`'s unbounded per-frame scan. `DurableTimerStore`
gained `Pending`, since a store that could only answer "is it due yet" cannot show a clock.

### Levels one through five

1. **Prose** — guided rehearsal and the drawer speak the running deadline; the drawer's
   pinned `in stage` line is appended to, never replaced.
2. **Structured controls** — unchanged from 3.3; bounds and closed facts already hold.
3. **Graph notation** — adaptive and spatial routes now carry chips and a distinct dotted
   violet edge keyed on *having conditions*, not on being the second route drawn.
4. **Canonical JSON** — the same shared trigger expression throughout.
5. **Live values and rejected branches** — Runtime evidence lines now say why the branches
   that outrank the winner did not take it (`Not mercy needs >= 2 deaths (1 death)`), and
   Studio projects `unmet` and `not_taken` onto receipt rows. Both read the actual-versus-
   expected slot that Slice 1 put on the trace and 3.1–3.3 filled in.

### Repeated attempts read as progress

The lap recorded that a valid first drop rendered as an empty circle beside `1/2` and
looked like a failure, and that repeated evaluations of one beat looked like separate
beats. Rehearsal traces now carry their owning beat, whether the attempt contributed, and
the deadline; the browser groups consecutive attempts beneath their beat and marks them
amber **partial 1/2** before the green completion. "Contributed" deliberately means the
event matched that beat's own clause rather than that structural progress advanced — under
a sliding window a later attempt can replace an expired one without advancing the count,
and it is still the player making progress.

### One fail-open closed on the way

Six action types compiled but had no Runtime executor (`arcane_sight`, the two counters,
`stage_activate`, and the two terminal results). An unimplemented action throws, which
leaves its transition pending and replays it forever — a hand-authored pack could silently
brick its own workflow. Nothing used them, Studio's catalog already excluded them, and
terminal results are authored as a transition outcome. The registry now admits exactly what
ships, with a test that fails if the two ever diverge again.

### Versions and verification

Packages stay at `0.6.0-local` (the 3.3 refresh already carried this contract line); the
interim payloads were repacked to include the countdown. Gates: licensed Lab and Runtime
Release builds, 253 Contracts/Lab xUnit tests, 272 Python pin tests, 76 Studio xUnit tests,
loopback browser E2E now asserting the grouped partial row in the real DOM, plus the
interim-package, no-reach-in, repo-identity, and generated-tome checks and a clean
full-history secret scan.

### Deliberate scope holds

- The banner shows one deadline — the soonest running timer, else the highest-priority
  running window. Stacked simultaneous deadlines wait for an authored quest that needs them.
- Studio still shows the authored window rather than a live ticking clock; the countdown
  belongs to the player in-world, and Studio's live lane remains receipt-driven.
- Adaptive primitives remain advanced-only. Nothing was promoted to the beginner palette.

## Remaining Phase 3 work

### Phase 3.5 — dev-channel proof and phase exit

1. Build one real dev-revision Event per admitted primitive.
2. Use synthetic proof for known compiler/evaluator/adapter behavior.
3. Request player interaction only for novel spatial legibility, temporal tension, or
   branch-explanation questions that cannot be answered synthetically.
4. Assemble the ten-minute desperate-defense Event.
5. Confirm every adaptive branch can be explained in Arcane Sight, including actual
   facts for branches that did not fire.
6. Record both product-experience observations and genuine “can't answer why” moments.

Phase 3 is complete only when that exit Event is explainable end to end. The temporal
foundation alone does not satisfy the phase exit.

### Delivered — the synthetic half of 3.5

Steps 2, 4, and 5 are done; the Event exists and is proven everywhere a machine can prove
it. `desperate-defense` is a Studio template, so the exit Event is a real authored artifact
rather than a description: muster stages eight Greylings and a ten-minute timer, then
**hold** carries the four dramatic branches, with **besieged** behind the reinforcement
route.

| Branch | Fires on | Reads | Ending |
| --- | --- | --- | --- |
| `held` | a kill | 8 of 8 cleared | complete, with a reward |
| `overrun` | the ten-minute timer | 1 or more still standing | fail |
| `reinforce` | a kill | 3 minutes in and 6 still standing | a second wave, to `besieged` |
| `mercy` | falling | 2 deaths this stage | complete, the ward releases you |

Each branch is rehearsed to its own outcome from its own synthetic path, and the exit's
hardest clause — actual facts for branches that did **not** fire — is asserted directly:
mid-fight, the triumph branch reports `>= 8 enemies` wanted against `3 enemies` seen, and
the mercy branch reports `>= 2 deaths` against `1 death`. One death alone does not trigger
mercy, and that non-firing is asserted too.

### What still needs the game

> **Falsified 2026-08-20 by lap session 2; left in place as the record of a wrong
> assumption.** The paragraph below declares adapter behaviour "settled" and consigns it to
> the synthetic suite. It was not settled: the kill witness feeds a spawn tally polled
> inside `Character.OnDeath`'s own call stack, and the resulting destroy-settle race
> counted eight of eight kills as none (ADR 0006). No synthetic harness covered it —
> both hand-fed the tally — and Studio's rehearsal `limitations[]` had already printed the
> gap on every run. **A plan may not declare behaviour proven on the strength of a suite
> that supplies the fact in question.** See `docs/phase-3-close-strategy.md` and
> `docs/five-intent-validation-lap-backlog.md` for what the lap actually found.

Only steps 1, 3, and 6, and they are deliberately short now that behavior is settled:
publish the template as a dev revision, play it once, and answer the questions synthetic
proof cannot — whether the countdown banner carries tension during actual combat, whether
the reinforcement beat lands as escalation rather than punishment, and whether mercy reads
as mercy rather than as failure. Record any genuine "can't answer why" moment. Known
compiler, evaluator, and adapter behavior stays in the synthetic suite; the lap already
established that re-confirming known behavior at the keyboard produces no decisions.

## Adjacent observed backlog retained for later slices

The completed Woodbound Signal lap produced valid follow-through that remains in
`docs/five-intent-validation-lap-backlog.md`:

- ~~group repeated rehearsal evaluations beneath their owning beat with explicit
  partial and complete hierarchy~~ (delivered in 3.4);
- ~~replace ambiguous F10 candidate text and lead F11 confirmation with the friendly
  quest name rather than raw identity and full hash~~ (delivered in the creator-loop
  UX baseline, `docs/creator-loop-ux-baseline.md`, together with the story/plumbing
  HUD channel split, the idle-F11 rollback-history fix, and keypress-borne orphan
  consequences);
- ~~rename the Arcane Sight ownership summary so it describes the actual
  intersection~~ (delivered: `0 active + locally owned`);
- ~~provide a universal timer/countdown affordance when an authored deadline is meant
  to matter during play~~ (delivered in 3.4; the next lap judges whether it carries
  tension during actual combat);
- reduce the cognitive cost of switching between Studio creation and the F9 in-world
  creator cockpit (`docs/quest-lab-persona-audit.md` reframes this as a scope
  decision: no in-world editor exists anywhere, and the Lab's Scenarios machinery is
  the strongest existing asset for in-world rehearsal);
- ~~evaluate F6 Quest Lab feature ownership and Arcane Sight's eventual spellbook
  role~~ (identification delivered in `docs/quest-lab-persona-audit.md`; extraction
  stays deferred to Phase 4 on observed need);
- build a sanitized, screenshot-led Studio tutorial from the already captured
  Woodbound authoring sequence without asking the player to recreate it.

These observations inform presentation and Phase 4 ownership. They do not justify
adding unrelated primitives to the Phase 3.2 spatial contract slice.

## Beyond Phase 3

After the adaptive phase exits:

- Phase 4 builds the Quest Lab spellbook and versioned reusable pattern identity from
  primitives proven worth saving.
- Phase 5 makes those artifacts community-ready with attribution, permissions,
  import/export, and repository semantics.

Those phases consume the same structured artifact; neither introduces an AI-only or
parallel quest format.

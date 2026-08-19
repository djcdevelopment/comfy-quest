# Five-Intent Program Plan — Quest creator ecosystem

Adopted 2026-08-18. Source design intents: `docs/arch/01..05_*.md` in the baseline
repository (Arcane Sight observability, Quest Lab apprenticeship, Studio↔Live closed
loop, community artifact ecosystem, adaptive event semantics).

Exploration confirmed the gold baselines those docs reference already exist
(RuntimeArcaneSight, F9 drawer with Look→Validate→Load→Confirm, receipts, hot-load,
Studio cockpit, synthetic rehearsal, the Lab Explain machinery), but all five intents
lean on a shared missing substrate: run/correlation identity, per-clause evaluation
evidence, stage-entry timestamps, and first-class active-revision surfacing. Two
intents collide with deliberately pinned invariants (Studio-never-mutates-game;
no-radius binding model) that require explicit decisions, recorded below.

## Program ethos and guardrails

Adopted from an external design review (2026-08-18) that scored the program strongly
against the WeakAuras ecosystem ethos — small composable primitives, portable
artifacts, inspectability, immediate feedback, progressive complexity, community
reuse — with one modern inversion: the machine absorbs the complexity that WeakAuras
historically forced onto the human. These constraints govern every phase; the danger
is not missing capability but exposing too much of it too quickly.

1. **The primitive is small; the composition is powerful.** Say, drop, pickup, wait
   are individually unimpressive; `Say → within 10s Drop ×2 → Pickup → Equip →
   Consume → Heal` is an authored ritual, and `combat duration > 8m → relief` is a
   reusable construct. The ecosystem effect is people combining constructs neither
   the system designers nor the original pattern authors anticipated. Design for
   composition, not for impressive primitives.
2. **Palette admission rule.** Capability enters the default authoring palette only
   from observed quest ideas, never wholesale (`docs/quest-rd-opportunity-matrix.md`:
   "selected from observed quest ideas rather than exposed wholesale"). The catalog
   grows because someone tried to make something compelling and couldn't — not
   because another hook exists. New Phase 3 predicates land in the extended/advanced
   palette first; promotion to the beginner palette requires a demonstrated authored
   need from a validation lap.
3. **Attenuation ladder, one artifact.** Beginner composes verbs; intermediate
   imports/remixes patterns; advanced manipulates explicit flows; expert inspects
   canonical JSON and runtime semantics; agents operate directly against the
   structured contract. Everyone works against the same underlying artifact. Don't
   remove the power — attenuate how much of it someone must understand at once.
4. **Receipts are explanation, not logging.** Every evidence field must render as
   beginner prose ("Drop an item twice within 10 seconds — 1/2, 6 seconds
   remaining") with drill-down to the actual expression and, in Arcane Sight, to why
   it fired. This is why D3 traces carry actual-vs-expected values and D5 resolves
   labels server-side; keep that discipline for every future receipt field.
5. **Multiplayer is a separate validation dimension.** The second machine exists only
   for peer role, listen-host authority, replication, and fail-closed multiplayer
   mutation questions. Content, ordering, rewards, and browser UX stay in the cheap
   local loop.
6. **AI is a composer/translator, not an architectural layer.** Agents use the same
   revision-guarded structured contract and notebook identities as humans — no
   agent-specific artifact format, no layer everything else depends on. Community
   artifacts become building material and accumulated knowledge for that loop
   ("find something close → import → describe the variation → agent composes →
   validator proves → Studio explains → hot-load → Arcane Sight shows what
   happened"), not merely finished things copied by people who can't operate the
   editor.

Orientation map for the whole ecosystem: Grimoire is the vocabulary, Studio the
workbench, Runtime the interpreter, receipts the explanation/proof, Arcane Sight the
devtools, Quest Lab the apprenticeship, Spellbook the personal accumulated craft, and
the eventual repository the community memory.

## Program overview

| Phase | Intent | Depends on |
| --- | --- | --- |
| 1 | Identity & evidence spine (01 core) | — |
| 2 | Studio↔Live closed loop (03) | Phase 1 receipts + ActiveSet surfacing |
| 3 | Adaptive event semantics (05) | Phase 1 stage timestamps; Phase 2 fast iteration |
| 4 | Quest Lab spellbook (02) | Pattern-worthy primitives from 3; identity from 1 |
| 5 | Community-ready artifacts (04) | Attribution/pattern IDs from 2–4; metadata parts may start earlier |

Ordering rationale: the spine is what every doc names. Phase 2 follows because the
spine hands it activation identity and receipts, and a fast loop accelerates every
later validation lap. Phase 3 rides the stage timestamps planted in Slice 1. Phase 4's
spellbook needs patterns worth saving (from Phase 3) and co-designs its identity type
with Phase 5's manifest. Phase 5 closes by making everything exportable/importable.

The **validation-lap Event** (intent 01's success test) is a cross-phase thread: a
real multi-stage Event built at the end of Slice 1, extended every phase on the OMEN
lap, with every "can't answer why" moment recorded as backlog.

---

## Slice 1 — Identity & evidence spine

### Design decisions

**D1 — Run identity, two levels, no new ZDO string.**

- `activation_id` (`act-<yyyyMMddTHHmmssfffZ>-<8hex>`): minted in
  `QuestPackStore.ActivateCandidate` (`network/mod/ComfyQuestContracts/RuntimeContract.cs`),
  additive nullable field on `ActiveSet`. Schema string stays
  `comfy-quest-active-set/v1` — `Rollback` checks the exact schema string plus the four
  identity fields only, so the additive field is safe (verified against source).
  Rollback mints a fresh activation_id: a rollback is a new activation epoch.
- `correlation_id` (`evt-<12hex>`): minted per accepted event at the top of
  `RuntimeExperienceEngine.OnEvent`, threaded through event/transition/action receipts
  so one event's evidence chains.
- Instance identity: reuse `binding_zdo` (already on receipts) and
  `WorkflowIdentity.Key`. Explicitly no sixth ZDO string — activation changes without
  re-CAST would leave a ZDO copy stale by construction. `RuntimeCharmBinding.cs` and
  `CharmReference` stay untouched.

**D2 — Receipt schema: additive on v1.** New nullable fields on `RuntimeReceipt`
(`RuntimeReceipts.cs`) with `NullValueHandling.Ignore` (the existing convention):
`activation_id`, `correlation_id`, `stage_entered_utc`, `evidence`,
`rejected_evidence`. Reader (Studio `RuntimeStatus`) and writers (engine, E2E
synthetic writers) share the Contracts class; no strict field-set validation exists.
Document the additive policy in a comment; bump the Contracts NuGet minor version.
Reserve a v2 schema bump for the day a field's meaning changes.

**D3 — Per-clause trace: parallel `Explain`, evidence on existing receipts.** New pure
`TriggerEvaluator.Explain(TriggerExpression, history)` → `TriggerClauseTrace`
mirroring the expression tree: per node `{op, event, target, satisfied,
current/required, sequence_index, within_seconds state, where:[{field, expected,
actual, satisfied}], children}`. `Matches` stays byte-identical (hot path,
determinism). Computed in the engine after `WorkflowStateStore.Begin` returns, while
history is still intact. Selected transition's trace lands as `evidence` on the
`event/matched` and `transition` receipts; up to three higher-priority rejected
transitions land as `rejected_evidence` `{transition_id, evidence}`. Skipped on
`IsPendingReplay` (replay history is not the history that decided). Bounds: ~8 KB
serialized cap, ≤8 where entries per node.

**D4 — Stage-entry timestamps: record and surface, no predicates yet.**
`WorkflowProgress` (`WorkflowRuntime.cs`) gains nullable `stage_entered_utc` and
`last_event_utc`, set from the triggering event's time, never wall clock: `EventAt`
is added to `WorkflowDecision` (not serialized), captured in `Begin`, consumed in
`Complete` when `StageId` advances. Old `comfy-quest-workflow-state/v1` files
deserialize with nulls. Surfacing: `stage_entered_utc` on transition receipts;
`DescribeProgress` appends "in stage Xm Ys". This is the deliberate bridgehead for
Phase 3's temporal predicates.

**D5 — Studio: stop dropping the ActiveSet; label receipt rows.**

- `StudioRuntimeStatusView` and `QuestStudioService.RuntimeStatusView` (which today
  drop the ActiveSet — the browser only receives a phase word) gain flattened
  `ActivePackId/ActiveVersion/ActiveContentHash/ActiveActivationId/ActivatedUtc` plus
  computed `ActiveRelation ∈ current|other_version|none` (the comparison already
  exists server-side in `QuestStudioWorkspace.RuntimeStatus`).
- `StudioRuntimeReceiptSummary` gains
  `TransitionId/ActionId/CorrelationId/RouteLabel/EffectLabel`; label resolution is
  server-side from the compiled document, keeping browser JS thin.
- UI lands in the `QuestStudioPage.cs` raw strings, publish stage and advanced tools
  only (author-stage no-plumbing-words pin): an "Active in game" line stating pack,
  version, short run id, and matches/differs-from-this-draft; receipt rows labeled
  with route and effect names; row-hover flashes the corresponding graph node via the
  existing `liveNodeId` mechanism. All vnext pins honored: one-physical-line JS
  functions, `$('#id')` cross-check, stale-response guards, contrast.

**D6 — Arcane Sight drawer evidence: in-memory ring, receipts stay truth.**

- The engine keeps a bounded ring (last 8 lines) appended when writing
  matched/transition/action receipts, exposed via `RecentEvidence()`; a new F9 drawer
  section RECENT RUNTIME EVIDENCE in `ComfyQuestRuntime.cs` (pattern-copy of
  `DrawOutcomes`) finally brings gameplay events into the drawer.
- Labels: short activation id on ACTIVE markers plus the binding ZDO short id in
  `RuntimeArcaneSight.cs`. The read-only pin stays intact (`zdo.Set(`, `SetOwner(`,
  `DestroyZDO(` never appear; the "loaded scene, no fixed radius" string is
  untouched).
- Orphan notice: in `TryLoad`, when the newly active `ContentHash` differs from the
  cached one, a single `WearNTear` scan counts mismatched bindings, writes receipt
  `operation="activation", status="orphaned_bindings", candidate_count=N`, and pushes
  a drawer line "N bindings now OTHER VERSION — re-CAST or roll back".
  `Vector3.Distance` stays out of the pinned Bindings region.

### Session breakdown

**1.1 Contracts spine** (CI-covered, no Valheim): `RuntimeContract.cs`
(`ActiveSet.ActivationId` + mint; `TriggerClauseTrace` + `Explain`),
`RuntimeReceipts.cs` (five fields + policy comment), `WorkflowRuntime.cs`
(timestamps + `EventAt`). Tests in `ComfyQuestLab.Tests`: activation_id mint/rotate
including rollback still passing; receipt round-trip with an old-JSON fixture;
`Explain` determinism and agreement with `Matches` across all five ops, where-clause
actual-vs-expected, size caps; stage timestamps on create/advance plus an old
state-file fixture. Verify: `dotnet test network/mod/ComfyQuestLab.Tests`;
`python -m unittest discover -s tests`.

**1.2 Runtime engine + Arcane Sight + drawer** (compile on OMEN):
`RuntimeExperienceEngine.cs` (correlation threading, evidence attach, stage_entered
on transition receipts, orphan scan and receipt, evidence ring),
`RuntimeArcaneSight.cs` (activation id + zdo id in labels), `ComfyQuestRuntime.cs`
(evidence section). Update `tests/test_quest_runtime_arcane_sight.py`: keep every
existing marker valid; add markers for the evidence section and the orphan receipt;
the Bindings-region no-distance slice must still pass. Verify: python suite; mod
compile per `docs/runbooks/I2-QUESTPACK-OMEN.md`.

**1.3 Studio return path** (local test lane): `QuestStudioWorkspace.cs` (status
records + label resolution), `QuestStudioService.cs` (view mapping),
`QuestStudioPage.cs` (Html/Css/Js additions). Tests: `Quest.Studio.Tests` (ActiveSet
projection and relation for current/other_version/none; label resolution; phase logic
unchanged); E2E synthetic writers stamp the new fields and the journey asserts the
active-revision line; vnext structural pins re-run —
`test_v2_routes_keep_game_mutation_out_of_the_browser` must stay green. Verify:
`dotnet test src/Quest.Studio.Tests`; `tools/quest-studio/Test-QuestStudioE2E.ps1`;
python suite. (Studio xUnit and Playwright are not in CI — local gates.)

**1.4 Validation-lap Event v0 + OMEN lap**: author a real 3-stage Event in Studio,
publish, activate, CAST, play. Exercise the activation id in drawer and cockpit,
evidence lines per advance, the orphan notice after r2. Deliverable: a backlog doc of
every "can't answer why" moment plus a runbook addendum describing the lap.

**Exit criteria**: every receipt carries activation_id and correlation_id; matched
transitions explain themselves clause-by-clause with actual values; workflow state
knows its stage entry time; the cockpit states what is active versus the draft; the
drawer shows gameplay evidence and orphaning; all pinned tests green with enumerated,
deliberate updates only.

---

## Phase 2 — Closed creator loop (03)

Goal: one creator, one session, one Event, repeated revisions, seconds-scale
feedback, no restart.

**Key decision — keep the browser-never-mutates-game pin; invert control ("armed dev
channel").** Studio publishes dev revisions to a separate `inbox-dev/`
(Author/Rehearse/Publish separation falls out structurally; dev revisions never
pollute the public inbox). The creator ARMS the channel once per session in the F9
drawer — explicit in-game consent. While armed, the runtime polls `inbox-dev/`,
auto-validates and auto-activates through the existing `QuestPackStore` lane, writing
receipts. `test_v2_routes_keep_game_mutation_out_of_the_browser` stays true: the game
pulls. "Play this revision" in Studio = publish-to-dev plus watching the receipt
stream rendered as the intent's Validation/Transfer/Activation/Runtime-observed PASS
lines, all reconstructable from Slice 1 receipts keyed by activation_id. The
alternative (flipping the pin with opt-in dev routes) forfeits a load-bearing
invariant; revisit only if the armed lap proves too slow in practice.

Milestones: (a) dev-channel contract, including re-CAST-free rebinding for dev
activations only (relax the exact ContentHash match strictly for armed dev-channel
activations, receipt every rebind); (b) PASS-line receipt composition in the cockpit;
(c) return path (connected/armed, current stage, last rejection); (d) the intent's
E2E — r1 → activate → observe → r2 live → verify → rollback → verify → publish
independently — automated synthetically and run manually on OMEN; fix the single-deep
ping-pong rollback (keep N previous active-sets, activation_id-addressed).

Exit: an hour of iteration on the lap Event with no restart, no manual file moves,
trustworthy receipts.

## Phase 3 — Adaptive event semantics (05)

**Key decision — keep the loaded-scene binding model; spatial semantics are
event-side, not binding-side.** Adapters stamp position fields on normalized events; a
canonical `AreaAnchor` (authored anchor | binding zdo | player | coordinates) plus a
`SpatialEvaluator` in Contracts evaluate
`within_radius/entered/left/remained/count_in_area` as trigger extensions.
`Vector3.Distance` lives in a new observation module, never in the pinned Bindings
region; the README's "no fixed radius" statement stays true of binding discovery (add
a clarifying sentence); extend the arcane-sight python pin rather than weaken it.

Milestones, per the intent's own order: fact inventory → temporal predicates first
(`time_since_stage_entered` rides Slice 1 timestamps; combat-duration and
no-progress need named measure slots on `WorkflowProgress`, since history clears per
transition) → area/anchor plus spatial predicates → performance facts (wave clear
time, deaths, remaining enemies via `SpawnExecutionStore`) → compiler validation →
`Explain` showing actual-vs-threshold (the Slice 1 trace format has the slot) →
Studio five-level presentation (prose / structured / graph / JSON / runtime values) →
one real Event per primitive on the dev channel. Sub-decision: new
`TriggerExpression` ops force compiler, rehearsal, evaluator, and Lab catalogs to
move together — write the schema-gate test first.

Palette admission (guardrail 2): every predicate this phase implements ships in the
extended/advanced palette; nothing joins the beginner beat palette until a validation
lap demonstrates an authored quest that needed it. Implementation breadth and default
exposure are separate decisions made at separate times.

Exit: the "ten-minute desperate defense" lap Event with every adaptive branch
explained in Arcane Sight.

## Phase 4 — Quest Lab spellbook (02)

Key decisions, made jointly with Phase 5's manifest: pattern identity
`pattern:<slug>@<semver>` plus SHA256 of the canonical fragment (same discipline as
`QuestPackContent.ComputeHash`); notebook store `comfy-quest-notebook/` beside the
runtime config root, schema `comfy-quest-notebook/v1`, one JSON per entry (id,
version, attribution, explanation, required primitives, example config, sharing
permission, optional canonical fragment) — independent of any Event, discoverable by
Studio through the existing `FindValheim` host seam; permission vocabulary = enum
`hidden|explainable|share_selected|remixable`, enforced at explain/save time,
explicitly no ACL. Reuse `QuestAuthoring.Diagnose` and the scenario cockpit as the
Explain engines; the Save action lands beside the read-only generated journal (the
notebook is a separate store, so the CI drift gates are untouched). Studio: a
notebook browser in advanced tools plus "start route from pattern"; agents reference
notebook ids during authoring — through the same revision-guarded v2 API and pattern
identities as humans, per guardrail 6; no agent-only formats or endpoints.

Exit: two local profiles standing in for two creators; notebook entries survive
Event deletion and round-trip into a new Studio draft.

## Phase 5 — Community-ready artifacts (04)

Key decisions: a **`comfy-quest-pack/v3` manifest** — v2's four fields cannot carry
author, license, dependencies, required primitives, tags, sharing policy, fork
lineage, changelog, or notebook references. Runtime and Studio dual-read v2/v3,
single-write v3; `QuestPackStore.Inspect` is the one gate to touch. **Freeze the
legacy v1 python pack lane** (`tools/quest-packs`): mine its certification, badge,
and privacy machinery as prior art; do not absorb it. Compact string = prefix +
Base64(deflate(canonical JSON)) + CRC, always re-validated through
`ExperienceCompiler` on import — never trusted directly. Import is the missing half
of the existing export-bundle machinery, built with provenance fields
(imported-from, imported version, fork parent); `Duplicate` records lineage instead
of appending `-copy`. Semantic diff in C# Studio at route/threshold/action level,
with the v1 python differ and the legacy `QuestStudioService.Diff` as prior art.

Exit: the intent's first-slice checklist demonstrable locally — export a pattern,
import it into a second profile, modify with provenance preserved, semantic-diff
against upstream.

---

## Verification (every phase, in order)

1. `dotnet test network/mod/ComfyQuestLab.Tests` (CI)
2. `python -m unittest discover -s tests` (CI — all pins and drift gates)
3. `dotnet test src/Quest.Studio.Tests` (local only)
4. `tools/quest-studio/Test-QuestStudioE2E.ps1` (local only)
5. OMEN mod compile + manual validation lap per `docs/runbooks/I2-QUESTPACK-OMEN.md`

Backlog item (out of program scope): add the Studio test lanes to `ci.yml`.

## Risks

1. **Pinned-test blast radius** — the python suite pins source text; every pin update
   must be deliberate and enumerated (arcane-sight markers, Bindings no-distance
   slice, vnext one-line-JS / id-cross-check / plumbing-words). Phase 3 spatial work
   is the highest-risk interaction; the event-side design exists to avoid weakening
   the pin.
2. **Schema evolution** — additive-on-v1 is safe while readers and writers share the
   Contracts class; the published NuGet means external consumers exist in principle —
   bump minor versions and document the policy. The ActiveSet additive field is
   verified against the exact-schema rollback check (tested in 1.1).
3. **Evidence volume** — ~8 KB per matched transition on an unbounded receipt store;
   cap enforced in `Explain`; add a retention backlog item if OMEN laps show growth.
4. **Timestamp determinism** — use event `At`, never wall clock, in workflow state.
5. **One-line JS maintainability** — keep browser logic thin by resolving labels
   server-side.
6. **OMEN dependency** — engine and Arcane Sight changes cannot compile in CI; land
   Contracts-level logic first and batch OMEN trips.
7. **Dev-channel rebinding (Phase 2)** — relaxing the exact ContentHash match weakens
   a fail-closed invariant; scope it strictly to armed dev-channel activations and
   receipt every rebind.

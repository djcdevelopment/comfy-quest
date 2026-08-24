# Creator portfolio requirements

Status: adopted product baseline, 2026-08-24.

## Outcome

The adoption gate is not another generated tutorial. It is this: **Derek can create,
run, revise, reset, organize, and release a complete guild-scale body of quests and
repeatable events through the Creator OS without becoming the fleet's KVM.** The work
should feel like authoring a world, not operating a mod deployment.

The immediate product loop is:

`Imagine -> Author in the world and Studio -> Rehearse -> Play -> Observe -> Revise -> Reset -> Run again -> Release`

Automation, templates, and saved patterns may accelerate that loop after repeated use
shows where they help. They are not substitutes for the authored source, the playable
world, or Runtime evidence, and they are not on the adoption critical path.

## Scope and product model

These terms are the intended durable product model. They are requirements, not claims
that the current v2 pack or Studio schema already implements them.

- A **portfolio** is one creator's local library. It must remain useful with dozens or
  hundreds of artifacts and may contain more than one guild.
- A **guild** is a releasable authored experience: identity, version, world bundle,
  questlines, standalone quests, repeatable events, progression coverage, dependencies,
  provenance, and release history.
- A **questline** orders or branches quests through explicit prerequisites and unlocks.
- A **quest** is one bounded experience graph with bindings, named anchors, outcomes,
  eligibility, and a retry/reset policy.
- A **guild event** is a rerunnable experience with participant scope, start policy,
  cooldown or cadence, outcome, cleanup, and evidence.
- A **world bundle** is the closed-game `.db`/`.fwl` pair plus stable world identity,
  compatibility facts, named entry points and anchors, hashes, and the exact guild
  release it supports. Runtime progress and character state are separate concerns.
- A **run** is one execution instance with participants, content identity, world and
  binding identity, progress, outcome, evidence, and reset lineage.
- A **pattern** is optional reusable craft earned from repetition. It is not a new
  authoring format and never becomes required to make a quest.

## Current baseline and gaps

This table is grounded in the surfaces that currently declare product behavior. It is
the reason for the requirements below rather than a second implementation of those
rules in prose.

| Surface | Current machine truth | Product gap exposed by guild authoring |
| --- | --- | --- |
| Experience contract | [`ExperienceContract.cs`](../network/mod/ComfyQuestContracts/ExperienceContract.cs) admits a 1 MiB document, 1..64 stages, up to 128 trigger leaves, 256 actions, expression depth 3, 32 named anchors, seven trigger operators, five adaptive measures, and five spatial predicates. | The graph can express a substantial quest, but it has no guild, questline, prerequisite, event cadence, or run-policy model. |
| Creator vocabulary | [`CreatorEventCatalog.g.cs`](../network/mod/ComfyQuestContracts/CreatorEventCatalog.g.cs) declares 34 creator-safe events backed by shipping Runtime adapters. `ExperienceCompiler` admits six effects: message, start/cancel timer, grant item, spawn, and clear spawned. | Studio must make the claimed vocabulary usable at portfolio scale. New effects enter only when a real authored guild idea is blocked. |
| Mutation safety | [`MutationRegistry.cs`](../network/mod/ComfyQuestContracts/MutationRegistry.cs) intentionally allowlists four grant items, two creatures, three spawned items, and two pieces. | The palette is safe but deliberately narrow. Dogfooding, not catalog speculation, decides each expansion. |
| Studio document | [`StudioProjectDocument`](../src/Quest.Studio/QuestStudioWorkspace.cs) schema 3 still owns one independent experience draft. [`QuestStudioPortfolio.cs`](../src/Quest.Studio/QuestStudioPortfolio.cs) now adds a bounded v1 local portfolio and guild contract with stable identity, semantic version, provenance, progression bands, questlines, standalone quests, events, optimistic revisions, transactional placement, and deep-fork import/export without rewriting project schema. | Prerequisites/unlocks, reachability, event cadence/start policy, batch readiness, named-anchor authoring, release history, and a multi-experience guild artifact remain absent. |
| Runtime pack and binding | [`QuestPackStore`](../network/mod/ComfyQuestContracts/RuntimeContract.cs) validates one or more experience documents in a v2 archive, but [`RuntimeCharmBinding.TryActive`](../network/mod/ComfyQuestRuntime/RuntimeCharmBinding.cs) rejects a second document as `active_experience_ambiguous`. | A guild cannot ship and bind as a multi-experience unit, and multiple active quests cannot be selected or understood independently. |
| Runtime progress | [`RuntimeRuns.cs`](../network/mod/ComfyQuestContracts/RuntimeRuns.cs) now registers one active run for the exact world, experience, binding, participant, and content scope while adopting legacy workflow keys in place. Reset previews workflow, timers, action claims, and owned spawns; refuses ambiguous cleanup; retains prior durable state; starts a linked successor with fresh per-run claims; and resumes idempotently after partial cleanup. Runtime receipts carry run, experience, and world identity. | Clean-versus-resume is not yet a pre-play choice, the shipping engine still supplies one local participant, and this automated contract proof has not yet received its batched live adapter acceptance. |
| Studio operations | The [v2 endpoints](../src/Quest.Studio/QuestStudioEndpoints.cs) now expose the portfolio/guild hierarchy and exact run status, reset preview, confirmed reset, and asynchronous correlated receipt pickup in addition to project create/import-fork/duplicate/save/validate/certify/rehearse/play/publish/history/export. | Portfolio-wide readiness, pre-play clean/resume, friction capture, and guild release operations remain to be built. |
| World authoring | [Creator Session](../tools/creator-session/Invoke-CreatorSession.ps1) backs up the closed-game world pair and can enable verified private Godbuild mode. Godbuild capture emits reviewable source, but its [manifest](../examples/worldbuild/first-portal-progression-shelter/manifest.json) explicitly excludes terrain/vegetation, portal links, container contents, door state, and arbitrary ZDO fields. | A capture is a useful module, not the playable authored environment. There is no versioned, release-ready world-bundle workflow. |
| Evidence | [`RuntimeReceiptStore`](../network/mod/ComfyQuestContracts/RuntimeReceipts.cs) writes correlated JSON receipts and lists at most 200 per read; gameplay and reset receipts now carry run identity, and run-control receipts retain a bounded 128 files. Rehearsal declares its proof level and limitations. | General Runtime receipt retention, complete per-run grouping/lineage, guild coverage, creator-altitude reset diagnostics, and archive/export remain incomplete. |
| Lab ownership | [`quest-lab-persona-audit.md`](quest-lab-persona-audit.md) finds that Lab is primarily observation and release machinery, Studio owns authoring, and no in-world editor exists. | Builder tools remain supporting infrastructure. Creator context must cross Studio and Runtime without pretending Lab is the portfolio editor. |

## Functional requirements

### Portfolio and progression

- **FR-PORT-001 — Portfolio workspace.** Studio must let the creator create, name,
  search, filter, archive, duplicate, import, and open guilds, questlines, quests, and
  repeatable events without navigating a flat project pile. The current artifact and
  unsaved state must remain obvious.
- **FR-PORT-002 — Guild identity.** A guild must have a stable id, semantic version,
  title, author, description, progression scope, compatibility declaration, provenance,
  and release state. Renames must not change identity.
- **FR-PORT-003 — Questlines.** The creator must be able to order and branch quests,
  declare prerequisites and unlocks, see unreachable or cyclic content before release,
  and understand the rule in creator language.
- **FR-PORT-004 — Progression coverage.** A guild must be able to label content with
  creator-defined progression bands and show uncovered, blocked, or over-coupled bands.
  The system must not infer Valheim progression from prefab names.
- **FR-PORT-005 — Batch readiness.** The guild view must aggregate draft, rehearsal,
  live-run, reset, and release evidence for every included artifact while retaining the
  ability to inspect one exact quest and revision.

### Quest and event authoring

- **FR-AUTH-001 — One artifact at every altitude.** Beginner beats, advanced graphs,
  canonical JSON, agent edits, and Runtime execution must round-trip through the same
  contract without flattening branches or silently dropping fields.
- **FR-AUTH-002 — Claimed event access.** Every event advertised as creator-safe must
  be discoverable by label, category, explanation, target, fields, proof level, and
  adapter status. Unsupported or unavailable events must fail closed before publication.
- **FR-AUTH-003 — Composition.** Authors must be able to use event, any, all, count,
  sequence, threshold, and spatial conditions; branching priorities; timers; entry and
  transition effects; terminal outcomes; and the current bounded adaptive measures.
- **FR-AUTH-004 — Effect admission.** Message, timers, bounded grant, bounded spawn,
  and marked cleanup remain the initial effect surface. A new mutation is admitted only
  when an authored guild idea records the block, its safety contract, its rehearsal
  model, and its live receipt proof.
- **FR-AUTH-005 — Repeatable guild events.** A guild event must declare how it starts,
  who participates, whether late join is allowed, success/failure/cancel outcomes,
  cooldown or cadence, cleanup, reward policy, and whether a new run is clean or resumes.
- **FR-AUTH-006 — Safe preservation.** Save conflicts, advanced-graph preservation,
  source lineage, and semantic change summaries must remain enforced as the hierarchy
  grows. Bulk operations may not bypass per-artifact validation.

### World-first authoring

- **FR-WORLD-001 — Named live anchors.** From a pinned private-world session, the
  creator must be able to capture a point, radius, region, or allowed object target by
  looking at the world. The machine records coordinates, world identity, target identity,
  and a creator name; no console command or file relay is allowed.
- **FR-WORLD-002 — Anchor authoring.** Studio must list those named anchors, preview
  their world and compatibility, and compile anchor references into the existing
  contract. Missing, wrong-world, or stale anchors must produce a diagnostic before play.
- **FR-WORLD-003 — World bundle.** With Valheim closed, the toolchain must create a
  versioned bundle containing the exact `.db`/`.fwl` pair, canonical hashes, stable world
  uid and name, game/mod/contract compatibility, entry points, anchor catalog, linked
  guild release, and declared exclusions. The bundle must be inspectable without Valheim.
- **FR-WORLD-004 — World is the playable source.** Terrain, vegetation, portal topology,
  authored spacing, and other world-native state remain authoritative in the saved world.
  Godbuild captures may provide modular source, preview, diff, and replay; a capture may
  not claim to reproduce fields its manifest excludes.
- **FR-WORLD-005 — World release lifecycle.** Install, upgrade, rollback, clone-for-test,
  and uninstall must be atomic, hash checked, path bounded, and unavailable while Valheim
  owns the files. Existing user worlds may never be silently overwritten.

### Creator loop and run control

- **FR-LOOP-001 — Persistent context.** Studio, the overhead Runtime surface, Creator
  Session, receipts, and exported evidence must carry the same guild, quest/event,
  revision, world, and run identities so the creator returns to the same telling.
- **FR-LOOP-002 — Bounded session ownership.** One Creator Session owns deployment,
  install lease, backups, safety gates, request expiry, capture transport, proof, and
  restoration. Its status must expose every precondition the next operation needs.
- **FR-LOOP-003 — Clean and resume modes.** Before play, the creator chooses a clean
  run or a deliberate resume. The system shows what each means for world state, quest
  progress, timers, spawns, rewards, participants, and evidence.
- **FR-RESET-001 — Scoped reset.** Reset must target an explicit run, quest/event,
  binding, participant scope, and world. It must reconcile workflow progress, durable
  timers, action-execution claims, spawned-object ownership and cleanup, pending
  transitions, Runtime notices, and any reward policy involved.
- **FR-RESET-002 — Preview and receipt.** Before mutation, reset must show its exact
  blast radius and refuse ambiguous scope. After confirmation it writes a correlated
  receipt listing every changed store and cleanup result, keeps recoverable prior state,
  and is idempotent on retry.
- **FR-RESET-003 — Rerun terminal content.** A completed quest or guild event must be
  rerunnable without renaming the world, character, binding, or content hash. The new run
  receives a distinct run identity and cannot contaminate previous evidence.
- **FR-RUN-001 — Multi-experience play.** A guild release must carry multiple quest and
  event documents. Runtime must select or bind a specific experience without ambiguity,
  maintain independent progress for simultaneous runs, and show the current objective,
  outcome, and retry state for each.
- **FR-RUN-002 — Authority and participants.** Every live mutation and transition must
  declare host/peer authority and participant scope, fail closed when authority is
  uncertain, and explain the rejection at player and creator altitudes.

### Release, evidence, and refinement

- **FR-REL-001 — Guild release artifact.** A release must bind the guild manifest,
  quest/event documents, world bundle, dependency and primitive requirements, license,
  tags, sharing policy, fork lineage, changelog, and canonical hashes into one
  self-verifying export. Existing v2 questpacks remain readable during migration.
- **FR-REL-002 — Import and semantic diff.** Import creates a local fork with provenance
  preserved. Upgrade and review show semantic changes at guild, progression, questline,
  route, condition, action, anchor, world, and dependency levels before activation.
- **FR-REL-003 — Atomic activation and rollback.** Installation and activation must
  validate the complete dependency/world/content set before changing the active release.
  Rollback restores one previously verified release identity, not a hand-picked mixture.
- **FR-EVID-001 — Run evidence.** Studio must group accepted and rejected receipts by
  run and render route progress, actual-versus-expected values, effects, outcome, reset
  lineage, and proof limitations in creator language, with raw evidence available below.
- **FR-EVID-002 — Coverage ledger.** Each advertised capability must map to at least one
  checked-in contract test and, where it claims live Valheim behavior, one genuine Runtime
  receipt. Guild artifacts show which capabilities they exercise; synthetic rehearsal is
  never labeled live proof.
- **FR-EVID-003 — Dogfood friction ledger.** From the current guild/quest/run context,
  the creator must be able to record a blocked idea or rough edge without reconstructing
  identities. The entry tracks the authored intent, observed evidence, requirement, tool
  change, and rerun result. Free text stays local unless explicitly exported.
- **FR-OPT-001 — Earned reuse.** Pattern/notebook work begins with structures Derek has
  actually repeated. Saving a pattern preserves identity, attribution, required
  primitives, explanation, and canonical fragment, but using it stays optional.
- **FR-OPT-002 — Optional generation.** Auto-generation may suggest drafts, patterns,
  test inputs, or release metadata. It must use the same contracts and validation as a
  human edit, preserve authored source, declare omissions, and never gate the manual
  world-first path.

## Non-functional requirements

- **NFR-SEAT-001 — Seat efficiency.** No flow may ask Derek to relay console commands,
  copy files, read hashes, report logs, press keys for automation, or verify facts the
  machine can observe. A planned creative session may require one launch, world entry,
  authored play/build time, and one quit; extra lifecycle turns are defects with receipts.
- **NFR-SEAT-002 — Judgment boundary.** Human acceptance is limited to authorship,
  spatial composition, visual hierarchy, narrative tone, and play feel. Identity,
  deployment, state, compatibility, and success/failure are machine verdicts.
- **NFR-SAFE-001 — Recoverability.** Every mutation has an exact target, preflight,
  atomic write or transaction boundary, recoverable prior state, correlated evidence,
  and a tested rollback. Destructive world operations require Valheim closed and explicit
  confirmation.
- **NFR-INTEGRITY-001 — Determinism.** Canonical serialization, content hashes,
  versioned schemas, generator drift checks, and check-before-load/release apply to every
  artifact. Unsupported data is rejected or declared; it is never silently discarded.
- **NFR-BOUND-001 — Bounded work.** Contract size, graph, history, archive, request,
  receipt-read, and live-evaluation limits remain explicit and executable-tested. New
  portfolio limits and Runtime frame budgets must be declared before their feature ships.
- **NFR-EXPLAIN-001 — Explainability.** Every failed precondition or rejected branch has
  a stable diagnostic code, creator-language cause, remediation, and raw detail. No blank
  state, filesystem path, hash, or `snake_case` token stands alone at creator altitude.
- **NFR-PRIV-001 — Local first.** Worlds, drafts, friction notes, usage evidence, and
  receipts remain local by default. Export and sharing are explicit; telemetry never
  includes authored prose, world paths, player identities, or exact activity timelines.
- **NFR-SEC-001 — Closed authority.** Runtime accepts only allowlisted operations,
  events, mutations, safe local identifiers, and bounded repository-owned paths. No
  arbitrary command, synthetic input, sibling-checkout reach-in, or silent multiplayer
  authority escalation is introduced.
- **NFR-COMPAT-001 — Portability.** Artifacts declare game, mod, contract, world, and
  dependency compatibility. Repository tools derive paths from repository/install
  identity and do not assume Derek's checkout layout.
- **NFR-USE-001 — Hundredth-use composition.** Common author, play, observe, reset, and
  rerun actions remain visible and keyboard/mouse usable at supported viewport sizes.
  Advanced machinery is available without taxing the normal reading order.
- **NFR-OBS-001 — Retention.** Receipt and evidence stores have explicit size/age
  retention with archive/export before deletion. Reads stay bounded, and pruning one run
  cannot break another run's audit chain.
- **NFR-TEST-001 — Split proof.** Pure contracts, Studio workflows, generators, and
  failure branches are automated. Rehearsal publishes `proof_level`, `disclaimer`, and
  per-run `limitations`. Only adapter behavior needs a batched live lap, and a human is
  never used to reproduce a machine-observable assertion.

## Adoption roadmap and exit gates

### 4A — Dogfood foundation

Build portfolio hierarchy, named live anchors, scoped reset/rerun, and saved-world
bundling before adding more generated content.

Exit: Derek authors a guild slice containing a questline with more than one quest and
one repeatable event, runs it from a packaged world, resets one completed experience,
and reruns it under a new run identity. The lap requires no repository edit, console,
manual file transfer, log reading, or machine-fact relay from Derek.

### 4B — Guild campaign

Add cross-quest prerequisites/unlocks, multi-experience release and Runtime selection,
portfolio-wide readiness, and atomic guild/world activation and rollback.

Exit: one top-to-bottom guild campaign covers every progression band the creator declares,
every included quest/event has rehearsal evidence, and all live-adapter claims used by
the guild have run receipts. A clean install can play it from its released world bundle.

### 4C — Refinement through use

Author additional guild ideas, log friction in context, and expand events/effects only
where a desired experience is blocked. Promote repeated structures into optional patterns
after their value is observed.

Exit: multiple full author-to-rerun cycles complete without a KVM turn, every accepted
tooling change links back to an authored need and forward to rerun evidence, and the
common loop remains coherent at hundredth-use density.

### 5 — Community release

Finish provenance, dependencies, permissions, import/export, semantic diff, and the
community-ready guild artifact after the local dogfood loop is real.

Exit: export one guild, import it into a clean local profile, modify it as a fork, inspect
the semantic/world diff, install it atomically, and preserve upstream lineage and evidence.

## First dogfood artifact

`examples/worldbuild/first-portal-progression-shelter` is the first captured human-spaced
module: 12 basic pieces, a 4.5689 m by 3.9197 m footprint, 3.5568 m height, and source
piece hash `e1e01ff675017bc6ed1d83868b3dcd5f9bb9a2f84089721dfa032fb79c537dfd`.
Its simple wood, sign, roof, and fire composition communicates early-game progression;
the uphill clearing observed from it is the next authored stage. Its manifest is also
the proof that this module cannot carry the surrounding terrain or portal topology.
Those remain in `ComfyQuestDemo`, which is why the saved world—not auto-generation—is
the playable release direction.

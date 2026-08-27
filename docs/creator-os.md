# Creator OS

> **This file is the operating strategy — why the loop is shaped the way it is. It is not
> the plan, the lane vocabulary, or the work queue.** Those are separate authorities, and a
> cold start reads them in this order:
>
> <!-- reading-order:begin -->
> 1. `docs/PLAN.md` — **The plan.** Goal, where we are, what happens next, and what needs Derek. Read this alone and you know the program.
> 2. `docs/handoff-2026-08-24.md` — **Session record.** What the 2026-08-24/25 sessions did, the environment traps, and the cold-start checks.
> 3. `docs/five-intent-program-plan.md` — **Why this exists.** The ethos the whole program is judged against: six product guardrails and one communication guardrail.
> 4. `docs/creator-os-build-strategy.md` — **The plan.** Lane order, the program invariant, and what each lane may not do.
> 5. `docs/creator-os-phases.json` — **Lane vocabulary.** The only definition of a lane, and the human boundary.
> 6. `docs/creator-requirements-ledger.json` — **Requirement dispositions.** Who is accountable for each of the 47 requirements right now.
> 7. `docs/quest-mission-control.json` — **Work queue.** Every work item with its lane and requirement lineage.
> 8. `docs/creator-os.md` — **Operating strategy.** Why the loop is shaped this way, and the golden rule.
> 9. `docs/creator-portfolio-requirements.md` — **What is required.** The 47 FR-/NFR- requirements themselves.
> 10. `docs/adr/README.md` — **Decisions.** Append-only; a reversal needs a superseding record.
> 11. `docs/creator-os-audit-2026-08-24.md` — **Findings.** What was wrong on 2026-08-24, with `file:line` receipts.
> <!-- reading-order:end -->
>
> The order is declared in `docs/quest-mission-control.json` under `reading_order` and
> checked on every render.

The scarce resource is the creator's time in the seat. Build, deployment, identity
checks, bounded world operations, capture transport, and proof collection belong to the
machine loop. The seat is for spatial judgment, authored choices, and play feel.

**Golden rule: do not spend Derek's seat time discovering machine-observable defects.**
The fleet, hardware, agents, browsers, game clients, request mailboxes, receipts, logs,
and capture surfaces scale. Derek's cognitive time does not. Be ready before calling
him: drive the actual vertical slice, fix what it exposes, rerun it, collect the evidence,
and reduce the eventual seat request to one prepared batch of genuinely human judgments.

## Results-driven operating strategy

Quest is one product lane in a much larger vertical slice. Quest owns its contracts,
Studio, Runtime, Lab, creator tools, request/receipt surfaces, and release artifacts.
Isolate owns the standalone turnkey Workbench and development MCP runtime. Baseline owns
the cross-product vertical slice and composes exact released artifacts; it is not a reason
for Quest to reach into sibling source trees. NetworkSense and Lumberjacks provide their
own released observation, lifecycle, and multiplayer capabilities.

The unit of delivery is a usable creator result across real boundaries, not a layer-local
model or a test count:

- Implement coherent changes through the contract, Studio GUI/API, serialized artifact,
  Runtime, installed mod, real Valheim process, and receipt/evidence return path wherever
  the feature crosses those layers.
- Drive the actual browser and game-facing surfaces. Preserve screenshots, logs, request
  and receipt envelopes, installed hashes, process lifecycle evidence, and resulting
  persisted state. Those observations are more valuable than another mock that agrees
  with the implementation that created it.
- Debug downward only when the full-width journey exposes a fault. The existing tools can
  isolate contract, serialization, filesystem, process, Unity, adapter, network, and UI
  failures after there is a real failing observation to explain.
- Prefer sweeping, internally coherent product changes that unlock authoring over a queue
  of small infrastructure exercises. A change is valuable when the creator can do
  something materially new through the product.

## Evidence strategy

Evidence priority is deliberately asymmetric:

1. **Automated real vertical-slice journeys.** Build and deploy the exact artifacts,
   launch the real processes, drive the Studio GUI and bounded in-game controls, observe
   the installed Runtime, and retain the resulting files, screenshots, logs, and receipts.
2. **Boundary integration tests.** Exercise actual serialization, process restarts,
   filesystem exchanges, package consumption, endpoint identity, failure recovery, and
   state continuity without substituting in-memory doubles for owned boundaries.
3. **Focused unit tests.** Use them for dense algorithms, safety invariants, parsers,
   deterministic reducers, and exact regressions found by the higher layers. They support
   delivery; they never define product completion by themselves.

When a roadmap item ends in a Derek-in-the-seat acceptance, keep new unit coverage lean
or omit it when it would only restate the new model. Spend that budget making the
automated harness drive farther through the real GUI/game journey. Report which journey
ran and what evidence it produced, not how many tests agreed with the code.

Use these proof states without promotion by implication:

- **Implemented:** the product code exists.
- **Integrated:** the real owned boundaries exchange the intended bytes and state.
- **Autonomously driven:** the installed application and game journey completed without a
  human relay and retained evidence.
- **Ready for seat:** every machine-observable gate is green, rollback is ready, and the
  remaining questions require human authorship or perception.
- **Human accepted:** Derek supplied those prepared judgments in one bounded sitting.

## Ready before the seat

Before asking Derek to enter Valheim or operate Studio for acceptance, the implementing
agent owns all applicable preparation:

- exact build, install, identity, configuration, world/content selection, launch, and
  clean shutdown;
- a driven Studio-to-artifact-to-Runtime journey using the actual GUI and exchange files;
- bounded in-game requests and correlated receipts for every machine-verifiable claim;
- automatic collection of logs, screenshots or window captures, persisted state, and
  failure diagnostics;
- correction and rerun of every known mechanical failure, including first-launch,
  restart, stale-state, and upgrade paths affected by the change;
- a recoverable prior state and rehearsed cleanup path; and
- one concise batch of remaining questions limited to composition, clarity, narrative,
  spacing, responsiveness, fun, or other human perception.

If those conditions are not met, the agent is not ready to call the seat. Derek is never
the keyboard relay, screenshot courier, log reader, hash checker, process monitor, or
manual retry loop for the fleet.

## Standalone Workbench boundary

The development MCP used by this program must be a released Isolate Workbench instance
that can stand up independently on any volunteer machine. It may not discover, import,
fall back to, share credentials with, share state with, or carry a runtime reference to
HEARTH or any other private operator gateway. A responsive localhost port is not
identity.

Before Quest relies on that surface, a clean-machine acceptance must prove the exact
Isolate project identity, immutable source/image identity, profile, provider allowlist,
generated local caller credentials, bounded state roots, and teardown. The volunteer
profile may not depend on Derek, OMEN, AM4, private hostnames, private paths, sibling
checkouts, or historical fleet defaults. Quest contributes only bounded product-owned
commands and receipts; it does not clone the MCP kernel or the Valheim lifecycle harness.

## Proof level

- `Invoke-CreatorSession.ps1` has an executed fixture-mode lifecycle covering Prepare,
  Status, Close, exact plugin/config/world-pair/character restoration, its closed action vocabulary,
  exclusive lease, install hash pins, backups, and rollback manifest. A forced late Prepare
  failure also proves partial plugin deployment and quarantined one-shot state roll back, and
  `Stop` remains available as a process-recovery fail-safe when an installed hash drifts. A second executable
  path drives Creator Session through the PowerShell sender, real Runtime request file,
  shipping controller, correlated receipt, BuildOn, BuildOff, Arm, Disarm, and Restore.
  Build control verifies both Valheim no-cost/all-pieces and god-mode state, rejects
  enabling in an unconfirmed private world, keeps disabling available as a fail-safe, and
  cannot carry a console command or synthetic key. The Lab
  sender has a corresponding correlated local round trip and fails on receipt identity
  drift.
- Bounded `Launch` and `Stop` compile against the installed Valheim assemblies and have live
  execution proof. `Launch` pins profile filename, world filename, display name and UID, machine,
  session, and expiry; `Stop` uses graceful-then-forced process recovery and archives the game
  logs. The complete installed 4A guild journey remains a separate, still-gated claim.
- Quest Lab capture normalization, projection, check-before-build, durable marking, and
  translation-independent diff are executable-test covered. The Godbuild importer is
  exercised through write, clean `--check`, and deliberate-drift rejection.
- The corrected control plane completed a live Valheim lap in Creator Session
  `creator-os-r31-corrected-20260824`: installed hashes matched, Runtime Arm completed,
  Status reported armed/watching, Lab `gallery_identify` completed, and Close returned a
  correlated Disarm receipt before clearing private-world confirmation. Lab reported
  87/87 seams and 10/10 quests armed; the corrected launch log contains no plugin, UI,
  GUILayout, null-reference, or patch failure.
- A direct Valheim window-buffer capture found one visual defect without asking the
  creator to relay it: the 36-pixel compact bar at y=48 was wholly behind the observed
  host diagnostic band ending at y=84 in the 1026x740 live viewport. Production geometry
  now uses the pure, executable-tested `RuntimeCreatorBarLayout` with a y=92 safe top and
  on-screen clamping. Creator Session `creator-20260824T105854Z-b55c8b18` deployed those
  exact hash-pinned bytes before the next process started. A no-activate direct buffer
  capture then showed the complete compact bar immediately below the host band at the
  same viewport; the capture cycle restored the game's minimized state without changing
  foreground focus or sending input. Close returned a correlated Disarm receipt, private
  confirmation is false, and the launch log has no exception, fatal, or error entry.
- Creator Session `creator-first-godbuild-buildmode-20260824` completed the first live
  world-authoring lap on the exact `ComfyQuestDemo` world. Correlated BuildOn request
  `runtime-build_on-20260824T130805Z-485fd57e` reported
  `creator_build_enabled: true`; Capture and Inspect recorded 12 creator-owned pieces
  within 12 metres as `first-portal-progression-shelter`, source-pieces SHA256
  `e1e01ff675017bc6ed1d83868b3dcd5f9bb9a2f84089721dfa032fb79c537dfd`, with the
  capture and PlanBuild projection internally consistent. BuildOff request
  `runtime-build_off-20260824T133209Z-187c0af5` reported
  `creator_build_enabled: false`. The closed session manifest records the exact prior
  plugin/config restoration and a released session; the authored world was deliberately
  retained. Its pre-session `.db`/`.fwl` pair remains in the session backup. Ordinary
  `Close -Restore` still retains authored world state; a technical lap may explicitly use
  `Close -Restore -RestoreGameState` after `Stop` to restore the exact pinned world pair and
  character profile.
- Creator Session `binding-fixture-smoke-20260827-r5` executed the autonomous installed-game
  preflight without a human relay. Steam entered `questyfour` / `ComfyQuestDemo` with the exact
  UID; reviewed replay produced `MATCH` for 12/12 pieces; Runtime returned 32 bounded binding
  candidates including the fixture sign at 5.6 metres; `Stop` closed gracefully; and `Close
  -Restore -RestoreGameState` restored the `.db`, `.fwl`, and `.fch` files to their pre-lap
  SHA256 values while leaving zero plugin files. This proves the launch, deterministic binding
  fixture, and recovery choreography, not the still-pending full A -> B -> A guild journey.
- Installed journey `queue-full-width-journey-20260827-r6` autonomously continued through guild
  authoring, immutable publication, exact world entry, fixture replay, dev activation, and the
  real 32-candidate binding selector. Its rejected bind receipt proved that a periodic Studio
  render had replaced the selected sign ZDO with the nearest wood-pole ZDO before submission.
  Studio now preserves an existing candidate selection across renders, and the synthetic browser
  journey explicitly selects the non-first sign, forces a re-render, and verifies the exact ZDO
  remains selected. The installed correction still requires a complete rerun before promotion.
- Installed journey `queue-full-width-journey-20260827-r7` passed the repaired sign selection,
  retained the locked-B prerequisite refusal, and completed A and B under independent run IDs.
  Its next evidence-only B preview exposed the installed helper posting to reset apply instead of
  reset preview. The helper now posts to `runs/reset-preview`, returns the API's exact diagnostic
  rather than an opaque browser exception, and executes in the synthetic guild journey. Cleanup
  again stopped Valheim gracefully, removed all plugins, and restored the world pair and character
  profile to their pinned hashes. The corrected full installed lap remains pending.
- Installed journey `queue-full-width-journey-20260827-r8` confirmed the non-mutating preview URL
  and returned `run_scope_not_loaded`: B's durable completion was visible before Runtime's next
  status heartbeat made that exact run eligible for scoped control. The installed helper now waits
  for fresh connected status containing the exact project/run pair before it requests a preview,
  and that precondition runs in the synthetic guild journey. Cleanup again restored all three
  game-state hashes and left no Valheim process or installed plugin.

An earlier live launch exposed a success-sentinel defect before Arm dispatch. The
validator now returns null on success, and both the executable controller round trip and
the corrected live Arm/Disarm receipts cover that exact branch.

## Creator session loop

1. With Valheim closed, run `tools\creator-session\Invoke-CreatorSession.ps1 Prepare`. Prepare takes the install-wide lease, builds and hash-verifies the payload, backs up exact plugin/config/world files, enables the private-world safety gate, and records the machine/world/session pins used by every later request.
2. The installed driver defaults to `Launch`, which starts Valheim through Steam and requests the exact profile and local world named by the session manifest. Runtime refuses a missing or ambiguous profile, absent or mismatched world UID, wrong machine/session, expired request, server join, console command, or synthetic input. `-HumanWorldEntry` retains the one-action ADR 0014 fallback while the autonomous path is being proven.
3. Run `tools\creator-session\Invoke-CreatorSession.ps1 Status -SessionId <id>`. Continue only while the session is active, the machine and world pins agree, and every installed plugin hash still matches Prepare.
4. For a Godbuild lap, run the bounded `BuildOn` operation and require its correlated receipt before the creator begins spatial work. While it is active, the creator uses ordinary hammer/build controls; run one bounded operation from `GalleryRebuild`, `Capture -BlueprintName <name>`, or `Arm` only when the lap calls for it. Every request expires, carries the same three identity pins, is consumed once in game, and has no console-command or synthetic-key field.
5. Capture automatically imports the fixed receipt artifact and runs the generator-drift check. Review `examples/worldbuild/<name>/preview.svg`, `plan.json`, and `manifest.json`; the manifest names upstream exclusions and the capture/blueprint pair remains replay authority.
6. Run `Replay -BlueprintName <name>` only after review. Replay hash-verifies and stages that exact reviewed pair, then performs check, build, and a mark-scoped translation-independent diff and fails unless the receipt says `MATCH`. Run `BuildOff` and require its receipt before leaving the loaded world; then use `Stop` to close the game and archive logs. Finish with `Close -Restore` to restore prior install bytes. A disposable technical lap adds `-RestoreGameState` to restore the exact pinned world pair and character profile; an authoring lap omits it and retains the authored world.

## Studio creator loop

The single-experience reading order remains **Author -> Rehearse -> Play -> Observe -> Capture Godbuild -> Replay elsewhere**. Runtime opens Studio with the active pack, version, requested stage, and current beat in the loopback query so Observe lands on the same telling rather than asking the creator to find it again.

The broader dogfood order is **Imagine -> Author in the world and Studio -> Rehearse -> Play -> Observe -> Revise -> Reset -> Run again -> Release**. The adoption gate is Derek creating a guild-scale portfolio of questlines, standalone quests, and rerunnable events through that loop. The requirements and exit gates are in `docs/creator-portfolio-requirements.md`.

The first real Godbuild target is complete. Its 12 basic pieces demonstrate the useful
part of capture: human spacing can become reviewable source, a typed plan, and a stable
module. The live observation supplied the more important product fact: ordinary wood,
roof, fire, and sign language locates a player in Valheim progression, while the terrain
and uphill clearing supply the next stage. The capture manifest explicitly excludes
terrain, vegetation, portal links, container contents, door state, and arbitrary ZDO
fields. Therefore the saved world is the playable release authority; Godbuild remains an
optional module/source/diff tool rather than a replacement for the world.

The safe-top follow-up completed on the next normal Valheim launch without a dedicated
seat session. Automation owned deployment, process/hash ordering, direct window-buffer
capture, host-band clearance, Disarm, and Close. No F9 press, screenshot relay, console
command, forced focus, or manual file copy was required. That is the standard for future
work: automate the real application journey, then spend the seat only on what the capture
cannot decide.

## Product roadmap

1. **Integrate the implemented foundation.** Portfolio hierarchy and exact run/reset
   identity exist. Drive them through the real Studio persistence and publication path,
   Runtime exchange, process restart, and installed-game receipt path before promoting
   their proof state.
2. **Unlock guild-scale execution.** Implement multi-experience guild artifacts,
   prerequisites and unlocks, Runtime experience selection, independent concurrent run
   status, and the creator event controls needed to author top-to-bottom guild ideas.
   Deliver these as full-width Studio-to-Valheim capabilities rather than isolated
   contract models.
3. **Make autonomous live integration routine.** Use the standalone Isolate Workbench,
   existing Valheim lifecycle harnesses, Quest's bounded inbox/outbox, browser driving,
   direct captures, logs, and receipts to run the installed vertical slice without Derek.
   Harden the standalone boundary before connecting it; never borrow HEARTH.
4. **Dogfood real guilds.** Only after the machine loop is ready, Derek authors and runs
   quests and repeatable creator events using ordinary building language and the current
   local working world. Add named anchors, event/effect vocabulary, and creator tooling
   when a real authored idea needs them. Feed observed friction directly into the next
   sweeping product change and rerun the same journey.
5. **Stabilize, then distribute.** After repeated guild authoring stabilizes the content,
   world, anchor, run, and compatibility contracts, build clean-profile community
   installation and release artifacts. Versioned `.db`/`.fwl` packaging, inspection,
   installation, and rollback are deliberately deferred until then. Ordinary recoverable
   world copies are sufficient during R&D.

## Capacity policy

- Phase 3's remaining combat-feel verdicts are parked and should be taken together in a
  later creative session. They do not gate Creator OS construction.
- i5 stays off for content-only and spatial work. Start the peer only when the multiplayer
  adapter itself changes.
- A feature is not complete because its unit tests pass. Promote it only through the
  implemented, integrated, autonomously driven, ready-for-seat, and human-accepted states
  that actually apply.
- If a known automated harness can launch, drive, observe, or close the relevant surface,
  using Derek for that operation is a test defect, not an acceptance step.
- A screenshot or checkbox is orientation, not machine proof. Request receipts, active-set
  identity, capture hashes, and `MATCH` are the machine facts.
- Auto-generation and the planned pattern notebook are optional accelerators. They earn
  critical-path priority only when repeated guild authoring demonstrates the need.

# Creator OS

The scarce resource is the creator's time in the seat. Build, deployment, identity
checks, bounded world operations, capture transport, and proof collection belong to the
machine loop. The seat is for spatial judgment, authored choices, and play feel.

## Proof level

- `Invoke-CreatorSession.ps1` has an executed fixture-mode lifecycle covering Prepare,
  Status, Close, exact plugin/config restoration, its closed action vocabulary,
  exclusive lease, install hash pins, backups, and rollback manifest. A second executable
  path drives Creator Session through the PowerShell sender, real Runtime request file,
  shipping controller, correlated receipt, BuildOn, BuildOff, Arm, Disarm, and Restore.
  Build control verifies both Valheim no-cost/all-pieces and god-mode state, rejects
  enabling in an unconfirmed private world, keeps disabling available as a fail-safe, and
  cannot carry a console command or synthetic key. The Lab
  sender has a corresponding correlated local round trip and fails on receipt identity
  drift.
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
  retained. Its pre-session `.db`/`.fwl` pair remains in the session backup, but Creator
  Session does not claim that `Close -Restore` restores world state.

An earlier live launch exposed a success-sentinel defect before Arm dispatch. The
validator now returns null on success, and both the executable controller round trip and
the corrected live Arm/Disarm receipts cover that exact branch.

## Creator session loop

1. With Valheim closed, run `tools\creator-session\Invoke-CreatorSession.ps1 Prepare`. Prepare takes the install-wide lease, builds and hash-verifies the payload, backs up exact plugin/config/world files, enables the private-world safety gate, and records the machine/world/session pins used by every later request.
2. Launch Valheim and enter the exact world named by the session manifest. This is the only keyboard step needed to establish the live-world precondition; do not open the console or relay F5 commands.
3. Run `tools\creator-session\Invoke-CreatorSession.ps1 Status -SessionId <id>`. Continue only while the session is active, the machine and world pins agree, and every installed plugin hash still matches Prepare.
4. For a Godbuild lap, run the bounded `BuildOn` operation and require its correlated receipt before the creator begins spatial work. While it is active, the creator uses ordinary hammer/build controls; run one bounded operation from `GalleryRebuild`, `Capture -BlueprintName <name>`, or `Arm` only when the lap calls for it. Every request expires, carries the same three identity pins, is consumed once in game, and has no console-command or synthetic-key field.
5. Capture automatically imports the fixed receipt artifact and runs the generator-drift check. Review `examples/worldbuild/<name>/preview.svg`, `plan.json`, and `manifest.json`; the manifest names upstream exclusions and the capture/blueprint pair remains replay authority.
6. Run `Replay -BlueprintName <name>` only after review. Replay hash-verifies and stages that exact reviewed pair, then performs check, build, and translation-independent diff and fails unless the receipt says `MATCH`. Run `BuildOff` and require its receipt before leaving the loaded world; then finish with `Close`. When prior install bytes should be restored, quit Valheim after `BuildOff` and use `Close -Restore`.

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
command, forced focus, or manual file copy was required. The next creator-scale target is
the dogfood foundation: portfolio hierarchy, named world anchors, scoped reset/rerun,
and a versioned saved-world bundle, followed by one real multi-quest guild slice.

## Capacity policy

- Phase 3's remaining combat-feel verdicts are parked and should be taken together in a
  later creative session. They do not gate Creator OS construction.
- i5 stays off for content-only and spatial work. Start the peer only when the multiplayer
  adapter itself changes.
- A screenshot or checkbox is orientation, not machine proof. Request receipts, active-set
  identity, capture hashes, and `MATCH` are the machine facts.
- Auto-generation and the planned pattern notebook are optional accelerators. They earn
  critical-path priority only when repeated guild authoring demonstrates the need.

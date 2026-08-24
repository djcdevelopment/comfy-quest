# Creator OS

The scarce resource is the creator's time in the seat. Build, deployment, identity
checks, bounded world operations, capture transport, and proof collection belong to the
machine loop. The seat is for spatial judgment, authored choices, and play feel.

## Proof level

- `Invoke-CreatorSession.ps1` has an executed fixture-mode lifecycle covering Prepare,
  Status, Close, exact plugin/config restoration, its closed action vocabulary,
  exclusive lease, install hash pins, backups, and rollback manifest. A second executable
  path drives Creator Session through the PowerShell sender, real Runtime request file,
  shipping controller, correlated receipt, Arm, Disarm, and Restore. The Lab sender has a
  corresponding correlated local round trip and fails on receipt identity drift.
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
  on-screen clamping. Those new bytes are build-proven, not live-proven; the running game
  still has the prior DLL mapped, so the page must not call the visual exit complete.

An earlier live launch exposed a success-sentinel defect before Arm dispatch. The
validator now returns null on success, and both the executable controller round trip and
the corrected live Arm/Disarm receipts cover that exact branch.

## Creator session loop

1. With Valheim closed, run `tools\creator-session\Invoke-CreatorSession.ps1 Prepare`. Prepare takes the install-wide lease, builds and hash-verifies the payload, backs up exact plugin/config/world files, enables the private-world safety gate, and records the machine/world/session pins used by every later request.
2. Launch Valheim and enter the exact world named by the session manifest. This is the only keyboard step needed to establish the live-world precondition; do not open the console or relay F5 commands.
3. Run `tools\creator-session\Invoke-CreatorSession.ps1 Status -SessionId <id>`. Continue only while the session is active, the machine and world pins agree, and every installed plugin hash still matches Prepare.
4. Run one bounded operation: `GalleryRebuild`, `Capture -BlueprintName <name>`, or `Arm`. Each request expires, carries the same three identity pins, is consumed once in game, and returns a request-correlated receipt.
5. Capture automatically imports the fixed receipt artifact and runs the generator-drift check. Review `examples/worldbuild/<name>/preview.svg`, `plan.json`, and `manifest.json`; the manifest names upstream exclusions and the capture/blueprint pair remains replay authority.
6. Run `Replay -BlueprintName <name>` only after review. Replay hash-verifies and stages that exact reviewed pair, then performs check, build, and translation-independent diff and fails unless the receipt says `MATCH`; finish with `Close`, adding `-Restore` while Valheim is closed when the prior install bytes should be restored.

## Studio creator loop

The product reading order is **Author -> Rehearse -> Play -> Observe -> Capture Godbuild -> Replay elsewhere**. Runtime opens Studio with the active pack, version, requested stage, and current beat in the loopback query so Observe lands on the same telling rather than asking the creator to find it again.

The first real Godbuild target is intentionally small: build one room or one lane with
human spacing, capture it once, review the generated preview and typed plan, then replay
it elsewhere. The point of that lap is the reusable loop, not the size of the build.

The next normal Valheim launch—not a dedicated seat session—is the only remaining
Runtime-surface check. After the game is naturally closed, Creator Session can deploy the
y=92 payload; on the next ordinary world entry automation should take the direct
window-buffer capture, verify that the compact bar clears the host band, and close the
session. No F9 press, screenshot relay, console command, or manual file copy is required.

## Capacity policy

- Phase 3's remaining combat-feel verdicts are parked and should be taken together in a
  later creative session. They do not gate Creator OS construction.
- i5 stays off for content-only and spatial work. Start the peer only when the multiplayer
  adapter itself changes.
- A screenshot or checkbox is orientation, not machine proof. Request receipts, active-set
  identity, capture hashes, and `MATCH` are the machine facts.

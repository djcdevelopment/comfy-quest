# Session build record - ERA17 autonomous creator seat

Recorded: 2026-08-27

Repository: `C:\work\comfy-quest`

Implementation range: `55a84d1` through `83ad491`

Landing branch: `main`

## Result

This session turned the existing Creator Session lifecycle into an installed, machine-owned
path from a Quest checkout to an exact local Valheim world, then used that path to prepare a
real ERA17 creative sitting.

The technical path now owns exact build and deployment, backup, Steam launch, world and
character selection, Runtime control, Studio activation, nearby target selection, retries,
receipts, shutdown, and recovery. Derek's only input used to shape the retained seat was `C`:
**Pilgrimage of Ash and Ice**, with a mythic, uncanny, reverent tone.

Before this build record, the session landed eleven commits touching 55 files, with 4,038
insertions and 273 deletions. The result is not just source-complete: the 4A guild journey,
the Isolate-to-Runtime slices, large-world shutdown, and the premise-C seat were all exercised
against installed Valheim.

## What was built

| Surface | Shipped result | Executed proof |
| --- | --- | --- |
| Creator Session | Exact, bounded world entry through Steam and Valheim's own APIs | OMEN entered the pinned world/profile without human navigation |
| Installed guild driver | Real Studio authoring, publication, activation, binding, run/reset, retention, and recovery choreography | `queue-full-width-journey-20260827-r9` passed with zero human actions |
| Runtime authority | Creator and run-control requests are pinned to the Creator Session that completed world entry | Missing, stale, wrong-world, and wrong-session requests failed closed |
| Isolate integration | Product-owned provider archive exposing five bounded Quest tools | Two installed Isolate slices controlled the retained ERA17 Runtime; target-local AM4 execution proved the added reviewed Lab replay before clean Isolate packaging |
| Studio integration | Guild-aware active relation, exact experience binding, fresh-run gating, and bounded stale-heartbeat grace | Studio reported the selected experience `current` and all four evidence stages `PASS` |
| Recovery | Exact plugin/config/world/profile backup and restoration plus large-world graceful shutdown | 4A restored three save hashes; ERA17 saved a 1.30 GB world in 32.46 seconds without force |
| Creator content | Eight-experience ERA17 guild reframed around the selected premise | Revision 27 was certified, activated, and its opening bound to a nearby sign |
| Evidence and operating truth | Installed evidence indices, updated plan/mission control, R&D edge log, and retrospective | Every installed claim below points to a committed reporting surface |

## 1. Machine-owned Creator Session world entry

The session added an explicit request/receipt contract in
[`RuntimeWorldEntry.cs`](../network/mod/ComfyQuestContracts/RuntimeWorldEntry.cs) and the
installed controller in
[`RuntimeWorldEntryController.cs`](../network/mod/ComfyQuestRuntime/RuntimeWorldEntryController.cs).

The request pins:

- request and Creator Session identities;
- expected machine and world UID;
- world filename and display name;
- character profile; and
- bounded creation and expiry times.

The contract deliberately has no server address, arbitrary path, launch argument, console
command, or synthetic-input field. Runtime accepts it only in the expected process, enters the
exact local world through Valheim's profile/world APIs, and emits a correlated receipt.

[`Invoke-CreatorSession.ps1`](../tools/creator-session/Invoke-CreatorSession.ps1) was expanded
to stage the request before launch, start Valheim through Steam, wait for the exact entered
receipt, verify the interactive process, and preserve the evidence. Its supported lifecycle is
now:

`Prepare`, `Status`, `Launch`, `Stop`, `GalleryRebuild`, `Capture`, `Replay`, `Arm`, `Disarm`,
`BuildOn`, `BuildOff`, and `Close`.

The session manifest is also a resume handle. `Status` can recover the exact machine, world,
profile, hashes, process, activation, binding, and run state from `-SessionId` instead of asking
the operator to repeat those arguments.

## 2. Autonomous installed guild journey

[`Invoke-QuestStudioGuildJourney.ps1`](../tools/quest-studio/Invoke-QuestStudioGuildJourney.ps1)
now drives the complete installed 4A sequence. The implementation work found and fixed four
real choreography edges before the successful lap:

1. installed preflight had to verify and replay the deterministic nearby sign fixture before
   starting the expensive journey;
2. experience selection had to preserve the exact chosen binding instead of accepting an
   ambiguous guild-level match;
3. clean-rerun preview had to address the newly loaded run rather than a prior run scope; and
4. the driver had to wait for Runtime's fresh heartbeat containing that exact project/run pair
   before issuing scoped control.

Installed session `queue-full-width-journey-20260827-r9` then:

- authored and immutable-published A plus prerequisite-locked B through the real Studio GUI;
- entered the pinned local world through Steam;
- replayed and matched the reviewed 12-of-12 sign fixture;
- activated identical production and dev content;
- proved B refused to start before A completed;
- completed independent A -> B -> A runs;
- previewed and applied a reset to A without changing completed B;
- completed the successor A run linked to the predecessor and reset;
- crossed the 32-receipt per-run bound by one while retaining exact archived evidence;
- restored four binding changes in LIFO order;
- stopped Valheim gracefully; and
- restored all three world/profile files to their pre-lap SHA-256 values, with no installed
  plug-ins or Valheim processes left behind.

The committed proof index is
[`queue-full-width-journey-20260827-r9.json`](evidence/queue-full-width-journey-20260827-r9.json).
The full ignored evidence bundle remains at:

`C:\work\comfy-quest\captures\queue.full-width-journey\queue-full-width-journey-20260827-r9`

## 3. Recovery and safe shutdown

The Creator Session recovery path was hardened in two separate ways.

First, `Close -Restore` now restores quarantined one-shot world-entry state exactly, including
the distinction between a previously absent request and a prior status file. Fixture tests
observed the stale-state failure before the fix and also exercise rollback after a forced late
Prepare failure.

Second, `Stop` no longer assumes a large world can save inside 20 seconds. ERA17 r4 exposed the
old behavior during a real save and preserved the interrupted 496 MB `.new` database in its
evidence rather than discarding it. The patched path gives Valheim a two-minute minimum graceful
window. ERA17 r5 then took 32.46 seconds to write a 1,299,575,181-byte database, emitted
`World saved`, exited without force, and left no world or character `.new` files.

This recovery proof is recorded in
[`isolate-era17-runtime-20260827-r2.json`](evidence/isolate-era17-runtime-20260827-r2.json).

## 4. Quest tooling consolidated through Isolate

Quest now produces a standalone provider release for the Isolate Workbench instead of
reimplementing Isolate or reaching into its source tree. The implementation is in:

- [`comfy_quest_workbench.py`](../tools/workbench-provider/comfy_quest_workbench.py);
- [`New-QuestWorkbenchProvider.ps1`](../tools/workbench-provider/New-QuestWorkbenchProvider.ps1);
  and
- the [provider README](../tools/workbench-provider/README.md).

The deterministic Python-importable archive exposes five tools:

| Tool | Capability |
| --- | --- |
| `quest_runtime_status` | Read bounded active-pack, dev-channel, world-entry, run, provider-release, and non-secret Creator Session status |
| `quest_runtime_receipts` | Read and filter the bounded 512-file live receipt window |
| `quest_runtime_creator_request` | Send only `status`, `arm`, `disarm`, `build_on`, or `build_off` |
| `quest_runtime_run_control` | List nearby bindings, bind an exact experience, restore a binding, and perform preview-confirmed scoped reset operations |
| `quest_lab_replay` | Hash-verify one release-mounted reviewed Godbuild pair, reassert live private-world Runtime authority, and perform ground-only Lab check/count/build/diff with verified cleanup |

The provider has no process control, lifecycle, console, synthetic input, arbitrary path, or
server capability. Isolate remains responsible for gateway identity, authentication, provider
mounting, and teardown. Runtime remains responsible for machine/world/session identity,
private-world confirmation, pack membership, target ownership, reset tokens, and receipts. Quest
Lab remains responsible for reviewed artifact validation, marked-piece counts, builds, clears,
and exact diff receipts.

Runtime now latches Creator Session authority only after exact world entry reaches `entered`.
Both creator and run-control requests must carry that identity, and their receipts echo it.
Studio and Isolate can learn the non-secret identity from bounded status rather than borrowing
harness context.

The installed red-team checks proved:

- a missing Isolate caller credential is rejected;
- a wrong world UID is rejected;
- a prior Creator Session cannot issue creator control;
- a prior Creator Session cannot issue run control;
- a forged or missing session receipt is rejected in source integration tests;
- provider calls expire before their tool wait and leave the occupied fixed mailbox for its
  authority to claim and reject, avoiding a compare-then-delete race with a successor request;
- reviewed Lab replay rejects stale session/private-world authority, artifact drift, partial or
  extra marked state, and unsupported sky placement before claiming success; and
- two consecutive provider builds produce identical archive bytes.

The final retained provider archive has SHA-256
`b4ec7d4aff381e929064b2b71f558f208c4019481109f76f8f6ddf8387dc020c`.

AM4 executed that exact archive target-locally against the retained Creator Session. Its first
replay built all 12 reviewed pieces and returned exact `MATCH`; an immediate retry omitted the
build and returned `blueprint_replay_already_match`. Every invocation reasserted `build_on` and
verified `build_off`. This closes the Quest-owned choreography edge, but not the independent
Isolate release gate: image identity, per-lap credentials, clean volunteer-machine setup, and
correlated teardown remain open.

## 5. Studio, Runtime, and authoring corrections

The integration work also shipped several product corrections:

- Studio compiles and correlates the containing guild identity, filters Runtime evidence to
  the selected experience, and reports a sibling project as `other_experience` instead of the
  misleading `other_version`.
- Studio recognizes explicit Isolate binding receipts and reports Validation, Transfer,
  Activation, and Rebind independently.
- An already-armed Runtime may briefly stall during a Valheim main-thread pause. Studio still
  fails closed on stale status, but the Play path now grants that specific state a bounded
  30-second reconnect window.
- Run control preserves its stale-status safety gate. The final premise-C bind waited for a
  fresh connected heartbeat and retried the exact session-pinned request; no gate was weakened
  and no human retry was requested.
- The generated Valheim catalog and Studio examples now use the installed prefab identity
  `woodwall`, not `wood_wall`. A mutation test proves the wrong spelling turns the generator
  check red.
- Interim `Comfy.Quest.Contracts.0.6.0-local.nupkg` and
  `Comfy.Quest.Studio.0.6.0-local.nupkg` artifacts were refreshed to carry the landed changes.

The principal Studio implementation is in
[`StudioDevChannelConnection.cs`](../src/Quest.Studio/StudioDevChannelConnection.cs),
[`QuestStudioRunControl.cs`](../src/Quest.Studio/QuestStudioRunControl.cs), and
[`QuestStudioWorkspace.cs`](../src/Quest.Studio/QuestStudioWorkspace.cs). Runtime authority is
implemented in
[`RuntimeCreatorSessionAuthority.cs`](../network/mod/ComfyQuestRuntime/RuntimeCreatorSessionAuthority.cs),
[`RuntimeCreatorRequestController.cs`](../network/mod/ComfyQuestRuntime/RuntimeCreatorRequestController.cs),
and
[`RuntimeRunControlController.cs`](../network/mod/ComfyQuestRuntime/RuntimeRunControlController.cs).

## 6. ERA17 content staged from Derek's choice

The 58-image drone survey was reduced to a 12-frame premise brief. Derek selected `C`, so the
existing eight-experience guild was reframed as **Pilgrimage of Ash and Ice**:

> Follow an abandoned ritual path from the burning skull shrine through the sky temple to the
> snowy ascent, discovering why it was abandoned and whether it should be completed.

Revision 27 contains the three progression bands **Read the Ash**, **Cross the Veil**, and
**Climb into Ice**, plus these eight experiences:

| Experience | Trigger | Target |
| --- | --- | --- |
| The Name in Ash | `sign_written` | `sign` |
| Take Up the Pilgrim's Staff | `item_picked_up` | `Wood` |
| Raise the Ashen Marker | `piece_placed` | `woodwall` |
| Kindle the Skull Flame | `station_fuel_added` | `Coal` |
| Cross the Sky Temple | `player_teleported` | none |
| The Snowbound Vigil | `kill` | `$enemy_greyling` |
| Gather the Pilgrim's Red | `resource_picked` | `RaspberryBush` |
| Mend a Forgotten Shrine | `piece_repaired` | `woodwall` |

The installed identity is:

| Field | Value |
| --- | --- |
| Machine | `OMEN` |
| World | `ComfyEra17`, UID `-523956327` |
| Character profile | `tugcorp` |
| Creator Session | `era17-broken-way-20260827-r6` |
| Guild | `guild-2de197289aea`, revision 27, version `0.1.1` |
| Content hash | `6e0f85f1bc2a26a7e7974817c17897281b1e722ea274509522a6bacb4aa3526e` |
| Package SHA-256 | `42690c075c6bcb380be6b3487ddb878fa05ca54e913bd3447a11181ea363979c` |
| Activation | `act-20260827T162128100Z-c8f94aae` |
| Opening experience | `quest-0d3372`, **The Name in Ash** |
| Bound target | nearby sign `1:3177525`, 4.9 metres at candidate scan |
| Current run | `run-20260827T162426244Z-9dc80dc7` |
| Run scope | `scope-9c3635e7ef76807efe6a596c341fe207`, stage `start` |

At evidence capture, Studio reported `bound`, `current`, connected, armed, and watching, with
no pending creator or run-control request. The retained-seat receipt is
[`era17-premise-c-20260827-r1.json`](evidence/era17-premise-c-20260827-r1.json).

## Evidence installed in Git

- [4A autonomous guild journey](evidence/queue-full-width-journey-20260827-r9.json)
- [Isolate installed slice r1](evidence/isolate-era17-runtime-20260827-r1.json)
- [Isolate session pin and shutdown slice r2](evidence/isolate-era17-runtime-20260827-r2.json)
- [ERA17 premise-C retained seat](evidence/era17-premise-c-20260827-r1.json)
- [R&D opportunity and observed-edge matrix](quest-rd-opportunity-matrix.md)
- [Current program plan](PLAN.md)
- [Journey retrospective](retros/2026-08-27-one-character-should-have-been-enough.md)

The larger live bundles are intentionally Git-ignored. Their committed indices pin absolute
locations, identities, verdicts, and key hashes so later prose does not need to reconstruct
those claims from logs.

## Commit ledger

| Commit | Result |
| --- | --- |
| `55a84d1` | Automated pinned Creator Session world entry |
| `f9322cc` | Hardened autonomous installed guild preflight |
| `10ac0be` | Preserved exact Runtime binding selection |
| `fb558b1` | Corrected installed reset-preview choreography |
| `89c0a71` | Waited for the fresh exact run scope before preview |
| `d4c1fb1` | Recorded the passing zero-human 4A exit proof |
| `a172e77` | Restored quarantined Creator Session state correctly |
| `506a1b4` | Hardened autonomous ERA17 sessions and added the Isolate provider |
| `78bc8cc` | Refreshed the interim Contracts and Studio packages |
| `e617953` | Recorded and staged the premise-C creative seat |
| `83ad491` | Recorded the journey retrospective |

## What Derek is supposed to provide next

The next input is creative, not mechanical:

1. write a natural name on the already-bound nearby sign;
2. judge composition and cue clarity;
3. judge response timing and explanation;
4. judge narrative tone and play feel; and
5. say what to keep, revise, or reject.

The machine still owns navigation performed only for setup, deployment, retries, captures,
receipts, logs, reset/rerun plumbing, shutdown, and recovery. A request for Derek to operate
those mechanics is a defect in this workflow.

The read-only resume command is:

```powershell
tools\creator-session\Invoke-CreatorSession.ps1 Status -SessionId era17-broken-way-20260827-r6
```

## Known limits

This session does not claim more than it proved:

- Derek has not yet supplied the creative acceptance verdict; the retained seat is ready for
  that judgment, not already accepted.
- Premise C was activated through the private dev path; this record does not claim a new
  community production release.
- The installed Isolate image reports no source revision, used an operator-specific registered
  credential, and mounted the provider from a checkout-local artifact. Clean volunteer-machine
  setup and correlated teardown remain unproven.
- The exact installed Studio retry branch during an already-armed stale heartbeat remains open;
  source integration covers the bounded grace, and the installed system separately proved
  fail-closed behavior plus reconnection after a real pause.
- The retained r6 game, Studio host, Isolate gateway, world state, and sign binding were
  deliberately left in place at evidence capture. Their current state should be read through
  the session status surface rather than inferred from this document.
- Saved-world distribution remains deferred until repeated 4B authorship stabilizes the
  content and world contracts.
- Creator Session owns and hash-pins its four Quest deployment files. This session did not
  install a general-purpose machine audit that can claim every unrelated hook, add-on, or
  autostart outside those owned targets has been removed.
- The provider consolidates bounded game tooling; it does not solve cross-client conversational
  memory or automatically transfer Derek's context between fleet windows.

## Verification

The landing gate for this session passed:

- Lab and Runtime Release builds: clean, zero warnings and zero errors;
- Lab xUnit: 353 passed;
- Studio Release build under the repository's .NET 9 toolchain: clean;
- Studio xUnit: 109 passed;
- Python: 373 passed;
- generator, gallery, Quest Lab, seam catalog, patch-target, demo-pack, mission-control, and
  source-intent drift checks: passed;
- repository identity and both boundary checks: passed; and
- full-history secret scan: passed with no leaks found.

The retained game was not cycled to produce this build record.

## Continuation - durable five-stage ERA17 opening (2026-08-27/28)

This continuation is installed R&D work in the current dirty worktree; it has not been landed.
The explicitly requested `/rnd` mode remains unlocked, so no test files were added or changed and
no test suite was run. The source was built, red-teamed by inspection, and exercised through the
real Studio-to-Valheim choreography instead.

The earlier retained seat was not yet the thing worth presenting to Derek. It had a selected
premise and a bound sign, but the opening was still essentially one trigger and the tooling had
not proved that its logical binding survived a cold ERA17 load. This continuation filled that
gap.

### Built in this continuation

| Surface | Result | Installed evidence |
| --- | --- | --- |
| Steam identity | Prepare resolves the active Steam process/account, scopes cloud character discovery to that account, records it, and Launch refuses executable or account drift | r12 pinned `wary.fool` / account `128445914` and entered exact `tugcorp` / `ComfyEra17` |
| Durable Charm identity | Binding marker is projected through Runtime references, run scope, workflow identity, receipts, status, Studio, and the Isolate provider; physical ZDO is no longer logical identity | r9 retained run, scope, marker, experience, hash, stage, and participant across cold load while ZDO changed `1:3177522 -> 1:3177521` |
| Cold-safe selection | Runtime resolves one marker selected by the binding journal, fails closed on missing/duplicate/ambiguous modern markers, and keeps a narrow marker-free legacy fallback | r12 selected only `binding-20260828T044359683Z-fc3f2842`; a historical anchor was reported `superseded_binding` |
| Hot revision continuity | Same-pack activation retains an explicit experience selector only when the new archive resolves that exact id once; removed, duplicated, unreadable, and cross-pack ids still clear | r11 exposed `active_experience_ambiguous`; r12 retained `quest-0d3372` on 0.1.3 and rebound automatically |
| Explicit experience start | Binding/rebinding owns an idempotent `StartBoundExperience`; status reads do not invent runs and repeated current binds do not inject a later lifecycle event | r12 created one new run and `experience_started` advanced `start -> name-in-ash` |
| Action safety | Action ledger v2 separates reservation from commit; grants/spawns preflight; successful mutation commits before receipts; partial spawn cleanup proves exact same-generation absence before route-scoped retry; reset and stale timers cannot replay a retired or later stage | Release build succeeded with zero warnings/errors; two independent read-only red-team passes found no remaining hot-seat blocker |
| Isolate projection | Packaged status now carries `binding_instance_id` and replaces participant ids with `participant_count` | archive `ac383971...d7b8` reported the exact r12 run without player identity |
| Studio evidence | Every `dev_rebind` requires fresh run evidence; rejected or superseded historical rows cannot satisfy present-tense readiness | fresh r12 status reported bound/current and five PASS lines, including Rebind and Runtime observed |
| Opening content | **The Name in Ash** is now a five-stage, six-route experience with two endings | guided rehearsal and both manual choice paths completed; installed Runtime is waiting at `name-in-ash` |

### The installed result

| Field | Value |
| --- | --- |
| Creator Session | `era17-pilgrimage-20260827-r12` |
| Evidence root | `C:\work\comfy-quest\captures\broken-way-campaign\era17-installed-20260827-r12` |
| Guild | `guild-2de197289aea`, revision 29, version `0.1.3` |
| Content hash | `197bd6002f162c70ac25c2007ab587728e66d7dd58491eb20d333883bd1fb702` |
| Package SHA-256 | `03107b9a1b7659b592796ee5abcf5d999cdc56c3af3b810f8bbec40b81a0aa31` |
| Activation | `act-20260828T054222834Z-e91eac71` |
| Experience | `quest-0d3372`, **The Name in Ash** |
| Run | `run-20260828T054222875Z-f3bc2ff5` |
| Scope | `scope-00e2758c59269d64eda0e82c3d625226` |
| Durable binding | `binding-20260828T044359683Z-fc3f2842` |
| Current physical sign | `1:3177522` (3.2 m at the r9 candidate scan) |
| Current stage | `name-in-ash` |
| Studio | `bound`, `current`, connected, armed, watching; five PASS lines |

The committed-shape evidence index is
[`era17-pilgrimage-20260827-r12.json`](evidence/era17-pilgrimage-20260827-r12.json).
The full ignored live proof is at:

`C:\work\comfy-quest\captures\broken-way-campaign\era17-installed-20260827-r12\final-live-proof`

### What Derek is supposed to provide now

Only play and judgment:

1. write a natural pilgrim name on the already-bound nearby sign while within 8 m;
2. drop the granted Resin;
3. kill the spawned Greyling himself;
4. choose memory by dropping Resin or oath by dropping Stone; and
5. say what felt clear, responsive, meaningful, confusing, slow, or dead.

No select, bind, console, copy, relaunch, navigation-for-setup, or diagnostic relay belongs in
that list. At this evidence capture, r12 was intentionally left entered and armed with Studio
running for the sitting.

### Limits retained on purpose

- The installed run proves activation, rebind, the opening message action, and arrival at
  `name-in-ash`; the remaining four live beats are the human creative sitting, not pre-claimed.
- Spawn recovery is automatically safe only inside the same ZDO manager/session. A process or
  network-generation change leaves the action pending for explicit reset rather than risking a
  duplicate.
- Spawn reset still records physical ZDO ids; cold cleanup of a partially spawned batch is not
  claimed, and a narrow tag-to-row crash gap remains.
- Greyling placement has no terrain/NavMesh proof. The local-player kill and tracked-spawn tally
  are both required but are not mathematically subject-correlated.
- Restore discovery remains bounded to 32 candidates within 20 m. Global duplicate-marker
  uniqueness and the restore-to-empty crash window remain unproven.
- This is a private dev-channel revision, not a community release. Clean Isolate portability and
  teardown remain separate.

### Continuation verification

- Runtime Release build: zero warnings, zero errors.
- Studio Host Release build against the final package-hash-keyed Contracts cache: zero warnings,
  zero errors.
- r9 cold choreography and r12 activation choreography were executed, not inferred from prose.
- Mission-control JSON was parsed and its living HTML regenerated.
- Tests were deliberately not run because `/rnd` remains unlocked.

## Continuation - restore ERA17's world support (r13, 2026-08-27/28)

Derek played the r12 opening through the sign, Resin offering, and Greyling trial to
`pilgrim-choice`. His first-hand verdict separated the product signal from the machine defect:
the hard freeze every approximately three seconds was distracting, while the experience was
otherwise "pretty inspiring."

The cleanup that made the OMEN install Quest-only had also removed a world-specific runtime
precondition. ERA17 had been relying on NetworkSense's cached portal and spawner connection
paths; without those non-Quest files, Valheim returned to its full connection scans on a world
with thousands of portal and spawner records. Session r13 restores that support automatically
from an exact manifest instead of asking Derek to diagnose, copy, or launch anything.

### The r13 installed result

| Surface | Machine evidence |
| --- | --- |
| World support | Captured NetworkSense 0.5.80 plus its four managed dependencies were deployed from exact size/SHA-256 pins under package `comfy-network-sense-portal-support`, release `m7-c10b-20260807-r42` |
| Sanitized behavior | Config hash `b107fb269a25ef58f4318e7b42e9b560f5f19dbf8f41e6f6ef05ba764c73c083` leaves only the portal and spawner caches enabled |
| Corrected cold launch | `20260828T062556Z-launch` entered `wary.fool` / `tugcorp` / `ComfyEra17` and wrote operation hash `0e0b62854de5f418e66526eeec8102256ec5fbffd6063dfb5185f98692a7cb85` |
| Startup cache | The evidence log recorded 5,171 saved portal links and 92,359 spawner sources against 19,493 targets; both vanilla `ConnectPortals =>` and `ConnectSpawners =>` markers were absent |
| Recurring cache | A separate snapshot recorded full-world cycles at 23:27:15, 23:28:15, and 23:29:15 local time, each processing 13,875 portal objects with zero dirty tags, connects, disconnects, or force-sends |
| Quest continuity | Arm completed on the same guild 0.1.3 activation and reported the retained current stage `pilgrim-choice`; Derek's completed beats were not replayed |

The successful launch evidence is at:

`C:\work\comfy-quest\captures\broken-way-campaign\era17-installed-20260827-r13\20260828T062556Z-launch`

Its captured support log has SHA-256
`ec8035c300777c3307b519228d94f64ce7bd4b9cc1e6edd46562d7e793bde615`.
The recurring-path snapshot is at:

`C:\work\comfy-quest\captures\broken-way-campaign\era17-installed-20260827-r13\20260828T062815Z-periodic-support`

The snapshot's log has SHA-256
`0a0b9ddde9b8c4a42810808d041686d0a6348c4f253319d988b2f7a4e3f0ecad`.
The committed-shape index is
[`era17-pilgrimage-20260827-r13.json`](evidence/era17-pilgrimage-20260827-r13.json).

### The launcher edge found by the first r13 attempt

The first r13 launch succeeded in Valheim: the later Stop evidence retained an `entered`
world-entry receipt completed at `2026-08-28T06:21:46.920827+00:00`, and its game log contained
the required cache signals. Launch nevertheless rejected every signal. `[IO.File]::ReadAllText`
requested sharing incompatible with BepInEx's open `LogOutput.log`; the caught sharing failure
silently replaced the sample with empty text.

The correction opens the live log with `FileShare.ReadWrite | FileShare.Delete`, snapshots it
through the shared handle, validates the exact evidence copy, and only then hashes it. The
second cold launch produced the operation and support-log hashes above. This is
execution-proven choreography. No test or build claim is attached to it.

### Tactical boundary

This r13 rescue is deliberately narrower than a generalized portal solution:

- `ComfyNetworkSense.dll` is an unsigned captured external artifact. Its exact bytes are pinned,
  but a SHA-256 pin is not an authenticated upstream publication.
- NetworkSense 0.5.80 registers its full assembly patch graph at load. The sanitized profile
  disables its other telemetry, automation, MCP, HUD, probe, and cutover switches; it does not
  turn the binary into a narrow cache-only assembly.
- Its portal tag index is initialized before the bulk world load and invalidated by `ZDOMan`
  identity rather than load generation. The evidence therefore covers the current static saved
  topology only. Creating or retagging portals after load is outside the claim.
- The periodic snapshot proves recurring cached execution while the topology was idle. It does
  not prove human portal traversal or live portal authoring.
- Derek has not yet confirmed that the recurring distraction is gone. That perceptual verdict,
  the final Resin-or-Stone choice, and what to keep or revise are the remaining seat input.

No tests were run for this r13 R&D continuation.

## Continuation - unattended AM4 mechanics lock (2026-08-28)

The mechanics lap moved off Derek's OMEN seat and onto AM4. AM4 already had an observable
Valheim desktop and no player using it; Steam was signed in as account `waryfool`, persona
`Zephar410`. That made it the correct place for launches, visible in-game mutation, retries, and
evidence capture while OMEN stayed available for Derek's normal work. Derek did not drive a
client, relay a command, capture a screenshot, or retry a failed step in this continuation.

The first cold AM4 pass also corrected the diagnosis. Reserving one candidate of each target
kind inside Runtime's 32-entry wire bound was a valid dense-world hardening, but it did not make
the missing sign appear. The patched candidate scan still contained no sign. Comparison with the
already-proven Demo fixture lap showed that the pinned world needed the reviewed 12-piece
Godbuild replay before binding discovery.

Executing that actual precondition found a second edge. `blueprint_check` rejected the
deterministic capture sidecar because Mono attempted to load the
`dataContractSerializer` configuration section. The capture and tree-recovery contracts now use
the already-deployed Newtonsoft.Json dependency with strict invariant settings. After a cold
client restart, the same reviewed blueprint/capture bytes passed check, built all 12 pieces
including one sign, and returned:

`MATCH — expected 12, selected 12, missing 0, extra 0`

### Mechanics built and installed

| Surface | Result | Installed evidence |
| --- | --- | --- |
| Candidate discovery | The deterministic 32-entry selector reserves the nearest candidate of every observed target kind before filling by distance, so dense generic pieces cannot hide all signs or item stands | The post-replay AM4 scan returned sign `631591890:63831` at 5.6 m; the pre-replay patched scan remained sign-free and disproved the original diagnosis |
| Cross-platform Lab artifacts | Capture and tree-recovery JSON use strict Newtonsoft.Json instead of the Mono-incompatible data-contract serializer | A cold AM4 check accepted deterministic sidecar `e1e01ff6…7dfd`; build produced 12 pieces / one sign; diff returned exact `MATCH` |
| Reset successor lifecycle | A reset transaction does not report complete until the exact persisted successor has a workflow; retries reuse that successor and idempotently supply `experience_started` | The live reset replaced `run-20260828T082859977Z-eacae6c4` with `run-20260828T083011921Z-c1818db2`, preserved binding marker `binding-20260828T082859963Z-05d8607d`, and advanced the successor to `name-in-ash` |
| Retry safety | A completed reset replay repairs a missing successor start if necessary but cannot mint another run or duplicate an existing lifecycle chain | Replaying the same preview token returned the same reset and successor IDs, left one active run, and added only the replay's `apply_reset` completion receipt |
| One-call reviewed replay | The fifth provider tool verifies the reviewed manifest and artifacts, obtains live Runtime build authority, performs Lab check/count/optional-build/diff, and restores authority it enabled | Archive `b4ec7d4a...020c` built and matched all 12 pieces on its first AM4 call; its immediate retry returned already-match without `blueprint_build`; the same pair passed again after clear |
| Deferred clear completion | Lab waits up to 120 frames for Valheim to retire every marked ZDO before it decides whether clear succeeded | The red live request removed 12 pieces but reported failed on a same-frame count; after a cold correction, `provider-clear-fixed-20260828T091833Z-9f43d8e1` returned completed, and provider replay proved the world was empty by rebuilding all 12 |
| Browser journey fidelity | The synthetic Studio journey no longer rebinds after reset as a test crutch; it requires the reset-created successor and its exact `experience_started` chain | The focused browser journey first observed the broken post-reset state, then passed with the successor fix |
| Atomic heartbeat reads | Studio opens the Runtime status stream first and retries only a bounded 5 x 10 ms filesystem replacement window; corrupt, invalid, stale, or exhausted status remains fail-closed | The full browser suite exposed pre-dispatch `runtime_run_status_unreadable` at reset and restore even though each journey passed alone. Real locked-file recovery and no-mailbox exhaustion tests now pass; the combined suite passes 2 with 1 expected installed-game opt-in skip. |

### Installed identity and proof

| Field | Value |
| --- | --- |
| Creator Session | `quest-am4-fastlane-20260828-r1` |
| Machine | `am4` |
| Steam | account `15896021`, account name `waryfool`, persona `Zephar410` |
| World | `ComfyQuestDemo`, UID `-7600395338659582326` |
| Character | `questyfour` / `Questyfour` |
| Guild content | `guild-2de197289aea` 0.1.3, hash `197bd6002f162c70ac25c2007ab587728e66d7dd58491eb20d333883bd1fb702` |
| Experience | `quest-0d3372`, **The Name in Ash** |
| Physical sign | `631591890:63831` |
| Durable binding | `binding-20260828T082859963Z-05d8607d` |
| Current successor | `run-20260828T083011921Z-c1818db2`, scope `scope-c22584558475f21e1f00a7477a45eeea`, stage `name-in-ash` |

The committed evidence index is
[`quest-am4-fastlane-20260828-r1.json`](evidence/quest-am4-fastlane-20260828-r1.json).
The full ignored receipt/log set is at:

`C:\work\comfy-quest\captures\creator-session\quest-am4-fastlane-20260828-r1\evidence`

The mechanics chain is under `post-fix-restart`; the provider build/retry, clear red-to-green,
restore/retry, raw correlated receipts, final disabled authority, client log, and shutdown summary
are under `provider-lab-replay`.

### What Derek is supposed to provide

Nothing for this mechanics lap. AM4 remains the automation seat and is stopped after a clean save,
ready for the next machine-verifiable launch on demand. OMEN is not a fallback KVM.

When the machine path is locked and an ERA17 acceptance batch is deliberately prepared, Derek's
remaining contribution is still the scarce one: finish the Resin-or-Stone choice and judge
clarity, response, pacing, meaning, the recurring-stall perception, and what deserves another
creative iteration. Launching, selecting, binding, navigating for setup, resetting, collecting
evidence, and recovering remain fleet work.

### Honest limits

- This lap proves the small-world mechanics path, not ERA17's scale, topology, or human feel.
- The installed Linux client logged a missing native PlayFab Party library, then successfully
  completed Steam and PlayFab login. Local-world Quest behavior passed; multiplayer Party behavior
  was not sampled.
- The successor-start failure/recovery branch was pinned in source tests but not deliberately
  injected into the live client. The observed live success and exact retry path were executed.
- The target-local, hash-verified provider now owns the reviewed Lab replay as one bounded call
  and executed both build and already-match paths on AM4 with verified Runtime authority cleanup.
  The remaining Workbench gate is clean Isolate release identity, a generated per-lap credential,
  volunteer-machine setup, and correlated teardown. Pair staging is fail-closed but not
  crash-atomic, and its serialization lock is process-local rather than a host-wide lease.

### Final verification

- ComfyQuestLab and ComfyQuestRuntime Release builds: zero warnings, zero errors.
- ComfyQuestLab.Tests: 357 passed.
- Quest Studio Release build under .NET SDK 9.0.315: zero warnings, zero errors.
- This host's default `dotnet` is 8.0.422, so the literal Studio command fails `NETSDK1045`;
  Studio verification used `C:\work\dotnet9\dotnet.exe` while net8 Lab tests used the default
  runtime. No SDK roll-forward was inferred.
- Quest Studio tests: 114 passed.
- Combined browser E2E: 2 passed, 1 expected installed-game opt-in skip, 0 failed.
- Python: 390 passed; focused provider suite: 26 passed.
- Gallery, Lab HTML, seam catalog, patch coverage, Demo package, mission-control drift, source
  intents, repository identity, boundary/self-test, and release-verifier self-test: passed.
- Full-history gitleaks scan: no leaks.
- Clean-packed Contracts artifact: 123,343 bytes, SHA-256
  `db0e9583abf787ca9da9548d0e435ab35e53135f3cb19d836dcdea9cf68185b0`;
  package identity/repository/source-revision validation passed.
- Studio artifact: 300,186 bytes, SHA-256
  `5b3413a170f38b4b72b937339af63ebfae12e5e98a71f99d0ff25c88a9d301f1`;
  package identity/repository/source-revision validation passed.
- `repin_public.py --check-interim` passes with the hash-pinned external PortalSupport release
  stored beside Creator Session world support rather than inside the three-file interim NuGet
  feed. This session does not claim a public release.

The first full Python pass observed one provider responder timeout, and the landing gate later
reproduced it. That recurrence disproved the earlier transient classification. Merely widening the
synthetic authority and provider budgets did not fix it. Instrumented repetition exposed Windows
sharing violation `WinError 32` on the fake Lab authority's one-shot mailbox unlink; the responder
exited and the provider correctly timed out. The test authority now retries only that bounded
`PermissionError`, and its request wait outlives the provider wait; production provider timing is
unchanged. The retry branch passed 100 fresh-process repetitions, the preexisting-build branch
passed 50, all 26 provider tests passed, and the full 390-test Python suite passed three
consecutive times.

### Exact final-byte cold-load closure

The final Contracts status-reader hardening changed the rebuilt Contracts byte identity and,
through the normal references, the rebuilt Runtime and Lab byte identities after the first AM4
provider proof. The earlier proof remained valid for its installed snapshot, but it could not be
used as proof that the final three DLLs had actually cold-loaded. AM4 therefore ran one final
unattended lap with the exact release-build bytes:

| Installed file | SHA-256 |
| --- | --- |
| `ComfyQuestContracts.dll` | `0cb3d6ca72132def41248e49d7762233b094342688c530ac5da733f5e04e27ea` |
| `ComfyQuestRuntime.dll` | `c021103f642f168f9596091bc15d69d8a4db03889ef28c5cbe26a2957f08e92b` |
| `ComfyQuestLab.dll` | `3325151a67779afc19f2c24e4c5967f94b1c3a8cdea8acc20aac0df4dcccb7e5` |

World-entry request `world-entry-final-bytes-fa1a31f842824cd8a2056e88c344b508`
entered `ComfyQuestDemo` UID `-7600395338659582326` as `questyfour` without seat input. The
hash-pinned provider then returned `ok: true`, `built: false`, and
`blueprint_replay_already_match`; its check, count, and diff receipts completed, cleanup verified
build authority disabled, and a separate Runtime status receipt again reported
`creator_build_enabled: false`. Valheim then emitted `Game - OnApplicationQuit`, `ZNet Shutdown`,
`PrepareSave`, and `World saved`; the process and tmux session exited in four seconds and no
world or character `.new` file remained. AM4 is stopped. OMEN was untouched.

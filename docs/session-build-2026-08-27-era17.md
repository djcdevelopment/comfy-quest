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
| Isolate integration | Product-owned provider archive exposing four bounded Quest tools | Two installed Isolate slices controlled the retained ERA17 Runtime |
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

The deterministic Python-importable archive exposes four tools:

| Tool | Capability |
| --- | --- |
| `quest_runtime_status` | Read bounded active-pack, dev-channel, world-entry, run, provider-release, and non-secret Creator Session status |
| `quest_runtime_receipts` | Read and filter the bounded 512-file live receipt window |
| `quest_runtime_creator_request` | Send only `status`, `arm`, `disarm`, `build_on`, or `build_off` |
| `quest_runtime_run_control` | List nearby bindings, bind an exact experience, restore a binding, and perform preview-confirmed scoped reset operations |

The provider has no process control, lifecycle, console, synthetic input, arbitrary path, or
server capability. Isolate remains responsible for gateway identity, authentication, provider
mounting, and teardown. Runtime remains responsible for machine/world/session identity,
private-world confirmation, pack membership, target ownership, reset tokens, and receipts.

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
- provider calls expire before their tool wait and retract only their own unclaimed mailbox;
  and
- two consecutive provider builds produce identical archive bytes.

The final retained provider archive has SHA-256
`933e4b801fcf6d2c4ada9f43fddb6e731b920d6520e747babb38d0d32b4a1893`.

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

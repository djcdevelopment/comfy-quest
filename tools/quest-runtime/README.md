# Quest Runtime acceptance harnesses

## Local Studio-to-OMEN validation lap

`Invoke-QuestRuntimeValidationLap.ps1` is the proof-gated local harness for the
five-intent program's three-stage **Woodbound Signal**. It backs up and hashes the
installed Runtime payload, quarantines historical inbox and active-state files,
keeps private-world safety false until an explicit arm step, validates the exact
Studio questpack through Contracts, and strictly parses receipts before coaching the
next player action. Human pacing never times out.

The complete author, arm, enter-world, F10/F11, CHECK/CAST, play, r2 orphan, and
restoration sequence is in `docs/runbooks/I2-QUESTPACK-OMEN.md`. Preview the harness
without touching the game installation:

```powershell
tools\quest-runtime\Invoke-QuestRuntimeValidationLap.ps1 -PlanOnly
```

The destructive preparation and restoration surface has a sentinel-owned filesystem
self-test:

```powershell
tools\quest-runtime\Test-QuestRuntimeValidationLap.ps1
```

## Native peer acceptance

`Invoke-QuestRuntimePeerAcceptance.ps1` stages and receipts an ordinary Valheim multiplayer test:
OMEN hosts a private listen world as `Tugcorp`, and i5 joins through Steam Friends as `durracktu`.
The staged Runtime payload includes its pinned `Newtonsoft.Json.dll` dependency; a missing dependency
is a preparation failure rather than a partially rendered in-game UI.
AM4, Docker, Gateway, and NetworkSense configuration are outside this harness and remain untouched.

Preview the topology and phases without changing either client:

```powershell
tools\quest-runtime\Invoke-QuestRuntimePeerAcceptance.ps1 -PlanOnly
```

Prepare both clients while Valheim is closed:

```powershell
tools\quest-runtime\Invoke-QuestRuntimePeerAcceptance.ps1 -Action Prepare -RunId <run-id>
```

Then run the receipt monitor and follow the generated workbook. Gameplay uses normal Valheim and
Steam Friends UI; the temporary server password is never supplied to the harness.

```powershell
tools\quest-runtime\Invoke-QuestRuntimePeerAcceptance.ps1 -Action Run -RunId <run-id>
```

The run requires positive OMEN listen-host action receipts and an i5
`mutation_authority_unavailable` receipt with zero peer actions. Evidence is written beneath
`captures/quest-runtime-peer/<run-id>/`, and both Quest Runtime configs are restored byte-for-byte.

For an interrupted run:

```powershell
tools\quest-runtime\Invoke-QuestRuntimePeerAcceptance.ps1 -Action Stop -RunId <run-id>
```

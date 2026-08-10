# Quest Runtime native peer acceptance

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
`fieldlab/runs/quest-runtime-peer/<run-id>/`, and both Quest Runtime configs are restored byte-for-byte.

For an interrupted run:

```powershell
tools\quest-runtime\Invoke-QuestRuntimePeerAcceptance.ps1 -Action Stop -RunId <run-id>
```

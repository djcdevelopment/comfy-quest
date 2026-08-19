# Five-intent validation-lap backlog

This is the sanitized product-and-diagnosis record for the program's evolving live
Event. Raw receipts, local paths, machine identities, and screenshots remain beneath
ignored `captures/`. The record distinguishes facts we can explain from moments where
we genuinely cannot yet answer why.

## Slice 1.4 — The Woodbound Signal

Intended experience: speaking wakes the Charm, two Wood offerings create a brief
rhythm, and reclaiming Wood seals the rite. The player should understand the sequence
from the in-world messages. Runtime evidence should confirm that understanding rather
than replace it. An r2 activation should make an old inscription visibly belong to an
earlier telling.

### Pre-lap findings with known causes

| Observation | Why it happened | Resolution before player interaction |
| --- | --- | --- |
| Installed Runtime and Contracts hashes differed from the current source build. | Sessions 1.1–1.3 had landed newer local payloads than the prior OMEN installation. | `Prepare` backs up the installed bytes and deploys the exact gate-tested payload by SHA-256. |
| The Runtime inbox held twelve historical questpacks. | Prior validation artifacts were intentionally retained. `LoadLatest` orders compatible versions globally, so a historical `1.7.0` could outrank lap r1 `1.0.0`. | Every prior pack is recoverably quarantined for the bounded lap and restored afterward. |
| `PrivateWorldConfirmed` was already true while Valheim was closed. | A previous bounded test left the opt-in enabled. | Preparation forces false; only `ArmPrivateWorld` may enable it after exact r1 validation; cleanup always returns it to false. |
| The prior runbook described Companion on port 8080 and an obsolete Studio workflow. | It predated the sovereign standalone Studio and current Author → Rehearse → Publish & Play journey. | The slice-1.4 runbook now uses port 8085 and explicitly includes arm, enter-world, CHECK, CAST, play, r2, and restore. |
| Control receipts do not all carry gameplay correlation IDs. | Check, load, bind, and orphan operations do not originate from an accepted gameplay event. | Acceptance requires activation/correlation agreement on each gameplay event chain and treats control-plane receipts separately. No synthetic correlation is invented. |
| The first preflight reported CRLF in many unrelated working-tree files even though Git was clean. | This Windows checkout materializes longstanding files with platform endings while Git's canonical index remains LF. The new lap files themselves are physical LF. | The gate now checks LF in the canonical index for all tracked text and physical LF for the session's code, JSON-facing tests, and runbook. It does not rewrite unrelated files or weaken an existing pin. |
| The first all-green preflight serialized gate console output beside its final receipt. | PowerShell returned uncaptured scriptblock output through the function pipeline, so `ConvertTo-Json` correctly encoded an array rather than the intended single result. | Gate output is now explicitly rendered to the host and cannot enter the receipt pipeline; preflight must end with one `comfy-quest-validation-lap-preflight/v1` object. |

### “Can't answer why” ledger

Add an entry immediately when evidence cannot explain a visible result; stop the lap
rather than filling the gap with repeated input.

| UTC phase | Player-visible symptom | Evidence preserved | Why still unknown | Bounded next investigation |
| --- | --- | --- | --- | --- |
| Synthetic authoring preflight | A Playwright double-click selected **Item dropped** but did not add it, even though the picker help says double-click adds. | Failed browser trace, DOM, and screenshot stayed in the ignored E2E artifact directory; the E2E stopped. | The DOM handler and visible instruction agree, but this run did not establish whether the miss was browser automation timing or a real pointer interaction defect. | The validated lap uses the explicit **Add to quest** button. Reopen this only if a human double-click also fails; do not grow a speculative interaction matrix first. |

### Player-experience observations

These questions are acceptance evidence, not optional polish:

- Did the three actions feel like one ritual or three laboratory instructions?
- Did each message arrive at a dramatic and causally clear moment?
- Did the 30-second offering window feel tense, generous, or irrelevant?
- Could the player continue without reading receipt JSON or maintainer terminology?
- Did the drawer explain what had happened without competing with the in-world story?
- Did **OTHER VERSION** communicate an older inscription, or only an internal version mismatch?
- Did finding target `Wood` under **More options → Make this action specific** feel
  like useful progressive disclosure or like the editor hid a necessary part of the ritual?

Record Derek's words after the lap, including positive moments. Do not translate a
player reaction into a mechanism-only diagnosis.

### Observed-need test growth

Built before the first lap because these paths can damage or confuse the OMEN install:

- running-game refusal;
- sentinel and quarantine ownership;
- interrupted preparation cleanup;
- byte-exact DLL, inbox, and active-state restoration;
- private-world safety closure;
- exact r1/r2 Contracts inspection;
- strict JSON stop behavior;
- separation of human pacing from machine receipt timeouts.

Deferred until a lap demonstrates need: broad malformed-receipt matrices, multiple
stale-hash variants, and additional inbox-precedence permutations. Each future case
must cite the observation that earned the new surface.

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
| Capturing both native streams made Python's progress dots terminate preflight despite exit code zero. | Windows PowerShell 5.1 promotes redirected native stderr to `NativeCommandError` under `ErrorActionPreference=Stop`; unittest writes progress on stderr. | Only success output is captured for host rendering. Native stderr stays visible, and the external process exit code remains the pass/fail authority. |
| The next all-green preflight returned one quoted JSON string instead of one JSON object. | `Invoke-Preflight` serialized its result before the common dispatcher serialized every action result, producing valid but double-encoded JSON. | Preflight now returns an object and the dispatcher is the sole serializer; a source pin forbids a second serializer in the preflight function. |
| The first live startup monitor stopped while reading five valid `active_set_missing` receipts. | `JObject` implements `IEnumerable`; `Read-StrictToken` returned it through PowerShell's success pipeline, which unrolled it into `JProperty` values before typed deserialization. The strict JSON parser had correctly accepted the files. | The reader now emits its token with `Write-Output -NoEnumerate`. An observed-need self-test feeds one valid receipt through `Status` before the existing duplicate-property rejection test. No player action or world entry is repeated. |
| After valid receipts parsed, startup proof could not hash BepInEx's writer-held `LogOutput.log`. | `Get-FileHash` uses file sharing that conflicts with the active log writer, while the harness's later log text reader already uses shared read/write access. | The common SHA-256 helper now hashes through a read stream with `FileShare.ReadWrite`; an observed-need test holds a fixture log writer open while startup proof reads it. The preserved live startup remains the proof—no relaunch is requested. |

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
- The guided rehearsal rendered valid first-drop progress as an empty circle beside
  `1/2`. Derek immediately asked why it lacked a green check. The current mark means
  "this beat has not advanced yet," but visually resembles a missed or failed input;
  both drop rows are evaluations of one repeated node, not peer quest nodes, yet the
  numbered flat list also makes the first evaluation look like a child or separate
  beat. Group repeated attempts beneath their owning beat with visible hierarchy and
  explicit partial/complete states (for example, an amber **partial 1/2** child row
  under beat 2, followed by its green completion) before treating the rehearsal as
  self-explanatory.
  **Resolved in Phase 3.4.** Attempts group beneath their owning beat and render an
  amber `partial 1/2` row before the green completion; the synthetic E2E asserts that
  row in the real DOM. An attempt counts as progress when it matched the beat's own
  clause, so a sliding window that replaces an expired attempt still reads as progress.
- After F10, Derek saw yellow text `1 pack, 1 loaded`. The count was visible, but
  **loaded** is ambiguous because F11 is the step that activates a pack. Replace the
  debug-shaped summary with language that states what F10 actually proved—for
  example, one candidate checked and accepted, activation unchanged—and reserve
  activation language for F11.
  **Resolved in the creator-loop UX baseline.** Check copy now states what checking
  proved and names the quest (`The Woodbound Signal 1.2.0 is ready. Press F11 to play
  it.`); activation language belongs to load alone. All copy is composed by the pure
  `CreatorLoopNotice` contract fact and proven sentence-by-sentence in xUnit; the
  plumbing also moved off the story's Center HUD channel to TopLeft. See
  `docs/creator-loop-ux-baseline.md`.
- After F11, the large yellow confirmation led with `Loaded quest-7b849e 1.0.0`
  followed by the full content hash. Derek expected the friendly name. Lead with
  **The Woodbound Signal**, retain the version, and demote the pack ID and a shortened
  hash to diagnostic detail rather than making raw identity the primary player copy.
  **Resolved in the creator-loop UX baseline.** The confirmation is now
  `Now playing: The Woodbound Signal — 1.2.0`; pack id, short hash, and activation tail
  live in the drawer's detail line. The title rides the pack inspection's existing
  compile pass, so no manifest change was needed. Two structural fixes landed with the
  copy: an idle repeat F11 no longer archives a fresh activation epoch (ten presses used
  to evict the entire rollback history), and the orphaned-charm consequence
  (`3 charms belong to an earlier telling`) now travels with the keypress instead of
  hiding in the drawer's evidence scroll.
- On first opening F9 after r1 activation, the drawer said `0 locally owned` while
  multiple Arcane Sight labels said `OTHER VERSION / LOCAL OWNER`. The implementation
  counts only markers that are both current and locally owned, so the values are not
  mechanically inconsistent, but the summary label is. Either show all local-owner
  bindings separately or name the intersection explicitly (for example,
  `0 active + locally owned`).
  **Resolved in the creator-loop UX baseline.** The summary now names the intersection
  it counts: `0 active + locally owned`, pinned in the python suite.
- CAST changed the selected sign to a bright purple glow. Derek's immediate reaction
  was, `woooo it changed glowly colors`. Preserve this strong, magical state change:
  it made successful binding legible at player altitude without requiring receipt or
  identity text, and it belongs prominently in the screenshot-led tutorial.

Record Derek's words after the lap, including positive moments. Do not translate a
player reaction into a mechanism-only diagnosis.

#### Live r1 player read

- As a tutorial sample, chat → two offerings → reclaim felt functional in the
  demonstration. This establishes legibility, not yet drama.
- Derek knew the 30-second window existed but did not notice it while playing. If the
  limit is meant to matter—especially during combat or another task—it needs a
  universal timer bar, yellow countdown warnings, or another unmistakable temporal
  affordance. A contract-only deadline is not player tension.
  **Resolved in Phase 3.4.** An always-on banner shows the running deadline
  (`1/2, 6 seconds remaining`) without opening the creator drawer, and turns red under
  five seconds. It reads one pure fact, `TriggerCountdown`, which reports a window as
  running only once an attempt has started it. The next lap should judge whether the
  banner's placement and wording carry tension during actual combat.
- F9 felt like a kernel system-information panel. Its valuable role is to make the
  cognitive switch between defining/creating in the web Studio and acting as an
  in-world Creator feel limited to nearly cost-free, while still exposing the state
  needed to build an instance.
- F6 QuestLab may carry feature bloat. Treat extraction as an attenuation question:
  identify which creator-facing capabilities belong outside Lab, and investigate
  Arcane Sight as part of a **spellbook** surface. Do not add a new wholesale palette
  before ownership and observed need are clear.
  **Identification complete** in `docs/quest-lab-persona-audit.md`: the Lab is ~70%
  diagnostics, ~20% release-gate machinery, ~10% indirect authoring, 0% publishing,
  with a persona-by-persona ownership map. Two facts that reframe the question: there
  are **two unrelated Arcane Sights** (Lab gallery highlighting vs. Runtime charm
  debugging), and the Lab's "Spellbook" tab already occupies the name Phase 4's
  portable notebook needs. Extraction itself stays deferred to Phase 4 on observed
  need, per this bullet's own rule.
- Once the already-known chat/drop/pickup path had reconfirmed behavior established
  two weeks earlier, the manual lap stopped producing decision-bearing evidence.
  Derek said, **“this is putting me to sleep.”** Treat known contract behavior as a
  synthetic regression surface; ask for player keyboard time only when a novel
  uncertainty can change the product decision or the next build.

### Player-facing follow-through

- Build a screenshot-led Studio authoring tutorial from this exact Woodbound
  progression. Preserve the useful sequence and field-level landmarks from the live
  session, sanitize machine/browser details, and do not ask the player to recreate
  screenshots. Schedule it after the validation lap so tutorial production does not
  expand or interrupt the proof surface.

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

## Phase 3 exit — Ten-Minute Desperate Defense (session 1, paused)

Intended experience: the creator loop should feel like one product from cold entry
through an in-place content update, and the ten-minute defense should answer whether
the countdown carries tension, reinforcement reads as escalation, and mercy reads as
mercy. Session 1 reached the update loop and paused before the defense began; the run
remains open so the defense verdicts can still be taken on the same preparation.

### Pre-lap findings with known causes

| Observation | Why it happened | Resolution |
| --- | --- | --- |
| Preflight's Studio gates failed with missing-symbol errors against current source. | The interim Contracts package keeps a fixed version while its bytes evolve; the gates restored from NuGet's immutable-version global cache, which still held pre-refresh bytes. | The lap ran Preflight with the E2E-style hash-keyed `NUGET_PACKAGES`; the harness now keys its own Studio-gate cache the same way (landed as "Key Preflight's Studio gates to the interim package bytes"). |
| The staged 1.0.1 was published before 1.0.0 had ever been activated, so the first check found two versions and loaded 1.0.1 directly — the first-load and update beats collapsed into one pass, and later loop presses had nothing new to announce. | The runtime always loads the highest valid version, so an update beat only exists if the newer version arrives after the older one is playing. The mid-session staging cue was executed without first confirming an activation receipt existed. | Process rule for future laps: mid-session staging waits on receipt evidence of an activation, not on the cue alone; the seat workbook should carry the sequencing explicitly instead of presuming a pre-staged second version. |

### Findings

| Observation | Evidence | Disposition |
| --- | --- | --- |
| The retokened drawer still reads as a kernel panel. Verbatim: "kernel UI on f9 press looks the same. i thought we updated the UI?" — after roughly a day of design-system adoption work, the seat could not tell the surface had changed. | Seat workbook verdict 1 note; the drawer screenshot shows the new tokens (title-first status card, amber arming action, four-step content-update strip) are live. | This is the follow-up measurement the lap was held for: token adoption alone does not cross the product threshold. Decides the composition question — the drawer needs the full status-card composition and/or a Phase 4 scope move, per the switch-cost decision brief. |
| Orphaned workflow state spammed rejected transitions at startup: 47 `transition/rejected · active_set_missing` receipts between world load and first check, including a 39-receipt burst in one second. | Receipt series in the run's captures. | Preparation quarantines `active/` and inbox packs but not `state/`, so historical pending transitions replayed against a missing active set. Fix candidates: quarantine `state/` recoverably in `Prepare`, and/or the runtime retires pending transitions after bounded rejection (same family as the 3.4 replay-forever fix). |

### "Can't answer why" ledger

| UTC phase | Player-visible symptom | Evidence preserved | Why still unknown | Bounded next investigation |
| --- | --- | --- | --- | --- |
| Update loop, after 1.0.1 was active | Five F10 presses and three F11 presses over seven seconds appeared to do nothing; the player concluded the keys were broken and ended the session. | Receipts show every press was accepted (`check/accepted` for both versions ×5, `load/already_active` ×3). `CreatorLoopNotice` composes copy for both branches ("… is already playing. Nothing new to load." / "… is already playing.") and the notices are wired to the top-left channel. | The copy exists and fires, yet nothing legible reached the player — unknown whether the message HUD queued/deduplicated repeats, the line's duration was too short, or attention was on the second monitor at emission time. | Inspect the top-left emission path for queue and duplicate behavior under rapid repeat presses; if the channel drops or delays repeats, make the idle response re-assert visibly. Do not add new channels before that answer. |

### Player-experience observations

- The status card passed its first test: title, version chip, and Now playing were
  ticked as answering "which revision is running?" without scrolling.
- The four-step content-update strip (LOOK 2 found → VALIDATE 2 valid → LOAD 1.0.1 →
  CONFIRM active) rendered a fully honest machine story — but the moment the loop had
  nothing new to say, the surface went silent, and silence read as breakage. The idle
  state needs to be as legible as the busy state.
- A version-only bump (identical content hash) was accepted as a real update with a
  fresh activation epoch — convenient for staging, but worth an explicit product
  decision on whether "nothing changed" updates deserve their own copy.
- Defense verdicts (countdown tension, escalation, mercy), evidence read-back, and
  the Studio round-trip were not reached; the experience never started. They remain
  the open Phase 3 exit questions for session 2.

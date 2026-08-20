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
| The staged 1.0.1 was published before 1.0.0 had ever been activated, so the first check found two versions and loaded 1.0.1 directly — the first-load and update beats collapsed into one pass, and later loop presses had nothing new to announce. | The runtime always loads the highest valid version, so an update beat only exists if the newer version arrives after the older one is playing. The mid-session staging cue was executed without first confirming an activation receipt existed. | **Machine-enforced.** `ValidateRevision -ExpectedVersion 1.0.1` now refuses until the r1 activation is proven, and the read-only `ConfirmActivation` action answers go/no-go for the second Publish from the load receipt and the active set. The runbook holds 1.0.1 as an unpublished Studio draft until `activation_confirmed`; the self-test pins the refusal and the gate. |

### Findings

| Observation | Evidence | Disposition |
| --- | --- | --- |
| The retokened drawer still reads as a kernel panel. Verbatim: "kernel UI on f9 press looks the same. i thought we updated the UI?" — after roughly a day of design-system adoption work, the seat could not tell the surface had changed. | Seat workbook verdict 1 note; the drawer screenshot shows the new tokens (title-first status card, amber arming action, four-step content-update strip) are live. | This is the follow-up measurement the lap was held for: token adoption alone does not cross the product threshold. **The full composition has since landed**: status card leads with its own DETAILS disclosure, the update ladder became the canvas's circles joined by rails with sentence-case actions, the evidence feed gained the time gutter and row-group CAST tint, and captures/arcane-sight/rollback receded behind one MACHINERY disclosure. Session 2 opens with a bounded two-minute composition pre-check before any beat. |
| Orphaned workflow state spammed rejected transitions at startup: 47 `transition/rejected · active_set_missing` receipts between world load and first check, including a 39-receipt burst in one second. | Receipt series in the run's captures. | Root cause corrected on code inspection: the burst was not a state replay — the engine's pre-subscription branch wrote one rejection receipt for **every** normalized world event while the active set was missing, before the subscription filter and duplicate window, unbounded by construction. **Both halves landed**: the engine now bounds identical (operation, error) rejections to three receipts plus one suppression marker until the error clears, and `Prepare` additionally quarantines `state/` recoverably (with `inbox-dev/`, a sibling gap found in the same inspection) so stale pending transitions cannot survive into a lap either. |

### "Can't answer why" ledger

| UTC phase | Player-visible symptom | Evidence preserved | Why still unknown | Bounded next investigation |
| --- | --- | --- | --- | --- |
| Update loop, after 1.0.1 was active | Five F10 presses and three F11 presses over seven seconds appeared to do nothing; the player concluded the keys were broken and ended the session. | Receipts show every press was accepted (`check/accepted` for both versions ×5, `load/already_active` ×3). `CreatorLoopNotice` composes copy for both branches ("… is already playing. Nothing new to load." / "… is already playing.") and the notices are wired to the top-left channel. | **Answered by the bounded inspection.** Every idle press composed a byte-identical string and handed it to the HUD with a message amount of 0; the top-left channel merges a repeated text into the line already on screen and only renders its "xN" counter once summed amounts exceed one, so every repeat vanished — and the HUD-not-live branch was silent, so the captures could not separate "not live" from "shown and missed". | Landed, no new channel: idleness is a contract fact tagged where the sentence is composed, idle responses ride amount 1 so a repeat re-asserts as "… x2", the status card carries a first-class "Now playing — up to date" state, and emission now logs `hud_absent` when the HUD is not live. **CLOSED in session 2:** the seat observed the native counter live — seven idle presses rendered "… x7" on the top-left channel while the card held "Now playing — up to date". |

### Session-2 player-experience observations

- The story text landed well — "the yellow text was nice" — but it is a glimpse with
  no history: "i think we should also post it in chat or say so there's history of it
  not just the glimpse." Product direction: story messages should also land in chat
  scrollback so a moment can be re-read. **Landed 2026-08-20 (W3):** the authored
  `message` action now writes to chat as well as Center.
  *(Correction, same day: this text was written up as the overrun's copy. It was the
  victory copy — see the corrected observation below.)*
- The evidence feed failed discoverability cold: "if you didn't ask me to read it i
  wouldn't even have known it was there."
- Composition direction from the seat, verbatim: "i think this addon works better as
  a top bar with horizontal layout, the 4 step sequence is large because we're in
  R&D but think of a user doing this hundreds of times, it could just minimize to 4
  dots and they'd understand what was happening." The ladder earned its size for
  first-run legibility; the hundredth run wants a minimized horizontal strip. Feeds
  the Phase 4 composition/scope decision alongside the switch-cost brief.
- ~~After the overrun (authored outcome `fail`), the EXPERIENCE row read
  "Ten-Minute Desperate Defense: **complete**" — a failed ending labeled with the
  workflow's terminal state instead of the authored outcome.~~ **Corrected
  2026-08-20 against the run's receipts: this observation was wrong, and its being
  wrong is the more useful finding.** No overrun ever fired. At 12:17:17 the
  `timer_elapsed` event re-evaluated the `hold` stage, and the route that matched was
  `held` (priority 40, outranking `overrun` at 30): by then the corpses were long
  gone, `spawned_enemies_cleared` read 8, and the eight kills still sitting in history
  satisfied the EVENT clause. The receipts say so plainly — `event/matched` on
  `timer_elapsed`, then `message-held`, `reward-held` and `stop-defense` executed, then
  `transition/complete` on `held`. The player won, the engine recorded the win
  correctly, and "complete" was the truthful label. What actually went wrong is the
  timing: **the victory arrived nine minutes and three seconds after the kill that
  earned it**, carried by the deadline instead of by the last Greyling, and the yellow
  text the seat praised at the ten-minute mark was the victory line, not an overrun.
  This is the same defect as the ledger's first row and its cleanest proof: the tally
  was never broken, only stale at the instant each kill was evaluated. (The EXPERIENCE
  row still spoke machinery — it printed the raw outcome token beneath a card already
  showing the title. Landed 2026-08-20: the row drops the repeated title and reads
  "Completed." / "Failed.")
- The evidence gutter's local HH:MM:SS stamps rendered in the feed as designed.
- The unifying alert finding, verbatim: "yeah we need a way for these alerts to
  appear in a known *config plan on the screen. it too much to ask in these simple
  ones let alone hard combat." Every channel miss this session — the camouflaged
  deadline, the glimpse-only story text, corner plumbing, the undiscovered evidence
  feed — is one failure: there is no single, player-known (and player-configurable)
  alert anchor. Tonight's channel taxonomy says who is speaking; nothing yet
  guarantees where the player should look. This is a Phase 4 presentation
  requirement, senior to per-channel fixes.
- Verdict 5, answered in part: "the click to open feels nice" — the drawer→Studio
  crossing landed. The card-agreement check is honestly unanswered (the game was
  closed before comparing states). Studio layout direction, verbatim: "button need
  to be larger and we need to think thru the positioning, as a user flow and how the
  UI placement and size can optimized for to align left to right, top to bottom sort
  of naturally reading patterns for (for me) relative the flow of actions, outputs,
  decision points and feedback(s)." Same family as the drawer's top-bar/4-dots
  direction: composition should follow the reading order of the creator's flow.

### Player-experience observations

- The status card passed its first test: title, version chip, and Now playing were
  ticked as answering "which revision is running?" without scrolling.
- The four-step content-update strip (LOOK 2 found → VALIDATE 2 valid → LOAD 1.0.1 →
  CONFIRM active) rendered a fully honest machine story — but the moment the loop had
  nothing new to say, the surface went silent, and silence read as breakage. The idle
  state needs to be as legible as the busy state.
  **Resolved in code, pending the seat.** "Now playing — up to date" is a first-class
  status-card state, idle HUD responses re-assert through the channel's own repeat
  counter, and the strip itself became the canvas's circle-and-rail ladder. Session 2
  judges whether the idle state now reads as calm rather than silence.
- A version-only bump (identical content hash) was accepted as a real update with a
  fresh activation epoch — convenient for staging, but worth an explicit product
  decision on whether "nothing changed" updates deserve their own copy.
- Defense verdicts (countdown tension, escalation, mercy), evidence read-back, and
  the Studio round-trip were not reached; the experience never started. They remain
  the open Phase 3 exit questions for session 2.

## Phase 3 exit — session 2 (2026-08-20, concluded — Phase 3 does not exit yet)

Session verdict: the creator loop ran end-to-end in the proven order and every
session-1 fix held live (state quarantine, staging gate, idle "x7" re-assertion,
punctual timer). The defense itself surfaced two live-only defects that block the
exit: the kill tally never counted a visibly slaughtered wave, and a running
ten-minute deadline carried zero tension. Both sit in this section's ledger with
bounded next investigations. Verdict 1 (composition) never received a yes/no — it
was superseded by a direction: the seat wants a horizontal top-bar that can minimize
to four dots, with alerts in one known configurable anchor. Verdicts on escalation
and mercy were unreachable behind the tally defect. The lap did what only a lap can
do.

### "Can't answer why" ledger

| UTC phase | Player-visible symptom | Evidence preserved | Why still unknown | Bounded next investigation |
| --- | --- | --- | --- | --- |
| Defense, 12:07:21–12:08:14 | Derek killed the entire wave ("i slaughtered them, but it was fun :]") and nothing happened — no held/victory message, no completion; the quest stayed in `hold` with the ward timer running toward overrun while the seat believed it had won. | Eight `event/ignored · kill · $enemy_greyling` receipts, every one `0/1`, no rejection diagnostics on the event receipts; `transition/advanced muster→hold` and its three executed actions are clean at 12:07:16. | **ANSWERED 2026-08-20, from this run's own receipts.** At 12:17:17 the `timer_elapsed` event re-evaluated the same `hold` stage and `held` matched immediately — the eight kills were still in history and `spawned_enemies_cleared` now read 8. So the tally was never broken; it was stale at the only moments it was ever read. `kill` is emitted from a Harmony postfix inside `Character.OnDeath()` and evaluated in that same call stack, one frame before `ZDOMan` releases the dying creature's ZDO, so the live poll still resolved the corpse and counted it alive. Nothing in the engine re-evaluated a stage between events, so the wrong answer stood for nine minutes and the win was finally delivered by the deadline. | **Landed 2026-08-20 (W1, ADR 0006):** a bounded recheck re-runs the same observation pass and the same routes for up to five ticks after an event leaves a world-tally route unmet, and ignored receipts now carry the THRESHOLD's real current/required plus its clause trace instead of the top-level ALL's `0/1`. Proven game-free in xUnit (`ARouteGatedOnAWorldTallyIsWonByTheRecheckWhenTheTallySettlesLate`). **Still open until session 3 observes it live:** the win landing within seconds of the final kill. The lap's `kill_partial` and `wave_cleared` expectations are the machine form of that verdict. |
| Defense, whole ten minutes | No countdown was perceived while a real 600-second deadline ran: "it might have and i didn't see it.. but i didn't see one." The timer fired to the second at the ten-minute mark, so it was live throughout. | Seat answer; fight-start screenshot shows no pill; receipts prove the timer ran 12:07:16→12:17:17. | **ANSWERED 2026-08-20 by inspection.** The banner rendered the whole ten minutes: `Pending` has no proximity cap, nothing clips it, and it draws with the drawer closed. It went unseen because of presentation alone — a fixed 320×30 dark strip at y=64, four pixels under the NetworkSense band in the same treatment; unscaled fixed-pixel geometry that shrank at 1440p; and "594 seconds remaining", which is a log line rather than a countdown. `RefreshDeadline`'s `catch { line = null; }` was also silent, so a throw and a quiet window were indistinguishable. | **Landed 2026-08-20 (W2):** `TriggerCountdown.Seconds` reads as a clock past a minute ("9:54"), the banner is a bordered amber pill sized to its copy, scaled via `GUI.matrix`, and anchored by a player-configurable `Presentation/DeadlineAnchor` fraction — an interim subordinate to ADR 0005, not a new fixed position. The swallowed read now writes one `deadline_unreadable` receipt per distinct failure. The copy and `Pending` behavior are xUnit-covered for the first time. **Still open until session 3 answers it:** does the pill carry tension in combat (verdict 3a). |

Run `phase3-exit-2026-08-20-s2`. Prepare quarantined 12 packs + 2 active + 8 state
files and deployed the day's payload; 1.0.0 published alone, 1.0.1 held as an
unpublished draft until the activation proof passed (receipt + active-set agreement,
activation `…2d270b3f`) — the new staging gate ran live and the update beat survived.
The ladder's ✓ glyph renders in the game font; startup produced no rejection burst.
The idle re-assertion is live-confirmed: seven idle presses rendered "… x7" on the
top-left channel — session 1's silent-idle ledger row is closed.

### Findings

| Observation | Evidence | Disposition |
| --- | --- | --- |
| With 1.0.0 playing and 1.0.1 staged, F10 composed the multi-quest copy — "2 quests are ready. Open F9 to choose." — and the card said "2 quests ready — choose". Derek hunted for a chooser that does not exist: both candidates are versions of one quest, and the only action is Load validated update / F11. Verbatim: "ahh this is very not intuitive… it seems like i should click on something to start the quest or select it? but... not sure how or where." "tollerable for a dev loop, unacceptable for a user." | Session-2 seat screenshots; `CreatorLoopNotice.Check` (`valid.Length == 1` branch) and `Card` (`valid.Length > 1` → Choice) count valid candidates, not distinct pack ids. | Copy defect, seat unblocked via the amber button. **Landed 2026-08-20 (W3):** `Check` and `Card` count distinct pack ids, so two versions of one quest compose the update sentence and the UpdateReady state; "choose" is reserved for genuinely distinct quests, and the xUnit copy matrix pins the session-2 scenario by name. |
| Speaking in chat did not start the defense; Derek first suspected his own position ("maybe it's at ground level and it's cuz i'm in the air"). The receipt said otherwise: `event/unbound · chat_sent` — the event arrived and matched the active quest, but no charm was bound, and an unbound quest evaluates nothing. | Session-2 receipts (`event/unbound` at 12:01:35Z); runbook and seat workbook both omit the CAST beat for the defense (Woodbound's script had `bind_r1`; the defense script never did). | Two fixes: (a) the runbook/workbook gain an explicit "cast the ward's anchor" beat before first contact copy promises a start; (b) product follow-up — `event/unbound` is honest machinery but invisible at player altitude; the player-facing surface should say "this quest needs a charm cast on an object" (card state or notice) instead of silently ignoring input the way session 1's idle loop did. **Landed 2026-08-20 (W3):** the unbound branch writes one warning-kind evidence line per activation naming the missing Charm and the gesture, and the EXPERIENCE row says the same thing when no binding matches. |
| CHECK captured a different object than the player believed — the raycast grabbed a stone through/behind a wall — and the cast's purple glow appeared around the corner: "it seems to cast but i don't see the purple" then "haha, looks like it just selected a target thru the wall??". The READY summary named only `player-built piece [1:26827]`, a ZDO id no player can map to a thing in the world, so the real target was only discovered after CAST, by glow location. The strip also snapped straight back to READY, and extra ` presses silently re-captured. | Session-2 receipts 12:04:35Z (`bind/inscribed` ZDO 1:26827); seat screenshots — the glow itself works on player_built_piece. | Pre-cast target legibility: CHECK should visibly mark the captured object in-world (the same highlight machinery the glow uses, in a "pending" treatment) so the player confirms *what* before CAST; investigate whether the capture raycast should stop at occluding geometry; and the strip needs a landed state instead of re-arming silently. **Landed 2026-08-20 (W3):** CHECK lights the captured object in CAST purple (borrowed renderer blocks plus a child lamp, both restored on release), the summary names the piece via `Localization` instead of a bare kind, and the strip has a third LANDED state that holds until the next CHECK. **Still open:** whether the capture raycast should stop at occluding geometry — session 3 seat question. |
| The authored title stacks in three places at once — status card title, the status line under the ladder ("Now playing: Ten-Minute Desperate Defense — …"), and the EXPERIENCE row ("Ten-Minute Desperate Defense: not started") — making section boundaries unclear. | Session-2 seat screenshot; Derek: "the stacked text repeats itself … in multiple places making it unclear where the sections are". | Composition follow-up: the card owns the title; the ladder's status line and the EXPERIENCE row should speak state without re-announcing the name (e.g. "not started", "up to date"). **Landed 2026-08-20 (W3):** the EXPERIENCE row no longer prints the title. The ladder's status line is the loop's own sentence at the moment of activation and is left alone; whether it still repeats too much is a session-3 seat question. |

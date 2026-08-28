# Retrospective - one character should have been enough

Written after Derek chose `C` and the retained ERA17 seat reached **The Name in Ash**, bound
and current in the installed game. This is a retrospective about producing the seat, not a
claim that the authored content has passed Derek's creative judgment. That judgment is the
next thing the seat exists to collect.

## The shortest honest version

Derek's contribution to the successful end of this journey was one character:

> C

That selected **Pilgrimage of Ash and Ice**: a mythic, uncanny, reverent path from the burning
skull shrine, through the sky temple, to the snowy ascent. The machine then reframed eight
experiences, certified the guild, transferred it to the installed Runtime, activated the exact
content hash, discovered the retained sign, waited for a fresh heartbeat, bound the opening
experience, and returned a start-stage run with all four Studio proof lines passing.

That is what the workflow was supposed to make possible.

At the beginning of the same journey, I could not clearly answer what Derek was supposed to
provide. After a planning session, I reported that I was "waiting for agents" when there were
no agents whose work blocked the result. Derek had to ask who I was waiting for, then had to
explain again that running a game UI consumes the context of the rest of his fleet and that he
could not afford to become a human KVM for a toolchain that already had launch, close, movement,
capture, and MCP harnesses.

That was not a cosmetic communication error. It exposed the central defect in my posture:
**I was tracking my internal work instead of the transaction the product owed the creator.**

## What we were actually building

The deliverable was not a DLL, an MCP provider, a questpack, a test suite, or a runbook. Those
were supporting artifacts. The deliverable was this boundary:

- Derek supplies premise, tone, composition, clarity, responsiveness, and play-feel judgment.
- Automation owns discovery, build, deployment, exact world/profile entry, retries, target
  selection, evidence, shutdown, and recovery.

The repository already said this in [`../creator-os.md`](../creator-os.md) and
[`../PLAN.md`](../PLAN.md). The planning session did not protect us because I treated those
documents as material to review rather than as the operating contract for the next action.
The corrective question was the one Derek supplied directly: **"What is Derek actually
supposed to provide in this session?"** Once answered, the rest of the architecture became much
easier to judge. Any step that needed Derek to copy a command, identify a process, select the
known world, relay an MCP result, retry a deployment, or diagnose a stale status was a defect in
the machine half of that contract.

## The journey, with the edges left in

The final path was earned through installed contact, not inferred from green layers.

| Contact point | What the environment taught us | What changed |
| --- | --- | --- |
| Existing fleet tooling | The Steam lifecycle harness already lived in `lumberjacks`; Valheim MCP patterns already lived in Isolate and Baseline. Asking Derek to reproduce those capabilities would have rebuilt the wrong layer and spent the seat. | Creator Session remained the install/world lifecycle authority. Isolate became the authenticated, bounded gateway rather than a new source host or a human-operated sidecar. |
| Exact prefab vocabulary | The authored example `wood_wall` looked plausible and was wrong for installed Valheim 0.221.12; the real prefab was `woodwall`. | Generator validation now checks creator examples against the prefab dump. The broken spelling was deliberately observed red before the correction was accepted. |
| First Isolate-to-Runtime slice | A loopback endpoint and a mounted Python module were not identity proof. Runtime control also needed the exact installed machine, world, and active content. | The Quest-owned provider release exposed four bounded tools through the Isolate image. The installed r1 slice rejected the wrong world, then armed the exact session, returned 32 bounded candidates, bound the 4.9 m sign, and reported the exact active run. |
| Red-team authority | A caller could otherwise arrive with stale harness context, and a receipt without the current Creator Session could be mistaken for current authority. | Runtime latches authority only after exact world entry reaches `entered`; creator and run controls carry and echo `creator_session_id`; missing caller credentials, prior-session requests, and forged receipt identity were all observed rejected. Status now returns the non-secret session id needed by the next control, so Derek is not the context transport. |
| Multi-experience guild activation | One guild pack can contain eight experiences. Pack equality alone cannot tell Studio which one is being played, and an ambiguous rebind must not be presented as a version mismatch. | Studio correlates through the containing guild, filters receipts to the selected experience, reports `other_experience` honestly, and recognizes the explicit binding receipt that selects the experience. |
| Real main-thread pauses | An already-armed Runtime can briefly stop refreshing while Valheim saves or stalls. Treating that as permanently disconnected makes a normal Play attempt become a human retry. | The Play path has a bounded 30-second grace only for a previously observed armed session. Run controls retain their stricter fresh-status gate. |
| ERA17 shutdown | The old 20-second close budget killed Valheim while its 1.30 GB world was still being written. The r4 lap preserved a 496 MB partial `.new` file instead of hiding the damage. | Stop now grants at least 120 seconds, quarantines an interrupted partial if force is ever required, and records the result. r5 observed a 32.46-second save, `World saved`, graceful exit, and no remaining partial files. |
| Package provenance | Repacking over incremental build state produced fresh archive bytes that still claimed the prior Git revision. A successful `dotnet pack` line was not provenance evidence. | Both local packages are clean-packed and inspected; the validator must see the exact committed source revision inside the archive before the package commit can land. |
| The real premise choice | Activating C correctly produced `other_experience`: the guild was current but no experience could be guessed. After candidate discovery, my first automated bind arrived in the narrow interval before Runtime's next same-frame run heartbeat and was refused as `runtime_run_status_stale`. | Automation preserved the safety refusal, waited for `connected` run status, then submitted the exact r6-pinned bind. No gate was weakened and Derek was not asked to retry. The resulting run is `run-20260827T162426244Z-9dc80dc7`, bound to `1:3177525` at stage `start`. |

The installed records are
[`../evidence/isolate-era17-runtime-20260827-r1.json`](../evidence/isolate-era17-runtime-20260827-r1.json),
[`../evidence/isolate-era17-runtime-20260827-r2.json`](../evidence/isolate-era17-runtime-20260827-r2.json),
and [`../evidence/era17-premise-c-20260827-r1.json`](../evidence/era17-premise-c-20260827-r1.json).
The edge register carries the reusable observations in
[`../quest-rd-opportunity-matrix.md`](../quest-rd-opportunity-matrix.md).

## What actually worked

### 1. Receipts turned mysteries into bounded diagnoses

The strongest parts of the system declared their own state. `creator_world_mismatch`,
`creator_session_mismatch`, `runtime_creator_session_mismatch`,
`active_experience_ambiguous`, and `runtime_run_status_stale` were not prose theories. Each
named the exact precondition that had failed. Once those diagnostics were consulted, the repair
was normally one bounded operation rather than repeated input.

The last bind is the cleanest example. Weakening the three-second freshness check would have
made the symptom disappear by discarding a safety property. Reading the diagnostic and the
fresh `runs.json` heartbeat showed that the request order was wrong: candidate receipt, then
fresh heartbeat, then bind. Choreography, not a broader permission, was the fix.

### 2. Recovery was treated as part of autonomy

Launching a game is easy to demo. Owning a creator session means preserving the exact install,
world pair, character, one-shot entry state, logs, and partial saves through success and failure.
The r4 save failure was expensive evidence because it disproved the idea that a generic process
timeout was good enough. The r5 graceful save made the lifecycle credible. An autonomous tool
that can enter the world but cannot safely leave it is not autonomous; it has merely deferred
the human intervention.

### 3. Identity moved with the machine output

Before the final provider change, a later caller needed to remember the Creator Session id from
the harness that launched the game. In a single-thread demo that can look harmless. In Derek's
fleet it makes his memory the bus between tools. Returning `world_entry.creator_session_id` from
bounded status changed the shape of the workflow: the next request can derive every pin from the
previous machine result. The stale r4 negative controls proved that this was authority, not just
metadata.

### 4. The creative brief compressed 58 images into one real decision

The drone survey was not handed to Derek as an unstructured directory and a request to inspect
everything. A 12-frame brief presented three explicitly provisional readings and asked one
question. `C` was enough to choose the fantasy and tone. That is the right use of human
attention: judgment over prepared evidence, not search through machine output.

### 5. Tests became valuable after contact named what deserved pinning

The final source work contains extensive xUnit, Python, generator, browser, package, identity,
boundary, and secret-scan coverage. Those checks caught real defects: the browser journey first
failed with `creator_session_unavailable`; the package validator rejected stale revision
metadata; a missing test fixture field broke the full Lab suite; the malformed prefab guard was
observed red. They matter because installed contact identified the contracts worth preserving,
not because green checks could substitute for the installed journey.

## What hurt

### 1. I spent trust before I had earned a seat

"Waiting for agents" was both vague and false as an explanation of the blocking state. It made
Derek inspect my orchestration instead of his game. In this environment, that is not a small
status-message defect. The entire fleet loses access to his attention when he is sitting in the
game UI. Every unnecessary clarification therefore carries the cost of reloading context into
one window.

The operational rule is sharper than "communicate better": **never report an internal state as
though it were progress toward the user's outcome.** Name the exact running process, receipt,
gate, or blocker. If nothing is running, say nothing is running. If no agent exists, never use
agents as the explanation.

### 2. We knew the plan but lost the human boundary

The repository had an authority chain, a mission-control manifest, a phase vocabulary, and a
golden rule. I still began by asking what code remained instead of what irreducible input the
seat was meant to supply. A plan does not guide work merely because it has been read. It guides
work only when the next action can be rejected for violating it.

The useful preflight question is now unavoidable: **Could automation do this step from the
artifacts already in scope?** If yes, it cannot appear in Derek's seat instructions.

### 3. The R&D and baseline transitions were blurred

The `/rnd` contract says vertical slice first, one edge ends a lap, and tests wait until the
shape survives contact. This journey eventually honored the substance but not the cadence. r1,
r2, r4, r5, r6, source hardening, package provenance, and the C activation happened inside one
long conversational arc. Persistence was necessary, but the unnamed lap boundaries made the
work opaque and encouraged the feeling that completion was always another gate away.

A better R&D sequence would have named and closed each probe:

1. Can Isolate read and control the exact entered Runtime without a human relay?
2. Can stale world/session authority be observed rejected?
3. Can the large ERA17 world close and recover without force?
4. Can one creative premise choice reach a bound/current installed experience?

Each result then earns its own decision: stop, set up the next probe, or enter the equivalent of
`/rnd --lock` and pin what survived. The tests we ended with are appropriate baseline work; the
mistake was allowing them to become the visible center of the journey before the C-to-sign slice
had answered the product question.

### 4. Consolidated tools did not yet mean consolidated resume context

Isolate now owns gateway authentication, caller identity, provider mounting, and the bounded
transport. Quest owns the provider release and Runtime semantics. That is the right repository
boundary. But Derek still had to tell this session where the Steam harness and Valheim MCP
references lived, and had to paste the external `/rnd` contract because a Claude hook does not
become Codex or fleet context by wishing it so.

The provider's status projection is the beginning of the real fix: context needed by a later
operation travels as machine-readable non-secret state. The plan, mission control, evidence,
and edge register do the same across sessions. A local skill path or one agent's memory is not a
fleet control plane.

### 5. The final staging path is proven once, but still agent-mediated

The C revision was authored through the real Studio HTTP surface and installed through the real
Runtime path. It was not hand-edited in Runtime state. That is valid vertical-slice evidence.
It is not yet a turnkey, released "premise choice to prepared seat" operation. The staging used
known project ids and an agent-composed mapping from the chosen premise to eight titles and
messages. The candidate-to-heartbeat-to-bind order was executed correctly only after the first
refusal exposed it.

That is an honest next edge, not a reason to build another framework immediately.

## What Isolate means after this lap

"Isolate consolidates the tooling" now has a concrete boundary:

- one authenticated loopback gateway identifies the Isolate image and mounted provider set;
- one Quest-owned, hash-verified provider archive exposes only status, bounded receipts,
  Creator Session operations, bounded run control, and reviewed ground-only Lab replay;
- Runtime remains the authority for machine, world, Creator Session, private-world safety,
  pack membership, binding ownership, and receipts;
- Creator Session remains the authority for install, Steam lifecycle, backups, shutdown, and
  recovery;
- Studio remains the authoring and certification surface.

Consolidation does not mean moving all of those responsibilities into the Isolate repository.
It means a cold caller can use one bounded gateway without reaching into sibling source or
asking Derek to bridge the owners manually.

It is not finished. The installed image still reports a null source revision; the provider was
mounted from a checkout-local release; caller credentials and state mounts remain
operator-specific; and clean volunteer-machine setup and teardown have not been run. Those are
recorded limitations, not hidden qualifications. They keep `queue.workbench-boundary` ready
rather than complete.

## The proof that mattered

The decisive sequence was not the 353 Lab tests, 109 Studio tests, 373 Python tests, two browser
journeys, two package inspections, or the all-history secret scan, although every one passed.
It was:

1. Derek replied `C`.
2. Studio persisted **Pilgrimage of Ash and Ice**, revision 27, with eight experiences.
3. Runtime activated content hash
   `6e0f85f1bc2a26a7e7974817c17897281b1e722ea274509522a6bacb4aa3526e`.
4. Isolate reported exact Creator Session `era17-broken-way-20260827-r6`, armed and watching.
5. Studio bound **The Name in Ash** to sign `1:3177525` and reported Validation, Transfer,
   Activation, and Rebind `PASS`.
6. The game exposed one fresh start-stage run and no pending control request.

One human choice crossed every real boundary. That is the vertical slice.

## The one next probe

Do not respond to this retrospective by building a generic campaign orchestrator. Run one cold
resume first:

> Given only the repository root, a Creator Session id, and a premise choice, can an agent with
> no transcript context reach the same bound/current seat using the declared status and evidence
> surfaces?

The existing read-only starting command is:

```powershell
tools\creator-session\Invoke-CreatorSession.ps1 Status -SessionId era17-broken-way-20260827-r6
```

If the cold resume succeeds, the artifacts are enough and another tool would be scaffolding. If
it fails because project identity, sequencing, or the next action is missing, that observed gap
earns a narrow choice-to-seat driver. Its output should be a seat packet containing the human
action, expected response, live identity pins, evidence links, and recovery handle - never a
list of commands for Derek to relay.

## What this does not claim

- Derek has not yet supplied the creative verdict on the sign, response, composition, or feel.
- The C guild is active on the dev channel, not a final production release.
- The retained r6 game and authored world have intentionally not been shut down or restored.
- Clean-machine Isolate portability remains unproven.
- Cross-fleet conversational context is not solved; durable repository and machine state merely
  make a cold recovery possible.
- One successful C-to-seat sample does not yet justify generalizing the staging choreography.

The lesson is not that more automation is always better. It is that the human boundary must be
designed first, the unknown shape must be touched end to end, and only the edges that survive
contact should be hardened. When that is done correctly, one character can be enough.

## Postscript - the probe disproved our reassurance

The retrospective ended by asking for one cold-resume probe. We ran it. That was more useful
than declaring the earlier seat reassuringly ready.

The probe showed that the saved sign's physical ZDO changed while its logical object did not.
It showed that a Creator Session could discover `tugcorp` under one Steam account and then
launch Valheim under another unless account identity was made a first-class pin. It showed that
the opening content itself was still too thin to justify Derek's scarce seat. And the final hot
activation showed that an eight-experience guild could correctly activate yet still refuse to
rebind because activation had cleared the explicit experience selector.

Each result changed the product:

- the Charm gained a durable marker carried through runs, receipts, Studio, and Isolate status;
- Prepare and Launch gained an active-Steam-account contract;
- **The Name in Ash** became a five-stage live sequence with an authored fork rather than a
  one-trigger placeholder;
- action execution gained reserve/commit semantics, mutation preflight, exact partial-spawn
  recovery, and route-scoped replay; and
- same-pack activation now retains a selector only if the new archive resolves it exactly once.

The last edge was especially instructive. r11 produced `active_experience_ambiguous`. Runtime
was right to refuse; the mistake would have been to explain the diagnostic away, manually bind
again, and call the seat ready. r12 instead proved the correction through a fresh guild revision:
the existing durable marker selected `quest-0d3372`, the new run was created, and
`experience_started` advanced to `name-in-ash` without another select, bind, move, or human
retry. Studio then returned Validation, Transfer, Activation, Rebind, and Runtime observed as
five separate PASS lines.

That changes what is honest to present to Derek. The offer is no longer "the plumbing says a
sign is bound." It is: the exact account/world/profile is entered; the opening survived cold
identity change; the final Runtime is deployed; the new revision activated itself onto the
reviewed Charm; and the game is waiting for four creative acts whose consequences have been
rehearsed through both endings. Derek is being asked whether the experience is good, not whether
the machine can find its own state.

The remaining qualifications mattered at that evidence capture. The live seat had not yet
proved the Resin grant, Greyling placement/kill, or final choice. Spawn cleanup across a cold
generation still failed closed into explicit reset, and physical spawned-object ids were not
yet portable. Those were limitations to state before the sitting, not reasons to turn Derek
into the recovery mechanism.

The corrected lesson is sharper than the title: one character can be enough only after the
machine has earned the right question. Reassurance is not readiness; an executed precondition
chain is.

## Second postscript - cleanup removed a world precondition

Derek then supplied exactly the kind of input the seat existed to collect. He played through
the sign, Resin offering, and Greyling trial to `pilgrim-choice`. His report was not ambiguous:
the hard freeze every approximately three seconds was distracting, and the experience was
otherwise "pretty inspiring."

The machine half had still failed him. The OMEN cleanup removed non-Quest add-ons to establish a
known install, but ERA17's scale depended on NetworkSense's cached portal and spawner connection
paths. Because that dependency was not declared as part of the world's runnable contract, the
cleanup removed it along with the unrelated add-ons. The result was a cleaner install that had
lost a prerequisite for usable play.

This is the same human-boundary failure in a more instructive form. Derek was not asked to copy
a DLL or inspect a log, but a machine-observable defect still reached his sitting and competed
with the creative experience. "Pretty inspiring" is the product signal. "Hard freeze" is the
automation debt that should have been found before the seat. Both belong in the record; neither
should be averaged into a reassuring verdict.

Session r13 made the missing prerequisite explicit. Creator Session now selects a
world-specific support manifest, pins the captured NetworkSense 0.5.80 assembly and its four
managed dependencies by hash, deploys a sanitized cache-only behavior profile, includes those
files in session recovery, and refuses Launch unless the current game log proves the required
cache paths and excludes the vanilla scan markers. The successful cold launch recorded 5,171
saved portal links and a cached spawner pass over 92,359 sources and 19,493 targets. A later
snapshot recorded three full-world cache cycles one minute apart over 13,875 portal objects,
all with zero dirty work, while Valheim remained responsive and Runtime remained armed at
`pilgrim-choice`.

The first r13 attempt also stopped reassurance from becoming evidence. Valheim entered and
emitted every required line, but `[IO.File]::ReadAllText` requested sharing incompatible with
BepInEx's open log. The caught exception turned the text into an empty string, so Launch
rejected all signals. The correction reads and copies through a
`FileShare.ReadWrite | FileShare.Delete`
handle, validates the captured copy, and hashes that copy. Only the second cold launch, which
produced operation hash `0e0b62854de5f418e66526eeec8102256ec5fbffd6063dfb5185f98692a7cb85`,
is the successful launcher proof. No test result is being substituted for that execution.

The rescue remains tactical, and saying so is part of presenting it honestly:

- the NetworkSense DLL is an unsigned captured external artifact, exact by hash but not
  authenticated as an upstream publication;
- the full NetworkSense patch assembly still registers even though configuration disables every
  non-cache behavior switch; and
- its runtime portal tag index is stale after bulk load because it keys invalidation to
  `ZDOMan` identity rather than a load generation, so only the current static saved topology is
  in scope. Portal creation, retagging, and traversal were not proved by this lap.

The recurring-path artifact proves the cache keeps running; it does not prove the human symptom
is gone. Derek's next contribution remains irreducibly human: make the final Resin-or-Stone
choice and say whether the distraction disappeared and whether the inspiring part survived.

The added lesson is simple: **cleanup is dependency resolution, not deletion.** A known-clean
install is not ready when it has silently removed a world-specific support precondition. The
world, its support manifest, the launcher proof, and rollback have to travel as one machine-owned
transaction before the seat begins.

## Third postscript - the automation seat is part of the product (AM4)

The next failure was not a Runtime algorithm. It was where I chose to run it.

Derek explained the operational fact I should have treated as a hard precondition: when he is
sitting in a game UI, the rest of his fleet no longer shares that context, and a Valheim window
on OMEN can steal focus or receive accidental input while he is trying to build. Saying that he
would not need to do much in the window missed the point. The window itself had consumed the
scarce seat.

AM4 was already idle, signed in as `waryfool` / persona `Zephar410`, attached to a monitor, and
available for exactly this kind of unattended client work. Once Valheim moved there, the whole
lap became calmer: launch, world entry, fixture mutation, binding, reset, retry, logs, and
receipts could continue while OMEN remained Derek's. He did not become a human KVM merely
because the automation required a visible game.

That changes the preflight question again. It is not only, "Can automation do this?" It is:

> Can automation do this on a machine whose foreground, crashes, pop-ups, and restarts do not
> consume the human seat?

If the answer is yes, choosing OMEN is already a workflow defect.

### The reassuring diagnosis failed, and that was useful

The missing sign looked like a straightforward 32-candidate truncation bug in a dense world.
The selector was changed to reserve each target kind, the contracts and Runtime were rebuilt,
and AM4 was cold-started. The next candidate receipt still contained no sign.

That installed failure was more valuable than an explanation of why the selector should work.
The selector hardening was valid, but it was not the cause of this symptom. The proven Demo lap
had replayed the reviewed 12-piece Godbuild first; the current world had not. Once the actual
precondition chain was compared instead of reconstructed from memory, the missing sign became a
missing fixture.

The fixture replay immediately found another edge that Windows tests had not: Mono rejected the
capture sidecar because `DataContractJsonSerializer` attempted to load an unavailable
configuration section. Replacing it with the already-deployed Newtonsoft.Json dependency and
cold-starting AM4 made the same bytes pass check, build all 12 pieces, and return an exact
12-versus-12 `MATCH`.

There are two lessons here:

- a plausible root cause is only a probe until the installed symptom changes; and
- a documented choreography is not a precondition unless the automation executes it in the
  current session.

### A completed reset was not a completed player flow

The earlier reset contract could retire the old run, create a successor record, and return
`completed` without sending the successor through `experience_started`. Storage had reset; the
experience had not restarted. A human would meet a sign whose run existed but whose opening cue
and workflow did not.

The fix moved the player-visible postcondition into the reset transaction. The exact successor
identity is persisted once. Reset completion now waits until that run's workflow exists, and a
retry supplies the same idempotent start event rather than minting another successor.

AM4 proved the shape that matters:

1. bind **The Name in Ash** to the replayed sign;
2. observe `experience_started` advance the initial run to `name-in-ash`;
3. preview and apply reset;
4. observe exactly one successor on the same durable binding advance independently to
   `name-in-ash`; and
5. replay the same confirmed reset and observe the same reset ID, same successor ID, one active
   run, and no second event/action/transition chain.

That is the difference between a reassuring registry entry and a rerun Derek can actually play.

### What consolidation means now

The lap exposed an agent-mediated seam, and this session closed it rather than documenting it as
future work. The fifth provider tool, `quest_lab_replay`, now hash-verifies one release-mounted
reviewed manifest and artifact pair, asks Runtime to reassert the exact live
machine/world/session and private-world build authority, then performs Lab
check/count/optional-build/diff. If it enabled build authority, it verifies `build_off` even when
the Lab attempt fails. Exact-count `MATCH` is already complete; partial, extra, location-drifted,
sky-mode, stale-session, or artifact-drifted state fails closed.

The red team changed the implementation before Derek reached it. It found that a stale world
entry was insufficient authority, that sky support pieces could never satisfy the reviewed
zero-diff contract, and that deleting a timed-out fixed mailbox could race a successor request.
The provider now obtains fresh Runtime authority on every call, refuses sky mode, and leaves an
expired request for Runtime or Lab to claim and reject.

The installed sequence then found the edge that source reasoning did not. On AM4:

1. the first provider call built all 12 reviewed pieces and returned exact `MATCH`;
2. the immediate retry omitted `blueprint_build` and returned
   `blueprint_replay_already_match`;
3. a deliberate clear destroyed all 12 pieces but reported `failed` because same-frame
   validation ran before Valheim retired their ZDOs;
4. the corrected Lab coroutine waited for the marked count to reach zero and the same live clear
   returned `completed` after a cold deploy;
5. provider replay rebuilt and matched all 12, and its retry was again a no-op; and
6. final Runtime status reported build authority disabled before Valheim saved and shut down
   cleanly with no residual process or `.new` save.

That is the `/rnd` distinction that matters here: the deliverable is not reassurance that one
call should work. It is the observed build edge, observed retry edge, observed failure, corrected
cold result, restored fixture, disabled authority, and retained receipts.

The ownership boundary remains important. Quest owns this target-local reviewed-artifact replay
and its correlated receipts. Isolate still owns the authenticated gateway, released image,
caller identity, provider loading, and machine lifecycle. Quest did not learn private fleet
topology or reach into a sibling checkout. The open work is the clean Isolate release proof:
source identity, a generated per-lap credential, volunteer-machine setup, and correlated
teardown. Pair staging also remains fail-closed rather than crash-atomic, and the provider lock
is process-local rather than a host-wide lease.

### The final gate was allowed to change the product

The focused Studio guild journey passed, and an earlier full browser run had also passed. That
was not enough. Two later complete-suite runs failed only when the guild journey followed the
other synthetic journey: once before reset and once before restore. Both failed with HTTP 400
`runtime_run_status_unreadable` before Studio wrote an apply mailbox or Runtime receipt.

The order sensitivity exposed two publication races. Studio inspected `runs.json` with a
single-shot `FileInfo` lookup followed by a separate open while Runtime atomically replaces that
heartbeat once per second. Either full snapshot was valid, but the path lookup could still land
inside a Windows replacement window. The synthetic Runtime amplified the probability by
replacing unchanged status every 40 ms and could expose a scoped control receipt before the
matching generic evidence and successor status.

The correction did not weaken the three-second freshness gate. Studio now opens the stream
first, checks that exact stream's length, and retries only filesystem replacement failures five
times at 10 ms. It deserializes and validates once after a successful read; corrupt JSON, wrong
schema, invalid identity, stale status, and an exhausted replacement window remain hard failures
before mailbox dispatch. The shared Contracts reader received the same bounded I/O hardening.
The fixture now publishes at the installed one-second cadence and treats status/evidence as
preconditions of its scoped receipt commit marker.

This produced the regressions that matter: a 15 ms exclusive-file window recovers; a held window
exhausts the budget and writes no mailbox; missing status writes no mailbox; and both browser
journeys pass together. The lesson is uncomfortable but useful: an exact test passing alone can
still be reassurance. The combined integration order is part of the system being claimed.

### What I would present to Derek now

Not reassurance, and not a request to keep the AM4 window handy.

I would present the executed receipt chain, the exact defects it found, the fixes that changed
the installed outcome, and a statement that he has nothing to do for the mechanics lap. OMEN is
released. AM4 is the automation seat, stopped after a clean save and ready on demand. The
remaining Derek input is still creative and can be
batched later on ERA17: the Resin-or-Stone choice, whether the recurring distraction is gone,
and what in the experience deserves to survive.

The committed index is
[`../evidence/quest-am4-fastlane-20260828-r1.json`](../evidence/quest-am4-fastlane-20260828-r1.json).
The full evidence root is:

`C:\work\comfy-quest\captures\creator-session\quest-am4-fastlane-20260828-r1\evidence`

The operational lesson is the one Derek had been asking for all along: autonomy is not measured
by how many commands the agent knows. It is measured by how little machine work reaches the
human seat before the machine has earned a genuinely human question.

One last provenance check reinforced that lesson. The final heartbeat-reader correction changed
the release-build Contracts, Runtime, and Lab hashes after the first installed AM4 proof. It would
have been easy to call the source gates green and reuse the older live result as reassurance.
Instead, the exact final bytes were backed up, deployed, cold-loaded on AM4, taken through pinned
world entry and a provider no-op `MATCH`, independently checked for disabled build authority, and
saved and stopped cleanly. Derek supplied no clicks, focus, relay, or observation. The point of
the extra lap was not another screenshot; it was eliminating the gap between "this source passed"
and "these exact bytes ran."

The landing gate also forced one final concession. A provider responder timeout previously called
transient recurred. Making the fake authority outlive the provider did not fix it; stress still
failed with much wider budgets. Instrumentation finally exposed Windows sharing violation
`WinError 32` on the synthetic Lab authority's one-shot mailbox unlink. That thread exited and the
provider correctly waited for a receipt that would never arrive. The fix was a bounded retry of
that exact `PermissionError`, not another timeout increase and not retrying the suite until green.
The retry branch then passed 100 fresh-process repetitions, the preexisting-build branch passed
50, all 26 provider tests passed, and the complete 390-test suite passed three consecutive times.
A failure that returns has promoted itself from observation to defect; the write-up has to promote
it too.

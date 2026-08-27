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
  Creator Session operations, and bounded run control;
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

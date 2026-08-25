# Creator OS roadmap audit — 2026-08-24

Status: findings report. Observed against `1fef480` with a dirty working tree (see
Provenance). No product code was changed by this audit.

## Scope

This audits the Creator OS **roadmap surfaces** — `docs/quest-mission-control.json` and its
rendered HTML, `docs/creator-portfolio-requirements.md`, `docs/creator-os.md`,
`docs/five-intent-program-plan.md`, the renderer, and the CI gates that claim to keep them
true. It reads the implementation only far enough to check whether those surfaces describe
it accurately.

The central conclusion:

> **The remaining work is smaller than the roadmap makes it look, because the contract
> layer is consistently ahead of the runtime layer.**

Every finding below was read in source and carries a `file:line` receipt. Severity is the
auditor's judgment, not a repository classification.

## Provenance

- `HEAD` = `1fef480`; `page.program_commit` = `a07cb62` (one commit behind).
- The working tree was already dirty when the audit was taken, from in-flight work unrelated
  to this audit: `docs/creator-os.md`, `docs/creator-portfolio-requirements.md`,
  `docs/quest-mission-control.{html,json}`, `tests/test_quest_mission_control.py`,
  `tools/render_quest_mission_control.py` modified; `.codex-pdf-profile/` and
  `docs/quest-mission-control.pdf` untracked.
- **Consequence:** findings A3, A4, and A5 describe renderer code that was *uncommitted* at
  audit time. Re-verify those receipts if that change lands or is discarded.
- `main` was 9 commits ahead of `origin/main`.

---

## A. Roadmap machinery bugs

### A1 — CI breaks the moment this work is pushed *(high)*

`tools/render_quest_mission_control.py:304` runs `git show -s --format=%s <program_commit>`
and fails the render if the commit is unreachable. `page.program_commit` is `a07cb62`;
`HEAD` is `1fef480` (`git rev-list --count a07cb62..HEAD` = 1). `actions/checkout@v4`
defaults to a depth-1 clone, which does not fetch the parent commit object. So
`tests/test_quest_mission_control.py` — which calls `render()` — errors in the `quest` job
with `program commit is not available: a07cb62`. It passes locally only because the local
clone has full history.

*Fix:* `fetch-depth: 0` on the `quest` job, or degrade the label check to a warning when the
object is absent.

### A2 — The tests that prove the "implemented" foundation never run in CI *(high)*

`.github/workflows/ci.yml:46-60` runs `ComfyQuestLab.Tests`, the Python suite, four drift
checks, and `dotnet pack`. It does **not** run `src/Quest.Studio.Tests` (72 tests, including
`QuestStudioPortfolioTests.cs` and `QuestStudioRunControlTests.cs`, both introduced by
`a07cb62` — the exact commit the mission-control page cites as proof) or
`src/Quest.Studio.E2E.Tests`, the repository's only browser-driving suite. `README.md:37-53`
lists both as local verification.

The page's claim that "automated coverage shows internal and browser behavior" is true on the
local cutter and ungated everywhere else.

### A3 — The renderer patches its own template with an unasserted find/replace table *(medium)*

`tools/render_quest_mission_control.py:588-622` applies 34 string replacements to the finished
document. At least four are dead: the outputs of "Cold-load first. Create second.",
"Now · recovery acceptance", "Keep the canonical build unchanged", and "Last handoff" are all
absent from the rendered page, so those keys never matched.

The remainder are load-bearing and nothing asserts they matched. The template emits a
`<select>` with `id="cold-verdict"` and `value="obvious"`, and the table rewrites both to
`lane-verdict` / `no-relay`. If a template edit breaks the `value="obvious"` key the page
still renders, the JS whitelist silently rejects the unknown value on reload, and the seat
verdict stops persisting with no error. `test_session_state_is_bounded_local_and_exportable`
checks for `lane-verdict` but not the option values.

### A4 — The replacement table runs over manifest content *(medium)*

Replacements are applied to the whole rendered document, so generic keys — "Project source",
"Last handoff", "Acceptance evidence" — would silently rewrite those phrases if they ever
appeared in a queue detail or a decision recommendation.

### A5 — Encoding corruption is papered over, not fixed *(medium)*

Six replacement keys carry U+FFFD variants, and line 625 does a blanket replacement of U+FFFD
with a middot. That compensates for a UTF-8-corrupting round trip rather than fixing it, and
it converts *any* replacement character — including one legitimately present in manifest text
— into a middot.

### A6 — Two state enums are unvalidated *(low)*

`ALLOWED_PHASE_STATES` and `ALLOWED_QUEUE_STATES` are enforced (`:24-25`, `:109`, `:114`), but
`machines[].state` and `environment[].state` are only `require_text`. `badge()` falls back to
`state.title()` and emits `badge-<state>` / `status-<state>`; an unknown value renders an
unstyled badge or a grey dot with no error.

### A7 — `--out` produces broken links *(low)*

`href_for()` computes relative paths from the module-level `OUTPUT.parent`, ignoring
`args.out`.

### A8 — The headline progress bar measures the inactive lane *(low)*

The hero progress bar tracks the 16 checkboxes of the "Prepared human-acceptance lane" — the
one lane the page itself says is "not the development method or the next task queue" and is
not yet active.

### A9 — The strongest evidence is rendered away *(low)*

`evidence_receipts()` reads only two arrays from `docs/creator-os-expected.json`, so the proof
panel shows 11 generic operation/status pairs and drops `observed_live_godbuild` entirely —
the real session id, request ids, world uid, and capture hash. The test asserts on that block;
the page never shows it.

### A10 — The page cannot tell that its own sources are dirty *(low)*

`validate_source_pin` checks that a marker string is present, not that the file is committed
or current. At audit time the pinned commit was one behind `HEAD` and both cited sources were
modified in the working tree.

---

## B. Contradictions between live documents

### B1 — Three incompatible "phase 1-5" numberings are live at once *(high)*

Mission-control phase 3 = "Adaptive event semantics". `docs/creator-os.md:183-207` product
roadmap item 3 = "Make autonomous live integration routine".
`docs/five-intent-program-plan.md:83` phase 4 = "Quest Lab spellbook". "Phase 3" and "Phase 4"
mean different things depending on which document you are reading — and the mission-control
page stitches its phase list from both families (phases 1-2 cite the program plan, 3-5 cite
creator-os / requirements).

### B2 — 4A and 4B have two incompatible scopes *(high)*

`docs/five-intent-program-plan.md:110-111` puts **named live anchors and saved-world bundles
in 4A**, and multi-experience Runtime in 4B. `docs/creator-portfolio-requirements.md:257-264`
makes anchors conditional, says explicitly that saved-world packaging does not gate R&D, and
puts multi-experience Runtime in 4A.

The pivot section says the requirements document "owns the detailed gates", but the
program-plan table 12 lines above it was never corrected. Someone working from the program
plan would build exactly what `decision.distribution` forbids.

### B3 — The 4A exit depends on a harness that exists but cannot be consumed *(high)*

The 4A exit requires automation that "launches and closes the installed game through the
standalone harness". There is no launcher in this repository — the only `valheim.exe`
reference anywhere is an existence check at
`tools/creator-session/Invoke-CreatorSession.ps1:84`.

Investigation of the sibling repositories found:

- The repository is `lumberjacks-platform`, not `Lumberjacks`.
- It holds a **server-join** lifecycle harness,
  `fieldlab/scripts/Invoke-NativeValheimClient.ps1` (1,306 lines): Steam applaunch with a
  `+connect` argument, graceful-then-forced stop, file-marker readiness. It does **not** do
  main-menu to named local world.
- The **world-entry** harness is in `baseline`, not `lumberjacks-platform`:
  `tools/selfie-stick/Invoke-OrbitCapture.ps1`, whose own description explicitly rejects the
  server-join harness — that harness has no path for loading a local world.
- **Neither is consumable.** `lumberjacks-platform` has zero git tags and zero GitHub
  releases; its only packable project is `Comfy.Transport.Contracts`; the harness is a `.ps1`
  with no project file. No document tells another repository how to consume it.
- **A packaged script would still be inert.** It depends on `ComfyNetworkSense.dll` (owned by
  `networksense`, no release) for character select, and world entry additionally needs the
  camera-proof plugin whose source sits in a retired `comfy` checkout with no release and no
  build lane. That is **three publishing lanes, not one.**
- The script dot-sources `Assert-RepoIdentity`, which throws unless the git origin is
  `lumberjacks-platform` — so a vendored copy cannot run either.

Note that `tools/assert_no_reach_in.py` bans only absolute work-root paths, so a vendored
script carrying Program Files defaults would pass the scan. The boundary here is held by
`Assert-RepoIdentity` and `BOUNDARY.md`, not by the reach-in gate.

Two constraints make this tractable rather than grim:

- **The no-synthetic-input invariant is already satisfied everywhere.**
  `tests/test_creator_session.py:98-116` asserts `Invoke-Expression`, `SendKeys`,
  `keybd_event`, `Console.instance`, and `ZInput.Simulate` appear nowhere here — and all four
  sibling repositories independently hold the same line by design.
- **The world-entry mechanism is proven and small.** `SaveSystem.GetWorldList()`, match by
  name, *refuse if absent*, `ZNet.SetServer(...)`, `LoadMainScene`, then wait for a real
  in-world player. That is a mod calling the game's own APIs, not input simulation.

### B4 — Superseded documents carry no marker *(medium)*

No `SUPERSEDED` or `HISTORICAL` marker appears anywhere in `docs/*.md`, though the convention
exists (`docs/runbooks/OMEN-LAP-PHASE3-EXIT.md` is marked "Do not run it"). Live-looking but
pre-pivot: `docs/five-intent-program-plan.md:78-84` (superseded by the pivot 12 lines below
it), `docs/phase-4-scope-packet.md` (still asks "What I need from you" about a Phase 4 shape
that was re-decided), `docs/handoff-2026-08-20.md`, `docs/f9-switch-cost-decision-brief.md`.

### B5 — The 4A checklist ends on a Phase-5 deliverable *(medium)*

Checkpoint C's stage 9 is "Release", inside the 4A lane. FR-REL-001 makes a release bind a
world bundle, and `decision.distribution` defers world bundles out of R&D entirely. Checking
"Release" in 4A asserts something the program says is not built.

### B6 — The checklist mandates a step the program calls optional *(medium)*

Checkpoint B step 6 requires `Replay` and a `MATCH` receipt. `docs/creator-os.md:174` and
`queue.first-godbuild` both say replay awaits use only when a reusable module needs cloning.

### B7 — The parked Phase-3 lap describes a UI that was replaced *(medium)*

`docs/runbooks/OMEN-LAP-PHASE3-EXIT-S3.md:47-50` tells the seat to press F10, then F11, then
to open the F9 drawer and aim. `decision.bar`, dated four days later, says F9 now "expands or
minimizes one always-present overhead bar", replacing the drawer.

The deeper point: **the runbook passes structural validation while describing controls that no
longer exist.** The renderer checks that the lap yields exactly 5 steps and 3 verdicts, and it
does — so the gate is green on a procedure that cannot be followed. That is evidence that
*syntactic runbook validation is insufficient for cold seat procedures*. Given `AGENTS.md`
("Three seat sessions were burned on sequences that had never been executed"), this is the
highest-consequence stale document in the repository.

### B8 — A guardrail misstates the state of the code *(medium)*

`cautions[7]` says portfolio and reset/rerun are "not yet integrated". Every layer is in fact
wired: endpoints (`src/Quest.Studio/QuestStudioEndpoints.cs:201-224`) to service
(`QuestStudioService.cs:131-137`) to store, browser UI (`QuestStudioPage.cs` —
`previewRunReset`, `confirmRunReset`, `pollPendingRunControl`), a file mailbox
(`QuestStudioRunControl.cs:105`), and a Runtime poller (`ComfyQuestRuntime.cs:77`).

What is missing is live-Valheim evidence — which is exactly what `environment[4].detail` in
the same file says. The caution as written sends a reader hunting for wiring that exists.

### B9 — The command reference omits three of the ten verbs *(low)*

`Arm`, `Disarm`, and `GalleryRebuild` are absent, though the page's own proof chain asserts
`runtime_arm` and `runtime_disarm`, and Checkpoint B step 4 names `GalleryRebuild`.

### B10 — Decisions have two homes *(medium)*

Eight "Decisions in force" live only in a mutable JSON, while `docs/adr/` is an append-only
lane with 7 accepted records and an explicit reversal protocol. A decision reversed in the
JSON leaves no trace.

---

## C. Requirement-coverage gaps

### C1 — The requirements document has no machine link at all *(high)*

There are exactly **47** distinct `FR-` / `NFR-` identifiers, and they appear in exactly two
files: the requirements document, and the manifest (4 times, incidentally, as
`source_contains` strings). Zero references in code, tests, or any ledger.

FR-EVID-002 — "each advertised capability must map to at least one checked-in contract test
and, where it claims live Valheim behavior, one genuine Runtime receipt" — is entirely
unimplemented, in a repository that machine-verifies everything else.

The fix is cheap: all 47 use one uniform bold form, so they extract with a two-line regex.

### C2 — Three requirements are assigned to no lane *(medium)*

- **FR-LOOP-003** (clean-vs-resume as a pre-play choice) — the baseline table names it
  absent; no lane claims it.
- **FR-RUN-002** (authority and participants) — the engine supplies one local participant,
  and the peer machine is policy-off for content work.
- **FR-EVID-001** (Studio run evidence grouping and lineage) — named incomplete, never
  scheduled.

### C3 — Evidence retention violates the authority model *(high)*

NFR-OBS-001 reads, verbatim: *"Receipt and evidence stores have explicit size/age retention
with archive/export before deletion. Reads stay bounded, and **pruning one run cannot break
another run's audit chain**."* Both halves are violated, from opposite ends. The severity is
about evidence integrity, not storage or polling cost.

**Silent cross-run evidence destruction.** `receiptDirectory` is `<root>/receipts/run-control`
— a **flat directory keyed by `request_id`**, not partitioned by run
(`network/mod/ComfyQuestRuntime/RuntimeRunControlController.cs:43`). `PruneReceipts(128)`
(`:47`) orders **all** files by `LastWriteTimeUtc` and deletes the tail. Pruning is therefore
global across runs: a burst of resets on one run silently evicts an older run's receipts. That
is not an inferred risk — it is the exact clause NFR-OBS-001 names as forbidden.

Supporting scale mismatch: the registry holds up to `MaxRuns = 512` runs and preserves lineage
via `PredecessorRunId` / `ResetId` (`network/mod/ComfyQuestContracts/RuntimeRuns.cs:44`,
`:53`), while only 128 run-control receipts survive. A long 4B campaign *guarantees* runs
whose reset receipts are gone but whose lineage still points at them.

**Why that matters here specifically.** Receipts are not logs; they are read back as
authority. `src/Quest.Studio/QuestStudioService.cs:363-368` constructs a `RuntimeReceiptStore`
over the live install and feeds `List(50)` into the Observe surface. And the established proof
idiom is a **correlated receipt set**, not a single file:
`network/mod/ComfyQuestLab.Tests/ExperienceContractTests.cs:52` asserts the complete set
`dev_activation`, `dev_rebind`, `dev_transfer`, `dev_validation` for one correlation id. Evict
one member and the proof is incomplete, with no signal that anything was lost. FR-EVID-002
makes "one genuine Runtime receipt" *the* proof of a live-behaviour claim, and `cautions` says
receipts are the machine facts. There is even a test named `RuntimeReceiptsAreImmutableFiles`.

**The other direction.** `network/mod/ComfyQuestContracts/RuntimeReceipts.cs:47-52` has no
prune, no delete, and no cap at all. The store the Observe surface reads grows without bound,
and `List()` enumerates and sorts every file on each call — degrading over exactly the long 4B
campaign whose exit is "multiple author-to-rerun cycles".

One store cannot forget; the other forgets silently and across runs. Both break the same
requirement, and the roadmap's proof model depends on the store being trustworthy.

### C4 — The standalone Workbench has no lane *(medium)*

NFR-MCP-001 and `queue.workbench-boundary` (state `ready`) have no place in the 4A/4B/4C/5
roadmap and no exit gate — while `docs/creator-os.md`'s roadmap item 3 makes it a precondition
for the autonomous integration that 4A's exit depends on.

### C5 — A declared bound is advisory *(low)*

`QuestStudioPortfolioStore.MaxProjects = 512` is reported in the portfolio `limits` block and
bounds bundle import, but nothing checks it on project creation — contra NFR-BOUND-001
("limits remain explicit and executable-tested").

### C6 — Root cause: nothing links work to lanes or requirements *(high)*

The manifest's 13 queue items carry exactly `id`, `title`, `detail`, `state`, `source`, and
`source_contains` — **no `phase`, `lane`, or `requirements` field** — and phases carry no
back-reference either. The validator therefore *cannot* detect a queue item that belongs to no
lane, or a requirement that no queue item claims.

That is not a coincidence alongside C2 and C4 — it is why they happened. The Workbench
boundary sits in the queue as `ready` with no lane (C4), and FR-LOOP-003 / FR-RUN-002 /
FR-EVID-001 sit in the requirements with no queue item (C2), and nothing anywhere could notice
either.

**C6 is the origin of the program invariant** recorded in
[`creator-os-build-strategy.md`](creator-os-build-strategy.md). Closing it closes C1, C2, and
C4 with one small addition to a validator that already exists.

---

## D. Implementation gaps that block the stated exits

These are **findings, not roadmap-repair work.** Each receives a ledger entry and a lane
disposition; none is fixed while repairing the roadmap surfaces.

### D1 — One check blocks multi-experience play *(high)*

`QuestPackStore.InspectLane` validates, hashes, and compiles N experience documents happily.
`network/mod/ComfyQuestRuntime/RuntimeCharmBinding.cs:45` then returns
`active_experience_ambiguous` the moment it finds a second one, and no call path
(`BindingAllows`, `InscribeAim`, `RebindDevActive`) carries an experience selector. Second site
at `RuntimeExperienceEngine.cs:1094`.

This blocks FR-RUN-001 and the 4A exit's "selects and runs more than one guild experience".
The contract layer is ahead of the runtime layer, which is good news for cost.

### D2 — The action allowlist is duplicated with different comparers *(medium)*

`network/mod/ComfyQuestContracts/ExperienceContract.cs:174` uses `OrdinalIgnoreCase`;
`src/Quest.Studio/QuestStudioWorkspace.cs:1111` uses `Ordinal`. Same six strings, no shared
constant. A capitalized action type compiles in the contract and fails Studio validation.

### D3 — Studio's whole GUI is three string literals in one file *(medium)*

`src/Quest.Studio/QuestStudioPage.cs` is 948 lines / 202 KB of `Html` / `Css` / `Js` raw
strings, asserted against by 33 text-matching Python tests. Guild-scale UI growth lands here.

### D4 — Creator Session cannot drive run control *(medium)*

Its ten verbs are Prepare, Status, GalleryRebuild, Capture, Replay, Arm, Disarm, BuildOn,
BuildOff, Close. Reset and rerun are reachable only through Studio HTTP, which sits awkwardly
with FR-LOOP-002 ("one Creator Session owns ... every precondition the next operation needs").

### D5 — Schema acceptance is asymmetric *(low)*

`NormalizeDocument` migrates v2 to v3 on read (`QuestStudioWorkspace.cs:1042`) but import
rejects anything but exact v3 (`:308`). The same bytes upgrade from disk and are refused on
import.

---

## E. Hygiene

- **E1** `docs/quest-mission-control.pdf` (511 KB) and `.codex-pdf-profile/` are untracked and
  absent from `.gitignore` — a browser profile directory beside a repository with push
  protection and a full-history secret-scan gate.
- **E2** `Lumberjacks/src/` is an empty directory tree — a `filter-repo` fossil, since
  `PROVENANCE.md:18` lists `Lumberjacks/src/Quest.Studio` among the extraction include paths.
  Harmless, but it reads like a reach-in mount point.
- **E3** `main` is 9 commits ahead of `origin/main`.
- **E4** The PDF is a third representation of the page with no drift gate.

---

## Punch list

Ordered for execution. Items 1-4 change adopted product baselines and are **recommendations
requiring sign-off**, not edits. The rest are mechanical.

| # | Item | File | Sign-off |
| --- | --- | --- | --- |
| 1 | Amend the 4A exit to NFR-SEAT-001's one-launch allowance | `docs/creator-portfolio-requirements.md:266-271` | **yes** |
| 2 | Reconcile the 4A/4B scope tables (B2) | `docs/five-intent-program-plan.md:110-111` | **yes** |
| 3 | One phase vocabulary across the three roadmaps (B1) | all three | **yes** |
| 4 | Creator-world-entry ownership (ADR 0012) | `docs/adr/0012-*.md` | **yes** |
| 5 | `fetch-depth: 0` on the `quest` job (A1) | `.github/workflows/ci.yml:32` | no |
| 6 | Add `Quest.Studio.Tests` to CI; decide on E2E (A2) | `.github/workflows/ci.yml` | no |
| 7 | Requirements ledger + five invariant checks (C1, C2, C4, C6) | `docs/creator-requirements-ledger.json`, `tools/render_quest_mission_control.py`, new test | no |
| 8 | Mark the Phase-3 lap stale; add `phase3_lap.state` (B7) | runbook, manifest, renderer | no |
| 9 | Reword `cautions[7]` to match `environment[4]` (B8) | `docs/quest-mission-control.json` | no |
| 10 | Add `Arm`/`Disarm`/`GalleryRebuild` to commands (B9) | `docs/quest-mission-control.json` | no |
| 11 | Mark superseded documents (B4) | the four documents listed in B4 | no |
| 12 | Assert the replacement table matched; delete dead entries (A3, A4) | `tools/render_quest_mission_control.py:588-622` | no |
| 13 | Validate `machines` / `environment` state enums (A6) | same file | no |
| 14 | Gitignore or remove `.codex-pdf-profile/`; decide the PDF's status (E1, E4) | `.gitignore` | no |
| 15 | Remove the empty `Lumberjacks/src/` fossil (E2) | — | no |

Audit recommendations do not silently become adopted architectural decisions. See the sign-off
boundary in [`creator-os-build-strategy.md`](creator-os-build-strategy.md).

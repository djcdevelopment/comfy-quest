# Comfy Quest

Comfy Quest is the sovereign Quest product repository extracted from the Baseline
research trunk. It owns the Valheim Quest Lab and Runtime plugins, shared Quest
contracts, Quest Studio, creator tooling, generated tome, and package builders.

The repository communicates with the hosting platform through versioned NuGet
packages and hash-verified release artifacts. It never reads source from a sibling
checkout.

## Start here

Creator OS work has one authority chain, read in this order. It is declared once in
`docs/quest-mission-control.json` under `reading_order` and checked on every render, so a
renamed authority or a stale pointer fails the drift gate rather than misleading the next
reader.

<!-- reading-order:begin -->
1. `docs/PLAN.md` — **The plan.** Goal, where we are, what happens next, and what needs Derek. Read this alone and you know the program.
2. `docs/handoff-2026-08-24.md` — **Session record.** What the 2026-08-24/25 sessions did, the environment traps, and the cold-start checks.
3. `docs/five-intent-program-plan.md` — **Why this exists.** The ethos the whole program is judged against: six product guardrails and one communication guardrail.
4. `docs/creator-os-build-strategy.md` — **The plan.** Lane order, the program invariant, and what each lane may not do.
5. `docs/creator-os-phases.json` — **Lane vocabulary.** The only definition of a lane, and the human boundary.
6. `docs/creator-requirements-ledger.json` — **Requirement dispositions.** Who is accountable for each of the 47 requirements right now.
7. `docs/quest-mission-control.json` — **Work queue.** Every work item with its lane and requirement lineage.
8. `docs/creator-os.md` — **Operating strategy.** Why the loop is shaped this way, and the golden rule.
9. `docs/creator-portfolio-requirements.md` — **What is required.** The 47 FR-/NFR- requirements themselves.
10. `docs/adr/README.md` — **Decisions.** Append-only; a reversal needs a superseding record.
11. `docs/creator-os-audit-2026-08-24.md` — **Findings.** What was wrong on 2026-08-24, with `file:line` receipts.
<!-- reading-order:end -->

The rest of the repository:

- Quest Lab plugin: network/mod/ComfyQuestLab
- Runtime plugin: network/mod/ComfyQuestRuntime
- Shared contract package: network/mod/ComfyQuestContracts
- Studio package: src/Quest.Studio
- Standalone Studio host: src/Quest.Studio.Host
- Generated web tome: docs/generated/questlab.html
- Quest package builders: tools/questlab-package
- NuGet publication runbook: docs/runbooks/NUGET-PUBLICATION.md
- Split-proof release runbook: docs/runbooks/QUEST-RELEASE.md
- OMEN Studio-to-Runtime acceptance: docs/runbooks/I2-QUESTPACK-OMEN.md
- Demo World minimal tutorial: examples/demo-world/first-portal
- Rendered mission-control page: docs/quest-mission-control.html
- R&D opportunity matrix: docs/quest-rd-opportunity-matrix.md
- Working agreements (how this repository expects to be worked on): docs/working-agreements.md
- Retrospectives: docs/retros/
- Repository boundary: BOUNDARY.md
- Extraction record: PROVENANCE.md

## Local verification

The mod build requires the licensed Valheim/BepInEx assemblies from a local game
installation. Do not set ComfyCopyToPlugins during verification.

    dotnet build network/mod/ComfyQuestLab/ComfyQuestLab.csproj -c Release
    dotnet build network/mod/ComfyQuestRuntime/ComfyQuestRuntime.csproj -c Release
    dotnet test network/mod/ComfyQuestLab.Tests/ComfyQuestLab.Tests.csproj -c Release
    python -m unittest discover -s tests
    python tools/component-packets/generate_gallery.py --check
    python tools/component-packets/render_quest_lab.py --check
    python tools/component-packets/generate_seam_catalog.py --check
    python tools/component-packets/check_lab_patches.py
    python tools/quest-studio/build_demo_world_first_portal.py --check
    python tools/render_quest_mission_control.py --check
    python tools/verify_source_intents.py
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/Assert-RepoIdentity.ps1
    python tools/assert_no_reach_in.py
    python tools/assert_no_reach_in.py --self-test
    gitleaks git --no-banner --redact --log-opts='--all' .
    $contractsHash = (Get-FileHash packages-local/Comfy.Quest.Contracts.0.6.0-local.nupkg -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0,16)
    $sdkVersion = (dotnet --version)
    $env:NUGET_PACKAGES = Join-Path $env:TEMP ("comfy-quest-verify-" + $contractsHash + "-" + $sdkVersion)
    dotnet build src/Quest.Studio/Quest.Studio.csproj -c Release
    dotnet test src/Quest.Studio.Tests/Quest.Studio.Tests.csproj -c Release

The interim Contracts package keeps a fixed local version while its bytes evolve.
The package-and-SDK-keyed cache above prevents NuGet from silently compiling Studio
against older bytes from another `0.6.0-local` run. Studio targets .NET 9 and therefore
requires a .NET 9 SDK even when the licensed plugins are built with .NET 8.

Run the sovereign, loopback-only Studio on its own port (the retired Baseline
Workbench may still occupy 8080):

    tools/quest-studio/Start-QuestStudio.ps1

Then open `http://127.0.0.1:8085/quest-studio`. Studio guides creators through
**Author -> Rehearse -> Play -> Observe** without locking the stages. Authoring defaults
to an ordered list of low-friction quest beats: say, shout, drop, pick up, equip,
consume, regain health, or wait. A beat can repeat up to 16 times, optionally inside a
time window. **Browse player actions** adds a searchable, school-filtered view of all 34
creator-safe Grimoire meanings, all backed by fail-closed Runtime adapters and available
for production authoring. The three engine events stay separate from that creator
vocabulary. The 91
low-level assembly seams never become authoring choices.

Open `docs/quest-mission-control.html` directly on a second display for the current
Creator OS lane, dogfood portfolio roadmap, fleet roles, machine-derived choreography,
proof queue, and private session notes. The functional and non-functional adoption gates
live in `docs/creator-portfolio-requirements.md`. Canonical status is tracked in Git;
checkmarks and notes stay in that browser unless explicitly exported. Before a commit,
update its JSON source when the change alters program state, machine roles, the creator
sequence, expected receipts, or the next seat decision, then run the renderer drift
check above.

Studio lowers production beats into bounded acyclic Runtime graphs and certifies them
against the shared contract. **Play this revision** writes an isolated dev artifact;
an explicitly armed Runtime session pulls, validates, activates, and rebinds it.
**Import project JSON** accepts a bounded Studio schema-v3 document and opens it as
a new local fork with fresh project, pack, and experience IDs; it never accepts a
server filesystem path.
**Publish immutable version** remains an independent production action. Contextual targets and event fields appear only when useful. Route IDs,
the full-width graph editor, certified JSON, and event-time adaptive conditions stay
under **Advanced tools**. Adaptive conditions currently expose only persisted time in
the stage and time since structural quest progress; they remain out of the beginner
palette. Runtime
identity and the older single-surface Charm override sit with **Data & history**, next
to the lossless project/history bundle, current questpack, compiled JSON, and
privacy-explicit local usage aggregate. Existing branched quests open without
conversion or data loss. Production activation still requires explicit F10 Check and
F11 Load; the creator loop never places dev revisions in that production inbox.

The Studio workspace is the fast R&D loop: an on-demand local quest library, beat-first
authoring, autosaved drafts, server-generated guided rehearsal, a compact Play cockpit,
and an Observe stage that renders Validation, Transfer, Activation, Rebind, and
Runtime-observed proof from local receipts. Guided
rehearsal derives representative inputs from the saved quest, evaluator-checks the
selected path, and reports untested branches or generation limits. Browser rehearsal
previews logic and effects; it never claims to prove a Valheim adapter or mutation. The
optional local usage toggle stores only fixed selections and broad quantity buckets for
13 weeks on this machine—never titles, messages, targets, searches, identities, exact
timestamps, or uploads. The normal lap is
**Author -> Rehearse -> Play this revision -> play -> Observe**. Runtime's Studio link
carries the active pack, version, requested stage, and current beat, so the creator
returns to the same telling instead of searching for it.
CAST is needed only for a quest without an existing Charm target. Reuse captured multiplayer scenarios for quest-content
changes; run i5 only when the multiplayer event adapter itself changes.

F9 expands or minimizes the always-present Runtime overhead bar. Its compact state keeps
the active title and independent Check, Ready, and Landed signals visible; the expanded
state exposes the Look, Validate, Load, Confirm ladder, contextual action, Studio
handoff, evidence, Charm controls, and machinery details. One clamped alert anchor owns
deadline and actionable warning state. The bar uses a clamped 92-pixel safe top so the
compact state clears the host diagnostic band at the live 1026x740 viewport. Arcane Sight remains a client-local inspection
layer over the locally owned loaded binding set, not a fixed distance radius.

For an install-wide creator lap, use
`tools/creator-session/Invoke-CreatorSession.ps1`. Prepare runs once while Valheim is
closed and owns build, exact backup, deployment, safety, identity pins, and rollback.
After the creator enters the pinned world, Gallery, Runtime, and blueprint operations
travel through bounded expiring request files and correlated receipts; no F5 relay or
cross-machine hand copying is part of the loop. `BuildOn` directly enables the local
private-world no-cost/all-pieces and god-mode states, verifies both, and exposes neither
console commands nor synthetic keys; `BuildOff` reverses both before the world is left.
Capture automatically produces a reviewable Godbuild under `examples/worldbuild/<name>`
and verifies generator drift. Replay stages the exact reviewed capture/blueprint pair,
runs check before build, scopes the live comparison to that blueprint's durable mark, and
fails unless the translation-independent diff receipt says `MATCH`. Ground replay also
refuses to treat an overhead Lab deck as the operator's ground elevation. Captures are
modular source and diff authority for the fields their manifest
supports; the saved `.db`/`.fwl` world remains authoritative for terrain, vegetation,
portal topology, and other excluded world-native state. The executable,
precondition-ordered choreography and its current proof level live in `docs/creator-os.md`.

The **R&D Signal Circuit** template is the current batch probe: normal chat, a durable
wait, shout, two drops inside 30 seconds, pickup, equip, consume, heal, and a small
reward. Its browser rehearsal exercises the same trigger evaluator as Runtime. Live
receipts now report the exact stage and partial count so the next OMEN batch lap can
test the new adapter edges without turning every authoring change into a game session.

Run the local synthetic Studio E2E with the pinned Playwright Chromium build:

    tools/quest-studio/Test-QuestStudioE2E.ps1

The test drives the real loopback browser UI through create, autosave, reload, guided
rehearsal, same-version dev revisions, activation-addressed rollback, independent
immutable publication, advanced-graph preservation, and version iteration. Focused static and browser coverage also exercises the Grimoire
picker boundary, progressive event/effect fields, hidden extraction controls, and
responsive/keyboard semantics. It activates the published bytes through `QuestPackStore` and
writes contract-native synthetic receipts beneath a sentinel-guarded disposable Valheim
root so the Runtime cockpit can be checked through partial progress and completion.
Use `-Headed` to watch the run, `-KeepArtifacts` to retain a successful trace, or
`-SkipBrowserInstall` when the matching browser is already installed. Failed runs retain
their trace, screenshot, DOM, browser errors, host logs, and synthetic filesystem under
`artifacts/quest-studio-e2e/`.

This is local-only synthetic E2E evidence. It does not prove Unity, BepInEx, Harmony
patches, hotkeys, or genuine Valheim events; the OMEN acceptance run remains the live
proof for those adapters.

The installed 4A journey has its own clean-checkout driver:

    tools/quest-studio/Invoke-QuestStudioGuildJourney.ps1

It claims the install through Creator Session, drives guild authoring and immutable
publication, verifies the world metadata and that the pinned character has saved state in that
world, writes one expiring request for the exact profile, world filename, display name, world
UID, machine, and session, and launches Valheim through Steam. Runtime consumes that
request once through Valheim's own profile/world APIs and refuses fallback characters,
different worlds, server joins, console commands, or synthetic input. The driver then owns
replay of a reviewed nearby sign fixture, activation, prerequisite refusal, A -> B -> A,
scoped reset/rerun, exact retention proof, screenshots, LIFO binding recovery, game shutdown,
log collection, and exact install, one-shot world-entry state, world-pair, and character-profile
restore.
`-HumanWorldEntry` retains the ADR 0014 fallback. Installed session
`queue-full-width-journey-20260827-r9` proved the default bounded launch/world-entry and the
complete guild lap with zero human actions, including exact retained evidence and recovery; its
committed index is `docs/evidence/queue-full-width-journey-20260827-r9.json`.
This is a technical 4A integration lap, not a creator seat; Derek's next session begins with
a prepared guild premise and questions about authorship, composition, clarity, and play feel.

Quest's bounded development MCP surface is packaged as a Python-importable provider release,
not hosted by another Quest gateway:

    tools/workbench-provider/New-QuestWorkbenchProvider.ps1

Mount the resulting verified zip read-only into an explicit Isolate profile. Isolate owns gateway
authentication, caller identity, provider loading, and lifecycle; Quest owns only five
fixed-mailbox tools for bounded Runtime status and receipts, Creator-Session-pinned creator/run
controls, and hash-verified ground-only replay of one reviewed Lab Godbuild.
The bounded status result includes the current non-secret Creator Session identity, so a caller
can derive the next control request without reading harness state or asking a human to relay it.
The installed ERA17 slices are indexed in `docs/evidence/isolate-era17-runtime-20260827-r1.json`
and `docs/evidence/isolate-era17-runtime-20260827-r2.json`. They are not the clean-machine
NFR-MCP-001 exit: the image reports no source revision, the registered local credential was not
generated for the lap, and its state still uses checkout-specific mounts. The final Isolate
release must generate credentials, attest its source identity, start from released artifacts
alone, and prove correlated teardown.

The interim packages-local feed exists only until the first public 0.4.0 NuGet
publication and exact consumer repin.

Publication readiness is checked without publishing:

    python tools/nuget/repin_public.py --check-interim
    python tools/release/verify_quest_release.py --self-test

The local release builder emits and verifies the four split-proof Quest assets plus
their manifest and checksums. It requires a clean checkout and never creates a tag
or release:

    powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/New-QuestRelease.ps1

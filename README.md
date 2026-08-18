# Comfy Quest

Comfy Quest is the sovereign Quest product repository extracted from the Baseline
research trunk. It owns the Valheim Quest Lab and Runtime plugins, shared Quest
contracts, Quest Studio, creator tooling, generated tome, and package builders.

The repository communicates with the hosting platform through versioned NuGet
packages and hash-verified release artifacts. It never reads source from a sibling
checkout.

## Start here

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
- R&D opportunity matrix: docs/quest-rd-opportunity-matrix.md
- Repository boundary: BOUNDARY.md
- Extraction record: PROVENANCE.md

## Local verification

The mod build requires the licensed Valheim/BepInEx assemblies from a local game
installation. Do not set ComfyCopyToPlugins during verification.

    dotnet build network/mod/ComfyQuestLab/ComfyQuestLab.csproj -c Release
    dotnet test network/mod/ComfyQuestLab.Tests/ComfyQuestLab.Tests.csproj -c Release
    python -m unittest discover -s tests
    python tools/component-packets/render_quest_lab.py --check
    dotnet build src/Quest.Studio/Quest.Studio.csproj -c Release
    dotnet test src/Quest.Studio.Tests/Quest.Studio.Tests.csproj -c Release

Run the sovereign, loopback-only Studio on its own port (the retired Baseline
Workbench may still occupy 8080):

    tools/quest-studio/Start-QuestStudio.ps1

Then open `http://127.0.0.1:8085/quest-studio`. Studio guides creators through
**Author -> Rehearse -> Publish & Play** without locking the stages. Authoring defaults
to an ordered list of low-friction quest beats: say, shout, drop, pick up, equip,
consume, regain health, or wait. A beat can repeat up to 16 times, optionally inside a
time window. **Browse player actions** adds a searchable, school-filtered view of all 34
creator-safe Grimoire meanings, all backed by fail-closed Runtime adapters and available
for production authoring. The two engine events stay separate from that creator
vocabulary. The 91
low-level assembly seams never become authoring choices.

Studio lowers production beats into bounded acyclic Runtime graphs, certifies them
against the shared contract, and publishes immutable `.questpack` files to the local
Runtime inbox. Contextual targets and event fields appear only when useful. Route IDs,
Charm surface details, the graph editor, and certified JSON stay under **Advanced
tools**. **Data & history** provides a lossless project/history bundle, the current
questpack, compiled JSON, and a privacy-explicit local usage aggregate without placing
export in the normal creator lap. Existing branched quests open without conversion or
data loss. Runtime still requires explicit F10 Check and F11 Load.

The Studio workspace is the fast R&D loop: an on-demand local quest library, beat-first
authoring, autosaved drafts, server-generated guided rehearsal, and a compact Publish &
Play cockpit that turns local receipts into the next manual instruction. Guided
rehearsal derives representative inputs from the saved quest, evaluator-checks the
selected path, and reports untested branches or generation limits. Browser rehearsal
previews logic and effects; it never claims to prove a Valheim adapter or mutation. The
optional local usage toggle stores only fixed selections and broad quantity buckets for
13 weeks on this machine—never titles, messages, targets, searches, identities, exact
timestamps, or uploads. The normal lap is
**Author -> Rehearse -> Publish & Play -> F10 -> F11 -> F9/backtick CHECK/CAST -> one
live event -> inspect receipts**. Reuse captured multiplayer scenarios for quest-content
changes; run i5 only when the multiplayer event adapter itself changes.

The **R&D Signal Circuit** template is the current batch probe: normal chat, a durable
wait, shout, two drops inside 30 seconds, pickup, equip, consume, heal, and a small
reward. Its browser rehearsal exercises the same trigger evaluator as Runtime. Live
receipts now report the exact stage and partial count so the next OMEN batch lap can
test the new adapter edges without turning every authoring change into a game session.

Run the local synthetic Studio E2E with the pinned Playwright Chromium build:

    tools/quest-studio/Test-QuestStudioE2E.ps1

The test drives the real loopback browser UI through create, autosave, reload, guided
rehearsal, certification, immutable questpack publication, advanced-graph preservation,
and version iteration. Focused static and browser coverage also exercises the Grimoire
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

The interim packages-local feed exists only until the first public 0.1.0 NuGet
publication and exact consumer repin.

Publication readiness is checked without publishing:

    python tools/nuget/repin_public.py --check-interim
    python tools/release/verify_quest_release.py --self-test

The local release builder emits and verifies the four split-proof Quest assets plus
their manifest and checksums. It requires a clean checkout and never creates a tag
or release:

    powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/New-QuestRelease.ps1

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

Then open `http://127.0.0.1:8085/quest-studio`. Studio authors bounded acyclic multi-stage
graphs from the Runtime event adapters that actually exist, certifies them against
the shared contract, and publishes immutable `.questpack` files to the local Runtime
inbox. Runtime still requires explicit F10 Check and F11 Load.

The v2 workspace is the fast R&D loop: a local project library, guided acyclic node
canvas, proven action editors, autosaved drafts, read-only certified JSON, deterministic
browser rehearsal, and a Runtime cockpit that turns local receipts into the next manual
instruction. Browser rehearsal previews logic and effects; it never claims to prove a
Valheim adapter or mutation. The normal lap is **Studio -> rehearse -> publish -> F10 ->
F11 -> F9/backtick CHECK/CAST -> one live event -> inspect receipts**. Reuse captured
multiplayer scenarios for quest-content changes; run i5 only when the multiplayer event
adapter itself changes.

The interim packages-local feed exists only until the first public 0.1.0 NuGet
publication and exact consumer repin.

Publication readiness is checked without publishing:

    python tools/nuget/repin_public.py --check-interim
    python tools/release/verify_quest_release.py --self-test

The local release builder emits and verifies the four split-proof Quest assets plus
their manifest and checksums. It requires a clean checkout and never creates a tag
or release:

    powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/New-QuestRelease.ps1

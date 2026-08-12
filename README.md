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

The interim packages-local feed exists only until the first public 0.1.0 NuGet
publication and exact consumer repin.

Publication readiness is checked without publishing:

    python tools/nuget/repin_public.py --check-interim
    python tools/release/verify_quest_release.py --self-test

The local release builder emits and verifies the four split-proof Quest assets plus
their manifest and checksums. It requires a clean checkout and never creates a tag
or release:

    powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/New-QuestRelease.ps1

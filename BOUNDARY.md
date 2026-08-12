# Repository Boundary: comfy-quest

## Purpose

Comfy Quest encapsulates the Quest authoring, learning, packaging, and runtime
vertical. Cross-repository integration uses formal contracts and verified files.

## Owns

- ComfyQuestLab, ComfyQuestRuntime, and their game-independent tests.
- Comfy.Quest.Contracts, including ModGlue source content.
- Comfy.Quest.Studio and the questpack authoring/publishing seam.
- Quest generators, catalogs, bridge, package builders, blueprints, and test clients.
- questlab.html, quest-lab.zip, and quest-picker.zip release assets.

## Does not own

- Companion, Gateway, production hosting, transport contracts, or field-lab
  orchestration; those belong to lumberjacks-platform.
- NetworkSense telemetry, mod deployment, or transport client behavior; those belong
  to networksense.
- Dev MCP; that belongs to isolate.
- Fleet architecture index and historical evidence; those belong to baseline.

## Artifact contracts

| Artifact | Direction | Contract |
| --- | --- | --- |
| Comfy.Quest.Contracts | published | SemVer NuGet package; exact cross-repo pins |
| Comfy.Quest.Studio | published | SemVer NuGet package; exact platform pin |
| questlab.html | published | release manifest, SHA256, and byte count |
| quest-lab.zip | published | release manifest, SHA256, and byte count |
| quest-picker.zip | published | release manifest, SHA256, and byte count |
| .questpack | both | RuntimeContract schema plus content hash |

The repository does not consume Comfy.Transport.Contracts.

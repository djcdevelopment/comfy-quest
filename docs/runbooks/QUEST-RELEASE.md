# Split-proof Quest release

The first extracted Quest release lane is
`quest-v0.2.0-split-proof`. Its public asset set is deliberately small:

- `questlab.html`;
- `quest-lab.zip`;
- `quest-picker.html`;
- `quest-picker.zip`;
- `release-manifest.json`; and
- `SHA256SUMS`.

The two metadata files cover exactly the four downloadable Quest assets. The
manifest also binds the release to a full repository revision and the Quest Lab
package schema, plugin version, release ID, DLL hash, and DLL byte count.

## Build locally

Use the local cutter with the licensed Valheim/BepInEx assemblies available. Start
from a clean checkout of the exact commit intended for the future tag:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\New-QuestRelease.ps1 -ReleaseTag quest-v0.2.0-split-proof
```

The builder regenerates `docs/generated/questlab.html` and runs its drift check,
then refuses to continue if generation changed the committed bytes. It invokes both
existing allowlist packagers, extracts the synthetic standalone picker from its ZIP,
and verifies the complete bundle. It never creates a tag, a GitHub release, or a
cloud build.

The default output is
`artifacts/releases/quest-v0.2.0-split-proof`. Recheck it independently:

```powershell
$revision = git rev-parse HEAD
python tools\release\verify_quest_release.py --release-dir artifacts\releases\quest-v0.2.0-split-proof --expected-tag quest-v0.2.0-split-proof --expected-questlab docs\generated\questlab.html --expected-revision $revision
```

The verifier fails on extra/missing assets, unsafe or duplicate ZIP entries,
hash/size drift, a standalone picker that differs from the ZIP, a generated tome
that differs from the tag, or a Quest Lab manifest/DLL identity mismatch.

## Future publication

Only after reviewing the local bundle:

1. create `quest-v0.2.0-split-proof` at the revision recorded in the manifest;
2. create a GitHub release for that tag;
3. attach exactly the six files listed above, without renaming them; and
4. publish the release.

The published-release workflow checks out the tag, downloads that exact file set,
and repeats the verifier against the tagged generated tome and revision. It has no
.NET setup, licensed assembly input, deployment step, or cloud mod build.

No tag or release is part of the readiness change that introduced this runbook.

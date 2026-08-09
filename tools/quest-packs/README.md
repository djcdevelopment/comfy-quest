# Quest packs

Quest packs are deterministic, data-only bundles for moving a tested Quest Lab experience
between creators. A pack may contain schema-1 quest views, bounded scenario recipes, PlanBuild
blueprints, documentation, screenshots, and machine-readable receipts. It cannot contain code.

## Build

Create a source directory with `quest-pack.source.json` and at least one `quests/*.json`:

```json
{
  "schema": "comfy-quest-pack-source/v1",
  "pack_id": "derek.first-course",
  "name": "Derek's First Course",
  "version": "1.0.0",
  "creator": "Derek",
  "license": "CC-BY-4.0",
  "description": "Eight compact introductions to the Quest Lab schools."
}
```

The allowlisted source directories are `quests`, `scenarios`, `blueprints`, `docs`,
`screenshots`, and `receipts`. Links, special files, unexpected extensions, code, traversal
paths, oversized entries, unsupported creator events, duplicate quest IDs, and non-scalar
`trigger.where` values fail closed.

```powershell
python tools\quest-packs\quest_pack.py build .\my-pack `
  --output .\dist\derek.first-course-1.0.0.questpack
python tools\quest-packs\quest_pack.py inspect .\dist\derek.first-course-1.0.0.questpack
```

The generated `quest-pack.json` records every payload size and SHA-256, the exact canonical
events required by its quests, the capability-catalog hash used to build it, and certifications
derived from included passing Quest Lab receipts. Building the same source twice produces the
same bytes.

## Preview, install, and uninstall

Always preview first:

```powershell
python tools\quest-packs\quest_pack.py install .\dist\derek.first-course-1.0.0.questpack `
  --quest-dir "$env:ProgramFiles(x86)\Steam\steamapps\common\Valheim\BepInEx\config\comfy-quest-lab\quests" `
  --dry-run
```

Remove `--dry-run` to install. Quest files receive pack-prefixed, hash-qualified names in the
flat directory the live loader already reads. The remaining payload and an install receipt live
beside that directory under `quest-packs/<pack-id>/<version>/`. Existing files are never
overwritten, and a conflict changes nothing.

```powershell
python tools\quest-packs\quest_pack.py uninstall derek.first-course `
  --quest-dir "$env:ProgramFiles(x86)\Steam\steamapps\common\Valheim\BepInEx\config\comfy-quest-lab\quests"
```

Uninstall removes only files named by its own receipt and only while their hashes are unchanged.
If a creator edited an installed quest, uninstall refuses and leaves the whole installation for
the creator to reconcile.

This is intentionally local-first. The tool does not upload, download, execute, or submit a
pack, and it does not replace the EventLog-based completion evidence bridge.

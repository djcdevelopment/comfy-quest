# Quest packs

Quest packs are deterministic, data-only handoff bundles for a Quest Lab experience. A pack may
contain schema-1 quest views, bounded scenario recipes, PlanBuild blueprints, documentation,
screenshots, and machine-readable receipts. It cannot contain code, links, or special files.

## Source layout

Create a directory with `quest-pack.source.json` and at least one `quests/*.json`:

```json
{
  "schema": "comfy-quest-pack-source/v1",
  "pack_id": "community.first-course",
  "name": "First Course",
  "version": "1.0.0",
  "creator": "Community Creator",
  "license": "CC-BY-4.0",
  "description": "A compact introduction to Quest Lab."
}
```

The allowlisted source directories are `quests`, `scenarios`, `blueprints`, `docs`,
`screenshots`, and `receipts`. Traversal paths, case-colliding names, links, unexpected
extensions, oversized entries, unsupported events, duplicate quest IDs, and non-scalar
`trigger.where` values fail closed.

## Certify, then publish

Certification requires the .NET 8 SDK because the small `QuestPackContract` host compiles and
calls the shipping `QuestViewLoader`, `QuestTriggerEvaluator`, generated 34-event catalog, and
Quest Lab armed-state probe directly. It is not a parallel Python interpretation of whether a
quest will fire.

```powershell
python tools\quest-packs\quest_pack.py certify .\my-pack `
  --output .\dist\first-course.certification.json

python tools\quest-packs\quest_pack.py publish .\my-pack `
  --output .\dist\first-course-1.0.0.questpack
```

`publish` writes the deterministic pack and, by default,
`<pack>.certification.json`. It also generates
`docs/QUEST-PACK-GETTING-STARTED.md` inside the pack with generic inspect, diagnose, install
preview, install, and uninstall commands. No local checkout path, machine/player name, target,
position, or raw action key is written to the public report or guide.

Release policy can require one or more honest badges before either artifact is written:

```powershell
python tools\quest-packs\quest_pack.py publish .\my-pack `
  --output .\dist\first-course-1.0.0.questpack `
  --require-badge shipping-evaluator-bindable `
  --require-badge all-pack-triggers-live-witnessed
```

The important badges are scoped rather than cumulative marketing claims:

- `shipping-loader-validated` and `shipping-evaluator-bindable` come only from the exact linked
  contract host.
- `all-pack-triggers-contract-witnessed` requires a complete 34-event synthetic-contract receipt
  with exact catalog coverage and evaluator witnesses.
- `all-pack-triggers-live-witnessed` requires every trigger in this pack to overlap a witnessed
  event in an exact passing live receipt.
- `all-schools-live-witnessed` and `same-action-dedupe-live-verified` describe the tested runtime,
  and require the full eight-school matrix, extended profile, coalescing, and zero double
  completions.
- Compatibility Doctor, Pacing Clinic, and Gallery acceptance reports are summarized without
  copying their raw contents. Pacing recommendations remain findings, never a fake pass badge.

A malformed, partial, mislabeled, or merely `verdict: pass` receipt is retained as payload but
marked rejected and earns nothing. Different release identities remain visible beside their
evidence hashes; the tool never invents live evidence or treats a screenshot as an evaluator
witness.

These are reproducible evidence claims, not publisher signatures. A receiver can verify that the
pack, public report, and included receipts agree byte-for-byte; there is not yet a trust registry
or signing authority that proves who ran the game. Raw receipts remain inside the pack only when
the creator deliberately puts them under `receipts/`, and may contain the originating machine
label or action-level details. Review those source files before distributing a pack publicly; the
derived certification report is the privacy-minimal artifact intended for public indexing.

`build` remains available for backward-compatible local drafts. Its manifest says
`uncertified`, and it cannot earn the exact loader/evaluator badges:

```powershell
python tools\quest-packs\quest_pack.py build .\my-pack --output .\scratch.questpack
```

## Inspect and diagnose before installation

Both commands are read-only. `inspect` verifies the archive, every declared size/hash, the quest
payload-to-manifest mapping, certification hashes, and evidence references. `diagnose` explains
catalog drift, unsupported events, changed contract sources, and rejected evidence in a
machine-readable, public-safe report.

```powershell
python tools\quest-packs\quest_pack.py inspect .\dist\first-course-1.0.0.questpack `
  --report .\dist\first-course-1.0.0.questpack.certification.json
python tools\quest-packs\quest_pack.py diagnose .\dist\first-course-1.0.0.questpack
```

## Preview, install, and uninstall

Always preview first; it performs all pack and compatibility checks but creates nothing:

```powershell
python tools\quest-packs\quest_pack.py install .\dist\first-course-1.0.0.questpack `
  --quest-dir PATH_TO_COMFY_QUEST_LAB_QUESTS --dry-run
```

Remove `--dry-run` only when `ready` is true. Quest files receive pack-prefixed,
hash-qualified names in the flat directory the live loader already reads. Other assets and an
install receipt live beside it under `quest-packs/<pack-id>/<version>/`. The exclusive-create
write path never overwrites an existing quest, even if another process races the preview.

```powershell
python tools\quest-packs\quest_pack.py uninstall community.first-course `
  --version 1.0.0 --quest-dir PATH_TO_COMFY_QUEST_LAB_QUESTS
```

Uninstall removes only files named by its own receipt and only while every hash is unchanged. If
a creator edited an installed quest, blueprint, guide, or manifest, it refuses before deleting
anything.

This workflow is local-first. It does not upload, download, execute pack content, open a remote
console, or submit quest completion evidence. A future community registry can distribute these
same bytes without changing their safety or certification semantics.

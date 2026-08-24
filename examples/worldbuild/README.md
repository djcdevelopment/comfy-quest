# Godbuild imports

This directory is generated source for creator-spaced builds captured in a private
Valheim world. Run:

```powershell
python tools/blueprints/import_capture.py <name>.capture.json
```

Each named directory contains the normalized capture authority, its replayable
PlanBuild projection, a typed plan, a deterministic top-down SVG, and a manifest that
names both evidence and unsupported world state. Re-run with `--check` to prove there
is no generator drift.

These are reusable building modules, not complete world releases. Terrain, vegetation,
portal topology, and every other field named as unsupported in a module manifest remain
authoritative in the saved `.db`/`.fwl` world.

The supported loop is capture → inspect → import → review → check → build → diff.
Quest Lab refuses build before check, marks every replayed piece, and accepts the final
proof only when `blueprint_diff` reports `MATCH`.

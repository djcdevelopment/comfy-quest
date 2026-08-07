# Provenance — quest-catalogs recipe refresh

## Origin

Refreshed 2026-08-06 from the public comfy archive repository
(`github.com/djcdevelopment/comfy`) at commit
`4cb188c` ("Build the provenance view, absorb the Etheiry creator-events
tracker, draw the loop").

Byte-exact copies from that commit (modulo git line-ending normalization):

- `harvest.py` — adds structured anomalies (`anom()` objects), the `Provenance`
  recorder + per-adapter column maps, the `creator-events-xlsx` adapter, and the
  `<output>-provenance.json` sidecar emit with a fail-loud catalog cross-check.
- `render_provenance.py` — new: renders the leader-facing provenance view
  (one page per source + `provenance.html` index) from the sidecars.
- `schema.md` — adds the "Provenance sidecar" contract section.
- `validate.py` — adds a warning-only stale-sidecar cross-check.
- `../../data/raw/creator-events-tracker.xlsx` — the Etheiry pilot artifact
  (Creator Events Tracker, Guidelines & Schedule), untouched, from comfy
  `comfy-etheiry-analysis/CreatorStuff/`.

## Merged, not copied

- `sources.json` — comfy's `creator-events-e18` entry added on top of the
  baseline version, which keeps its own QP-1 note on `rangers-example`.

## Deliberately NOT taken from comfy (baseline is ahead here)

- `render_quest_picker.py` and `quest-view-schema.md` — baseline corrected the
  mod config path to `Valheim/BepInEx/config/comfy-network-sense/` after the
  control-surface prune; comfy still says the stale `comfy-control/`. The comfy
  commit did not change these files, so baseline's versions stand.

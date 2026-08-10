# Schema — a player quest view

`quest-view.json` is the per-player dataset: the quests one player chose to track. The
picker page writes it; the player drops it into
`Valheim/BepInEx/config/comfy-network-sense/quest-view.json`; the mod displays it.
(The old `comfy-control/` path from the pruned control-surface era is stale — the live mod
reads `comfy-network-sense/`; a file in the old location fails silently.)

It is deliberately **self-contained**: each entry carries the full quest (plus its guild
and era), so the mod never needs the catalogs, the tracker, or the network. Delete the
file and it never existed.

Top level:
- `schema_version` — number. Currently `1`.
- `player` — object:
  - `name` — text. The Valheim character name this view is for. Informational — the mod
    shows whose view it is; it does not gate on it.
  - `discord` — text or null. Discord username, for the day the review side wants to
    match a submission to the guild tracker. Optional.
- `created_at` — text. ISO timestamp from the picker (browser clock).
- `picker_version` — number. Which generation of the picker page wrote this.
- `quests` — the tracked quests, in the order the player picked them. Each entry is a
  catalog quest (see `schema.md`) **plus**:
  - `guild` — text. Copied down from the catalog's top level.
  - `era` — number. Same.

## Trigger contract (additive in schema 1)

`trigger` remains optional. Existing `kill` objects keep their exact meaning and files
without the newer fields parse unchanged.

- `event` — a stable creator event name. The generated source of truth is
  [`quest-capability-manifest.json`](../../tools/component-packets/samples/quest-capability-manifest.json):
  34 canonical names are currently safe for the shared evaluator. `hit` remains a
  compatibility alias accepting both `damage_dealt` and `resource_damaged`.
- `target` — optional subject substring, case-insensitive; empty or `any` is a wildcard.
  The subject meaning is event-specific: a creature for `kill`, a station for
  `item_crafted` with the current producer, a prefab for piece events, a key for world-key
  events, and so on. The generated capability manifest exposes each event's exact target
  meaning and example instead of asking creators to infer it from a method name.
- `weapon_skill`, `projectile`, and `shots` — the existing kill/hit fields. `shots` is
  retained for file compatibility but no longer changes completion behavior now that a
  durable EventLog row, rather than paired screenshots, is the proof.
- `where` — optional object of event-specific scalar filters. Keys and values compare
  case-insensitively; the value `any` accepts any present value. Strings, JSON numbers,
  and booleans are accepted. Arrays, nested objects, `null`, empty names, and duplicate
  names are rejected instead of silently widening a trigger.

The human-owned field and target descriptions behind that generated contract live in
[`quest-event-authoring.json`](../../tools/component-packets/quest-event-authoring.json).
Quest Lab's shared `QuestAuthoring` helper uses those definitions to turn a witnessed event
into schema-1 JSON, then proves the result by round-tripping the real loader and evaluator.
Volatile measurements such as amount and quantity remain available to deliberate authors
but are omitted from automatic drafts so a single observation does not accidentally become
an over-specific quest.

```json
{
  "event": "station_input_added",
  "target": "CopperOre",
  "where": { "station": "smelter", "item": "CopperOre" }
}
```

The shared evaluator supports the whole safe catalog. A runtime event still needs a
normalized producer and a witnessed integration before the mods may claim it fires in
game; capability classification is not a live receipt.

The mod treats the file as read-only input. Completion proof flows through the durable
EventLog contract; this file only decides what is tracked and which normalized events
can complete it.

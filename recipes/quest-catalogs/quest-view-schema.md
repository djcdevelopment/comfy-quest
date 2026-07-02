# Schema — a player quest view

`quest-view.json` is the per-player dataset: the quests one player chose to track. The
picker page writes it; the player drops it into
`Valheim/BepInEx/config/comfy-control/quest-view.json`; the mod displays it.

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

The mod treats the file as read-only input. Quest submissions still flow through the
existing outbox contract; this file only decides *what is shown*.

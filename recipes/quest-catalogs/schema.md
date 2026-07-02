# Schema — a guild quest catalog

One JSON file describes one guild's quest inventory for one era. Sibling of the rank-ladder
recipe: the ladder says *what a rank requires*, the catalog says *what each quest actually is*.

Top level:
- `schema_version` — number. Currently `1`.
- `guild` — text. The guild's name, e.g. `"Slayers"`.
- `era` — number. Which era these quests are for.
- `source` — object, optional. Where this came from: `{ "kind", "detail", "retrieved" }`
  (e.g. the tracker sheet tab and the date it was pulled). Provenance, not behavior.
- `quests` — the list of quests.

Each entry in `quests`:
- `quest_id` — text. Stable slug, unique within the catalog, e.g. `"air_drop"`. Generated
  from the name; never changes once players reference it.
- `name` — text. The quest's name as the guild writes it, e.g. `"Air Drop"`. Verbatim —
  except a parenthesized annotation the sheet appends on its own line, which moves to:
- `name_note` — text or null, optional. Sheet annotation split off the name, verbatim,
  e.g. `"(new)"` or `"(Ranger Station)"`.
- `reward` — text or null, optional. Per-quest reward as written, e.g. `"1 Queen Bee"`.
  (Rank-level rewards live in the rank ladder, not here.)
- `category` — text. The guild's grouping, e.g. `"General summons"`, `"Rogue summons"`.
  Copied as written — the guild's taxonomy, not ours.
- `coopable` — true/false. Whether a group turn-in credits everyone mentioned.
- `requirements` — text. What you must do, copied **verbatim** including the evidence
  emoji (📸 screenshot, 🎞️ optional video, 🔗 Discord link, 🤜🤛 group turn-in). We
  standardize the plumbing, never the content.
- `evidence` — object, derived from the bot command's slots (the machine-readable truth):
  - `screenshots` — number. How many `image:` slots the turn-in command has (0–4).
  - `video_alternative` — true/false. 🎞️ appears in the requirements text.
  - `link` — true/false. Command has a `summons_url:` slot.
  - `group_turnin` — true/false. Command has a `participants:` slot.
  - `notes` — true/false. Command has a `summons_notes:` or `badge_notes:` slot.
- `evidence_note` — text or null, optional. The guild's free-text description of the
  required evidence (the Ranger tracker's "Required Screenshots" column), verbatim.
- `bot_command` — text. The **verbatim** turn-in command with empty slots, e.g.
  `"/slayer summons summons_type:Air Drop image: image2:"`. Null for auto-checked quests.
- `auto_checked` — true/false. True for meta-quests with no submission (the bot or a
  human checks them off automatically, e.g. "complete all 4 of the above").
- `venue` — text. `"in_game"` or `"irl"`. Defaults to `"in_game"`; exists now so
  Ranger-style real-life badge quests don't force a schema change later.
- `trigger` — object or null. A machine-readable spec for automatic in-game evidence
  capture. Null means manual capture only. Implemented today (mod v0.5.0):
  - `{ "event": "hit", "target": "tree_or_bush" | "tree" | "bush" | "any", "weapon_skill": "Unarmed" }`
    — one shot when the local player damages a matching world object.
  - `{ "event": "kill", "target": "<creature name substring>", "weapon_skill": "...",
    "projectile": true|false, "shots": ["on_first_hit", "on_death"] }` — creature kills
    attributed to the local player's blow; with `shots`, the first matching hit captures
    shot 1 and that creature's death captures shot 2 in one submission (180s to finish,
    60s per-quest cooldown). Without `shots`, a single capture fires on the kill.
  Reserved: damage thresholds, item-stand mounts, emote/position events. No harvester
  emits triggers; they are authored per-quest.

That's the whole shape. If you need a field that isn't here, add it — then teach
`validate.py` and the harvest adapters about it.

## Where catalogs come from

Catalogs are **harvested, not hand-written**. `harvest.py` reads `sources.json` (the
configurator: which guild, which source, which adapter) and emits the catalog plus an
**anomalies report**. Anything odd in the source — duplicate bot commands, mismatched
evidence counts, unparseable rows — lands in the report for the guild to rule on. The
harvester never silently fixes the guild's content.

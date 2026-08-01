# Component packets — extract-grounded modding reference

Turns any component in `assembly_valheim.dll` into a verifiable "extract packet"
(JSON) and, from it, a beginner-facing **field dictionary** (markdown). Built for
the community custom-fields guide: field names, types, inheritance, ZDO usage,
and RPCs come from the game assembly itself, so the docs can be regenerated —
not re-researched — after every game patch.

A packet contains, for one component:

- **Inheritance chain** and interfaces (the OOP lesson — e.g. `Humanoid : Character`,
  where `m_health` and `m_runSpeed` actually live on `Character`)
- **Tunable fields**, flattened across the base-class chain, each tagged with the
  class that declares it (these are what vanilla "custom fields" target)
- **ZDO fields** the component reads/writes — the saved, synced state — resolved
  through the `ZDOVars` hash map back to their string keys
- **Instance RPCs** it registers, and which method registers them
- **Lifecycle methods** it defines (`Awake`, `Update`, …)

## Pipeline (three steps)

```powershell
# 1. Extract a packet (defaults to the Steam install path for the dll)
dotnet run -- "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll" Fireplace

# 2. Draft the description column with any LLM — see annotation-prompt.md —
#    then human-review the rows the model flagged with "(?)".

# 3. Assemble the field dictionary
python assemble_dictionary.py fireplace-packet.json annotations-fireplace.json
```

Requires the host .NET 8 SDK (targets `net8.0` deliberately — no sdk:9.0
container needed) and a licensed local Valheim install to read the dll from.
Analysis is read-only; nothing from the game ships anywhere.

## Confidence labeling

Two provenance levels are mixed in a dictionary and must stay labeled:

- Field names, types, declaring classes, ZDO keys, RPCs: **verified** — read
  mechanically from the assembly by this tool.
- Descriptions: **drafted** — LLM output for a human editor. `(?)` marks the
  model's own low-confidence guesses; they are the review queue.

## Samples

`samples/` holds packets, reviewed-pending annotations, and assembled
dictionaries for `Piece`, `WearNTear`, `Humanoid` (+`Character`), and
`MonsterAI` (+`BaseAI`), plus a packet-only `Fireplace` (the code-level lesson
example). Extracted 2026-08-01; regenerate after game patches.

## Known limits / next steps

- The dll cannot say **which prefabs carry a component** — that composition
  lives in Unity asset data. If "what can I apply this to?" becomes a real
  question, the answer is a small runtime dump of `ZNetScene`'s prefab list on a
  lab server, not more static analysis.
- Related, heavier machinery: `tools/synthetic-baseline-extractor/` does
  netcode-focused whole-assembly RPC analysis (net9, container build). This tool
  stays separate and small on purpose: per-component, host-runnable,
  community-facing.

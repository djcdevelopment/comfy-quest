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

# 1b. Or sweep the whole assembly: every MonoBehaviour-derived component plus
#     global cross-indexes (ZDO key -> readers/writers, RPC name -> registrars)
dotnet run -- "<dll>" --all valheim-component-atlas.json

# 1c. Or ask what one field actually GATES: every read of it, and the block of
#     IL each branch skips. Use this before believing any description of a flag.
dotnet run -- "<dll>" --field WearNTear.m_noSupportWear

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

A drafted description can be confidently, unmarked, and exactly **backwards**.
`WearNTear.m_noSupportWear` was annotated "Disables damage and collapse caused
by lack of structural support". It does the opposite: `UpdateWear` reaches the
support damage only when the flag is `true`, so it is an opt-in wearing a name
that reads like an opt-out. Nothing carried a `(?)`, because the model was not
unsure — it was wrong, and the name agreed with it. That cost three rounds of a
gallery falling down before anyone read the branch.

So: a negated boolean name (`m_no*`) is a coin flip on polarity, and the
description is not a tiebreaker. Run `--field <Type>.<name>` and read the IL
before writing behaviour into a dictionary, or before setting the flag in a mod.

## Samples

`samples/` holds packets, reviewed-pending annotations, and assembled
dictionaries for `Piece`, `WearNTear`, `Humanoid` (+`Character`), and
`MonsterAI` (+`BaseAI`), plus a packet-only `Fireplace` (the code-level lesson
example), and `valheim-component-atlas.json` — the `--all` sweep: 336
components, 194 ZDO keys, 119 RPC names with global cross-indexes, the
LLM-queryable reference the guide draws from. Extracted 2026-08-01; regenerate
after game patches.

## Quest capability projections

`generate_seam_catalog.py` joins the assembly-derived event atlas, reviewed capability
rules, and creator authoring metadata. One run produces the 91-row/90-signature atlas
projection, all 34 creator-safe meanings, the exact 57 creator-safe witness signatures,
the eight quick presets, and a separate fail-closed Runtime production registry.

The production registry currently contains twenty-six canonical events and forty-five exact witnesses.
Local/RPC routes for healing, piece removal, and piece repair remain separate witnesses of one
action, while the container transfer is emitted only after its granted response proves the
container is empty. Its
`automated-contract` evidence state deliberately does not claim live-gameplay proof; a
40-character evidence revision is required before a generator input may say
`verified-live`. `timer_elapsed` and `chat_received` remain explicitly labelled engine
events rather than being folded into the 34-event creator vocabulary.

```powershell
python tools/component-packets/generate_seam_catalog.py --check
```

## Known limits / next steps

- The dll cannot say **which prefabs carry a component** — that composition
  lives in Unity asset data. If "what can I apply this to?" becomes a real
  question, the answer is a small runtime dump of `ZNetScene`'s prefab list on a
  lab server, not more static analysis.
- Related, heavier machinery: `tools/synthetic-baseline-extractor/` does
  netcode-focused whole-assembly RPC analysis (net9, container build). This tool
  stays separate and small on purpose: per-component, host-runnable,
  community-facing.

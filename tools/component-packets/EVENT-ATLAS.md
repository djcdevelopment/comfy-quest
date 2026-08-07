# The event atlas

`samples/valheim-event-atlas.json` — every Valheim method a quest could plausibly
trigger on, extracted from the game assembly, categorised, and joined against what the
Baseline mods actually hook today.

It exists because a quest builder's first hour is otherwise spent guessing. The shipping
mod produces five event types from three hooks on `Character`. `EventType.cs` names 34.
The gap between those numbers is invisible from the outside, and the failure mode is
silence rather than an error.

```bash
dotnet run -c Release -- "<path to assembly_valheim.dll>" --events samples/valheim-event-atlas.json
```

Defaults to the Steam install path if the first argument is omitted. Needs no Valheim
running, no Docker, and no BepInEx — it reads the DLL.

## What the numbers say today

91 seams across 8 categories. All 91 are patchable. **One is usable by a quest today.**

| Category | Seams | Hooked | Quest-usable |
| --- | ---: | ---: | ---: |
| combat | 12 | 3 | **1** |
| inventory | 22 | 0 | 0 |
| harvest | 13 | 0 | 0 |
| crafting | 11 | 0 | 0 |
| progression | 10 | 0 | 0 |
| building | 9 | 0 | 0 |
| world | 9 | 0 | 0 |
| social | 5 | 0 | 0 |

## Reading a verdict

`quest_usable` is the column that matters, and it has four values:

| Value | Means |
| --- | --- |
| `today` | The shipping mod hooks it and the evaluator matches something it produces. Only `Character.OnDeath` qualifies. |
| `produces-event-no-trigger` | The mod hooks it and emits an event, but no quest trigger matches that event. `Character.Damage` and `RPC_Damage` emit `first_hit`, and `QuestTriggerEvaluator` matches `kill` only — so a builder can see the event and cannot fire on it. |
| `lab-candidate` | Patchable, nothing hooks it in a shipping mod. Most of the atlas. The three harvest seams the retired ComfyControlSurface hooked are here too, marked with that mod and `state: retired`. |
| `not-patchable` | No method body to attach a postfix to. None currently. |

## Provenance — two kinds of claim, always labelled

Following the confidence contract in
[`docs/guides/custom-fields/STARTHERE.md`](../../docs/guides/custom-fields/STARTHERE.md):

- **Verified** (`existence_provenance: verified:assembly`) — the type exists, the method
  exists, this is its signature and visibility, it has a body. Read from the DLL. If
  this is wrong, the extractor is wrong.
- **Derived** (`category_provenance: derived:rule-table`) — the category and the
  `quest_usable` verdict. These come from the rule table at the top of `EmitEventAtlas()`
  in `Program.cs`, which is a judgement about what a quest builder would want. Argue with
  it by editing the table, not the JSON.

`known-hooks.json` is a third thing: hand-maintained fact, each row carrying the source
file and line it was read from. **A stale row there is worse than a missing one**, because
the atlas presents it as verified. Update it in the same commit as any patch change.

## Things the extraction found that a human list would have missed

- **`Inventory.AddItem` has seven overloads** and `RemoveItem` four. A hook has to pick
  the right signature; "hook AddItem" is not a specification.
- `MineRock5.RPC_Damage(long, HitData, int)` takes a third argument the other rock types
  do not.
- `TreeLog` is a separate type from `TreeBase` — a felled trunk and a standing tree are
  different hooks, which is why the retired mod patched both.
- `Pickable.Interact(Humanoid, bool, bool)` is the berry-picking seam, and it is a
  `bool`-returning interact rather than a damage event.
- `WorldGenerator.GetBiome` has two overloads; `ZoneSystem.SetGlobalKey` has three.

## Names that do not exist

Worth recording, because they are the ones people reach for:

| Reached for | Actual |
| --- | --- |
| `OnHit` | `Character.Damage` / `Character.RPC_Damage` |
| `LastHit` | `m_lastHit` is an internal field; last-hit attribution is the mod's own 15 s window |
| `KillingBlow` | The mod's event name, produced from `Character.OnDeath` |
| `EnergySpent` | `Player.UseStamina(float)` |
| `CurrentSkillLevel` | `Skills.GetSkillLevel(SkillType)`; the *event* seam is `RaiseSkill(SkillType, float)` |

## Keeping it honest across game updates

`diff_atlas.py` compares a committed atlas against a fresh sweep. Run it after a Valheim
patch: a seam that disappears is a hook that will silently stop firing, which is exactly
the class of breakage this file exists to catch early.

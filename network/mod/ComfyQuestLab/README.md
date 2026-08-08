# ComfyQuestLab

**A private-world lab for learning what Valheim can trigger a quest on.**

Install it on your own world, hit something, and watch the game tell you what it just
did — and whether a quest could actually fire on it.

> **Scaffold.** The live view, the spellbook and the quest lane all work, and all eight
> schools are wired — 26 atlas integrations plus two panel/input support hooks; 25 atlas
> integrations apply by default (`Player.UseStamina` is config-gated). `lab_setup` raises
> the practice gallery and writes a starter quest file; it is the one command a newcomer
> needs.
>
> **Verified in game** (2026-08-07, one session). A seam fires and the live view reports
> it, which is the whole claim:
>
> ```
> 03:50:03  striking a standing tree
>   Beech1 (tree)   skill Unarmed
>   -> nothing binds a quest to this yet
> ```
>
> Plain name, resolved target, skill, and the verdict — a builder learns in one glance
> that the game sees the hit *and* that no quest can be bound to it. The gallery also
> builds (620 pieces), the ground-to-plaza portal pair connects, and the structure stands
> rather than decaying. The monuments read as their glyphs, each lit in its school's
> colour.
>
> **Combat and the quest lane verified in game 2026-08-08.** All four combat seams fired in
> one fight — `OnDeath`, `Damage`, `RPC_Damage`, `Stagger` — and the seeded quest completed
> on the kill: `quest fired: First Blood`, twice, with the roster reporting `fired 2 times`
> and a live cooldown. The naming fix is visible and correct: the console shows
> `$enemy_greyling` with no prefab name beside it, because that token already contains
> `Greyling` — the "stay quiet when they agree" case.
>
> **Still unwitnessed:** inventory, building, crafting, progression, world, social. Patched
> and reported hooked, but no event from them has been seen. Item stands stay bare
> (`SetVisualItem` is a registered RPC, not a callable method; the gear is dropped beside
> them instead). This README describes what is here, not what is planned.

## Why this exists

Authoring a quest today means guessing, and guessing fails *silently*.

The shipping mod produces five event types from three hooks. The contract names 34. And
the quest loader still accepts `trigger.event = "hit"` with `target = "tree_or_bush"` —
vocabulary the evaluator stopped matching, so a bush quest produces no error and no
event. Nothing in the game tells you any of this.

The lab makes it visible. Every row it shows carries the honest answer to *can I build on
this?*

## Try it

| | |
| --- | --- |
| `lab_setup` | raise the practice gallery and write you a starter quest file. Do this first. |
| **F6** | open the console (or `questlab_panel`) |
| `lab_reload` | re-read your quest files and say what changed |
| `lab_target [school]` | put a fresh practice target in front of you (default: combat) |
| `questlab_help` | what this build can do |
| `questlab_seams` | which seams hooked on your game version, and which didn't |
| `questlab_clear` | empty the console |

`lab_setup` is typed into **Valheim's** console, which is **F5**. **F6** opens the lab's own
panel. Two different keys, and mixing them up is the most common first stumble.

The **Spellbook** tab is a page per rune: what that school covers, something to go and
try, and the trap. Every page lists what the world answers to in that school and marks
which ones this build will show you — so you can tell "Valheim can do this" from "the lab
will show it to you".

Each category has a rune, and **the same rune is its filter in the live view**. Learn the
book and you have learned the console. Turning to a page also lights that rune in the
console, because the next thing anyone does after reading "punch a tree" is punch a
tree.

Open the live view and punch a tree. You should see:

```
[rune]  14:22:07  striking a standing tree
        Beech1 (tree)   skill Unarmed
        -> nothing binds a quest to this yet
```

That third line is the point of the whole tool.

Note what is *not* there: no method name, no talk of hooks. **You do not need to know how
a spell works in order to cast one.** The method name is the thing's *true name* and it
is one toggle away in the Spellbook — because knowing a true name is what lets you
command a thing, which here means writing code against it.

## Reading a row

Every event ends in one of three verdicts:

| Verdict | Means |
| --- | --- |
| **a quest can be bound to this today** | Exactly one thing qualifies right now: a creature dying. |
| **the world speaks, but no quest is listening yet** | It really happens and you can watch it — striking a living thing does — but nothing will carry it into a quest. |
| **nothing binds a quest to this yet** | Most of the game. |

The full picture—91 atlas rows, 90 exact signatures, and 77 method IDs:
[`tools/component-packets/EVENT-ATLAS.md`](../../../tools/component-packets/EVENT-ATLAS.md).
Its generated capability manifest classifies every exact signature and names 34 stable
creator-event candidates. Those are classifications, not runtime promises: the shared
evaluator accepts all 34 (plus the schema-1 `hit` alias), while this scaffold's quest
engine still forwards only `kill` until normalized, witnessed integrations land.

## Writing a quest

`lab_setup` writes `BepInEx/config/comfy-quest-lab/quests/starter.json` — but only into an
empty folder, so it never overwrites your work. Edit it, run `lab_reload`, and the **Quests**
tab tells you what changed and what will fire.

Each file is a **whole `quest-view.json`**, not a fragment. That is deliberate: any file in
that folder can be copied byte-for-byte to `BepInEx/config/comfy-network-sense/quest-view.json`
and the shipping mod accepts it unchanged. Several files sit side by side, and one that will
not parse costs only its own quests.

The starter file holds two quests that disagree with each other on purpose:

| | |
| --- | --- |
| `first_blood` — `kill` / `Greyling` | **armed.** Kill the Greyling under the combat monument. |
| `punchwood` — `hit` / `tree_or_bush` | **not armed**, and nothing errors. |

**You never have to go hunting.** `lab_target` puts a fresh practice target in front of you —
`lab_target harvest` for a tree, `lab_target crafting` for a smelter, and so on for all eight
schools. The gallery's stations are placed once; this is how you get another one after you've
killed, chopped or otherwise consumed the first.

That second one is the current runtime lesson. The shared evaluator understands every safe
catalog event and retains `hit` as a compatibility alias, but the lab engine still forwards
only witnessed kills. A `hit` quest therefore parses cleanly and is contract-bindable without
yet firing in game. All eight schools are *hooked*; exactly one currently reaches the quest
engine. The Quests tab names which, and why.

The tab also shows **the last kill the matcher was given** — creature, skill, melee or ranged
— which is what turns "why didn't it fire" from a guess into a read.

### The name a quest matches on is not the prefab name

The matcher compares against the creature's `m_name`, a localization token. For `Neck` the
token contains the prefab name and typing the obvious thing works. For `Greydwarf_Elite` the
token is `$enemy_greydwarfbrute` — they share nothing, so that quest never fires and never
errors. The console now shows both names whenever they disagree, and the Quests tab says so
outright.

## Config — `[Quests]`

| Key | Default | |
| --- | --- | --- |
| `questsEnabled` | `true` | OFF still loads and shows your files; nothing fires. Useful to prove a firing is what you think it is. |
| `questCooldownSeconds` | `60` | Matches the shipping mod's default, so what you see is what a player sees. Drop it to `0` while authoring. |

`lab_reload` always clears cooldowns outright — a deliberate divergence from the shipping mod,
where they persist for the session. Waiting a minute to retest an edit is exactly the flow
`lab_reload` exists to protect.

## Filters are the feature

A single fight emits more rows than the window holds, and all eight categories are live.
The console opens on **combat + harvest** only; every other category is one click. There is a text match and a pause button,
and pausing drops nothing — the ring keeps collecting behind it.

## What it will not do

- **It never changes the game.** Every hook is a postfix that reads and records. If a
  postfix throws it is swallowed, because a patch that throws takes Valheim's damage path
  with it.
- **It mostly reports what *you* did.** Anything driven by a hit is filtered to the local
  player, because a world of creatures fighting each other buries the console within
  seconds of a fight starting. Five seams are deliberately *not* filtered, because they
  are about the world rather than about you: `Character.OnDeath` and `Stagger` (the
  target is the subject), `WearNTear.Destroy` (a structure breaking matters whoever broke
  it), `ZoneSystem.SetGlobalKey` (world state, and interesting precisely because someone
  else may have caused it), and `Sign.SetText`.
- **It does nothing on a dedicated server.** Detected in `Awake`, before any patch is
  applied.
- **It is not the shipping mod.** Uninstall it and nothing about your game changes.

## Config — `[Lab]`

| Key | Default | |
| --- | --- | --- |
| `enabled` | `true` | Master switch. OFF drops every observation. |
| `panelShortcut` | `F6` | F7 is taken by the retired control surface. `None` = console commands only. |
| `consoleRows` | `18` | Rows on screen; the ring holds 8× that so you can scroll back. |
| `verboseLogging` | `false` | Also write every event to the BepInEx log. Noisy in combat, good for pasting into a thread. |
| `observeStamina` | `false` | `Player.UseStamina` fires on nearly every action including running. Turn it on to see the shape, then off again. **Needs a restart** — it decides whether the patch applies at all. |

Hot-reloadable except `observeStamina` — every other read is live, so `Config.Reload()`
lands on the next frame.

## For the next person to work on it

**The quest contract is linked, not copied.** `TrackedQuest`, `QuestViewLoader` and
`QuestTriggerEvaluator` compile from ComfyNetworkSense's own files (see the csproj), so a
quest behaves identically in both. If you ever need an adapter between them, the contract
has drifted and the fix is upstream.

**Armed state is not a predicate — it is the evaluator.** `LabQuestSet.ProbeArmed` dry-fires
a throwaway `QuestTriggerEvaluator` with a quest's own filters echoed back at it. A mirror
predicate restating the matcher's rules is the thing that drifts silently, and "fires here
means fires there" is the only promise the lab makes. If the evaluator gains a lane, this
answer changes with it for free.

**`LabQuestSet`, `LabQuestAdvisor` and `LabQuestSeed` are Unity-free and linked into
ComfyNetworkSense.Tests**, so the logic a creator depends on is provable in seconds without a
game install. `LabQuestSet.Build` takes file *contents*, not paths, for exactly that reason —
keep disk IO in `LabQuestEngine`. `LabQuestAdvisor` takes world facts as injected delegates
so every advisory has a test and none of them guess during `Awake`, before `ZNetScene` exists.

**`LabCreatureNaming` holds a deliberate copy** of
`GameplayEventProducer.NormalizeCreatureName`. It is Unity-free and linked into the test project
on purpose: this rule has been wrong once already, and while it lived beside `Character` the only
way to check it was to launch the game and kill something. The Greydwarf_Elite case is a test now,
and a mutation check confirms reintroducing the old behaviour fails three of them.

Still owed: extracting that rule into a file **both mods** link, so the lab cannot drift from the
producer at all. That is a change to the shipping mod and carries its own note.

**Adding a category** is the shape of
[`Patches/HarvestPatches.cs`](Patches/HarvestPatches.cs): one `LabPatching.TryPatch` per
seam, a postfix that only describes, no gameplay consequence. Argument types are explicit
because overloads are the norm — `Inventory.AddItem` has seven.

**`LabPatching.TryPatch` never throws.** The lab reaches into far more of the game than
the shipping mod, so it is far more exposed to a game update. A seam that vanishes shows
up in `questlab_seams` as unavailable instead of taking the mod down.

**The keystroke guard is load-bearing.** `InputGuard` is ported from the camera proof
kit, plus an inverse it never needed. Without it, typing `bush` into the filter walks the
player forward, swings whatever is equipped, and closes the panel on the `s`.

**Known gap:** `GameplayEventTypes` is not linked — it shares a file with a 353-line
Unity-dependent class. Extracting it to its own file in the shipping mod is a small
change that owes a roadmap note.

## Build

```powershell
dotnet build .\ComfyQuestLab.csproj -c Release -p:PluginOutputPath=C:\__comfy_lab_no_plugin_copy__
```

Two guards stop the build touching your live plugins folder: the path must exist **and**
`ComfyCopyToPlugins=true`. Both are shut by default, because an unguarded auto-copy once
replaced a pinned DLL mid-programme.

## Licence

BUSL-1.1 with the community-steward safe harbor, converting to AGPL-3.0-only. See
[LICENSING.md](../../../docs/legal/LICENSING.md).

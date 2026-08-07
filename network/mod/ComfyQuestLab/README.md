# ComfyQuestLab

**A private-world lab for learning what Valheim can trigger a quest on.**

Install it on your own world, hit something, and watch the game tell you what it just
did — and whether a quest could actually fire on it.

> **Scaffold.** The live view and the spellbook both work, and all eight schools are
> wired — 28 seams, 27 of which apply by default (`Player.UseStamina` is config-gated).
> There is no `lab_setup` yet and no download.
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
> **Not verified in game:** only the harvest category has been observed firing; the other
> seven are patched but unwitnessed. Item stands stay bare (`SetVisualItem` is a registered
> RPC, not a callable method; the gear is dropped beside them instead). This README
> describes what is here, not what is planned.

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
| **F6** | open the console (or `questlab_panel`) |
| `questlab_help` | what this build can do |
| `questlab_seams` | which seams hooked on your game version, and which didn't |
| `questlab_clear` | empty the console |

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

The full picture, with all 91 seams:
[`tools/component-packets/EVENT-ATLAS.md`](../../../tools/component-packets/EVENT-ATLAS.md).

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

# Changelog

### Unreleased

**Scaffold.** The project, the console, all eight hook categories, the journal, and the
three tools that keep them honest. Nothing is published and there is no download.

- New mod `ComfyQuestLab`, separate from ComfyNetworkSense on purpose: it hooks far more
  of the game and draws an overlay, and neither of those belongs in a mod that runs
  against the live server. Client-only — `Awake` detects a dedicated server and returns
  before applying a single patch.
- **Quest contract linked, not copied.** `TrackedQuest`, `QuestViewLoader` and
  `QuestTriggerEvaluator` compile from ComfyNetworkSense's own source files, the same way
  ComfyNetworkSense.Tests already links them. A quest that behaves one way in the lab
  behaves the same way in the shipping mod because both compile the same bytes.
- **All eight categories wired**, 28 seams:
  - **harvest** — `TreeBase.Damage`, `TreeLog.Damage`, `Destructible.Damage`,
    `Pickable.Interact`. The first three are what the retired ComfyControlSurface hooked,
    and the reason punching a bush used to fire a quest and no longer does. `TreeLog` is
    separate from `TreeBase` because a felled trunk is a different type — exactly the
    distinction a hand-written hook list gets wrong. `Pickable.Interact` is berry
    picking, which is an interact and not a damage event.
  - **combat** — `Character.Damage`, `RPC_Damage`, `OnDeath`, `Stagger`. Both damage
    paths, because client-owned melee and server-routed damage are different ownership
    routes and hooking one silently loses half your hits. The kill comes from `OnDeath`
    rather than `IsDead()` at a damage postfix, which is still false there.
  - **inventory** — `Humanoid.Pickup`, `EquipItem`, `ConsumeItem`, `Container.TakeAll`.
    `Inventory.AddItem` is deliberately left alone: seven overloads, and it fires on
    every internal shuffle, so a quest built on it would fire constantly and mean nothing.
  - **progression** — `Skills.RaiseSkill`, `Player.OnDeath`, and `Player.UseStamina`
    behind its own flag because it fires on nearly every action including running.
  - **building** — `Player.PlacePiece`, `RemovePiece`, `Repair`, `WearNTear.Destroy`.
    The player verbs come off `Player` rather than `Piece`: a piece being placed is a
    consequence, the player placing it is the act. Destroy is not player-filtered, since
    a structure breaking matters whoever broke it.
  - **crafting** — `InventoryGui.DoCrafting`, `Smelter.OnAddOre`, `OnAddFuel`. Note where
    the craft lives: on the *UI* class, not on `Player` or `CraftingStation`. Nobody
    guesses that, and a builder looking for "Player.Craft" would conclude crafting cannot
    be hooked at all.
  - **world** — `Player.TeleportTo`, `ZoneSystem.SetGlobalKey(string)`. Global keys are
    how Valheim remembers a boss is dead; it is the closest thing to a server-wide
    progression event and nothing has ever hooked it. Only the string overload is taken —
    the two enum overloads route into it, so hooking all three would triple-count.
  - **social** — `Chat.SendText`, `Sign.SetText`. The quiet one with real potential: a
    quest that completes on writing a sign needs nothing from combat, and community
    rituals look more like that than like killing things. `Talker.Say` is deliberately
    skipped — it is the broadcast that results, so taking both double-counts your own
    messages.
- **`LabSeamCatalog.g.cs`, generated from the atlas.** A patch names its seam and the
  catalog answers category and usability. Without it every patch file would restate a
  verdict the atlas already knows and the two would drift the first time a hook landed.
  Regenerate with `tools/component-packets/generate_seam_catalog.py`.
- **`check_lab_patches.py` — the guard that matters.** Harmony resolves
  `AccessTools.Method` at runtime, so a wrong argument list does not fail the build; it
  returns null and the patch silently never applies. The checker verifies all 28 targets
  against the atlas headless, and it is a real guard: it rejects wrong arg lists, missing
  parameters, typo'd names, and overloads that do not exist.
- **Runes, one per category, doing double duty.** The same mark is the spellbook tab and
  the console's filter toggle, so learning the book teaches the console for free. Drawn
  procedurally from line segments — runes ARE line segments — so there are no PNGs to
  ship, no atlas to keep in sync with a game update, and no font that might be missing
  the glyph. Elder Futhark, chosen because the meanings are apt rather than decorative:
  Jera is literally the harvest rune, Fehu is property, Othala the homestead, Tiwaz the
  battle rune. Social uses Mannaz (community) rather than Ansuz (speech), which was the
  better meaning, because Ansuz and Fehu are the same shape at a different arm angle and
  at eighteen pixels that is no shape at all. Legible beats faithful.
- **You do not need to know how a spell works to cast one.** A student building quests
  should not have to learn what a Harmony postfix is, so the tome speaks in plain terms:
  *striking a standing tree*, *feeding ore to a smelter*, *the world recording something,
  as when a boss falls*. All 77 things the world answers to are named this way, and the
  live view uses the same words, so a row is recognisable without reading a method
  signature.
  The method name is the **true name**, one toggle away. That is not decoration — knowing
  a thing's true name is what gives you power over it, and here the true name is literally
  what you would write code against. Available, never in the way.
- **Spellbook**, one page per rune: what it covers, something to go and do, and the
  thing that would otherwise cost an hour. Generated by
  `tools/component-packets/generate_journal.py`, which joins hand-written prose
  (`journal-pages.json`) with the atlas, so a page cannot promise a seam the extractor
  does not see. Each page lists every seam the game has in that category and marks the
  ones this build actually shows you — the difference between "the game can do this" and
  "the lab will show you" is invisible in a list of method names and decides what someone
  chooses to build. Picking a page also enables that category in the console, because the
  next thing anyone does after reading "punch a tree" is punch a tree.
- **Live event console** (F6). Per-category filters defaulting to combat + harvest,
  because one fight emits more rows than the window holds and an unreadable first
  impression teaches nothing. Text match, pause that drops nothing, and a bounded ring
  holding 8× the visible rows so you can scroll back over what just happened.
- **Every row carries a usability verdict** — whether a quest can fire on it today, emits
  an event no trigger matches, or is lab-only. That column is the reason the tool exists.
- `InputGuard`, ported from the camera proof kit plus an inverse it never needed. Without
  it, typing in the filter field walks the player forward, swings a weapon, and closes
  the panel. Uses `global::Console` because `System.Console` shadows Valheim's.
- `LabPatching.TryPatch` records every patch outcome instead of throwing, so a seam that
  moves in a game update shows up in `questlab_seams` as unavailable rather than taking
  the mod down on load.
- Console: `questlab_help`, `questlab_panel`, `questlab_seams`, `questlab_clear`.
- Panel anchored away from the top-left corner, which ComfyNetworkSense's debug HUD owns.

- **The gallery, laid out** (`Core/LabGalleryPlan.g.cs` — planned, not yet built in
  game). A student should get value two minutes after downloading, not after an hour of
  hunting Greylings and crafting a bow, so the Tome brings its own ground: eight rune
  monuments on a 38 m ring, a practice station on each pad, and an armoury at the centre
  carrying a bow *and* arrows for it.
  The monuments are raised from logs, cut from the **same** segment table that draws the
  14-pixel glyphs — one shape at two scales, so a monument cannot differ from the page it
  belongs to. All eight were rebuilt face-on from the beam data alone to prove the shape
  survives being chopped into 2 m lengths. It does.
  On a raised platform rather than bare terrain, because Valheim ground is not flat and
  89 beams on a hillside is how this reads as broken rather than impressive. A plaza,
  eight spokes and eight pads — 499 tiles rather than the ~1,100 a solid disc would need,
  and shaped so the floor itself tells you where to walk. Every station and all 89 beam
  footings verified to land on a tile.

**Not here yet:** the journal (a stub that shows the seam roster); `lab_setup`; and
JSONL persistence. `GameplayEventTypes` is
not linked — it shares a file with a 353-line Unity-dependent class and needs extracting
first.

**Verified in game 2026-08-07.** The gallery builds and *stands*: 620 pieces, platform
35 m up, the portal pair connecting ground to plaza. Getting there took three rounds
against one root cause. `WearNTear.m_noSupportWear` and `m_noRoofWear` are opt-**ins** —
`UpdateWear` reaches each damage path only when its flag is `true` — so setting them true,
which is what the field names and the atlas annotation both suggested, armed exactly the
decay they were meant to prevent. `tools/component-packets --field` exists now so the next
flag gets read rather than guessed.

Two other things that session: the ground portal was sampled with `GetSolidHeight` *after*
the floor was laid, so it found the deck it had just built and landed on the roof beside
its own partner. And `clear` trusted remembered ZDOIDs, which are session-scoped — after a
reload it destroyed two unrelated objects and the local player. Pieces now carry a mark in
their own ZDO and nothing unmarked is ever destroyed.

**The first hook fired the same session.** Punching a beech produced, in the live view:

```
03:50:03  striking a standing tree
  Beech1 (tree)   skill Unarmed
  -> nothing binds a quest to this yet
```

Every layer at once — the harvest seam patched and firing, the plain name from the
spellbook table, the target resolved to a prefab, the skill, the ring buffer holding, and
the verdict that is the entire reason the lab exists. Nothing about that line needs
explaining to a quest builder.

**The monuments read as runes, and they are lit.** Two more things the first real look
found. The beams were being oriented on an assumption — that a wood beam runs along its
local Z — and 89 of them standing end-on to the glyphs they were drawing looked, in
Derek's words, like the dots in a connect-the-dots book. The builder now measures the
prefab's own mesh, picks whichever local axis it is longest on, and swings *that* onto
each stroke, correcting for a pivot that is not at the mesh's middle. It prints what it
measured, so the next person can check rather than trust.

Each monument then carries a coloured lamp, one per school. Valheim has no field for
this — `LightFlicker` and `LightLod` only modulate and cull a light that already exists,
and just three fields in the assembly hold a `Light` at all — so the lamp is a
`UnityEngine.Light` the lab hangs itself, which makes it client-side and unsaved, and
means it needs the same re-application on zone reload that the wear flags needed.
Intensity, range, and on/off are config; colour is per school.

**Clear now sweeps by mark, not by manifest.** The manifest only ever described the most
recent build — every build empties it first — so a second gallery orphaned the first
beyond any reach, and the pieces accumulated to 1527 before anyone noticed a clear
reporting zero. `WearNTear.GetAllInstances()` enumerates every loaded piece, so the sweep
finds all lab-marked galleries in any session. It only sees loaded zones, and says so.

**Still not verified in game:** only harvest has been witnessed. The other seven
categories are patched but no event from them has been seen. Item stands stay bare;
`SetVisualItem` is a registered RPC rather than a callable method, so the gear is dropped
beside them.

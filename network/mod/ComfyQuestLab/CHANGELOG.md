# Changelog

### 0.2.0 — 2026-08-08

**Creator-event expansion.** The initial lab is now a release-cut, self-service package:
all classified integration routes, the shared generic evaluator, Gallery v2, and bounded
machine-readable suites ship together. This supersedes the narrow 0.1.0 package.

- New mod `ComfyQuestLab`, separate from ComfyNetworkSense on purpose: it hooks far more
  of the game and draws an overlay, and neither of those belongs in a mod that runs
  against the live server. Client-only — `Awake` detects a dedicated server and returns
  before applying a single patch.
- **Quest contract linked, not copied.** `TrackedQuest`, `QuestViewLoader` and
  `QuestTriggerEvaluator`, plus the new `QuestEvent` and generated `QuestEventCatalog`,
  compile from ComfyNetworkSense's own source files, the same way ComfyNetworkSense.Tests
  already links them. A quest that behaves one way in the lab behaves the same way in the
  shipping mod because both compile the same bytes.
- **The shared evaluator is no longer kill-shaped.** All 34 creator-safe canonical events
  use one `OnEvent` path with the existing `OnCreatureKilled` API retained as a wrapper.
  Schema 1 gains optional scalar `trigger.where` filters without invalidating an existing
  file. The published `hit` verb remains a broad alias for creature or resource damage.
  Caller-supplied action identity dedupes local/RPC witnesses independently of quest
  cooldown, including at the lab's zero-cooldown authoring setting.
- **Atlas expansion complete in code.** All 86 practical exact signatures now have explicit
  runtime patches, including all 57 creator-safe signatures. A central canonical router applies
  core/extended/diagnostic profiles, emits stable event names, and coalesces local/RPC or overload
  witnesses before quest evaluation. Diagnostic-only witnesses are structurally non-bindable;
  four query/cheat signatures remain deliberately disabled. Headless and assembly-build receipts
  are complete. An exact-r4 OMEN run witnessed 8/8 schools and completed 8/8 ordinary example
  quests with zero same-action doubles; the final r8 cut still owes its exact-release re-witness.
- **Gallery v2 is profile-driven and reversible.** The generated `classic`, `marble-wide`,
  and `marble-grand` plans retain the proven geometry as a baseline while adding solid
  black-marble floors, 8–10 m halls, larger rings, and larger runes. Runtime commands can
  list, check, build, compare, identify, selectively clear, and rebuild profiles. Every
  object carries plan/profile/build marks; comparison sides share a build id, and clear
  sweeps the locally known ZDO table so portals and loose supplies are covered alongside
  structure pieces without ever touching an unmarked object. Uninstantiated marked ZDOs are
  explicitly claimed before destruction; the r3 live run exposed that Valheim otherwise
  ignores the delete while returning normally. Generated JSON/count/preview artifacts
  and drift tests agree. Live comparison selected `marble-grand` as the direction; r5
  raised it 3 m over the highest sampled terrain. Its screenshot then caught the vanilla
  one-metre sign wrapping whole school names vertically. r6 generates one sign per letter
  in a centred horizontal row and gives each word one school-coloured light. The corrected
  headers remain subject to the final visual pass. r8 keeps the selected 10 m halls and
  monumental runes while shortening each grand hub-to-station walk from 37 m to 9 m and
  reducing the marked build from 3,671 to 1,349 objects. Its portal-side birch/axe/food,
  player-side bow/arrows, building hammer/wood, smelter coal, and hub `sign here` sign turn
  the court into a short self-explaining course rather than a supply hunt.
- **Gallery sites are reusable.** r7 makes console and batch clear asynchronous and safe:
  when the local player is standing on the selected raised floor, the command finds the
  terrain-only height at the same X/Z, completes and verifies Valheim's own replicated
  teleport, and only then removes marked objects. `rebuild` uses the same lifecycle. A
  refused or incomplete terrain return leaves the gallery standing instead of dropping the
  player, so repeated visual batches no longer require fresh ground or a manual portal exit.
  The first exact-r7 live clear returned to terrain and removed all 5,860 accumulated pieces,
  then exposed that the request receipt sampled the ZDO table before Valheim retired queued
  destroys. r8 waits a bounded five seconds for that retirement and only passes the lifecycle
  when no matching marked object remains. Its first exact live reset cleared 3,668 marked
  objects and built the requested 1,349-object course, but Unity retained the retired marble
  colliders beyond their ZDOs: solid-height sampling stacked the new floor 18.1 m above the
  player and suspended the nominal ground portal, causing a fatal fall. r9 samples Valheim
  terrain—not transient solids—for both the whole floor and ground portal, and gives cleared
  GameObjects two quiescence frames before rebuilding. Derek's exact-r9 review then found the
  course readable enough to complete but caught the remaining physical sequence: its Birch,
  axe, and food were already upstairs, the hub sign sat at floor level, and natural trees still
  crossed the deck. r10 keeps the world reversible rather than deleting those trees: the selected
  deck rises 32 m above the highest sampled terrain (past the measured 30.5 m Meadows beech),
  while a ground welcome camp places the Birch/axe before the portal and mounts three foods on
  real item stands at a picnic table. The Social sign now stands on a two-metre post with a
  persistent school-coloured lamp. The current generated build is 1,353 marked objects.
- **The panel is an interactive tool, not a translucent overlay.** r6 uses a nearly opaque
  high-contrast surface, larger default dimensions, a bounded lower-right resize handle,
  and a spreadsheet-style live-event grid with stable columns and explicit BINDABLE /
  DIAGNOSTIC verdicts. Opening it now acquires cursor/input ownership, resets held buttons,
  blocks camera/player actions, and restores the previous cursor state on F6, Escape, the
  Close button, disable, or plugin teardown. It retains native IMGUI and adds no Jötunn
  dependency. r8 adds visible − / + whole-panel zoom from 65–200%, persisted in config, so
  creators can tune windowed, 1080p, and 4K layouts without editing a file.
- **Bounded batch evidence is self-service.** `questlab_batch` can prepare, run, reset,
  report, and export two explicit suites. `all-schools` installs eight ordinary bindable
  example quests, safely clears every marked old build, raises a fresh compact course with
  targets and supplies staged at point of use, and receipts real router witnesses
  separately from quest completions. `creator-events` probes all 34 safe events through the
  shared evaluator and labels the result synthetic. The live lane temporarily uses zero
  cooldown in memory so local/RPC double completion cannot hide behind normal cooldown, then
  restores config through reload. An expiring fixed-file i5 request mailbox exposes ten
  allowlisted suite/gallery operations—no console, keystrokes, arbitrary paths, or prefab
  strings—and writes request/suite JSON plus relevant log evidence.
- **The initial eight-category scaffold wired** 26 atlas integrations plus two panel/input support hooks:
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
- **Exact capability contract generated from the atlas plus policy.** The 91 atlas rows
  normalize to 90 exact signatures and 77 method IDs. `quest-capability-rules.json`
  classifies every method into a stable creator event, route, runtime profile, actor
  boundary, and dedupe group; `quest-capability-manifest.json` expands that policy back
  across every overload. `LabSeamCatalog.g.cs` compiles the same joined data into the
  mod while keeping legacy method-ID lookups compatible. Generation and `--check` fail
  on a missing or stale classification.
- **`check_lab_patches.py` — the guard that matters.** Harmony resolves
  `AccessTools.Method` at runtime, so a wrong argument list does not fail the build; it
  returns null and the patch silently never applies. The checker verifies 57/57 safe and
  86/86 practical exact-signature integrations against the 90-signature atlas, reports
  the four intentionally disabled signatures, and keeps the two lab support hooks separate.
  Mutation coverage proves a missing safe patch turns the guard red.
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

**`lab_setup` landed** — one command that raises the practice gallery and points at the
tome, so a newcomer needs no other instruction. It is a front door onto
`questlab_gallery build` rather than a separate mechanism.

**Not here yet:** the journal (a stub that shows the seam roster); and JSONL persistence.
`GameplayEventTypes` is not linked — it shares a file with a 353-line Unity-dependent
class and needs extracting first.

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
reporting zero. The locally known ZDO table lets the sweep find all lab-marked galleries
without depending on instantiated `WearNTear` components; local worlds know the whole table,
while a remote client can only answer for objects it has synchronized.

**Still unwitnessed at that point:** only harvest. Combat and the quest lane were witnessed
on 2026-08-08 — see the end of this file. Six categories remained unseen. That initial cut
left item stands bare because `SetVisualItem` is a registered RPC rather than a callable
method, so its gear was dropped beside them; r10's welcome table later mirrors the verified
vanilla ZDO state and invokes that exact RPC to mount its three foods.

**The lab can read a quest now — and say when it never could.**

The csproj had linked `TrackedQuest`, `QuestViewLoader` and `QuestTriggerEvaluator` from the
beginning so the contract would compile identically. Nothing called them. The lab could show
what the game said and had no idea what a quest was.

- **`LabQuestSet`** folds `QuestViewLoader.Parse` over each file in
  `BepInEx/config/comfy-quest-lab/quests/*.json` **independently**. `Parse`, never `Load`:
  `Load` keeps one slot of static state and clears the whole tracked set on any exception, so
  three good drafts plus one typo'd draft would leave a creator with zero quests and one
  message. Per-file parsing means a bad file loses only itself.
- **Armed state is the evaluator, dry-fired**, not a predicate restating its rules. A quest's
  own filters are echoed back at a throwaway zero-cooldown `QuestTriggerEvaluator`;
  `CreatureMatches(filter, filter)` is a substring of itself and `ranged: true` satisfies both
  states of `TriggerProjectile`, so an empty result means no kill can *ever* fire that quest.
  Zero duplicated matching logic and nothing to drift. When a quest is not armed, one ablation
  — the same quest with the verb forced to `kill` — decides whether the verb was the only
  obstacle, so the panel can name the creator's actual trigger instead of shrugging.
- **The verdict is the point, not a caveat.** At this stage the quest engine forwarded only
  `kill`, so of eight hooked schools exactly one reached a quest. A `hit` trigger parsed
  cleanly, errored nowhere, and could not fire in game. The **Quests tab** said which, and
  why, per quest. The later generic evaluator keeps that current runtime verdict separate
  from contract capability until each producer is wired and witnessed.
- **`lab_reload`** re-reads and **diffs by name** — `+ first_blood`, `~ punchwood (trigger
  changed)`, `= 3 unchanged`. "Reloaded" alone never tells a creator the file they just saved
  is the file the lab just read. It also builds a fresh evaluator, dropping cooldowns: a
  deliberate divergence from the shipping mod's session-long 60 s, because waiting a minute to
  retest an edit is precisely the flow `lab_reload` exists to protect.
- **The seed is a lesson, not a template.** `lab_setup` writes `starter.json` — only into an
  empty folder, never overwriting — holding `first_blood` (armed) beside `punchwood` (`hit`,
  silently unfireable). A test parses that exact string through the real contract and asserts
  both verdicts, so it goes red the day either side moves. Reading about the trap in a README
  teaches far less than opening the file it happens in.
- **`LabQuestAdvisor`** takes the world as injected facts rather than looking it up, so every
  advisory has a test and none of them guess during `Awake` before `ZNetScene` exists. It
  catches a mistyped `weapon_skill` with the nearest real one, a target in no catalog,
  `projectile: true` on a melee-only skill (Spears excluded — a thrown spear is genuinely
  ranged), a duplicate `quest_id`, and `shots`, which carry no behaviour.
- Contract messages are passed through **untouched**, with the lab's remedy appended as a
  separate sentence. Rewording them is how the lab would start lying about the shipping mod.
- The regex parser can silently yield fewer quests than were written, so parsed count is
  compared against `"quest_id"` occurrences — the one check that notices a trailing comma.

**A real bug, found while wiring it.** `CombatPatches.Describe` returned the GameObject name
and the comment above it promised `QuestTriggerEvaluator` "does a case-insensitive substring
against exactly this". It does not: the shipping mod hands the evaluator the creature's
`m_name`, a localization token. `Neck` and `Boar` survived by luck; `Greydwarf_Elite` against
`$enemy_greydwarfbrute` shares nothing, so a builder who typed what the console showed them
got a quest that parsed, errored nowhere, and could never fire. The console now shows the
matchable name and adds the prefab name beside it **only when they disagree**, and the advisor
names the real string. `LabKillWatch` mirrors the producer's rule with the source line cited.

- **`LabKillWatch`** is the lab's own last-hit window (15 s, 256 entries, matching the
  producer's constants). `Character.OnDeath` carries no `HitData` and `IsDead()` is still false
  in a damage postfix, so the three strings the evaluator needs can only be recorded at hit
  time and consumed at death. Attribution is implicit and load-bearing: entries are only ever
  written when the local player landed the hit, so no entry means no evaluation, and the
  unfiltered `OnDeath` row stays unfiltered.
- Console: `lab_reload` added; `lab_setup` now seeds before raising the gallery.
- Config `[Quests]`: `questsEnabled`, and `questCooldownSeconds` wired to `SettingChanged` so
  it retunes the live evaluator — the second reactive knob in the mod, following the rune lamps.
- **Stale claims corrected.** `questlab_seams` and the class doc both still said seven of the
  eight categories were unwired. All eight are hooked; what differs is whether a quest can be
  *bound*, which is now what they say.

231 tests pass, 28 of them new.

**Verified in game 2026-08-08 — combat, and the whole quest lane.**

Two firsts in one session. Every combat seam fired in a single fight — `Character.OnDeath`,
`Damage`, `RPC_Damage` and `Stagger`, all four rows against `$enemy_greyling` — so combat
joins harvest as a witnessed category and the console's three different verdicts appeared
side by side, which is the lesson that was previously only describable.

And the quest lane ran end to end: `2 quests loaded (1 armed)` at startup, then
`quest fired: First Blood` twice, with the Quests tab reporting `fired 2 times since the
last reload · re-arms in 22s` and Punchwood dimmed beneath it carrying the verb explanation.
Seed → parse → armed probe → hit attribution → `OnDeath` → the real evaluator → credit,
proven in the game rather than only against the contract.

The naming fix showed correct in the same shot: the console printed `$enemy_greyling` with
**no** prefab name beside it, because that token already contains `Greyling`. The quiet case
is as important as the loud one — showing both names unconditionally would have been noise.

**And the session found a bug in the diagnostic itself.** The "last kill" line read
`$enemy_greyling · Axes · melee → matched nothing` for a kill that matched perfectly well —
the quest was simply still cooling down, as the roster two lines above was saying at that
exact moment. `QuestTriggerEvaluator.OnCreatureKilled` returns an empty list both when no
quest wants a kill and when one wants it but is on cooldown, and collapsing those into
"matched nothing" sends a creator off to edit a target that was never wrong. That is the
wrong-place-to-look failure this entire tool exists to prevent, sitting in the line built to
prevent it. It now distinguishes them and names the quest and the seconds remaining, deciding
"would this have matched?" by dry-firing the real matcher on a throwaway zero-cooldown
evaluator so the answer cannot drift.

Independent corroboration worth recording: ComfyNetworkSense was running alongside and
completed its own `greyling_cull` quest off the same kill, from its own quest-view.json. Two
mods, one linked contract, same verdict — which is the promise the source-link exists to make.

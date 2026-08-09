# ComfyQuestLab

**A private-world lab for learning what Valheim can trigger a quest on.**

Install it on your own world, hit something, and watch the game tell you what it just
did — and whether a quest could actually fire on it.

> **Expansion build.** The live view, spellbook, and shared quest lane cover all eight
> schools. The runtime explicitly patches all 86 practical atlas signatures plus two
> panel/input support hooks. All 57 creator-safe signatures normalize to 34 stable events;
> the remaining practical witnesses are diagnostic-only, and four query/cheat signatures
> are deliberately disabled. `Player.UseStamina` remains separately config-gated.
>
> **Verified in game** (2026-08-07, one session). A seam fires and the live view reports
> it, which is the whole claim:
>
> ```
> 03:50:03  resource_damaged
>   Beech1 (tree)   skill Unarmed
>   -> a quest can be bound to this today
> ```
>
> Stable event name, resolved target, skill, and verdict — a builder learns in one glance
> that the game sees the hit and which quest vocabulary binds to it. The original gallery
> build completed (620 pieces), its ground-to-plaza portal pair connected, and the structure
> stood rather than decaying. Live comparison then selected the raised `marble-grand` court;
> its marked-object clear/rebuild lifecycle has returned a player safely to terrain and
> removed 5,860 accumulated objects without requiring a new patch of ground.
>
> **Combat and the quest lane verified in game 2026-08-08.** All four combat seams fired in
> one fight — `OnDeath`, `Damage`, `RPC_Damage`, `Stagger` — and the seeded quest completed
> on the kill: `quest fired: First Blood`, twice, with the roster reporting `fired 2 times`
> and a live cooldown. The naming fix is visible and correct: the console shows
> `$enemy_greyling` with no prefab name beside it, because that token already contains
> `Greyling` — the "stay quiet when they agree" case.
>
> **Live receipt boundary:** an exact-r4 OMEN run passed all eight schools — 8/8 canonical
> events witnessed, 8/8 ordinary example quests completed, 12 local/RPC witnesses coalesced,
> and zero same-action double completions. The synthetic shared-contract suite also passed
> 34/34 creator events. r22 adds the shared Creator Foundry contract after r21 changed the
> selected court's roof and tree-ledger serializer, so those same suites must be re-witnessed
> against the exact r22 DLL before
> the release cut is final. This README
> distinguishes that remaining exact-release check from the already witnessed runtime claim.

## Why this exists

Authoring a quest today means guessing, and guessing fails *silently*.

The shipping mod produces five event types from three hooks. The contract names 34. And
the quest loader still accepts `trigger.event = "hit"` with `target = "tree_or_bush"` —
vocabulary the evaluator stopped matching, so a bush quest produces no error and no
event. Nothing in the game tells you any of this.

The lab makes it visible. Every row it shows carries the honest answer to *can I build on
this?*

## Try it

### Install once

1. Close Valheim. Install BepInEx if it is not already present.
2. Copy `ComfyQuestLab.dll` from the zip to `Valheim/BepInEx/plugins/`.
3. Optionally copy `djcdevelopment.valheim.comfyquestlab.cfg` to
   `Valheim/BepInEx/config/`. It contains the reviewed defaults; if you omit it, the mod
   creates the same settings on first launch.
4. Start Valheim and load a **private/local world**. This lab is not for a shared server.

Updating is the same operation with Valheim closed: replace the DLL. Quest files under
`BepInEx/config/comfy-quest-lab/quests/` are separate and are never overwritten by an
update.

| | |
| --- | --- |
| `lab_setup` | write the starter quest, safely clear marked old builds, and raise one fresh compact practice course on the same site. Do this first. |
| **F6** | open the console (or `questlab_panel`) |
| `lab_reload` | re-read your quest files and say what changed |
| `lab_target [school]` | put a fresh practice target in front of you (default: combat) |
| `questlab_help` | what this build can do |
| `questlab_seams` | which seams hooked on your game version, and which didn't |
| `questlab_profile [core\|extended\|diagnostic]` | select stable-event breadth or inspect raw witnesses |
| `questlab_clear` | empty the console |
| `questlab_gallery profiles` | list the generated Gallery v2 geometry choices and counts |
| `questlab_gallery check [profile]` | resolve every prefab without placing anything |
| `questlab_gallery build [profile]` | raise one marked profile; default is `marble-grand` |
| `questlab_gallery compare [left] [right]` | raise two profiles side by side under one build id |
| `questlab_gallery identify` | report profile/build/role marks, live roof coverage, and tree-ledger state before changing the world |
| `questlab_gallery evidence [profile-or-build-id]` | export read-only Gallery Truth: bounds, weather exposure, fresh-prefab comparisons, fixture assertions, and named camera views |
| `questlab_gallery clear [profile-or-build-id]` | return safely to terrain, remove only matching marked gallery objects, then restore matching ledgered trees |
| `questlab_gallery rebuild [profile]` | safely clear and rebuild one profile at the same reusable site |
| `questlab_gallery trees` | report pending/restored natural-tree recovery ledgers |
| `questlab_gallery restore-trees [profile-or-build-id]` | restore a pending ledger only after matching gallery pieces are gone |
| `questlab_prefabs inspect <exact-name>` | compare startup prefab, current prefab, and loaded-instance renderer state; export material, emission, property-block, GI, and light evidence as JSON |
| `questlab_batch suites` | list the two bounded evidence classes |
| `questlab_batch prepare all-schools` | write eight ordinary example quests, safely reset the marked site, and raise one fresh compact course with targets and supplies at point of use |
| `questlab_batch run [all-schools\|creator-events]` | start live witnessing or run the explicitly synthetic 34-event contract probe |
| `questlab_batch reset\|report\|export` | reset safely, show progress, or write a machine-readable receipt |

`lab_setup` is typed into **Valheim's** console, which is **F5**. **F6** opens the lab's own
panel. Two different keys, and mixing them up is the most common first stumble.

Opening the panel explicitly hands mouse and player input to the Lab: the cursor unlocks,
camera look and gameplay clicks stop, and the previous cursor state is restored on **F6**,
**Escape**, or the visible **Close** button. The high-contrast window opens at 900×620 and
the lower-right handle resizes it within the current screen. Position and size are saved when
the panel closes. The visible **− / +** controls zoom the whole panel from 65–200% in 10%
steps; click the percentage to return to 100%. A windowed/1080p creator and a 4K creator can
tune the same grid independently.

The **Quests** tab is a compact dashboard rather than a prose dump. Its fixed columns show
the colored school rune, quest name, creator event and target, armed state, and fire count.
Use the row's **down chevron** only when you need its source file, evaluator explanation,
cooldown, or advisories; the up chevron collapses it and load errors are grouped separately.

Hovering any clipped grid cell writes its full meaning into the help bar at the bottom of the
window. Blue bindable-event cells are buttons: click one to copy the exact `trigger.event` ID
instead of transcribing it. The Quests tab opens the local quest folder directly; its visible
path can also be clicked to copy it. **Pause** freezes the retained moment while keeping search
and school filters live over that snapshot; **Resume** returns to the live stream. **All** and
**Default** recover the eight-school or quiet two-school filter presets. Clearing the search and
clearing the retained log are separate, explicitly labelled actions.

The **Spellbook** tab is a page per rune: what that school covers, something to go and
try, and the trap. Its world-action grid gives each integration one row with a colored
`BINDABLE`, `DIAGNOSTIC`, or `NOT IN BUILD` verdict; exact Valheim method names remain a
toggle. You can tell "Valheim can do this" from "the lab will show it to you" without
reading a repeated prose block.

Each category has a rune, and **the same rune is its filter in the live view**. Learn the
book and you have learned the console. Turning to a page also lights that rune in the
console, because the next thing anyone does after reading "punch a tree" is punch a
tree.

Open the live view and punch a tree. You should see:

```text
TIME      SCHOOL    CREATOR EVENT       TARGET / DETAIL                QUEST USE
14:22:07  Harvest   resource damaged    Beech1 (tree) · skill Unarmed  BINDABLE
```

That third line is the point of the whole tool.

Note what is *not* there: no method name, no talk of hooks. **You do not need to know how
a spell works in order to cast one.** The method name is the thing's *true name* and it
is one toggle away in the Spellbook — because knowing a true name is what lets you
command a thing, which here means writing code against it.

## Reading a row

Every event ends in one of two creator-relevant verdicts:

| Verdict | Means |
| --- | --- |
| **BINDABLE** | The witness normalized to one of the 34 safe canonical events and entered the shared evaluator. |
| **DIAGNOSTIC** | Useful raw evidence under the diagnostic profile, but structurally barred from quest evaluation. |

The full picture—91 atlas rows, 90 exact signatures, and 77 method IDs:
[`tools/component-packets/EVENT-ATLAS.md`](../../../tools/component-packets/EVENT-ATLAS.md).
Its generated capability manifest classifies every exact signature and names 34 stable
creator events. The shared evaluator accepts all 34 (plus the schema-1 `hit` alias), and
the lab routes every creator-safe signature through that same evaluator. The patch guard
fails if any of the 57 safe or 86 practical signatures loses runtime coverage.

## Gallery v2 profiles

Gallery geometry is generated from the same eight rune definitions used by the panel.
`classic` keeps the prior mixed-material shape as a comparison baseline. `marble-wide`
has a solid black-marble floor, 8 m halls, larger runes, 1.5 m terrain clearance, and
about 2,291 placed objects. The selected `marble-grand` direction is the default: it keeps
10 m halls, monumental runes, and horizontal school-name headers built from individually
readable glowing letters, but compresses each hub-to-station walk from 37 m to 9 m. Its
1,912 marked objects fit within a 48 m footprint.

The grand floor now sits 6 m above the highest sampled terrain, below the altitude where the
r17 live comparison showed Valheim coating every upward-facing slab and station in snow. A
generated 550-slab black-marble canopy copies the hub, hall, and station-pad floor cells at a
16 m ceiling height. Valheim's own roof check treats those non-leaky piece colliders as real
cover; the 17 m rune stages stay open to the sky. One real ceiling brazier hangs at the hub and
one halfway down each spoke. Each fixture's measured upper mesh extent attaches just below the
16 m roof underside, leaving its roughly 1.945 m body visibly hanging below instead of trusting
the prefab pivot or burying the body in the slab. Their narrowly marked vanilla
fireplaces stay fuelled across zone reloads, without changing any ordinary brazier in the world.
`questlab_gallery identify` also measures loaded roof and fixture meshes in world space and
reports whether every visible fixture body is actually below the slab; the role count alone is
not treated as visual-placement evidence.

`questlab_gallery evidence [profile-or-build-id]` is the repeatable Truth Lens pass. It writes
`comfy-questlab-gallery-truth/v1` under
`BepInEx/config/comfy-quest-lab/receipts/truth/` without moving the player, camera, clock,
weather, or a single world object. Each standing profile/build records renderer-derived world
bounds; live `RoofCheck` coverage; ceiling-brazier clearance from the overlapping roof
underface; and up to two live-vs-fresh render-configuration samples per role/prefab group.
Those comparisons include material illumination, snow/wet/rain surface signals, and configured
light deltas.

The same artifact plans deterministic `overview-north`, `overview-east`, `overhead`,
`arrival-eye`, and—when a roof is loaded—`roof-underside` views for the camera proof lane. It
can prove an intersection, exposure, or material/light delta. It cannot prove that a frame
looks good: visible snow and final appearance remain human visual judgments. Verify a collected
artifact with `python tools/component-packets/verify_questlab_truth.py <artifact.json>`.

The default is a course rather than an empty monument. Before the ascent portal, a ground
welcome camp puts a Birch beside its bronze axe and serves cooked meat, Queens Jam, and bread
on real horizontal item stands atop a picnic table. Each display carries a generated, prefab-
checked fallback list because not every food has Valheim's required `attach` child in every game
build. Combat leaves a bow and 100 wood arrows
on the player's side with its Greyling at the rune. Building pairs its hammer and wood,
Crafting puts coal directly in front of the smelter, and Social raises its illuminated
`sign here` sign on a two-metre post in the hub. Every consumable is recreated by `lab_setup`
or batch `prepare`, so a creator never needs prior inventory or a scavenger hunt.

Before placing anything, the grand profile scans only loaded `TreeBase` roots whose trunks
intersect the generated platform cells plus a bounded 12 m crown margin (the committed
Meadows Beech survey is 21.5 m across). It records exact
prefab, position, native Euler rotation, quaternion, scale, and any non-default health in a
JSON write-ahead ledger. The ledger carries an expected record count and SHA-256 digest and is
read back through the source-shared data-contract serializer before the first world mutation; an absent or partial
tree collection aborts the gallery build. It then retires those roots
directly—no damage, drops, or player statistics. The welcome Birch is placed afterwards and
remains a marked gallery object. An ordinary `clear` restores matching pending ledgers only
after every selected gallery mark is gone; setup/rebuild preserve the ledger while refreshing
the course. `trees` reports recovery state, and `restore-trees` refuses to run through a
standing matching gallery.

Every object carries the plan version, profile id, and build id in its own ZDO. `identify`
reads those durable marks from the locally known ZDO table; `clear` accepts either a profile or
one comparison build id and refuses to touch anything unmarked. A comparison gives both
sides one shared build id, so it can come down in one bounded operation. If the local player
is standing on the selected raised floor, `clear` first uses Valheim's replicated teleport to
return them to the natural terrain at the same X/Z; deletion starts only after that target is
verified. Once deletion settles, ordinary clear restores any selected tree ledger. `rebuild`
inherits the safe movement/deletion lifecycle but intentionally keeps the ledger active while
the replacement occupies the site, allowing one patch of ground to be reused.
`lab_setup` and `questlab_batch prepare all-schools` apply that lifecycle to selector `all`
before every build, so old comparisons, spent targets, and abandoned drops cannot leak into
the next test.
Generated counts
and previews live in
[`gallery-profiles.json`](../../../tools/component-packets/samples/gallery-profiles.json)
and `gallery-plan-comparison.png`; `generate_gallery.py --check` guards plan drift.

## Bounded suites and receipts

`all-schools` prepares one schema-1, source-compatible example quest per school and safely
rebuilds the compact course with fresh targets and interaction-local supplies. A run
clears router/evaluator state, uses a volatile zero cooldown without changing the creator's
config, and waits for real game actions. The receipt requires both the canonical event and
its example quest completion in every school. It records raw signatures, canonical action
keys, coalesced local/RPC witnesses, and fails closed if the same action completes one quest
twice. Passing runs export automatically; `report` and `export` also preserve incomplete work.

`creator-events` is deliberately different: it exercises all 34 safe event names through
the exact source-shared evaluator and labels its receipt `synthetic-contract`. It proves
bindability, not that a Valheim player performed those actions. Receipts are ordinary JSON
under `BepInEx/config/comfy-quest-lab/receipts/`.

For an unattended i5 lane, [`Invoke-I5QuestLabBatch.ps1`](../../../tools/i5/Invoke-I5QuestLabBatch.ps1)
deploys one expiring request through the SHA-verified config lane and collects its request,
suite, Gallery Truth, and relevant-log receipts. Its eleven operations, two suites, and three gallery profiles
are fixed allowlists. The request schema has no console text, key, path, or prefab field.

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
| `punchwood` — `hit` / `tree_or_bush` | **armed.** Punch the harvest target; `hit` remains a broad compatibility alias. |

**You never have to go hunting.** `lab_target` puts a fresh practice target in front of you —
`lab_target harvest` for a tree, `lab_target crafting` for a smelter, and so on for all eight
schools. The gallery's stations are placed once; this is how you get another one after you've
killed, chopped or otherwise consumed the first.

The shared evaluator understands every safe catalog event and retains `hit` as a compatibility
alias for `damage_dealt` and `resource_damaged`. All eight schools reach the quest engine through
the same canonical router. Alternative local/RPC and overload witnesses share a bounded action
key, so even a zero-cooldown quest cannot complete twice for one action.

### Creator Foundry contract

Every canonical event now carries shared creator metadata in `QuestEventCatalog`: what its target
actually means, one honest example target, whether weapon skill/projectile context is meaningful,
and each event-specific scalar field accepted by `trigger.where`. The human-owned source is
[`quest-event-authoring.json`](../../../tools/component-packets/quest-event-authoring.json); the
generator refuses a missing or invented event and publishes the same rows into the capability
manifest and both mod assemblies.

`QuestAuthoring.FromEvent` is the Unity-free foundation for a future panel action. It turns one
witnessed `QuestEvent` into a complete schema-1 view, feeds that JSON through the exact shipping
`QuestViewLoader`, and requires the exact `QuestTriggerEvaluator` to match the original witness
before returning it. Stable identity fields such as station/item are retained; volatile amount
and quantity values stay discoverable but are not copied into a draft by default. Existing quest
files are unchanged.

`QuestAuthoring.ValidateDraft` and `Diagnose` return structured parse/match diagnostics. A miss is
explained by asking the evaluator counterfactual questions with one constraint at a time; there is
no second parser or matcher. The metadata is deliberately honest about current producer limits:
chat/sign text is redacted and cannot be filtered, while `item_crafted` currently identifies the
crafting subject rather than the crafted item. r22 does not yet add a panel editor or write files;
it supplies the drift-guarded contract those surfaces can safely consume next.

The tab also shows **the last event the matcher was given** — canonical name, target, and context
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

- **Passive observation never changes the game.** Every integration hook is a postfix that
  reads and records; if one throws it is swallowed, because a patch that throws takes
  Valheim's own path with it. Gallery and suite preparation are the explicit exceptions:
  they change a private world only after a typed command or validated bounded request, mark
  everything they own, and expose selective cleanup.
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
| `panelScale` | `1` | Whole-panel zoom, 0.65–2.00. The in-panel − / + buttons change and persist it in 10% steps. |
| `panelX`, `panelY` | `80`, `90` | Saved logical screen position. Updated on close and clamped after resolution/zoom changes. |
| `panelWidth`, `panelHeight` | `900`, `620` | Saved logical window size. Updated on close; the drag handle remains bounded to the current screen. |
| `consoleRows` | `18` | Rows on screen; the ring holds 8× that so you can scroll back. |
| `galleryPiecesPerFrame` | `24` | Gallery objects placed per frame, clamped to 1–200. Lower it on a slower client. |
| `blueprintPiecesPerFrame` | `12` | Blueprint objects placed per frame, clamped to 1–200. |
| `verboseLogging` | `false` | Also write every event to the BepInEx log. Noisy in combat, good for pasting into a thread. |
| `eventProfile` | `extended` | `core` is low-noise; `extended` adds safe high-frequency events; `diagnostic` also shows raw non-bindable witnesses. Hot-reloadable. |
| `observeStamina` | `false` | `Player.UseStamina` fires on nearly every action including running. Turn it on to see the shape, then off again. **Needs a restart** — it decides whether the patch applies at all. |

Hot-reloadable except `observeStamina` — every other read is live, so `Config.Reload()`
lands on the next frame.

## Config — `[Gallery]`

| Key | Default | |
| --- | --- | --- |
| `runeLights` | `true` | Hang a client-only coloured light on each rune monument. |
| `runeLightIntensity` | `3` | Brightness tuned for the black-marble backdrop. |
| `runeLightRange` | `11` | Light reach in metres. |

All three gallery light settings retune the standing monuments without a rebuild.

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

**`LabQuestSet`, `LabQuestAdvisor`, `LabQuestSeed`, `QuestAuthoring`, and `LabBatchContract` are Unity-free and linked into
ComfyNetworkSense.Tests**, so the logic a creator depends on is provable in seconds without a
game install. `LabQuestSet.Build` takes file *contents*, not paths, for exactly that reason —
keep disk IO in `LabQuestEngine`. `LabQuestAdvisor` takes world facts as injected delegates
so every advisory has a test and none of them guess during `Awake`, before `ZNetScene` exists.
`LabBatchController` is the deliberately thin runtime half; remote operations must remain in
`LabBatchRequestPolicy`, whose closed allowlist is headlessly tested.

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

**Panel input is an ownership lifecycle.** `InputGuard` snapshots the cursor, unlocks and
maintains it while the panel is interactive, blocks the local player's gameplay input,
resets held buttons at both transitions, and restores the prior state on close. This is
the native IMGUI/Harmony equivalent of a GUI manager's block/unblock pair, without adding
a Jötunn dependency to the package.

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

SourceLink is deliberately disabled for this directly distributed DLL. Its repository URL
includes the containing commit, which made a documentation-only landing alter the PE debug
checksum and therefore the package SHA. Deterministic path mapping remains enabled, and a
guard requires the release setting so unchanged plugin source rebuilds to unchanged bytes.

## Licence

BUSL-1.1 with the community-steward safe harbor, converting to AGPL-3.0-only. See
[LICENSING.md](../../../docs/legal/LICENSING.md).

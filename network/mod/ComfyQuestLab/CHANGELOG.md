# Changelog

### Unreleased

**Scaffold.** The project, the console, four of eight hook categories, and the two
tools that keep them honest. Nothing is published and there is no download.

- New mod `ComfyQuestLab`, separate from ComfyNetworkSense on purpose: it hooks far more
  of the game and draws an overlay, and neither of those belongs in a mod that runs
  against the live server. Client-only — `Awake` detects a dedicated server and returns
  before applying a single patch.
- **Quest contract linked, not copied.** `TrackedQuest`, `QuestViewLoader` and
  `QuestTriggerEvaluator` compile from ComfyNetworkSense's own source files, the same way
  ComfyNetworkSense.Tests already links them. A quest that behaves one way in the lab
  behaves the same way in the shipping mod because both compile the same bytes.
- **Four of eight categories wired**, 17 seams:
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
- **`LabSeamCatalog.g.cs`, generated from the atlas.** A patch names its seam and the
  catalog answers category and usability. Without it every patch file would restate a
  verdict the atlas already knows and the two would drift the first time a hook landed.
  Regenerate with `tools/component-packets/generate_seam_catalog.py`.
- **`check_lab_patches.py` — the guard that matters.** Harmony resolves
  `AccessTools.Method` at runtime, so a wrong argument list does not fail the build; it
  returns null and the patch silently never applies. The checker verifies all 17 targets
  against the atlas headless, and it is a real guard: it rejects wrong arg lists, missing
  parameters, typo'd names, and overloads that do not exist.
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

**Not here yet:** building, crafting, world and social categories; the journal (a stub
that shows the seam roster); `lab_setup`; and JSONL persistence. `GameplayEventTypes` is
not linked — it shares a file with a 353-line Unity-dependent class and needs extracting
first.

**Not verified in game.** Everything above is compile- and atlas-verified only. No hook
has been observed firing.

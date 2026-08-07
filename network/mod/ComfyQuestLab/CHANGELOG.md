# Changelog

### Unreleased

**Scaffold.** The project, the console, and one worked hook family. Nothing is published
and there is no download.

- New mod `ComfyQuestLab`, separate from ComfyNetworkSense on purpose: it hooks far more
  of the game and draws an overlay, and neither of those belongs in a mod that runs
  against the live server. Client-only — `Awake` detects a dedicated server and returns
  before applying a single patch.
- **Quest contract linked, not copied.** `TrackedQuest`, `QuestViewLoader` and
  `QuestTriggerEvaluator` compile from ComfyNetworkSense's own source files, the same way
  ComfyNetworkSense.Tests already links them. A quest that behaves one way in the lab
  behaves the same way in the shipping mod because both compile the same bytes.
- **Harvest wired end to end** as the worked example for the other seven categories:
  `TreeBase.Damage`, `TreeLog.Damage`, `Destructible.Damage`. These are the three the
  retired ComfyControlSurface hooked, and the reason punching a bush used to fire a quest
  and no longer does. `TreeLog` is separate from `TreeBase` because a felled trunk is a
  different type — that distinction is exactly the kind of thing a hand-written hook list
  gets wrong.
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

**Not here yet:** seven of eight atlas categories, the journal (stub), `lab_setup`, and
JSONL persistence. `GameplayEventTypes` is not linked — it shares a file with a 353-line
Unity-dependent class and needs extracting first.

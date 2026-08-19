# Creator-loop UX baseline

This is the product baseline for the in-game creator loop: what each surface may say, to
whom, and through which channel. It exists because the validation lap showed the loop was
mechanically complete but spoke maintainer language at creator moments — `1 pack, 1 loaded`
after F10, `Loaded quest-7b849e 1.0.0` plus a full content hash after F11, and
`0 locally owned` beside labels reading `LOCAL OWNER`. The governing intent is doc 03's
success condition:

> The creator can spend an hour iterating on an Event without restarting Valheim, manually
> moving files, remembering hotkeys, or wondering which revision is running.

## Three altitudes, one vocabulary

The source intents never reconciled their role words (doc 01 *author*, doc 03 *creator*,
doc 02 *creator lead*, doc 04 *author/maintainer*). For surfaces, this baseline settles it:

| Altitude | Is doing | May see | Must never see |
| --- | --- | --- | --- |
| **Player** | living inside the story | authored messages, the countdown banner, the charm glow | pack ids, hashes, versions, paths, snake_case |
| **Creator** | iterating on their own quest | the quest's **name**, whether the game took the latest revision, what changed, what broke | hashes, activation ids, raw exceptions |
| **Maintainer** | proving or diagnosing | hashes, activation ids, diagnostics, receipts, paths | — |

This is the receipts guardrail applied to HUD copy: explanation at the reader's level.

## Channel rules

- **`MessageHud.Center`** — reserved for the authored experience (the `message` action) and
  nothing else. The plumbing never writes here; a check result must not compete with the
  story for the same dramatic surface. The engine keeps exactly one Center writer.
- **`MessageHud.TopLeft`** — creator plumbing: check/load outcomes, the dev channel, the
  one-time discoverability hint. This matches Quest Lab's existing `Report` convention.
- **F9 drawer** — creator state, plus maintainer identity in the small detail line under
  the status and behind the `VERSIONS & ROLLBACK` disclosure.
- **BepInEx log** — maintainer only.
- The always-on countdown banner is player altitude: the deadline, never identity.

## Naming rule

Every creator-facing sentence leads with the quest's authored **title**; version second;
pack id and a short hash only as trailing detail, and only inside the drawer. The title
comes from the compiled experience document the pack inspection already produces — no
manifest change, no second file read (`PackCandidate.Titles`).

## The copy

All check/load copy is composed by `ComfyQuestContracts.CreatorLoopNotice` — a pure fact
type, like `TriggerCountdown` — so every sentence is provable in xUnit without the game.
The plugin renders; it never composes creator copy inline (pinned in
`tests/test_quest_runtime_arcane_sight.py`).

| Situation | Headline |
| --- | --- |
| Check, empty inbox | `No new quests in your inbox.` |
| Check, one ready | `The Woodbound Signal 1.2.0 is ready. Press F11 to play it.` |
| Check, several ready | `2 quests are ready. Open F9 to choose.` |
| Check, already current | `The Woodbound Signal 1.2.0 is already playing. Nothing new to load.` |
| Check, some rejected | + `1 of 2 quests can't be loaded. Open F9 for the reason.` |
| Check failed | `Couldn't read your quest inbox. Open F9 for the reason.` |
| Load, activated | `Now playing: The Woodbound Signal — 1.2.0` |
| Load, orphaned charms | + `3 charms belong to an earlier telling. Re-CAST them or roll back.` |
| Load, already current | `The Woodbound Signal 1.2.0 is already playing.` (no new activation) |
| Load, nothing to load | `Nothing to load. Press F10 to check your inbox.` |
| Load failed | `That quest couldn't be loaded. Open F9 for the reason.` |

Check copy states what checking proved; activation language belongs to load — the lap's
exact complaint about `loaded`. Raw diagnostics (`previous_active_content_mismatch`,
`charm_local_ownership_required`) stay in `Detail`, the drawer, and receipts. Key names in
the copy are the *configured* bindings, not hardcoded letters.

## Structural rules the copy sits on

1. **One state machine.** F10/F11 are accelerators of the same state the drawer buttons
   drive. F11 refreshes the inbox before activating, so the drawer's LOOK → VALIDATE →
   LOAD → CONFIRM ladder is a fact about what the keys did, not a narrative they bypass.
2. **An idle repeat press is a no-op.** `QuestPackStore.LoadLatest` answers "already
   current" without re-activating; before this, ten idle F11 presses silently evicted the
   entire ten-entry rollback history. The repeat press writes an `already_active` receipt.
3. **Consequences travel with the keypress.** The orphaned-binding count from the engine's
   single bounded scan rides the F11 confirmation instead of living only in the drawer's
   evidence scroll.
4. **Failure always says something.** The check path is guarded; an unreadable inbox
   reports itself instead of dying silently inside `Update()`.
5. **Keys respect text focus.** Chat, console, and text input suppress all Runtime
   hotkeys, mirroring the Lab's `InputGuard` seams without referencing its assembly.
6. **Urgency is a fact, not a parse.** The countdown banner's red state reads
   `RuntimeExperienceEngine.DeadlineUrgent()`, computed beside the line — a copy change
   can never silently kill it. The banner and `TriggerDeadline.Label` share one separator:
   `1/2, 6 seconds remaining`.
7. **Discoverability is taught in place.** One session-scoped TopLeft line when quest
   content exists (`Comfy Quest ready. Press F9 for the creator drawer.`), and the
   drawer's check/load buttons display their own configured hotkeys.

## What this baseline deliberately does not do

- No manifest change — the title is derived; `comfy-quest-pack/v3` belongs to Phase 5.
- The maintenance section (`VERSIONS & ROLLBACK`) keeps full hashes and activation ids:
  that is the maintainer surface, and demoting identity there would remove the proof.
- Quest Lab's own copy is not touched; its altitude fixes belong to the Phase 4 ownership
  decisions in `docs/quest-lab-persona-audit.md`.
- Whether the loop *feels* seamless is the one question a test cannot answer; it rides the
  Phase 3.5 live lap already queued.

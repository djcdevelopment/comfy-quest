# OMEN lap runbook — session 3, the Phase 3 exit verdicts

**Derived, not written.** The beat order below comes from three machine sources, and no
step is here because someone remembered it:

- `docs/runbooks/I2-QUESTPACK-OMEN.md` — the order already proven in a completed lap:
  Preflight → Prepare → publish → ValidateRevision → ArmPrivateWorld → launch and enter the
  world → F10 → F11 → CHECK → CAST → play.
- **A Studio rehearsal of the exact project being played** (`POST
  /api/v2/quest-studio/projects/{id}/rehearse`). Its `trace`, `transcript`,
  `available_paths` and `limitations[]` define the beat spine and — more importantly —
  define what the seat is *for*.
- **The precondition chain in the code.** `active/active-set.json` is written only by F11
  (`LoadLatest → ActivateCandidate`); F10 only inspects the inbox. Without it,
  `RuntimeCharmBinding.TryActive` returns `active_set_missing`, which is what both the
  CHECK preview and the CAST commit fail with. Without a cast charm,
  `RuntimeExperienceEngine.OnEvent` finds no binding and every action writes
  `event/unbound`. Session 3's first script skipped F11 and died on its first beat.

`tests/test_quest_runtime_validation_lap.py` now enforces this ordering over every live
runbook, so the omission cannot recur silently.

## What the seat is for

Rehearsal already walks the quest and reports `proof_level: rehearsal` with the
disclaimer *"Synthetic rehearsal only; this does not prove a Valheim adapter or live
mutation."* It also declares, per run, exactly where it diverges from play:

> Stage hold has 4 routes; this run followed highest priority route held.
> Route held waits for staged objects to be cleared; rehearsal removes 8 of them on
> request, while play removes one when the object itself is gone.

**Everything rehearsal proves, the seat is not asked to check.** Three judgments remain,
and they are the whole session. Do not add a fourth.

## Preparation (I run this; Derek does nothing)

Release-build the mod, `Preflight`, `Prepare -ContentProfile defense`, publish 1.0.0 alone
from Studio, `ValidateRevision -ExpectedVersion 1.0.0`, `ArmPrivateWorld`. No second
revision this lap — the update beat and its staging gate were proven live in session 2.
Optionally set `Presentation/DeadlineAnchor` before launch.

## Sequence

1. **Launch Valheim and enter the private solo world.** Arming only flips the safety
   config; it does not start the game.
2. **Press F10**, then **press F11**. F11 is what activates the quest — the card should
   read *Now playing: Ten-Minute Desperate Defense — 1.0.0*. Nothing below works before
   this.
3. **Open F9, aim at something you built, and press `` ` `` once** to CHECK. The captured
   object should light up and be named. **Press `` ` `` once more** to CAST. The strip
   should settle on LANDED.
4. **Say anything in normal local chat.** The ward wakes; eight Greylings spawn; a
   ten-minute clock starts.
5. **Play it once, honestly.**

## The three verdicts

- **Does the clock carry tension in combat?** Rehearsal cannot answer this at all — it has
  no screen. If the pill is in the wrong place, say where you want it; the anchor is a
  config fraction.
- **When the last Greyling falls, does the win arrive within a second or two?** This is
  rehearsal's declared limitation, verbatim: it despawns eight on request, play removes one
  at a time as they die. Session 2 lost this and the win arrived nine minutes late.
- **Do escalation and mercy read as intended, if they happen?** Rehearsal can walk all four
  `hold` routes and proves the mechanics; only whether they *feel* like escalation and
  mercy is yours.

Stop and record the moment anything produces a "can't answer why". Record Derek's words
verbatim, positive reactions included.

## Machine verdicts (I run these; no seat time)

`Monitor -Expectation kill_partial` — a kill that leaves the beat unmet reports the count
the player is working on (session 2: eight kills, every one `0/1`).
`Monitor -Expectation wave_cleared` — the completion is carried by a `kill`, not by
`timer_elapsed`. A `rck-` correlation id on the winning transition is the settle race being
caught rather than lost.

## Cleanup

`Cleanup` restores the install byte-exact and closes the safety window. Write findings from
what the seat said and what the receipts show, in that order — and read the rehearsal's
`limitations` before claiming anything was newly discovered.

# OMEN lap runbook — session 3, the Phase 3 exit verdicts

Short and targeted. Session 2 ran clean and still could not exit Phase 3: the wave
counted none of eight kills, and a real ten-minute deadline was never perceived.
Both causes are fixed and gated (`docs/phase-3-close-strategy.md`, ADR 0006). This
lap exists to answer the three defense questions with a tally that counts and a
clock that reads — nothing else. Budget: about fifteen minutes in the seat.

Record Derek's words verbatim, positive reactions included. Anything that produces a
"can't answer why" stops the lap and goes to the ledger; do not fill a gap with
repeated input.

## Preparation (before entering the world)

1. Build the deployable bytes in Release (`ComfyQuestRuntime`, `ComfyQuestContracts`)
   — `Prepare` deploys by SHA-256 and a stale Release DLL deploys stale behaviour.
2. `Prepare -ContentProfile defense`. The profile is recorded in the run context, so
   every later step gates the defense content by machine (ADR 0007); no step needs a
   hand-assembled proof the way session 2 did.
3. Publish **Ten-Minute Desperate Defense** 1.0.0 from Studio. **No second revision
   this lap** — the update beat and its staging gate were proven live in session 2
   and are not on trial again.
4. `ValidateRevision -ExpectedVersion 1.0.0`, then `ArmPrivateWorld`.
5. Optional, before the fight: set `Presentation/DeadlineAnchor` in the runtime
   config if 0.16 puts the pill somewhere the seat dislikes. Moving it is a supported
   answer, and where it ends up is itself a finding for the Phase 4 alert anchor.

## Sequence

### 1. Drawer pre-check (1 min)
Open F9 before any beat.
- [ ] The EXPERIENCE row no longer repeats the quest's title. Does the section
      boundary read better than session 2's stacked names?
- [ ] With nothing cast, the row says the quest has no Charm and how to cast one.
      Does that read as an instruction you would follow?

### 2. Cast the ward's anchor (2 min) — **do not skip**
Session 2's script omitted this beat entirely and the quest ignored the player until
a receipt explained why.
- [ ] Aim at something you built and press **`** once. Does the captured object light
      up in the world, and does the summary name the *thing* (e.g. "Wood wall") rather
      than only a ZDO id?
- [ ] Press **`** again to cast. Does the strip settle into LANDED and stay there,
      instead of snapping back to READY?
- Machine: `Monitor -Expectation bind_r1`.

### 3. Start the defense (1 min)
Say anything in normal local chat.
- [ ] The ward-wakes line appears on Center **and** in chat scrollback. Does the chat
      copy make the moment re-readable, or is it clutter?
- Machine: `Monitor -Expectation chat_advance`.

### 4. The fight (10 min) — the three exit questions
Play it once, honestly.
- [ ] **Does the countdown pill carry tension during actual combat?** It reads as a
      clock now (`9:54`), in a bordered amber pill, scaled to the screen, at the anchor
      you chose. Noticed at all? Glanced at mid-swing? Still fighting another overlay?
- [ ] **Does the reinforcement beat read as escalation rather than punishment?**
      (Three minutes in, six or more still standing → a second wave.)
- [ ] **Does mercy read as mercy rather than failure?** (Two deaths → the ward
      releases you.)
- [ ] And the one session 2 could not reach: **when the last Greyling falls, does the
      win arrive?** Within seconds, not minutes — and as the victory line, not as a
      deadline that happened to notice.
- Also watch, unprompted: is the drawer irrelevant during the fight (it should be —
  the pill and the story channel carry everything player-side)?

### 5. Machine verdicts (1 min)
Two of session 2's open questions have a machine form now. Both would have failed
last time.
- `Monitor -Expectation kill_partial` — a kill that leaves the beat unmet reports the
  count the player is working on (session 2: eight kills, every one `0/1`).
- `Monitor -Expectation wave_cleared` — the completion is carried by a `kill`
  (session 2: carried by `timer_elapsed`, nine minutes late).
Then read the last few receipts: a `rck-` correlation id on the winning transition is
the settle-race being caught rather than lost.

## Exit criteria

Phase 3 exits when the three defense questions have answers — any answers, including
"the pill is wallpaper", which is a finding and not a failure — and both ledger rows
in `docs/five-intent-validation-lap-backlog.md` close on live observation. Findings
land in the backlog; the composition and alert-anchor directions travel on to the W5
Phase 4 packet rather than being fixed piecemeal here.

## Cleanup

`Cleanup` restores the install byte-for-byte (packs, active set, `state/`,
`inbox-dev/`, config safety back to false) and sweeps lap-created files to
`post-run/`. Write the findings from what the seat said and what the receipts show —
in that order. Session 2's record set the player's own "i slaughtered them" aside in
favour of an inference about the engine, and carried a defect that never existed while
missing the proof of the one that did.

# Retrospective — the Phase 3 exit sessions (2026-08-19 → 2026-08-20)

One arc, honestly told: a mulliganed lap, a same-day fix sprint, and a second lap
that ran clean and still refused to let Phase 3 exit. Written the day it ended,
while the verbatims were fresh.

## What happened

- **Session 1 (mulliganed).** The seat verdict on a day of design-token adoption
  was "kernel UI on f9 press looks the same"; idle F10/F11 presses were invisible
  despite eight accepted receipts and the player concluded the keys were broken;
  47 identical rejection receipts spammed startup; and a pre-staged 1.0.1
  collapsed the update beat before it existed. The run was cleaned and restored;
  every finding went to the lap backlog.
- **The fix sprint (same day).** The drawer's full composition landed from the
  design canvas (`428c686` — built by a background worktree agent the next session
  wrongly declared dead); the canvas ladder, evidence time gutter, sentence-case
  actions, `hud_absent` emission evidence, and the rejection bound followed
  (`3351ec1`); `Prepare` learned to quarantine `state/` and `inbox-dev/` and the
  staging gate became machine-enforced (`4dddb0c`); packages refreshed (`09751dc`);
  the backlog carried every resolution (`3a60f6f`).
- **Session 2 (ran end-to-end, Phase 3 does not exit).** Every session-1 fix held
  on first live contact — quarantine caught all 8 state files, the staging gate
  held 1.0.1 until the activation was proven, idle presses re-asserted as "x7",
  the ward timer fired to the second. The lap then earned its keep twice over:
  the kill tally counted zero of eight real kills (destroy-settle race, ADR 0006)
  and a running ten-minute deadline carried zero perceived tension (presentation,
  ADR 0005). Recorded in `a473af6`; strategy in `phase-3-close-strategy.md`.

## What went well

- **Observed-need fixes all held live, first try.** Nothing built from session-1
  findings had to be rebuilt after session 2.
- **A process rule became a machine refusal.** "Wait for activation evidence"
  stopped being advice the moment `ValidateRevision` learned to say no (ADR 0001).
- **Receipts made every seat mystery diagnosable in under a minute.**
  `event/unbound` explained the quest that wouldn't start; `event/ignored ×8`
  turned "I slaughtered them and nothing happened" into a root cause the same
  hour; `bind/inscribed` settled "did my cast land" instantly.
- **The mulligan was the program's best purchase.** Two seat sessions cost under
  an hour of keyboard time and produced the two defects no synthetic harness
  could reach, plus the composition thesis that will shape Phase 4.

## What hurt, and the lessons

1. **We declared a live agent dead.** The composition worktree agent survived the
   session shutdown, finished, and landed on main — while the next session
   reported its work lost and nearly rebuilt it. *Lesson: liveness is a claim
   about evidence, not memory — check worktrees, branches, and logs again before
   declaring loss, and re-check before rebuilding.*
2. **Both harnesses fed the tally they existed to measure.** xUnit constructs
   `SpawnTally(8,0)` by hand; Studio rehearsal decrements a dictionary on request
   and says so **in its own machine output, on every run** — `limitations[]` from
   `QuestStudioWorkspace.BuildGuidedSteps` prints "Route held waits for staged objects to
   be cleared; rehearsal removes 8 of them on request, while play removes one when the
   object itself is gone." That sentence named this defect before it was ever played, and
   Studio rendered it on screen. Nobody read it; this lesson was re-derived overnight from
   receipts instead. The live
   `kill → destroy → re-poll` cycle was structurally untestable in either, so its
   race shipped straight to the player. The Lab even already knew destroys don't
   settle same-frame (`DestroySettleTimeout`) — gallery-local knowledge that never
   became engine-wide. *Lesson: every fact a harness injects is a seam; name each
   one in the lap runbook as "unproven until observed live," and treat
   engine-timing discoveries as engine-wide the day they're made.*
3. **Copy computed from the wrong noun.** "2 quests are ready. Open F9 to
   choose." counted valid candidates; the player saw two *versions* of one quest
   and hunted for a chooser that doesn't exist. *Lesson: player-facing counts
   must count player-visible nouns; pin the copy against the noun, not the
   collection size.*
4. **We overrode the player's account with a mechanism theory.** The seat said
   "ahh well i slaughtered them, but it was fun :]" — an unambiguous report that
   the wave was dead and the fight was won. The ledger row recorded it faithfully.
   Then the same write-up reached the 12:17:17 moment, reasoned *from a belief*
   ("the tally counted zero, so the only route that can fire at the ten-minute
   mark is the overrun"), and wrote that inference down as fact — an inference
   that required the player to have lost. Both halves sat in the same document,
   contradicting each other, unreconciled: a recorded victory and an invented
   defeat. The receipts settled it in one pass the next day (the *victory* route
   matched at the deadline event, outranking the overrun) and in settling it
   proved the tally defect outright. *Lesson: the player's account of what
   happened to them is evidence, and a mechanism inference is not. When the two
   disagree, the account stands and the inference is the thing under suspicion —
   the seat is not a witness to be corrected by a theory about the engine.* The
   inverted write-up cost a workstream item aimed at a bug that never existed and
   nearly buried the cleanest proof of the one that did.
5. **A cue executed against unverified state.** Session 1's collapsed beat came
   from staging on cue rather than on evidence. Now a standing guardrail (any
   cue/state disagreement gets a one-line hold, never silent execution) *and* a
   machine gate — belt and suspenders, both used in session 2.
6. **Runbooks drift like code but weren't reviewed like code.** The defense
   script omitted the CAST beat entirely (the quest ignored the player until a
   receipt revealed why) and instructed the pre-staging that sank session 1.
   *Lesson: a runbook edit that changes sequencing gets the same review and — where
   possible — the same pins as a code change.*

## Process changes

Already banked during the arc: the seat-time guardrail (hold on disagreement);
machine-enforced staging (ADR 0001); quarantine of all runtime state (ADR 0004);
`hud_absent` emission evidence; the "can't answer why" ledger discipline, which
paid for itself twice tonight.

Added by this retro:
- ~~**Injected-fact inventory:** every rehearsal/synthetic proof must enumerate the
  facts it injects; the lap runbook lists them as live-unproven seams.~~
  **Withdrawn 2026-08-20 — the product already does this.** Studio's guided rehearsal
  returns a per-run `limitations[]` naming every gap between rehearsal and play, plus a
  `proof_level` and a `disclaimer`, and `QuestStudioPage.renderRehearsal()` already draws
  them as a "Coverage limits" card. Prescribing a hand-written inventory duplicated a
  shipped feature — and contradicted the house rule two files over
  (`network/mod/ComfyQuestLab/README.md`: "not a checklist somebody can forget to
  update"). Replacement rule: **consume `limitations`, cite it, never re-enumerate by
  hand.**
- **Bounded recheck as doctrine** (ADR 0006): event-driven evaluation plus
  world-derived facts requires a bounded catch-up; never assume same-frame settle.
- **Copy pins name nouns:** new player-facing counts get a pin asserting what is
  being counted, not just the sentence.
- **The seat's account outranks an inference about the engine.** A lap row states
  what the player reported and what the receipts show; where a write-up needs a
  mechanism story to connect them, that story is labelled as one and checked
  against the capture before it is promoted. A row that contradicts the seat's own
  words is wrong until the evidence says otherwise.
- **Runbook sequencing changes ride the harness self-test** where a machine gate
  exists (as the staging gate now demonstrates).

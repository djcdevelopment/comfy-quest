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
   and says so in its own comments ("rehearsal has no ZDOs"). The live
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
5. **We wrote a finding we had not checked against the receipts.** This retro's
   first version reported that the defense's overrun labeled a failed ending
   "complete". It did not: no overrun ever fired. The victory route won at the
   deadline event, the engine recorded the win correctly, and the yellow text the
   seat praised was the victory line arriving nine minutes late. The receipts were
   sitting in the run capture the whole time and settled it in one pass — and in
   settling it, they proved the tally defect outright. *Lesson: a finding written
   from what the seat felt is a hypothesis; promote it only after the evidence the
   lap already preserved has been read. The wrong write-up cost a workstream item
   aimed at a bug that did not exist, and nearly buried the cleanest proof of the
   one that did.*
4. **A cue executed against unverified state.** Session 1's collapsed beat came
   from staging on cue rather than on evidence. Now a standing guardrail (any
   cue/state disagreement gets a one-line hold, never silent execution) *and* a
   machine gate — belt and suspenders, both used in session 2.
5. **Runbooks drift like code but weren't reviewed like code.** The defense
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
- **Injected-fact inventory:** every rehearsal/synthetic proof must enumerate the
  facts it injects; the lap runbook lists them as live-unproven seams.
- **Bounded recheck as doctrine** (ADR 0006): event-driven evaluation plus
  world-derived facts requires a bounded catch-up; never assume same-frame settle.
- **Copy pins name nouns:** new player-facing counts get a pin asserting what is
  being counted, not just the sentence.
- **Read the capture before writing the finding:** a lap record's rows carry the
  receipt that supports them, or they are marked as the seat's reading rather than
  the run's.
- **Runbook sequencing changes ride the harness self-test** where a machine gate
  exists (as the staging gate now demonstrates).

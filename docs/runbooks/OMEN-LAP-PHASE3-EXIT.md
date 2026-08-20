# OMEN lap runbook — Phase 3 exit + creator-loop design verdicts

> **HISTORICAL — session 2's script. Do not run it.** Superseded by
> `OMEN-LAP-PHASE3-EXIT-S3.md`. Kept because the lap record and the retro cite it, and
> because its two defects are the evidence for how this class of failure works.
>
> **Defect 1 — it omits the CAST beat entirely.** The player sequence goes from loading
> straight to playing the fight, so no charm is ever bound to an anchor object. Under
> `RuntimeExperienceEngine.OnEvent` no matching binding means every player action produces
> an `event/unbound` receipt and the quest ignores the player — which is exactly what
> happened live, and was only explained by reading a receipt mid-session.
>
> **Defect 2 — the premise below was false when written.** "Everything mechanical is
> synthetically proven" was untrue: the kill-tally race was a mechanical defect that no
> synthetic harness covered, and Studio's own rehearsal `limitations[]` had already said so
> ("rehearsal removes 8 of them on request, while play removes one when the object itself
> is gone"). A lap script may not assert that machinery is proven; it may only cite what a
> named proof actually covered, and what that proof declares it does not.

One bounded session on OMEN. This lap exists for judgments a test cannot make. Stop and
record the moment anything produces a "can't answer why" — do not fill gaps with repeated
input. Record Derek's words verbatim, including positive reactions; do not translate a
player reaction into a mechanism-only diagnosis.

## Preparation (before entering the world)

1. Deploy the gate-tested payload by SHA-256 (`Prepare` lane from the slice-1.4
   harness); quarantine historical questpacks recoverably; force
   `PrivateWorldConfirmed=false` until arming.
2. Publish **Ten-Minute Desperate Defense** (`desperate-defense` template) 1.0.0 from
   Studio. Do not rehearse it again first — the browser proof exists; the lap buys only
   the in-world read.
3. Hold the second version (1.0.1) as a Studio draft only: press **Start new iteration**
   so the version is bumped, but do **not** press Publish. Publish is the atomic staging
   act — session 1 pre-staged 1.0.1 into the inbox, the runtime loaded the highest
   version at the first check, and the first-load and update beats collapsed into one
   press. The harness enforces the order: `ValidateRevision -ExpectedVersion 1.0.1`
   refuses until the r1 activation is proven.

## Sequence — twenty minutes, five verdicts

### 1. First contact (2 min) — discoverability + status card
Enter the world cold, drawer closed.
- [ ] Did the one-time `Comfy Quest ready. Press F9 for the creator drawer.` line
      register, or vanish unread? (TopLeft channel, fires once per session.)
- [ ] Open F9: does the status card answer "which revision is running?" **before any
      scrolling or reading** — title, version, Now playing?
- [ ] Two-minute visual pre-check **before any beat**: does the drawer read as the
      design canvas's composition (status card leading, circle-and-rail ladder, time-
      guttered evidence feed, machinery behind one disclosure) — or still as a "kernel
      system information panel"? Session 1 answered "still kernel" after tokens alone;
      this is the full composition's follow-up measurement, and a repeat verdict costs
      two minutes here instead of the session.

### 2. The update loop in situ (3 min) — creator-loop copy
First play onto 1.0.0 (F10, F11), then stage the update in the proven order:
1. Run `ConfirmActivation`. It answers go/no-go from the load receipt and the active
   set — **stage the second revision only on `activation_confirmed`**. A cue that
   disagrees with observed state gets a one-line hold, never silent execution.
2. Press Publish in Studio for 1.0.1, then `ValidateRevision -ExpectedVersion 1.0.1`.

Then take the beat:
- [ ] Press F10 with the drawer **closed**: does the TopLeft copy
      (`…is ready. Press F11 to play it.`) read at a glance without the drawer open?
- [ ] Press F11: does `Now playing: <title> — <version>` land as confirmation rather
      than log output? If charms orphan, does the appended consequence read as an
      instruction you'd actually follow?
- [ ] Press F11 again idle: `…is already playing.` — and on repeat presses the HUD line
      should re-assert itself as `… x2`, `… x3`. Noise, or reassuring? (Session 1's
      invisible idle responses are the finding this verifies.)
- [ ] Does creator plumbing ever collide with story text on the center channel? (It
      must not — Center is authored-story-only now.)

### 3. The desperate defense (10 min) — the Phase 3 exit questions
Play it once, honestly. The three questions that decide Phase 3 exit:
- [ ] **Does the countdown banner carry tension during actual combat?** (Amber pill,
      red under 5s. Watch for: noticed at all? glanced at mid-swing? position fights
      Valheim's own HUD?)
- [ ] **Does the reinforcement beat read as escalation rather than punishment?**
      (3 minutes in, 6+ still standing → second wave.)
- [ ] **Does mercy read as mercy rather than failure?** (Two deaths → "the ward
      releases you" ending.)
Also watch, without prompting for it:
- [ ] During the fight, is the drawer irrelevant (good — the banner should carry
      everything player-side)?

### 4. Evidence read-back (3 min) — taxonomy legibility
After the quest ends, open F9 and read the evidence feed cold:
- [ ] Can you reconstruct what happened from the story rows (◆ ivory) alone?
- [ ] Does the CAST row's purple register as "that magic moment" or just another color?
- [ ] Do warnings (▲ amber) read as "do this next," per the design's rule?
- [ ] Is plumbing successfully ignorable?

### 5. Studio round-trip (2 min) — one language, two surfaces
Open Studio from the drawer:
- [ ] Same quest name, same state, same palette — does crossing the boundary feel like
      one product now? (The original flow-state ask.)
- [ ] Does the Studio status card agree with the drawer's, byte-for-byte on title and
      state?

## Exit criteria

Phase 3 exits if the three defense questions get answers (any answers — including "the
banner is wallpaper," which is a finding, not a failure) and no "can't answer why"
moment survives the session unexplained. Findings land in
`docs/five-intent-validation-lap-backlog.md` as observations; design verdicts
(retoken/card/taxonomy) land beside them and decide whether the drawer's full
status-card composition proceeds.

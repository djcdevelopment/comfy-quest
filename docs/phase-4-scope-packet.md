# Phase 4 scope packet — one decision, everything on the table

Strategy workstream W5. This is a decision document, not a work order: it puts the
four parked inputs, the Lab ownership map, and three shapes of Phase 4 in front of
one call. Nothing in it is implemented before that call is made.

## The decision

The program plan scopes Phase 4 as the **spellbook/notebook** — portable personal
craft: pattern identity `pattern:<slug>@<semver>`, the `comfy-quest-notebook/` store,
a `hidden|explainable|share_selected|remixable` permission vocabulary, a Studio
notebook browser, and "start route from pattern".

Everything the seat has asked for since the design language landed is something else:
a creator surface that survives its hundredth use, one place a player knows to look,
a Studio that reads in the order work happens, and a Studio↔world switch that stops
costing what it costs. None of that is funded by the plan.

**So the call is: does Phase 4 stay the notebook, does presentation take its place, or
do they ship as one thing?**

## What is already paid for

The design-language work bought the appearance: shared tokens on both surfaces, the
shared active-title fact, the four-kind evidence taxonomy, one HUD channel convention,
and the drawer's full canvas composition. The Phase 3 close-out added the interim
deadline pill with a player-set anchor fraction — deliberately a down payment on a
real anchor rather than a competitor to it (ADR 0005).

What remains is **capability and composition, not appearance.** That distinction is
what makes the items below scope decisions rather than polish.

## The four inputs

### A — The composition thesis: a top bar that minimizes to four dots

> "i think this addon works better as a top bar with horizontal layout, the 4 step
> sequence is large because we're in R&D but think of a user doing this hundreds of
> times, it could just minimize to 4 dots and they'd understand what was happening"
> — session 2

The ladder earned its size for first-run legibility; the hundredth run wants a
minimized strip. This is a Phase 4 item and not a polish task because it changes what
the drawer *is* — from a panel you open into a surface that is always there — and that
single change also answers where alerts live and how often the creator must switch
surfaces at all. Status: **no verdict was ever taken.** Session 2's composition verdict
was superseded by this direction before it was answered.

### B — One known, player-configurable alert anchor (ADR 0005, proposed)

> "yeah we need a way for these alerts to appear in a known *config plan on the screen.
> it too much to ask in these simple ones let alone hard combat" — session 2

One requirement explains three separate session-2 misses: a running ten-minute deadline
that merged into a host HUD band, story text that existed only as a glimpse, and an
evidence feed that failed discovery cold ("if you didn't ask me to read it i wouldn't
even have known it was there"). The channel taxonomy answers *who is speaking*; nothing
yet answers *where the player looks*. Combat is the design's hardest case and its
acceptance test: an alert scheme that needs a calm player has failed. Status:
**requirement accepted, design unscoped.**

### C — Studio's reading order

> "button need to be larger and we need to think thru the positioning, as a user flow
> and how the UI placement and size can optimized for to align left to right, top to
> bottom sort of naturally reading patterns for (for me) relative the flow of actions,
> outputs, decision points and feedback(s)" — session 2

The drawer→Studio crossing itself landed ("the click to open feels nice"). What is
unaddressed is Studio's own composition: the page is organized by object, not by the
order work happens in. Status: **direction only, no design.**

### D — The Studio↔world switch cost

Already priced in `docs/f9-switch-cost-decision-brief.md` as four options: **O1**
polish the seam (small, no scope risk), **O2** in-world rehearsal by repointing Lab's
Scenarios machinery (medium; lands inside whatever Lab becomes), **O3** minimal in-world
*tuning* through the revision-guarded contract (high; the first true in-world write
path), **O4** declare the second-screen stance (small; quietly excludes single-screen
players). The brief's own recommendation stands: sequence O1 now, decide O2 inside this
scope call, hold O3 until a lap shows a creator losing the thread at the boundary.

## The Lab ownership map this call has to settle anyway

From the F6 persona audit — decision records today, work orders the moment Phase 4
starts:

- **The Spellbook name is already taken.** Lab's Spellbook tab is a static school
  browser; Phase 4's spellbook is the portable notebook. The audit's own trigger for
  acting — "Phase 4's notebook needing the Spellbook name" — is exactly this call.
  Recommendation on the table: the notebook keeps **Spellbook**, Lab's tab becomes the
  **Grimoire** it already renders.
- **There are two Arcane Sights**, same name and same technique across two mods. Intent
  01 defines Arcane Sight as *the* in-world runtime debugger; the Runtime one keeps the
  name, Lab's becomes gallery highlighting.
- **Lab already owns the missing rung of intent 03's loop.** Scenarios runs
  `Prepare draft → Run evaluator → Report → Export` through the real shared evaluator,
  with receipts — aimed at release verification, not creators. Repointing it is O2.
- **Lab solved intent 04's hardest problem in miniature.** Its
  `BINDABLE / DIAGNOSTIC / NO TRIGGER / NOT IN BUILD` classifier, each verdict carrying
  player-altitude prose, is the same shape as the permission enum. Model the vocabulary
  on it rather than inventing one.
- **No in-world authoring exists anywhere.** Every in-world surface can observe and
  activate but never change, which is why the switch is mandatory for any edit.

## Three shapes for Phase 4

**P4-A — Notebook as planned.** Presentation stays parked; the interim anchor holds.
Cheapest to plan and keeps Phase 5's manifest dependency (pattern identity) on schedule.
Cost: every seat session since the design adoption has asked for presentation, and
parking it a second time guarantees the next lap re-litigates it instead of judging
what it was convened to judge.

**P4-B — Presentation first, notebook after.** A composition phase: top bar with its
four-dot state, the alert anchor, Studio's reading order, O1's seam polish. Answers the
seat directly and immediately. Cost: the program spends a phase without a capability,
and pattern identity — which Phase 5's manifest is designed around — slips a phase.

**P4-C — Ship the notebook *on* the new composition. (Recommended.)** The notebook is
the first feature that genuinely needs a persistent, minimized creator surface: you
consult patterns *while building*, which a modal panel is the wrong shape for. So the
top bar is not a polish line item — it is the notebook's delivery surface, and the
alert anchor is what the notebook's own notices speak through. One phase, one exit,
presentation paid for by the feature that requires it.
Risk: scope creep, and it is a real risk. Bound it by leaving the plan's exit criteria
exactly as written — *two local profiles standing in for two creators; notebook entries
survive Event deletion and round-trip into a new Studio draft* — and treating the
composition as the surface those criteria are demonstrated on, not as its own
deliverable with its own acceptance list.

## What session 3 still adds

- Verdict 3a — does the clock carry tension in combat — tells us whether the anchor
  requirement is about **position** or about **presence**. Those need different designs.
- If the seat *moves* the pill's configurable fraction, where it lands is the anchor's
  first real data point.
- Escalation and mercy were unreachable behind the tally defect; both are content
  verdicts that feed what the notebook's first patterns should be.

None of these change which shape is right. They change how the composition work inside
it is aimed.

## Recommendation

**P4-C**, with the composition work bounded to three things: the top bar with its
four-dot minimized state, the configurable alert anchor, and O1's seam polish. Hold O2
until the Lab ownership decisions below are made in the same call; hold O3 under the
palette-admission rule until a lap demonstrates a creator losing the thread at the
boundary. Take the Spellbook/Grimoire rename and the Arcane Sight naming as part of
this call, because the notebook is what forces both.

## What I need from you

1. **Which shape** — P4-A, P4-B, or P4-C.
2. **Does the top bar replace the drawer, or coexist with it?** (Replace is cleaner and
   riskier; coexist means two surfaces to keep honest.)
3. **Do the Lab ownership decisions ride this phase**, or stay decision records?

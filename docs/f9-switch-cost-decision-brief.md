# Decision brief — the Studio↔world switch cost (Phase 4 scoping input)

## The finding, restated

The lap recorded: "F9 felt like a kernel system-information panel. Its valuable role is
to make the cognitive switch between defining/creating in the web Studio and acting as
an in-world Creator feel limited to nearly cost-free." The persona audit sharpened it:
**no in-world editor exists anywhere** — Lab has none, Runtime has none; every in-world
surface can observe and activate but never change. The switch to Studio is therefore
*mandatory for any edit*, which makes this a scope decision, not a polish task.

What the design-language work already bought: shared tokens, the shared title fact, the
shared evidence taxonomy, and one HUD channel convention. The remaining cost is
*capability*, not appearance.

## The one existing asset

Quest Lab's Scenarios machinery is the only in-world rehearsal rung in the product:
`Prepare draft → Run evaluator → Report → Export` through the real shared evaluator,
with receipts. It is currently aimed at release verification. Repointing it is the
cheapest path to "iterate without leaving the world" — build nothing new, change who it
serves.

## Options (not mutually exclusive; ordered by cost)

**O1 — Accept the switch; polish the seam.** Keep Studio as the only editor. Invest in
round-trip continuity: the drawer's Open Studio deep-links to the active project's
publish lane; Studio's Play confirmation names what to do in-world next. Cost: small.
Ceiling: the switch stays mandatory; the complaint is dampened, not resolved.

**O2 — In-world rehearsal via Lab's machinery (recommended candidate).** A creator in
the world can re-run the current quest's rehearsal against the real evaluator and read
the trace — no browser. Repoints Scenarios; adds no editor; respects the
palette-admission rule (observed need exists: the lap said so). Cost: medium. Risk:
blurs Lab's Phase 4 ownership boundaries — should be decided together with the
Spellbook/Grimoire rename and the maintainer-gate question.

**O3 — Minimal in-world *tuning*, not authoring.** The drawer (or Lab) exposes a tiny
set of authored knobs the creator marked tunable (a timer's seconds, a spawn count) —
edits flow back through the same revision-guarded contract as Studio, producing a real
dev revision. This is the WeakAuras-ethos answer (compose in the world you play in) and
the first true in-world *write* path. Cost: high — it needs the revision contract in
the mod, UI, and new pins. Premature before Phase 4's identity work; recorded so the
option is priced, not forgotten.

**O4 — Second-screen stance.** Declare Studio-on-a-second-monitor the intended posture
and design for it: Studio's live lane already refreshes every 3s; make the in-game
surfaces assume a companion screen exists (shorter drawer, more banner). Cost: small.
Risk: quietly excludes single-screen players; contradicts "the process is so seamless"
if the seam is a monitor bezel.

## What the decision needs before it's made

1. The OMEN lap's verdict on whether the retokened drawer + status card already lowered
   the felt cost (measure before adding capability).
2. Phase 4's Lab ownership decisions (audit: Spellbook rename, maintainer gating) — O2
   lands inside whatever Lab becomes.
3. One observed authoring loop where the switch demonstrably broke flow — the
   palette-admission rule applies to capability here too: admit the in-world write path
   (O3) only when a lap shows a creator losing the thread crossing the boundary.

## Recommendation

Sequence O1 now (cheap, no scope risk), decide O2 inside Phase 4 scoping with the Lab
ownership map on the table, hold O3 until a lap demonstrates the need, and treat O4 as
a posture question for the community docs rather than a build item.

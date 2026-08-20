# 0005 — A channel taxonomy is not an anchor

Status: accepted — one configurable anchor; the composition that carries it is being
built, not scheduled (amended 2026-08-20, see Decision).

## Context

The creator-loop design language gave every message a voice: story on Center,
plumbing on TopLeft, the four-kind evidence taxonomy, the always-on deadline
banner. Session 2 proved the taxonomy answers *who is speaking* but not *where
the player looks*: a running ten-minute deadline was never perceived (its banner
merged with a host HUD band), the overrun story text was praised but existed only
as a glimpse ("we should also post it in chat … so there's history of it not just
the glimpse"), and the evidence feed failed discovery cold ("if you didn't ask me
to read it i wouldn't even have known it was there"). The seat's summary:
"we need a way for these alerts to appear in a known *config plan on the screen.
it too much to ask in these simple ones let alone hard combat."

## Decision

Alerts get **one known, player-configurable anchor**, and every channel speaks through it
rather than forking its own fixed position.

> **Amended 2026-08-20.** This decision originally read: "a Phase 4 presentation
> requirement, senior to per-channel fixes", with interim work "explicitly subordinate".
> That clause was written by me, not asked for, and it converted the seat's *design
> direction* — a horizontal top bar minimising to four dots, given in session 2 — into a
> scheduling question. I then cited my own clause back at Derek as the reason his design
> was unbuilt, and produced `docs/phase-4-scope-packet.md` asking him which phase it
> belonged to. **The deferral clause is struck.** A design decision from the seat is an
> input to a build, not an input to a governance artifact. What survives is the technical
> constraint, which is the part that was actually load-bearing: one anchor, not many.

## Consequences

- Phase 4 scoping receives this beside the seat's composition direction (a
  horizontal top bar that minimizes to four dots) and the F9 switch-cost brief.
- The W2 interim (landed 2026-08-20) honours this by not choosing a position at
  all: the deadline pill's vertical anchor is a player-set fraction of screen
  height (`Presentation/DeadlineAnchor`). It is a down payment on the anchor's
  configurability, not a competitor to it, and the real anchor subsumes it.
- Until the anchor exists, every new surface decision must answer "where does the
  player already look?" before choosing a position.
- Combat is the design's hardest case and its acceptance test: an alert scheme
  that needs a calm player has failed.

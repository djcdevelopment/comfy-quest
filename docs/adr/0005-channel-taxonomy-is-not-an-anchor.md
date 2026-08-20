# 0005 — A channel taxonomy is not an anchor

Status: proposed — the requirement is accepted; the anchor's design lands with
Phase 4 scoping.

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

A single, known, player-configurable alert anchor is a Phase 4 presentation
requirement, senior to per-channel fixes. Interim per-channel improvements (W2's
banner separation, story-to-chat history) are explicitly subordinate: they must
not invent new fixed positions that the anchor will have to unwind.

## Consequences

- Phase 4 scoping receives this beside the seat's composition direction (a
  horizontal top bar that minimizes to four dots) and the F9 switch-cost brief.
- Until the anchor exists, every new surface decision must answer "where does the
  player already look?" before choosing a position.
- Combat is the design's hardest case and its acceptance test: an alert scheme
  that needs a calm player has failed.

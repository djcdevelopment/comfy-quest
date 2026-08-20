# 0002 — Idle responses ride native HUD repetition

Status: accepted — live-confirmed in lap session 2 ("… x7").

## Context

In session 1, five idle F10 presses and three idle F11 presses were all accepted
by receipt yet nothing legible reached the player, who concluded the keys were
broken and ended the session. Root cause: every idle press composed a
byte-identical sentence handed to Valheim's top-left `MessageHud` with message
amount 0; the HUD merges a repeated text into the line already on screen and only
renders its "xN" counter once summed amounts exceed one — so every repeat
vanished. The ledger rule for the fix forbade adding new channels before the
emission path was understood.

## Decision

Idle creator-loop responses re-assert through the HUD's own repeat affordance:
idleness is a contract fact tagged where the sentence is composed
(`CreatorLoopNotice.Idle`), the plugin passes amount `reassert?1:0` to
`ShowMessage`, and a repeat renders as "… x2", "… x3". No new channel, no string
mutation (no counters or timestamps polluting pinned copy). Emission logs
`hud_absent` when `MessageHud` is not live, so "not shown" and "shown and missed"
are distinguishable in captures. The status card additionally carries a
first-class idle state ("Now playing — up to date") so the drawer never answers
idleness with silence.

## Consequences

- Session 2 observed "… x7" live after seven idle presses; the session-1 ledger
  row is closed.
- Copy pins stay byte-stable; the repetition signal is the channel's, not ours.
- The player's *known place to look* problem remains — see ADR 0005.

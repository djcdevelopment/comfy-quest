# 0014 — One human launch and world entry is the baseline

Status: accepted 2026-08-24.

## Context

Lane 4A's exit required that automation "launches and closes the installed game through the
standalone harness". Nothing in this repository can do that. The only `valheim.exe` reference
anywhere is an existence check at `tools/creator-session/Invoke-CreatorSession.ps1:84`, and
the harnesses that exist elsewhere are not consumable — see
[0012](0012-creator-session-world-entry-is-not-field-lab-orchestration.md).

Meanwhile `NFR-SEAT-001` already grants the opposite: *"A planned creative session may require
one launch, world entry, authored play/build time, and one quit."* And the Creator Session
loop already operates that way — its step 2 is explicitly "the only keyboard step needed".

So the exit gate demanded autonomy the system did not have, while the requirement it sits
beside permitted the human step the system actually uses. Success was defined around
automation that does not exist, which makes the rest of Creator OS unprovable for a reason
unrelated to whether it works.

The fix is not to weaken the goal. It is to stop conflating two different things: what the
system does today, and how far the human boundary will eventually recede.

## Decision

**Exactly one human action is permitted per creator session: launching the game and entering
the pinned authoring world. Everything after world entry is machine-owned.**

**The allowance is strictly scoped to launch and world entry.** It is not a generic "one human
intervention" budget. In particular it does not permit relaying a console command, copying a
file, reading a hash or log or receipt back to the machine, pressing a key on automation's
behalf, retrying a failed mechanical step, or verifying any fact the machine can observe.

The strict scoping is the substance of this record. Read as a generic allowance, it becomes an
escape hatch for whichever piece of automation is missing that week — and it would retroactively
excuse exactly the seat-as-KVM pattern the golden rule exists to prevent.

Recorded machine-readably in `docs/creator-os-phases.json` under `human_boundary`.

## Consequences

- Lane 4A no longer waits on launch automation. It can concentrate on what it is actually
  about — whether Creator OS can operate a guild.
- **Automating world entry becomes a later reduction of the human boundary, not a prerequisite
  for any lane exit.** [0012](0012-creator-session-world-entry-is-not-field-lab-orchestration.md)
  says where that capability would live when it is built; this record says it does not gate
  anything in the meantime.
- Every *other* human touch in a session remains a defect with a receipt, exactly as
  `NFR-SEAT-001` already states. This record narrows what is excused; it does not broaden it.
- The 4A exit prose in `docs/creator-portfolio-requirements.md` still describes the old
  autonomous-launch gate and must be brought into agreement. Under
  [0013](0013-one-numbering-authority-for-lane-vocabulary.md) that prose is a reference rather
  than an authority, so the disagreement is a documentation defect, not a live contradiction.
- A future record may narrow this allowance to zero. Widening it — to a second human action, or
  to a generic intervention budget — requires superseding this one.

# 0001 — Activation evidence gates staging

Status: accepted — proven live in lap session 2 (2026-08-20).

## Context

The runtime always activates the highest valid version in the inbox. An "update
beat" — the player experiencing new content arriving over running content —
therefore exists only if the newer version lands *after* the older one is playing.
In Phase 3 exit session 1, a mid-session cue staged 1.0.1 before 1.0.0 had ever
been activated; the first check found both, loaded 1.0.1 directly, and the
first-load and update beats collapsed into one press
(`five-intent-validation-lap-backlog.md`, session-1 pre-lap findings).

## Decision

Staging a second revision waits on machine evidence of the first activation —
a `load/activated` receipt in agreement with `active/active-set.json` on pack id,
version, content hash, and a well-formed activation id — never on a cue or a
human's recollection. Enforced twice: `ValidateRevision -ExpectedVersion 1.0.1`
refuses until the r1 activation is recorded, and the read-only `ConfirmActivation`
action answers go/no-go for the second Publish
(`tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1`). Runbooks hold the
second version as an unpublished Studio draft until `activation_confirmed`.

## Consequences

- The collapsed-beat failure is structurally unrepeatable; session 2's update beat
  survived on the first try.
- Studio publish stays the single atomic staging act; no intermediate state grew.
- The gate is currently Woodbound-wired through `Assert-WoodboundCandidate`
  (generalization is strategy workstream W4); session 2 proved the gate's substance
  by hand for non-Woodbound content.

# 0017 — An absent experience selector means exactly one, and activation clears it

Status: accepted 2026-08-25.

## Context

`QuestPackStore.InspectLane` has always validated, hashed and compiled *N* experience documents
in a v2 archive. Two Runtime call sites then refused the second one:
`RuntimeCharmBinding.TryActive` returned `active_experience_ambiguous` the moment it found
another, and `RuntimeExperienceEngine.TryLoad` refused any entry count but one (audit D1).

That single check is what kept a guild from shipping and binding as one unit, blocking
`FR-RUN-001` and the 4A exit's "selects and runs more than one guild experience". The contract
layer was ahead of the runtime layer, so the fix is a selector — not a pack format, not an
orchestration system, not a Runtime rewrite.

The risk in adding one is not the happy path. It is that a selector introduces a second way for
Runtime to be wrong about what it is running, and a wrong binding is worse than a refusal: it
produces receipts that look valid and prove the wrong thing.

## Decision

**An absent selector means the pack holds exactly one experience, and behaves exactly as it did
before the selector existed — including the diagnostic it fails with.**

- `ActiveSet.experience_id` is additive and nullable, the same shape `activation_id` already
  uses and that the schema check behind `Rollback` has always tolerated. Unset, it serialises
  away entirely, so an active set written before this field and one written after are
  byte-identical.
- With no selector and more than one document, resolution still fails with
  `active_experience_ambiguous`. Runtime does not pick the first one, and it does not pick the
  alphabetically-lowest one.
- **Activation clears the selector.** A new revision may not contain the selected experience,
  and an explicit re-selection is cheaper than a silently wrong binding. Publishing therefore
  resets the choice; that cost is accepted.
- **Selection is not an activation.** It mints no activation id and archives no history, because
  nothing about the installed content changed — only which of its experiences answers.
  Re-selecting what is already selected does not rewrite the file, because the engine
  invalidates its cached document on that file's write time.
- Two documents sharing an id stay ambiguous even when one is selected, because there is no
  correct way to choose between them.

**Resolution lives in one place** — `ActiveExperienceResolver` in Contracts — behind both former
refusal sites. They had independently reimplemented the same archive walk and could have
drifted on what "active" means. It is also the only form in which this logic is testable, since
neither call site can be reached without the game.

The bounded operation that drives it, `select_experience`, joins the existing run-control
allowlist. It addresses the activated pack rather than a run, so it is the one operation
carrying no run id — and the reset operations are refused if they carry an experience id, so
the new field cannot ride an old path. It sits behind the same machine, world and
private-world gates as `apply_reset`, because it changes what Runtime answers to.

## Consequences

- Multi-experience guild packs bind and run. Independent run state, prerequisites and unlocks
  build **on top of** this selector rather than alongside it.
- Single-experience packs are unaffected, and that equivalence is asserted rather than assumed.
- Every ambiguity refuses rather than guesses. If a future change wants Runtime to choose a
  default when several documents are present, it supersedes this record — that is the change
  worth noticing, not the one worth making quietly.
- `MaxSelectableExperiences` is declared and tested at 64, per `NFR-BOUND-001`'s rule that a new
  limit is declared before its feature ships. This is not audit C5, which is about
  `MaxProjects` going unchecked on project creation and remains parked.

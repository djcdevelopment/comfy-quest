# Architecture decision records

Load-bearing decisions for the Quest product, one file per decision:
`NNNN-short-slug.md`, format Status / Context / Decision / Consequences. A record
quotes the evidence that forced it — seat verbatims, receipts, captures — and links
the lap-backlog section it came from. Statuses: `proposed`, `accepted`,
`superseded by NNNN`. Records are append-only; a reversal gets a new record.

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-activation-evidence-gates-staging.md) | Activation evidence gates staging | accepted (proven live) |
| [0002](0002-idle-responses-ride-native-hud-repetition.md) | Idle responses ride native HUD repetition | accepted (proven live) |
| [0003](0003-bounded-identical-rejection-receipts.md) | Bounded identical rejection receipts | accepted |
| [0004](0004-prepare-quarantines-all-runtime-state.md) | Prepare quarantines all runtime state | accepted (proven live) |
| [0005](0005-channel-taxonomy-is-not-an-anchor.md) | A channel taxonomy is not an anchor | accepted (deferral clause struck 2026-08-20) |
| [0006](0006-bounded-recheck-for-adaptive-routes.md) | Bounded recheck for adaptive routes | accepted (awaiting live confirmation) |
| [0007](0007-lap-gates-are-keyed-to-a-content-profile.md) | Lap gates are keyed to a content profile | accepted |
| [0008](0008-the-overhead-bar-replaces-the-f9-drawer.md) | The overhead bar replaces the F9 drawer | accepted (extends 0005; verified live) |
| [0009](0009-the-saved-world-is-the-playable-release-authority.md) | The saved world is the playable release authority | accepted |
| [0010](0010-world-packaging-is-deferred-out-of-rnd.md) | Versioned world packaging is deferred out of R&D | accepted |
| [0011](0011-the-workbench-may-not-borrow-a-private-gateway.md) | The Workbench may not borrow a private operator gateway | accepted |
| [0012](0012-creator-session-world-entry-is-not-field-lab-orchestration.md) | Creator-session world entry is not field-lab orchestration | accepted |
| [0013](0013-one-numbering-authority-for-lane-vocabulary.md) | One numbering authority for lane vocabulary | accepted |
| [0014](0014-one-human-launch-and-entry-is-the-baseline.md) | One human launch and world entry is the baseline | accepted |
| [0015](0015-the-requirements-ledger-cannot-confer-lane-authority.md) | The requirements ledger records lane authority, it cannot confer it | accepted (extends 0013) |
| [0016](0016-evidence-retention-archives-it-never-silently-deletes.md) | Evidence retention archives; it never silently deletes | accepted |
| [0017](0017-an-absent-experience-selector-means-exactly-one.md) | An absent experience selector means exactly one | accepted |

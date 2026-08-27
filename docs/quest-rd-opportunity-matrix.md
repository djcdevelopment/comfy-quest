# Quest toolkit R&D opportunity matrix

This is a lap-selection aid, not a product backlog. The working rule is to prove the
smallest edge that unlocks a larger authoring surface, project it through Grimoire,
Studio, Runtime, and receipts, then use one short game batch to decide what deserves
another cycle.

## Current projection

`Grimoire primary seam -> generated fast-signal catalog -> Studio beat -> Runtime adapter -> progress receipt`

The catalog is intentionally small. Say, shout, drop, pickup, equip, consume, heal,
and durable wait are enough to explore ordering, repetition, time windows, feedback,
and rewards without asking an author to identify ZDOs or manipulate graph plumbing.

| Edge | What is structurally projected | Remaining live question |
| --- | --- | --- |
| Say / shout | privacy-minimal chat mode, fixed target | already proven locally; multiplayer actor changes are a separate lap |
| Drop | wildcard stable item prefab | already observed live; exact-item authoring can wait |
| Pickup / equip / consume | reviewed `core` + `primary` Grimoire seams and local-player Runtime adapters | confirm all three once in the next OMEN batch |
| Heal | wildcard local healing signal | already observed; decide later whether amount thresholds are worth exposing |
| Wait | durable named timer | already proven; keep time controls simple |
| Repeat + window | contract `COUNT(EVENT)` with visible partial progress | confirm `1/2` then completion in one live lap |
| Resume Creator Session by id | install-local session manifest carries the exact machine, world, profile, and recovery pins | passed on the active ERA17 session: `Status -SessionId era17-broken-way-20260827-r2` resolved OMEN, world `-523956327`, `ComfyEra17`, `tugcorp`, matching plugin hashes, running game, current 0.1.1 activation, and stage `start` without requiring redundant machine/world arguments |
| Open a newly created guild for editing | Studio persists and selects the guild, but its settings remain inside a collapsed disclosure | passed in Broken Way authoring r5; the visible disclosure was opened and `guild-2de197289aea` now holds the authored title, premise, three bands, questline, six ordered quests, and two repeatable events |
| Cross from the library into authoring | a new blank draft opens while the library overlay remains an intentional modal boundary | passed in r5 by closing the visible library boundary before author-canvas input; all eight campaign projects were authored through Studio |
| Wait for the new draft identity | project creation is asynchronous, so the previously open clean draft remains a valid page until the new project response is loaded | passed in r5; the unrelated `desperate-defense-c2afbc` title was restored and eight distinct campaign project/experience identities were retained without another cross-draft edit |
| Place an artifact in a progression band | Studio derives the available band choices from the selected guild destination | passed in r5; six quests and two repeatable events are placed in the intended Find/Mend/Keep bands and the compiled guild contains eight experience documents |
| Resume the already-current draft | selecting the current project can legitimately leave the identity predicate true before the drawer transition finishes | passed in r5; current-project resume no longer raced the drawer and the full author/rehearse/publish preparation completed |
| Consolidate bounded Runtime control through Isolate | a product-owned provider release contributes status, receipts, identity-pinned creator requests, and bounded run control to an explicit Isolate profile | installed slices passed the Quest path: a missing caller credential and wrong ERA17 world/session identities were rejected, the correct session armed, 32 bounded candidates were listed, the selected experience rebound, and the exact 0.1.1 run reported. Final r6 status returned its own non-secret Creator Session identity, and a follow-up control succeeded using only that bounded status result; released identity, per-lap generated credentials, volunteer-machine setup, and teardown remain open |
| Teach exact buildable piece targets | Studio examples come from the generated creator catalog and Runtime compares the prefab string emitted by Valheim | installed Valheim 0.221.12 and the matching prefab dump proved `woodwall`, not `wood_wall`; the four examples, both campaign routes, generated catalogs, and live Studio were corrected, and a mutated `wood_wall` example now turns the generator test red |
| Relate a Studio project to its containing guild release | Studio compiles the current guild identity, filters receipts to the selected experience, and recognizes explicit Isolate binding receipts | source integration tests and the installed ERA17 screen now report The Broken Sign as `current` / `bound` with matching 0.1.1 hashes and Validation, Transfer, Activation, and Rebind all PASS; a sibling project reports `other_experience` instead of the false `other_version` |
| Tolerate a briefly stalled armed Runtime | Play distinguishes a missing/disarmed Runtime from an already-armed heartbeat that has gone temporarily stale | the installed pre-fix sequence rejected once and succeeded after the Valheim heartbeat resumed; r5 then showed Studio's run view fail closed during a real Valheim main-thread pause and reconnect on the next heartbeat. The patched Play path waits up to 30 seconds only for an already-armed status and its delayed-heartbeat integration test passes; the exact installed Play grace branch remains open |
| Bind control authority to completed world entry | Runtime latches a Creator Session only after exact world entry reaches `entered`, and both creator and run-control receipts echo that identity | passed in installed r5: creator request `mcp-creator-status-20260827T153152128008Z-bfe24e02` rejected r4 with `creator_session_mismatch`, run request `mcp-run-list_binding_candidates-20260827T153157142232Z-b67f3790` rejected it with `runtime_creator_session_mismatch`, and only r5 armed and returned 32 candidates; source tests also reject missing and forged session evidence |
| Bound synchronous provider calls | each Isolate request expires before its tool wait and retracts only its own unclaimed fixed mailbox | the provider timeout test observed expiry inside the two-second wait and an empty mailbox afterward; two consecutive package builds produced the same archive hash |
| Let a large world finish saving | Creator Session shutdown waits for Valheim's process exit, with a two-minute minimum before force, and preserves any interrupted `.new` file in evidence | passed in installed r5: r4 exposed the old 20-second kill and its 496 MB partial was quarantined; the patched Stop then let ERA17 spend 32.46 seconds writing its 1.30 GB database, observed `World saved`, exited gracefully with no forced process, and left no world or character `.new` files |

## One prepared batch lap

Use Studio's **R&D Signal Circuit** template. Rehearse it in the browser, publish one
immutable iteration, then do the explicit Runtime Check and Load. The game portion is
one sequence on OMEN: say, wait, shout, drop twice, pick up, equip, consume, and heal.
The Runtime cockpit should name the current beat and show partial count progress. Save
the resulting receipt set as the decision evidence.

This lap does not need i5. Use the second machine only when the question is peer role,
listen-host authority, replication, or fail-closed multiplayer mutation. Content,
ordering, messages, rewards, and browser UX stay in the local Studio/rehearsal loop.

## Decision after the batch

Promote only edges that are both useful and legible in receipts. The likely next
cycle is one of:

- item specificity, if wildcard pickup/equip/consume is too vague;
- simple thresholds, if count/window and healing amount create useful rituals;
- compact alternatives, if authors need optional beats more than more signals;
- additional Grimoire `core` seams, selected from observed quest ideas rather than
  exposed wholesale.

Keep exact-world targeting, arbitrary branching, generalized variables, and new
multiplayer authority paths out of the fast lane until a concrete quest needs them.
They remain available as Advanced work, but they should not tax the default R&D lap.

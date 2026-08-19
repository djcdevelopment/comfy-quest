# Adaptive event fact inventory

This inventory governs Phase 3 adaptive predicates. A fact is admitted only when its
source, timestamp semantics, and failure behavior are explicit. New predicates remain
in Studio's extended/advanced surface until a live Event demonstrates observed need
for beginner-palette admission.

## Admitted temporal facts

| Measure | Authoritative source | Meaning | Missing-data behavior |
| --- | --- | --- | --- |
| `time_since_stage_entered` | `WorkflowProgress.stage_entered_utc` and the triggering `RuntimeEvent.at` | Whole seconds elapsed since the current stage was entered | Fails closed |
| `time_since_progress` | `WorkflowProgress.last_progress_utc` and the triggering `RuntimeEvent.at` | Whole seconds elapsed since an event last advanced the current trigger's structural event progress | Fails closed |

Both measures use event time, never wall-clock time. Runtime persists the two anchors
in `comfy-quest-workflow-state/v1`; older state files deserialize them as null and
therefore fail temporal thresholds closed. Structural event progress can establish a
missing progress anchor; entering a new stage establishes both anchors.

The first contract form is deliberately small: `THRESHOLD` accepts one registered
measure, comparison `gte`, and an integer number of seconds from 1 through 86400. A
transition must also contain an `EVENT`, keeping evaluation event-driven. `Explain`
reports the measured actual value beside the authored threshold.

## Admitted spatial facts

| Fact | Authoritative source | Meaning | Missing-data behavior |
| --- | --- | --- | --- |
| Witness position | `Player.m_localPlayer.transform.position`, read once at emission by the Runtime spatial observation seam and stamped onto the normalized event | Where the local player stood when the event was witnessed, rounded to 0.1 m | Events without a stamped position fail every player-subject spatial predicate closed |
| Binding position | The bound Charm's `ZDO.GetPosition()`, read at evaluation time | Where the bound Charm object currently stands | The `binding` anchor fails closed when unavailable |
| Tracked spawned-object positions | `SpawnExecutionStore` ownership records resolved to live ZDO positions at evaluation time; records whose spawned identity marks no longer match are excluded | Where this workflow's still-live authored spawns currently stand | `count_in_area` fails closed when no resolution was provided |
| Authored anchor positions | The experience document's own `anchors` list | Coordinates the creator wrote down and named | An `authored` reference to a missing anchor is rejected at compile time |

Positions are stamped by exactly one Runtime observation module and normalized (finite,
bounded, rounded to 0.1 m) at the shared privacy boundary. Distance is 3D Euclidean,
computed only in the pure Contracts `SpatialEvaluator` — binding discovery keeps no
radius and the pinned Bindings region keeps no distance code.

The contract form mirrors `THRESHOLD`: one `SPATIAL` trigger operator with a closed
predicate registry — `within_radius`, `entered`, `left`, `remained` (>= 1..86400
seconds), and `count_in_area` (>= 1..128 objects) — a required anchor, and a required
radius of 1..100 whole meters. A transition must still contain an `EVENT`. The
`player` anchor is admitted only for `count_in_area`; the other predicates already
take the player as their subject, so a player-relative area would be circular.

### Interpretation limits, stated deliberately

- `within_radius` and `remained` read the triggering event's own stamped position;
  they never substitute an older observation for "where the player is now".
- `entered` and `left` require an observed transition between two stamped positions
  inside the current trigger history. No observation, no transition.
- `remained` measures the trailing run of in-area observations ending at the
  triggering event. Two in-area observations bracket the interval between them; a
  teleport away and back inside one window would itself be observed, because
  `player_teleported` is a stamped normalized event. This is the same
  no-invented-continuity standard that keeps combat duration deferred.
- Spatial facts are evaluated fresh during pending-transition replay (binding and
  spawned positions are live facts, not history); the persisted event history keeps
  the original witness positions.

## Admitted encounter facts

| Measure | Authoritative source | Meaning | Missing-data behavior |
| --- | --- | --- | --- |
| `player_deaths_in_stage` | `WorkflowProgress.deaths_in_stage`, incremented when a normalized `player_died` event is appended to trigger history | Times the local player fell since the current stage was entered | Fails closed; state written before this measure existed deserializes as null |
| `spawned_enemies_remaining` | Spawn-ledger rows for one authored spawn action that still resolve to a live ZDO with matching identity marks | How many of that wave's staged objects are still present | Fails closed |
| `spawned_enemies_cleared` | That action's total ledger rows minus the rows that still resolve | How many of that wave's staged objects are gone | Fails closed |

Both spawn measures name their wave: the `THRESHOLD` carries an `action_id` referring to
an authored `spawn` action in the same document, rejected at compile time if it does not
exist. Total and live come from one ledger read, so a ledger that cannot be read supplies
no tally rather than an empty one, and an absent ledger reports zero staged — which fails
every `>= 1` predicate closed rather than reporting a wave triumphantly cleared.

The death counter is persisted because trigger history clears on every transition. It is
the minimum fact that must survive that clearing; it resets when the stage advances and
deliberately not when a same-stage transition completes.

Adding these measures added no new witness: `player_died` and `kill` were already in the
closed production catalog. Because a compiled document may count deaths without naming
the event in any clause, activation subscribes a measure's declared source event, or the
engine would drop it before evaluation.

### Interpretation limits, stated deliberately

- "No longer resolves" means the object was destroyed, **not** that the player walked
  away. This holds only because evaluation runs exclusively on the world authority
  (`CharmPolicy.CanMutate` admits solo and listen-host private worlds and denies
  dedicated servers and peer clients), and that process holds every ZDO whether or not
  its zone is loaded. If evaluation ever moves to a peer client, these two measures must
  be re-derived or withdrawn.
- A `kill` event carries no spawned-object identity, so no fact claims *who* removed a
  staged object. `spawned_enemies_cleared` counts absence, and says so.
- The `clear_spawned` action removes ledger rows, lowering staged and remaining together.
  An authored despawn is therefore not counted as a player victory.
- An event that arrives while a transition is still pending is discarded before it
  reaches history; the death counter inherits that behavior from every other event.
- Wave-clear *time* is not a measure. A route driven by the clearing event wins its race
  against a lower-priority timer route, and the elapsed time is reported on the receipt
  from `stage_entered_utc` and the deciding event time. No fact claims to know the moment
  the last object fell, because nothing observes it.

## Deferred facts

### Continuous combat duration

The current licensed game API inspection did not reveal an authoritative continuous
combat-session start or duration fact. The available combat timers describe isolated
actions or presentation timeouts; they do not establish that the player remained in
combat for the whole interval. Inferring duration from gaps between damage events
would fabricate continuity and is therefore rejected.

This is a current **can't answer why** item: Runtime can report individual normalized
combat events, but it cannot yet answer why a player has been continuously fighting
for a specified duration. Admit this measure only after a dedicated observation
module supplies a named, testable combat-state fact with explicit start, end, and
reload semantics.

### Per-wave elapsed time

`time_since_spawned` — seconds since one authored spawn action last staged its objects —
is deliberately not admitted. Every Phase 3.3 branch is expressible from stage-entry time
plus the counted facts above, and the measure would cost a persisted per-action timestamp
written from the engine. Admit it when a validation lap needs a wave that begins partway
through a stage, or several waves inside one stage, and not before.

### Cross-stage death totals

Only deaths in the current stage are admitted. A whole-quest total is a different fact
with a different reset rule; it waits for an authored quest that needs it.

### Positions of other players and unspawned creatures

Only the local witness, the bound Charm, and this workflow's own tracked spawns have
admitted positions. Ambient creatures, other players, and world objects have no
authoritative per-event position source yet; predicates over them are rejected rather
than approximated from scene scans.

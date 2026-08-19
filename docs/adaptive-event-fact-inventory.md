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

### Spatial and performance facts

Area anchors, radius membership, wave-clear time, deaths, and remaining enemies stay
deferred to their later Phase 3 milestones. They require their own authoritative
observation seams and do not borrow meaning from the temporal predicates above.

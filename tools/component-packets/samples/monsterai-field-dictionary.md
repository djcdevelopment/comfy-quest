# `MonsterAI` field dictionary

*Aggression, fleeing, sleeping, eating - senses and movement come from BaseAI below.*

Inheritance: `MonsterAI : BaseAI : MonoBehaviour`  
Source: extracted from `assembly_valheim.dll` 2026-08-01. Descriptions are AI-drafted
for editing; entries marked `(?)` are low-confidence guesses - verify before publishing.

## Declared by `MonsterAI` — 37 fields

| Field | Type | What it does (draft) |
|---|---|---|
| `m_onConsumedItem` | Action`1 | Action triggered when the monster successfully consumes an item. |
| `m_alertRange` | float | The distance at which the monster becomes alerted to threats. |
| `m_fleeIfHurtWhenTargetCantBeReached` | bool | Whether the monster flees if damaged and its target is unreachable. |
| `m_fleeUnreachableSinceAttacking` | float | Time after which the creature flees if its target remains unreachable while attacking. |
| `m_fleeUnreachableSinceHurt` | float | Time after which the creature flees if its target remains unreachable after being hurt. |
| `m_fleeIfNotAlerted` | bool | Whether the creature flees from danger when it is not currently alerted. |
| `m_fleeIfLowHealth` | float | Health percentage threshold below which the creature will start fleeing. |
| `m_fleeTimeSinceHurt` | float | How long the creature continues fleeing after being hurt. |
| `m_fleeInLava` | bool | Whether the creature flees when it enters lava. |
| `m_fleePheromoneMin` | float | Minimum value for the pheromone system used during fleeing behavior (?) |
| `m_fleePheromoneMax` | float | Maximum value for the pheromone system used during fleeing behavior (?) |
| `m_circulateWhileCharging` | bool | Whether the creature circles around its target while preparing an attack. |
| `m_circulateWhileChargingFlying` | bool | Whether the flying creature circles its target while preparing an attack. |
| `m_enableHuntPlayer` | bool | If true, the creature will actively hunt and track players down. |
| `m_attackPlayerObjects` | bool | Whether the creature will attack player-built structures and objects. |
| `m_privateAreaTriggerTreshold` | int | Number of private area triggers before the monster becomes hostile (?) |
| `m_interceptTimeMax` | float | Maximum time the creature spends trying to intercept its moving target. |
| `m_interceptTimeMin` | float | Minimum time the creature spends trying to intercept its moving target. |
| `m_maxChaseDistance` | float | Maximum distance the creature will chase a target before giving up. |
| `m_minAttackInterval` | float | Minimum time the creature must wait between consecutive attacks. |
| `m_circleTargetInterval` | float | Time interval between decisions to circle around the target. |
| `m_circleTargetDuration` | float | How long the creature circles its target before attacking or moving. |
| `m_circleTargetDistance` | float | The distance the creature maintains while circling its target. |
| `m_sleeping` | bool | Whether the creature spawns in a sleeping state. |
| `m_wakeupRange` | float | The range at which the creature wakes up from sleep due to players. |
| `m_noiseWakeup` | bool | Whether nearby noises can wake the creature from sleep. |
| `m_maxNoiseWakeupRange` | float | Maximum range at which noise can wake up the sleeping creature. |
| `m_wakeupEffects` | EffectList | Visual and audio effects played when the creature wakes up. |
| `m_sleepEffects` | EffectList | Visual and audio effects played while the creature is sleeping. |
| `m_wakeUpDelayMin` | float | Minimum delay in seconds before the creature fully wakes up. |
| `m_wakeUpDelayMax` | float | Maximum delay in seconds before the creature fully wakes up. |
| `m_fallAsleepDistance` | float | Distance from players at which the creature can fall asleep. |
| `m_avoidLand` | bool | Whether the creature actively avoids moving onto land. |
| `m_consumeItems` | List`1 | List of items that the creature is able to eat. |
| `m_consumeRange` | float | Distance from an item at which the creature can eat it. |
| `m_consumeSearchRange` | float | Distance within which the creature will search for food to eat. |
| `m_consumeSearchInterval` | float | How often the creature searches for nearby food items. |

## Declared by `BaseAI` *(inherited)* — 42 fields

| Field | Type | What it does (draft) |
|---|---|---|
| `m_onBecameAggravated` | Action`1 | Action triggered when a passive-aggressive creature becomes hostile. |
| `m_viewRange` | float | The maximum distance the creature can see targets. |
| `m_viewAngle` | float | The field of view angle for the creature's vision. |
| `m_hearRange` | float | The maximum distance at which the creature can hear noises. |
| `m_mistVision` | bool | Whether the creature can see through mist or fog unaffected. |
| `m_alertedEffects` | EffectList | Visual and audio effects played when the creature becomes alerted. |
| `m_idleSound` | EffectList | Sound effects played periodically while the creature is idle. |
| `m_idleSoundInterval` | float | Time interval between periodic idle sound plays. |
| `m_idleSoundChance` | float | The percentage chance for an idle sound to play each interval. |
| `m_pathAgentType` | AgentType | The pathfinding agent type, determining how it navigates the world. |
| `m_moveMinAngle` | float | Minimum angle to target required before the creature starts moving forward. |
| `m_smoothMovement` | bool | Whether the creature's movement pathing and turning are smoothed out. |
| `m_serpentMovement` | bool | Enables swimming snake-like movement physics and animations. |
| `m_serpentTurnRadius` | float | The turning radius limit for serpent-like swimming movement. |
| `m_jumpInterval` | float | Cooldown interval between random jumps during movement. |
| `m_randomCircleInterval` | float | How often the creature chooses a new random direction to circle. |
| `m_randomMoveInterval` | float | How often the creature chooses a new random point to idle-walk. |
| `m_randomMoveRange` | float | The maximum distance the creature will wander during random idle movement. |
| `m_randomFly` | bool | Whether the creature wanders randomly through the air. |
| `m_chanceToTakeoff` | float | The periodic chance for a flying creature on the ground to takeoff. |
| `m_chanceToLand` | float | The periodic chance for a flying creature to land on the ground. |
| `m_groundDuration` | float | Average time a flying creature spends on the ground before taking off. |
| `m_airDuration` | float | Average time a flying creature spends in the air before landing. |
| `m_maxLandAltitude` | float | Maximum altitude at which a flying creature is allowed to land. |
| `m_takeoffTime` | float | How long the takeoff sequence takes before the creature is considered airborne. (?) |
| `m_flyAltitudeMin` | float | Minimum altitude relative to the ground the creature maintains while flying. |
| `m_flyAltitudeMax` | float | Maximum altitude relative to the ground the creature maintains while flying. |
| `m_flyAbsMinAltitude` | float | The absolute minimum altitude above sea level the creature must maintain. (?) |
| `m_avoidFire` | bool | Whether the creature pathfinds around fires to avoid taking damage. |
| `m_afraidOfFire` | bool | Whether the creature actively flees when near any fire source. |
| `m_avoidWater` | bool | Whether the creature avoids pathfinding through deep water. |
| `m_avoidLava` | bool | Whether the creature actively avoids stepping into lava. |
| `m_skipLavaTargets` | bool | Whether the creature ignores targets that are standing inside lava. |
| `m_avoidLavaFlee` | bool | Whether the creature avoids pathfinding through lava when fleeing. |
| `m_aggravatable` | bool | Whether a passive creature can be angered by attacking it. |
| `m_passiveAggresive` | bool | Whether the creature only attacks players who attack them first. |
| `m_spawnMessage` | string | The shout or screen message displayed when this creature spawns. |
| `m_deathMessage` | string | The shout or screen message displayed when this creature dies. |
| `m_alertedMessage` | string | The shout or screen message displayed when this creature becomes alerted. |
| `m_fleeRange` | float | The distance the creature tries to maintain from threats when fleeing. |
| `m_fleeAngle` | float | The angle relative to the threat used to calculate fleeing paths. |
| `m_fleeInterval` | float | How often the creature recalculates its fleeing path away from threats. |

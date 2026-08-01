# `Humanoid` field dictionary

*Equipment and blocking - but the famous stats (health, speed) come from Character below.*

Inheritance: `Humanoid : Character : MonoBehaviour`  
Source: extracted from `assembly_valheim.dll` 2026-08-01. Descriptions are AI-drafted
for editing; entries marked `(?)` are low-confidence guesses - verify before publishing.

## Declared by `Humanoid` — 15 fields

| Field | Type | What it does (draft) |
|---|---|---|
| `m_blockStaminaDrain` | float | Stamina cost when successfully blocking an attack. |
| `m_perfectBlockStaminaDrain` | float | Stamina cost when executing a timed perfect block. |
| `m_perfectBlockStatusEffect` | StatusEffect | Status effect applied to the attacker upon a perfect block. |
| `m_defaultItems` | GameObject[] | Array of default item prefabs the character spawns with. |
| `m_randomWeapon` | GameObject[] | Pool of weapon prefabs chosen randomly on spawn. |
| `m_randomArmor` | GameObject[] | Pool of armor prefabs chosen randomly on spawn. |
| `m_randomShield` | GameObject[] | Pool of shield prefabs chosen randomly on spawn. |
| `m_randomSets` | ItemSet[] | Sets of items the character can randomly equip on spawn. |
| `m_randomItems` | RandomItem[] | Individual items that have a chance to spawn with the character. |
| `m_unarmedWeapon` | ItemDrop | Default weapon prefab used when the character has no weapon equipped. |
| `m_pickupEffects` | EffectList | Visual and audio effects played when picking up an item. |
| `m_dropEffects` | EffectList | Visual and audio effects played when dropping an item. |
| `m_consumeItemEffects` | EffectList | Visual and audio effects played when consuming food or potions. |
| `m_equipEffects` | EffectList | Visual and audio effects played when equipping an item. |
| `m_perfectBlockEffect` | EffectList | Visual and audio effects played during a perfect block. |

## Declared by `Character` *(inherited)* — 75 fields

| Field | Type | What it does (draft) |
|---|---|---|
| `m_nViewOverride` | ZNetView | Overrides the default ZNetView component for networking. (?) |
| `m_onDamaged` | Action`2 | Callback triggered whenever the character takes damage. (?) |
| `m_onDeath` | Action | Callback triggered when the character dies. (?) |
| `m_onLevelSet` | Action`1 | Callback triggered when the character's star level changes. (?) |
| `m_onLand` | Action`1 | Callback triggered when the character lands on the ground. (?) |
| `m_name` | string | The display name of the character shown in-game. |
| `m_group` | string | Group identifier used for faction and AI social behaviors. (?) |
| `m_faction` | Faction | The faction determining friend-or-foe relationships with other characters. |
| `m_boss` | bool | Whether this character is classified as a boss. |
| `m_dontHideBossHud` | bool | Prevents the boss health bar from hiding when far away. (?) |
| `m_bossEvent` | string | The raid event associated with this boss character. (?) |
| `m_defeatSetGlobalKey` | string | Global world key unlocked when this character is defeated. |
| `m_aiSkipTarget` | bool | If true, AI enemies will ignore this character. (?) |
| `m_crouchSpeed` | float | Movement speed of the character while crouching. |
| `m_walkSpeed` | float | Movement speed of the character while walking. |
| `m_speed` | float | Base movement speed of the character. |
| `m_turnSpeed` | float | How quickly the character can rotate or turn. |
| `m_runSpeed` | float | Movement speed of the character while running. |
| `m_runTurnSpeed` | float | How quickly the character can turn while running. |
| `m_flySlowSpeed` | float | Base speed of the character when flying slowly. |
| `m_flyFastSpeed` | float | Base speed of the character when flying quickly. |
| `m_flyTurnSpeed` | float | How quickly the character can turn while flying. |
| `m_acceleration` | float | How fast the character reaches their maximum speed. |
| `m_jumpForce` | float | Upward physics force applied when jumping. |
| `m_jumpForceForward` | float | Forward physics force applied when jumping. |
| `m_jumpForceTiredFactor` | float | Reduces jump height when the character is out of stamina. |
| `m_airControl` | float | How much movement control the character has mid-air. |
| `m_canSwim` | bool | Determines whether the character can swim in water. |
| `m_swimDepth` | float | Water depth required to transition into swimming state. |
| `m_swimSpeed` | float | Movement speed of the character while swimming. |
| `m_swimTurnSpeed` | float | How quickly the character can turn while swimming. |
| `m_swimAcceleration` | float | How fast the character accelerates while swimming. |
| `m_groundTilt` | GroundTiltType | Determines how the model tilts to match sloped terrain. |
| `m_groundTiltSpeed` | float | How fast the model adjusts its tilt to slopes. |
| `m_flying` | bool | Determines if the character is currently flying. (?) |
| `m_jumpStaminaUsage` | float | Stamina cost for performing a jump. |
| `m_disableWhileSleeping` | bool | Disables updates or AI routines while character is sleeping. (?) |
| `m_eye` | Transform | Transform used for line-of-sight and targeting checks. |
| `m_hitEffects` | EffectList | Effects played when taking normal damage. |
| `m_critHitEffects` | EffectList | Effects played when taking critical hit damage. |
| `m_backstabHitEffects` | EffectList | Effects played when hit from behind. |
| `m_deathEffects` | EffectList | Effects played when the character dies. |
| `m_waterEffects` | EffectList | Effects played when interacting with water. |
| `m_tarEffects` | EffectList | Effects played when covered in tar. |
| `m_slideEffects` | EffectList | Effects played when sliding down steep slopes. |
| `m_jumpEffects` | EffectList | Effects played when performing a jump. |
| `m_flyingContinuousEffect` | EffectList | Continuous effects played while character is flying. |
| `m_pheromoneLoveEffect` | EffectList | Effects played when tamed or in love. (?) |
| `m_useAltStatusEffectScaling` | bool | Enables alternative scaling calculations for status effects. (?) |
| `m_tolerateWater` | bool | If false, character takes damage in water. |
| `m_tolerateFire` | bool | If true, prevents damage from fire. (?) |
| `m_tolerateSmoke` | bool | If true, prevents suffocation damage from smoke. |
| `m_tolerateTar` | bool | If true, character is immune to tar slow. (?) |
| `m_health` | float | Maximum health pool of the character. |
| `m_regenAllHPTime` | float | Time in seconds to regenerate full health. (?) |
| `m_damageModifiers` | DamageModifiers | Damage type resistances and weaknesses. |
| `m_weakSpots` | WeakSpot[] | Sub-objects where attacks deal extra damage. (?) |
| `m_staggerWhenBlocked` | bool | Staggers this character if their attack is blocked. |
| `m_staggerDamageFactor` | float | Percentage of max health needed in one hit to stagger. (?) |
| `m_enemyAdrenalineMultiplier` | float | Multiplies stats when enemies are nearby. (?) |
| `m_heatBuildupBase` | float | Base rate of heat accumulation. (?) |
| `m_heatCooldownBase` | float | Base rate of heat dissipation. (?) |
| `m_heatBuildupWater` | float | Heat buildup rate while in water. (?) |
| `m_heatWaterTouchMultiplier` | float | Heat cooldown multiplier when touching water. (?) |
| `m_lavaDamageTickInterval` | float | Time between damage ticks when in lava. (?) |
| `m_heatLevelFirstDamageThreshold` | float | Heat level threshold before damage begins. (?) |
| `m_lavaFirstDamage` | float | Initial damage taken upon entering lava. (?) |
| `m_lavaFullDamage` | float | Continuous damage taken when deep in lava. (?) |
| `m_lavaAirDamageHeight` | float | Height limit above lava where heat damage still occurs. (?) |
| `m_dayHeatGainRunning` | float | Heat accumulation rate while running in daytime. (?) |
| `m_dayHeatGainStill` | float | Heat accumulation rate while standing in daytime. (?) |
| `m_dayHeatEquipmentStop` | float | How much equipped gear blocks heat buildup. (?) |
| `m_lavaSlowMax` | float | Maximum movement slow applied by lava. (?) |
| `m_lavaSlowHeight` | float | Lava depth required to apply maximum slow. (?) |
| `m_lavaHeatEffects` | EffectList | Effects played when suffering heat damage from lava. (?) |

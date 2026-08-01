# `WearNTear` field dictionary

*Health, structural support, weathering, and damage visuals.*

Inheritance: `WearNTear : MonoBehaviour`  
Source: extracted from `assembly_valheim.dll` 2026-08-01. Descriptions are AI-drafted
for editing; entries marked `(?)` are low-confidence guesses - verify before publishing.

## Declared by `WearNTear` — 28 fields

| Field | Type | What it does (draft) |
|---|---|---|
| `m_onDestroyed` | Action | Event action triggered when this piece is destroyed. |
| `m_onDamaged` | Action | Event action triggered when this piece takes damage. |
| `m_new` | GameObject | The visual model representing the undamaged state. |
| `m_worn` | GameObject | The visual model representing the damaged state. |
| `m_broken` | GameObject | The visual model representing the nearly broken state. |
| `m_wet` | GameObject | Visual model overlay applied when the piece is wet. |
| `m_noRoofWear` | bool | Disables deterioration when exposed to rain without roof cover. |
| `m_noSupportWear` | bool | Disables damage and collapse caused by lack of structural support. |
| `m_ashDamageImmune` | bool | Makes this piece immune to Ashlands environmental fire damage. (?) |
| `m_ashDamageResist` | bool | Gives this piece resistance to Ashlands environmental damage. (?) |
| `m_burnable` | bool | Enables this piece to catch fire and take burn damage. |
| `m_materialType` | MaterialType | The material category, determining structural support values. |
| `m_supports` | bool | Allows this piece to support other structural pieces. |
| `m_comOffset` | Vector3 | Offset for the center of mass calculation. (?) |
| `m_forceCorrectCOMCalculation` | bool | Forces high-precision center of mass calculations. (?) |
| `m_staticPosition` | bool | Prevents physics forces from moving this object. (?) |
| `m_nonSolidRenderers` | List`1 | Renderers that do not affect physical collisions. (?) |
| `m_health` | float | The maximum durability or health of the structure. |
| `m_damages` | DamageModifiers | Custom damage resistances and weaknesses for this piece. |
| `m_minToolTier` | int | Minimum tool tier required to damage or dismantle this. |
| `m_hitNoise` | float | The noise level generated when this piece is struck. |
| `m_destroyNoise` | float | The noise level generated when this piece is destroyed. |
| `m_triggerPrivateArea` | bool | Triggers ward alerts if this piece is damaged inside one. (?) |
| `m_destroyedEffect` | EffectList | Visual and audio effects triggered upon destruction. |
| `m_hitEffect` | EffectList | Visual and audio effects triggered when struck. |
| `m_switchEffect` | EffectList | Effects played when switching between wear states. (?) |
| `m_autoCreateFragments` | bool | Automatically spawns broken fragments when destroyed. |
| `m_fragmentRoots` | GameObject[] | Object roots used to generate physics debris fragments. |

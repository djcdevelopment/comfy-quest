# `Piece` field dictionary

*Placement rules, build-menu identity, and crafting costs. Lives on every buildable.*

Inheritance: `Piece : StaticTarget : MonoBehaviour`  
Source: extracted from `assembly_valheim.dll` 2026-08-01. Descriptions are AI-drafted
for editing; entries marked `(?)` are low-confidence guesses - verify before publishing.

## Declared by `Piece` — 51 fields

| Field | Type | What it does (draft) |
|---|---|---|
| `m_targetNonPlayerBuilt` | bool | Allows enemies to target this piece even if not player-built. (?) |
| `m_icon` | Sprite | The icon displayed for this piece in the build menu. |
| `m_name` | string | The display name of the piece. |
| `m_description` | string | The description text shown in the build menu. |
| `m_enabled` | bool | Determines if this piece is available to be built. |
| `m_category` | PieceCategory | The build menu tab category this piece belongs to. |
| `m_isUpgrade` | bool | If true, this piece acts as a crafting station upgrade. |
| `m_comfort` | int | The amount of comfort value this piece provides to players. |
| `m_comfortGroup` | ComfortGroup | Prevents comfort stacking with other items in the same group. |
| `m_comfortObject` | GameObject | The specific child object that defines the source of comfort. (?) |
| `m_groundPiece` | bool | Forces the piece to require solid ground contact to exist. |
| `m_allowAltGroundPlacement` | bool | Allows placing the piece on imperfect or uneven ground. (?) |
| `m_groundOnly` | bool | Restricts placement so the piece must sit directly on ground. |
| `m_cultivatedGroundOnly` | bool | Restricts placement so the piece must sit on cultivated soil. |
| `m_waterPiece` | bool | Allows the piece to be built in or on water. |
| `m_clipGround` | bool | Allows the piece to partially clip into the ground. |
| `m_clipEverything` | bool | Allows the piece to clip through any other objects. |
| `m_noInWater` | bool | Prevents the piece from being built in water. |
| `m_notOnWood` | bool | Prevents the piece from being placed on wood structures. |
| `m_notOnTiltingSurface` | bool | Prevents the piece from being placed on sloped surfaces. |
| `m_inCeilingOnly` | bool | Restricts placement so the piece must hang from ceilings. |
| `m_notOnFloor` | bool | Prevents the piece from being placed on flat floors. |
| `m_noClipping` | bool | Prevents this piece from clipping into other structures. |
| `m_onlyInTeleportArea` | bool | Allows placing this piece only inside active teleport areas. (?) |
| `m_allowedInDungeons` | bool | Allows players to build this piece inside dungeons. |
| `m_spaceRequirement` | float | The clear clearance radius required around the piece to build. |
| `m_repairPiece` | bool | If true, this piece represents the hammer's repair action. (?) |
| `m_removePiece` | bool | If true, this piece represents the hammer's deconstruct action. (?) |
| `m_canRotate` | bool | Allows players to rotate the piece before building it. |
| `m_randomInitBuildRotation` | bool | Randomizes the rotation of the piece when it is built. |
| `m_canBeRemoved` | bool | Allows players to dismantle and remove the placed piece. |
| `m_canRockJade` | bool | Enables rocking or swaying physics behavior for the piece. (?) |
| `m_allowRotatedOverlap` | bool | Allows the piece to overlap others when rotated. (?) |
| `m_vegetationGroundOnly` | bool | Restricts placement to ground covered by vegetation. (?) |
| `m_blockingPieces` | List`1 | List of other pieces that cannot be built near this. |
| `m_blockRadius` | float | The exclusion radius where blocking pieces cannot be built. |
| `m_mustConnectTo` | ZNetView | The target network object this piece must connect to. (?) |
| `m_connectRadius` | float | The search radius used to find the required connection object. |
| `m_mustBeAboveConnected` | bool | Forces this piece to be built above its connected object. |
| `m_noVines` | bool | Prevents decorative vines from growing on this piece. (?) |
| `m_extraPlacementDistance` | int | Extends the player's build reach distance for this piece. |
| `m_onlyInBiome` | Biome | Restricts building this piece to specific game biomes. |
| `m_harvest` | bool | Enables harvesting interactions on this piece. (?) |
| `m_harvestRadius` | float | The interaction radius for harvesting this piece. (?) |
| `m_harvestRadiusMaxLevel` | float | The maximum harvest radius at highest upgrade level. (?) |
| `m_placeEffect` | EffectList | Visual and audio effects triggered upon placing this piece. |
| `m_dlc` | string | Restricts construction to players owning the specified DLC. |
| `m_craftingStation` | CraftingStation | The nearby crafting station required to build this piece. |
| `m_returnResourceHeightOffset` | float | Height offset where refunded building materials are spawned. |
| `m_resources` | Requirement[] | The list of item materials required to craft this. |
| `m_destroyedLootPrefab` | GameObject | Prefab containing items dropped when this piece is destroyed. |

## Declared by `StaticTarget` *(inherited)* — 2 fields

| Field | Type | What it does (draft) |
|---|---|---|
| `m_primaryTarget` | bool | Marks this piece as a high-priority target for enemies. |
| `m_randomTarget` | bool | Marks this piece as a random target for enemy attacks. |

# ComfyQuestRuntime

Small gameplay-side consumer for certified `comfy-quest-experience/v1` documents. It owns explicit
inbox checking and atomic activation; it does not watch files and never executes schema-1 quests.
The current shell exposes an F9 compact drawer, explicit F10 Check and F11 Load latest, immutable JSON
receipts, and a fixed Open Studio button. The drawer previews a bounded aimed target and can inscribe
five namespaced references onto a locally owned creator-built object after explicit private-world
confirmation. A one-stage `kill` or exact-bound-object `piece_damaged` to `message` executor uses a durable
world/player/ZDO/content/stage/transition/action claim to suppress duplicates and restart replays. OMEN has
live-witnessed aim, inscription, message execution, duplicate suppression, explicit hot loading, selected
version activation, and hash-verified atomic rollback.

Durable multi-stage progress is keyed by world, character, binding ZDO, and content hash. Transitions remain
pending across reloads until their exactly-once actions are processed; the drawer reports current
stage/outcome. Two-stage sign editing and restart-safe engine-owned timers are automated and live-proven.

The F9 drawer follows the Quest Lab F6 visual language while remaining a separate compact product
surface: opaque dark hierarchy, section headers, readable status rows and hashes, title-bar dragging,
Escape close, and distinct Active Content, Charm, and Experience sections. Multiple OMEN usability passes
established the final two-press backquote CHECK/CAST workflow and bounded outcome log.

The second visual candidate prioritizes the normal operator path: a green READY/INSCRIBE affordance first,
then a numbered Look/Validate/Load/Confirm update workflow driven by one context-sensitive button. Loading
still requires its own explicit click. Version selection and rollback are collapsed into maintenance at the
bottom, and every window interaction state retains the dark background.

While F9 is open, configurable backquote (`` ` ``) is a two-press CHECK/CAST gesture. CHECK captures the exact aimed
ZDO; CAST revalidates and inscribes that captured identity, independent of later cursor movement. Rejected
targets stay in CHECK mode, and the drawer retains the latest 20 timestamped capture/outcome rows. Middle
mouse and Ctrl+Space are deliberately untouched because Valheim uses them for secondary attacks and roll.

The executor implements a closed mutation registry: capped allowlisted item grants and bounded
creature/item/piece spawns. Every spawned ZDO is durably recorded and namespaced with its content hash,
action ID, and full action identity; `clear_spawned` deletes only records whose live marks still match.
All mutation remains gated to an explicitly confirmed private solo/listen-host world.

Live native multiplayer acceptance passed on 2026-08-10: OMEN hosted an ordinary private listen world and
executed the 1.5.0 message, one-Wood grant, marked floor spawn, timer, marked-only cleanup, and terminal
transition. i5 joined through Steam Friends, activated the identical certified content hash, rejected Charm
mutation with `mutation_authority_unavailable`, and executed zero actions. The deployment payload must include
`ComfyQuestRuntime.dll`, `ComfyQuestContracts.dll`, and the pinned `Newtonsoft.Json.dll`; the peer harness
hash-verifies all three.

Inbox: `BepInEx/config/comfy-quest-runtime/inbox/*.questpack`.

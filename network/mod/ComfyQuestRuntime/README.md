# ComfyQuestRuntime

Small gameplay-side consumer for certified `comfy-quest-experience/v2` documents. It owns explicit
inbox checking and atomic activation; it does not watch files and never executes schema-1 quests.
The current shell exposes an always-present overhead creator bar, explicit F10 Check and F11 Load latest,
immutable JSON receipts, and a configurable loopback Open Studio button (default
`127.0.0.1:8085`). F9 changes the bar between a 36-pixel compact state and a named, actionable
116-pixel state. Its details popover previews a bounded aimed target and can inscribe
five namespaced references onto a locally owned creator-built object after explicit private-world
confirmation. A one-stage `kill` or exact-bound-object `piece_damaged` to `message` executor uses a durable
world/player/ZDO/content/stage/transition/action claim to suppress duplicates and restart replays. OMEN has
live-witnessed aim, inscription, message execution, duplicate suppression, explicit hot loading, selected
version activation, and hash-verified atomic rollback.

Runtime publication and inbox inspection now use the generated production registry rather than the
larger research vocabulary. The current boundary is all thirty-four creator-safe canonical events plus the separately declared
timer/chat engine events. Each patch is registered against an exact generated signature and fails
independently when a game build moves that overload. Local/RPC healing witnesses are correlated before
workflow history. The Core local-actions cohort adds proven-result container emptying, item unequipping,
piece destruction/removal/repair, and successful local teleport; request, access, full-health, stale,
partial-transfer, and other no-op paths do not advance a quest. Unknown fields are stripped at the privacy
boundary, and events absent from the active
document are rejected before binding discovery or receipt writes. The active compiled document and a
short-lived binding index are cached; package timestamp/length changes invalidate that cache and force the
full hash/certification path again. Catalog rows marked `automated-contract` are implementation evidence,
not a new live-gameplay claim.

Progression witnesses publish only proven local-player changes: maximum health and stamina use their
observed deltas, skill events compare raw level/accumulator state, and death requires the living-to-dead
ZDO transition. Global-key witnesses require server authority and compare exact before/after membership,
so duplicate sets and missing-key removals remain no-ops. Nested overload and RPC routes are correlated by
the shared action-deduplication boundary.

The Combat + Harvest cohort adds successful local blocks, freshly attributed staggers, actual creature
health loss, authoritative resource health mutation, and owner-side resource picking. Creature targets use
the matchable localization token; resources use clone-free prefab names. Only weapon skill and projectile
classification cross the event boundary, and rejected/no-op damage or pick requests never advance a quest.

Durable multi-stage progress is keyed by world, character, binding ZDO, and content hash. Transitions remain
pending across reloads until their exactly-once actions are processed; the creator details report current
stage/outcome. Two-stage sign editing and restart-safe engine-owned timers are automated and live-proven.

The overhead bar follows the Quest Lab F6 visual language while remaining a separate product surface.
Its clamped 92-pixel safe top clears the host diagnostic band proven at the live 1026x740 viewport.
Its compact state keeps four workflow dots, active revision, CHECK/READY/LANDED, and the F9 affordance
visible without taking the player out of the world. Expansion names Look/Validate/Load/Confirm, joins
the rungs with state rails, and exposes the contextual Check/Play action, session-only dev arm, Studio
handoff, and one details popover. Multiple OMEN usability passes established the final two-press
backquote CHECK/CAST workflow and bounded outcome log.

Deadlines and present-tense warnings share one scaled, clamped alert anchor. The creator can drag it
while the bar is expanded; Runtime stores X/Y fractions. Actionable warnings carry stable condition
keys and disappear from both the anchor and evidence feed when their source reports that the condition
has cleared. Copy is never parsed to infer state.

Expanding F9 also enables client-local **Arcane Sight**. Every valid Runtime Charm binding in the
currently loaded scene receives a temporary glow and an on-screen label with its experience,
version, active/older-content state, local/remote ownership, and player distance. Minimizing F9
restores the prior renderer property blocks and removes the temporary lights; Arcane Sight never
writes a ZDO or changes event routing. CHECK's fallback aim ray is bounded to 10 metres. Ambient
quest events have no fixed metre radius: Runtime currently considers every matching, locally
owned binding present in the loaded `WearNTear` instance set, and the details popover says so explicitly.
"No fixed radius" describes binding discovery only: authored event predicates may evaluate
spatial relationships. A reviewed `SPATIAL` trigger clause compares stamped witness positions
against an authored anchor and radius in the pure Contracts evaluator, without ever filtering
which bindings participate.

The expanded bar prioritizes the normal creator path: a four-rung Look/Validate/Load/Confirm workflow
driven by one context-sensitive button. Loading still requires its own explicit click. Version selection,
capture history, Arcane Sight controls, and rollback recede behind the maintenance disclosure.

While F9 is expanded, configurable backquote (`` ` ``) is a two-press CHECK/CAST gesture. CHECK captures the exact aimed
ZDO; CAST revalidates and inscribes that captured identity, independent of later cursor movement. Rejected
targets stay in CHECK mode, and the details popover retains the latest 20 timestamped capture/outcome rows. Middle
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

The first genuinely cooperative Runtime transition was live-proven on 2026-08-12 in a native OMEN
listen-host world with i5 as the Steam Friends peer. Quest Lab on OMEN observed an i5 Shout exactly once
as `chat_received / shout / actor peer`, with the message text redacted. Peer-local sign edits, building,
portal travel, and inventory actions did not surface through the current host Lab seams. Pack 1.6 therefore
used the proven boundary: an i5 Shout advanced the bound sign from `await-peer-shout` to
`await-host-sign`; one OMEN sign placement completed the experience. No AM4, Gateway, Lumberjacks, or
NetworkSense execution participated.

Standalone Quest Studio then authored, certified, and published a three-stage 1.7.0 successor: peer
Shout, listen-host sign placement, and local sign inscription. OMEN explicitly activated content hash
`374c43056f479089fca1faf680a3a074b55db0bcc098884b5c212cce0118bab1` through F10/F11. The unchanged
peer-Shout adapter was not replayed a second time; its expectation is inherited from the direct 1.6 proof.

Inbox: `BepInEx/config/comfy-quest-runtime/inbox/*.questpack`.

### Dedicated-server personal progression (beta)

Native Valheim networking remains the transport. A remote client may opt into durable,
per-character progression without asking the server or a listen host to mutate quest state by
pinning one exact world UID and one exact active pack content hash:

```ini
[DedicatedPersonalProgression]
Enabled = true
WorldUid = -7600395338659582326
ContentHash = <64 lowercase hex characters from active-set.json>
```

This profile fails closed unless the player is a peer, the loaded world UID and active content
hash match exactly, and every campaign trigger/action stays inside the beta policy. The only live
progression witness admitted is a kill attributed to the local player; the synthetic
`experience_started` event may initialize a run. Every entry or transition action must be
`message`. Grants, spawns, timers, cleanup, and every other shared-world mutation are rejected.
Bindings are read from the replicated world as exact, read-only references: the peer does not
inscribe, continue, or otherwise rewrite their ZDOs. Run state, action claims, continuation
lineage, receipts, and messages remain local to the player and are keyed by world, character,
binding, campaign experience, and content hash.

Leaving `Enabled = false` preserves the existing host-authoritative behavior. Treat the three
values as one release pin; changing a world or campaign requires distributing a new reviewed
configuration rather than relaxing the match.

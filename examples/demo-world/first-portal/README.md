# Demo World: First Portal

This is Comfy Quest's minimal public creator-loop tutorial: one production event,
one visible effect, and one terminal transition.

`studio-project.json` is the stable Studio schema-v3 source. `experience.json` is
the certified `comfy-quest-experience/v1` document compiled from the same graph.
`demo-world-first-portal-1.0.0.questpack` is the deterministic Runtime package;
its internal manifest is `comfy-quest-pack/v2`. It is not the legacy Quest Lab
schema-v1 format. `expected.json` pins the activation and gameplay receipts that
constitute a successful lap.

## Public creator loop

1. Open Quest Studio and choose **Import project JSON**.
2. Select `studio-project.json`. Studio validates it and opens a new local fork.
3. With Runtime running, open its F9 drawer and arm the dev channel once, then
   choose **Play this revision** in Studio. Confirm the active pack and experience
   IDs are the new fork IDs shown by Studio.
4. Take the unavoidable ascent portal from the ground welcome camp into the Creator
   Hub. The fork has no matching Charm yet, so this teleport is intentionally
   unbound and does not advance the quest.
5. Import intentionally creates new project, pack, and experience IDs. That makes
   the draft safe to edit without overwriting the canonical artifact, but it also
   means an existing canonical Demo World Charm cannot auto-rebind to the fork.
   Use the immediately visible illuminated **CAST HERE** First Portal tutorial sign
   on the hub's inner arrival edge. Close F9, aim Valheim's fixed center crosshair
   at the sign face, reopen F9 without moving the camera, and press backtick once
   to CHECK, then again to CAST the active fork. The drawer's movable mouse cursor
   does not select world targets. In the generated plan the sign is at relative
   `(x=3.5, y=1.7, z=6, yaw=180)` from the gallery floor origin. Expect
   `bind/inscribed` after the initial
   `dev_rebind/skipped` receipt with `no_loaded_binding`.
6. Take the paired portal at the World school. This is the first portal after the
   quest is bound. The trigger is targetless `player_teleported`; Runtime
   shows "The First Portal answers. Your quest is complete." and completes the
   workflow.

Later Play operations for the same fork keep its pack and experience IDs, so the
rebind step is automatic: changed compiled content yields `dev_rebind/rebound`,
while unchanged content yields `dev_rebind/skipped` with `already_current`. A fresh
CAST is not required for each edited revision.

The first-fork proof chain is `dev_transfer/accepted`,
`dev_validation/accepted`, `dev_activation/activated`, the explicit CAST receipt,
then `event/matched`, `action/executed`, and `transition/complete`. Correlation,
activation, world, character, and binding identities are run-specific and are not
fixed in this portable example.

## Direct Runtime artifact

Copy the checked-in questpack as an opaque artifact directly into
`BepInEx/config/comfy-quest-runtime/inbox/`, press F10 to check it, then use the F9
maintenance list to select and load the exact `demo-world-first-portal` pack at
`1.0.0`. Do not use F11 for this acceptance step: F11 chooses the globally highest
semantic version and a nonempty inbox may contain another pack. Confirm the active
pack and experience IDs are both exactly `demo-world-first-portal`. If no exact
canonical binding is already present, the ascent is unbound; once upstairs, CHECK
and CAST the canonical experience onto the generated `marble-grand` **CAST HERE**
tutorial sign identified above before taking the World school's paired portal.
For editing, use the Studio import-and-Play loop above; its fork needs
one fresh CAST, and subsequent same-fork revisions auto-rebind.

A world save with that sign already bound to the exact canonical IDs may complete
on the unavoidable ascent portal, because the trigger is targetless. This repository
does not claim that a matching prebound Demo World save is live-proven, so the
portable acceptance path does not depend on it.

Restoring only the same world files does not guarantee a replay. Runtime completion
is durable in `BepInEx/config/comfy-quest-runtime/state/workflow-states.json` and is
keyed by world, character, binding, and content identity. Reusing all four can
resurrect the completed workflow. Use a demonstrably fresh identity for a repeated
acceptance lap; this tutorial does not claim that a scoped Runtime reset is
live-proven.

`manifest.json` records exact bytes and SHA-256 values for every paired artifact.
The repository contract tests recompile the built-in template, validate the
questpack through the production Runtime v2 contract, and fail on drift.

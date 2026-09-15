# Photography in Creator/DM

Creator/DM includes a **Capture** mode beside its gameplay tools. It opens Steward's
shared archived-world composer with the photograph as a reference thumbnail. Lens,
frame, output size, position/aim, independent Outside view, coverage and reset all use
the same component as the public gallery. Returning to gameplay and back preserves
the composition.

The archive scene is read-only and is selected by the photograph's world identity.
Gameplay targeting retains its existing Creator Session proof and authoring limits.
Photography does not select live Runtime targets or change the active game world.

## Use Capture mode

Start Studio with its configured Steward scene origin, open Creator/DM and choose
**Capture**. Select an archived build and photograph with Gallery; the photograph
remains visible as the reference. Lens view edits the output camera with mouse look,
WASD/QE and Shift. Outside view (`V`) orbits an independent observer, shows the
camera and player-scale marker, and offers position and aim handles or geometry
picks. Position / Aim also accepts numeric X, elevation and Z. Lens chooses 90°,
65° or 35° vertical FOV; Frame chooses 16:9, 1:1 or 9:16; Size chooses 1,920 or
3,840 on the longest edge. Coverage (`C`) shows the yellow estimate without moving
the camera. Reset (`R`) returns to the photograph's pose. The gallery reference's
recorded FOV is retained until a preset is chosen.

Download returns one local Windows/Linux capture ZIP from Steward's exact catalog
and current camera. The [SelfieStick runbook](https://github.com/djcdevelopment/SelfieStick/blob/main/runner/README.md)
explains how to supply the matching world, local character and Valheim installation,
then inspect the PNG and restoration receipt. Studio obtains its normal browser token
for Capture requests; it never puts Steward's operator credential in the browser.
If pose, scene, lighting or WebGPU support is insufficient, the photo stays
browsable and the workspace explains why editing or replay is unavailable.
Returning to gameplay and back keeps the exported capture composition; it does not
retarget a live Quest experience.

`QuestStudioCapture` serves hash-checked embedded assets and proxies catalog, detail,
scene and export reads through the configured Steward scene origin. Studio requires
its normal browser token on `/api/v2/quest-studio/captures`. The component's fetch adapter
obtains that token locally; it is not forwarded to Steward. Both applications call the
same Steward export implementation and return identical ZIP bytes for a composition.

## Pinned component

Steward owns the JavaScript, CSS, camera model, renderer and shared fixtures.
`tools/release/import_capture_composer.py --artifact <zip> --pin <json>` verifies the
package and every file before copying it into `src/Quest.Studio/CaptureComposer/`.
The importer accepts explicit artifacts and has no sibling-checkout discovery.

The staged component is `1.0.0-preview.3`, 15,566 bytes, SHA-256
`c7ea6bbe0e2346b50c5ee2da2bc65a161c59d39420a31a7c42acb5dde96ffe24`.
Its manifest pins Steward's pushed revision
`2a9197d5bd45124aa4a91982a859669ac8cd0bd3`. The artifact remains labeled
candidate while public rollout is pending.
The embedded asset paths are fixed to LF checkout in `.gitattributes`: their raw
bytes are version pins, and Windows `core.autocrlf` must not change them when a
new worktree is created. Studio's existing creator renderer has the same rule.

## Verification

The Studio test suite passes (149 tests). Steward's integration browser test renders
the same real archived build in both hosts, exercises modes and resets, downloads from
each, compares the ZIP bytes and verifies that unauthenticated Studio catalog access
returns 403. The fixture browser test covers all controls, geometry aiming and position
dragging, keyboard input, resize, repeated view switches and missing WebGPU/metadata.

See `docs/evidence/gallery-capture-20260915.json` for candidate and evidence pins.
OMEN and AM4 game proof belongs to SelfieStick; Steward only enables downloads when
those receipts match the exact runner. Public deployment has not been changed.

## Pushed-source stage, 2026-09-15

Studio `0.9.14-gallery.3` was packed from pushed Quest revision
`dd3685b357e87d295215790fad93b65ccd044ad8`: 574,311 bytes, SHA-256
`98dba27f4fcc0d0bbe5851220fb697f6cf318790b7aea5ba8b564b65d92c2049`.
Its NuGet metadata names that commit, and a temporary loopback host consumed the
package rather than a project reference. Steward staged 83 Era 11 photographs with
the pinned SelfieStick 0.3.1 runner. Fresh OMEN and AM4 proof each passed 12 exact
camera cases and two legacy cases with the original plugins and saves restored.
The gallery composer and package-consuming Studio host rendered the same archived
build and downloaded identical 109,900-byte ZIPs, SHA-256
`e94e0145cf80c6ce757250e1812b66b2fa541a40a1c3a3d52d955fbce58ef7dc`.
The [Baseline stage evidence](https://github.com/djcdevelopment/baseline/blob/main/docs/evidence/gallery-capture-stage-20260915.json)
pins the full local artifacts and receipts. Public downloads remain pending.

## Why this mode and where it goes

Creator/DM already owns authored gameplay targets and their exact active-session
proof. Archived photography asks a different question: what would a local still look
like from this recorded build? Reusing Steward's scene and component avoids a second
camera model in Studio. The token-gated proxy protects Studio's application boundary
while the archive stays read-only and SelfieStick handles Valheim/save restoration.

Studio now carries the composer ZIP built from pushed Steward source and its staged
package has an exact byte/hash pin. Steward's local stage has the pushed SelfieStick
runner and fresh OMEN/AM4 capture receipts. Public promotion remains a separate
release action. The
[cross-repository plan](https://github.com/djcdevelopment/baseline/blob/main/docs/gallery-capture-program-plan.md)
tracks that release sequence. Moving-camera video belongs to a later time-sampled
contract and game proof, separate from Creator/DM's current still Capture mode.

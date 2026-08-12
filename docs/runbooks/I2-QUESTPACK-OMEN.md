# I2 manual acceptance: OMEN Studio to Runtime

This is a local, single-machine acceptance of the versioned boundary:

`Quest Studio UI -> certified .questpack -> Runtime inbox -> explicit Check -> explicit Load -> gameplay receipt`

It is not a deployment to AM4 or i5 and it does not prove multiplayer behavior.

## Preconditions

- Use OMEN in a private solo or listen-host world you control.
- The platform Companion running on OMEN already consumes the matching
  `Comfy.Quest.Studio` package, implements `IQuestStudioHost`, and binds only
  to loopback at `http://127.0.0.1:8080`.
- The locally installed Runtime payload contains matching
  `ComfyQuestRuntime.dll`, `ComfyQuestContracts.dll`, and
  `Newtonsoft.Json.dll`. Close Valheim before replacing plugin DLLs.
- Runtime has created
  `BepInEx\config\comfy-quest-runtime\inbox` and
  `BepInEx\config\comfy-quest-runtime\receipts`.
- Move unrelated invalid packs out of the inbox before the bounded run; retain their
  original paths so they can be restored unchanged afterward.
- Record `git -C C:\work\comfy-quest rev-parse HEAD`, the Companion build
  identity, UTC start time, and the pre-run receipt filenames. Do not record the
  browser token or copy it from developer tools.

Set `Safety.PrivateWorldConfirmed = true` only for this private world; the
Runtime experience requires an explicitly bound, locally owned object. Reset it
after the run.

## Author and publish

1. Start the loopback Companion, then start Valheim and enter the private world.
2. Open `http://127.0.0.1:8080/quest-studio`. The page obtains its short-lived
   browser token itself; never paste a token into a command or report.
3. Use a unique version so the immutable same-version/hash guard remains meaningful.
   For a bounded first pass, keep the starter `kill` event, target
   `$enemy_greyling`, one message action, and record the pack ID, version,
   experience ID, target, and expected message.
4. Select **Save draft**, then **Certify**. Both must report success and the same
   content hash.
5. Select **Publish to Game**. This calls
   `/api/v1/workbench/quest-studio/publish-project`. The top-level result must
   report `status=published`; the nested receipt must report
   `status=published` or `status=already_present`.
6. Record the receipt's `pack_id`, `version`, `content_hash`,
   `package_sha256`, `filename`, and `already_present` value.
   Verify that exact filename exists in Runtime's inbox and that its local SHA-256
   equals `package_sha256`.

A hash collision, filename collision, invalid pack, missing Valheim installation, or
manual-copy response is a failed I2 result. Preserve the diagnostic; do not rename or
edit the pack to get around it.

## Check, load, and witness

1. In Valheim press **F9**. In **Content Update**, choose **CHECK FOR UPDATES**
   (or press **F10**).
2. Require one new Runtime receipt with `operation=check`,
   `status=accepted`, and no diagnostics. The drawer must show the candidate as
   valid. Checking alone must not change
   `active\active-set.json`.
3. Choose **LOAD VALIDATED UPDATE** (or press **F11**). Require a new receipt with
   `operation=load`, `status=activated`, and the exact Studio
   `pack_id`, `version`, and `content_hash`.
4. Confirm `active\active-set.json` names the same inbox filename and identity.
5. Aim at a locally owned supported piece while F9 is open. Press backquote once
   for **CHECK**, then again for **CAST**.
   Require `operation=bind` / `status=inscribed` with the same content
   hash.
6. Close F9 and perform the single bounded trigger. For the starter case, kill one
   Greyling. Require the expected message and new receipts
   `operation=action` / `status=executed` followed by
   `operation=transition` / `status=complete`, all on the exact
   content hash.
7. Perform the same trigger once more. No second action may execute; retain any
   `duplicate_suppressed` receipt as the exactly-once proof.

## Evidence and cleanup

Keep the Quest and Companion revisions, UTC window, Studio publication receipt
fields, package SHA-256, active-set JSON, and only the new Runtime receipt JSON
files. Redact browser tokens and unrelated local paths.

Use **Versions & Rollback** to restore the prior validated content if one existed;
otherwise close Valheim and remove only the exact test pack after preserving
evidence. Confirm the active set is the intended prior version (or deliberately
absent), reset `PrivateWorldConfirmed`, and do not copy this I2 payload to AM4
or i5.

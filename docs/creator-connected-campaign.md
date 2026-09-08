# Connected campaign R&D handoff

The installed DMos cockpit can select the typed Slayers campaign, Play it, preview
an attempt-wide replay, and confirm replay against the current authored content.
Studio retains the command journal, exact attempt lineage, both project identities,
fixture preparation, binding changes, and Runtime receipts. The Linux host carries
its Python prerequisite helpers in `campaign/`; it does not need a Quest checkout.

The September 8 lap executed campaign start, in-progress replay, replay after a
game restart, a visible Studio edit to Cold Shot's completion message, and another
replay with a different content hash and binding instance. Lab recovered the
fixture receipt from saved preparation marks after restart. A 54-piece Steward
snapshot reached the existing DMos renderer. Both authored hunts passed synthetic
Studio rehearsal, including a rejected target before the accepted event. Installed
command replay, conflicting reuse, stale revision, and old-attempt refusals passed.

The live attempt remains **started**, not complete. This lap did not execute the
two thrown-spear finishing kills, automatic live Cold Shot continuation, or replay
after both hunts completed. These remain the human integration lap below. The
underlying event production is trusted from Derek's existing work; it is not a
reason to expand a weapon matrix or stall the connected engineering slice.

The original AM4 world, profile, plugins, and configuration were byte-restored;
26 recovery records cover 176 original files. Temporary services and input are
stopped. The separate saved checkpoint, Studio authoring state, measurements, and
evidence remain under the named run and connection roots. No human acceptance is
claimed. Release artifacts and installed R&D assembly deltas have separate hashes.

The immutable `0.9.10-local` release is pinned to source `ed6e098` in the
[release manifest](evidence/creator-connected-20260908/release.json). Its packaged
Linux host passed a [separate installed check](evidence/creator-connected-20260908/release-smoke.json)
after the game had been restored. All six bundled helpers were present, campaign
discovery and retained context worked, and the restored game correctly lacked an
active Creator session. That check did not mutate the game. The
[final independent restoration check](evidence/creator-connected-20260908/restoration-verified.json)
verified all 176 original files and closed service ports. Existing gates passed:
149 Studio tests, 386 Lab/Runtime tests, 455 Python checks, generated-source drift,
repository identity/boundary, full-history and staged secret scans. OMEN's Studio
tests used .NET 10 roll-forward because the local .NET 9 runtime was absent; the
installed Linux host carries its own .NET 9 runtime.

The final `0.9.11-local` cut adds one further replay guard: the final campaign
compilation must match the content hash approved in the preview. If a draft changes
during retirement or fixture cleanup, replay stops for recovery before preparing
or starting different content. The `0.9.10-local` evidence remains immutable.
The [final release manifest](evidence/creator-connected-20260908/release-0.9.11.json)
pins source `2b2e7dc`; its [installed host check](evidence/creator-connected-20260908/release-0.9.11-smoke.json)
passed the same retained-context, helper, and private-measurement checks. That
temporary connection is also stopped. The Studio, Lab/Runtime, and Python gates
were rerun successfully against this final cut.

## Run the installed API probes

These commands ran against the leased AM4 Studio through private loopback forwards.
They require that connection and a current campaign attempt; they do not establish
a game installation or a new recovery lease. Use the actual IDs returned by
`creator_campaign_authoring.py`, not the historical IDs from this document.

```powershell
python tools/quest-studio/creator_campaign_probe.py rehearse --guild <guild-id> --campaign <campaign-id> --output <evidence-directory>
python tools/quest-studio/creator_campaign_probe.py refusals --guild <guild-id> --campaign <campaign-id> --output <evidence-directory>
python tools/quest-studio/creator_campaign_probe.py scene --guild <guild-id> --campaign <campaign-id> --snapshot 1 --bounds 370 490 150 290 --output <evidence-directory>
```

`rehearse` reports synthetic proof and verifies it left the live attempt unchanged.
`refusals` exercises the installed journal without starting another run. `scene`
fetches an already measured snapshot and links it to the exact current attempt
only if its save hash matches the Creator checkpoint. It does not measure a live
world. The two Slayers routes have no spatial conditions: their Runtime receipts
belong in Chronicle, while Field Lodge's spatial-condition traces can be imported
into Steward. Do not add artificial spatial predicates to manufacture map evidence.

## Human integration lap — not yet accepted

Preconditions: the operator has prepared a fresh recoverable private AM4 session,
entered `ComfyQuestDemo` as `questyfour`, connected Studio and DMos, and authored a
fresh two-hunt campaign using the retained Slayers source and typed abstraction.
Reuse the separately retained meadow checkpoint, keeping original recovery backups
separate. The prior September 7 lease is restored and must not be reused as active.

1. Link the campaign in DMos and choose **Play campaign**. Its receipt must reach
   `started`, with Air Drop started and Cold Shot waiting. The fixture receipt must
   identify revision 2, 20 staged objects, both live target tokens, and its sign.
2. In Valheim, collect a staged spear, equip it, and finish the Deathsquito with a
   thrown spear. Observe the authoritative Air Drop completion and automatic Cold
   Shot start. Record the two run IDs and exact continuation receipt. Capture any
   touch/feel issue directly; diagnose missing evidence from those IDs.
3. Finish the Drake with a thrown spear. DMos may say campaign `complete` only when
   both exact runs and the source-to-successor continuation completed under the
   same content hash, world, and binding instance.
4. Follow **Edit this hunt** into Studio, change the completion message, and wait
   for **Saved**. Return to DMos, choose **Preview campaign replay**, inspect both
   run retirements and binding changes, then **Confirm campaign replay**.
5. Verify successor-first retirement, reverse binding restoration, owned fixture
   replacement, a new preparation, new binding instance and new attempt ID. The
   previous rewards and evidence remain. Both hunts must require fresh gameplay.
6. Repeat the two hunts and capture the revised message. Record human acceptance
   separately from the operation receipts. Archive, gracefully stop, checkpoint,
   and restore using the owned-session helpers; verify the restoration receipt.

The probes and cockpit runbook are linked from
[DMos's connected workflow](https://github.com/djcdevelopment/DMos/blob/main/docs/creator-dm/connected-creator-workflow.md).
The retained local machine evidence is `artifacts/creator-dm/campaign-20260908`.

## Recovery boundary

A lost browser response reuses the same command ID. Conflicting reuse is refused.
An uncertain mutation persists `recovery_required` and blocks further operations;
restarting Studio does not re-execute it. Inspect the operation receipt and the
attempt's `pending_step` and accumulated `reset_receipts`. There is no automatic
partial-campaign recovery in this cut. Preserve evidence and use the owned whole
session restoration path; do not clear the journal to force another Play.

Campaign replay is currently supported on the packaged Linux AM4 lane. Its fixture
clear adapter explicitly refuses Windows. Existing single-project controls retain
their platform behavior.

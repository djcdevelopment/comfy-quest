# Derek checklist: create, play, create, revise

The AM4 integration slice is verified and restored. The final release passed a
clear daylight arrival, opening the supply chest with real inputs, all three
encounters, restart, and matching DMos results at 1080p and 4K. Earlier native and
browser evidence proves creation and target/response revision without a game restart.

[Open the visual review](../artifacts/derek-checklist/review.html), or use the
[one-page human walkthrough](derek-walkthrough.md). The retained campaign already
contains three encounters; the next human review can edit the third. The recording
shows its earlier creation from the two-encounter campaign.

AM4's original files are restored and its owned services are stopped. OMEN remains
restored. Restaging and checking a seat before inviting Derek is the operator's job.
Machine-readable release, test and artifact pins are in
[derek-checklist-evidence.json](derek-checklist-evidence.json).

## The experience

1. Start outside the Field Lodge in daylight, wearing armor with food and the
   godsword equipped. One briefing sign and its supply chest sit left of the clear
   approach. Read the current quest title and instruction in the normal game HUD.
2. Let the Draugr approach and left-click to strike. The completion response appears,
   and the second encounter starts automatically. Strike the Greyling to complete it.
3. Open the Integration Practice campaign in DMos. See the measured lodge, expand
   **View recorded result**, and read the actual target, finishing condition and
   authored response. **Edit this hunt** opens its Studio source.
4. In Studio, create a Signature Hunt from the configurable practice palette. Choose
   a target and finishing condition, write the instruction and response, and place
   it after Second Encounter. Preview and confirm campaign replay in DMos.
5. Complete the three encounters. Change the third hunt's target and response in
   Studio, save, and replay again. See the changed enemy and response in Valheim;
   inspect the new result in DMos. Earlier attempts retain their own revisions.

This sequence was executed on AM4 with real input and browser controls. It is a
small creator integration demonstration. Combat balance, atmosphere and onboarding
for an unfamiliar player remain human judgments. No event injection or synthetic
completion qualifies as native play evidence.

## Configurable experiments

Derek redirected automated testing toward nearby ground targets and a godsword.
`tools/quest-studio/profiles/integration-melee.json` selects that preparation;
`signature-throw.json` retains the spear loadout for later feel testing. Install the
chosen JSON as `BepInEx/config/comfy-quest-lab/practice-profile.json` before preparation.
Studio exposes each hunt's target and either melee or thrown-spear finish. The
original Slayers snapshots and earlier frozen palette revisions are preserved;
R&D target additions have explicit provenance rather than invented source citations.

Runtime spawns only the current encounter's target. The fixture owns one sign, one
supporting post and one supply chest; it has no static target arenas. Revision 4
replaced the eleven-object layout after Derek observed the signs blocking movement.
The chest contains four quality-4 Carapace spears. Integration melee needs no pickup.

## Executed evidence

AM4 run root: `/home/derek/valheim-capture/creator-dm/runs/20260908-derek-r1`.
Local browser and image evidence: `artifacts/derek-checklist`.
Session: `creator-derek-20260908-r1`; world UID `-7600395338659582326`.
Guild: `guild-19411f439d1d`; campaign: `campaign-cb08fddc9f54`.

| Experience | Native or browser evidence |
| --- | --- |
| Recover the failed OMEN session | `artifacts/creator-dm/omen-human-20260908/played-20260908T220437Z`; restoration verified all 1,266 original files. |
| Clear arrival and supplies | `sparse-arrival-r16.jpg`; `sparse-approach-r16.json` records 2.56 m unobstructed movement. `supplies-open-r17.jpg` visibly shows the four spears in the open chest. The first arrival harness only proved approach/interact; final refinement requires observed inventory visibility as well. |
| Intact lodge, no practice graves | `practice-venue-20260909T013053Z.json`: 40 pieces, geometry MATCH. `venue-before-tidy-r17` preserves the complete pre-cleanup world; native tidy receipt archives both owned graves including inventory before removal. Later live status: lodge 40, graves 0. |
| Two encounters and automatic progression | Attempt `557edb89-6afd-4d22-9896-5bd09ca95e54`; native laps `hunt-lap-20260909T010852Z` and `hunt-lap-20260909T010858Z`, two attack inputs each. |
| Create a third hunt and play it | Studio UI `2026-09-09T01-09-59` creates `project-ffb81b0e035b`; attempt `2d055e10-ee86-485d-ab98-e4119dcf7aee` completes all three. |
| Revise target and response and replay | Studio UI at 01:14:49 UTC changes the third Draugr to Greyling. Attempt `908e1acd-efb0-4141-bb33-8eab315346f8` completes all three. Transition `20260909T011740974Z-cb9d20fa80034b6b83adc15606b12f92` proves Greyling and projectile=false. `revised-response-r16.jpg` shows the exact revised response. |
| No restart across create/play/revise | All three attempts above share game PID 1547565; recording `sparse-melee-r16.mp4`. Earlier outcomes preserved in `evidence-20260909T012454Z`. |
| Wrong finishing hit and clear recovery | Real sword kill in a thrown-spear hunt produces ignored event `20260909T012727158Z-7ad1ddf2a9624c43a164add1991f40be`, no completion, and persistent Replay guidance in `wrong-finisher-r16.jpg`. The harness timed out due to diagnostic field casing; that failed result remains preserved. The separate native receipt and screenshot prove rejection. |
| Retire, restart, replay | DMos retirement at 01:30:34 UTC, game restart, Studio correction, then attempt `73a77046-3b6d-4dfb-9b71-a2933fd1681e` completes all three with one native attack each. Earlier destroyed-anchor recovery and pre-bind retry receipts remain archived. |
| Matching measured world | Saved world SHA-256 `d459848f3b387c7d51cdd63a60f73688fb608617f7aba568c674429423288705`; scene `9bd72a1c735b2c92394054da425b39d78b87519dbd7b01785877f889faf51bfa`, 42 pieces/render instances. Studio UI loaded geometry; the published HTTP boundary attached that exact scene to the attempt. It is labeled saved geometry, not live actors. |
| Native response in DMos | Attempt `2462bccc-691a-4243-8fb5-b9a08faeed64`, runs `run-20260909T015208021Z-74c2e478`, `run-20260909T015247123Z-18ee3051`, `run-20260909T015254401Z-315f76c1`. Browser `2026-09-09T01-55-57-207Z-view.json` contains each real completion and executed authored response. |
| Readable result workflow | 1080p `2026-09-09T02-10-32-907Z-view.png/json`: actual summary clicks, all three results stay open through refresh, no page errors. 4K overview `2026-09-09T02-02-10-989Z-view.png/json`; final release captures will include expanded results at both sizes. |

The revised third response reads: ?Revision confirmed: a Greyling replaced the
Draugr, and this new response came from your Studio edit.? Runtime records it only
after the message action executes; DMos does not infer it from the current draft.

## Recovery and proof limits

Campaign replay previews the exact attempt/revisions, retires its recorded runs,
restores bindings and clears its owned fixture before starting again. Lost anchors
are reported and recoverable; old attempts and mutation receipts remain available.
Historical campaigns retired through earlier direct orphan recovery can retain a
stale Studio `started` label. This slice does not claim general reconciliation of
all historical campaigns, arbitrary branching, or unreviewed creature mechanics.

Practice status proves current daylight, protection and loadout only. Full walkthrough
acceptance belongs to the evidence above and the final release record; a successful
Play command alone never establishes a completed quest or human readiness.

## Final release and recovery

Contracts/Studio **0.9.13-local**, Lab **0.2.2**, Runtime **0.1.1** were built from
`cb54141526a248c62cdb78151e6527e7b4bc415a`; DMos is
`1c78f7b0c34aa6799c1b7529cb1e9d9625773e54`. The release is
`artifacts/derek-checklist/release-0.9.13-local/release.json`, mirrored on AM4 at
`/home/derek/valheim-capture/creator-dm/releases/derek-checklist-0.9.13-local`.
All 20 release files were hash verified there, and all four installed plugin hashes
matched. Older packages and releases retain their original bytes.

Fixture revision 5 puts the chest south of the sign, clear of the direct approach.
The 0.9.12 cold lap exposed the earlier collision and sign interaction; that failed
lap is retained. The corrected native lap `arrival-lap-20260909T022755Z` opens the
chest in about three seconds. `supplies-open-r20.jpg` visibly confirms four spears.

Final attempt `fe3ed7f0-4495-4af6-8398-c4dd32c4a3a3` completed with one native attack
per encounter, in laps `023005Z`, `023015Z` and `023039Z`. Its content hash is
`cad26567613489f3815b0a273301e260830d956a5af29b22dcac8b5b0b8ef679`.
A later cold restart retained all three completed outcomes and the same responses.
An overlapping preparation/Play request was refused with `world_mutation_busy`;
retry after preparation completed succeeded. No completion was injected.

Final measured world hash:
`3f5b1406a71957ddc5a0976d18dd470a1d816b816c983cbbca5a81b72ac512e5`.
Scene: `5b59dce87b9f16907323a472e163b48169bbdde86566696b8584e9243b21ff7c`,
42 exact pieces/render instances. Browser captures at `02-34-58-503Z` (1080p)
and `02-34-57-725Z` (4K) show all three expanded results through refresh, with
no page errors. The exact measured checkpoint remains in `played-final/measured-checkpoint`
and the Steward measurement directory; the final later save is separate.

Required checks: Lab **385/385**, Studio **149/149**, Python **455/455**, DMos
connector/safety **11/11**. Generator drift, identity, boundary and its failing
negative probe, and full-history secret scans passed. Studio used a package-hash
and SDK-keyed fresh cache. There was no redundant new unit-test matrix.

At **02:36:53 UTC**, the game closed gracefully. Restoration verified all **26 AM4
recovery records**, removed the lease, and retained every matching played world and
character save with a hash manifest. Studio, both Steward containers, the temporary
input device, local gateway and tunnel are stopped. Native receipts are archived in
`evidence-20260909T023653Z`; authoring history remains in the connected state directory.
The small review bundle is `artifacts/derek-checklist/final-review-evidence.tar.gz`.

The 60-second `create-play-revise-highlights.mp4` contains three labeled cuts from
the same native create/revise session. It predates the final grave cleanup and chest
repositioning; the final arrival screenshot and browser captures show the refined
release. The uncut final-release lap is retained on AM4 as `final-native-r20.mp4`.

Implementation began 2026-09-08 at **15:02 PDT**. The development block used about
**4 hours 40 minutes**, less than the proposed 6?10 hours. The completed slice is
ready for a short creator-workflow review after the operator stages the seat.
Combat balance, atmosphere and broader historical-campaign reconciliation remain
outside this automated acceptance. Work receipt: `br-20260908-220304-edea1309`.

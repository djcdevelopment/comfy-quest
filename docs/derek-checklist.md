# Derek checklist: create, play, create, revise

The AM4 integration slice has executed: clear daylight arrival, two linked hunts,
a third created through Studio, and a target/response revision played without a
game restart. Required source checks pass. The frozen release, final cold lap,
short walkthrough and AM4 restoration are being finalized. OMEN is restored;
no new OMEN playtest has been requested.

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

## Release and handoff status

Final source uses the new immutable `0.9.13-local` Contracts/Studio version,
Lab 0.2.2 and Runtime 0.1.1. Prior package versions and all eight development
packages retain their original bytes; development packages are archived under
`artifacts/derek-checklist/development-packages`.

Required checks completed before freeze: Lab 385/385, Studio 149/149, Python
455/455, DMos connector/safety 11/11; generators, identity, boundary and its negative
self-test, and full-history secret scan passed. Final release build, checks against
its exact packages, final cold-entry lap, short video, and restored AM4 file hashes
will be recorded here before a human handoff is declared ready.

Implementation began 2026-09-08 at 15:02 PDT. Core integration was proven inside
four hours. The original budget is 6?10 hours; finishing work is expected to use
about another hour. Work receipt: `br-20260908-220304-edea1309`.

The 0.9.12 cold lap caught an obstructed supply approach: the sign lay between
arrival and chest, and the harness interacted with the sign. That failed lap and
release are retained. Fixture revision 5 moves the chest south of the sign; the
harness now aims at the observed chest collider and requires the real supply
interaction target plus an open inventory. Final native verification is pending.

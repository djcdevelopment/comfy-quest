# CreatorOS Beta 1 creator kit

Run `Start-CreatorStudio.ps1`. The packaged host binds only to loopback and keeps drafts under
`%LOCALAPPDATA%\ComfyQuest\CreatorOSBeta1`; it does not require this repository.

For the beta, the steward-owned Field Lodge and released Signature Hunt are immutable examples.
Creators may draft and export projects locally. Sharing a draft does not install or publish it:
export the Studio JSON, attach it in the designated Discord review thread, and wait for a steward
to return a reviewed release package. Never paste enrollment tokens, server passwords, local
paths, or Steam numeric IDs into a review export.

The human review has eight judgments: orient to the composition, name the place, draw steward
locks, draw creator freedoms, make the hunt inhabit the venue, attack the seams, judge the demo,
and choose the next attack. Browser checkmarks are notes, not machine evidence.

If your player ZIP includes `optional-overlay`, run `Start-CreatorOsOverlay.ps1` there. It uses
Discoverlay only as a dumb local renderer for recent Runtime receipts. It never certifies a run,
posts telemetry, reads credentials, or replaces the durable NetworkSense/EventLog evidence.

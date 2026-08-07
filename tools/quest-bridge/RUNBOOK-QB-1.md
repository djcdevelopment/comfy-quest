# Runbook — QB-1: prove the quest bridge live

The last step for workbench tool `quest-submission-bridge`. Everything upstream is
built, decided (**ADR 0018**) and passing against a fixture; what has never happened is
one real in-game completion travelling the whole path. This is that run.

**Time:** about 20 minutes, most of it waiting for containers.
**You need:** Docker Desktop, host Postgres on 5433, a Valheim client with
ComfyNetworkSense installed, and about two minutes in a Meadows world.

---

## Read this first: punchwood worked once, and the port deferred it

Hitting a bush **did** trigger a quest — that was the first-ever test, and it was an
`attack`/`hit` trigger, not a kill. It is not in the current mod, and that is a
deliberate deferral rather than a limitation.

The retired `ComfyControlSurface` carried three trigger buckets
(`docs/quest-vertical-slice-architecture.md:171-174`): `hit` against world targets such
as trees and bushes, `kill` against creatures, and two-shot `on_first_hit` → `on_death`
sequences. The `hit` bucket was three Harmony postfixes in a 2.4 KB file
(`comfy/handoffs/comfy-control-surface/Patches/QuestTriggerPatches.cs`):

| Patched method | Call |
| --- | --- |
| `TreeBase.Damage` | `OnLocalPlayerHit("tree", name, hit)` |
| `TreeLog.Damage` | `OnLocalPlayerHit("tree", name, hit)` |
| `Destructible.Damage` | `OnLocalPlayerHit("destructible", name, hit)` — a bush |

feeding `QuestTriggerService.OnLocalPlayerHit`, which gated on
`TriggerEvent == "hit"` (`QuestTriggerService.cs:142,159`).

The port to ComfyNetworkSense kept only the creature path, and says so in its own source
(`QuestTriggerEvaluator.cs:15-17`):

> `on_first_hit` is preserved on the model as informational, and hit-on-world-object
> ("hit") triggers (punchwood) are a deferred increment (the current seam only hooks
> creatures), so this evaluator matches `kill` triggers only.

So **today** the seam hooks `Character` only
(`Patches/GameplayEventPatches.cs:26`), and a tree is a `TreeBase`, a bush a
`Destructible`, a rock a `MineRock`. Hitting one fires nothing — no event, no error,
nothing in the log. That silence is the deferral, not a bug.

**Two ways to run QB-1, and the choice is real:**

- **Greyling, today, no code change.** The weakest thing in the game, dies to bare
  fists, and `greyling_cull` is already authored in the staged quest-view with no weapon
  or projectile constraint. With `spawn Greyling 1` you never look for one. This is what
  the rest of this runbook assumes.
- **Restore punchwood first.** Three postfixes, an `OnLocalPlayerHit` entry point, and a
  `hit` branch in the evaluator, all specified byte-exact by the archive. It is a
  `network/` change, so it owes a roadmap note and a mod rebuild — but it closes a named
  deferred increment rather than adding scaffolding for a test, and the quest vertical
  slice will want it regardless.

---

## 1 — Bring the stack up

Postgres is expected to already be running on the **host** at port 5433; the dev compose
does not start one (`docker-compose.dev.yml:1`).

```powershell
docker compose -f Lumberjacks\infra\docker\docker-compose.dev.yml up -d gateway eventlog
```

Confirm the EventLog is answering before going near the game — this is the cheapest
possible place to find out it is not:

```powershell
curl.exe "http://localhost:4002/events?type=quest_completed&limit=1"
```

Expect `{"events":[],"count":0}` on a clean database. Anything else — connection
refused, a 500 — is a stack problem, not a quest problem, and it is much easier to fix
now than after you have killed something.

## 2 — Arm the evaluator

Two separate flags, and the second one defaults **off**
(`PluginConfig.cs:814`). In `BepInEx/config/`, in the ComfyNetworkSense config:

```ini
[Gameplay]
gameplayEventsEnabled = true
questEvaluatorEnabled = true
```

Both are hot-reloadable, so you can flip them with the game running.

**The subtle one:** `quest-view.json` loads *regardless* of `questEvaluatorEnabled` —
the flag only gates *matching*. So the failure mode is not an error. It is quests
loading cleanly, a kill landing, and simply no `quest_completed`. If you get silence,
check this flag before you check anything else.

## 3 — Stage a quest and get a Greyling

A ready-made quest-view with four alpha quests, including `greyling_cull`, is at:

```
artifacts\modpacks\m32-stage\Valheim\BepInEx\config\comfy-network-sense\quest-view.json
```

Copy it to your install's `BepInEx\config\comfy-network-sense\quest-view.json`. Set
`player.name` to whatever you are playing as.

Then, in a Meadows world:

- **With devcommands** — `spawn Greyling 1` drops one at your feet. No searching.
- **Without** — Greylings are the common small grey humanoids at any Black Forest edge;
  Boars work too (`boar_hunter` is staged as well), and they are everywhere in Meadows.

Punch it. Any weapon is fine — `greyling_cull` sets no weapon-skill or projectile
constraint, so fists count.

Matching is a **case-insensitive substring** on the creature name, with `(clone)`
stripped (`QuestTriggerEvaluator.CreatureMatches`). So `greyling` matches `Greyling`
and does *not* match `Greydwarf` — a Greydwarf will not fire this quest. There is also
a **60-second per-quest cooldown**, so a second Greyling inside a minute is silently
ignored; that is correct behaviour, not a miss.

You should see this in the BepInEx log:

```
[gp] quest complete: greyling_cull (Greyling Cull)
```

That line means the client did its job. The rest is transport.

## 4 — Fetch, review, export

```powershell
python tools\quest-bridge\fetch_completions.py --url http://localhost:4002 --out bridge-inbox
python tools\quest-bridge\bridge_consumer.py bridge-inbox
python tools\quest-bridge\review_inbox.py bridge-inbox list
python tools\quest-bridge\review_inbox.py bridge-inbox accept <submission_id>
python tools\quest-bridge\review_inbox.py bridge-inbox export <submission_id>
```

`export` writes `bridge-review/export/<id>.txt` carrying the quest's own guild command
(it rides the EventLog payload verbatim as `bot_command`) and names the EventLog event
id as the evidence. Every state change appends to `bridge-review/events.jsonl`.

**QB-1 is done when** that export file exists and names a real event id from a real
kill. Keep it — it is the receipt.

---

## If it goes quiet

Work down the path; each step tells you which half is at fault.

| Symptom | Where to look |
| --- | --- |
| No `[gp] quest complete` line | `questEvaluatorEnabled` is still `false`, or the creature name did not substring-match, or you are inside the 60s cooldown |
| Nothing at all when hitting a tree, bush or rock | Expected today — punchwood is a deferred increment, see the top of this runbook. Not a bug and not a config problem |
| Log line fires but `/events` stays empty | Transport: the routed RPC or the gateway's `POST /valheim/events`. Check the gateway can reach `ServiceUrls__EventLog` (`http://eventlog:4002`) |
| Row exists but the review record is thin | Expected. ADR 0018 — the EventLog row *is* the evidence. There is no screenshot, trace, or position in this contract, and that is the decision, not a gap |
| Row exists but has no `quest_name` | You are on a mod build older than `468e9d1`. The gateway forwards only the payload, so `quest_name` has to be *in* it |

## Notes for whoever lands this

- The fetcher tolerates `payload` arriving as either a JSON string or an object
  (`parse_payload`), so do not be alarmed by either shape on the wire.
- Submission ids are deterministic, so re-running `fetch_completions.py` never
  duplicates a row or clobbers a review decision. Fetch as often as you like.
- Only `schema_version: 2` thin submissions are accepted here. The old
  `schema_version: 1` outbox envelopes belong to the archived consumer at
  `recipes/quest-submission-bridge/bridge-consumer/`.
- When it lands: flip `first_tasks` in `Lumberjacks/docs/workbench/workbench.json`, then
  render → commit inputs → re-render → commit HTML → `Publish-WorkbenchAssets.ps1`, and
  run `workbench_discord.py plan` so the thread does not drift from the catalog.

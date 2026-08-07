# Quest bridge — EventLog → review inbox

The ported back half of the quest-submission bridge (workbench tool
`quest-submission-bridge`, claiming task QB-1). Design: **ADR 0018** —
the durable EventLog row is the evidence; there is no screenshot, trace,
or position in this contract, and the retired `ComfyControlSurface`
outbox envelope is deliberately not re-materialized.

Flow (all local files, no bot token, human review in the loop):

```text
live mod (QuestEvaluatorEnabled) → routed RPC → server POST /valheim/events
  → durable EventLog (quest_completed row)
    → fetch_completions.py   (row → thin submission JSON in bridge-inbox/)
      → bridge_consumer.py   (submission → bridge-review/<id>.md + state)
        → review_inbox.py    (list/show/accept/reject/needs-info/export)
```

## Use

The EventLog is private-plane only (`http://localhost:4002` where the lab
runs); run the fetch there, or save a `GET /events?type=quest_completed`
response body and use `--from-file`.

```powershell
python tools\quest-bridge\fetch_completions.py --url http://localhost:4002 --out bridge-inbox
python tools\quest-bridge\bridge_consumer.py bridge-inbox
python tools\quest-bridge\review_inbox.py bridge-inbox list
python tools\quest-bridge\review_inbox.py bridge-inbox accept <submission_id>
python tools\quest-bridge\review_inbox.py bridge-inbox export <submission_id>
```

`export` writes `bridge-review/export/<id>.txt` with the quest's own guild
command (it rides the EventLog payload verbatim as `bot_command`) and names
the EventLog event id as the evidence. Every state change appends to
`bridge-review/events.jsonl`.

## Contract

- Input to the consumer: `schema_version: 2` thin submissions written by
  `fetch_completions.py` (one per EventLog row; deterministic submission
  ids, so refetching never duplicates or clobbers a review decision).
- `schema_version: 1` outbox payloads are **not** accepted here — the
  byte-exact archived consumer at
  `recipes/quest-submission-bridge/bridge-consumer/` still handles those
  (fixtures/demo only; its producer mod is retired).
- Older EventLog rows may predate the `quest_name` payload field; the
  record falls back to the `quest_id`.

## Provenance & license

`bridge_consumer.py` and `review_inbox.py` are derived from the MIT-licensed
comfy archive copies at `recipes/quest-submission-bridge/bridge-consumer/`
(see that directory's `PROVENANCE.md`); these ports retain the MIT terms.
`fetch_completions.py` is new.

## Privacy

A review record carries a player id and what they did. Same rule as the
original: everything is a plain local file — keep the inbox, the review
directory, and exports off any public surface.

## Tests

`tests/test_quest_bridge.py` runs the whole fixture-driven path
(fixture → fetch → consume → review → accept → export).

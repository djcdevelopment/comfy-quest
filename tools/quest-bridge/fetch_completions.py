#!/usr/bin/env python3
"""Fetch quest_completed rows from the durable EventLog into a local bridge inbox.

The ADR 0018 front door of the ported quest-submission bridge: each EventLog row
becomes one thin submission payload (schema_version 2) that bridge_consumer.py
renders into a review record. The EventLog row IS the evidence — there is no
screenshot, trace, or position in this contract, by design.

Usage:
  python fetch_completions.py --url http://localhost:4002 [--limit N] [--out DIR]
  python fetch_completions.py --from-file events-response.json [--out DIR]

--from-file takes a saved GET /events response body (JSON), for offline use and
for fixtures. The EventLog is private-plane only; run this where the lab runs.
"""
import argparse
import json
import os
import re
import sys
import urllib.request
from datetime import datetime, timezone


QUEST_COMPLETED = "quest_completed"


def utc_now():
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def fetch_events(url, limit):
    query = f"{url.rstrip('/')}/events?type={QUEST_COMPLETED}&limit={limit}"
    with urllib.request.urlopen(query, timeout=10) as response:
        return json.load(response), query


def load_events_file(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f), os.path.abspath(path)


def parse_payload(raw):
    # The EventLog stores payload as raw JSON text and returns it as a string;
    # tolerate an already-parsed object for hand-built fixtures.
    if isinstance(raw, dict):
        return raw
    if isinstance(raw, str) and raw.strip():
        try:
            parsed = json.loads(raw)
            return parsed if isinstance(parsed, dict) else {}
        except json.JSONDecodeError:
            return {}
    return {}


def slug(value, fallback):
    text = re.sub(r"[^a-z0-9]+", "-", str(value or "").lower()).strip("-")
    return text or fallback


def compact_timestamp(occurred_at):
    text = re.sub(r"[^0-9]", "", str(occurred_at or ""))[:14]
    return text or "00000000000000"


def submission_id_for(event):
    # Deterministic: refetching the same row lands on the same file and the same
    # review state, so a rerun never duplicates or clobbers a review decision.
    payload = parse_payload(event.get("payload"))
    event_id = str(event.get("event_id") or "unknown")
    return "-".join([
        compact_timestamp(event.get("occurred_at")),
        slug(payload.get("quest_id"), "quest"),
        slug(event_id.replace("-", "")[:8], "event"),
    ])


def to_submission(event, fetched_from):
    payload = parse_payload(event.get("payload"))
    quest_id = payload.get("quest_id") or ""
    return {
        "schema_version": 2,
        "kind": "quest_completion_eventlog",
        "submission_id": submission_id_for(event),
        "submission_type": "quest_proof",
        "action_id": quest_id or str(event.get("event_id") or "unknown"),
        "created_at_utc": str(event.get("occurred_at") or ""),
        "status": "ready_for_review",
        "quest": {
            "quest_id": quest_id,
            # quest_name reaches the EventLog payload only after the producer
            # change that forwards it (ADR 0018); older rows fall back to the id.
            "name": payload.get("quest_name") or quest_id or "unknown",
        },
        "workflow": {
            "guild": payload.get("guild"),
            "category": payload.get("category"),
            "bot_command_template": payload.get("bot_command"),
        },
        "player": {
            "name": None,
            "player_id": event.get("actor_id"),
        },
        "world": {
            "name": event.get("world_id"),
            "seed": None,
        },
        "trigger": {
            "creature": payload.get("creature"),
            "weapon": payload.get("weapon"),
            "ranged": bool(payload.get("ranged")),
        },
        "evidence": {
            "eventlog": {
                "event_id": event.get("event_id"),
                "occurred_at": event.get("occurred_at"),
                "source_service": event.get("source_service"),
                "schema_version": event.get("schema_version"),
                "fetched_from": fetched_from,
                "fetched_at_utc": utc_now(),
            }
        },
        "raw_event": event,
    }


def atomic_write_json(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    os.replace(tmp, path)


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--url", help="EventLog base URL (e.g. http://localhost:4002)")
    source.add_argument("--from-file", help="saved GET /events response body (JSON)")
    parser.add_argument("--limit", type=int, default=50, help="rows to fetch (default 50)")
    parser.add_argument("--out", default="bridge-inbox", help="inbox directory (default bridge-inbox)")
    args = parser.parse_args(argv)

    if args.url:
        body, fetched_from = fetch_events(args.url, args.limit)
    else:
        body, fetched_from = load_events_file(args.from_file)

    events = body.get("events") if isinstance(body, dict) else None
    if not isinstance(events, list):
        print("error: response has no 'events' list", file=sys.stderr)
        return 1

    written = 0
    skipped = 0
    for event in events:
        if not isinstance(event, dict) or event.get("event_type") != QUEST_COMPLETED:
            skipped += 1
            continue
        submission = to_submission(event, fetched_from)
        path = os.path.join(os.path.abspath(args.out), f"{submission['submission_id']}.json")
        atomic_write_json(path, submission)
        print(f"wrote {path}")
        written += 1

    if skipped:
        print(f"skipped {skipped} non-{QUEST_COMPLETED} row(s)")
    print(f"fetched {written} completion(s) from {fetched_from}")
    return 0 if written else 1


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(1)

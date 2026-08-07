#!/usr/bin/env python3
"""Render thin quest-completion submissions (schema_version 2) into review markdown.

The ADR 0018 port of the archived comfy bridge consumer: input is the bridge inbox
that fetch_completions.py fills from the durable EventLog, not the retired
ComfyControlSurface outbox. The evidence section names the EventLog row; there is
no screenshot, trace, or position in this contract. Output shape is unchanged from
the original so the review-inbox workflow carries over:

  bridge-review/<submission_id>.md
  bridge-review/index.json
  bridge-review/state/<submission_id>.json

Usage:
  python bridge_consumer.py <inbox-dir> [out-dir]

Derived from recipes/quest-submission-bridge/bridge-consumer/bridge_consumer.py
(MIT, provenance in that directory's PROVENANCE.md). Schema_version 1 payloads are
NOT accepted here — the byte-exact original still handles those.
"""
import json
import os
import sys
from datetime import datetime, timezone


def load_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def require_object(value, where):
    if not isinstance(value, dict):
        raise ValueError(f"{where} must be an object")
    return value


def require_string(data, key, where):
    value = data.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{where}.{key} must be a non-empty string")
    return value


def optional_string(data, key, where):
    value = data.get(key)
    if value is None:
        return None
    if not isinstance(value, str):
        raise ValueError(f"{where}.{key} must be a string or null")
    return value


def validate_payload(data, path):
    require_object(data, "payload")
    if data.get("schema_version") != 2:
        raise ValueError(
            f"{path}: schema_version must be 2 (the EventLog thin record; "
            "schema 1 outbox payloads belong to the archived consumer)")
    if data.get("status") != "ready_for_review":
        raise ValueError(f"{path}: status must be ready_for_review")

    payload = {
        "submission_id": require_string(data, "submission_id", "payload"),
        "action_id": require_string(data, "action_id", "payload"),
        "submission_type": require_string(data, "submission_type", "payload"),
        "created_at_utc": require_string(data, "created_at_utc", "payload"),
    }

    quest = require_object(data.get("quest"), "payload.quest")
    workflow = data.get("workflow") if isinstance(data.get("workflow"), dict) else {}
    player = require_object(data.get("player"), "payload.player")
    world = data.get("world") if isinstance(data.get("world"), dict) else {}
    trigger = data.get("trigger") if isinstance(data.get("trigger"), dict) else {}
    evidence = require_object(data.get("evidence"), "payload.evidence")
    eventlog = require_object(evidence.get("eventlog"), "payload.evidence.eventlog")

    payload["quest_id"] = optional_string(quest, "quest_id", "payload.quest")
    payload["quest_name"] = require_string(quest, "name", "payload.quest")
    payload["workflow_guild"] = optional_string(workflow, "guild", "payload.workflow")
    payload["workflow_category"] = optional_string(workflow, "category", "payload.workflow")
    payload["workflow_bot_command_template"] = optional_string(
        workflow, "bot_command_template", "payload.workflow"
    )
    payload["player_name"] = optional_string(player, "name", "payload.player")
    payload["player_id"] = optional_string(player, "player_id", "payload.player")
    payload["world_name"] = optional_string(world, "name", "payload.world")
    payload["trigger_creature"] = optional_string(trigger, "creature", "payload.trigger")
    payload["trigger_weapon"] = optional_string(trigger, "weapon", "payload.trigger")
    payload["trigger_ranged"] = bool(trigger.get("ranged"))
    payload["event_id"] = require_string(eventlog, "event_id", "payload.evidence.eventlog")
    payload["event_occurred_at"] = optional_string(eventlog, "occurred_at", "payload.evidence.eventlog")
    payload["event_source_service"] = optional_string(eventlog, "source_service", "payload.evidence.eventlog")
    payload["event_fetched_from"] = optional_string(eventlog, "fetched_from", "payload.evidence.eventlog")
    payload["notes"] = data.get("notes") if isinstance(data.get("notes"), str) else ""
    return payload


def discover_payloads(input_dir):
    return sorted(
        os.path.join(input_dir, name)
        for name in os.listdir(input_dir)
        if name.lower().endswith(".json")
    )


def render_review(payload, source_path):
    trigger = payload["trigger_creature"] or "unknown creature"
    weapon = payload["trigger_weapon"] or "unknown weapon"
    style = "ranged" if payload["trigger_ranged"] else "melee"
    bot_line = payload["workflow_bot_command_template"] or "(no bot command on the quest record)"

    return "\n".join([
        f"# Submission {payload['submission_id']}",
        "",
        "## Review",
        "",
        "- Status: ready for review",
        f"- Type: {payload['submission_type']}",
        f"- Quest: {payload['quest_name']}" + (
            f" ({payload['quest_id']})" if payload["quest_id"] and payload["quest_id"] != payload["quest_name"] else ""),
        f"- Workflow: {workflow_label(payload)}",
        f"- Completed: {payload['created_at_utc']}",
        f"- Player: {payload['player_name'] or 'unknown'} (id {payload['player_id'] or 'unknown'})",
        f"- World: {payload['world_name'] or 'unknown'}",
        f"- Trigger: killed {trigger} with {weapon} ({style})",
        "",
        "## Evidence",
        "",
        "The proof is the durable EventLog row (ADR 0018) — server-received on the",
        "Producer-gated ingress; a public client cannot post one.",
        "",
        f"- EventLog event: `{payload['event_id']}`",
        f"- Received: {payload['event_occurred_at'] or 'unknown'}"
        + (f" via {payload['event_source_service']}" if payload["event_source_service"] else ""),
        f"- Fetched from: `{payload['event_fetched_from'] or 'unknown'}`",
        f"- Source payload: `{source_path}`",
        "",
        "## Copy-paste command draft",
        "",
        "```text",
        bot_line,
        "```",
        "",
        "## Notes",
        "",
        payload["notes"] or "_No notes supplied._",
        "",
    ])


def workflow_label(payload):
    parts = [payload.get("workflow_guild"), payload.get("workflow_category")]
    text = " / ".join(str(part) for part in parts if part)
    return text or "none"


def write_review(out_dir, submission_id, text):
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, f"{submission_id}.md")
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    os.replace(tmp, path)
    return path


def utc_now():
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def atomic_write_json(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    os.replace(tmp, path)


def ensure_review_state(out_dir, payload, source_path, review_path):
    state_dir = os.path.join(out_dir, "state")
    state_path = os.path.join(state_dir, f"{payload['submission_id']}.json")
    if os.path.exists(state_path):
        state = load_json(state_path)
        state.update(state_metadata(payload, source_path, review_path, out_dir))
        atomic_write_json(state_path, state)
        return state

    state = {
        "submission_id": payload["submission_id"],
        "status": "pending",
        "reason": "",
        "created_at_utc": utc_now(),
        "updated_at_utc": utc_now()
    }
    state.update(state_metadata(payload, source_path, review_path, out_dir))
    atomic_write_json(state_path, state)
    return state


def state_metadata(payload, source_path, review_path, out_dir):
    return {
        "action_id": payload["action_id"],
        "submission_type": payload["submission_type"],
        "player": payload["player_name"] or payload["player_id"],
        "world": payload["world_name"],
        "quest_id": payload["quest_id"],
        "quest_name": payload["quest_name"],
        "workflow_guild": payload["workflow_guild"],
        "workflow_category": payload["workflow_category"],
        "workflow_bot_command_template": payload["workflow_bot_command_template"],
        "evidence_event_id": payload["event_id"],
        "source_payload": os.path.abspath(source_path),
        "review_file": os.path.relpath(review_path, out_dir).replace("\\", "/"),
        "payload_created_at_utc": payload["created_at_utc"]
    }


def write_index(out_dir, items):
    index = {
        "schema_version": 1,
        "generated_at_utc": utc_now(),
        "count": len(items),
        "items": items
    }
    atomic_write_json(os.path.join(out_dir, "index.json"), index)


def main(argv):
    if len(argv) not in (1, 2):
        print(__doc__.strip(), file=sys.stderr)
        return 2

    input_dir = os.path.abspath(argv[0])
    if not os.path.isdir(input_dir):
        print(f"error: input directory not found: {input_dir}", file=sys.stderr)
        return 1

    out_dir = os.path.abspath(argv[1]) if len(argv) == 2 else os.path.join(input_dir, "bridge-review")
    payload_paths = discover_payloads(input_dir)
    if not payload_paths:
        print(f"error: no payload json files found in {input_dir}", file=sys.stderr)
        return 1

    count = 0
    index_items = []
    for path in payload_paths:
        data = load_json(path)
        payload = validate_payload(data, path)
        review = render_review(payload, path)
        review_path = write_review(out_dir, payload["submission_id"], review)
        state = ensure_review_state(out_dir, payload, path, review_path)
        index_items.append({
            "submission_id": payload["submission_id"],
            "status": state["status"],
            "submission_type": payload["submission_type"],
            "action_id": payload["action_id"],
            "player": payload["player_name"] or payload["player_id"],
            "world": payload["world_name"],
            "created_at_utc": payload["created_at_utc"],
            "review_file": os.path.relpath(review_path, out_dir).replace("\\", "/")
        })
        print(f"wrote {review_path}")
        count += 1

    write_index(out_dir, index_items)
    print(f"processed {count} payload(s)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(1)

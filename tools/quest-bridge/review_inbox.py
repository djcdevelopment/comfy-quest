#!/usr/bin/env python3
"""Review quest-completion submissions rendered by bridge_consumer.py.

The ADR 0018 port of the archived comfy review inbox, unchanged in workflow —
list / show / accept / reject / needs-info / export over bridge-review/ state
files — but export drafts the command from the thin EventLog record: the quest's
own bot command rides the payload verbatim, and the evidence named is the durable
EventLog row, not screenshot attachments.

Usage:
  python review_inbox.py <inbox-root-or-review-dir> list
  python review_inbox.py <inbox-root-or-review-dir> show <submission_id>
  python review_inbox.py <inbox-root-or-review-dir> accept <submission_id>
  python review_inbox.py <inbox-root-or-review-dir> reject <submission_id> --reason "..."
  python review_inbox.py <inbox-root-or-review-dir> needs-info <submission_id> --reason "..."
  python review_inbox.py <inbox-root-or-review-dir> export <submission_id>

Derived from recipes/quest-submission-bridge/bridge-consumer/review_inbox.py
(MIT, provenance in that directory's PROVENANCE.md).
"""
import json
import os
import sys
from datetime import datetime, timezone


VALID_STATUSES = {"pending", "accepted", "rejected", "needs_info", "exported"}


def utc_now():
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def load_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def atomic_write_json(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    os.replace(tmp, path)


def atomic_write_text(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    os.replace(tmp, path)


def review_root(path):
    root = os.path.abspath(path)
    if os.path.basename(root).lower() == "bridge-review":
        return root
    return os.path.join(root, "bridge-review")


def state_dir(root):
    return os.path.join(root, "state")


def state_path(root, submission_id):
    return os.path.join(state_dir(root), f"{submission_id}.json")


def require_state(root, submission_id):
    path = state_path(root, submission_id)
    if not os.path.exists(path):
        raise ValueError(f"unknown submission_id: {submission_id}")
    state = load_json(path)
    status = state.get("status")
    if status not in VALID_STATUSES:
        raise ValueError(f"{path}: invalid status {status!r}")
    return state


def parse_reason(args):
    if "--reason" not in args:
        return ""
    index = args.index("--reason")
    if index == len(args) - 1:
        raise ValueError("--reason requires text")
    return " ".join(args[index + 1:]).strip()


def append_event(root, event):
    path = os.path.join(root, "events.jsonl")
    event = {"ts_utc": utc_now(), **event}
    with open(path, "a", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(event, separators=(",", ":")) + "\n")


def transition(root, submission_id, status, reason=""):
    if status not in VALID_STATUSES:
        raise ValueError(f"invalid status: {status}")
    if status in {"rejected", "needs_info"} and not reason:
        raise ValueError(f"{status} requires --reason")

    state = require_state(root, submission_id)
    previous = state["status"]
    state["status"] = status
    state["reason"] = reason
    state["updated_at_utc"] = utc_now()
    atomic_write_json(state_path(root, submission_id), state)
    append_event(root, {
        "submission_id": submission_id,
        "from": previous,
        "to": status,
        "actor": "local",
        "reason": reason
    })
    return state


def list_items(root):
    index_path = os.path.join(root, "index.json")
    if os.path.exists(index_path):
        items = load_json(index_path).get("items", [])
    else:
        items = []

    by_id = {item.get("submission_id"): item for item in items if item.get("submission_id")}
    for name in sorted(os.listdir(state_dir(root))) if os.path.isdir(state_dir(root)) else []:
        if not name.endswith(".json"):
            continue
        state = load_json(os.path.join(state_dir(root), name))
        item = by_id.get(state["submission_id"], {"submission_id": state["submission_id"]})
        item["status"] = state["status"]
        item["reason"] = state.get("reason", "")
        by_id[state["submission_id"]] = item
    return [by_id[key] for key in sorted(by_id)]


def show_item(root, submission_id):
    state = require_state(root, submission_id)
    review_file = state.get("review_file") or f"{submission_id}.md"
    review_path = os.path.join(root, review_file)
    if os.path.exists(review_path):
        with open(review_path, encoding="utf-8") as f:
            review = f.read()
    else:
        review = f"(missing review file: {review_path})\n"
    return state, review


def export_item(root, submission_id):
    state, review = show_item(root, submission_id)
    payload = load_json(state["source_payload"]) if state.get("source_payload") else None
    command = render_export_command(payload) if payload else ""
    event_id = eventlog_event_id(payload) if payload else ""
    export_path = os.path.join(root, "export", f"{submission_id}.txt")
    lines = [
        f"Submission: {submission_id}",
        f"Review status: {state['status']}",
        f"Reason: {state.get('reason', '') or 'none'}",
        "",
    ]
    if command:
        lines += [
            "Copy-paste command:",
            command,
            "",
        ]
    if event_id:
        lines += [
            f"Evidence: durable EventLog event {event_id} (ADR 0018 — no attachments)",
            "",
        ]
    lines += [review.rstrip(), ""]
    text = "\n".join(lines)
    atomic_write_text(export_path, text)
    transition(root, submission_id, "exported", reason=state.get("reason", ""))
    return export_path


def render_export_command(payload):
    # Thin EventLog record (schema 2): the quest's guild command rides the
    # payload verbatim; there is no screenshot to substitute into anything.
    workflow = payload.get("workflow") if isinstance(payload.get("workflow"), dict) else {}
    template = workflow.get("bot_command_template")
    if isinstance(template, str) and template.strip():
        return template.strip()
    return ""


def eventlog_event_id(payload):
    evidence = payload.get("evidence") if isinstance(payload.get("evidence"), dict) else {}
    eventlog = evidence.get("eventlog") if isinstance(evidence.get("eventlog"), dict) else {}
    event_id = eventlog.get("event_id")
    return str(event_id) if event_id else ""


def print_list(root):
    items = list_items(root)
    if not items:
        print("no submissions")
        return
    for item in items:
        print(
            f"{item.get('submission_id')}  "
            f"{item.get('status', 'unknown'):<10}  "
            f"{item.get('submission_type', 'unknown'):<12}  "
            f"{item.get('player', 'unknown')}"
        )


def main(argv):
    if len(argv) < 2:
        print(__doc__.strip(), file=sys.stderr)
        return 2

    root = review_root(argv[0])
    command = argv[1]
    args = argv[2:]

    if not os.path.isdir(root):
        raise ValueError(f"review root not found: {root}. Run bridge_consumer.py first.")

    if command == "list":
        print_list(root)
        return 0

    if len(args) < 1:
        raise ValueError(f"{command} requires submission_id")

    submission_id = args[0]
    reason = parse_reason(args[1:])

    if command == "show":
        state, review = show_item(root, submission_id)
        print(f"state: {state['status']}")
        if state.get("reason"):
            print(f"reason: {state['reason']}")
        print("")
        print(review.rstrip())
        return 0

    if command == "accept":
        state = transition(root, submission_id, "accepted", reason=reason)
        print(f"{submission_id}: {state['status']}")
        return 0

    if command == "reject":
        state = transition(root, submission_id, "rejected", reason=reason)
        print(f"{submission_id}: {state['status']}")
        return 0

    if command == "needs-info":
        state = transition(root, submission_id, "needs_info", reason=reason)
        print(f"{submission_id}: {state['status']}")
        return 0

    if command == "export":
        path = export_item(root, submission_id)
        print(f"wrote {path}")
        return 0

    raise ValueError(f"unknown command: {command}")


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(1)

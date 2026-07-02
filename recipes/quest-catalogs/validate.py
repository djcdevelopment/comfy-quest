#!/usr/bin/env python3
"""Validate a guild quest-catalog file. Standard library only — no installs.

Usage:  python validate.py <path-to-catalog.json>

It prints problems you must fix (X) and advice (!). Exit code 1 if anything is broken.
This is your guardrail: if it says PASS, the file is safe for the picker and the mod.
"""
import json, sys, os

if len(sys.argv) < 2:
    raise SystemExit("usage: python validate.py <path-to-catalog.json>")
path = sys.argv[1]

data = json.load(open(path, encoding="utf-8-sig"))
errors, warnings = [], []

for key in ("schema_version", "guild", "era", "quests"):
    if key not in data:
        errors.append(f"missing top-level field: {key}")

if data.get("schema_version") != 1:
    errors.append(f"schema_version must be 1, got {data.get('schema_version')!r}")

quests = data.get("quests", [])
if not quests:
    errors.append("no quests defined")

EVIDENCE_KEYS = {"screenshots", "video_alternative", "link", "group_turnin", "notes"}
seen_ids = set()
for i, q in enumerate(quests):
    where = f"quest[{i}] ({q.get('name', '?')})"
    for key in ("quest_id", "name", "category", "requirements", "evidence",
                "auto_checked", "venue"):
        if key not in q:
            errors.append(f"{where}: missing '{key}'")
    qid = q.get("quest_id")
    if qid in seen_ids:
        errors.append(f"{where}: duplicate quest_id '{qid}'")
    seen_ids.add(qid)

    ev = q.get("evidence") or {}
    missing = EVIDENCE_KEYS - set(ev)
    if missing:
        errors.append(f"{where}: evidence missing {sorted(missing)}")
    shots = ev.get("screenshots")
    # Discord allows up to 10 attachments per message; Ranger quests go as high as 7
    if not isinstance(shots, int) or not (0 <= shots <= 10):
        errors.append(f"{where}: evidence.screenshots must be an int 0-10, got {shots!r}")

    if q.get("auto_checked"):
        if q.get("bot_command"):
            errors.append(f"{where}: auto_checked quests must have bot_command null")
    else:
        if not q.get("bot_command"):
            errors.append(f"{where}: needs a bot_command (or auto_checked: true)")
        elif shots == 0 and not (ev.get("link") or ev.get("group_turnin") or ev.get("notes")):
            warnings.append(f"{where}: no screenshots, no link, no group, no notes slot — how is it proven?")

    if q.get("venue") not in ("in_game", "irl"):
        errors.append(f"{where}: venue must be 'in_game' or 'irl', got {q.get('venue')!r}")

    trig = q.get("trigger")
    if trig is not None and not isinstance(trig, dict):
        errors.append(f"{where}: trigger must be null or an object")

for e in errors:
    print("X", e)
for w in warnings:
    print("!", w)
print(f"{'FAIL' if errors else 'PASS'}: {len(quests)} quest(s), "
      f"{len(errors)} error(s), {len(warnings)} warning(s)")
sys.exit(1 if errors else 0)

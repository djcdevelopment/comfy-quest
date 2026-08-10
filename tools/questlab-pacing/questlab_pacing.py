#!/usr/bin/env python3
"""Turn local Quest Lab suite receipts into privacy-minimal pacing evidence."""

from __future__ import annotations

import argparse
import json
import statistics
import sys
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path
from typing import Any, Iterable


SCHEMA = "comfy-questlab-pacing-report/v1"
SUITE_SCHEMA = "comfy-questlab-suite-receipt/v1"
SCHOOL_ORDER = ("combat", "harvest", "inventory", "building", "crafting", "progression", "world", "social")


class PacingError(RuntimeError):
    pass


def timestamp(value: Any, label: str) -> datetime:
    if not isinstance(value, str) or not value:
        raise PacingError(f"{label} is missing")
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise PacingError(f"{label} is not ISO-8601") from exc


def read_receipt(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as exc:
        raise PacingError(f"{path.name}: unreadable JSON: {exc}") from exc
    if not isinstance(value, dict) or value.get("schema") != SUITE_SCHEMA:
        raise PacingError(f"{path.name}: not a Quest Lab suite receipt")
    if value.get("suite") != "all-schools" or value.get("evidence_kind") != "live-gameplay":
        raise PacingError(f"{path.name}: pacing accepts only live all-schools receipts")
    return value


def analyze_run(receipt: dict[str, Any], ordinal: int, hesitation_seconds: float, noisy_actions: int) -> dict[str, Any]:
    started = timestamp(receipt.get("started_utc"), "started_utc")
    finished = timestamp(receipt.get("finished_utc"), "finished_utc")
    expectations = receipt.get("expectations")
    witnesses = receipt.get("witnesses")
    if not isinstance(expectations, list) or not isinstance(witnesses, list):
        raise PacingError("expectations and witnesses must be arrays")
    rows = []
    completion_order = []
    for item in expectations:
        if not isinstance(item, dict):
            raise PacingError("expectation entry is not an object")
        school = item.get("school")
        event = item.get("event")
        if school not in SCHOOL_ORDER or not isinstance(event, str):
            raise PacingError("expectation has an unknown school or event")
        witnessed_at = timestamp(item.get("first_witness_utc"), f"{school}.first_witness_utc")
        completed_at = timestamp(item.get("first_completion_utc"), f"{school}.first_completion_utc")
        elapsed = max(0.0, (witnessed_at - started).total_seconds())
        completion_elapsed = max(0.0, (completed_at - started).total_seconds())
        actions = item.get("canonical_action_count")
        completions = item.get("quest_completion_count")
        if not isinstance(actions, int) or not isinstance(completions, int):
            raise PacingError(f"{school}: action/completion counts must be integers")
        rows.append(
            {
                "school": school,
                "event": event,
                "first_witness_seconds": round(elapsed, 3),
                "first_completion_seconds": round(completion_elapsed, 3),
                "canonical_actions": actions,
                "quest_completions": completions,
                "noisy": actions > noisy_actions,
            }
        )
        completion_order.append((witnessed_at, school, event))
    completion_order.sort()
    previous = started
    gaps = []
    for index, (at, school, event) in enumerate(completion_order):
        gap = max(0.0, (at - previous).total_seconds())
        gaps.append(
            {
                "school": school,
                "event": event,
                "phase": "startup" if index == 0 else "between-actions",
                "gap_seconds": round(gap, 3),
                "hesitation": gap > hesitation_seconds,
            }
        )
        previous = at
    frequencies: Counter[tuple[str, str]] = Counter()
    for witness in witnesses:
        if isinstance(witness, dict) and isinstance(witness.get("school"), str) and isinstance(witness.get("event"), str):
            frequencies[(witness["school"], witness["event"])] += 1
    top_events = [
        {"school": school, "event": event, "witnesses": count}
        for (school, event), count in frequencies.most_common(12)
    ]
    raw = receipt.get("raw_witnesses")
    canonical = receipt.get("canonical_actions")
    coalesced = receipt.get("coalesced_witnesses")
    return {
        "run": ordinal,
        "release_id": receipt.get("release_id"),
        "verdict": receipt.get("verdict"),
        "duration_seconds": round(max(0.0, (finished - started).total_seconds()), 3),
        "raw_witnesses": raw,
        "canonical_actions": canonical,
        "coalesced_witnesses": coalesced,
        "coalescing_ratio": round(coalesced / raw, 4) if isinstance(raw, int) and raw > 0 and isinstance(coalesced, int) else None,
        "schools": sorted(rows, key=lambda row: SCHOOL_ORDER.index(row["school"])),
        "completion_order": [item[1] for item in completion_order],
        "gaps": gaps,
        "top_events": top_events,
    }


def median(values: list[float]) -> float | None:
    return round(statistics.median(values), 3) if values else None


def aggregate(runs: list[dict[str, Any]], hesitation_seconds: float, noisy_actions: int) -> dict[str, Any]:
    school_values: dict[str, dict[str, list[float]]] = defaultdict(lambda: defaultdict(list))
    hesitation_counts: Counter[str] = Counter()
    startup_delays = 0
    order_counts: Counter[tuple[str, ...]] = Counter()
    for run in runs:
        order_counts[tuple(run["completion_order"])] += 1
        for row in run["schools"]:
            school_values[row["school"]]["witness"].append(row["first_witness_seconds"])
            school_values[row["school"]]["completion"].append(row["first_completion_seconds"])
            school_values[row["school"]]["actions"].append(float(row["canonical_actions"]))
        for gap in run["gaps"]:
            if gap["hesitation"] and gap["phase"] == "startup":
                startup_delays += 1
            elif gap["hesitation"]:
                hesitation_counts[gap["school"]] += 1
    schools = []
    recommendations = []
    for school in SCHOOL_ORDER:
        values = school_values.get(school, {})
        row = {
            "school": school,
            "median_first_witness_seconds": median(values.get("witness", [])),
            "median_first_completion_seconds": median(values.get("completion", [])),
            "median_canonical_actions": median(values.get("actions", [])),
            "hesitation_runs": hesitation_counts[school],
        }
        schools.append(row)
        if row["hesitation_runs"]:
            recommendations.append(
                {
                    "kind": "navigation-friction",
                    "school": school,
                    "detail": f"gap before {school} exceeded {hesitation_seconds:g}s in {row['hesitation_runs']}/{len(runs)} run(s)",
                }
            )
        if row["median_canonical_actions"] is not None and row["median_canonical_actions"] > noisy_actions:
            recommendations.append(
                {
                    "kind": "noisy-trigger",
                    "school": school,
                    "detail": f"median required-event action count {row['median_canonical_actions']:g} exceeds {noisy_actions}",
                }
            )
    orders = [
        {"schools": list(order), "runs": count}
        for order, count in order_counts.most_common()
    ]
    if startup_delays:
        recommendations.insert(
            0,
            {
                "kind": "startup-delay",
                "school": None,
                "detail": f"first required action exceeded {hesitation_seconds:g}s in {startup_delays}/{len(runs)} run(s); arm the suite only when the tester is ready",
            },
        )
    return {
        "schools": schools,
        "startup_delay_runs": startup_delays,
        "completion_orders": orders,
        "recommendations": recommendations,
    }


def collect_paths(inputs: Iterable[Path]) -> list[Path]:
    found: list[Path] = []
    for path in inputs:
        if path.is_dir():
            found.extend(path.glob("all-schools-*.json"))
        elif path.is_file():
            found.append(path)
        else:
            raise PacingError(f"input does not exist: {path}")
    unique = {path.resolve(): path.resolve() for path in found}
    return sorted(unique.values(), key=lambda path: (path.stat().st_mtime, path.name))


def analyze(inputs: Iterable[Path], hesitation_seconds: float = 60.0, noisy_actions: int = 5) -> dict[str, Any]:
    paths = collect_paths(inputs)
    if not paths:
        raise PacingError("no all-schools receipts found")
    runs = [
        analyze_run(read_receipt(path), index, hesitation_seconds, noisy_actions)
        for index, path in enumerate(paths, 1)
    ]
    return {
        "schema": SCHEMA,
        "generated_utc": datetime.now().astimezone().isoformat(),
        "thresholds": {"hesitation_seconds": hesitation_seconds, "noisy_actions": noisy_actions},
        "runs_analyzed": len(runs),
        "runs": runs,
        "aggregate": aggregate(runs, hesitation_seconds, noisy_actions),
        "privacy": {
            "player_identity": False,
            "targets": False,
            "positions": False,
            "raw_action_keys": False,
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", type=Path, nargs="+")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--hesitation-seconds", type=float, default=60.0)
    parser.add_argument("--noisy-actions", type=int, default=5)
    args = parser.parse_args()
    if args.hesitation_seconds <= 0 or args.noisy_actions < 1:
        parser.error("thresholds must be positive")
    try:
        report = analyze(args.inputs, args.hesitation_seconds, args.noisy_actions)
    except PacingError as exc:
        print(f"questlab-pacing: {exc}", file=sys.stderr)
        return 1
    encoded = json.dumps(report, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(encoded, encoding="utf-8")
    print(encoded, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

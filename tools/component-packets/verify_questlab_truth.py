#!/usr/bin/env python3
"""Verify one machine-readable Quest Lab Gallery Truth artifact.

The verifier treats a structural failure as a failing receipt, accepts warnings as honest
partial evidence, and never claims that the artifact replaces human visual acceptance.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any


SCHEMA = "comfy-questlab-gallery-truth/v1"
VERDICTS = {"pass", "warn", "fail"}
REQUIRED_ASSERTIONS = {
    "loaded-world-bounds",
    "floor-weather-protection",
    "ceiling-fixture-clearance",
    "fresh-prefab-configuration",
    "named-view-plan",
}
REQUIRED_VIEWS = {"overview-north", "overview-east", "overhead", "arrival-eye"}


class VerificationError(ValueError):
    pass


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise VerificationError(message)


def _vector(value: Any, label: str) -> None:
    _require(isinstance(value, list) and len(value) == 3, f"{label} must be a 3-vector")
    _require(
        all(isinstance(item, (int, float)) and math.isfinite(item) for item in value),
        f"{label} must contain finite numbers",
    )


def verify(payload: dict[str, Any], allow_fail: bool = False) -> dict[str, Any]:
    _require(payload.get("schema") == SCHEMA, f"schema must be {SCHEMA}")
    verdict = payload.get("verdict")
    _require(verdict in VERDICTS, "top-level verdict must be pass, warn, or fail")
    _require(allow_fail or verdict != "fail", "truth receipt has a failing assertion")
    _require(
        payload.get("environment", {}).get("visibleSnow") == "human-frame-required",
        "visible snow must remain a human-frame judgment",
    )
    _require(
        "human visual acceptance is authoritative" in payload.get("capturePolicy", ""),
        "capture policy must preserve human visual authority",
    )
    subjects = payload.get("subjects")
    _require(isinstance(subjects, list) and subjects, "at least one Gallery subject is required")

    total_objects = 0
    total_views = 0
    warnings = 0
    failures = 0
    for index, subject in enumerate(subjects):
        label = f"subjects[{index}]"
        _require(isinstance(subject.get("profile"), str), f"{label}.profile is required")
        _require(isinstance(subject.get("build"), str), f"{label}.build is required")
        marked = subject.get("markedObjects")
        loaded = subject.get("loadedObjects")
        _require(isinstance(marked, int) and marked > 0, f"{label}.markedObjects must be positive")
        _require(isinstance(loaded, int) and 0 <= loaded <= marked, f"{label}.loadedObjects is invalid")
        total_objects += marked

        bounds = subject.get("worldBounds")
        _require(isinstance(bounds, dict), f"{label}.worldBounds is required")
        for field in ("min", "max", "center", "size"):
            _vector(bounds.get(field), f"{label}.worldBounds.{field}")

        views = subject.get("namedViews")
        _require(isinstance(views, list), f"{label}.namedViews must be a list")
        ids: set[str] = set()
        for view_index, view in enumerate(views):
            view_label = f"{label}.namedViews[{view_index}]"
            view_id = view.get("id")
            _require(isinstance(view_id, str) and view_id not in ids, f"{view_label}.id is invalid")
            ids.add(view_id)
            _vector(view.get("lens"), f"{view_label}.lens")
            _vector(view.get("target"), f"{view_label}.target")
            _vector(view.get("up"), f"{view_label}.up")
            fov = view.get("fieldOfView")
            _require(isinstance(fov, (int, float)) and 20 <= fov <= 120, f"{view_label}.fieldOfView is invalid")
        _require(REQUIRED_VIEWS <= ids, f"{label} is missing baseline named views")
        total_views += len(views)

        assertions = subject.get("assertions")
        _require(isinstance(assertions, list), f"{label}.assertions must be a list")
        assertion_ids: set[str] = set()
        for assertion in assertions:
            assertion_id = assertion.get("id")
            assertion_verdict = assertion.get("verdict")
            _require(isinstance(assertion_id, str), f"{label} has an assertion without id")
            _require(assertion_id not in assertion_ids, f"{label} repeats assertion {assertion_id}")
            _require(assertion_verdict in VERDICTS, f"{assertion_id} has an invalid verdict")
            _require(isinstance(assertion.get("detail"), str), f"{assertion_id} needs detail")
            assertion_ids.add(assertion_id)
            warnings += assertion_verdict == "warn"
            failures += assertion_verdict == "fail"
        _require(REQUIRED_ASSERTIONS <= assertion_ids, f"{label} is missing required assertions")

    return {
        "schema": SCHEMA,
        "verdict": verdict,
        "subjects": len(subjects),
        "marked_objects": total_objects,
        "named_views": total_views,
        "warnings": warnings,
        "failures": failures,
        "human_visual_acceptance_required": True,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifact", type=Path)
    parser.add_argument("--allow-fail", action="store_true", help="validate shape even when assertions fail")
    args = parser.parse_args()
    try:
        payload = json.loads(args.artifact.read_text(encoding="utf-8"))
        result = verify(payload, allow_fail=args.allow_fail)
    except (OSError, json.JSONDecodeError, VerificationError) as exc:
        print(f"Quest Lab truth verification failed: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

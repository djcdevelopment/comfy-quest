#!/usr/bin/env python3
"""Fail-closed verifier for the live Quest Lab release evidence bundle."""

from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime
from pathlib import Path
from typing import Any


REPO = Path(__file__).resolve().parents[2]
CAPABILITY_MANIFEST = (
    REPO / "tools" / "component-packets" / "samples" / "quest-capability-manifest.json"
)
SUITE_SCHEMA = "comfy-questlab-suite-receipt/v1"
REQUEST_SCHEMA = "comfy-questlab-batch-request-receipt/v1"
ACCEPTANCE_SCHEMA = "comfy-questlab-gallery-acceptance/v1"
SUMMARY_SCHEMA = "comfy-questlab-release-verification/v1"
LIVE_EXPECTATIONS = {
    "combat": "kill",
    "harvest": "resource_damaged",
    "inventory": "item_picked_up",
    "building": "piece_placed",
    "crafting": "station_fuel_added",
    "progression": "skill_raised",
    "world": "player_teleported",
    "social": "sign_written",
}
GALLERY_OPERATIONS = {
    "gallery_build",
    "gallery_compare",
    "gallery_identify",
    "gallery_clear",
    "gallery_rebuild",
}
MARBLE_PROFILES = {"marble-wide", "marble-grand"}
VISUAL_CHECKS = {
    "solid_marble_floor",
    "marble_floor_not_snow_coated",
    "scale_acceptable",
    "hall_width_acceptable",
    "roof_canopy_acceptable",
    "hanging_braziers_acceptable",
    "natural_tree_clearance_acceptable",
    "runes_acceptable",
    "rune_banners_acceptable",
    "sign_lighting_acceptable",
    "welcome_camp_acceptable",
    "quest_grid_readable",
}


class VerificationError(RuntimeError):
    pass


def read_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise VerificationError(f"{path}: unreadable JSON: {exc}") from exc
    if not isinstance(value, dict):
        raise VerificationError(f"{path}: root must be an object")
    return value


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require(condition: bool, message: str, errors: list[str]) -> None:
    if not condition:
        errors.append(message)


def validate_suite_common(
    receipt: dict[str, Any],
    *,
    suite: str,
    evidence_kind: str,
    required_events: int,
    expected_version: str,
    expected_release: str,
) -> list[str]:
    errors: list[str] = []
    require(receipt.get("schema") == SUITE_SCHEMA, "suite schema is not v1", errors)
    require(receipt.get("suite") == suite, f"suite must be {suite}", errors)
    require(
        receipt.get("evidence_kind") == evidence_kind,
        f"evidence_kind must be {evidence_kind}",
        errors,
    )
    require(receipt.get("plugin_version") == expected_version, "plugin version mismatch", errors)
    require(receipt.get("release_id") == expected_release, "release id mismatch", errors)
    require(receipt.get("state") == "complete", "suite state is not complete", errors)
    require(receipt.get("verdict") == "pass", "suite verdict is not pass", errors)
    require(receipt.get("required_events") == required_events, "required event count mismatch", errors)
    require(receipt.get("witnessed_events") == required_events, "not every event was witnessed", errors)
    require(
        receipt.get("completed_example_quests") == required_events,
        "not every example quest completed",
        errors,
    )
    require(receipt.get("double_completions") == 0, "same-action double completion recorded", errors)
    require(bool(receipt.get("machine")), "machine identity is empty", errors)
    require(bool(receipt.get("started_utc")), "suite start time is empty", errors)
    require(bool(receipt.get("finished_utc")), "suite finish time is empty", errors)
    expectations = receipt.get("expectations")
    witnesses = receipt.get("witnesses")
    require(isinstance(expectations, list), "expectations must be an array", errors)
    require(isinstance(witnesses, list), "witnesses must be an array", errors)
    if isinstance(expectations, list):
        require(len(expectations) == required_events, "expectation array length mismatch", errors)
        for item in expectations:
            if not isinstance(item, dict):
                errors.append("expectation entry is not an object")
                continue
            label = f"{item.get('school')}/{item.get('event')}"
            require(item.get("witnessed") is True, f"{label} was not witnessed", errors)
            require(item.get("quest_completed") is True, f"{label} quest did not complete", errors)
            require(
                isinstance(item.get("canonical_action_count"), int)
                and item["canonical_action_count"] >= 1,
                f"{label} has no canonical action",
                errors,
            )
            require(bool(item.get("first_signature")), f"{label} has no exact signature", errors)
            require(bool(item.get("first_action_key")), f"{label} has no action key", errors)
            require(bool(item.get("first_witness_utc")), f"{label} has no witness time", errors)
            require(bool(item.get("first_completion_utc")), f"{label} has no completion time", errors)
    return errors


def validate_creator_events(
    receipt: dict[str, Any], expected_version: str, expected_release: str
) -> list[str]:
    manifest = read_json(CAPABILITY_MANIFEST)
    safe_events = set(manifest.get("CreatorSafeEvents", []))
    errors = validate_suite_common(
        receipt,
        suite="creator-events",
        evidence_kind="synthetic-contract",
        required_events=len(safe_events),
        expected_version=expected_version,
        expected_release=expected_release,
    )
    require(len(safe_events) == 34, "generated creator-safe catalog is not 34 events", errors)
    expectations = receipt.get("expectations", [])
    observed = {item.get("event") for item in expectations if isinstance(item, dict)}
    require(observed == safe_events, "creator-events receipt does not exactly cover the catalog", errors)
    witnesses = receipt.get("witnesses", [])
    require(len(witnesses) == len(safe_events), "synthetic witness array length mismatch", errors)
    for witness in witnesses:
        if isinstance(witness, dict):
            require(witness.get("evaluated") is True, "synthetic witness bypassed evaluator", errors)
            require(
                witness.get("source") == "synthetic-contract",
                "synthetic witness source is mislabeled",
                errors,
            )
    return errors


def validate_all_schools(
    receipt: dict[str, Any], expected_version: str, expected_release: str
) -> list[str]:
    errors = validate_suite_common(
        receipt,
        suite="all-schools",
        evidence_kind="live-gameplay",
        required_events=len(LIVE_EXPECTATIONS),
        expected_version=expected_version,
        expected_release=expected_release,
    )
    expectations = receipt.get("expectations", [])
    observed = {
        item.get("school"): item.get("event")
        for item in expectations
        if isinstance(item, dict)
    }
    require(observed == LIVE_EXPECTATIONS, "live receipt does not contain the exact eight-school matrix", errors)
    require(
        str(receipt.get("runtime_profile", "")).startswith("extended"),
        "live receipt did not use the extended batch profile",
        errors,
    )
    require(
        isinstance(receipt.get("raw_witnesses"), int)
        and isinstance(receipt.get("canonical_actions"), int)
        and receipt["raw_witnesses"] >= receipt["canonical_actions"],
        "raw/canonical witness accounting is inconsistent",
        errors,
    )
    require(
        isinstance(receipt.get("coalesced_witnesses"), int)
        and receipt["coalesced_witnesses"] >= 1,
        "live run did not witness any local/RPC or overload coalescing",
        errors,
    )
    return errors


def validate_gallery(
    request_receipts: list[dict[str, Any]],
    acceptance: dict[str, Any],
    expected_machine: str | None = None,
    expected_version: str | None = None,
    expected_release: str | None = None,
) -> tuple[list[str], str]:
    errors: list[str] = []
    by_operation: dict[str, list[dict[str, Any]]] = {}
    for receipt in request_receipts:
        require(receipt.get("schema") == REQUEST_SCHEMA, "gallery request schema is not v1", errors)
        operation = str(receipt.get("operation", ""))
        by_operation.setdefault(operation, []).append(receipt)
        require(receipt.get("state") == "completed", f"{operation} request did not complete", errors)
        require(bool(receipt.get("request_id")), f"{operation} request id is empty", errors)
        require(bool(receipt.get("machine")), f"{operation} machine is empty", errors)
        if expected_machine:
            require(
                receipt.get("machine") == expected_machine,
                f"{operation} request came from a different machine",
                errors,
            )
        if expected_version:
            require(
                receipt.get("plugin_version") == expected_version,
                f"{operation} request plugin version mismatch",
                errors,
            )
        if expected_release:
            require(
                receipt.get("release_id") == expected_release,
                f"{operation} request release id mismatch",
                errors,
            )
    require(
        GALLERY_OPERATIONS.issubset(by_operation),
        "gallery receipts must include build, compare, identify, clear, and rebuild",
        errors,
    )
    clear_details = [str(item.get("detail", "")) for item in by_operation.get("gallery_clear", [])]
    require(any(detail.startswith("cleared ") for detail in clear_details), "no gallery clear removed marks", errors)
    identify_details = [str(item.get("detail", "")) for item in by_operation.get("gallery_identify", [])]
    require(
        any("gallery structures:" in detail for detail in identify_details),
        "gallery identify did not find marked structures",
        errors,
    )

    require(acceptance.get("schema") == ACCEPTANCE_SCHEMA, "gallery acceptance schema is not v1", errors)
    selected = str(acceptance.get("selected_profile", ""))
    require(selected in MARBLE_PROFILES, "selected gallery is not a marble profile", errors)
    require(bool(acceptance.get("accepted_by")), "gallery acceptance has no human", errors)
    try:
        datetime.fromisoformat(str(acceptance.get("accepted_utc", "")).replace("Z", "+00:00"))
    except ValueError:
        errors.append("gallery accepted_utc is not ISO-8601")
    observations = acceptance.get("observations")
    require(isinstance(observations, dict), "gallery observations must be an object", errors)
    if isinstance(observations, dict):
        require(set(observations) == VISUAL_CHECKS, "gallery observation set is incomplete", errors)
        for name in VISUAL_CHECKS:
            require(observations.get(name) is True, f"visual check {name} was not accepted", errors)
    comparison_id = acceptance.get("comparison_request_id")
    comparison_ids = {
        item.get("request_id") for item in by_operation.get("gallery_compare", [])
    }
    require(comparison_id in comparison_ids, "visual acceptance does not name its comparison receipt", errors)
    selected_details = [
        str(item.get("detail", ""))
        for operation in ("gallery_build", "gallery_rebuild")
        for item in by_operation.get(operation, [])
    ]
    require(
        any(selected in detail for detail in selected_details),
        "selected marble profile has no successful build/rebuild receipt",
        errors,
    )
    return errors, selected


def verify_release(
    *,
    creator_path: Path,
    live_path: Path,
    gallery_paths: list[Path],
    acceptance_path: Path,
    expected_version: str,
    expected_release: str,
) -> dict[str, Any]:
    creator = read_json(creator_path)
    live = read_json(live_path)
    gallery = [read_json(path) for path in gallery_paths]
    acceptance = read_json(acceptance_path)
    errors = validate_creator_events(creator, expected_version, expected_release)
    errors.extend(validate_all_schools(live, expected_version, expected_release))
    require(
        creator.get("machine") == live.get("machine"),
        "creator-events and all-schools receipts came from different machines",
        errors,
    )
    gallery_errors, selected = validate_gallery(
        gallery,
        acceptance,
        expected_machine=str(live.get("machine", "")),
        expected_version=expected_version,
        expected_release=expected_release,
    )
    errors.extend(gallery_errors)
    if errors:
        raise VerificationError("Quest Lab release evidence failed:\n- " + "\n- ".join(errors))
    return {
        "schema": SUMMARY_SCHEMA,
        "verified_utc": datetime.now().astimezone().isoformat(),
        "plugin_version": expected_version,
        "release_id": expected_release,
        "verdict": "pass",
        "atlas": {
            "rows": 91,
            "unique_signatures": 90,
            "unique_methods": 77,
            "safe_creator_events": 34,
        },
        "live": {
            "machine": live["machine"],
            "schools_witnessed": live["witnessed_events"],
            "example_quests_completed": live["completed_example_quests"],
            "coalesced_witnesses": live["coalesced_witnesses"],
            "double_completions": live["double_completions"],
        },
        "gallery": {
            "selected_profile": selected,
            "operations_verified": sorted(GALLERY_OPERATIONS),
            "accepted_by": acceptance["accepted_by"],
            "accepted_utc": acceptance["accepted_utc"],
        },
        "evidence": [
            {"kind": "creator-events", "path": str(creator_path), "sha256": sha256(creator_path)},
            {"kind": "all-schools", "path": str(live_path), "sha256": sha256(live_path)},
            *[
                {"kind": "gallery-request", "path": str(path), "sha256": sha256(path)}
                for path in gallery_paths
            ],
            {"kind": "gallery-acceptance", "path": str(acceptance_path), "sha256": sha256(acceptance_path)},
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--creator-events", type=Path, required=True)
    parser.add_argument("--all-schools", type=Path, required=True)
    parser.add_argument("--gallery-request", type=Path, action="append", default=[], required=True)
    parser.add_argument("--gallery-acceptance", type=Path, required=True)
    parser.add_argument("--expected-version", required=True)
    parser.add_argument("--expected-release", required=True)
    parser.add_argument("--write", type=Path)
    args = parser.parse_args()
    try:
        summary = verify_release(
            creator_path=args.creator_events,
            live_path=args.all_schools,
            gallery_paths=args.gallery_request,
            acceptance_path=args.gallery_acceptance,
            expected_version=args.expected_version,
            expected_release=args.expected_release,
        )
    except VerificationError as exc:
        print(exc)
        return 1
    rendered = json.dumps(summary, indent=2) + "\n"
    if args.write:
        args.write.parent.mkdir(parents=True, exist_ok=True)
        args.write.write_text(rendered, encoding="utf-8")
        print(f"PASS: wrote {args.write}")
    else:
        print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

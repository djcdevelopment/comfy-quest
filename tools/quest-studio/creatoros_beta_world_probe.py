#!/usr/bin/env python3
"""Target-local, identity-pinned CreatorOS Beta 1 world-seeding pass.

This composes three already bounded game-owned surfaces: the reviewed Field Lodge blueprint,
the parameter-free Signature Hunt fixture, and Runtime's recoverable binding mailbox. It cannot
choose prefabs, arbitrary content, coordinates, commands, or a different world. The caller still
owns launch, graceful shutdown, final world download, and byte-exact AM4 restoration.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import time
from typing import Any

import architectural_live_probe as base


RESULT_SCHEMA = "creatoros-beta1-world-seed/v1"
RUN_REQUEST_SCHEMA = "comfy-quest-runtime-run-control-request/v1"
RUN_RECEIPT_SCHEMA = "comfy-quest-runtime-run-control-receipt/v1"
FIXTURE_SCHEMA = "comfy-questlab-signature-hunt-fixture/v1"
ACTIVE_SCHEMA = "comfy-quest-active-set/v1"
EXPECTED_WORLD = "CreatorOSBeta1"
EXPECTED_WORLD_UID = "4257656027"
EXPECTED_BLUEPRINT = "tn0304-6ue2ukrad7ntjfoa7cvdmvwkcvsccusli5wrfvhq2rnkhtm2ehuq"
EXPECTED_PIECES = 40
EXPECTED_PACK = "slayers-signature-hunt"
EXPECTED_VERSION = "1.0.0"
EXPECTED_CONTENT_HASH = "ad94f708efa9afbb8267cae6336226fc2c59c9d86c777d2309b932842f09c734"
EXPECTED_ENTRY_EXPERIENCE = "slayers-air-drop"
SAFE_ZDO = re.compile(r"^-?[0-9]+:[0-9]+$")


def require_exact_args(args: argparse.Namespace) -> None:
    expected = {
        "world": EXPECTED_WORLD,
        "world_uid": EXPECTED_WORLD_UID,
        "blueprint": EXPECTED_BLUEPRINT,
        "piece_count": EXPECTED_PIECES,
        "pack_id": EXPECTED_PACK,
        "pack_version": EXPECTED_VERSION,
        "content_hash": EXPECTED_CONTENT_HASH,
        "entry_experience": EXPECTED_ENTRY_EXPERIENCE,
    }
    for field, value in expected.items():
        if getattr(args, field) != value:
            raise RuntimeError(f"creatoros_beta_identity_mismatch:{field}")
    base.require_safe_token(args.machine, "machine")
    base.require_safe_token(args.character, "character")
    base.require_safe_token(args.session, "session")
    base.require_sha(args.capture_sha256, "capture")
    base.require_sha(args.blueprint_sha256, "blueprint")
    base.require_sha(args.canonical_pieces_sha256, "canonical_pieces")
    base.require_sha(args.questpack_sha256, "questpack")
    if not args.questpack.is_file() or base.sha256(args.questpack) != args.questpack_sha256:
        raise RuntimeError("questpack_hash_mismatch")


def wait_active(runtime: Path, filename: str, args: argparse.Namespace) -> dict[str, Any]:
    deadline = time.monotonic() + args.request_timeout
    status_path = runtime / "status" / "dev-channel.json"
    active_path = runtime / "active" / "active-set.json"
    last = "missing"
    while time.monotonic() < deadline:
        try:
            status = base.read_json(status_path)
            active = base.read_json(active_path)
            last = str(status.get("last_rejection") or status.get("state") or "unreadable")
            if (status.get("schema") == "comfy-quest-dev-channel-status/v1"
                    and status.get("armed") is True
                    and active.get("schema") == ACTIVE_SCHEMA
                    and active.get("source_channel") == "dev"
                    and active.get("source") == filename
                    and active.get("pack_id") == args.pack_id
                    and active.get("version") == args.pack_version
                    and active.get("content_hash") == args.content_hash
                    and status.get("active_activation_id") == active.get("activation_id")
                    and status.get("active_content_hash") == args.content_hash):
                return active
        except (OSError, ValueError, json.JSONDecodeError):
            pass
        time.sleep(.25)
    raise RuntimeError("beta_pack_activation_timeout:" + last)


def run_control(driver: base.LiveDriver, operation: str, *,
                experience_id: str | None = None,
                binding_zdo: str | None = None) -> dict[str, Any]:
    now = base.utc_now()
    nonce = hashlib.sha256(base.os.urandom(32)).hexdigest()[:10]
    request_id = f"beta-{operation.replace('_', '-')}-{now.strftime('%H%M%S')}-{nonce}"
    request: dict[str, Any] = {
        "schema": RUN_REQUEST_SCHEMA,
        "request_id": request_id,
        "operation": operation,
        "created_utc": base.iso(now),
        "expires_utc": base.iso(now + dt.timedelta(minutes=5)),
        "expected_machine": driver.args.machine,
        "expected_world_uid": driver.args.world_uid,
        "creator_session_id": driver.args.session,
        "confirm_reset": False,
    }
    if experience_id is not None:
        request["experience_id"] = experience_id
    if binding_zdo is not None:
        request["binding_zdo"] = binding_zdo
    runtime = driver.paths["runtime"]
    mailbox = runtime / "requests" / "run-control.json"
    receipt_path = runtime / "receipts" / "run-control" / "pack" / f"{request_id}.json"
    if mailbox.exists() or receipt_path.exists():
        raise RuntimeError("run_control_mailbox_collision")
    base.atomic_json(mailbox, request, create=True)
    receipt = base.wait_json(receipt_path, driver.args.request_timeout)
    expected = {
        "schema": RUN_RECEIPT_SCHEMA,
        "request_id": request_id,
        "operation": operation,
        "machine": driver.args.machine,
        "world_uid": driver.args.world_uid,
        "creator_session_id": driver.args.session,
    }
    if any(receipt.get(key) != value for key, value in expected.items()):
        raise RuntimeError("run_control_receipt_identity_mismatch:" + operation)
    target = driver.receipt_root / f"run-{operation}.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(receipt_path, target)
    if receipt.get("state") != "completed":
        raise RuntimeError(f"run_control_{operation}_{receipt.get('state')}:"
                           + str(receipt.get("detail")))
    return receipt


def validate_fixture(receipt: dict[str, Any], driver: base.LiveDriver) -> dict[str, Any]:
    evidence = Path(str(receipt.get("evidence_path") or "")).resolve()
    fixture_root = (driver.paths["lab"] / "receipts" / "fixtures").resolve()
    if evidence.parent != fixture_root or not evidence.is_file():
        raise RuntimeError("fixture_evidence_path_invalid")
    fixture = base.read_json(evidence)
    expected = {
        "schema": FIXTURE_SCHEMA,
        "fixture_id": "slayers-signature-hunt",
        "fixture_revision": 1,
        "state": "ready",
        "proof_level": "fixture-preparation",
        "request_id": receipt.get("request_id"),
        "machine": driver.args.machine,
        "world_name": driver.args.world,
        "world_uid": driver.args.world_uid,
    }
    if any(fixture.get(key) != value for key, value in expected.items()):
        raise RuntimeError("fixture_evidence_identity_mismatch")
    objects = fixture.get("objects")
    anchor = fixture.get("binding_anchor")
    targets = fixture.get("targets")
    if (not isinstance(objects, dict) or objects.get("expected") != 17
            or objects.get("standing_at_capture") != 17
            or not isinstance(anchor, dict)
            or anchor.get("role") != "marker-loadout-sign"
            or anchor.get("target_kind") != "sign"
            or not SAFE_ZDO.fullmatch(str(anchor.get("zdo_id") or ""))
            or not isinstance(targets, list) or len(targets) != 2):
        raise RuntimeError("fixture_evidence_shape_mismatch")
    target_identity = [(value.get("role"), value.get("prefab"),
                        value.get("matcher_target")) for value in targets]
    if target_identity != [
            ("target-deathsquito", "Deathsquito", "$enemy_deathsquito"),
            ("target-drake", "Hatchling", "$enemy_drake")]:
        raise RuntimeError("fixture_target_identity_mismatch")
    copied = driver.run_root / "signature-hunt-fixture.json"
    shutil.copy2(evidence, copied)
    return fixture


def exact_anchor_candidate(candidates: Any, zdo_id: str) -> dict[str, Any]:
    if not isinstance(candidates, list) or len(candidates) > 32:
        raise RuntimeError("binding_candidate_set_invalid")
    matches = [value for value in candidates if isinstance(value, dict)
               and value.get("binding_zdo") == zdo_id
               and value.get("target_kind") == "sign"]
    if len(matches) != 1:
        raise RuntimeError("fixture_binding_anchor_missing_or_ambiguous")
    distance = matches[0].get("distance_metres")
    if isinstance(distance, bool) or not isinstance(distance, (int, float)) \
            or not math.isfinite(float(distance)) or float(distance) > 25:
        raise RuntimeError("fixture_binding_anchor_distance_invalid")
    return matches[0]


def validate_binding(receipt: dict[str, Any], args: argparse.Namespace,
                     anchor_zdo: str) -> dict[str, Any]:
    change = receipt.get("binding_change")
    applied = change.get("applied") if isinstance(change, dict) else None
    if (not isinstance(change, dict)
            or change.get("schema") != "comfy-quest-runtime-binding-change/v1"
            or change.get("state") != "applied"
            or change.get("binding_zdo") != anchor_zdo
            or change.get("world_id") != args.world_uid
            or not isinstance(applied, dict)
            or applied.get("pack_id") != args.pack_id
            or applied.get("version") != args.pack_version
            or applied.get("experience_id") != args.entry_experience
            or applied.get("binding_id") != "default"
            or applied.get("content_hash") != args.content_hash
            or not base.SAFE_TOKEN.fullmatch(str(applied.get("binding_instance_id") or ""))):
        raise RuntimeError("binding_change_evidence_mismatch")
    return change


def wait_started_run(runtime: Path, args: argparse.Namespace,
                     binding_instance_id: str) -> dict[str, Any]:
    path = runtime / "status" / "runs.json"
    deadline = time.monotonic() + args.request_timeout
    while time.monotonic() < deadline:
        try:
            value = base.read_json(path)
            runs = value.get("runs")
            matches = [run for run in runs if isinstance(run, dict)
                       and run.get("experience_id") == args.entry_experience
                       and run.get("content_hash") == args.content_hash
                       and run.get("binding_instance_id") == binding_instance_id]
            if (value.get("schema") == "comfy-quest-runtime-run-status/v1"
                    and value.get("machine") == args.machine
                    and value.get("world_uid") == args.world_uid
                    and len(matches) == 1):
                return matches[0]
        except (OSError, ValueError, TypeError, json.JSONDecodeError):
            pass
        time.sleep(.25)
    raise RuntimeError("started_run_status_timeout")


def run(args: argparse.Namespace) -> dict[str, Any]:
    require_exact_args(args)
    valheim, run_root = base.validate_roots(args.valheim_root, args.run_root)
    prepare = base.read_json(run_root / "prepare.json")
    expected_prepare = {
        "schema": base.PREPARE_SCHEMA,
        "status": "READY",
        "machine": args.machine,
        "world": args.world,
        "world_uid": args.world_uid,
        "character": args.character,
        "creator_session_id": args.session,
        "blueprint_name": args.blueprint,
        "piece_count": args.piece_count,
        "canonical_pieces_sha256": args.canonical_pieces_sha256,
    }
    if any(prepare.get(key) != value for key, value in expected_prepare.items()):
        raise RuntimeError("beta_prepare_identity_mismatch")

    driver = base.LiveDriver(args, prepare)
    started = base.utc_now()
    failure: str | None = None
    entered = False
    fixture: dict[str, Any] | None = None
    active: dict[str, Any] | None = None
    binding: dict[str, Any] | None = None
    started_run: dict[str, Any] | None = None
    screenshot: dict[str, Any] | None = None
    standing = 0
    try:
        entry_id = prepare["world_entry_request"]["request_id"]
        entry_path = driver.paths["runtime"] / "receipts" / "world-entry" / f"{entry_id}.json"
        entry = base.wait_world_entry(entry_path, entry_id, args.world_timeout)
        expected_entry = {
            "schema": base.WORLD_ENTRY_RECEIPT_SCHEMA,
            "request_id": entry_id,
            "creator_session_id": args.session,
            "state": "entered",
            "machine": args.machine,
            "world_uid": args.world_uid,
            "world_name": args.world,
            "character_profile": args.character,
        }
        if any(entry.get(key) != value for key, value in expected_entry.items()):
            raise RuntimeError("beta_world_entry_identity_mismatch")
        entered = True
        shutil.copy2(entry_path, run_root / "world-entry-receipt.json")

        status = driver.request("runtime", "status")
        if status.get("creator_build_enabled") is not False:
            raise RuntimeError("creator_authority_preexisting")
        check = driver.request("lab", "blueprint_check", driver.lab_fields("blueprint_check"))
        detail = str(check.get("detail") or "")
        if (f"{args.piece_count} buildable piece(s)" not in detail
                or args.canonical_pieces_sha256 not in detail):
            raise RuntimeError("field_lodge_blueprint_check_mismatch")
        before = driver.count(driver.request(
            "lab", "blueprint_count", driver.lab_fields("blueprint_count")))
        if before != 0:
            raise RuntimeError("field_lodge_preexisting_marks")

        authority = driver.request("runtime", "build_on")
        driver.authority_enabled = authority.get("creator_build_enabled") is True
        if not driver.authority_enabled:
            raise RuntimeError("creator_authority_not_enabled")
        driver.build_attempted = True
        built = driver.request("lab", "blueprint_build",
                               driver.lab_fields("blueprint_build", placed=True))
        if driver.count(built) != args.piece_count:
            raise RuntimeError("field_lodge_build_count_mismatch")
        standing = driver.count(driver.request(
            "lab", "blueprint_count", driver.lab_fields("blueprint_count")))
        if standing != args.piece_count:
            raise RuntimeError("field_lodge_standing_count_mismatch")
        diff = driver.request("lab", "blueprint_diff",
                              driver.lab_fields("blueprint_diff", placed=True))
        diff_detail = str(diff.get("detail") or "")
        if (f"capture diff {args.blueprint}: MATCH" not in diff_detail
                or f"expected {args.piece_count}, selected {args.piece_count}, missing 0, extra 0"
                    not in diff_detail):
            raise RuntimeError("field_lodge_diff_mismatch")

        armed = driver.request("runtime", "arm")
        if armed.get("dev_armed") is not True:
            raise RuntimeError("dev_channel_not_armed")
        source_name = ("creatoros-beta1-seed-" + args.questpack_sha256[:12]
                       + "-" + hashlib.sha256(base.os.urandom(32)).hexdigest()[:8]
                       + ".questpack")
        target = driver.paths["runtime"] / "inbox-dev" / source_name
        if target.exists():
            raise RuntimeError("dev_questpack_collision")
        base.atomic_copy(args.questpack, target)
        if base.sha256(target) != args.questpack_sha256:
            raise RuntimeError("dev_questpack_stage_mismatch")
        active = wait_active(driver.paths["runtime"], source_name, args)

        fixture_request = driver.request("lab", "signature_hunt_prepare")
        fixture = validate_fixture(fixture_request, driver)
        anchor_zdo = str(fixture["binding_anchor"]["zdo_id"])
        candidates = run_control(driver, "list_binding_candidates")
        exact_anchor_candidate(candidates.get("binding_candidates"), anchor_zdo)
        bound = run_control(driver, "bind_selected_experience",
                            experience_id=args.entry_experience,
                            binding_zdo=anchor_zdo)
        binding = validate_binding(bound, args, anchor_zdo)
        started_run = wait_started_run(
            driver.paths["runtime"], args,
            str(binding["applied"]["binding_instance_id"]))
        fixture_status = driver.request("lab", "signature_hunt_status")
        if "17 exact-mark objects standing" not in str(fixture_status.get("detail") or ""):
            raise RuntimeError("signature_hunt_standing_count_mismatch")
        standing = driver.count(driver.request(
            "lab", "blueprint_count", driver.lab_fields("blueprint_count")))
        if standing != args.piece_count:
            raise RuntimeError("field_lodge_post_bind_count_mismatch")
        screenshot = base.capture_screenshot(run_root)
    except Exception as error:
        failure = f"{type(error).__name__}:{error}"
    finally:
        try:
            driver.disable_authority()
        except Exception as error:
            failure = failure or f"cleanup_build_off:{type(error).__name__}:{error}"
        if entered:
            try:
                final_status = driver.request("runtime", "status", allow_failure=True)
                if (final_status.get("state") != "completed"
                        or final_status.get("creator_build_enabled") is not False):
                    failure = failure or "creator_authority_cleanup_unverified"
            except Exception as error:
                failure = failure or f"cleanup_status:{type(error).__name__}:{error}"
            try:
                driver.request("runtime", "disarm", allow_failure=True)
            except Exception as error:
                failure = failure or f"cleanup_disarm:{type(error).__name__}:{error}"

    pair_root = driver.paths["lab"] / "blueprints"
    pair = {
        f"{args.blueprint}.capture.json": base.pin(
            pair_root / f"{args.blueprint}.capture.json"),
        f"{args.blueprint}.blueprint": base.pin(
            pair_root / f"{args.blueprint}.blueprint"),
    }
    canonical = (pair[f"{args.blueprint}.capture.json"]["sha256"] == args.capture_sha256
                 and pair[f"{args.blueprint}.blueprint"]["sha256"] == args.blueprint_sha256)
    if not canonical:
        failure = failure or "canonical_artifact_drift"
    result = {
        "schema": RESULT_SCHEMA,
        "status": "PASS" if failure is None else "FAIL",
        "failure": failure,
        "started_utc": base.iso(started),
        "completed_utc": base.iso(base.utc_now()),
        "identity": {
            "machine": args.machine,
            "world": args.world,
            "world_uid": args.world_uid,
            "character": args.character,
            "creator_session_id": args.session,
        },
        "field_lodge": {
            "blueprint": args.blueprint,
            "placement_mode": "ground",
            "piece_count": standing,
            "canonical_pieces_sha256": args.canonical_pieces_sha256,
            "canonical_pair": pair,
        },
        "signature_hunt": {
            "fixture_id": fixture.get("fixture_id") if fixture else None,
            "preparation_id": fixture.get("preparation_id") if fixture else None,
            "object_count": fixture.get("objects", {}).get("standing_at_capture")
                if fixture else None,
            "binding_anchor": fixture.get("binding_anchor") if fixture else None,
            "targets": fixture.get("targets") if fixture else None,
        },
        "questpack": {
            "pack_id": args.pack_id,
            "version": args.pack_version,
            "content_hash": args.content_hash,
            "sha256": args.questpack_sha256,
            "activation_id": active.get("activation_id") if active else None,
        },
        "binding": binding,
        "started_run": started_run,
        "sequence": driver.sequence,
        "screenshot": screenshot,
        "assertions": {
            "fresh_world_entered": entered,
            "field_lodge_exact_match": failure is None and standing == args.piece_count,
            "fixture_exact_count": fixture is not None
                and fixture.get("objects", {}).get("standing_at_capture") == 17,
            "entry_anchor_bound": binding is not None,
            "entry_experience_started": started_run is not None,
            "creator_build_disabled": not driver.authority_enabled,
            "canonical_artifacts_unchanged": canonical,
        },
    }
    base.atomic_json(run_root / "creatoros-beta1-world-seed.json", result)
    return result


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--valheim-root", type=Path, required=True)
    parser.add_argument("--run-root", type=Path, required=True)
    parser.add_argument("--unity-root", type=Path, required=True)
    parser.add_argument("--machine", required=True)
    parser.add_argument("--world", required=True)
    parser.add_argument("--world-uid", required=True)
    parser.add_argument("--character", required=True)
    parser.add_argument("--session", required=True)
    parser.add_argument("--blueprint", required=True)
    parser.add_argument("--piece-count", type=int, required=True)
    parser.add_argument("--capture-sha256", required=True)
    parser.add_argument("--blueprint-sha256", required=True)
    parser.add_argument("--canonical-pieces-sha256", required=True)
    parser.add_argument("--questpack", type=Path, required=True)
    parser.add_argument("--questpack-sha256", required=True)
    parser.add_argument("--pack-id", required=True)
    parser.add_argument("--pack-version", required=True)
    parser.add_argument("--content-hash", required=True)
    parser.add_argument("--entry-experience", required=True)
    parser.add_argument("--world-timeout", type=int, default=600)
    parser.add_argument("--request-timeout", type=int, default=90)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    # LiveDriver's generic placement helper reads this additive field.
    args.placement_mode = "ground"
    args.x = args.y = args.z = args.yaw = 0.0
    try:
        result = run(args)
        print(json.dumps(result, sort_keys=True))
        return 0 if result.get("status") == "PASS" else 1
    except Exception as error:
        print(json.dumps({"schema": RESULT_SCHEMA, "status": "ERROR",
                          "error": type(error).__name__, "detail": str(error)},
                         sort_keys=True))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

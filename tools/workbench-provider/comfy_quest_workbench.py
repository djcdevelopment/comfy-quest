"""Bounded Comfy Quest tools for an independently hosted Isolate gateway.

This module is a product-owned provider, not an MCP server.  Isolate owns the
authenticated gateway and lifecycle; this provider only speaks the fixed Runtime
mailboxes and reads their correlated receipts.  It accepts no command, key, process,
or filesystem-path input.
"""

from __future__ import annotations

import datetime as dt
import json
import os
import re
import time
import uuid
from pathlib import Path
from typing import Any


CREATOR_OPERATIONS = {"status", "arm", "disarm", "build_on", "build_off"}
RUN_CONTROL_OPERATIONS = {
    "preview_reset",
    "apply_reset",
    "select_experience",
    "list_binding_candidates",
    "bind_selected_experience",
    "restore_binding",
}
RESET_OPERATIONS = {"preview_reset", "apply_reset"}
SAFE_TOKEN = re.compile(r"^[A-Za-z0-9_.-]+$")
SAFE_ZDO = re.compile(r"^-?[0-9]+:[0-9]+$")
MAX_REQUEST_BYTES = 16 * 1024
MAX_STATUS_BYTES = 256 * 1024


def get_tools() -> list:
    """Return the provider functions mounted by Isolate's gateway."""
    return [
        quest_runtime_status,
        quest_runtime_receipts,
        quest_runtime_creator_request,
        quest_runtime_run_control,
    ]


def _runtime_root() -> Path:
    configured = os.environ.get("COMFY_QUEST_RUNTIME_ROOT", "").strip()
    if not configured:
        raise RuntimeError("COMFY_QUEST_RUNTIME_ROOT must be set by the Isolate release profile")
    return Path(configured).resolve()


def _token(value: str, label: str, maximum: int) -> str:
    if not isinstance(value, str) or not value or len(value) > maximum or not SAFE_TOKEN.fullmatch(value):
        raise ValueError(f"{label} must be a 1-{maximum} character safe identifier")
    return value


def _world_uid(value: str) -> str:
    if not isinstance(value, str) or not re.fullmatch(r"-?[0-9]{1,20}", value):
        raise ValueError("expected_world_uid must be a non-zero signed integer string")
    if int(value) == 0:
        raise ValueError("expected_world_uid must be non-zero")
    return value


def _timeout(value: int) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or not 2 <= value <= 60:
        raise ValueError("timeout_seconds must be an integer from 2 through 60")
    return value


def _read_json(path: Path, maximum: int, *, required: bool = False) -> dict[str, Any] | None:
    try:
        size = path.stat().st_size
    except FileNotFoundError:
        if required:
            raise
        return None
    if size <= 0 or size > maximum:
        raise ValueError(f"{path.name} has an invalid bounded size")
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream)
    if not isinstance(value, dict):
        raise ValueError(f"{path.name} must contain a JSON object")
    return value


def _write_exclusive(path: Path, document: dict[str, Any]) -> None:
    payload = json.dumps(document, ensure_ascii=False, separators=(",", ":"), sort_keys=True).encode("utf-8")
    if len(payload) <= 0 or len(payload) > MAX_REQUEST_BYTES:
        raise ValueError("Runtime request exceeds the bounded request size")
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError as exc:
        raise RuntimeError(f"Runtime mailbox {path.name} is already occupied; inspect its receipt before retrying") from exc
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
    except Exception:
        try:
            path.unlink()
        except FileNotFoundError:
            pass
        raise


def _retract_request(path: Path, request_id: str) -> None:
    """Remove only this provider call's still-pending mailbox document.

    Runtime claims requests by moving the fixed mailbox before dispatch. If that claim already
    happened, the request expiry remains the authority that prevents a late mutation.
    """
    try:
        value = _read_json(path, MAX_REQUEST_BYTES)
        if value is not None and value.get("request_id") == request_id:
            path.unlink(missing_ok=True)
    except (OSError, ValueError, json.JSONDecodeError):
        return


def _wait_for_receipt(
    path: Path,
    request_id: str,
    operation: str,
    timeout_seconds: int,
    mailbox: Path,
) -> dict[str, Any]:
    deadline = time.monotonic() + timeout_seconds
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        try:
            receipt = _read_json(path, MAX_STATUS_BYTES)
            if receipt is not None:
                if receipt.get("request_id") != request_id or receipt.get("operation") != operation:
                    raise ValueError("Runtime returned a receipt with mismatched correlation identity")
                return receipt
        except (OSError, json.JSONDecodeError) as error:
            last_error = error
        time.sleep(0.1)
    _retract_request(mailbox, request_id)
    detail = f"; last read failed with {type(last_error).__name__}" if last_error else ""
    raise TimeoutError(f"Runtime did not return receipt {request_id} within {timeout_seconds} seconds{detail}")


def _request_id(kind: str, operation: str) -> str:
    stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    return f"mcp-{kind}-{operation}-{stamp}-{uuid.uuid4().hex[:8]}"


def _window(timeout_seconds: int) -> tuple[str, str]:
    now = dt.datetime.now(dt.timezone.utc)
    # Expire before the synchronous tool wait ends. A paused game may claim a request after the
    # caller has stopped waiting; Runtime must then reject it rather than applying a late change.
    expires = now + dt.timedelta(seconds=max(1.0, timeout_seconds - 0.5))
    return now.isoformat(), expires.isoformat()


def _pick(value: dict[str, Any] | None, names: tuple[str, ...]) -> dict[str, Any] | None:
    if value is None:
        return None
    return {name: value[name] for name in names if name in value}


def quest_runtime_status(max_runs: int = 32) -> dict[str, Any]:
    """Read bounded Runtime, active-pack, world-entry, and run status without mutation.

    Participant identities are deliberately reduced to a count.  The tool never reads
    authored quest text or accepts a path.
    """
    if isinstance(max_runs, bool) or not isinstance(max_runs, int) or not 1 <= max_runs <= 64:
        raise ValueError("max_runs must be an integer from 1 through 64")
    root = _runtime_root()
    release_hash = os.environ.get("COMFY_QUEST_PROVIDER_SHA256", "").strip().lower()
    if release_hash and not re.fullmatch(r"[0-9a-f]{64}", release_hash):
        raise ValueError("COMFY_QUEST_PROVIDER_SHA256 is not a SHA-256 identity")
    active = _read_json(root / "active" / "active-set.json", MAX_STATUS_BYTES)
    dev = _read_json(root / "status" / "dev-channel.json", MAX_STATUS_BYTES)
    world = _read_json(root / "status" / "world-entry.json", MAX_STATUS_BYTES)
    run_status = _read_json(root / "status" / "runs.json", MAX_STATUS_BYTES)
    runs: list[dict[str, Any]] = []
    for value in list((run_status or {}).get("runs") or [])[:max_runs]:
        if not isinstance(value, dict):
            continue
        public = _pick(value, (
            "run_id", "scope_id", "experience_id", "binding_zdo", "content_hash",
            "stage_id", "outcome", "reward_policy",
        )) or {}
        participants = value.get("participant_ids")
        public["participant_count"] = len(participants) if isinstance(participants, list) else 0
        runs.append(public)

    def packs(directory: str) -> list[str]:
        lane = root / directory
        if not lane.is_dir():
            return []
        return sorted(
            value.name for value in lane.iterdir()
            if value.is_file() and value.suffix == ".questpack" and len(value.name) <= 160
        )[:256]

    return {
        "schema": "comfy-quest-workbench-status/v1",
        "provider": {
            "module": "comfy_quest_workbench",
            "release_sha256": release_hash or None,
        },
        "active": _pick(active, (
            "schema", "pack_id", "version", "content_hash", "activation_id",
            "activated_utc", "source", "source_channel", "experience_id",
        )),
        "dev_channel": _pick(dev, (
            "schema", "session_id", "observed_utc", "armed", "armed_utc", "state",
            "active_pack_id", "active_version", "active_content_hash",
            "active_activation_id", "current_stage_id", "last_rejection",
        )),
        "world_entry": _pick(world, (
            "schema", "request_id", "creator_session_id", "state", "detail", "machine",
            "expected_world_uid", "world_uid", "completed_utc",
        )),
        "runs_observed_utc": (run_status or {}).get("observed_utc"),
        "runs": runs,
        "production_inbox": packs("inbox"),
        "dev_inbox": packs("inbox-dev"),
        "pending": {
            "creator_request": (root / "requests" / "creator-request.json").exists(),
            "run_control": (root / "requests" / "run-control.json").exists(),
        },
    }


def quest_runtime_receipts(
    limit: int = 20,
    operation: str = "",
    experience_id: str = "",
    run_id: str = "",
) -> dict[str, Any]:
    """Read the bounded live Runtime receipt window, newest first.

    Optional filters are safe identifiers, never paths. Runtime receipts already redact
    authored chat text and player identity; this tool does not read logs or archived files.
    """
    if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 100:
        raise ValueError("limit must be an integer from 1 through 100")
    if operation:
        operation = _token(operation, "operation", 80)
    if experience_id:
        experience_id = _token(experience_id, "experience_id", 80)
    if run_id:
        run_id = _token(run_id, "run_id", 96)
    directory = _runtime_root() / "receipts"
    files = sorted(
        (value for value in directory.glob("*.json") if value.is_file()),
        key=lambda value: value.name,
        reverse=True,
    )[:512]
    receipts: list[dict[str, Any]] = []
    unreadable: list[str] = []
    for path in files:
        try:
            receipt = _read_json(path, MAX_STATUS_BYTES, required=True)
        except (OSError, ValueError, json.JSONDecodeError):
            unreadable.append(path.name)
            continue
        if receipt.get("schema") != "comfy-quest-runtime-receipt/v1":
            unreadable.append(path.name)
            continue
        if operation and receipt.get("operation") != operation:
            continue
        if experience_id and receipt.get("experience_id") != experience_id:
            continue
        if run_id and receipt.get("run_id") != run_id:
            continue
        receipts.append(receipt)
        if len(receipts) >= limit:
            break
    return {
        "schema": "comfy-quest-workbench-receipts/v1",
        "window": "live",
        "retention_bound": 512,
        "scanned": len(files),
        "receipts": receipts,
        "unreadable_receipts": unreadable[:20],
    }


def quest_runtime_creator_request(
    operation: str,
    expected_machine: str,
    expected_world_uid: str,
    creator_session_id: str,
    timeout_seconds: int = 20,
) -> dict[str, Any]:
    """Send one existing identity-pinned Runtime creator operation and await its receipt.

    Allowed operations are status, arm, disarm, build_on, and build_off.  This is not
    a console or input bridge and cannot load content, cast a Charm, or name a path.
    """
    if not isinstance(operation, str):
        raise ValueError("operation must be a string")
    operation = operation.strip().lower()
    if operation not in CREATOR_OPERATIONS:
        raise ValueError("operation is not in the Runtime creator allowlist")
    machine = _token(expected_machine, "expected_machine", 80)
    world = _world_uid(expected_world_uid)
    session = _token(creator_session_id, "creator_session_id", 80)
    timeout = _timeout(timeout_seconds)
    request_id = _request_id("creator", operation)
    created, expires = _window(timeout)
    request = {
        "schema": "comfy-quest-runtime-request/v1",
        "request_id": request_id,
        "operation": operation,
        "created_utc": created,
        "expires_utc": expires,
        "expected_machine": machine,
        "expected_world_uid": world,
        "creator_session_id": session,
    }
    root = _runtime_root()
    mailbox = root / "requests" / "creator-request.json"
    _write_exclusive(mailbox, request)
    receipt = _wait_for_receipt(
        root / "receipts" / "creator-requests" / f"{request_id}.json",
        request_id,
        operation,
        timeout,
        mailbox,
    )
    if receipt.get("schema") != "comfy-quest-runtime-request-receipt/v1":
        raise ValueError("Runtime returned an unexpected creator receipt schema")
    rejected = receipt.get("state") == "rejected"
    detail = receipt.get("detail")
    if (receipt.get("machine", "").casefold() != machine.casefold()
            and not (rejected and detail == "creator_machine_mismatch")):
        raise ValueError("Runtime returned a creator receipt for a different machine")
    if (receipt.get("world_uid") != world
            and not (rejected and detail in {"creator_world_mismatch", "creator_world_not_loaded"})):
        raise ValueError("Runtime returned a creator receipt for a different world")
    if receipt.get("creator_session_id") != session:
        raise ValueError("Runtime returned a creator receipt for a different Creator Session")
    return {
        "schema": "comfy-quest-workbench-creator-result/v1",
        "ok": receipt.get("state") == "completed",
        "request_id": request_id,
        "receipt": receipt,
    }


def quest_runtime_run_control(
    operation: str,
    expected_machine: str,
    expected_world_uid: str,
    creator_session_id: str,
    run_id: str = "",
    experience_id: str = "",
    binding_zdo: str = "",
    binding_change_id: str = "",
    preview_token: str = "",
    confirm_reset: bool = False,
    timeout_seconds: int = 20,
) -> dict[str, Any]:
    """Send one existing bounded Runtime run-control operation and await its receipt.

    The Runtime itself enforces exact world identity, private-world mutation authority,
    activated-pack membership, nearby binding candidates, prerequisite state, reset
    preview tokens, and recovery.  No arbitrary path, command, or input is accepted.
    """
    if not isinstance(operation, str):
        raise ValueError("operation must be a string")
    operation = operation.strip().lower()
    if operation not in RUN_CONTROL_OPERATIONS:
        raise ValueError("operation is not in the Runtime run-control allowlist")
    machine = _token(expected_machine, "expected_machine", 80)
    world = _world_uid(expected_world_uid)
    session = _token(creator_session_id, "creator_session_id", 80)
    timeout = _timeout(timeout_seconds)
    reset = operation in RESET_OPERATIONS
    select = operation in {"select_experience", "bind_selected_experience"}
    bind = operation == "bind_selected_experience"
    restore = operation == "restore_binding"

    if reset:
        run_id = _token(run_id, "run_id", 96)
    elif run_id:
        raise ValueError("run_id is allowed only for reset operations")
    if select:
        experience_id = _token(experience_id, "experience_id", 80)
    elif experience_id:
        raise ValueError("experience_id is allowed only for selection and binding")
    if bind or restore:
        if not isinstance(binding_zdo, str) or len(binding_zdo) > 80 or not SAFE_ZDO.fullmatch(binding_zdo):
            raise ValueError("binding_zdo must be an exact signed-user:unsigned-object identity")
    elif binding_zdo:
        raise ValueError("binding_zdo is allowed only for binding and restore")
    if restore:
        binding_change_id = _token(binding_change_id, "binding_change_id", 80)
    elif binding_change_id:
        raise ValueError("binding_change_id is allowed only for restore_binding")
    if operation == "apply_reset":
        if confirm_reset is not True:
            raise ValueError("apply_reset requires confirm_reset=true")
        preview_token = _token(preview_token, "preview_token", 80)
    elif confirm_reset or preview_token:
        raise ValueError("reset confirmation is allowed only for apply_reset")

    request_id = _request_id("run", operation)
    created, expires = _window(timeout)
    request: dict[str, Any] = {
        "schema": "comfy-quest-runtime-run-control-request/v1",
        "request_id": request_id,
        "operation": operation,
        "created_utc": created,
        "expires_utc": expires,
        "expected_machine": machine,
        "expected_world_uid": world,
        "creator_session_id": session,
        "run_id": run_id,
        "confirm_reset": confirm_reset,
    }
    if experience_id:
        request["experience_id"] = experience_id
    if binding_zdo:
        request["binding_zdo"] = binding_zdo
    if binding_change_id:
        request["binding_change_id"] = binding_change_id
    if preview_token:
        request["preview_token"] = preview_token

    root = _runtime_root()
    mailbox = root / "requests" / "run-control.json"
    _write_exclusive(mailbox, request)
    scope = run_id if reset else "pack"
    receipt = _wait_for_receipt(
        root / "receipts" / "run-control" / scope / f"{request_id}.json",
        request_id,
        operation,
        timeout,
        mailbox,
    )
    if receipt.get("schema") != "comfy-quest-runtime-run-control-receipt/v1":
        raise ValueError("Runtime returned an unexpected run-control receipt schema")
    rejected = receipt.get("state") == "rejected"
    detail = receipt.get("detail")
    if (receipt.get("machine", "").casefold() != machine.casefold()
            and not (rejected and detail == "runtime_machine_mismatch")):
        raise ValueError("Runtime returned a run-control receipt for a different machine")
    if (receipt.get("world_uid") != world
            and not (rejected and detail in {"runtime_world_mismatch", "runtime_world_not_loaded"})):
        raise ValueError("Runtime returned a run-control receipt for a different world")
    if receipt.get("creator_session_id") != session:
        raise ValueError("Runtime returned run-control evidence for a different Creator Session")
    return {
        "schema": "comfy-quest-workbench-run-control-result/v1",
        "ok": receipt.get("state") in {"completed", "previewed"},
        "request_id": request_id,
        "receipt": receipt,
    }

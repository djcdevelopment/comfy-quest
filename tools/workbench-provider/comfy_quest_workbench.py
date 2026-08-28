"""Bounded Comfy Quest tools for an independently hosted Isolate gateway.

This module is a product-owned provider, not an MCP server.  Isolate owns the
authenticated gateway and lifecycle; this provider only speaks the fixed Runtime and
Quest Lab mailboxes and reads their correlated receipts.  It accepts no command, key,
process, host, or filesystem-path input.
"""

from __future__ import annotations

import datetime as dt
import hashlib
import json
import math
import os
import re
import threading
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
SAFE_BLUEPRINT = re.compile(r"^[a-z0-9_-]{1,64}$")
SAFE_ARTIFACT = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,159}$")
SAFE_DIAGNOSTIC = re.compile(r"^[a-z][a-z0-9_]{0,79}$")
MAX_REQUEST_BYTES = 16 * 1024
MAX_STATUS_BYTES = 256 * 1024
MAX_GODBUILD_ARTIFACT_BYTES = 8 * 1024 * 1024
MAX_GODBUILD_ARTIFACTS = 32
MAX_GODBUILD_TOTAL_BYTES = 32 * 1024 * 1024
_LAB_REPLAY_LOCK = threading.Lock()


def get_tools() -> list:
    """Return the provider functions mounted by Isolate's gateway."""
    return [
        quest_runtime_status,
        quest_runtime_receipts,
        quest_runtime_creator_request,
        quest_runtime_run_control,
        quest_lab_replay,
    ]


def _runtime_root() -> Path:
    configured = os.environ.get("COMFY_QUEST_RUNTIME_ROOT", "").strip()
    if not configured:
        raise RuntimeError("COMFY_QUEST_RUNTIME_ROOT must be set by the Isolate release profile")
    return Path(configured).resolve()


def _profile_root(variable: str, label: str) -> Path:
    """Resolve one non-caller-controlled root supplied by the Isolate release profile."""
    configured = os.environ.get(variable, "").strip()
    if not configured:
        raise RuntimeError(f"{variable} must be set by the Isolate release profile")
    candidate = Path(configured)
    if not candidate.is_absolute():
        raise RuntimeError(f"{variable} must be an absolute release-profile path")
    try:
        resolved = candidate.resolve(strict=True)
    except OSError as error:
        raise RuntimeError(f"{label} release-profile root is unavailable") from error
    if not resolved.is_dir() or resolved.parent == resolved:
        raise RuntimeError(f"{label} release-profile root is unsafe")
    return resolved


def _lab_root() -> Path:
    return _profile_root("COMFY_QUEST_LAB_ROOT", "Quest Lab")


def _reviewed_godbuild_root() -> Path:
    return _profile_root("COMFY_QUEST_REVIEWED_GODBUILD_ROOT", "reviewed Godbuild")


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


def _write_exclusive(
    path: Path,
    document: dict[str, Any],
    *,
    authority: str = "Runtime",
) -> None:
    payload = json.dumps(document, ensure_ascii=False, separators=(",", ":"), sort_keys=True).encode("utf-8")
    if len(payload) <= 0 or len(payload) > MAX_REQUEST_BYTES:
        raise ValueError("Runtime request exceeds the bounded request size")
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError as exc:
        raise RuntimeError(
            f"{authority} mailbox {path.name} is already occupied; "
            "inspect its receipt before retrying"
        ) from exc
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


def _wait_for_receipt(
    path: Path,
    request_id: str,
    operation: str,
    timeout_seconds: int,
    *,
    authority: str = "Runtime",
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
    detail = f"; last read failed with {type(last_error).__name__}" if last_error else ""
    raise TimeoutError(
        f"{authority} did not return receipt {request_id} "
        f"within {timeout_seconds} seconds; its expired mailbox request remains for "
        f"the authority to claim and reject{detail}"
    )


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


def _bounded_child(root: Path, candidate: Path, label: str, *, directory: bool = False) -> Path:
    try:
        resolved = candidate.resolve(strict=True)
    except OSError as error:
        raise ValueError(f"{label} is unavailable") from error
    try:
        resolved.relative_to(root)
    except ValueError as error:
        raise ValueError(f"{label} escaped its release-profile root") from error
    if directory and not resolved.is_dir():
        raise ValueError(f"{label} is not a directory")
    if not directory and not resolved.is_file():
        raise ValueError(f"{label} is not a file")
    return resolved


def _read_bounded_bytes(path: Path, maximum: int, label: str) -> bytes:
    try:
        size = path.stat().st_size
    except OSError as error:
        raise ValueError(f"{label} is unavailable") from error
    if size <= 0 or size > maximum:
        raise ValueError(f"{label} has an invalid bounded size")
    try:
        value = path.read_bytes()
    except OSError as error:
        raise ValueError(f"{label} could not be read") from error
    if len(value) != size:
        raise ValueError(f"{label} changed while it was being read")
    return value


def _sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def _json_object(value: bytes, label: str) -> dict[str, Any]:
    try:
        document = json.loads(value.decode("utf-8-sig"))
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{label} is not valid UTF-8 JSON") from error
    if not isinstance(document, dict):
        raise ValueError(f"{label} must contain a JSON object")
    return document


def _reviewed_godbuild(name: str) -> dict[str, Any]:
    """Validate one release-mounted reviewed bundle without accepting a caller path.

    Creator Session's source-side drift gate remains release authority. At the target we
    independently require its replay declaration and verify every artifact byte/size/hash
    in that manifest before either replay artifact is staged.
    """
    root = _reviewed_godbuild_root()
    source = _bounded_child(root, root / name, "reviewed Godbuild", directory=True)
    manifest_path = _bounded_child(source, source / "manifest.json", "Godbuild manifest")
    manifest_bytes = _read_bounded_bytes(
        manifest_path, MAX_STATUS_BYTES, "Godbuild manifest")
    manifest = _json_object(manifest_bytes, "Godbuild manifest")
    replay = manifest.get("replay")
    artifacts = manifest.get("artifacts")
    expected_authority = f"{name}.capture.json + {name}.blueprint"
    if (manifest.get("schema") != "comfy-quest-godbuild/v1" or manifest.get("name") != name):
        raise ValueError("reviewed Godbuild identity is invalid")
    if (manifest.get("source_schema") != "comfy-questlab-capture/v1"):
        raise ValueError("reviewed Godbuild source schema is unsupported")
    if (not isinstance(replay, dict)
            or replay.get("authority") != expected_authority
            or replay.get("check_before_build") is not True
            or replay.get("post_build_proof") != "blueprint_diff must report MATCH"):
        raise ValueError("reviewed Godbuild does not authorize check-before-build replay")
    if not isinstance(artifacts, dict) or not 1 <= len(artifacts) <= MAX_GODBUILD_ARTIFACTS:
        raise ValueError("reviewed Godbuild artifact inventory is invalid")

    required = {
        f"{name}.capture.json",
        f"{name}.blueprint",
        "plan.json",
        "preview.svg",
    }
    if not required.issubset(artifacts):
        raise ValueError("reviewed Godbuild bundle is incomplete")

    verified: dict[str, dict[str, Any]] = {}
    total_bytes = 0
    for leaf, record in artifacts.items():
        if (not isinstance(leaf, str) or not SAFE_ARTIFACT.fullmatch(leaf)
                or Path(leaf).name != leaf or leaf in {".", ".."}):
            raise ValueError("reviewed Godbuild artifact name is unsafe")
        if not isinstance(record, dict):
            raise ValueError(f"reviewed Godbuild artifact record is invalid: {leaf}")
        expected_bytes = record.get("bytes")
        expected_sha = record.get("sha256")
        if (isinstance(expected_bytes, bool) or not isinstance(expected_bytes, int)
                or not 1 <= expected_bytes <= MAX_GODBUILD_ARTIFACT_BYTES):
            raise ValueError(f"reviewed Godbuild artifact size is invalid: {leaf}")
        total_bytes += expected_bytes
        if total_bytes > MAX_GODBUILD_TOTAL_BYTES:
            raise ValueError("reviewed Godbuild artifact inventory exceeds its total byte bound")
        if not isinstance(expected_sha, str) or not re.fullmatch(r"[0-9a-f]{64}", expected_sha):
            raise ValueError(f"reviewed Godbuild artifact hash is invalid: {leaf}")
        path = _bounded_child(source, source / leaf, f"reviewed Godbuild artifact {leaf}")
        data = _read_bounded_bytes(path, MAX_GODBUILD_ARTIFACT_BYTES, leaf)
        if len(data) != expected_bytes or _sha256(data) != expected_sha:
            raise ValueError(f"reviewed Godbuild artifact hash mismatch: {leaf}")
        verified[leaf] = {
            "bytes": expected_bytes,
            "sha256": expected_sha,
            "data": data,
        }

    capture_leaf = f"{name}.capture.json"
    blueprint_leaf = f"{name}.blueprint"
    capture = _json_object(verified[capture_leaf]["data"], capture_leaf)
    source_hash = manifest.get("source_pieces_sha256")
    pieces = capture.get("Pieces")
    capture_count = capture.get("PieceCount")
    capture_radius = capture.get("RadiusMetres")
    if (capture.get("Schema") != "comfy-questlab-capture/v1"
            or capture.get("Name") != name
            or capture.get("Selection") not in {"mine", "lab"}
            or not isinstance(source_hash, str)
            or not re.fullmatch(r"[0-9a-f]{64}", source_hash)
            or capture.get("PiecesSha256") != source_hash
            or isinstance(capture_count, bool)
            or not isinstance(capture_count, int)
            or not 1 <= capture_count <= 2048
            or isinstance(capture_radius, bool)
            or not isinstance(capture_radius, (int, float))
            or not math.isfinite(float(capture_radius))
            or not 1 <= float(capture_radius) <= 40
            or not isinstance(pieces, list)
            or capture_count != len(pieces)
            or manifest.get("piece_count") != len(pieces)):
        raise ValueError("reviewed Godbuild capture identity is inconsistent")
    try:
        blueprint = verified[blueprint_leaf]["data"].decode("utf-8")
    except UnicodeError as error:
        raise ValueError("reviewed Godbuild blueprint is not UTF-8") from error
    lines = blueprint.splitlines()
    expected_description = (
        f"#Description:Deterministic projection of {source_hash}; "
        "metadata is in the .capture.json sidecar."
    )
    if (not lines or lines[0] != f"#Name:{name}"
            or len(lines) < 4 or lines[2] != expected_description
            or lines[3] != "#Pieces"):
        raise ValueError("reviewed Godbuild blueprint identity is inconsistent")

    return {
        "name": name,
        "manifest_sha256": _sha256(manifest_bytes),
        "source_pieces_sha256": source_hash,
        "piece_count": len(pieces),
        "artifacts": verified,
    }


def _stage_reviewed_godbuild(bundle: dict[str, Any], lab: Path) -> dict[str, Any]:
    """Replace only the reviewed capture/blueprint pair with in-process rollback."""
    target_root = lab / "blueprints"
    target_root.mkdir(parents=True, exist_ok=True)
    try:
        target_root = target_root.resolve(strict=True)
        target_root.relative_to(lab)
    except (OSError, ValueError) as error:
        raise ValueError("Quest Lab blueprint directory escaped its release-profile root") from error

    name = bundle["name"]
    records: list[dict[str, Any]] = []
    for leaf in (f"{name}.capture.json", f"{name}.blueprint"):
        target = target_root / leaf
        temporary = target.with_name(target.name + ".workbench-staging")
        previous = target.with_name(target.name + ".workbench-prior")
        if previous.exists():
            raise RuntimeError(f"Godbuild recovery file already exists: {previous.name}")
        try:
            temporary.unlink()
        except FileNotFoundError:
            pass
        records.append({
            "leaf": leaf,
            "target": target,
            "temporary": temporary,
            "previous": previous,
            "data": bundle["artifacts"][leaf]["data"],
            "sha256": bundle["artifacts"][leaf]["sha256"],
            "bytes": bundle["artifacts"][leaf]["bytes"],
            "moved_previous": False,
            "installed": False,
        })

    try:
        for record in records:
            descriptor = os.open(
                record["temporary"], os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            try:
                with os.fdopen(descriptor, "wb") as stream:
                    stream.write(record["data"])
                    stream.flush()
                    os.fsync(stream.fileno())
            except Exception:
                try:
                    os.close(descriptor)
                except OSError:
                    pass
                raise
            staged = _read_bounded_bytes(
                record["temporary"], MAX_GODBUILD_ARTIFACT_BYTES, record["leaf"])
            if _sha256(staged) != record["sha256"]:
                raise ValueError(f"Godbuild staging hash mismatch: {record['leaf']}")

        for record in records:
            if record["target"].exists():
                os.replace(record["target"], record["previous"])
                record["moved_previous"] = True
            os.replace(record["temporary"], record["target"])
            record["installed"] = True

        for record in records:
            installed = _read_bounded_bytes(
                record["target"], MAX_GODBUILD_ARTIFACT_BYTES, record["leaf"])
            if len(installed) != record["bytes"] or _sha256(installed) != record["sha256"]:
                raise ValueError(f"Godbuild deployment hash mismatch: {record['leaf']}")
    except Exception:
        for record in reversed(records):
            if record["installed"]:
                try:
                    record["target"].unlink()
                except FileNotFoundError:
                    pass
            if record["moved_previous"] and record["previous"].exists():
                os.replace(record["previous"], record["target"])
        raise
    finally:
        for record in records:
            try:
                record["temporary"].unlink()
            except FileNotFoundError:
                pass

    for record in records:
        try:
            record["previous"].unlink()
        except FileNotFoundError:
            pass
    return {
        "manifest_sha256": bundle["manifest_sha256"],
        "source_pieces_sha256": bundle["source_pieces_sha256"],
        "piece_count": bundle["piece_count"],
        "artifacts": {
            record["leaf"]: {
                "sha256": record["sha256"],
                "bytes": record["bytes"],
            }
            for record in records
        },
    }


def _require_entered_creator_session(machine: str, world: str, session: str) -> None:
    entered = _read_json(_runtime_root() / "status" / "world-entry.json", MAX_STATUS_BYTES)
    if (entered is None
            or entered.get("schema") != "comfy-quest-world-entry-receipt/v1"
            or entered.get("state") != "entered"):
        raise RuntimeError("Quest Lab replay requires a completed Runtime world-entry receipt")
    if str(entered.get("machine", "")).casefold() != machine.casefold():
        raise RuntimeError("Quest Lab replay machine differs from Runtime world entry")
    if entered.get("world_uid") != world:
        raise RuntimeError("Quest Lab replay world differs from Runtime world entry")
    if entered.get("creator_session_id") != session:
        raise RuntimeError("Quest Lab replay Creator Session differs from Runtime world entry")


def _project_lab_receipt(receipt: dict[str, Any]) -> dict[str, Any]:
    return _pick(receipt, (
        "schema", "request_id", "operation", "state", "machine",
        "plugin_version", "release_id", "creator_session_id", "world_uid", "completed_utc",
    )) or {}


def _public_lab_diagnostic(detail: Any, fallback: str) -> str:
    """Expose Lab codes, never free-form diagnostics that can contain private paths."""
    if isinstance(detail, str) and SAFE_DIAGNOSTIC.fullmatch(detail):
        return detail
    return fallback


def _validate_reported_lab_artifacts(
    receipt: dict[str, Any],
    lab: Path,
    bundle: dict[str, Any],
) -> None:
    name = bundle["name"]
    expected = {
        "artifact_path": f"{name}.capture.json",
        "blueprint_path": f"{name}.blueprint",
    }
    target_root = (lab / "blueprints").resolve(strict=True)
    for field, leaf in expected.items():
        raw = receipt.get(field)
        if raw is not None and raw != "":
            if (not isinstance(raw, str) or len(raw) > 1024
                    or any(ord(character) < 32 for character in raw)):
                raise ValueError(f"Quest Lab {field} is not a bounded artifact identity")
            # The game and Isolate can have different host/container path syntax and mount
            # locations. Never dereference the game-reported path. It may identify only the
            # expected leaf; bytes always come from the fixed release-profile Lab mount.
            reported_leaf = raw.replace("\\", "/").rsplit("/", 1)[-1]
            if reported_leaf != leaf:
                raise ValueError(f"Quest Lab {field} reported a different reviewed artifact")
        data = _read_bounded_bytes(
            target_root / leaf, MAX_GODBUILD_ARTIFACT_BYTES, leaf)
        if _sha256(data) != bundle["artifacts"][leaf]["sha256"]:
            raise ValueError(f"Quest Lab {field} differs from the staged reviewed artifact")


def _blueprint_diff_state(detail: Any, name: str, expected_count: int) -> str:
    """Classify only the Lab's canonical mark-scoped diff diagnostic."""
    if not isinstance(detail, str):
        return "invalid"
    match = re.match(
        rf"^capture diff {re.escape(name)}: (MATCH|DIFFERENT) .*?"
        r"expected ([0-9]+), selected ([0-9]+), missing ([0-9]+), extra ([0-9]+)",
        detail,
    )
    if match is None:
        return "invalid"
    verdict = match.group(1)
    expected, selected, missing, extra = (int(match.group(index)) for index in range(2, 6))
    if expected != expected_count:
        return "invalid"
    exact = selected == expected and missing == 0 and extra == 0
    empty = selected == 0 and missing == expected and extra == 0
    if verdict == "MATCH" and exact:
        return "match"
    if verdict == "DIFFERENT" and empty:
        return "empty"
    if verdict == "DIFFERENT" and not exact:
        return "partial"
    return "invalid"


def _blueprint_loaded_count(detail: Any, name: str) -> int | None:
    """Parse only BlueprintBuilder.Count's canonical loaded-ZDO diagnostic."""
    if not isinstance(detail, str):
        return None
    if detail == (
        "no blueprint-built pieces in the loaded area. Only loaded zones are "
        "counted — stand near the build."
    ):
        return 0
    match = re.match(
        rf"^{re.escape(name)}: ([0-9]+) piece\(s\) standing in loaded zones(?:\r?\n|$)",
        detail,
    )
    if match is None:
        return None
    count = int(match.group(1))
    return count if count <= 4096 else None


def _lab_replay_request(
    lab: Path,
    operation: str,
    name: str,
    machine: str,
    world: str,
    session: str,
    build_mode: str,
    radius: str,
    timeout: int,
) -> dict[str, Any]:
    request_id = _request_id("lab", operation.replace("_", "-"))
    created, expires = _window(timeout)
    request: dict[str, Any] = {
        "schema": "comfy-questlab-batch-request/v1",
        "request_id": request_id,
        "operation": operation,
        "created_utc": created,
        "expires_utc": expires,
        "expected_machine": machine,
        "expected_world_uid": world,
        "creator_session_id": session,
        "blueprint_name": name,
    }
    if operation == "blueprint_build":
        request["build_mode"] = build_mode
    if operation == "blueprint_diff":
        request["radius_metres"] = radius
        request["selection"] = "lab"

    mailbox = lab / "requests" / "questlab-batch-request.json"
    _write_exclusive(mailbox, request, authority="Quest Lab")
    receipt = _wait_for_receipt(
        lab / "receipts" / "requests" / f"{request_id}.json",
        request_id,
        operation,
        timeout,
        authority="Quest Lab",
    )
    if receipt.get("schema") != "comfy-questlab-batch-request-receipt/v1":
        raise ValueError("Quest Lab returned an unexpected replay receipt schema")
    state = receipt.get("state")
    detail = receipt.get("detail")
    failed = state in {"failed", "rejected"}
    if state not in {"completed", "failed", "rejected"}:
        raise ValueError("Quest Lab returned an unexpected replay receipt state")
    if (str(receipt.get("machine", "")).casefold() != machine.casefold()
            and not (failed and detail == "creator_machine_mismatch")):
        raise ValueError("Quest Lab returned replay evidence for a different machine")
    if (receipt.get("world_uid") != world
            and not (failed and detail in {
                "creator_world_mismatch", "creator_world_not_loaded", "creator_world_unreadable",
            })):
        raise ValueError("Quest Lab returned replay evidence for a different world")
    if receipt.get("creator_session_id") != session:
        raise ValueError("Quest Lab returned replay evidence for a different Creator Session")
    return receipt


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
            "run_id", "scope_id", "experience_id", "binding_zdo", "binding_instance_id",
            "content_hash", "stage_id", "outcome", "reward_policy",
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


def _execute_lab_replay(
    blueprint_name: str,
    machine: str,
    world: str,
    session: str,
    build_mode: str,
    radius: str,
    timeout: int,
    lab: Path,
    bundle: dict[str, Any],
) -> dict[str, Any]:
    """Stage and replay one prevalidated Godbuild through Quest Lab's fixed mailbox.

    The reviewed source and writable Lab roots are release-profile mounts, never tool
    arguments. It validates and stages the reviewed pair, runs CHECK, then uses loaded mark
    COUNT plus the same mark-scoped DIFF as retry preflight. An existing exact count and MATCH
    returns success; only a proven zero-count/zero-piece state may BUILD before a final DIFF.
    Partial/extra state fails closed. Isolate remains responsible for lifecycle and transport.
    """
    staged = _stage_reviewed_godbuild(bundle, lab)
    receipts: list[dict[str, Any]] = []
    check = _lab_replay_request(
        lab, "blueprint_check", blueprint_name, machine, world, session,
        build_mode, radius, timeout)
    _validate_reported_lab_artifacts(check, lab, bundle)
    receipts.append(_project_lab_receipt(check))
    if check.get("state") != "completed":
        return {
            "schema": "comfy-quest-workbench-lab-replay-result/v1",
            "ok": False,
            "blueprint_name": blueprint_name,
            "failed_step": "blueprint_check",
            "detail": _public_lab_diagnostic(
                check.get("detail"), "blueprint_check_failed"),
            "reviewed": staged,
            "receipts": receipts,
        }

    # Count catches marked pieces that are loaded but outside the player's diff radius. It is
    # not success authority; it prevents an apparently empty local diff from authorizing a
    # duplicate build when the loaded ZDO table already contains this blueprint mark.
    count_receipt = _lab_replay_request(
        lab, "blueprint_count", blueprint_name, machine, world, session,
        build_mode, radius, timeout)
    _validate_reported_lab_artifacts(count_receipt, lab, bundle)
    receipts.append(_project_lab_receipt(count_receipt))
    if count_receipt.get("state") != "completed":
        return {
            "schema": "comfy-quest-workbench-lab-replay-result/v1",
            "ok": False,
            "blueprint_name": blueprint_name,
            "failed_step": "blueprint_count_preflight",
            "detail": _public_lab_diagnostic(
                count_receipt.get("detail"), "blueprint_count_failed"),
            "built": False,
            "reviewed": staged,
            "receipts": receipts,
        }
    loaded_count = _blueprint_loaded_count(count_receipt.get("detail"), blueprint_name)
    if loaded_count is None:
        return {
            "schema": "comfy-quest-workbench-lab-replay-result/v1",
            "ok": False,
            "blueprint_name": blueprint_name,
            "failed_step": "blueprint_count_preflight",
            "detail": "blueprint_count_diagnostic_unrecognized",
            "built": False,
            "reviewed": staged,
            "receipts": receipts,
        }

    # Diff cannot represent the zero-mark case: Lab deliberately reports that as a failed
    # selection. A canonical zero loaded count is therefore the sole build authorization.
    # When marks do exist, the same mark-scoped diff used after build decides whether this is
    # an already-complete retry or partial/extra state that must fail closed.
    if loaded_count != 0:
        before = _lab_replay_request(
            lab, "blueprint_diff", blueprint_name, machine, world, session,
            build_mode, radius, timeout)
        _validate_reported_lab_artifacts(before, lab, bundle)
        receipts.append(_project_lab_receipt(before))
        if before.get("state") != "completed":
            return {
                "schema": "comfy-quest-workbench-lab-replay-result/v1",
                "ok": False,
                "blueprint_name": blueprint_name,
                "failed_step": "blueprint_diff_preflight",
                "detail": _public_lab_diagnostic(
                    before.get("detail"), "blueprint_diff_preflight_failed"),
                "built": False,
                "reviewed": staged,
                "receipts": receipts,
            }
        before_state = _blueprint_diff_state(
            before.get("detail"), blueprint_name, bundle["piece_count"])
        if before_state == "match" and loaded_count == bundle["piece_count"]:
            return {
                "schema": "comfy-quest-workbench-lab-replay-result/v1",
                "ok": True,
                "blueprint_name": blueprint_name,
                "detail": "blueprint_replay_already_match",
                "built": False,
                "reviewed": staged,
                "receipts": receipts,
            }
        return {
            "schema": "comfy-quest-workbench-lab-replay-result/v1",
            "ok": False,
            "blueprint_name": blueprint_name,
            "failed_step": "blueprint_diff_preflight",
            "detail": "blueprint_replay_state_not_empty_or_match",
            "built": False,
            "reviewed": staged,
            "receipts": receipts,
        }

    build = _lab_replay_request(
        lab, "blueprint_build", blueprint_name, machine, world, session,
        build_mode, radius, timeout)
    _validate_reported_lab_artifacts(build, lab, bundle)
    receipts.append(_project_lab_receipt(build))
    if build.get("state") != "completed":
        return {
            "schema": "comfy-quest-workbench-lab-replay-result/v1",
            "ok": False,
            "blueprint_name": blueprint_name,
            "failed_step": "blueprint_build",
            "detail": _public_lab_diagnostic(
                build.get("detail"), "blueprint_build_failed"),
            "built": False,
            "reviewed": staged,
            "receipts": receipts,
        }

    after = _lab_replay_request(
        lab, "blueprint_diff", blueprint_name, machine, world, session,
        build_mode, radius, timeout)
    _validate_reported_lab_artifacts(after, lab, bundle)
    receipts.append(_project_lab_receipt(after))
    if after.get("state") != "completed":
        return {
            "schema": "comfy-quest-workbench-lab-replay-result/v1",
            "ok": False,
            "blueprint_name": blueprint_name,
            "failed_step": "blueprint_diff",
            "detail": _public_lab_diagnostic(
                after.get("detail"), "blueprint_diff_failed"),
            "built": True,
            "reviewed": staged,
            "receipts": receipts,
        }
    if _blueprint_diff_state(
            after.get("detail"), blueprint_name, bundle["piece_count"]) != "match":
        return {
            "schema": "comfy-quest-workbench-lab-replay-result/v1",
            "ok": False,
            "blueprint_name": blueprint_name,
            "failed_step": "blueprint_diff",
            "detail": "blueprint_diff_completed_without_match",
            "built": True,
            "reviewed": staged,
            "receipts": receipts,
        }
    return {
        "schema": "comfy-quest-workbench-lab-replay-result/v1",
        "ok": True,
        "blueprint_name": blueprint_name,
        "detail": "blueprint_replay_match",
        "built": True,
        "reviewed": staged,
        "receipts": receipts,
    }


def _creator_authority_receipt(
    result: Any,
    operation: str,
    machine: str,
    world: str,
    session: str,
) -> dict[str, Any]:
    """Require the identity-bearing Runtime receipt used by the replay transaction."""
    if not isinstance(result, dict) or not isinstance(result.get("receipt"), dict):
        raise ValueError("Runtime creator authority result is missing its receipt")
    receipt = result["receipt"]
    if (receipt.get("schema") != "comfy-quest-runtime-request-receipt/v1"
            or receipt.get("operation") != operation):
        raise ValueError("Runtime creator authority receipt has an unexpected identity")
    if str(receipt.get("machine", "")).casefold() != machine.casefold():
        raise ValueError("Runtime creator authority receipt is for a different machine")
    if receipt.get("world_uid") != world:
        raise ValueError("Runtime creator authority receipt is for a different world")
    if receipt.get("creator_session_id") != session:
        raise ValueError("Runtime creator authority receipt is for a different Creator Session")
    return receipt


def _project_creator_authority(
    receipt: dict[str, Any],
    fallback_detail: str,
) -> dict[str, Any]:
    projected = _pick(receipt, (
        "schema", "request_id", "operation", "state", "machine", "world_uid",
        "creator_session_id", "completed_utc", "creator_build_enabled",
    )) or {}
    projected["detail"] = _public_lab_diagnostic(
        receipt.get("detail"), fallback_detail)
    return projected


def _authority_failure(
    blueprint_name: str,
    failed_step: str,
    detail: str,
    authority: dict[str, Any],
    cleanup: dict[str, Any],
) -> dict[str, Any]:
    return {
        "schema": "comfy-quest-workbench-lab-replay-result/v1",
        "ok": False,
        "blueprint_name": blueprint_name,
        "failed_step": failed_step,
        "detail": detail,
        "built": False,
        "authority": authority,
        "cleanup": cleanup,
    }


def _disable_creator_build(
    machine: str,
    world: str,
    session: str,
    timeout: int,
    cleanup: dict[str, Any],
) -> bool:
    """Attempt and prove the compensating Runtime build-off operation."""
    cleanup["attempted"] = True
    try:
        result = quest_runtime_creator_request(
            "build_off", machine, world, session, timeout)
        receipt = _creator_authority_receipt(
            result, "build_off", machine, world, session)
        projection = _project_creator_authority(
            receipt, "runtime_build_off_failed")
        cleanup["receipt"] = projection
        verified = (
            receipt.get("state") == "completed"
            and receipt.get("creator_build_enabled") is False
        )
        cleanup["verified_disabled"] = verified
        cleanup["detail"] = (
            "creator_build_disabled" if verified else projection["detail"]
        )
        return verified
    except Exception:
        cleanup["verified_disabled"] = False
        cleanup["detail"] = "runtime_build_off_request_failed"
        return False


def _quest_lab_replay(
    blueprint_name: str,
    expected_machine: str,
    expected_world_uid: str,
    creator_session_id: str,
    build_mode: str = "ground",
    radius_metres: float = 20,
    timeout_seconds: int = 60,
) -> dict[str, Any]:
    """Authorize, execute, and compensate one reviewed replay transaction."""
    if not isinstance(blueprint_name, str) or not SAFE_BLUEPRINT.fullmatch(blueprint_name):
        raise ValueError(
            "blueprint_name must use 1-64 lowercase letters, digits, '-' or '_'"
        )
    machine = _token(expected_machine, "expected_machine", 80)
    world = _world_uid(expected_world_uid)
    session = _token(creator_session_id, "creator_session_id", 80)
    if build_mode != "ground":
        raise ValueError("build_mode must be ground for zero-diff reviewed replay")
    if (isinstance(radius_metres, bool) or not isinstance(radius_metres, (int, float))
            or not math.isfinite(float(radius_metres))
            or not 1 <= float(radius_metres) <= 40):
        raise ValueError("radius_metres must be a finite number from 1 through 40")
    radius = f"{float(radius_metres):.3f}".rstrip("0").rstrip(".")
    timeout = _timeout(timeout_seconds)

    # Read-only gates precede live authority so an invalid artifact never toggles build mode.
    _require_entered_creator_session(machine, world, session)
    lab = _lab_root()
    bundle = _reviewed_godbuild(blueprint_name)

    authority: dict[str, Any] = {}
    cleanup: dict[str, Any] = {
        "required": False,
        "attempted": False,
        "preserved_preexisting_enabled": False,
        "verified_disabled": False,
    }

    status_result = quest_runtime_creator_request(
        "status", machine, world, session, timeout)
    status = _creator_authority_receipt(
        status_result, "status", machine, world, session)
    authority["status"] = _project_creator_authority(
        status, "runtime_creator_status_failed")
    if status.get("state") != "completed":
        return _authority_failure(
            blueprint_name,
            "runtime_creator_status",
            authority["status"]["detail"],
            authority,
            cleanup,
        )
    preexisting_enabled = status.get("creator_build_enabled")
    if not isinstance(preexisting_enabled, bool):
        return _authority_failure(
            blueprint_name,
            "runtime_creator_status",
            "runtime_creator_build_state_unavailable",
            authority,
            cleanup,
        )
    cleanup["preserved_preexisting_enabled"] = preexisting_enabled
    cleanup["verified_disabled"] = not preexisting_enabled
    authority["preexisting_build_enabled"] = preexisting_enabled

    # Always reassert build_on. Runtime checks the live session, world, machine, and private-world
    # confirmation here; a persisted world-entry receipt alone is never mutation authority.
    try:
        build_on_result = quest_runtime_creator_request(
            "build_on", machine, world, session, timeout)
    except BaseException as error:
        if not preexisting_enabled:
            cleanup["required"] = True
            cleanup_ok = _disable_creator_build(
                machine, world, session, timeout, cleanup)
            if not cleanup_ok:
                raise RuntimeError(
                    "Runtime build-on failed and creator build cleanup was not verified"
                ) from error
        raise
    build_on = _creator_authority_receipt(
        build_on_result, "build_on", machine, world, session)
    authority["build_on"] = _project_creator_authority(
        build_on, "runtime_build_on_failed")
    if (build_on.get("state") != "completed"
            or build_on.get("creator_build_enabled") is not True):
        if not preexisting_enabled and build_on.get("creator_build_enabled") is True:
            cleanup["required"] = True
            _disable_creator_build(machine, world, session, timeout, cleanup)
        elif not preexisting_enabled and build_on.get("creator_build_enabled") is False:
            cleanup["verified_disabled"] = True
        return _authority_failure(
            blueprint_name,
            "runtime_creator_build_on",
            authority["build_on"]["detail"],
            authority,
            cleanup,
        )

    cleanup["required"] = not preexisting_enabled
    cleanup["verified_disabled"] = False
    lab_result: dict[str, Any] | None = None
    lab_error: BaseException | None = None
    cleanup_ok = True
    try:
        try:
            lab_result = _execute_lab_replay(
                blueprint_name,
                machine,
                world,
                session,
                build_mode,
                radius,
                timeout,
                lab,
                bundle,
            )
        except BaseException as error:
            lab_error = error
    finally:
        if cleanup["required"]:
            cleanup_ok = _disable_creator_build(
                machine, world, session, timeout, cleanup)
        else:
            cleanup["detail"] = "preexisting_creator_build_preserved"

    if lab_error is not None:
        if not cleanup_ok:
            raise RuntimeError(
                "Quest Lab replay failed and creator build cleanup was not verified"
            ) from lab_error
        raise lab_error
    if lab_result is None:
        raise RuntimeError("Quest Lab replay did not produce a result")

    lab_result["authority"] = authority
    lab_result["cleanup"] = cleanup
    if not cleanup_ok:
        lab_result["lab_outcome"] = _pick(lab_result, (
            "ok", "failed_step", "detail", "built",
        ))
        lab_result["ok"] = False
        lab_result["failed_step"] = "runtime_creator_build_off"
        lab_result["detail"] = "creator_build_cleanup_not_verified"
    return lab_result


def quest_lab_replay(
    blueprint_name: str,
    expected_machine: str,
    expected_world_uid: str,
    creator_session_id: str,
    build_mode: str = "ground",
    radius_metres: float = 20,
    timeout_seconds: int = 60,
) -> dict[str, Any]:
    """Run one serialized reviewed Godbuild replay with Lab-state retry detection.

    Roots come only from the attested release profile. The target-local lock prevents two
    calls in this provider process from both observing an empty mark before either builds;
    the fixed Lab mailbox remains the cross-operation mutation authority.
    """
    if not _LAB_REPLAY_LOCK.acquire(blocking=False):
        raise RuntimeError("another Quest Lab replay is already active")
    try:
        return _quest_lab_replay(
            blueprint_name,
            expected_machine,
            expected_world_uid,
            creator_session_id,
            build_mode,
            radius_metres,
            timeout_seconds,
        )
    finally:
        _LAB_REPLAY_LOCK.release()

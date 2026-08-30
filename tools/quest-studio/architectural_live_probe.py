#!/usr/bin/env python3
"""Target-local AM4 harness for hash-pinned architectural live work.

The surface is intentionally narrower than a general Valheim driver. Acceptance mode can
prepare, apply, diff, clear, and restore one exact Lab build. Warm mode keeps that installation,
client, and proven marked build alive between R&D laps, reusing it until an identity drifts.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import tempfile
import time
from typing import Any


LAB_REQUEST_SCHEMA = "comfy-questlab-batch-request/v1"
LAB_RECEIPT_SCHEMA = "comfy-questlab-batch-request-receipt/v1"
RUNTIME_REQUEST_SCHEMA = "comfy-quest-runtime-request/v1"
RUNTIME_RECEIPT_SCHEMA = "comfy-quest-runtime-request-receipt/v1"
WORLD_ENTRY_REQUEST_SCHEMA = "comfy-quest-world-entry-request/v1"
WORLD_ENTRY_RECEIPT_SCHEMA = "comfy-quest-world-entry-receipt/v1"
PREPARE_SCHEMA = "comfy-quest-architectural-live-prepare/v1"
LIVE_SCHEMA = "comfy-quest-architectural-live-replay/v1"
WARM_SCHEMA = "comfy-quest-architectural-warm-lap/v1"
RESTORE_SCHEMA = "comfy-quest-architectural-live-restoration/v1"
SAFE_TOKEN = re.compile(r"^[a-zA-Z0-9._-]{1,80}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")


def utc_now() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def iso(value: dt.datetime) -> str:
    return value.isoformat().replace("+00:00", "Z")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def pin(path: Path) -> dict[str, Any]:
    return {"bytes": path.stat().st_size, "sha256": sha256(path)}


def require_sha(value: str, label: str) -> str:
    if not SHA256.fullmatch(value or ""):
        raise RuntimeError(f"{label}_sha256_invalid")
    return value


def require_safe_token(value: str, label: str, maximum: int = 80) -> str:
    if not SAFE_TOKEN.fullmatch(value or "") or len(value) > maximum:
        raise RuntimeError(f"{label}_invalid")
    return value


def require_finite(value: float, label: str) -> float:
    value = float(value)
    if not math.isfinite(value) or abs(value) > 10500:
        raise RuntimeError(f"{label}_invalid")
    return value


def atomic_json(path: Path, value: Any, *, create: bool = False) -> None:
    payload = (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False)
               + "\n").encode("utf-8")
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(
        prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        if create:
            os.link(temporary, path)
            os.unlink(temporary)
        else:
            os.replace(temporary, path)
    finally:
        try:
            os.unlink(temporary)
        except FileNotFoundError:
            pass


def atomic_copy(source: Path, target: Path) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(
        prefix=target.name + ".", suffix=".tmp", dir=target.parent)
    try:
        with source.open("rb") as incoming, os.fdopen(descriptor, "wb") as outgoing:
            shutil.copyfileobj(incoming, outgoing)
            outgoing.flush()
            os.fsync(outgoing.fileno())
        os.replace(temporary, target)
    finally:
        try:
            os.unlink(temporary)
        except FileNotFoundError:
            pass


def read_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise RuntimeError(f"json_object_required:{path}")
    return value


def inventory(root: Path) -> dict[str, dict[str, Any]]:
    if not root.exists():
        return {}
    return {
        str(path.relative_to(root)): pin(path)
        for path in sorted(root.rglob("*")) if path.is_file()
    }


def matching_save_files(unity: Path, world: str, character: str) -> list[Path]:
    roots = ((unity / "worlds_local", world),
             (unity / "characters_local", character))
    result: list[Path] = []
    for root, prefix in roots:
        if root.is_dir():
            result.extend(path for path in sorted(root.glob(prefix + "*")) if path.is_file())
    return result


def process_snapshot() -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    proc = Path("/proc")
    if not proc.is_dir():
        return result
    for entry in proc.iterdir():
        if not entry.name.isdigit():
            continue
        try:
            command = (entry / "comm").read_text(errors="replace").strip()
            arguments = (entry / "cmdline").read_bytes().replace(b"\0", b" ").decode(
                "utf-8", errors="replace").strip()
        except OSError:
            continue
        if command.lower().startswith("valheim"):
            result.append({"pid": int(entry.name), "command": command,
                           "arguments": arguments[:1024]})
    return sorted(result, key=lambda item: item["pid"])


def steam_running() -> bool:
    try:
        return subprocess.run(["pgrep", "-x", "steam"], stdout=subprocess.DEVNULL,
                              stderr=subprocess.DEVNULL, check=False).returncode == 0
    except FileNotFoundError:
        return False


def owned_paths(valheim: Path) -> dict[str, Path]:
    config = valheim / "BepInEx" / "config"
    return {
        "runtime": config / "comfy-quest-runtime",
        "lab": config / "comfy-quest-lab",
        "lab_plugin": valheim / "BepInEx" / "plugins" / "ComfyQuestLab.dll",
    }


def snapshot(valheim: Path, unity: Path, world: str, character: str,
             blueprint: str) -> dict[str, Any]:
    paths = owned_paths(valheim)
    save_files = matching_save_files(unity, world, character)
    blueprints = paths["lab"] / "blueprints"
    pair = [blueprints / f"{blueprint}.capture.json",
            blueprints / f"{blueprint}.blueprint"]
    return {
        "schema": "comfy-quest-architectural-live-snapshot/v1",
        "machine": socket.gethostname(),
        "valheim_processes": process_snapshot(),
        "steam_running": steam_running(),
        "lab_plugin": pin(paths["lab_plugin"]) if paths["lab_plugin"].is_file() else None,
        "runtime": inventory(paths["runtime"]),
        "lab": inventory(paths["lab"]),
        "saves": {
            str(path.relative_to(unity)): pin(path) for path in save_files
        },
        "canonical_pair": {
            path.name: pin(path) for path in pair if path.is_file()
        },
    }


def validate_roots(valheim: Path, run_root: Path) -> tuple[Path, Path]:
    valheim = valheim.resolve()
    run_root = run_root.resolve()
    if valheim == Path("/") or not (valheim / "valheim.x86_64").is_file():
        raise RuntimeError("valheim_root_invalid")
    if run_root == Path("/") or run_root == valheim or valheim in run_root.parents:
        raise RuntimeError("run_root_invalid")
    return valheim, run_root


def prepare(args: argparse.Namespace) -> dict[str, Any]:
    valheim, run_root = validate_roots(args.valheim_root, args.run_root)
    unity = args.unity_root.resolve()
    if socket.gethostname() != args.machine:
        raise RuntimeError("machine_identity_mismatch")
    if process_snapshot():
        raise RuntimeError("valheim_must_be_stopped")
    require_safe_token(args.session, "creator_session")
    require_safe_token(args.blueprint, "blueprint", maximum=64)
    require_sha(args.capture_sha256, "capture")
    require_sha(args.blueprint_sha256, "blueprint")
    require_sha(args.canonical_pieces_sha256, "canonical_pieces")
    require_sha(args.candidate_lab_sha256, "candidate_lab")
    paths = owned_paths(valheim)
    pair_root = paths["lab"] / "blueprints"
    capture = pair_root / f"{args.blueprint}.capture.json"
    blueprint_path = pair_root / f"{args.blueprint}.blueprint"
    if sha256(capture) != args.capture_sha256 or sha256(blueprint_path) != args.blueprint_sha256:
        raise RuntimeError("canonical_pair_hash_mismatch")
    capture_value = read_json(capture)
    if (capture_value.get("Schema") != "comfy-questlab-capture/v1"
            or capture_value.get("Name") != args.blueprint
            or capture_value.get("Selection") != "lab"
            or capture_value.get("PieceCount") != args.piece_count
            or capture_value.get("PiecesSha256") != args.canonical_pieces_sha256):
        raise RuntimeError("canonical_capture_identity_mismatch")
    candidate = args.candidate_lab.resolve()
    if not candidate.is_file() or sha256(candidate) != args.candidate_lab_sha256:
        raise RuntimeError("candidate_lab_hash_mismatch")
    pending = [paths["runtime"] / "requests" / "world-entry.json",
               paths["runtime"] / "requests" / "creator-request.json",
               paths["lab"] / "requests" / "questlab-batch-request.json"]
    if any(path.exists() for path in pending):
        raise RuntimeError("preexisting_mailbox_request")
    if any(run_root.iterdir()):
        raise RuntimeError("run_root_not_empty")

    before = snapshot(valheim, unity, args.world, args.character, args.blueprint)
    backup = run_root / "backup"
    backup.mkdir()
    for name in ("runtime", "lab"):
        if paths[name].is_dir():
            shutil.copytree(paths[name], backup / "config" / paths[name].name)
    (backup / "plugins").mkdir(parents=True)
    shutil.copy2(paths["lab_plugin"], backup / "plugins" / "ComfyQuestLab.dll")
    for source in matching_save_files(unity, args.world, args.character):
        target = backup / "saves" / source.relative_to(unity)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
    atomic_json(run_root / "before.json", before, create=True)

    atomic_copy(candidate, paths["lab_plugin"])
    if sha256(paths["lab_plugin"]) != args.candidate_lab_sha256:
        raise RuntimeError("candidate_lab_deploy_mismatch")

    now = utc_now()
    request_id = "world-entry-architectural-" + now.strftime("%Y%m%dT%H%M%SZ")
    request = {
        "schema": WORLD_ENTRY_REQUEST_SCHEMA,
        "request_id": request_id,
        "created_utc": iso(now),
        "expires_utc": iso(now + dt.timedelta(minutes=15)),
        "expected_machine": args.machine,
        "expected_world_uid": args.world_uid,
        "world_name": args.world,
        "world_display_name": args.world_display_name,
        "character_profile": args.character,
        "creator_session_id": args.session,
    }
    atomic_json(paths["runtime"] / "requests" / "world-entry.json", request, create=True)
    result = {
        "schema": PREPARE_SCHEMA,
        "status": "READY",
        "prepared_utc": iso(utc_now()),
        "machine": args.machine,
        "world": args.world,
        "world_uid": args.world_uid,
        "character": args.character,
        "creator_session_id": args.session,
        "blueprint_name": args.blueprint,
        "piece_count": args.piece_count,
        "canonical_pieces_sha256": args.canonical_pieces_sha256,
        "world_entry_request": request,
        "candidate_lab": pin(paths["lab_plugin"]),
        "prior_lab": before["lab_plugin"],
        "steam_was_running": before["steam_running"],
        "canonical_pair": before["canonical_pair"],
    }
    atomic_json(run_root / "prepare.json", result, create=True)
    return result


def reenter(args: argparse.Namespace) -> dict[str, Any]:
    """Refresh only the consumed world-entry request for an existing warm installation."""
    valheim, run_root = validate_roots(args.valheim_root, args.run_root)
    if socket.gethostname() != args.machine:
        raise RuntimeError("machine_identity_mismatch")
    if process_snapshot():
        raise RuntimeError("reenter_requires_valheim_stopped")
    prepare_value = read_json(run_root / "prepare.json")
    expected = {
        "schema": PREPARE_SCHEMA,
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
    if any(prepare_value.get(key) != value for key, value in expected.items()):
        raise RuntimeError("warm_prepare_identity_mismatch")
    paths = owned_paths(valheim)
    if (not paths["lab_plugin"].is_file()
            or sha256(paths["lab_plugin"]) != args.candidate_lab_sha256):
        raise RuntimeError("warm_candidate_drift_requires_close")
    pair_root = paths["lab"] / "blueprints"
    capture = pair_root / f"{args.blueprint}.capture.json"
    blueprint = pair_root / f"{args.blueprint}.blueprint"
    if (not capture.is_file() or sha256(capture) != args.capture_sha256
            or not blueprint.is_file() or sha256(blueprint) != args.blueprint_sha256):
        raise RuntimeError("warm_canonical_pair_drift_requires_close")
    pending = [paths["runtime"] / "requests" / "world-entry.json",
               paths["runtime"] / "requests" / "creator-request.json",
               paths["lab"] / "requests" / "questlab-batch-request.json"]
    if any(path.exists() for path in pending):
        raise RuntimeError("preexisting_mailbox_request")

    now = utc_now()
    request_id = ("world-entry-architectural-warm-"
                  + now.strftime("%Y%m%dT%H%M%SZ"))
    request = {
        "schema": WORLD_ENTRY_REQUEST_SCHEMA,
        "request_id": request_id,
        "created_utc": iso(now),
        "expires_utc": iso(now + dt.timedelta(minutes=15)),
        "expected_machine": args.machine,
        "expected_world_uid": args.world_uid,
        "world_name": args.world,
        "world_display_name": args.world_display_name,
        "character_profile": args.character,
        "creator_session_id": args.session,
    }
    atomic_json(paths["runtime"] / "requests" / "world-entry.json", request, create=True)
    prepare_value["world_entry_request"] = request
    prepare_value["reentered_utc"] = iso(utc_now())
    atomic_json(run_root / "prepare.json", prepare_value)
    return {
        "schema": "comfy-quest-architectural-warm-reentry/v1",
        "status": "READY",
        "creator_session_id": args.session,
        "blueprint_name": args.blueprint,
        "candidate_lab": pin(paths["lab_plugin"]),
        "canonical_pair": {
            capture.name: pin(capture),
            blueprint.name: pin(blueprint),
        },
        "world_entry_request": request,
    }


def wait_json(path: Path, timeout: float) -> dict[str, Any]:
    deadline = time.monotonic() + timeout
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        if path.is_file():
            try:
                return read_json(path)
            except (OSError, json.JSONDecodeError) as error:
                last_error = error
        time.sleep(.25)
    raise RuntimeError(f"receipt_timeout:{path.name}:{type(last_error).__name__ if last_error else 'missing'}")


def wait_world_entry(path: Path, request_id: str, timeout: float) -> dict[str, Any]:
    deadline = time.monotonic() + timeout
    last_state = "missing"
    while time.monotonic() < deadline:
        if path.is_file():
            try:
                candidate = read_json(path)
                if (candidate.get("schema") == WORLD_ENTRY_RECEIPT_SCHEMA
                        and candidate.get("request_id") == request_id):
                    last_state = str(candidate.get("state") or "missing")
                    if last_state == "entered":
                        return candidate
                    if last_state == "rejected":
                        raise RuntimeError("world_entry_rejected:" +
                                           str(candidate.get("detail")))
            except (OSError, json.JSONDecodeError):
                pass
        time.sleep(.25)
    raise RuntimeError(f"world_entry_timeout:{last_state}")


class LiveDriver:
    def __init__(self, args: argparse.Namespace, prepare_value: dict[str, Any]):
        self.args = args
        self.prepare = prepare_value
        self.valheim = args.valheim_root.resolve()
        self.run_root = args.run_root.resolve()
        self.paths = owned_paths(self.valheim)
        self.receipt_root = self.run_root / "receipts"
        self.sequence: list[dict[str, Any]] = []
        self.build_attempted = False
        self.cleared = False
        self.authority_enabled = False

    def request(self, surface: str, operation: str,
                fields: dict[str, Any] | None = None,
                *, allow_failure: bool = False) -> dict[str, Any]:
        now = utc_now()
        nonce = hashlib.sha256(os.urandom(32)).hexdigest()[:10]
        request_id = f"architectural-{operation.replace('_', '-')}-{now.strftime('%H%M%S')}-{nonce}"
        common = {
            "request_id": request_id,
            "operation": operation,
            "created_utc": iso(now),
            "expires_utc": iso(now + dt.timedelta(minutes=5)),
            "expected_machine": self.args.machine,
            "expected_world_uid": self.args.world_uid,
            "creator_session_id": self.args.session,
        }
        if surface == "runtime":
            request = {"schema": RUNTIME_REQUEST_SCHEMA, **common}
            mailbox = self.paths["runtime"] / "requests" / "creator-request.json"
            receipt_path = (self.paths["runtime"] / "receipts" /
                            "creator-requests" / f"{request_id}.json")
            schema = RUNTIME_RECEIPT_SCHEMA
        elif surface == "lab":
            request = {"schema": LAB_REQUEST_SCHEMA, **common, **(fields or {})}
            mailbox = self.paths["lab"] / "requests" / "questlab-batch-request.json"
            receipt_path = self.paths["lab"] / "receipts" / "requests" / f"{request_id}.json"
            schema = LAB_RECEIPT_SCHEMA
        else:
            raise RuntimeError("request_surface_invalid")
        if fields and surface == "runtime":
            request.update(fields)
        if mailbox.exists() or receipt_path.exists():
            raise RuntimeError(f"mailbox_collision:{surface}:{operation}")
        atomic_json(mailbox, request, create=True)
        receipt = wait_json(receipt_path, self.args.request_timeout)
        expected = {
            "schema": schema,
            "request_id": request_id,
            "operation": operation,
            "machine": self.args.machine,
            "world_uid": self.args.world_uid,
            "creator_session_id": self.args.session,
        }
        if any(receipt.get(key) != value for key, value in expected.items()):
            raise RuntimeError(f"receipt_identity_mismatch:{surface}:{operation}")
        target = self.receipt_root / f"{len(self.sequence) + 1:02d}-{operation}.json"
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(receipt_path, target)
        record = {
            "surface": surface,
            "operation": operation,
            "request_id": request_id,
            "state": receipt.get("state"),
            "detail": receipt.get("detail"),
            "receipt": target.name,
            "receipt_sha256": sha256(target),
        }
        if "placement" in receipt:
            record["placement"] = receipt["placement"]
        self.sequence.append(record)
        if not allow_failure and receipt.get("state") != "completed":
            raise RuntimeError(f"{surface}_{operation}_{receipt.get('state')}:{receipt.get('detail')}")
        return receipt

    def lab_fields(self, operation: str, placed: bool = False) -> dict[str, Any]:
        fields: dict[str, Any] = {"blueprint_name": self.args.blueprint}
        if operation == "blueprint_diff":
            fields["selection"] = "lab"
        if placed:
            mode = getattr(self.args, "placement_mode", "at")
            if mode == "ground":
                # Normal diff infers an origin from the marked selection. Ground is supplied
                # only to BUILD; explicit coordinates remain exclusive to the reviewed at-mode.
                if operation == "blueprint_build":
                    fields["build_mode"] = "ground"
            else:
                fields.update({
                    "build_mode": "at",
                    "world_x": number(self.args.x),
                    "world_y": number(self.args.y),
                    "world_z": number(self.args.z),
                    "yaw_degrees": number(self.args.yaw),
                })
        return fields

    def count(self, receipt: dict[str, Any]) -> int:
        detail = str(receipt.get("detail") or "")
        match = re.search(rf"(?m)^{re.escape(self.args.blueprint)}: (\d+) piece\(s\)", detail)
        if match:
            return int(match.group(1))
        if detail.startswith("no blueprint-built pieces in the loaded area"):
            return 0
        raise RuntimeError("blueprint_count_diagnostic_unrecognized")

    def placement_matches(self, receipt: dict[str, Any]) -> bool:
        if getattr(self.args, "placement_mode", "at") == "ground":
            return "placement" not in receipt
        placement = receipt.get("placement")
        return isinstance(placement, dict) and all(
            abs(float(placement.get(key, math.nan)) - expected) < 0.000001
            for key, expected in (("x", self.args.x), ("y", self.args.y),
                                  ("z", self.args.z),
                                  ("yaw_degrees", self.args.yaw)))

    def clear_if_needed(self) -> None:
        if not self.build_attempted or self.cleared:
            return
        clear = self.request("lab", "blueprint_clear",
                             self.lab_fields("blueprint_clear"), allow_failure=True)
        self.cleared = clear.get("state") == "completed"

    def disable_authority(self) -> None:
        if not self.authority_enabled:
            return
        off = self.request("runtime", "build_off", allow_failure=True)
        self.authority_enabled = not (
            off.get("state") == "completed" and off.get("creator_build_enabled") is False)


def number(value: float) -> str:
    text = f"{float(value):.6f}".rstrip("0").rstrip(".")
    return "0" if text in {"", "-0"} else text


def parse_valheim_window(tree: str) -> dict[str, Any]:
    pattern = re.compile(
        r'^\s*(0x[0-9a-fA-F]+)\s+"([^"]*)":\s+'
        r'\("([^"]+)"\s+"([^"]+)"\)\s+'
        r'(\d+)x(\d+)\+(-?\d+)\+(-?\d+)', re.MULTILINE)
    matches = []
    for match in pattern.finditer(tree):
        instance, window_class = match.group(3), match.group(4)
        if instance.lower() != "valheim.x86_64" or window_class.lower() != "valheim.x86_64":
            continue
        matches.append({
            "id": int(match.group(1), 16),
            "id_hex": match.group(1).lower(),
            "title": match.group(2),
            "instance": instance,
            "class": window_class,
            "width": int(match.group(5)),
            "height": int(match.group(6)),
            "x": int(match.group(7)),
            "y": int(match.group(8)),
        })
    if len(matches) != 1:
        raise RuntimeError("valheim_window_missing_or_ambiguous")
    window = matches[0]
    if window["title"] != "Valheim" or window["width"] < 640 or window["height"] < 480:
        raise RuntimeError("valheim_window_identity_invalid")
    return window


def discover_valheim_window(display: str) -> dict[str, Any]:
    tree = subprocess.check_output(
        ["xwininfo", "-display", display, "-root", "-tree"],
        text=True, timeout=10)
    return parse_valheim_window(tree)


def capture_screenshot(run_root: Path) -> dict[str, Any]:
    screenshot = run_root / "architectural-live-applied.png"
    try:
        display = os.environ.get("DISPLAY", ":0")
        window = discover_valheim_window(display)
        result = subprocess.run([
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "x11grab", "-window_id", str(window["id"]),
            "-video_size", f'{window["width"]}x{window["height"]}', "-i", display,
            "-frames:v", "1", str(screenshot),
        ], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=30,
            check=False)
        if result.returncode != 0 or not screenshot.is_file():
            raise RuntimeError("ffmpeg_capture_failed:" + result.stderr[-500:])
        return {"status": "captured", "name": screenshot.name,
                "capture_kind": "x11-window", "window": window, **pin(screenshot)}
    except Exception as error:  # Screenshot is secondary to mailbox/world receipts.
        return {"status": "unavailable", "detail": type(error).__name__}


def run_live(args: argparse.Namespace) -> dict[str, Any]:
    valheim, run_root = validate_roots(args.valheim_root, args.run_root)
    prepare_value = read_json(run_root / "prepare.json")
    if (prepare_value.get("schema") != PREPARE_SCHEMA
            or prepare_value.get("status") != "READY"
            or prepare_value.get("creator_session_id") != args.session
            or prepare_value.get("blueprint_name") != args.blueprint):
        raise RuntimeError("prepare_identity_mismatch")
    driver = LiveDriver(args, prepare_value)
    started = utc_now()
    failure: str | None = None
    screenshot: dict[str, Any] | None = None
    entered = False
    try:
        entry_id = prepare_value["world_entry_request"]["request_id"]
        entry_path = (owned_paths(valheim)["runtime"] / "receipts" /
                      "world-entry" / f"{entry_id}.json")
        entry = wait_world_entry(entry_path, entry_id, args.world_timeout)
        expected_entry = {
            "schema": WORLD_ENTRY_RECEIPT_SCHEMA,
            "request_id": entry_id,
            "creator_session_id": args.session,
            "state": "entered",
            "machine": args.machine,
            "world_uid": args.world_uid,
            "world_name": args.world,
            "character_profile": args.character,
        }
        if any(entry.get(key) != value for key, value in expected_entry.items()):
            raise RuntimeError("world_entry_identity_mismatch:" + str(entry.get("detail")))
        entered = True
        shutil.copy2(entry_path, run_root / "world-entry-receipt.json")

        status = driver.request("runtime", "status")
        if status.get("creator_build_enabled") is not False:
            raise RuntimeError("creator_authority_preexisting")

        check = driver.request("lab", "blueprint_check",
                               driver.lab_fields("blueprint_check"))
        detail = str(check.get("detail") or "")
        if (f"{args.piece_count} buildable piece(s)" not in detail
                or args.canonical_pieces_sha256 not in detail
                or f"Ready. questlab_blueprint build {args.blueprint}" not in detail):
            raise RuntimeError("blueprint_check_contract_mismatch")
        for field, expected_hash in (("artifact_path", args.capture_sha256),
                                     ("blueprint_path", args.blueprint_sha256)):
            artifact = Path(str(check.get(field) or ""))
            if not artifact.is_file() or sha256(artifact) != expected_hash:
                raise RuntimeError(f"blueprint_check_{field}_hash_mismatch")

        before_count = driver.count(driver.request(
            "lab", "blueprint_count", driver.lab_fields("blueprint_count")))
        if before_count != 0:
            raise RuntimeError(f"blueprint_preexisting_mark_count:{before_count}")

        authority = driver.request("runtime", "build_on")
        driver.authority_enabled = authority.get("creator_build_enabled") is True
        if not driver.authority_enabled:
            raise RuntimeError("creator_authority_not_enabled")

        driver.build_attempted = True
        built = driver.request("lab", "blueprint_build",
                               driver.lab_fields("blueprint_build", placed=True))
        if getattr(args, "placement_mode", "at") == "ground":
            if driver.count(built) != args.piece_count:
                raise RuntimeError("blueprint_build_ground_count_mismatch")
        elif (not driver.placement_matches(built)
              or f"placed={args.piece_count}" not in str(built.get("detail"))
              or "failed=0" not in str(built.get("detail"))):
            raise RuntimeError("blueprint_build_placement_mismatch")
        after_count = driver.count(driver.request(
            "lab", "blueprint_count", driver.lab_fields("blueprint_count")))
        if after_count != args.piece_count:
            raise RuntimeError(f"blueprint_applied_count:{after_count}")

        diff = driver.request("lab", "blueprint_diff",
                              driver.lab_fields("blueprint_diff", placed=True))
        diff_detail = str(diff.get("detail") or "")
        if (not driver.placement_matches(diff)
                or f"capture diff {args.blueprint}: MATCH" not in diff_detail
                or f"expected {args.piece_count}, selected {args.piece_count}, missing 0, extra 0"
                    not in diff_detail):
            raise RuntimeError("blueprint_diff_placement_mismatch")
        screenshot = capture_screenshot(run_root)

        cleared = driver.request("lab", "blueprint_clear",
                                 driver.lab_fields("blueprint_clear"))
        driver.cleared = cleared.get("state") == "completed"
        if not driver.cleared:
            raise RuntimeError("blueprint_clear_not_completed")
        cleared_count = driver.count(driver.request(
            "lab", "blueprint_count", driver.lab_fields("blueprint_count")))
        if cleared_count != 0:
            raise RuntimeError(f"blueprint_clear_count:{cleared_count}")
    except Exception as error:
        failure = f"{type(error).__name__}:{error}"
    finally:
        try:
            driver.clear_if_needed()
        except Exception as error:
            failure = failure or f"cleanup_clear:{type(error).__name__}:{error}"
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

    pair_root = owned_paths(valheim)["lab"] / "blueprints"
    pair = {
        f"{args.blueprint}.capture.json": pin(
            pair_root / f"{args.blueprint}.capture.json"),
        f"{args.blueprint}.blueprint": pin(
            pair_root / f"{args.blueprint}.blueprint"),
    }
    if (pair[f"{args.blueprint}.capture.json"]["sha256"] != args.capture_sha256
            or pair[f"{args.blueprint}.blueprint"]["sha256"] != args.blueprint_sha256):
        failure = failure or "canonical_artifact_drift"
    result = {
        "schema": LIVE_SCHEMA,
        "status": "PASS" if failure is None else "FAIL",
        "failure": failure,
        "started_utc": iso(started),
        "completed_utc": iso(utc_now()),
        "identity": {
            "machine": args.machine,
            "world": args.world,
            "world_uid": args.world_uid,
            "character": args.character,
            "creator_session_id": args.session,
            "blueprint_name": args.blueprint,
        },
        "placement": ({"mode": "ground"}
                      if getattr(args, "placement_mode", "at") == "ground"
                      else {"mode": "at", "x": args.x, "y": args.y, "z": args.z,
                            "yaw": args.yaw}),
        "piece_count": args.piece_count,
        "canonical_pieces_sha256": args.canonical_pieces_sha256,
        "canonical_pair": pair,
        "sequence": driver.sequence,
        "screenshot": screenshot,
        "assertions": {
            "check_completed": any(item["operation"] == "blueprint_check"
                                   and item["state"] == "completed"
                                   for item in driver.sequence),
            "placement_applied": driver.build_attempted,
            "placed_diff_match": any(item["operation"] == "blueprint_diff"
                                     and item["state"] == "completed"
                                     and "MATCH" in str(item["detail"])
                                     for item in driver.sequence),
            "marked_pieces_cleared": driver.cleared,
            "creator_build_disabled": not driver.authority_enabled,
            "canonical_artifacts_unchanged": failure != "canonical_artifact_drift",
        },
    }
    atomic_json(run_root / "live-replay.json", result)
    return result


def run_warm(args: argparse.Namespace) -> dict[str, Any]:
    """Prove or create the exact marked build and retain the live state for the next lap."""
    valheim, run_root = validate_roots(args.valheim_root, args.run_root)
    session_root = args.session_root.resolve()
    if (session_root == Path("/") or session_root == valheim
            or valheim in session_root.parents):
        raise RuntimeError("session_root_invalid")
    prepare_value = read_json(session_root / "prepare.json")
    expected_prepare = {
        "schema": PREPARE_SCHEMA,
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
    if any(prepare_value.get(key) != value for key, value in expected_prepare.items()):
        raise RuntimeError("warm_prepare_identity_mismatch")
    if not process_snapshot():
        raise RuntimeError("warm_lap_requires_running_valheim")
    paths = owned_paths(valheim)
    if (not paths["lab_plugin"].is_file()
            or sha256(paths["lab_plugin"]) != args.candidate_lab_sha256):
        raise RuntimeError("warm_candidate_drift_requires_close")

    driver = LiveDriver(args, prepare_value)
    started = utc_now()
    failure: str | None = None
    screenshot: dict[str, Any] | None = None
    entered = False
    build_action = "unresolved"
    standing_count: int | None = None
    try:
        entry_id = prepare_value["world_entry_request"]["request_id"]
        entry_path = (paths["runtime"] / "receipts" /
                      "world-entry" / f"{entry_id}.json")
        entry = wait_world_entry(entry_path, entry_id, args.world_timeout)
        expected_entry = {
            "schema": WORLD_ENTRY_RECEIPT_SCHEMA,
            "request_id": entry_id,
            "creator_session_id": args.session,
            "state": "entered",
            "machine": args.machine,
            "world_uid": args.world_uid,
            "world_name": args.world,
            "character_profile": args.character,
        }
        if any(entry.get(key) != value for key, value in expected_entry.items()):
            raise RuntimeError("world_entry_identity_mismatch:" + str(entry.get("detail")))
        entered = True
        shutil.copy2(entry_path, run_root / "world-entry-receipt.json")

        status = driver.request("runtime", "status")
        if status.get("creator_build_enabled") is True:
            driver.authority_enabled = True
            driver.disable_authority()
        if driver.authority_enabled:
            raise RuntimeError("creator_authority_preexisting_cleanup_failed")

        check = driver.request("lab", "blueprint_check",
                               driver.lab_fields("blueprint_check"))
        detail = str(check.get("detail") or "")
        if (f"{args.piece_count} buildable piece(s)" not in detail
                or args.canonical_pieces_sha256 not in detail
                or f"Ready. questlab_blueprint build {args.blueprint}" not in detail):
            raise RuntimeError("blueprint_check_contract_mismatch")
        for field, expected_hash in (("artifact_path", args.capture_sha256),
                                     ("blueprint_path", args.blueprint_sha256)):
            artifact = Path(str(check.get(field) or ""))
            if not artifact.is_file() or sha256(artifact) != expected_hash:
                raise RuntimeError(f"blueprint_check_{field}_hash_mismatch")

        count_receipt = driver.request(
            "lab", "blueprint_count", driver.lab_fields("blueprint_count"))
        standing_count = driver.count(count_receipt)
        if standing_count == 0:
            authority = driver.request("runtime", "build_on")
            driver.authority_enabled = authority.get("creator_build_enabled") is True
            if not driver.authority_enabled:
                raise RuntimeError("creator_authority_not_enabled")
            driver.build_attempted = True
            built = driver.request("lab", "blueprint_build",
                                   driver.lab_fields("blueprint_build", placed=True))
            if getattr(args, "placement_mode", "at") == "ground":
                if driver.count(built) != args.piece_count:
                    raise RuntimeError("blueprint_build_ground_count_mismatch")
            elif (not driver.placement_matches(built)
                  or f"placed={args.piece_count}" not in str(built.get("detail"))
                  or "failed=0" not in str(built.get("detail"))):
                raise RuntimeError("blueprint_build_placement_mismatch")
            count_receipt = driver.request(
                "lab", "blueprint_count", driver.lab_fields("blueprint_count"))
            standing_count = driver.count(count_receipt)
            if standing_count != args.piece_count:
                raise RuntimeError(f"blueprint_applied_count:{standing_count}")
            build_action = "created"
        elif standing_count == args.piece_count:
            build_action = "reused"
        else:
            raise RuntimeError(f"warm_mark_count_drift:{standing_count}")

        count_detail = str(count_receipt.get("detail") or "")
        for expected_prefab in ("wood_floor 16", "wood_roof_45 8", "woodwall 16"):
            if expected_prefab not in count_detail:
                raise RuntimeError("warm_prefab_split_mismatch:" + expected_prefab)

        diff = driver.request("lab", "blueprint_diff",
                              driver.lab_fields("blueprint_diff", placed=True))
        diff_detail = str(diff.get("detail") or "")
        if (not driver.placement_matches(diff)
                or f"capture diff {args.blueprint}: MATCH" not in diff_detail
                or f"expected {args.piece_count}, selected {args.piece_count}, missing 0, extra 0"
                    not in diff_detail):
            raise RuntimeError("blueprint_diff_placement_mismatch")
        screenshot = capture_screenshot(run_root)
    except Exception as error:
        failure = f"{type(error).__name__}:{error}"
    finally:
        try:
            driver.disable_authority()
        except Exception as error:
            failure = failure or f"cleanup_build_off:{type(error).__name__}:{error}"
        # A failed lap removes only pieces it created itself. A previously accepted warm build
        # is never torn down merely because a later probe failed. Authority cleanup happens
        # first so a failed build-off also turns a newly created structure into owned cleanup.
        if failure is not None and driver.build_attempted:
            try:
                driver.clear_if_needed()
            except Exception as error:
                failure = failure or f"cleanup_clear:{type(error).__name__}:{error}"
        if driver.authority_enabled:
            try:
                driver.disable_authority()
            except Exception as error:
                failure = failure or f"cleanup_build_off_retry:{type(error).__name__}:{error}"
        if entered:
            try:
                final_status = driver.request("runtime", "status", allow_failure=True)
                if (final_status.get("state") != "completed"
                        or final_status.get("creator_build_enabled") is not False):
                    failure = failure or "creator_authority_cleanup_unverified"
            except Exception as error:
                failure = failure or f"cleanup_status:{type(error).__name__}:{error}"

    pair_root = paths["lab"] / "blueprints"
    pair = {
        f"{args.blueprint}.capture.json": pin(
            pair_root / f"{args.blueprint}.capture.json"),
        f"{args.blueprint}.blueprint": pin(
            pair_root / f"{args.blueprint}.blueprint"),
    }
    canonical_unchanged = (
        pair[f"{args.blueprint}.capture.json"]["sha256"] == args.capture_sha256
        and pair[f"{args.blueprint}.blueprint"]["sha256"] == args.blueprint_sha256)
    if not canonical_unchanged:
        failure = failure or "canonical_artifact_drift"
    running = bool(process_snapshot())
    result = {
        "schema": WARM_SCHEMA,
        "status": "PASS" if failure is None else "FAIL",
        "failure": failure,
        "started_utc": iso(started),
        "completed_utc": iso(utc_now()),
        "identity": {
            "machine": args.machine,
            "world": args.world,
            "world_uid": args.world_uid,
            "character": args.character,
            "creator_session_id": args.session,
            "blueprint_name": args.blueprint,
        },
        "placement": ({"mode": "ground"}
                      if getattr(args, "placement_mode", "at") == "ground"
                      else {"mode": "at", "x": args.x, "y": args.y, "z": args.z,
                            "yaw": args.yaw}),
        "piece_count": args.piece_count,
        "standing_piece_count": standing_count,
        "canonical_pieces_sha256": args.canonical_pieces_sha256,
        "canonical_pair": pair,
        "build_action": build_action,
        "sequence": driver.sequence,
        "screenshot": screenshot,
        "assertions": {
            "check_completed": any(item["operation"] == "blueprint_check"
                                   and item["state"] == "completed"
                                   for item in driver.sequence),
            "existing_build_reused": build_action == "reused",
            "build_created_once": build_action == "created",
            "placed_diff_match": any(item["operation"] == "blueprint_diff"
                                     and item["state"] == "completed"
                                     and "MATCH" in str(item["detail"])
                                     for item in driver.sequence),
            "marked_pieces_retained": failure is None and standing_count == args.piece_count,
            "creator_build_disabled": not driver.authority_enabled,
            "canonical_artifacts_unchanged": canonical_unchanged,
            "valheim_left_running": running,
        },
    }
    atomic_json(run_root / "warm-lap.json", result)
    return result


def safe_replace_tree(target: Path, backup: Path, allowed_parent: Path) -> None:
    target = target.resolve()
    allowed_parent = allowed_parent.resolve()
    if target.parent != allowed_parent or target.name not in {
            "comfy-quest-runtime", "comfy-quest-lab"}:
        raise RuntimeError(f"unsafe_restore_target:{target}")
    if target.exists():
        shutil.rmtree(target)
    if backup.exists():
        shutil.copytree(backup, target)


def restore(args: argparse.Namespace) -> dict[str, Any]:
    valheim, run_root = validate_roots(args.valheim_root, args.run_root)
    unity = args.unity_root.resolve()
    if process_snapshot():
        raise RuntimeError("restore_requires_valheim_stopped")
    before = read_json(run_root / "before.json")
    backup = run_root / "backup"
    paths = owned_paths(valheim)
    config_parent = (valheim / "BepInEx" / "config").resolve()
    safe_replace_tree(paths["runtime"], backup / "config" / "comfy-quest-runtime",
                      config_parent)
    safe_replace_tree(paths["lab"], backup / "config" / "comfy-quest-lab",
                      config_parent)
    atomic_copy(backup / "plugins" / "ComfyQuestLab.dll", paths["lab_plugin"])

    for root, prefix in ((unity / "worlds_local", args.world),
                         (unity / "characters_local", args.character)):
        root = root.resolve()
        if root.parent != unity or root.name not in {"worlds_local", "characters_local"}:
            raise RuntimeError("unsafe_save_restore_root")
        for path in root.glob(prefix + "*"):
            if path.is_file() or path.is_symlink():
                path.unlink()
    save_backup = backup / "saves"
    if save_backup.is_dir():
        for source in save_backup.rglob("*"):
            if source.is_file():
                target = unity / source.relative_to(save_backup)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, target)

    player_log = unity / "Player.log"
    log_text = ""
    if player_log.is_file():
        shutil.copy2(player_log, run_root / "Player.log")
        log_text = player_log.read_text(encoding="utf-8", errors="replace")
    after = snapshot(valheim, unity, args.world, args.character, args.blueprint)
    comparable = ("lab_plugin", "runtime", "lab", "saves", "canonical_pair")
    differences = [name for name in comparable if before.get(name) != after.get(name)]
    if after["valheim_processes"]:
        differences.append("valheim_processes")
    required_markers = [
        "[world-entry] entered: world_entry_complete",
        "completed — blueprint_check",
        "completed — blueprint_build",
        "completed — blueprint_diff",
        "completed — blueprint_clear",
        "Game - OnApplicationQuit",
        "World saved",
    ]
    live_path = run_root / "live-replay.json"
    live_passed = live_path.is_file() and read_json(live_path).get("status") == "PASS"
    missing_markers = ([marker for marker in required_markers if marker not in log_text]
                       if live_passed else [])
    if missing_markers:
        differences.append("player_log_markers")
    result = {
        "schema": RESTORE_SCHEMA,
        "status": "PASS" if not differences else "FAIL",
        "completed_utc": iso(utc_now()),
        "differences": differences,
        "missing_log_markers": missing_markers,
        "before": {name: before.get(name) for name in comparable},
        "after": {name: after.get(name) for name in comparable},
        "assertions": {
            "valheim_stopped": not after["valheim_processes"],
            "plugin_restored": before.get("lab_plugin") == after.get("lab_plugin"),
            "runtime_state_restored": before.get("runtime") == after.get("runtime"),
            "lab_state_and_staged_pair_restored": before.get("lab") == after.get("lab"),
            "world_and_character_bytes_restored": before.get("saves") == after.get("saves"),
            "graceful_shutdown_and_live_sequence_logged": live_passed and not missing_markers,
        },
    }
    atomic_json(run_root / "restoration.json", result)
    return result


def launch(args: argparse.Namespace) -> dict[str, Any]:
    valheim, run_root = validate_roots(args.valheim_root, args.run_root)
    if process_snapshot():
        raise RuntimeError("launch_requires_valheim_stopped")
    log_path = run_root / "valheim.stdout.log"
    environment = os.environ.copy()
    environment.update({
        "DISPLAY": args.display,
        "XDG_RUNTIME_DIR": "/run/user/1000",
        "DBUS_SESSION_BUS_ADDRESS": "unix:path=/run/user/1000/bus",
    })
    with log_path.open("wb") as log:
        process = subprocess.Popen(
            [str(valheim / "start_game_bepinex.sh"), "-console"],
            cwd=valheim,
            env=environment,
            stdin=subprocess.DEVNULL,
            stdout=log,
            stderr=subprocess.STDOUT,
            start_new_session=True,
        )
    time.sleep(2)
    observed = process_snapshot()
    if process.poll() is not None and not observed:
        raise RuntimeError(f"valheim_launch_exited:{process.returncode}")
    result = {
        "schema": "comfy-quest-architectural-live-launch/v1",
        "status": "STARTED",
        "started_utc": iso(utc_now()),
        "launcher_pid": process.pid,
        "valheim_processes": observed,
        "log": str(log_path),
    }
    atomic_json(run_root / "launch.json", result, create=True)
    return result


def stop(args: argparse.Namespace) -> dict[str, Any]:
    _, run_root = validate_roots(args.valheim_root, args.run_root)
    observed = process_snapshot()
    signaled: list[int] = []
    for item in observed:
        try:
            group = os.getpgid(item["pid"])
            if group not in signaled:
                os.killpg(group, signal.SIGINT)
                signaled.append(group)
        except ProcessLookupError:
            pass
    deadline = time.monotonic() + args.timeout
    while process_snapshot() and time.monotonic() < deadline:
        time.sleep(.5)
    remaining = process_snapshot()
    forced = False
    if remaining:
        forced = True
        for item in remaining:
            try:
                os.kill(item["pid"], signal.SIGTERM)
            except ProcessLookupError:
                pass
        deadline = time.monotonic() + 15
        while process_snapshot() and time.monotonic() < deadline:
            time.sleep(.25)
    after = process_snapshot()
    result = {
        "schema": "comfy-quest-architectural-live-shutdown/v1",
        "status": "PASS" if not after and not forced else "FAIL",
        "completed_utc": iso(utc_now()),
        "initial_processes": observed,
        "signaled_process_groups": signaled,
        "forced_term": forced,
        "remaining_processes": after,
    }
    atomic_json(run_root / "shutdown.json", result)
    return result


def common(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--valheim-root", type=Path, required=True)
    parser.add_argument("--run-root", type=Path, required=True)
    parser.add_argument("--unity-root", type=Path, required=True)
    parser.add_argument("--machine", required=True)
    parser.add_argument("--world", required=True)
    parser.add_argument("--world-uid", required=True)
    parser.add_argument("--character", required=True)
    parser.add_argument("--session", required=True)
    parser.add_argument("--blueprint", required=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    prepare_parser = commands.add_parser("prepare")
    common(prepare_parser)
    prepare_parser.add_argument("--world-display-name", required=True)
    prepare_parser.add_argument("--piece-count", type=int, required=True)
    prepare_parser.add_argument("--capture-sha256", required=True)
    prepare_parser.add_argument("--blueprint-sha256", required=True)
    prepare_parser.add_argument("--canonical-pieces-sha256", required=True)
    prepare_parser.add_argument("--candidate-lab", type=Path, required=True)
    prepare_parser.add_argument("--candidate-lab-sha256", required=True)

    reenter_parser = commands.add_parser("reenter")
    common(reenter_parser)
    reenter_parser.add_argument("--world-display-name", required=True)
    reenter_parser.add_argument("--piece-count", type=int, required=True)
    reenter_parser.add_argument("--capture-sha256", required=True)
    reenter_parser.add_argument("--blueprint-sha256", required=True)
    reenter_parser.add_argument("--canonical-pieces-sha256", required=True)
    reenter_parser.add_argument("--candidate-lab-sha256", required=True)

    run_parser = commands.add_parser("run")
    common(run_parser)
    run_parser.add_argument("--piece-count", type=int, required=True)
    run_parser.add_argument("--capture-sha256", required=True)
    run_parser.add_argument("--blueprint-sha256", required=True)
    run_parser.add_argument("--canonical-pieces-sha256", required=True)
    run_parser.add_argument("--x", type=float, required=True)
    run_parser.add_argument("--y", type=float, required=True)
    run_parser.add_argument("--z", type=float, required=True)
    run_parser.add_argument("--yaw", type=float, required=True)
    run_parser.add_argument("--placement-mode", choices=("at", "ground"), default="at")
    run_parser.add_argument("--world-timeout", type=int, default=600)
    run_parser.add_argument("--request-timeout", type=int, default=90)

    warm_parser = commands.add_parser("warm")
    common(warm_parser)
    warm_parser.add_argument("--session-root", type=Path, required=True)
    warm_parser.add_argument("--piece-count", type=int, required=True)
    warm_parser.add_argument("--capture-sha256", required=True)
    warm_parser.add_argument("--blueprint-sha256", required=True)
    warm_parser.add_argument("--canonical-pieces-sha256", required=True)
    warm_parser.add_argument("--candidate-lab-sha256", required=True)
    warm_parser.add_argument("--x", type=float, required=True)
    warm_parser.add_argument("--y", type=float, required=True)
    warm_parser.add_argument("--z", type=float, required=True)
    warm_parser.add_argument("--yaw", type=float, required=True)
    warm_parser.add_argument("--placement-mode", choices=("at", "ground"), default="at")
    warm_parser.add_argument("--world-timeout", type=int, default=600)
    warm_parser.add_argument("--request-timeout", type=int, default=90)

    restore_parser = commands.add_parser("restore")
    common(restore_parser)

    launch_parser = commands.add_parser("launch")
    launch_parser.add_argument("--valheim-root", type=Path, required=True)
    launch_parser.add_argument("--run-root", type=Path, required=True)
    launch_parser.add_argument("--display", default=":0")

    stop_parser = commands.add_parser("stop")
    stop_parser.add_argument("--valheim-root", type=Path, required=True)
    stop_parser.add_argument("--run-root", type=Path, required=True)
    stop_parser.add_argument("--timeout", type=int, default=150)
    return parser.parse_args()


def validate_args(args: argparse.Namespace) -> None:
    if args.command in {"launch", "stop"}:
        if args.command == "stop" and not 10 <= args.timeout <= 300:
            raise RuntimeError("stop_timeout_invalid")
        return
    require_safe_token(args.machine, "machine")
    require_safe_token(args.world, "world")
    require_safe_token(args.character, "character")
    require_safe_token(args.session, "session")
    require_safe_token(args.blueprint, "blueprint", maximum=64)
    if not re.fullmatch(r"-?[1-9][0-9]*", args.world_uid or ""):
        raise RuntimeError("world_uid_invalid")
    if args.command in {"run", "warm", "reenter"}:
        for label in ("capture_sha256", "blueprint_sha256",
                      "canonical_pieces_sha256"):
            require_sha(getattr(args, label), label)
    if args.command in {"warm", "reenter"}:
        require_sha(args.candidate_lab_sha256, "candidate_lab")
    if args.command in {"run", "warm"}:
        args.x = require_finite(args.x, "x")
        args.y = require_finite(args.y, "y")
        args.z = require_finite(args.z, "z")
        args.yaw = require_finite(args.yaw, "yaw")
        if abs(args.yaw) > 3600:
            raise RuntimeError("yaw_invalid")
    if getattr(args, "piece_count", 1) < 1 or getattr(args, "piece_count", 1) > 256:
        raise RuntimeError("piece_count_invalid")


def main() -> int:
    args = parse_args()
    try:
        validate_args(args)
        if args.command == "prepare":
            result = prepare(args)
        elif args.command == "reenter":
            result = reenter(args)
        elif args.command == "run":
            result = run_live(args)
        elif args.command == "warm":
            result = run_warm(args)
        elif args.command == "launch":
            result = launch(args)
        elif args.command == "stop":
            result = stop(args)
        else:
            result = restore(args)
        print(json.dumps(result, sort_keys=True))
        return 0 if result.get("status") in {"READY", "STARTED", "PASS"} else 1
    except Exception as error:
        print(json.dumps({"status": "ERROR", "error": type(error).__name__,
                          "detail": str(error)}, sort_keys=True))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

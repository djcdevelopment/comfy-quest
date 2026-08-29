#!/usr/bin/env python3
"""Hash-only AM4 proof for the architectural Build staging boundary.

This probe never launches Valheim and never writes beneath the Valheim root. Its only
writes are explicit evidence JSON files under the caller-owned run directory.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import tempfile


STAGE_SCHEMA = "comfy-quest-studio-build-stage/v1"
PROOF_SCHEMA = "comfy-quest-studio-architectural-am4-proof/v1"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def sha256_prefix(path: Path, length: int) -> str:
    digest = hashlib.sha256()
    remaining = length
    with path.open("rb") as stream:
        while remaining:
            block = stream.read(min(1024 * 1024, remaining))
            if not block:
                raise RuntimeError(f"file ended before {length} bytes: {path}")
            digest.update(block)
            remaining -= len(block)
    return digest.hexdigest()


def files(root: Path, *, exclude: Path | None = None) -> dict[str, dict]:
    if not root.is_dir():
        return {}
    result: dict[str, dict] = {}
    excluded = exclude.resolve() if exclude else None
    for path in sorted(root.rglob("*")):
        if not path.is_file():
            continue
        resolved = path.resolve()
        if excluded and (resolved == excluded or excluded in resolved.parents):
            continue
        relative = path.relative_to(root).as_posix()
        stat = path.stat()
        result[relative] = {
            "bytes": stat.st_size,
            "sha256": sha256(path),
            "mtime_ns": stat.st_mtime_ns,
        }
    return result


def valheim_processes() -> list[dict]:
    result = []
    proc = Path("/proc")
    if not proc.is_dir():
        return result
    for entry in proc.iterdir():
        if not entry.name.isdigit():
            continue
        try:
            command = (entry / "comm").read_text(encoding="utf-8").strip()
            arguments = (entry / "cmdline").read_bytes().replace(b"\0", b" ").decode(
                "utf-8", errors="replace").strip()
        except OSError:
            continue
        executable = Path(arguments.split(" ", 1)[0]).name.lower() if arguments else ""
        if command.lower().startswith("valheim") or executable.startswith("valheim"):
            result.append({"pid": int(entry.name), "command": command,
                           "arguments": arguments[:1024]})
    return sorted(result, key=lambda value: value["pid"])


def snapshot(valheim: Path) -> dict:
    valheim = valheim.resolve()
    lab = valheim / "BepInEx" / "config" / "comfy-quest-lab"
    blueprints = lab / "blueprints"
    unity = Path.home() / ".config" / "unity3d" / "IronGate" / "Valheim"
    return {
        "schema": "comfy-quest-studio-architectural-am4-snapshot/v1",
        "machine": os.uname().nodename,
        "valheim_root": str(valheim),
        "valheim_processes": valheim_processes(),
        "world_and_character_files": files(unity),
        "lab_control_files": files(lab, exclude=blueprints),
        "creator_session_files": files(valheim / "BepInEx" / "config" /
                                       "comfy-creator-session"),
        "blueprint_files": files(blueprints),
    }


def atomic_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode()
    descriptor, temporary = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp",
                                             dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        try:
            os.unlink(temporary)
        except FileNotFoundError:
            pass


def latest_stage(state: Path) -> tuple[Path, dict]:
    candidates = sorted((state / "quest-studio" / "builds").glob(
        "*/latest-stage.json"))
    if len(candidates) != 1:
        raise RuntimeError(f"expected one latest stage receipt, found {len(candidates)}")
    path = candidates[0]
    value = json.loads(path.read_text(encoding="utf-8"))
    if value.get("schema") != STAGE_SCHEMA:
        raise RuntimeError("stage receipt schema differs")
    return path, value


def is_event_archive(name: str) -> bool:
    return (name.startswith("event-archive/") and
            Path(name).suffix.lower() in {".jsonl", ".csv"})


def warm_lab_control_is_safe(before: dict[str, dict], after: dict[str, dict],
                             lab: Path) -> bool:
    """Allow only additions/appends in the Lab's known event archive."""
    for name in set(before) | set(after):
        old = before.get(name)
        new = after.get(name)
        if old == new:
            continue
        if not is_event_archive(name) or new is None:
            return False
        if old is None:
            continue
        try:
            old_bytes = int(old["bytes"])
            if int(new["bytes"]) < old_bytes:
                return False
            if sha256_prefix(lab / name, old_bytes) != old["sha256"]:
                return False
        except (KeyError, OSError, RuntimeError, TypeError, ValueError):
            return False
    return True


def verify(before_path: Path, valheim: Path, state: Path, expected_world: str,
           expected_character: str, allow_warm_client: bool = False) -> dict:
    before = json.loads(before_path.read_text(encoding="utf-8"))
    after = snapshot(valheim)
    stage_path, stage = latest_stage(state)
    failures: list[str] = []
    if before.get("machine") != after["machine"] or after["machine"] != "am4":
        failures.append("machine_identity")
    if before.get("valheim_root") != after["valheim_root"]:
        failures.append("valheim_root_changed")
    before_processes = before.get("valheim_processes", [])
    after_processes = after["valheim_processes"]
    if allow_warm_client:
        if not before_processes:
            failures.append("warm_valheim_process_missing")
        elif before_processes != after_processes:
            failures.append("valheim_process_changed")
    elif before_processes or after_processes:
        failures.append("valheim_process_observed")
    if before.get("world_and_character_files") != after["world_and_character_files"]:
        failures.append("world_and_character_files_changed")
    if before.get("creator_session_files") != after["creator_session_files"]:
        failures.append("creator_session_files_changed")
    lab = valheim.resolve() / "BepInEx" / "config" / "comfy-quest-lab"
    if allow_warm_client:
        lab_control_safe = warm_lab_control_is_safe(
            before.get("lab_control_files", {}), after["lab_control_files"], lab)
    else:
        lab_control_safe = before.get("lab_control_files") == after["lab_control_files"]
    if not lab_control_safe:
        failures.append("lab_control_files_changed")
    world_names = set(after["world_and_character_files"])
    if not any(expected_world in name for name in world_names):
        failures.append("expected_world_missing")
    if not any(expected_character in name for name in world_names):
        failures.append("expected_character_missing")
    for field in ("placement_applied", "creator_session_started",
                  "mailbox_request_written", "world_mutation_performed"):
        if stage.get(field) is not False:
            failures.append("stage_" + field)
    artifacts = stage.get("artifacts")
    if not isinstance(artifacts, dict) or set(artifacts) != {"capture", "blueprint"}:
        failures.append("stage_artifact_set")
        artifacts = {}
    destination = (valheim / "BepInEx" / "config" / "comfy-quest-lab" /
                   "blueprints").resolve()
    artifact_names = set()
    for kind, artifact in artifacts.items():
        try:
            path = Path(artifact["path"]).resolve()
            artifact_names.add(path.name)
            if path.parent != destination or path.name != artifact["name"]:
                failures.append(kind + "_destination")
            if not path.is_file() or path.stat().st_size != artifact["bytes"]:
                failures.append(kind + "_size")
            elif sha256(path) != artifact["sha256"]:
                failures.append(kind + "_hash")
        except (KeyError, OSError, TypeError, ValueError):
            failures.append(kind + "_pin")
    before_blueprints = before.get("blueprint_files", {})
    after_blueprints = after["blueprint_files"]
    for name, pin in before_blueprints.items():
        if name not in artifact_names and after_blueprints.get(name) != pin:
            failures.append("unrelated_blueprint_changed:" + name)
    additions = set(after_blueprints) - set(before_blueprints)
    if not additions.issubset(artifact_names):
        failures.append("unexpected_blueprint_addition")
    for name in artifact_names:
        if name not in after_blueprints:
            failures.append("staged_artifact_missing:" + name)
    return {
        "schema": PROOF_SCHEMA,
        "status": "PASS" if not failures else "FAIL",
        "failures": failures,
        "machine": after["machine"],
        "world": expected_world,
        "character": expected_character,
        "valheim_root": after["valheim_root"],
        "stage_receipt_path": str(stage_path),
        "stage": stage,
        "before": before,
        "after": after,
        "assertions": {
            "valheim_never_started": not before_processes and not after_processes,
            "valheim_process_reused": bool(before_processes) and
                                      before_processes == after_processes,
            "warm_event_archive_only": allow_warm_client and lab_control_safe,
            "world_and_character_bytes_unchanged":
                before.get("world_and_character_files") ==
                after["world_and_character_files"],
            "no_creator_session_or_mailbox_write":
                lab_control_safe and
                before.get("creator_session_files") ==
                after["creator_session_files"],
            "only_canonical_pair_staged": not any(failure.startswith(
                ("capture_", "blueprint_", "stage_artifact", "unexpected_blueprint",
                 "unrelated_blueprint", "staged_artifact")) for failure in failures),
        },
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    snap = sub.add_parser("snapshot")
    snap.add_argument("--valheim-root", type=Path, required=True)
    snap.add_argument("--output", type=Path, required=True)
    proof = sub.add_parser("verify")
    proof.add_argument("--before", type=Path, required=True)
    proof.add_argument("--valheim-root", type=Path, required=True)
    proof.add_argument("--studio-state", type=Path, required=True)
    proof.add_argument("--expected-world", required=True)
    proof.add_argument("--expected-character", required=True)
    proof.add_argument("--allow-warm-client", action="store_true")
    proof.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.command == "snapshot":
        value = snapshot(args.valheim_root)
    else:
        value = verify(args.before, args.valheim_root, args.studio_state,
                       args.expected_world, args.expected_character,
                       args.allow_warm_client)
    atomic_json(args.output, value)
    print(json.dumps(value, sort_keys=True))
    return 0 if value.get("status", "PASS") == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())

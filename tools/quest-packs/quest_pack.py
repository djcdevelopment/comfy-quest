#!/usr/bin/env python3
"""Build, verify, preview, install, and uninstall local-first Quest Lab packs.

The pack is deliberately data-only. It may carry schema-1 quest views, bounded scenario
recipes, PlanBuild blueprints, prose, screenshots, and receipts; it can never carry code.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import sys
import tempfile
import uuid
import zipfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import Any, Iterable


PACK_SCHEMA = "comfy-quest-pack/v1"
SOURCE_SCHEMA = "comfy-quest-pack-source/v1"
INSTALL_SCHEMA = "comfy-quest-pack-install/v1"
SUITE_SCHEMA = "comfy-questlab-suite-receipt/v1"
ACCEPTANCE_SCHEMA = "comfy-questlab-gallery-acceptance/v1"
SOURCE_FILE = "quest-pack.source.json"
MANIFEST_FILE = "quest-pack.json"
MAX_FILES = 512
MAX_FILE_BYTES = 16 * 1024 * 1024
MAX_TOTAL_BYTES = 64 * 1024 * 1024
FIXED_ZIP_TIME = (2020, 1, 1, 0, 0, 0)
PACK_ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{1,63}$")
VERSION_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$")
KINDS: dict[str, set[str]] = {
    "quests": {".json"},
    "scenarios": {".json"},
    "blueprints": {".blueprint"},
    "docs": {".md", ".txt"},
    "screenshots": {".png", ".jpg", ".jpeg", ".webp"},
    "receipts": {".json"},
}


class PackError(RuntimeError):
    pass


def repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def default_capability_manifest() -> Path:
    return repo_root() / "tools" / "component-packets" / "samples" / "quest-capability-manifest.json"


def canonical_json(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_json_bytes(value: bytes, label: str) -> dict[str, Any]:
    try:
        parsed = json.loads(value.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PackError(f"{label}: invalid JSON: {exc}") from exc
    if not isinstance(parsed, dict):
        raise PackError(f"{label}: root must be an object")
    return parsed


def read_json_file(path: Path) -> dict[str, Any]:
    try:
        return read_json_bytes(path.read_bytes(), str(path))
    except OSError as exc:
        raise PackError(f"{path}: cannot read: {exc}") from exc


def safe_member(name: str) -> PurePosixPath:
    if not name or "\\" in name:
        raise PackError(f"unsafe pack path: {name!r}")
    path = PurePosixPath(name)
    if path.is_absolute() or any(part in ("", ".", "..") for part in path.parts):
        raise PackError(f"unsafe pack path: {name!r}")
    if ":" in path.parts[0]:
        raise PackError(f"unsafe pack path: {name!r}")
    return path


def validate_identity(metadata: dict[str, Any]) -> tuple[str, str]:
    if metadata.get("schema") != SOURCE_SCHEMA:
        raise PackError(f"{SOURCE_FILE}: schema must be {SOURCE_SCHEMA}")
    pack_id = str(metadata.get("pack_id", ""))
    version = str(metadata.get("version", ""))
    if not PACK_ID_RE.fullmatch(pack_id):
        raise PackError("pack_id must be 2-64 lowercase letters, numbers, dots, dashes, or underscores")
    if not VERSION_RE.fullmatch(version):
        raise PackError("version must be 1-64 filename-safe characters")
    for field in ("name", "creator", "license"):
        if not isinstance(metadata.get(field), str) or not metadata[field].strip():
            raise PackError(f"{SOURCE_FILE}: {field} must be non-empty text")
    if "description" in metadata and not isinstance(metadata["description"], str):
        raise PackError(f"{SOURCE_FILE}: description must be text")
    return pack_id, version


def load_safe_events(path: Path) -> tuple[set[str], str]:
    manifest = read_json_file(path)
    events = manifest.get("CreatorSafeEvents")
    if not isinstance(events, list) or not events or not all(isinstance(item, str) for item in events):
        raise PackError(f"{path}: CreatorSafeEvents must be a non-empty string array")
    return set(events), sha256_file(path)


def validate_quest_view(
    data: bytes,
    label: str,
    safe_events: set[str],
) -> tuple[list[str], set[str]]:
    root = read_json_bytes(data, label)
    if root.get("schema_version") != 1:
        raise PackError(f"{label}: schema_version must be 1")
    quests = root.get("quests")
    if not isinstance(quests, list) or not quests:
        raise PackError(f"{label}: quests must be a non-empty array")
    ids: list[str] = []
    required_events: set[str] = set()
    for index, quest in enumerate(quests):
        if not isinstance(quest, dict):
            raise PackError(f"{label}: quest[{index}] must be an object")
        for field in ("quest_id", "name", "guild"):
            if not isinstance(quest.get(field), str) or not quest[field].strip():
                raise PackError(f"{label}: quest[{index}].{field} must be non-empty text")
        ids.append(quest["quest_id"])
        trigger = quest.get("trigger")
        if trigger is None:
            continue
        if not isinstance(trigger, dict):
            raise PackError(f"{label}: quest[{index}].trigger must be null or an object")
        event = trigger.get("event")
        if not isinstance(event, str) or not event.strip():
            raise PackError(f"{label}: quest[{index}].trigger.event must be non-empty text")
        if event not in safe_events and event != "hit":
            raise PackError(f"{label}: quest[{index}] uses unsupported creator event {event!r}")
        required_events.add(event)
        where = trigger.get("where")
        if where is not None:
            if not isinstance(where, dict):
                raise PackError(f"{label}: quest[{index}].trigger.where must be an object")
            for key, value in where.items():
                if not isinstance(key, str) or not key.strip():
                    raise PackError(f"{label}: trigger.where field names must be non-empty text")
                if value is None or isinstance(value, (dict, list)) or not isinstance(value, (str, int, float, bool)):
                    raise PackError(f"{label}: trigger.where.{key} must be a scalar")
    duplicates = sorted({quest_id for quest_id in ids if ids.count(quest_id) > 1})
    if duplicates:
        raise PackError(f"{label}: duplicate quest_id values: {', '.join(duplicates)}")
    return ids, required_events


def classify_receipt(data: bytes, path: str) -> list[dict[str, str]]:
    try:
        receipt = read_json_bytes(data, path)
    except PackError:
        return []
    schema = receipt.get("schema")
    badges: list[str] = []
    if schema == SUITE_SCHEMA and receipt.get("state") == "complete" and receipt.get("verdict") == "pass":
        if receipt.get("suite") == "creator-events":
            badges.append("creator-contract-live-evaluator")
        elif receipt.get("suite") == "all-schools" and receipt.get("evidence_kind") == "live-gameplay":
            badges.append("all-schools-live-witnessed")
        if receipt.get("double_completions") == 0:
            badges.append("same-action-dedupe-verified")
    elif schema == ACCEPTANCE_SCHEMA:
        observations = receipt.get("observations")
        if (
            isinstance(observations, dict)
            and observations
            and all(value is True for value in observations.values())
            and bool(receipt.get("accepted_by"))
        ):
            badges.append("gallery-human-accepted")
    evidence_sha = sha256_bytes(data)
    return [{"badge": badge, "evidence": path, "sha256": evidence_sha} for badge in badges]


def collect_source(root: Path) -> list[tuple[str, str, bytes]]:
    collected: list[tuple[str, str, bytes]] = []
    total = 0
    for kind, extensions in KINDS.items():
        directory = root / kind
        if not directory.exists():
            continue
        if directory.is_symlink() or not directory.is_dir():
            raise PackError(f"{directory}: must be a real directory, not a link")
        for path in sorted(directory.rglob("*"), key=lambda item: item.as_posix().lower()):
            if path.is_symlink():
                raise PackError(f"{path}: links and special files are not allowed")
            if path.is_dir():
                continue
            if not path.is_file():
                raise PackError(f"{path}: links and special files are not allowed")
            if path.suffix.lower() not in extensions:
                raise PackError(f"{path}: extension is not allowed in {kind}/")
            relative = path.relative_to(root).as_posix()
            safe_member(relative)
            data = path.read_bytes()
            if len(data) > MAX_FILE_BYTES:
                raise PackError(f"{relative}: exceeds the {MAX_FILE_BYTES}-byte per-file limit")
            total += len(data)
            if total > MAX_TOTAL_BYTES:
                raise PackError(f"pack exceeds the {MAX_TOTAL_BYTES}-byte total limit")
            collected.append((kind, relative, data))
            if len(collected) > MAX_FILES:
                raise PackError(f"pack exceeds the {MAX_FILES}-file limit")
    if not any(kind == "quests" for kind, _, _ in collected):
        raise PackError("a quest pack must contain at least one quests/*.json file")
    return collected


def zip_write(archive: zipfile.ZipFile, name: str, data: bytes) -> None:
    info = zipfile.ZipInfo(name, FIXED_ZIP_TIME)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o100644 << 16
    archive.writestr(info, data, compresslevel=9)


def build_pack(source: Path, output: Path, capability_manifest: Path) -> dict[str, Any]:
    source = source.resolve()
    metadata = read_json_file(source / SOURCE_FILE)
    pack_id, version = validate_identity(metadata)
    safe_events, catalog_sha = load_safe_events(capability_manifest)
    payload = collect_source(source)
    all_quest_ids: list[str] = []
    required_events: set[str] = set()
    certifications: list[dict[str, str]] = []
    files: list[dict[str, Any]] = []
    for kind, relative, data in payload:
        if kind == "quests":
            ids, events = validate_quest_view(data, relative, safe_events)
            all_quest_ids.extend(ids)
            required_events.update(events)
        elif kind == "receipts":
            certifications.extend(classify_receipt(data, relative))
        files.append(
            {
                "path": relative,
                "kind": kind,
                "bytes": len(data),
                "sha256": sha256_bytes(data),
            }
        )
    duplicates = sorted({quest_id for quest_id in all_quest_ids if all_quest_ids.count(quest_id) > 1})
    if duplicates:
        raise PackError("quest_id values must be unique across the pack: " + ", ".join(duplicates))
    manifest = {
        "schema": PACK_SCHEMA,
        "pack_id": pack_id,
        "name": metadata["name"].strip(),
        "version": version,
        "creator": metadata["creator"].strip(),
        "license": metadata["license"].strip(),
        "description": metadata.get("description", "").strip(),
        "requirements": {
            "quest_schema": 1,
            "creator_events": sorted(required_events),
            "capability_manifest_sha256": catalog_sha,
        },
        "quest_ids": sorted(all_quest_ids),
        "certifications": sorted(certifications, key=lambda item: (item["badge"], item["evidence"])),
        "files": files,
    }
    output = output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".tmp-" + uuid.uuid4().hex)
    try:
        with zipfile.ZipFile(temporary, "w") as archive:
            zip_write(archive, MANIFEST_FILE, canonical_json(manifest))
            for _, relative, data in payload:
                zip_write(archive, relative, data)
        os.replace(temporary, output)
    finally:
        if temporary.exists():
            temporary.unlink()
    return {"manifest": manifest, "package": str(output), "package_sha256": sha256_file(output)}


def read_verified_pack(path: Path, capability_manifest: Path | None = None) -> tuple[dict[str, Any], dict[str, bytes], list[str]]:
    path = path.resolve()
    warnings: list[str] = []
    try:
        with zipfile.ZipFile(path, "r") as archive:
            infos = archive.infolist()
            names = [info.filename for info in infos]
            if len(names) != len(set(names)):
                raise PackError("pack contains duplicate member names")
            if len(names) > MAX_FILES + 1:
                raise PackError(f"pack exceeds the {MAX_FILES}-file limit")
            for info in infos:
                safe_member(info.filename)
                if info.is_dir():
                    raise PackError(f"pack contains an unexpected directory entry: {info.filename}")
                if info.file_size > MAX_FILE_BYTES:
                    raise PackError(f"{info.filename}: exceeds the per-file limit")
            if sum(info.file_size for info in infos) > MAX_TOTAL_BYTES:
                raise PackError("pack exceeds the total uncompressed size limit")
            if MANIFEST_FILE not in names:
                raise PackError(f"pack is missing {MANIFEST_FILE}")
            payload = {name: archive.read(name) for name in names if name != MANIFEST_FILE}
            manifest = read_json_bytes(archive.read(MANIFEST_FILE), MANIFEST_FILE)
    except (OSError, zipfile.BadZipFile, RuntimeError) as exc:
        if isinstance(exc, PackError):
            raise
        raise PackError(f"{path}: cannot read pack: {exc}") from exc
    if manifest.get("schema") != PACK_SCHEMA:
        raise PackError(f"pack schema must be {PACK_SCHEMA}")
    pack_id = str(manifest.get("pack_id", ""))
    version = str(manifest.get("version", ""))
    if not PACK_ID_RE.fullmatch(pack_id) or not VERSION_RE.fullmatch(version):
        raise PackError("pack identity is invalid")
    entries = manifest.get("files")
    if not isinstance(entries, list):
        raise PackError("manifest files must be an array")
    declared: dict[str, dict[str, Any]] = {}
    for entry in entries:
        if not isinstance(entry, dict) or not isinstance(entry.get("path"), str):
            raise PackError("manifest contains a malformed file entry")
        name = entry["path"]
        safe_member(name)
        if name in declared:
            raise PackError(f"manifest repeats {name}")
        kind = str(entry.get("kind", ""))
        if kind not in KINDS or not name.startswith(kind + "/"):
            raise PackError(f"{name}: kind/path mismatch")
        if PurePosixPath(name).suffix.lower() not in KINDS[kind]:
            raise PackError(f"{name}: extension is not allowed for {kind}")
        declared[name] = entry
    if set(declared) != set(payload):
        missing = sorted(set(declared) - set(payload))
        extra = sorted(set(payload) - set(declared))
        raise PackError(f"manifest/payload mismatch; missing={missing}, extra={extra}")
    for name, data in payload.items():
        entry = declared[name]
        if entry.get("bytes") != len(data) or entry.get("sha256") != sha256_bytes(data):
            raise PackError(f"{name}: size or SHA-256 mismatch")
    requirements = manifest.get("requirements")
    if not isinstance(requirements, dict) or requirements.get("quest_schema") != 1:
        raise PackError("pack requires an unsupported quest schema")
    if capability_manifest is not None:
        safe_events, current_sha = load_safe_events(capability_manifest)
        required = requirements.get("creator_events")
        if not isinstance(required, list) or not all(isinstance(event, str) for event in required):
            raise PackError("requirements.creator_events must be a string array")
        unsupported = sorted(set(required) - safe_events - {"hit"})
        if unsupported:
            raise PackError("current Quest Lab does not support events: " + ", ".join(unsupported))
        expected_sha = requirements.get("capability_manifest_sha256")
        if expected_sha != current_sha:
            warnings.append("capability catalog hash changed; every required event remains available")
    evidence = manifest.get("certifications", [])
    if not isinstance(evidence, list):
        raise PackError("certifications must be an array")
    for certification in evidence:
        if not isinstance(certification, dict):
            raise PackError("certification entry must be an object")
        evidence_path = certification.get("evidence")
        if evidence_path not in payload or certification.get("sha256") != sha256_bytes(payload[evidence_path]):
            raise PackError("certification evidence is missing or does not match")
    return manifest, payload, warnings


def quest_target_name(pack_id: str, source: str, digest: str) -> str:
    stem = PurePosixPath(source).stem
    safe_stem = re.sub(r"[^A-Za-z0-9._-]+", "-", stem).strip("-._") or "quests"
    return f"{pack_id}--{safe_stem}-{digest[:8]}.json"


def install_plan(manifest: dict[str, Any], quest_dir: Path, pack_root: Path) -> dict[str, Any]:
    pack_id = manifest["pack_id"]
    version = manifest["version"]
    install_dir = (pack_root / pack_id / version).resolve()
    quest_dir = quest_dir.resolve()
    quest_files = []
    for entry in manifest["files"]:
        if entry["kind"] != "quests":
            continue
        target = quest_dir / quest_target_name(pack_id, entry["path"], entry["sha256"])
        quest_files.append(
            {
                "source": entry["path"],
                "target": str(target),
                "sha256": entry["sha256"],
                "conflict": target.exists(),
            }
        )
    return {
        "schema": INSTALL_SCHEMA,
        "operation": "install-preview",
        "pack_id": pack_id,
        "version": version,
        "install_dir": str(install_dir),
        "install_dir_conflict": install_dir.exists(),
        "quest_files": quest_files,
        "ready": not install_dir.exists() and not any(item["conflict"] for item in quest_files),
    }


def install_pack(
    package: Path,
    quest_dir: Path,
    pack_root: Path | None,
    capability_manifest: Path,
    dry_run: bool,
) -> dict[str, Any]:
    manifest, payload, warnings = read_verified_pack(package, capability_manifest)
    quest_dir = quest_dir.resolve()
    pack_root = (pack_root or (quest_dir.parent / "quest-packs")).resolve()
    plan = install_plan(manifest, quest_dir, pack_root)
    plan["warnings"] = warnings
    plan["package_sha256"] = sha256_file(package)
    if dry_run:
        return plan
    if not plan["ready"]:
        raise PackError("install preview found a conflict; no files were changed")
    quest_dir.mkdir(parents=True, exist_ok=True)
    pack_root.mkdir(parents=True, exist_ok=True)
    install_dir = Path(plan["install_dir"])
    staging = pack_root / (".staging-" + uuid.uuid4().hex)
    created_quests: list[Path] = []
    try:
        staging.mkdir()
        for name, data in payload.items():
            target = staging.joinpath(*PurePosixPath(name).parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        manifest_bytes = canonical_json(manifest)
        (staging / MANIFEST_FILE).write_bytes(manifest_bytes)
        for item in plan["quest_files"]:
            source_data = payload[item["source"]]
            target = Path(item["target"])
            # The preview is useful to a human, but the exclusive create is the actual
            # no-overwrite guarantee if another process races us after preflight.
            with target.open("xb") as stream:
                stream.write(source_data)
                stream.flush()
                os.fsync(stream.fileno())
            created_quests.append(target)
        payload_files = [
            {"source": name, "sha256": sha256_bytes(data)}
            for name, data in sorted(payload.items())
        ]
        receipt = {
            "schema": INSTALL_SCHEMA,
            "installed_utc": datetime.now(timezone.utc).isoformat(),
            "pack_id": manifest["pack_id"],
            "version": manifest["version"],
            "package_sha256": plan["package_sha256"],
            "manifest_sha256": sha256_bytes(manifest_bytes),
            "quest_dir": str(quest_dir),
            "quest_files": [
                {"source": item["source"], "target": item["target"], "sha256": item["sha256"]}
                for item in plan["quest_files"]
            ],
            "payload_files": payload_files,
        }
        (staging / "install-receipt.json").write_bytes(canonical_json(receipt))
        install_dir.parent.mkdir(parents=True, exist_ok=True)
        staging.rename(install_dir)
        return {**receipt, "install_dir": str(install_dir), "warnings": warnings}
    except Exception:
        for target in created_quests:
            if target.exists():
                target.unlink()
        if staging.exists():
            shutil.rmtree(staging)
        raise


def resolve_install(pack_root: Path, pack_id: str, version: str | None) -> Path:
    if not PACK_ID_RE.fullmatch(pack_id):
        raise PackError("invalid pack_id")
    pack_dir = (pack_root.resolve() / pack_id).resolve()
    if pack_dir.parent != pack_root.resolve():
        raise PackError("pack path escaped the install root")
    if version:
        if not VERSION_RE.fullmatch(version):
            raise PackError("invalid version")
        selected = pack_dir / version
        if not selected.is_dir():
            raise PackError(f"pack {pack_id} version {version} is not installed")
        return selected
    versions = sorted(path for path in pack_dir.iterdir() if path.is_dir()) if pack_dir.is_dir() else []
    if len(versions) != 1:
        raise PackError(f"pack {pack_id} has {len(versions)} installed versions; specify --version")
    return versions[0]


def uninstall_pack(pack_id: str, quest_dir: Path, pack_root: Path | None, version: str | None) -> dict[str, Any]:
    quest_dir = quest_dir.resolve()
    pack_root = (pack_root or (quest_dir.parent / "quest-packs")).resolve()
    install_dir = resolve_install(pack_root, pack_id, version)
    receipt = read_json_file(install_dir / "install-receipt.json")
    if receipt.get("schema") != INSTALL_SCHEMA or receipt.get("pack_id") != pack_id:
        raise PackError("install receipt does not belong to the requested pack")
    targets: list[tuple[Path, str]] = []
    for item in receipt.get("quest_files", []):
        if not isinstance(item, dict):
            raise PackError("install receipt has a malformed quest entry")
        target = Path(str(item.get("target", ""))).resolve()
        if target.parent != quest_dir:
            raise PackError("install receipt names a quest outside the selected quest directory")
        if target.exists() and sha256_file(target) != item.get("sha256"):
            raise PackError(f"refusing to remove modified quest: {target}")
        targets.append((target, str(item.get("sha256", ""))))
    manifest_path = install_dir / MANIFEST_FILE
    if not manifest_path.is_file() or sha256_file(manifest_path) != receipt.get("manifest_sha256"):
        raise PackError(f"refusing to remove modified pack manifest: {manifest_path}")
    for item in receipt.get("payload_files", []):
        if not isinstance(item, dict) or not isinstance(item.get("source"), str):
            raise PackError("install receipt has a malformed payload entry")
        member = safe_member(item["source"])
        target = install_dir.joinpath(*member.parts).resolve()
        if install_dir not in target.parents or not target.is_file():
            raise PackError(f"refusing to remove missing pack payload: {target}")
        if sha256_file(target) != item.get("sha256"):
            raise PackError(f"refusing to remove modified pack payload: {target}")
    removed = []
    for target, _ in targets:
        if target.exists():
            target.unlink()
            removed.append(str(target))
    shutil.rmtree(install_dir)
    pack_dir = install_dir.parent
    if pack_dir.is_dir() and not any(pack_dir.iterdir()):
        pack_dir.rmdir()
    return {
        "schema": INSTALL_SCHEMA,
        "operation": "uninstall",
        "pack_id": pack_id,
        "version": receipt.get("version"),
        "removed_quest_files": removed,
        "removed_install_dir": str(install_dir),
    }


def summary(path: Path, capability_manifest: Path | None) -> dict[str, Any]:
    manifest, _, warnings = read_verified_pack(path, capability_manifest)
    return {
        "schema": PACK_SCHEMA,
        "package": str(path.resolve()),
        "package_sha256": sha256_file(path),
        "pack_id": manifest["pack_id"],
        "name": manifest.get("name"),
        "version": manifest["version"],
        "creator": manifest.get("creator"),
        "quest_ids": manifest.get("quest_ids", []),
        "required_events": manifest.get("requirements", {}).get("creator_events", []),
        "certifications": manifest.get("certifications", []),
        "files": len(manifest.get("files", [])),
        "warnings": warnings,
        "verdict": "pass",
    }


def emit(value: Any) -> None:
    print(json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False))


def parser() -> argparse.ArgumentParser:
    cli = argparse.ArgumentParser(description=__doc__)
    sub = cli.add_subparsers(dest="command", required=True)
    build = sub.add_parser("build", help="build a deterministic .questpack")
    build.add_argument("source", type=Path)
    build.add_argument("--output", type=Path, required=True)
    build.add_argument("--capability-manifest", type=Path, default=default_capability_manifest())
    inspect = sub.add_parser("inspect", help="verify and describe a pack")
    inspect.add_argument("package", type=Path)
    inspect.add_argument("--capability-manifest", type=Path, default=default_capability_manifest())
    install = sub.add_parser("install", help="preview or install without overwriting files")
    install.add_argument("package", type=Path)
    install.add_argument("--quest-dir", type=Path, required=True)
    install.add_argument("--pack-root", type=Path)
    install.add_argument("--capability-manifest", type=Path, default=default_capability_manifest())
    install.add_argument("--dry-run", action="store_true")
    uninstall = sub.add_parser("uninstall", help="remove an unchanged installed pack")
    uninstall.add_argument("pack_id")
    uninstall.add_argument("--quest-dir", type=Path, required=True)
    uninstall.add_argument("--pack-root", type=Path)
    uninstall.add_argument("--version")
    return cli


def main(argv: Iterable[str] | None = None) -> int:
    args = parser().parse_args(argv)
    try:
        if args.command == "build":
            emit(build_pack(args.source, args.output, args.capability_manifest))
        elif args.command == "inspect":
            emit(summary(args.package, args.capability_manifest))
        elif args.command == "install":
            emit(install_pack(args.package, args.quest_dir, args.pack_root, args.capability_manifest, args.dry_run))
        elif args.command == "uninstall":
            emit(uninstall_pack(args.pack_id, args.quest_dir, args.pack_root, args.version))
        return 0
    except PackError as exc:
        print(f"quest-pack: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

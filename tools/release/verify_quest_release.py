#!/usr/bin/env python3
"""Verify a Comfy Quest release without building licensed game code."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import tempfile
import zipfile
from pathlib import Path, PurePosixPath


SCHEMA = "comfy-quest-release-manifest/v1"
REPOSITORY = "djcdevelopment/comfy-quest"
ASSET_NAMES = (
    "questlab.html",
    "quest-lab.zip",
    "quest-picker.html",
    "quest-picker.zip",
)


class ReleaseError(RuntimeError):
    pass


def sha256(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def file_record(path: Path) -> dict[str, object]:
    payload = path.read_bytes()
    return {"name": path.name, "sha256": sha256(payload), "bytes": len(payload)}


def safe_zip_entries(archive: zipfile.ZipFile) -> dict[str, zipfile.ZipInfo]:
    result: dict[str, zipfile.ZipInfo] = {}
    for entry in archive.infolist():
        name = entry.filename
        path = PurePosixPath(name)
        if (
            not name
            or name.startswith("/")
            or "\\" in name
            or path.is_absolute()
            or ".." in path.parts
        ):
            raise ReleaseError(f"unsafe ZIP entry: {name!r}")
        if name in result:
            raise ReleaseError(f"duplicate ZIP entry: {name!r}")
        result[name] = entry
    return result


def load_json_bytes(payload: bytes, label: str) -> dict:
    try:
        value = json.loads(payload.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ReleaseError(f"{label} is not valid UTF-8 JSON: {exc}") from exc
    if not isinstance(value, dict):
        raise ReleaseError(f"{label} must be a JSON object")
    return value


def parse_checksums(path: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        match = re.fullmatch(r"([0-9a-f]{64})  ([A-Za-z0-9._-]+)", line)
        if not match:
            raise ReleaseError(f"SHA256SUMS line {line_number} is malformed")
        digest, name = match.groups()
        if name in result:
            raise ReleaseError(f"SHA256SUMS repeats {name}")
        result[name] = digest
    return result


def expected_version(tag: str) -> str:
    match = re.fullmatch(r"quest-v([0-9]+\.[0-9]+\.[0-9]+)-split-proof", tag)
    if not match:
        raise ReleaseError(
            "release tag must be quest-v<stable-semver>-split-proof"
        )
    return match.group(1)


def verify_release(
    release_dir: Path,
    tag: str,
    expected_questlab: Path | None = None,
    expected_revision: str | None = None,
) -> dict[str, object]:
    version = expected_version(tag)
    expected_files = set(ASSET_NAMES) | {"release-manifest.json", "SHA256SUMS"}
    actual_files = {
        path.name for path in release_dir.iterdir() if path.is_file()
    }
    if actual_files != expected_files:
        raise ReleaseError(
            f"release file set drifted: expected {sorted(expected_files)!r}, "
            f"got {sorted(actual_files)!r}"
        )

    manifest = load_json_bytes(
        (release_dir / "release-manifest.json").read_bytes(),
        "release-manifest.json",
    )
    if manifest.get("schema") != SCHEMA:
        raise ReleaseError(f"unexpected release schema: {manifest.get('schema')!r}")
    if manifest.get("repository") != REPOSITORY:
        raise ReleaseError(f"unexpected repository: {manifest.get('repository')!r}")
    if manifest.get("release_tag") != tag:
        raise ReleaseError("release manifest tag does not match the requested tag")
    if manifest.get("version") != version:
        raise ReleaseError("release manifest version does not match the tag")
    revision = manifest.get("revision")
    if not isinstance(revision, str) or not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ReleaseError("release manifest revision must be a full lowercase SHA")
    if expected_revision is not None and revision != expected_revision.lower():
        raise ReleaseError("release manifest revision does not match the checked-out tag")

    artifact_rows = manifest.get("artifacts")
    if not isinstance(artifact_rows, list):
        raise ReleaseError("release manifest artifacts must be an array")
    artifact_map: dict[str, dict] = {}
    for row in artifact_rows:
        if not isinstance(row, dict) or not isinstance(row.get("name"), str):
            raise ReleaseError("release manifest contains a malformed artifact row")
        if row["name"] in artifact_map:
            raise ReleaseError(f"release manifest repeats {row['name']}")
        artifact_map[row["name"]] = row
    if set(artifact_map) != set(ASSET_NAMES):
        raise ReleaseError("release manifest must name exactly the four Quest assets")

    checksums = parse_checksums(release_dir / "SHA256SUMS")
    if set(checksums) != set(ASSET_NAMES):
        raise ReleaseError("SHA256SUMS must name exactly the four Quest assets")
    for name in ASSET_NAMES:
        record = file_record(release_dir / name)
        manifest_row = artifact_map[name]
        if manifest_row != record:
            raise ReleaseError(f"manifest hash/byte record does not match {name}")
        if checksums[name] != record["sha256"]:
            raise ReleaseError(f"SHA256SUMS does not match {name}")

    questlab_bytes = (release_dir / "questlab.html").read_bytes()
    if b"<title>ComfyQuestLab" not in questlab_bytes:
        raise ReleaseError("questlab.html does not look like the generated Quest Lab tome")
    if expected_questlab is not None and questlab_bytes != expected_questlab.read_bytes():
        raise ReleaseError("released questlab.html drifted from the tagged generated file")

    picker_bytes = (release_dir / "quest-picker.html").read_bytes()
    if b"SAMPLE-CATALOG: synthetic demonstration data only" not in picker_bytes:
        raise ReleaseError("standalone Quest Picker is not the synthetic public artifact")
    with zipfile.ZipFile(release_dir / "quest-picker.zip") as archive:
        entries = safe_zip_entries(archive)
        if "quest-picker.html" not in entries:
            raise ReleaseError("quest-picker.zip has no quest-picker.html")
        zipped_picker = archive.read(entries["quest-picker.html"])
    if picker_bytes != zipped_picker:
        raise ReleaseError("standalone Quest Picker differs from the ZIP entry")

    quest_lab = manifest.get("quest_lab")
    if not isinstance(quest_lab, dict):
        raise ReleaseError("release manifest is missing Quest Lab identity")
    if quest_lab.get("package_schema") != "comfy-quest-package/v1":
        raise ReleaseError("Quest Lab package schema drifted")
    if quest_lab.get("plugin_version") != version:
        raise ReleaseError("Quest Lab plugin version does not match the release tag")
    release_id = quest_lab.get("release_id")
    if not isinstance(release_id, str) or not release_id or release_id == "dev":
        raise ReleaseError("Quest Lab release ID is missing or unbaked")

    with zipfile.ZipFile(release_dir / "quest-lab.zip") as archive:
        entries = safe_zip_entries(archive)
        if "manifest.json" not in entries or "ComfyQuestLab.dll" not in entries:
            raise ReleaseError("quest-lab.zip lacks its manifest or plugin DLL")
        package_manifest = load_json_bytes(
            archive.read(entries["manifest.json"]), "quest-lab.zip manifest.json"
        )
        dll_bytes = archive.read(entries["ComfyQuestLab.dll"])
    if package_manifest.get("schema") != quest_lab.get("package_schema"):
        raise ReleaseError("Quest Lab ZIP manifest schema does not match release identity")
    if package_manifest.get("tool") != "quest-lab":
        raise ReleaseError("Quest Lab ZIP manifest names the wrong tool")
    if package_manifest.get("version") != quest_lab.get("plugin_version"):
        raise ReleaseError("Quest Lab ZIP manifest version does not match release identity")
    if package_manifest.get("release_id") != release_id:
        raise ReleaseError("Quest Lab ZIP manifest release ID does not match release identity")
    package_files = package_manifest.get("files")
    if not isinstance(package_files, list):
        raise ReleaseError("Quest Lab ZIP manifest files must be an array")
    dll_rows = [
        row
        for row in package_files
        if isinstance(row, dict) and row.get("path") == "ComfyQuestLab.dll"
    ]
    if len(dll_rows) != 1:
        raise ReleaseError("Quest Lab ZIP manifest must identify one plugin DLL")
    expected_dll = {
        "path": "ComfyQuestLab.dll",
        "sha256": sha256(dll_bytes),
        "bytes": len(dll_bytes),
    }
    if dll_rows[0] != expected_dll:
        raise ReleaseError("Quest Lab ZIP manifest plugin hash/bytes do not match")
    if quest_lab.get("dll_sha256") != expected_dll["sha256"]:
        raise ReleaseError("release manifest Quest Lab DLL hash does not match")
    if quest_lab.get("dll_bytes") != expected_dll["bytes"]:
        raise ReleaseError("release manifest Quest Lab DLL byte count does not match")

    return {
        "tag": tag,
        "version": version,
        "revision": revision,
        "release_id": release_id,
        "assets": len(ASSET_NAMES),
    }


def write_synthetic_release(root: Path, tag: str) -> None:
    version = expected_version(tag)
    questlab = b"<!doctype html><title>ComfyQuestLab test</title>\n"
    picker = (
        b"<!doctype html><title>Quest Picker test</title>"
        b"<!-- SAMPLE-CATALOG: synthetic demonstration data only -->\n"
    )
    dll = b"synthetic-plugin-dll"
    package_manifest = {
        "schema": "comfy-quest-package/v1",
        "tool": "quest-lab",
        "version": version,
        "release_id": "questlab-self-test",
        "files": [
            {
                "path": "ComfyQuestLab.dll",
                "sha256": sha256(dll),
                "bytes": len(dll),
            }
        ],
    }
    (root / "questlab.html").write_bytes(questlab)
    (root / "quest-picker.html").write_bytes(picker)
    with zipfile.ZipFile(root / "quest-picker.zip", "w") as archive:
        archive.writestr("quest-picker.html", picker)
    with zipfile.ZipFile(root / "quest-lab.zip", "w") as archive:
        archive.writestr("ComfyQuestLab.dll", dll)
        archive.writestr(
            "manifest.json",
            json.dumps(package_manifest, separators=(",", ":")).encode(),
        )
    records = [file_record(root / name) for name in ASSET_NAMES]
    release_manifest = {
        "schema": SCHEMA,
        "repository": REPOSITORY,
        "release_tag": tag,
        "revision": "a" * 40,
        "version": version,
        "quest_lab": {
            "package_schema": package_manifest["schema"],
            "plugin_version": version,
            "release_id": package_manifest["release_id"],
            "dll_sha256": sha256(dll),
            "dll_bytes": len(dll),
        },
        "artifacts": records,
    }
    (root / "release-manifest.json").write_text(
        json.dumps(release_manifest, indent=2) + "\n", encoding="utf-8"
    )
    (root / "SHA256SUMS").write_text(
        "".join(f"{row['sha256']}  {row['name']}\n" for row in records),
        encoding="utf-8",
    )


def self_test() -> None:
    tag = "quest-v0.2.0-split-proof"
    with tempfile.TemporaryDirectory(prefix="comfy-quest-release-self-test-") as tmp:
        root = Path(tmp)
        write_synthetic_release(root, tag)
        verify_release(root, tag)

        questlab = root / "questlab.html"
        original = questlab.read_bytes()
        questlab.write_bytes(original + b"tamper")
        try:
            verify_release(root, tag)
        except ReleaseError:
            print("PASS: hash tamper was rejected")
        else:
            raise ReleaseError("hash tamper unexpectedly passed")
        questlab.write_bytes(original)

        picker = root / "quest-picker.html"
        picker.write_bytes(picker.read_bytes() + b"drift")
        changed = file_record(picker)
        manifest_path = root / "release-manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        manifest["artifacts"] = [
            changed if row["name"] == changed["name"] else row
            for row in manifest["artifacts"]
        ]
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        checksums = parse_checksums(root / "SHA256SUMS")
        checksums[changed["name"]] = str(changed["sha256"])
        (root / "SHA256SUMS").write_text(
            "".join(f"{checksums[name]}  {name}\n" for name in ASSET_NAMES),
            encoding="utf-8",
        )
        try:
            verify_release(root, tag)
        except ReleaseError as exc:
            if "differs from the ZIP" not in str(exc):
                raise
            print("PASS: standalone/ZIP drift was rejected after hashes were updated")
        else:
            raise ReleaseError("standalone/ZIP drift unexpectedly passed")
    print("SELFTEST PASS: valid, tampered, and drifted release cases behaved correctly")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--release-dir", type=Path)
    parser.add_argument("--expected-tag")
    parser.add_argument("--expected-questlab", type=Path)
    parser.add_argument("--expected-revision")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    try:
        if args.self_test:
            self_test()
        else:
            if args.release_dir is None or not args.expected_tag:
                parser.error("--release-dir and --expected-tag are required")
            result = verify_release(
                args.release_dir,
                args.expected_tag,
                args.expected_questlab,
                args.expected_revision,
            )
            print(
                "VERIFIED "
                + " ".join(f"{key}={value}" for key, value in result.items())
            )
    except (ReleaseError, OSError, zipfile.BadZipFile, json.JSONDecodeError) as exc:
        print(f"INVALID: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

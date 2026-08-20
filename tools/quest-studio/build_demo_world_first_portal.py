#!/usr/bin/env python3
"""Build or verify the deterministic Demo World: First Portal Runtime v2 bundle."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import sys
import zipfile
from pathlib import Path


REPO = Path(__file__).resolve().parents[2]
BUNDLE = REPO / "examples" / "demo-world" / "first-portal"
SOURCE = BUNDLE / "studio-project.json"
EXPERIENCE = BUNDLE / "experience.json"
EXPECTED = BUNDLE / "expected.json"
README = BUNDLE / "README.md"
BUNDLE_MANIFEST = BUNDLE / "manifest.json"
QUESTPACK = BUNDLE / "demo-world-first-portal-1.0.0.questpack"
FIXED_TIME = (1980, 1, 1, 0, 0, 0)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def content_hash(entries: list[tuple[str, bytes]]) -> str:
    digest = hashlib.sha256()
    for name, data in sorted(entries):
        digest.update((name + "\n").encode("utf-8"))
        digest.update(data)
    return digest.hexdigest()


def zip_entry(archive: zipfile.ZipFile, name: str, data: bytes) -> None:
    info = zipfile.ZipInfo(name, FIXED_TIME)
    info.create_system = 3
    info.compress_type = zipfile.ZIP_STORED
    info.external_attr = 0o100644 << 16
    archive.writestr(info, data)


def render() -> tuple[bytes, bytes]:
    source = json.loads(SOURCE.read_text(encoding="utf-8"))
    experience = json.loads(EXPERIENCE.read_text(encoding="utf-8"))
    expected = json.loads(EXPECTED.read_text(encoding="utf-8"))
    bundle_manifest = json.loads(BUNDLE_MANIFEST.read_text(encoding="utf-8"))
    if source.get("schema_version") != 3:
        raise ValueError("Studio source must be schema version 3")
    if experience.get("schema") != "comfy-quest-experience/v1":
        raise ValueError("Compiled experience must use comfy-quest-experience/v1")
    if expected.get("tutorial_id") != "demo-world-first-portal":
        raise ValueError("Expected behavior belongs to the wrong tutorial")
    if bundle_manifest.get("schema") != "comfy-quest-tutorial-bundle/v1":
        raise ValueError("Tutorial bundle manifest schema is invalid")

    experience_bytes = EXPERIENCE.read_bytes()
    experience_name = "experiences/demo-world-first-portal.json"
    runtime_manifest = {
        "schema": "comfy-quest-pack/v2",
        "pack_id": "demo-world-first-portal",
        "version": "1.0.0",
        "content_hash": content_hash([(experience_name, experience_bytes)]),
    }
    runtime_manifest_bytes = (json.dumps(runtime_manifest, indent=2) + "\n").encode("utf-8")
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        zip_entry(archive, "manifest.json", runtime_manifest_bytes)
        zip_entry(archive, experience_name, experience_bytes)
    package_bytes = buffer.getvalue()

    files = bundle_manifest["files"]
    values = {
        "studio_project": SOURCE.read_bytes(),
        "compiled_experience": experience_bytes,
        "runtime_questpack": package_bytes,
        "expected_behavior": EXPECTED.read_bytes(),
        "documentation": README.read_bytes(),
    }
    for key, data in values.items():
        files[key]["byte_count"] = len(data)
        files[key]["sha256"] = sha256(data)
    bundle_manifest_bytes = (json.dumps(bundle_manifest, indent=2) + "\n").encode("utf-8")
    return package_bytes, bundle_manifest_bytes


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    package_bytes, bundle_manifest_bytes = render()
    if args.check:
        stale = []
        if not QUESTPACK.exists() or QUESTPACK.read_bytes() != package_bytes:
            stale.append(QUESTPACK)
        if BUNDLE_MANIFEST.read_bytes() != bundle_manifest_bytes:
            stale.append(BUNDLE_MANIFEST)
        if stale:
            for path in stale:
                print(f"stale Demo World artifact: {path.relative_to(REPO)}", file=sys.stderr)
            return 1
        print(f"verified {QUESTPACK.name} · package {sha256(package_bytes)}")
        return 0
    if os.environ.get("COMFY_QUEST_DEMO_WORLD_WRITE") != "1":
        print("write mode requires the identity-guarded PowerShell wrapper", file=sys.stderr)
        return 2
    for path, data in ((QUESTPACK, package_bytes), (BUNDLE_MANIFEST, bundle_manifest_bytes)):
        temporary = path.with_name(path.name + ".tmp")
        temporary.write_bytes(data)
        os.replace(temporary, path)
    print(f"built {QUESTPACK.name} · package {sha256(package_bytes)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

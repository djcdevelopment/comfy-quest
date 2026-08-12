#!/usr/bin/env python3
"""Build or verify the deterministic Quest Runtime native-acceptance pack."""

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
LIVE = REPO / "network" / "mod" / "ComfyQuestRuntime" / "LiveTest"
MANIFEST = LIVE / "manifest.json"
FIXED_TIME = (1980, 1, 1, 0, 0, 0)


def content_hash(entries: list[tuple[str, bytes]]) -> str:
    digest = hashlib.sha256()
    for name, data in sorted(entries):
        digest.update((name + "\n").encode("utf-8"))
        digest.update(data)
    return digest.hexdigest()


def zip_entry(archive: zipfile.ZipFile, name: str, data: bytes) -> None:
    info = zipfile.ZipInfo(name, FIXED_TIME)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o100644 << 16
    archive.writestr(info, data, compresslevel=9)


def render() -> tuple[bytes, Path, bytes, str]:
    document = json.loads(MANIFEST.read_text(encoding="utf-8"))
    if document.get("schema") != "comfy-quest-pack/v2":
        raise ValueError("LiveTest manifest schema must be comfy-quest-pack/v2")
    version = document.get("version")
    if not isinstance(version, str) or not version:
        raise ValueError("LiveTest manifest version is required")
    sources = []
    for path in sorted((LIVE / "experiences").glob("*.json")):
        sources.append((f"experiences/{path.name}", path.read_bytes()))
    if len(sources) != 1:
        raise ValueError("Native acceptance pack must contain exactly one experience")
    digest = content_hash(sources)
    document["content_hash"] = digest
    manifest_bytes = (json.dumps(document, indent=2) + "\n").encode("utf-8")
    output = LIVE / f"omen-inscription-proof-{version}.questpack"
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        zip_entry(archive, "manifest.json", manifest_bytes)
        for name, data in sources:
            zip_entry(archive, name, data)
    return manifest_bytes, output, buffer.getvalue(), digest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    manifest_bytes, output, package_bytes, digest = render()
    if args.check:
        stale = []
        if MANIFEST.read_bytes() != manifest_bytes:
            stale.append(MANIFEST)
        if not output.exists() or output.read_bytes() != package_bytes:
            stale.append(output)
        if stale:
            for path in stale:
                print(f"stale live-test artifact: {path.relative_to(REPO)}", file=sys.stderr)
            return 1
        print(
            f"verified {output.name} · content {digest} · "
            f"package {hashlib.sha256(package_bytes).hexdigest()}"
        )
        return 0
    if os.environ.get("COMFY_QUEST_RUNTIME_PACK_WRITE") != "1":
        print("write mode requires the identity-guarded PowerShell wrapper", file=sys.stderr)
        return 2
    for path, data in ((MANIFEST, manifest_bytes), (output, package_bytes)):
        temporary = path.with_name(path.name + ".tmp")
        temporary.write_bytes(data)
        os.replace(temporary, path)
    print(
        f"built {output.name} · content {digest} · "
        f"package {hashlib.sha256(package_bytes).hexdigest()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

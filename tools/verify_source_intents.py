#!/usr/bin/env python3
"""Verify the immutable Baseline source intents pinned by Quest Mission Control."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any, Callable


REPO = Path(__file__).resolve().parents[1]
DEFAULT_MANIFEST = REPO / "docs" / "quest-mission-control.json"
REPOSITORY = "djcdevelopment/baseline"
INTENT_PATHS = {
    "01": "docs/arch/01_arcane_sight_runtime_observability.md",
    "02": "docs/arch/02_quest_lab_apprenticeship_spellbook.md",
    "03": "docs/arch/03_studio_live_valheim_creator_loop.md",
    "04": "docs/arch/04_community_artifact_ecosystem.md",
    "05": "docs/arch/05_adaptive_event_semantics.md",
}


class SourceIntentError(RuntimeError):
    """The published source authority is absent, mutable, or does not match its pin."""


def require_text(value: Any, where: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise SourceIntentError(f"{where} must be non-empty text")
    return value


def load_authority(path: Path) -> dict[str, Any]:
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise SourceIntentError(f"cannot load {path}: {exc}") from exc
    authority = manifest.get("source_intents") if isinstance(manifest, dict) else None
    if not isinstance(authority, dict):
        raise SourceIntentError("manifest source_intents must be an object")
    if authority.get("repository") != REPOSITORY:
        raise SourceIntentError(f"source_intents.repository must be {REPOSITORY}")
    revision = require_text(authority.get("revision"), "source_intents.revision")
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise SourceIntentError("source_intents.revision must be a lowercase 40-character SHA")
    documents = authority.get("documents")
    if not isinstance(documents, list):
        raise SourceIntentError("source_intents.documents must be a list")
    ids = [document.get("id") for document in documents if isinstance(document, dict)]
    if ids != list(INTENT_PATHS):
        raise SourceIntentError(f"source_intents.documents must contain ordered ids {list(INTENT_PATHS)}")
    return authority


def raw_url(repository: str, revision: str, path: str) -> str:
    return f"https://raw.githubusercontent.com/{repository}/{revision}/{path}"


def fetch_published(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "comfy-quest-source-intent-verifier/1"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.read()
    except (OSError, urllib.error.URLError) as exc:
        raise SourceIntentError(f"cannot fetch immutable source {url}: {exc}") from exc


def verify_authority(
    authority: dict[str, Any], fetch: Callable[[str], bytes] = fetch_published
) -> list[tuple[str, int, str]]:
    repository = require_text(authority.get("repository"), "source_intents.repository")
    revision = require_text(authority.get("revision"), "source_intents.revision")
    documents = authority.get("documents")
    if not isinstance(documents, list):
        raise SourceIntentError("source_intents.documents must be a list")
    verified: list[tuple[str, int, str]] = []
    for index, document in enumerate(documents):
        where = f"source_intents.documents[{index}]"
        if not isinstance(document, dict):
            raise SourceIntentError(f"{where} must be an object")
        intent_id = require_text(document.get("id"), f"{where}.id")
        expected_path = INTENT_PATHS.get(intent_id)
        path = require_text(document.get("path"), f"{where}.path")
        if path != expected_path:
            raise SourceIntentError(f"{where}.path must be {expected_path!r}; got {path!r}")
        expected_bytes = document.get("bytes")
        if isinstance(expected_bytes, bool) or not isinstance(expected_bytes, int) or expected_bytes <= 0:
            raise SourceIntentError(f"{where}.bytes must be a positive integer")
        expected_hash = require_text(document.get("sha256"), f"{where}.sha256")
        if not re.fullmatch(r"[0-9a-f]{64}", expected_hash):
            raise SourceIntentError(f"{where}.sha256 must be a lowercase SHA-256")
        url = raw_url(repository, revision, path)
        payload = fetch(url)
        actual_hash = hashlib.sha256(payload).hexdigest()
        if len(payload) != expected_bytes:
            raise SourceIntentError(
                f"intent {intent_id} byte count is {len(payload)}, expected {expected_bytes}: {url}"
            )
        if actual_hash != expected_hash:
            raise SourceIntentError(
                f"intent {intent_id} SHA-256 is {actual_hash}, expected {expected_hash}: {url}"
            )
        verified.append((intent_id, len(payload), actual_hash))
    if [item[0] for item in verified] != list(INTENT_PATHS):
        raise SourceIntentError(f"verified intent ids must be {list(INTENT_PATHS)}")
    return verified


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    args = parser.parse_args(argv)
    try:
        authority = load_authority(args.manifest.resolve())
        verified = verify_authority(authority)
        for intent_id, byte_count, digest in verified:
            print(f"VERIFIED intent {intent_id}: {byte_count} bytes sha256 {digest}")
        print(
            f"VERIFIED {len(verified)} Baseline source intents at "
            f"{authority['revision']}"
        )
        return 0
    except SourceIntentError as exc:
        print(f"BLOCKED: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

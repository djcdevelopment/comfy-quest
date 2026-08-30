#!/usr/bin/env python3
"""Build the deterministic CreatorOS Beta 1 campaign and projections."""

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
ROOT = REPO / "creatoros" / "beta1"
CAMPAIGN_SOURCE = ROOT / "campaign-source.json"
VENUE = ROOT / "venue.json"
QUEST_VIEW_SOURCE = ROOT / "quest-view-source.json"
CAMPAIGN = ROOT / "campaign.json"
QUEST_VIEW = ROOT / "quest-view.json"
CONTENT_MANIFEST = ROOT / "content-manifest.json"
QUESTPACK = ROOT / "slayers-signature-hunt-1.0.0.questpack"
FIXED_TIME = (1980, 1, 1, 0, 0, 0)


class BetaContentError(RuntimeError):
    pass


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def json_bytes(value: object) -> bytes:
    return (json.dumps(value, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def content_hash(entries: list[tuple[str, bytes]]) -> str:
    digest = hashlib.sha256()
    for name, data in sorted(entries):
        digest.update((name + "\n").encode("utf-8"))
        digest.update(data)
    return digest.hexdigest()


def named_hash(entries: list[tuple[str, bytes]]) -> str:
    return content_hash(entries)


def zip_entry(archive: zipfile.ZipFile, name: str, data: bytes) -> None:
    info = zipfile.ZipInfo(name, FIXED_TIME)
    info.create_system = 3
    info.compress_type = zipfile.ZIP_STORED
    info.external_attr = 0o100644 << 16
    archive.writestr(info, data)


def require_hash(value: object, label: str) -> str:
    text = str(value or "")
    if len(text) != 64 or any(character not in "0123456789abcdef" for character in text):
        raise BetaContentError(f"{label} must be lowercase SHA-256")
    return text


def validate_experience(document: dict, expected: dict) -> None:
    if document.get("schema") != "comfy-quest-experience/v2":
        raise BetaContentError("experience schema drifted")
    if document.get("id") != expected["experience_id"]:
        raise BetaContentError("campaign and experience identity disagree")
    prerequisites = document.get("prerequisites", [])
    successors = document.get("successor_experience_ids", [])
    if prerequisites != expected["prerequisite_experience_ids"]:
        raise BetaContentError(f"{document['id']} prerequisites drifted")
    if successors != expected["successor_experience_ids"]:
        raise BetaContentError(f"{document['id']} successors drifted")
    for stage in document.get("stages", []):
        for action in stage.get("entry_actions", []):
            if action.get("type") != "message":
                raise BetaContentError("beta entry actions must be message-only")
        for transition in stage.get("transitions", []):
            when = transition.get("when", {})
            if when.get("op") != "EVENT" or when.get("event") != "kill":
                raise BetaContentError("beta transitions must be direct kill events")
            for action in transition.get("actions", []):
                if action.get("type") != "message":
                    raise BetaContentError("beta transition actions must be message-only")


def render() -> dict[Path, bytes]:
    source = json.loads(CAMPAIGN_SOURCE.read_text(encoding="utf-8"))
    venue = json.loads(VENUE.read_text(encoding="utf-8"))
    view_source = json.loads(QUEST_VIEW_SOURCE.read_text(encoding="utf-8"))
    if source.get("schema") != "creatoros-campaign-source/v1":
        raise BetaContentError("campaign source schema drifted")
    if venue.get("schema") != "creatoros-guild-venue/v1" or venue.get("state") != "published":
        raise BetaContentError("venue must be one published v1 primitive")
    if source.get("venue", {}).get("venue_id") != venue.get("venue_id"):
        raise BetaContentError("campaign venue identity drifted")
    if source.get("campaign_id") not in venue.get("allowed_campaign_ids", []):
        raise BetaContentError("venue does not allow this campaign")
    require_hash(venue.get("source", {}).get("capsule_sha256"), "capsule_sha256")
    require_hash(venue.get("source", {}).get("canonical_pieces_sha256"), "canonical_pieces_sha256")
    artifacts = source.get("artifacts")
    if not isinstance(artifacts, list) or len(artifacts) != 2:
        raise BetaContentError("Beta 1 must contain exactly two campaign artifacts")

    experience_entries: list[tuple[str, bytes]] = []
    lineage: list[dict] = []
    source_entries: list[tuple[str, bytes]] = [
        ("campaign-source.json", CAMPAIGN_SOURCE.read_bytes()),
        ("venue.json", VENUE.read_bytes()),
    ]
    for artifact in artifacts:
        path = ROOT / artifact["path"]
        if path.parent != ROOT / "experiences" or not path.is_file():
            raise BetaContentError("experience path escaped the reviewed source directory")
        payload = path.read_bytes()
        document = json.loads(payload.decode("utf-8"))
        validate_experience(document, artifact)
        entry_name = f"experiences/{document['id']}.json"
        experience_entries.append((entry_name, payload))
        source_entries.append((artifact["path"], payload))
        lineage.append(
            {
                "project_id": artifact["project_id"],
                "source_quest_id": artifact["source_quest_id"],
                "experience_id": document["id"],
                "experience_sha256": sha256(payload),
                "prerequisite_experience_ids": artifact["prerequisite_experience_ids"],
                "successor_experience_ids": artifact["successor_experience_ids"],
            }
        )

    pack_content_hash = content_hash(experience_entries)
    pack_manifest = {
        "schema": "comfy-quest-pack/v2",
        "pack_id": source["campaign_id"],
        "version": source["version"],
        "content_hash": pack_content_hash,
    }
    pack_buffer = io.BytesIO()
    with zipfile.ZipFile(pack_buffer, "w") as archive:
        zip_entry(archive, "manifest.json", json_bytes(pack_manifest))
        for name, payload in sorted(experience_entries):
            zip_entry(archive, name, payload)
    questpack_bytes = pack_buffer.getvalue()

    venue_bytes = VENUE.read_bytes()
    campaign_source_bytes = CAMPAIGN_SOURCE.read_bytes()
    composition_hash = named_hash(source_entries)
    campaign = {
        "schema": "creatoros-campaign/v1",
        "campaign_id": source["campaign_id"],
        "revision": source["revision"],
        "version": source["version"],
        "title": source["title"],
        "creator": source["creator"],
        "guild": source["guild"],
        "era": source["era"],
        "release_state": source["release_state"],
        "composition_hash": composition_hash,
        "pack": {
            "path": QUESTPACK.name,
            "content_hash": pack_content_hash,
            "sha256": sha256(questpack_bytes),
        },
        "venue": {
            "path": "venue.json",
            "venue_id": venue["venue_id"],
            "revision": venue["revision"],
            "sha256": sha256(venue_bytes),
            "entry_anchor": venue["entry_anchor"],
        },
        "lineage": lineage,
        "beta_policy": source["beta_policy"],
        "compatibility": source["compatibility"],
    }
    campaign_bytes = json_bytes(campaign)

    quest_view = dict(view_source)
    quest_view["release_lineage"] = {
        "schema": "creatoros-quest-view-lineage/v1",
        "release_id": "creatoros-beta1",
        "campaign_id": source["campaign_id"],
        "campaign_revision": source["revision"],
        "composition_hash": composition_hash,
        "pack_content_hash": pack_content_hash,
        "venue_id": venue["venue_id"],
        "venue_revision": venue["revision"],
        "venue_sha256": sha256(venue_bytes),
        "experience_ids": {
            artifact["source_quest_id"]: artifact["experience_id"] for artifact in artifacts
        },
    }
    quest_view_bytes = json_bytes(quest_view)

    records = []
    payloads = {
        CAMPAIGN: campaign_bytes,
        QUEST_VIEW: quest_view_bytes,
        QUESTPACK: questpack_bytes,
    }
    for path, payload in sorted(payloads.items(), key=lambda item: item[0].name):
        records.append({"path": path.name, "sha256": sha256(payload), "bytes": len(payload)})
    content_manifest = {
        "schema": "creatoros-beta-content/v1",
        "release_id": "creatoros-beta1",
        "composition_hash": composition_hash,
        "pack_content_hash": pack_content_hash,
        "venue_sha256": sha256(venue_bytes),
        "generated": records,
    }
    payloads[CONTENT_MANIFEST] = json_bytes(content_manifest)
    return payloads


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    try:
        payloads = render()
    except (BetaContentError, KeyError, ValueError, json.JSONDecodeError) as exc:
        print(f"CreatorOS Beta 1 content invalid: {exc}", file=sys.stderr)
        return 1
    if args.check:
        stale = [path for path, payload in payloads.items() if not path.exists() or path.read_bytes() != payload]
        if stale:
            for path in stale:
                print(f"stale CreatorOS Beta 1 artifact: {path.relative_to(REPO)}", file=sys.stderr)
            return 1
        manifest = json.loads(payloads[CONTENT_MANIFEST])
        print(
            "verified CreatorOS Beta 1 · content "
            + manifest["pack_content_hash"]
            + " · composition "
            + manifest["composition_hash"]
        )
        return 0
    if os.environ.get("COMFY_CREATOROS_BETA1_WRITE") != "1":
        print("write mode requires the identity-guarded PowerShell wrapper", file=sys.stderr)
        return 2
    for path, payload in payloads.items():
        temporary = path.with_name(path.name + ".tmp")
        temporary.write_bytes(payload)
        os.replace(temporary, path)
    print("built CreatorOS Beta 1 content")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

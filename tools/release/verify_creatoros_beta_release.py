#!/usr/bin/env python3
"""Verify a complete, immutable CreatorOS public-beta release directory."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import zipfile
from pathlib import Path, PurePosixPath


SCHEMA = "creatoros-beta-release/v1"
REQUIRED_ROLES = {
    "world_db",
    "world_fwl",
    "venue",
    "campaign",
    "questpack",
    "quest_view",
    "runtime_dll",
    "contracts_dll",
    "newtonsoft_dll",
    "networksense_dll",
    "runtime_config",
    "networksense_client_config",
    "networksense_server_config",
    "server_quest_view",
    "studio_host",
    "install_script",
    "provenance",
}
SECRET_PATTERN = re.compile(
    r"(?i)(?:client[_ -]?access[_ -]?key|admin[_ -]?key|bearer|invite[_ -]?token|password)"
    r"\s*[=:]\s*[A-Za-z0-9+/_=-]{12,}"
)
STEAM_ID_PATTERN = re.compile(r"\b7656119\d{10}\b")


class ReleaseError(RuntimeError):
    pass


def sha256(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def require_hash(value: object, label: str) -> str:
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{64}", value):
        raise ReleaseError(f"{label} must be lowercase SHA-256")
    return value


def load_object(path: Path, label: str) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ReleaseError(f"{label} is not valid UTF-8 JSON: {exc}") from exc
    if not isinstance(value, dict):
        raise ReleaseError(f"{label} must be a JSON object")
    return value


def safe_relative(value: object, label: str) -> str:
    if not isinstance(value, str) or not value or "\\" in value:
        raise ReleaseError(f"{label} must be a non-empty POSIX relative path")
    parsed = PurePosixPath(value)
    if parsed.is_absolute() or ".." in parsed.parts or "." in parsed.parts:
        raise ReleaseError(f"{label} is unsafe")
    return value


def file_record(root: Path, path: str) -> dict[str, object]:
    payload = (root / Path(*PurePosixPath(path).parts)).read_bytes()
    return {"path": path, "sha256": sha256(payload), "bytes": len(payload)}


def named_hash(entries: list[tuple[str, bytes]]) -> str:
    digest = hashlib.sha256()
    for name, payload in sorted(entries):
        digest.update((name + "\n").encode("utf-8"))
        digest.update(payload)
    return digest.hexdigest()


def named_file_hash(entries: list[tuple[str, Path]]) -> str:
    digest = hashlib.sha256()
    for name, path in sorted(entries):
        digest.update((name + "\n").encode("utf-8"))
        with path.open("rb") as stream:
            while chunk := stream.read(1024 * 1024):
                digest.update(chunk)
    return digest.hexdigest()


def parse_sums(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        match = re.fullmatch(r"([0-9a-f]{64})  ([A-Za-z0-9._/-]+)", line)
        if not match:
            raise ReleaseError(f"SHA256SUMS line {number} is malformed")
        digest, name = match.groups()
        safe_relative(name, f"SHA256SUMS line {number}")
        if name in values:
            raise ReleaseError(f"SHA256SUMS repeats {name}")
        values[name] = digest
    return values


def config_value(text: str, section_name: str, key: str) -> str | None:
    section = re.search(
        rf"(?ms)^\[{re.escape(section_name)}\]\s*(.*?)(?=^\[|\Z)", text
    )
    if not section:
        return None
    match = re.search(rf"(?m)^{re.escape(key)}\s*=\s*(.*?)\s*$", section.group(1))
    return match.group(1) if match else None


def verify_questpack(path: Path, expected_hash: str, campaign: dict) -> None:
    with zipfile.ZipFile(path) as archive:
        names: set[str] = set()
        entries: list[tuple[str, bytes]] = []
        for entry in archive.infolist():
            name = safe_relative(entry.filename, "questpack entry")
            if name in names:
                raise ReleaseError(f"questpack repeats {name}")
            names.add(name)
            if name.startswith("experiences/") and name.endswith(".json"):
                entries.append((name, archive.read(entry)))
        if names != {
            "manifest.json",
            "experiences/slayers-air-drop.json",
            "experiences/slayers-cold-shot.json",
        }:
            raise ReleaseError("questpack file set drifted")
        pack_manifest = json.loads(archive.read("manifest.json"))
        if pack_manifest.get("schema") != "comfy-quest-pack/v2":
            raise ReleaseError("questpack schema drifted")
        if pack_manifest.get("pack_id") != campaign.get("campaign_id"):
            raise ReleaseError("questpack campaign identity drifted")
        actual_hash = named_hash(entries)
        if pack_manifest.get("content_hash") != actual_hash or expected_hash != actual_hash:
            raise ReleaseError("questpack content hash drifted")
        documents = [json.loads(payload) for _, payload in entries]
        if {value.get("id") for value in documents} != {"slayers-air-drop", "slayers-cold-shot"}:
            raise ReleaseError("questpack experience identity drifted")
        for document in documents:
            for stage in document.get("stages", []):
                actions = list(stage.get("entry_actions", []))
                for transition in stage.get("transitions", []):
                    if transition.get("when", {}).get("event") != "kill":
                        raise ReleaseError("beta questpack contains a non-kill transition")
                    actions.extend(transition.get("actions", []))
                if any(action.get("type") != "message" for action in actions):
                    raise ReleaseError("beta questpack contains a non-message action")


def verify_release(release_dir: Path) -> dict:
    manifest_path = release_dir / "release-manifest.json"
    sums_path = release_dir / "SHA256SUMS"
    if not manifest_path.is_file() or not sums_path.is_file():
        raise ReleaseError("release-manifest.json and SHA256SUMS are required")
    manifest = load_object(manifest_path, "release-manifest.json")
    if manifest.get("schema") != SCHEMA or manifest.get("release_id") != "creatoros-beta1":
        raise ReleaseError("release manifest identity drifted")
    if manifest.get("release_state") not in {"candidate-dirty", "frozen"}:
        raise ReleaseError("release_state must be candidate-dirty or frozen")
    source = manifest.get("source")
    if not isinstance(source, dict) or not re.fullmatch(r"[0-9a-f]{40}", str(source.get("revision", ""))):
        raise ReleaseError("source revision must be a full lowercase commit")
    if manifest["release_state"] == "frozen" and source.get("clean") is not True:
        raise ReleaseError("a frozen release requires a clean source checkout")

    rows = manifest.get("artifacts")
    if not isinstance(rows, list) or not rows:
        raise ReleaseError("artifacts must be a non-empty array")
    artifacts: dict[str, dict] = {}
    roles: dict[str, list[dict]] = {}
    for row in rows:
        if not isinstance(row, dict):
            raise ReleaseError("artifact row must be an object")
        path = safe_relative(row.get("path"), "artifact path")
        if path in artifacts:
            raise ReleaseError(f"artifact path repeated: {path}")
        role = row.get("role")
        if not isinstance(role, str) or not role:
            raise ReleaseError(f"artifact role missing: {path}")
        actual = file_record(release_dir, path)
        if {key: row.get(key) for key in ("path", "sha256", "bytes")} != actual:
            raise ReleaseError(f"artifact record mismatch: {path}")
        artifacts[path] = row
        roles.setdefault(role, []).append(row)
    missing_roles = REQUIRED_ROLES - set(roles)
    if missing_roles:
        raise ReleaseError(f"required artifact roles missing: {sorted(missing_roles)!r}")
    for role in REQUIRED_ROLES:
        if len(roles[role]) != 1:
            raise ReleaseError(f"required role must appear exactly once: {role}")
    overlay_roles = {"overlay_hud", "overlay_driver"} & set(roles)
    if overlay_roles and overlay_roles != {"overlay_hud", "overlay_driver"}:
        raise ReleaseError("optional Discoverlay integration requires both HUD and local driver")
    for role in overlay_roles:
        if len(roles[role]) != 1:
            raise ReleaseError(f"optional overlay role must appear exactly once: {role}")

    actual_files = {
        path.relative_to(release_dir).as_posix()
        for path in release_dir.rglob("*")
        if path.is_file()
    }
    expected_files = set(artifacts) | {"release-manifest.json", "SHA256SUMS"}
    if actual_files != expected_files:
        raise ReleaseError(
            f"release file set drifted: missing={sorted(expected_files - actual_files)!r}, "
            f"extra={sorted(actual_files - expected_files)!r}"
        )
    sums = parse_sums(sums_path)
    if set(sums) != set(artifacts):
        raise ReleaseError("SHA256SUMS must name every payload artifact exactly once")
    for path, row in artifacts.items():
        if sums[path] != row["sha256"]:
            raise ReleaseError(f"SHA256SUMS mismatch: {path}")

    world = manifest.get("world")
    if not isinstance(world, dict) or world.get("name") != "CreatorOSBeta1":
        raise ReleaseError("world name must be CreatorOSBeta1")
    uid = str(world.get("uid", ""))
    if not re.fullmatch(r"-?[1-9][0-9]*", uid):
        raise ReleaseError("world UID must be a non-zero integer string")
    db_row, fwl_row = roles["world_db"][0], roles["world_fwl"][0]
    if PurePosixPath(db_row["path"]).name != "CreatorOSBeta1.db" or PurePosixPath(fwl_row["path"]).name != "CreatorOSBeta1.fwl":
        raise ReleaseError("world files must use the CreatorOSBeta1 basename")
    pair_hash = named_file_hash(
        [
            ("CreatorOSBeta1.db", release_dir / Path(*PurePosixPath(db_row["path"]).parts)),
            ("CreatorOSBeta1.fwl", release_dir / Path(*PurePosixPath(fwl_row["path"]).parts)),
        ]
    )
    if world.get("pair_hash") != pair_hash:
        raise ReleaseError("world pair hash drifted")

    campaign_path = release_dir / Path(*PurePosixPath(roles["campaign"][0]["path"]).parts)
    campaign = load_object(campaign_path, "campaign")
    venue_path = release_dir / Path(*PurePosixPath(roles["venue"][0]["path"]).parts)
    venue = load_object(venue_path, "venue")
    view_path = release_dir / Path(*PurePosixPath(roles["quest_view"][0]["path"]).parts)
    view = load_object(view_path, "quest view")
    if campaign.get("schema") != "creatoros-campaign/v1" or campaign.get("release_state") != "immutable-beta":
        raise ReleaseError("campaign is not an immutable CreatorOS campaign")
    if venue.get("schema") != "creatoros-guild-venue/v1" or venue.get("state") != "published":
        raise ReleaseError("venue is not a published CreatorOS venue")
    if campaign.get("venue", {}).get("sha256") != sha256(venue_path.read_bytes()):
        raise ReleaseError("campaign venue hash drifted")
    lineage = view.get("release_lineage", {})
    if lineage.get("schema") != "creatoros-quest-view-lineage/v1":
        raise ReleaseError("quest view release lineage is missing")
    if lineage.get("composition_hash") != campaign.get("composition_hash"):
        raise ReleaseError("quest view composition lineage drifted")
    content_hash = require_hash(campaign.get("pack", {}).get("content_hash"), "campaign pack content hash")
    if lineage.get("pack_content_hash") != content_hash:
        raise ReleaseError("quest view pack lineage drifted")
    questpack_path = release_dir / Path(*PurePosixPath(roles["questpack"][0]["path"]).parts)
    if sha256(questpack_path.read_bytes()) != campaign.get("pack", {}).get("sha256"):
        raise ReleaseError("campaign questpack byte hash drifted")
    verify_questpack(questpack_path, content_hash, campaign)

    runtime_config_path = release_dir / Path(*PurePosixPath(roles["runtime_config"][0]["path"]).parts)
    runtime_config = runtime_config_path.read_text(encoding="utf-8")
    required_config = {
        "Enabled": "true",
        "WorldUid": uid,
        "ContentHash": content_hash,
    }
    section = re.search(r"(?ms)^\[DedicatedPersonalProgression\]\s*(.*?)(?=^\[|\Z)", runtime_config)
    if not section:
        raise ReleaseError("runtime config has no DedicatedPersonalProgression section")
    for key, expected in required_config.items():
        match = re.search(rf"(?m)^{re.escape(key)}\s*=\s*(\S+)\s*$", section.group(1))
        if not match or match.group(1) != expected:
            raise ReleaseError(f"runtime config {key} pin drifted")

    client_network_config = (
        release_dir / Path(*PurePosixPath(roles["networksense_client_config"][0]["path"]).parts)
    ).read_text(encoding="utf-8")
    server_network_config = (
        release_dir / Path(*PurePosixPath(roles["networksense_server_config"][0]["path"]).parts)
    ).read_text(encoding="utf-8")
    client_pins = {
        ("Lumberjacks", "lumberjacksCutoverMode"): "native",
        ("Lumberjacks", "zdoAuthoritativeConsumerEnabled"): "false",
        ("Lumberjacks", "lumberjacksMotionEnabled"): "false",
        ("LumberjacksGameSession", "lumberjacksGameSessionEnabled"): "false",
        ("Gameplay", "gameplayEventProducerEnabled"): "true",
        ("Gameplay", "questEvaluatorEnabled"): "true",
        ("Netcode", "zdoRedirectEnabled"): "false",
        ("Netcode", "handshakeResponderEnabled"): "false",
    }
    server_pins = {
        ("Lumberjacks", "lumberjacksGatewayUrl"): "http://gateway:4000",
        ("Lumberjacks", "lumberjacksCutoverMode"): "native",
        ("Lumberjacks", "zdoAuthoritativeConsumerEnabled"): "false",
        ("Lumberjacks", "lumberjacksMotionEnabled"): "false",
        ("LumberjacksGameSession", "lumberjacksGameSessionEnabled"): "false",
        ("Gameplay", "gameplayEventProducerEnabled"): "true",
        ("Gameplay", "questEvaluatorEnabled"): "true",
        ("Netcode", "zdoRedirectEnabled"): "false",
        ("Netcode", "handshakeResponderEnabled"): "true",
        ("Netcode", "handshakeResponderEndpoint"): "http://gateway:4000",
        ("Netcode", "handshakeResponderStrictMode"): "true",
        ("Netcode", "handshakeResponderWindowId"): "creatoros-beta1",
        ("Netcode", "handshakeResponderActiveSeconds"): "0",
    }
    for (section_name, key), expected in client_pins.items():
        if config_value(client_network_config, section_name, key) != expected:
            raise ReleaseError(f"NetworkSense client pin drifted: {section_name}.{key}")
    for (section_name, key), expected in server_pins.items():
        if config_value(server_network_config, section_name, key) != expected:
            raise ReleaseError(f"NetworkSense server pin drifted: {section_name}.{key}")
    server_view = release_dir / Path(*PurePosixPath(roles["server_quest_view"][0]["path"]).parts)
    if server_view.read_bytes() != view_path.read_bytes():
        raise ReleaseError("server and player quest-view lineage drifted")

    for role in ("runtime_dll", "contracts_dll", "newtonsoft_dll", "networksense_dll", "studio_host"):
        path = release_dir / Path(*PurePosixPath(roles[role][0]["path"]).parts)
        if not path.read_bytes().startswith(b"MZ"):
            raise ReleaseError(f"{role} is not a Windows PE artifact")
    if campaign.get("compatibility", {}).get("post_1_0_revalidation_required") is not True:
        raise ReleaseError("the Valheim 1.0 revalidation gate must remain explicit")

    for path in actual_files:
        candidate = release_dir / Path(*PurePosixPath(path).parts)
        if candidate.suffix.lower() in {".json", ".cfg", ".md", ".ps1", ".txt"}:
            text = candidate.read_text(encoding="utf-8", errors="replace")
            if SECRET_PATTERN.search(text) or STEAM_ID_PATTERN.search(text):
                raise ReleaseError(f"public release contains credential or Steam-ID shaped data: {path}")

    return {
        "status": "valid",
        "release_id": manifest["release_id"],
        "release_state": manifest["release_state"],
        "world_uid": uid,
        "pack_content_hash": content_hash,
        "artifact_count": len(artifacts),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release-dir", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = verify_release(args.release_dir.resolve())
    except (OSError, KeyError, ValueError, zipfile.BadZipFile, ReleaseError) as exc:
        print(f"CreatorOS beta release invalid: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Build or verify a deterministic, credential-free CreatorOS Beta 1 player ZIP."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import re
import sys
import zipfile
from pathlib import Path, PurePosixPath


ROOT = Path(__file__).resolve().parents[2]
RELEASE_VERIFIER = ROOT / "tools" / "release" / "verify_creatoros_beta_release.py"
PACKAGE_SCHEMA = "creatoros-beta-player-package/v1"
INSTALL_SCHEMA = "creatoros-beta-install-manifest/v1"
PACKAGE_ROOT = "CreatorOSBeta1"
LABEL = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,31}")
SHA256 = re.compile(r"[0-9a-f]{64}")
STEAM_ID = re.compile(r"7656119\d{10}")
SECRET = re.compile(
    rb"(?i)(?:client[_ -]?access[_ -]?key|admin[_ -]?key|bearer|invite[_ -]?token|password)"
    rb"\s*[=:]\s*[A-Za-z0-9+/_=-]{12,}"
)
ZIP_TIME = (2026, 8, 31, 0, 0, 0)


class PackageError(RuntimeError):
    pass


def _load_release_verifier():
    spec = importlib.util.spec_from_file_location("creatoros_release_verifier", RELEASE_VERIFIER)
    if spec is None or spec.loader is None:
        raise PackageError("release verifier could not be loaded")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _sha(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def _canonical(value: object) -> bytes:
    return (json.dumps(value, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def _safe_entry(name: str) -> str:
    if not name or "\\" in name:
        raise PackageError("ZIP entry must be a non-empty POSIX path")
    path = PurePosixPath(name)
    if path.is_absolute() or "." in path.parts or ".." in path.parts:
        raise PackageError(f"unsafe ZIP entry: {name}")
    return name


def _label(value: str) -> str:
    if not LABEL.fullmatch(value or ""):
        raise PackageError("player label must be 1-32 letters, digits, '.', '_' or '-'")
    if STEAM_ID.fullmatch(value):
        raise PackageError("player label must be a pseudonym, not a Steam numeric ID")
    return value


def _read_object(payload: bytes, label: str) -> dict:
    try:
        value = json.loads(payload.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PackageError(f"{label} is not valid UTF-8 JSON: {exc}") from exc
    if not isinstance(value, dict):
        raise PackageError(f"{label} must be a JSON object")
    return value


def _zip_write(archive: zipfile.ZipFile, name: str, payload: bytes) -> None:
    info = zipfile.ZipInfo(_safe_entry(name), ZIP_TIME)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o100644 << 16
    archive.writestr(info, payload, compresslevel=9)


def build_package(
    release_dir: Path,
    player_label: str,
    output: Path,
    *,
    allow_candidate: bool = False,
) -> dict[str, object]:
    player_label = _label(player_label)
    verifier = _load_release_verifier()
    try:
        release_result = verifier.verify_release(release_dir)
    except verifier.ReleaseError as exc:
        raise PackageError(f"parent release is invalid: {exc}") from exc

    release_manifest_bytes = (release_dir / "release-manifest.json").read_bytes()
    release_manifest = _read_object(release_manifest_bytes, "release-manifest.json")
    release_state = str(release_manifest.get("release_state", ""))
    if release_state != "frozen" and not allow_candidate:
        raise PackageError("player packages require a frozen release (or explicit --allow-candidate)")

    client = release_dir / "client"
    install_path = client / "install-manifest.json"
    if not install_path.is_file():
        raise PackageError("parent release has no client/install-manifest.json")
    install = _read_object(install_path.read_bytes(), "install-manifest.json")
    if install.get("schema") != INSTALL_SCHEMA or install.get("release_id") != "creatoros-beta1":
        raise PackageError("install manifest identity drifted")
    install["player_label"] = player_label
    install_bytes = _canonical(install)

    parent_sha = _sha(release_manifest_bytes)
    package_manifest = {
        "schema": PACKAGE_SCHEMA,
        "release_id": "creatoros-beta1",
        "release_state": release_state,
        "player_label": player_label,
        "credentials_included": False,
        "admission": "redeem-the-one-time-invite-delivered-out-of-band",
        "parent_release_manifest_sha256": parent_sha,
        "world_name": release_manifest.get("world", {}).get("name"),
        "world_uid": release_manifest.get("world", {}).get("uid"),
        "pack_content_hash": release_manifest.get("campaign", {}).get("pack_content_hash"),
    }
    readme = (
        "CreatorOS Beta 1\r\n"
        "================\r\n\r\n"
        "1. Install BepInEx for Valheim if it is not already installed.\r\n"
        "2. Close Valheim.\r\n"
        "3. Run Install-CreatorOsBeta1.ps1 from this folder.\r\n"
        "4. Redeem the one-time enrollment link sent to you separately.\r\n\r\n"
        "This ZIP intentionally contains no invite token, access key, password, or Steam ID.\r\n"
    ).encode("utf-8")

    output = output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".tmp")
    if temporary.exists():
        raise PackageError(f"temporary output already exists: {temporary}")
    try:
        with zipfile.ZipFile(temporary, "w") as archive:
            for source in sorted(path for path in client.rglob("*") if path.is_file()):
                relative = source.relative_to(client).as_posix()
                payload = install_bytes if relative == "install-manifest.json" else source.read_bytes()
                _zip_write(archive, f"{PACKAGE_ROOT}/{relative}", payload)
            _zip_write(archive, f"{PACKAGE_ROOT}/player-package.json", _canonical(package_manifest))
            _zip_write(archive, f"{PACKAGE_ROOT}/README.txt", readme)
        temporary.replace(output)
    finally:
        if temporary.exists():
            temporary.unlink()

    result = verify_package(output, release_dir=release_dir, allow_candidate=allow_candidate)
    return {
        **result,
        "path": str(output),
        "sha256": _sha(output.read_bytes()),
        "bytes": output.stat().st_size,
        "parent_artifact_count": release_result["artifact_count"],
    }


def verify_package(
    package: Path,
    *,
    release_dir: Path | None = None,
    allow_candidate: bool = False,
) -> dict[str, object]:
    if not package.is_file():
        raise PackageError(f"player package does not exist: {package}")
    with zipfile.ZipFile(package) as archive:
        entries: dict[str, bytes] = {}
        for info in archive.infolist():
            name = _safe_entry(info.filename)
            if info.is_dir():
                raise PackageError("player package must not contain directory entries")
            if name in entries:
                raise PackageError(f"duplicate ZIP entry: {name}")
            entries[name] = archive.read(info)

    prefix = PACKAGE_ROOT + "/"
    if not entries or any(not name.startswith(prefix) for name in entries):
        raise PackageError(f"every ZIP entry must live below {PACKAGE_ROOT}/")
    package_name = prefix + "player-package.json"
    install_name = prefix + "install-manifest.json"
    readme_name = prefix + "README.txt"
    for required in (package_name, install_name, readme_name, prefix + "Install-CreatorOsBeta1.ps1"):
        if required not in entries:
            raise PackageError(f"required ZIP entry missing: {required}")

    package_manifest = _read_object(entries[package_name], "player-package.json")
    install = _read_object(entries[install_name], "install-manifest.json")
    if package_manifest.get("schema") != PACKAGE_SCHEMA:
        raise PackageError("player package schema drifted")
    label = _label(str(package_manifest.get("player_label", "")))
    if install.get("schema") != INSTALL_SCHEMA or install.get("release_id") != "creatoros-beta1":
        raise PackageError("install manifest identity drifted")
    if install.get("player_label") != label:
        raise PackageError("player label disagrees between package and installer manifests")
    if package_manifest.get("credentials_included") is not False:
        raise PackageError("player package must explicitly declare credentials_included=false")
    state = package_manifest.get("release_state")
    if state != "frozen" and not allow_candidate:
        raise PackageError("player package is not derived from a frozen release")
    parent_sha = package_manifest.get("parent_release_manifest_sha256")
    if not isinstance(parent_sha, str) or not SHA256.fullmatch(parent_sha):
        raise PackageError("parent release manifest SHA-256 is invalid")

    declared: set[str] = set()
    files = install.get("files")
    if not isinstance(files, list) or not files:
        raise PackageError("install manifest files must be a non-empty array")
    for index, row in enumerate(files):
        if not isinstance(row, dict):
            raise PackageError(f"install file {index} must be an object")
        source = row.get("source")
        target = row.get("target")
        expected = row.get("sha256")
        if not isinstance(source, str) or not source.startswith("payload/"):
            raise PackageError(f"install file {index} has an invalid source")
        _safe_entry(source)
        if not isinstance(target, str):
            raise PackageError(f"install file {index} has an invalid target")
        _safe_entry(target)
        entry = prefix + source
        if entry in declared or entry not in entries:
            raise PackageError(f"install payload missing or repeated: {source}")
        declared.add(entry)
        payload = entries[entry]
        if expected != _sha(payload) or row.get("bytes") != len(payload):
            raise PackageError(f"install payload hash/size mismatch: {source}")

    actual_payload = {name for name in entries if name.startswith(prefix + "payload/")}
    if actual_payload != declared:
        raise PackageError("ZIP payload set disagrees with install manifest")
    for name, payload in entries.items():
        if SECRET.search(payload) or STEAM_ID.search(payload.decode("utf-8", errors="ignore")):
            raise PackageError(f"credential- or Steam-ID-shaped data found in {name}")

    if release_dir is not None:
        release_bytes = (release_dir / "release-manifest.json").read_bytes()
        if _sha(release_bytes) != parent_sha:
            raise PackageError("player package parent release hash mismatch")
        expected_client = {
            prefix + path.relative_to(release_dir / "client").as_posix()
            for path in (release_dir / "client").rglob("*")
            if path.is_file()
        }
        expected = expected_client | {package_name, readme_name}
        if set(entries) != expected:
            raise PackageError("player package file set disagrees with parent release client tree")

    return {
        "schema": PACKAGE_SCHEMA,
        "status": "valid",
        "player_label": label,
        "release_state": state,
        "entry_count": len(entries),
        "payload_count": len(declared),
        "parent_release_manifest_sha256": parent_sha,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    build = commands.add_parser("build")
    build.add_argument("--release-dir", type=Path, required=True)
    build.add_argument("--player-label", required=True)
    build.add_argument("--output", type=Path, required=True)
    build.add_argument("--allow-candidate", action="store_true")
    verify = commands.add_parser("verify")
    verify.add_argument("--package", type=Path, required=True)
    verify.add_argument("--release-dir", type=Path)
    verify.add_argument("--allow-candidate", action="store_true")
    args = parser.parse_args()
    try:
        if args.command == "build":
            result = build_package(
                args.release_dir.resolve(), args.player_label, args.output,
                allow_candidate=args.allow_candidate,
            )
        else:
            result = verify_package(
                args.package.resolve(),
                release_dir=args.release_dir.resolve() if args.release_dir else None,
                allow_candidate=args.allow_candidate,
            )
    except (PackageError, OSError, zipfile.BadZipFile) as exc:
        print(f"creatoros player package invalid: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

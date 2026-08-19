#!/usr/bin/env python3
"""Atomically repin Comfy Quest after both public packages are verifiably available."""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
import tempfile
from pathlib import Path
from xml.etree import ElementTree

from validate_nupkg import PackageError, validate_package
from wait_for_nuget import download


ROOT = Path(__file__).resolve().parents[2]
EXPECTED_ORIGIN = "djcdevelopment/comfy-quest"
CONTRACT_PROJECT = ROOT / "network/mod/ComfyQuestContracts/ComfyQuestContracts.csproj"
LAB_PROJECT = ROOT / "network/mod/ComfyQuestLab/ComfyQuestLab.csproj"
STUDIO_PROJECT = ROOT / "src/Quest.Studio/Quest.Studio.csproj"
CONFIG = ROOT / "nuget.config"
LOCAL_PACKAGES = ROOT / "packages-local"
KNOWN_LOCAL_FILES = {
    "Comfy.Quest.Contracts.0.5.0-local.nupkg",
    "Comfy.Quest.Studio.0.5.0-local.nupkg",
    "README.md",
}


class RepinError(RuntimeError):
    pass


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def replace_once(text: str, old: str, new: str, path: Path) -> str:
    count = text.count(old)
    if count != 1:
        raise RepinError(
            f"{path.relative_to(ROOT)} expected one occurrence of {old!r}, found {count}"
        )
    return text.replace(old, new)


def assert_identity() -> None:
    result = subprocess.run(
        ["git", "remote", "get-url", "origin"],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    origin = result.stdout.strip()
    if result.returncode or EXPECTED_ORIGIN not in origin:
        raise RepinError(
            f"repository identity failure: origin={origin!r}, "
            f"expected *{EXPECTED_ORIGIN}*"
        )


def clean_revision() -> str:
    status = subprocess.run(
        ["git", "status", "--porcelain=v1"],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    if status.returncode:
        raise RepinError("unable to inspect repository status")
    if status.stdout.strip():
        raise RepinError("public repin requires a clean checkout")
    result = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    revision = result.stdout.strip().lower()
    if result.returncode or not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise RepinError("unable to resolve the public package revision")
    return revision


def expected_public_config() -> str:
    return """<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
"""


def check_public_state(version: str) -> None:
    exact = f"[{version}]"
    problems: list[str] = []
    contracts = read(CONTRACT_PROJECT)
    lab = read(LAB_PROJECT)
    studio = read(STUDIO_PROJECT)
    if f"<Version>{version}</Version>" not in contracts:
        problems.append("Contracts producer version is not public")
    if f'Version="{exact}"' not in lab:
        problems.append("Quest Lab does not use the exact public Contracts pin")
    if f">{exact}</QuestContractsVersion>" not in studio:
        problems.append("Studio default Contracts dependency is not exact/public")
    if f"<Version>{version}</Version>" not in studio:
        problems.append("Studio producer version is not public")
    for path, text in (
        (CONTRACT_PROJECT, contracts),
        (LAB_PROJECT, lab),
        (STUDIO_PROJECT, studio),
    ):
        if "0.5.0-local" in text or "packages-local" in text:
            problems.append(f"{path.relative_to(ROOT)} retains interim package state")

    try:
        document = ElementTree.fromstring(read(CONFIG))
        sources = [
            (node.attrib.get("key"), node.attrib.get("value"))
            for node in document.findall("./packageSources/add")
        ]
        if sources != [("nuget.org", "https://api.nuget.org/v3/index.json")]:
            problems.append(f"nuget.config sources are not NuGet.org-only: {sources!r}")
    except ElementTree.ParseError as exc:
        problems.append(f"nuget.config is not valid XML: {exc}")
    if LOCAL_PACKAGES.exists():
        problems.append("packages-local still exists")
    if problems:
        raise RepinError("; ".join(problems))


def check_interim_state() -> None:
    problems: list[str] = []
    contracts = read(CONTRACT_PROJECT)
    lab = read(LAB_PROJECT)
    studio = read(STUDIO_PROJECT)
    if "<Version>0.5.0-local</Version>" not in contracts:
        problems.append("Contracts interim producer version drifted")
    if 'Version="[0.5.0-local]"' not in lab:
        problems.append("Quest Lab interim dependency is not exact")
    if ">[0.5.0-local]</QuestContractsVersion>" not in studio:
        problems.append("Studio interim Contracts dependency is not exact")
    if "<Version>0.5.0-local</Version>" not in studio:
        problems.append("Studio interim producer version drifted")
    try:
        document = ElementTree.fromstring(read(CONFIG))
        sources = [
            (node.attrib.get("key"), node.attrib.get("value"))
            for node in document.findall("./packageSources/add")
        ]
        expected = [
            ("packages-local", "./packages-local"),
            ("nuget.org", "https://api.nuget.org/v3/index.json"),
        ]
        if sources != expected:
            problems.append(f"interim NuGet sources drifted: {sources!r}")
    except ElementTree.ParseError as exc:
        problems.append(f"nuget.config is not valid XML: {exc}")
    required_files = {
        "Comfy.Quest.Contracts.0.5.0-local.nupkg",
        "Comfy.Quest.Studio.0.5.0-local.nupkg",
        "README.md",
    }
    actual_files = (
        {path.name for path in LOCAL_PACKAGES.iterdir()}
        if LOCAL_PACKAGES.is_dir()
        else set()
    )
    if actual_files != required_files:
        problems.append(
            f"interim packages-local payload drifted: {sorted(actual_files)!r}"
        )
    if problems:
        raise RepinError("; ".join(problems))


def fetch_and_validate(version: str, expected_commit: str) -> None:
    with tempfile.TemporaryDirectory(prefix="comfy-quest-public-repin-") as temporary:
        root = Path(temporary)
        for package_id, kind in (
            ("Comfy.Quest.Contracts", "contracts"),
            ("Comfy.Quest.Studio", "studio"),
        ):
            package = root / f"{package_id}.{version}.nupkg"
            download(package_id, version, package, timeout=120)
            validate_package(
                package,
                kind,
                version,
                expected_commit=expected_commit,
                allow_signature=True,
            )


def apply(version: str) -> None:
    assert_identity()
    revision = clean_revision()
    fetch_and_validate(version, revision)
    exact = f"[{version}]"
    changes: dict[Path, str] = {}

    contracts = read(CONTRACT_PROJECT)
    changes[CONTRACT_PROJECT] = replace_once(
        contracts,
        "<Version>0.5.0-local</Version>",
        f"<Version>{version}</Version>",
        CONTRACT_PROJECT,
    )

    lab = read(LAB_PROJECT)
    changes[LAB_PROJECT] = replace_once(
        lab,
        'Version="[0.5.0-local]"',
        f'Version="{exact}"',
        LAB_PROJECT,
    )

    studio = read(STUDIO_PROJECT)
    studio = replace_once(
        studio,
        ">[0.5.0-local]</QuestContractsVersion>",
        f">{exact}</QuestContractsVersion>",
        STUDIO_PROJECT,
    )
    studio = replace_once(
        studio,
        "<Version>0.5.0-local</Version>",
        f"<Version>{version}</Version>",
        STUDIO_PROJECT,
    )
    changes[STUDIO_PROJECT] = studio

    if not LOCAL_PACKAGES.is_dir():
        raise RepinError("packages-local is absent; interim state is not what was expected")
    actual_local_files = {path.name for path in LOCAL_PACKAGES.iterdir()}
    unexpected = actual_local_files - KNOWN_LOCAL_FILES
    if unexpected:
        raise RepinError(
            "packages-local contains unexpected files; refusing deletion: "
            + ", ".join(sorted(unexpected))
        )

    for path, text in changes.items():
        temporary = path.with_name(path.name + ".repinning")
        temporary.write_text(text, encoding="utf-8", newline="\n")
        temporary.replace(path)
    config_temp = CONFIG.with_name(CONFIG.name + ".repinning")
    config_temp.write_text(expected_public_config(), encoding="utf-8", newline="\n")
    config_temp.replace(CONFIG)

    for path in LOCAL_PACKAGES.iterdir():
        path.unlink()
    LOCAL_PACKAGES.rmdir()
    check_public_state(version)
    print(
        f"APPLIED exact public pins {exact}; nuget.config is NuGet.org-only; "
        f"packages-local removed; packages match revision {revision}"
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--check", action="store_true")
    action.add_argument("--check-interim", action="store_true")
    action.add_argument("--apply", action="store_true")
    parser.add_argument("--version", default="0.5.0")
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", args.version):
        parser.error("--version must be a stable three-part SemVer")
    try:
        if args.apply:
            apply(args.version)
        elif args.check_interim:
            check_interim_state()
            print("VERIFIED exact 0.5.0-local interim pins and local-first feed")
        else:
            check_public_state(args.version)
            print(
                f"VERIFIED exact public pins [{args.version}], "
                "NuGet.org-only config, and no local feed"
            )
    except (RepinError, PackageError, OSError, RuntimeError) as exc:
        print(f"REPINCHECK FAILED: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

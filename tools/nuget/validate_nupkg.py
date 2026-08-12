#!/usr/bin/env python3
"""Strictly inspect Comfy Quest NuGet packages before and after publication."""

from __future__ import annotations

import argparse
import re
import sys
import zipfile
from pathlib import Path, PurePosixPath
from xml.etree import ElementTree


REPOSITORY = "https://github.com/djcdevelopment/comfy-quest"
CONTRACT_ID = "Comfy.Quest.Contracts"
STUDIO_ID = "Comfy.Quest.Studio"
MOD_GLUE = {
    "QuestAuthoring.cs",
    "QuestEvent.cs",
    "QuestEventCatalog.g.cs",
    "QuestTriggerEvaluator.cs",
    "QuestViewLoader.cs",
    "TrackedQuest.cs",
}


class PackageError(RuntimeError):
    pass


def local_name(element: ElementTree.Element) -> str:
    return element.tag.rsplit("}", 1)[-1]


def children(element: ElementTree.Element, name: str) -> list[ElementTree.Element]:
    return [child for child in element if local_name(child) == name]


def descendant(element: ElementTree.Element, name: str) -> ElementTree.Element:
    found = [item for item in element.iter() if local_name(item) == name]
    if len(found) != 1:
        raise PackageError(f"expected exactly one {name} element, found {len(found)}")
    return found[0]


def text_of(metadata: ElementTree.Element, name: str) -> str:
    value = descendant(metadata, name).text
    return (value or "").strip()


def safe_entries(archive: zipfile.ZipFile) -> list[str]:
    names = [entry.filename for entry in archive.infolist()]
    if len(names) != len(set(names)):
        raise PackageError("package contains duplicate ZIP entries")
    for name in names:
        path = PurePosixPath(name)
        if (
            not name
            or name.startswith("/")
            or "\\" in name
            or path.is_absolute()
            or ".." in path.parts
        ):
            raise PackageError(f"unsafe package entry: {name!r}")
    return names


def validate_payload(names: set[str], kind: str, allow_signature: bool) -> None:
    package_id = CONTRACT_ID if kind == "contracts" else STUDIO_ID
    required = {
        "[Content_Types].xml",
        "_rels/.rels",
        f"{package_id}.nuspec",
        "PACKAGE-README.md",
    }
    if kind == "contracts":
        required.add("lib/netstandard2.0/ComfyQuestContracts.dll")
        required.update(f"contentFiles/cs/any/ModGlue/{name}" for name in MOD_GLUE)
    else:
        required.add("lib/net9.0/Comfy.Quest.Studio.dll")

    missing = required - names
    if missing:
        raise PackageError("package payload is missing: " + ", ".join(sorted(missing)))

    allowed = set(required)
    core_properties = {
        name
        for name in names
        if re.fullmatch(
            r"package/services/metadata/core-properties/[0-9a-f]{32}\.psmdcp",
            name,
        )
    }
    if len(core_properties) != 1:
        raise PackageError(
            "package must contain exactly one generated core-properties entry"
        )
    allowed.update(core_properties)
    if allow_signature and ".signature.p7s" in names:
        allowed.add(".signature.p7s")
    unexpected = names - allowed
    if unexpected:
        raise PackageError(
            "package payload contains unexpected entries: "
            + ", ".join(sorted(unexpected))
        )


def dependency_rows(metadata: ElementTree.Element) -> list[tuple[str, str, str]]:
    rows: list[tuple[str, str, str]] = []
    dependencies = descendant(metadata, "dependencies")
    for group in children(dependencies, "group"):
        framework = group.attrib.get("targetFramework", "")
        for dependency in children(group, "dependency"):
            rows.append(
                (
                    framework,
                    dependency.attrib.get("id", ""),
                    dependency.attrib.get("version", ""),
                )
            )
    return rows


def validate_package(
    package: Path,
    kind: str,
    version: str,
    expected_commit: str | None,
    allow_signature: bool = False,
) -> dict[str, str]:
    package_id = CONTRACT_ID if kind == "contracts" else STUDIO_ID
    expected_filename = f"{package_id}.{version}.nupkg"
    if package.name.lower() != expected_filename.lower():
        raise PackageError(
            f"package filename is {package.name!r}, expected {expected_filename!r}"
        )
    if expected_commit is not None and not re.fullmatch(
        r"[0-9a-fA-F]{40}", expected_commit
    ):
        raise PackageError("expected repository commit must be a full 40-hex SHA")

    with zipfile.ZipFile(package) as archive:
        names = set(safe_entries(archive))
        validate_payload(names, kind, allow_signature)
        nuspec_name = f"{package_id}.nuspec"
        root = ElementTree.fromstring(archive.read(nuspec_name))

    metadata = descendant(root, "metadata")
    actual_id = text_of(metadata, "id")
    actual_version = text_of(metadata, "version")
    if actual_id != package_id:
        raise PackageError(f"package id is {actual_id!r}, expected {package_id!r}")
    if actual_version != version:
        raise PackageError(
            f"package version is {actual_version!r}, expected {version!r}"
        )
    if text_of(metadata, "license") != "BUSL-1.1":
        raise PackageError("package license expression must be BUSL-1.1")
    if text_of(metadata, "readme") != "PACKAGE-README.md":
        raise PackageError("package readme must be PACKAGE-README.md")
    if not text_of(metadata, "title") or not text_of(metadata, "description"):
        raise PackageError("package title and description must be present")

    repository = descendant(metadata, "repository")
    repository_url = repository.attrib.get("url", "").rstrip("/")
    repository_commit = repository.attrib.get("commit", "")
    if repository.attrib.get("type") != "git":
        raise PackageError("package repository type must be git")
    if repository_url != REPOSITORY:
        raise PackageError(
            f"package repository URL is {repository_url!r}, expected {REPOSITORY!r}"
        )
    if not re.fullmatch(r"[0-9a-fA-F]{40}", repository_commit):
        raise PackageError("package repository commit is not a full SHA")
    if expected_commit and repository_commit.lower() != expected_commit.lower():
        raise PackageError(
            f"package repository commit is {repository_commit}, "
            f"expected {expected_commit}"
        )

    rows = dependency_rows(metadata)
    if kind == "contracts":
        expected_rows = [(".NETStandard2.0", "Newtonsoft.Json", "13.0.3")]
        if rows != expected_rows:
            raise PackageError(f"Contracts dependency rows drifted: {rows!r}")
        content_files = descendant(metadata, "contentFiles")
        declared = {
            row.attrib.get("include", "")
            for row in children(content_files, "files")
            if row.attrib.get("buildAction") == "Compile"
        }
        expected_sources = {f"cs/any/ModGlue/{name}" for name in MOD_GLUE}
        if declared != expected_sources:
            raise PackageError(
                "Contracts contentFiles declarations drifted: "
                + repr(sorted(declared))
            )
    else:
        exact_contract_version = f"[{version}]"
        expected_rows = [("net9.0", CONTRACT_ID, exact_contract_version)]
        if rows != expected_rows:
            raise PackageError(
                "Studio must have only the exact Contracts dependency "
                f"{exact_contract_version}; got {rows!r}"
            )
        framework_references = descendant(metadata, "frameworkReferences")
        references = [
            (
                group.attrib.get("targetFramework", ""),
                reference.attrib.get("name", ""),
            )
            for group in children(framework_references, "group")
            for reference in children(group, "frameworkReference")
        ]
        if references != [("net9.0", "Microsoft.AspNetCore.App")]:
            raise PackageError(f"Studio framework references drifted: {references!r}")

    return {
        "id": actual_id,
        "version": actual_version,
        "repository": repository_url,
        "commit": repository_commit.lower(),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--kind", required=True, choices=("contracts", "studio"))
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit")
    parser.add_argument("--allow-repository-signature", action="store_true")
    args = parser.parse_args()
    try:
        result = validate_package(
            args.package,
            args.kind,
            args.version,
            args.commit,
            args.allow_repository_signature,
        )
    except (PackageError, OSError, zipfile.BadZipFile, ElementTree.ParseError) as exc:
        print(f"INVALID: {exc}", file=sys.stderr)
        return 1
    print(
        "VERIFIED "
        + " ".join(f"{key}={value}" for key, value in result.items())
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

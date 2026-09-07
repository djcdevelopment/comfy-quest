#!/usr/bin/env python3
"""Build a traceable, self-contained Linux Creator/DM release from clean source.

Produces private-feed packages, four installable plugins, the standalone Studio
host and the recovery tools. It never deploys to a game installation or publishes
to a package registry. The caller retains release.json with the live evidence.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/nuget"))
from validate_nupkg import validate_package


def sha(path: Path) -> str:
    return hashlib.file_digest(path.open("rb"), "sha256").hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    subprocess.run(["powershell", "-NoProfile", "-File",
                    str(ROOT / "tools/Assert-RepoIdentity.ps1")], cwd=ROOT, check=True)
    if subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT).strip():
        raise RuntimeError("release_requires_clean_source")
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    contracts = ROOT / "network/mod/ComfyQuestContracts/ComfyQuestContracts.csproj"
    studio = ROOT / "src/Quest.Studio/Quest.Studio.csproj"
    version = ET.parse(contracts).findtext("./PropertyGroup/Version")
    if not version or ET.parse(studio).findtext("./PropertyGroup/Version") != version:
        raise RuntimeError("producer_versions_differ")
    out = args.output.resolve()
    if out.exists() and any(out.iterdir()):
        raise RuntimeError("release_output_must_be_empty")
    out.mkdir(parents=True, exist_ok=True)
    feed, cache = out / "packages", out / "nuget-cache"
    feed.mkdir()
    config = out / "nuget.config"
    document = ET.Element("configuration")
    sources = ET.SubElement(document, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="release", value=str(feed))
    ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
    ET.ElementTree(document).write(config, encoding="utf-8", xml_declaration=True)
    common = ["-c", "Release", "--nologo", "-p:ComfyCopyToPlugins=false",
              "-p:RepositoryCommit=" + revision, "-p:ContinuousIntegrationBuild=true",
              "-p:RestoreConfigFile=" + str(config), "-p:RestorePackagesPath=" + str(cache)]
    def build(label: str, command: list[str]) -> None:
        print(label, flush=True)
        with (out / (label + ".log")).open("w", encoding="utf-8") as log:
            result = subprocess.run([args.dotnet, *command, *common], cwd=ROOT,
                                    stdout=log, stderr=subprocess.STDOUT)
        if result.returncode:
            raise RuntimeError(label + "_failed; see release build log")
    build("contracts", ["pack", str(contracts), "-o", str(feed)])
    build("runtime", ["build", "network/mod/ComfyQuestRuntime/ComfyQuestRuntime.csproj"])
    build("lab", ["build", "network/mod/ComfyQuestLab/ComfyQuestLab.csproj"])
    build("studio-package", ["pack", str(studio), "-o", str(feed)])
    build("studio-linux", ["publish", "src/Quest.Studio.Host/Quest.Studio.Host.csproj",
                           "-r", "linux-x64", "--self-contained", "true",
                           "-o", str(out / "studio")])
    packages = []
    for name, kind in (("Contracts", "contracts"), ("Studio", "studio")):
        package = feed / f"Comfy.Quest.{name}.{version}.nupkg"
        validate_package(package, kind, version, expected_commit=revision)
        packages.append({"path": package.relative_to(out).as_posix(),
                         "sha256": sha(package), "bytes": package.stat().st_size})
    plugins = out / "plugins"
    plugins.mkdir()
    entries = []
    for name in ("ComfyQuestContracts.dll", "ComfyQuestRuntime.dll", "ComfyQuestLab.dll",
                 "Newtonsoft.Json.dll"):
        project = "ComfyQuestLab" if name == "ComfyQuestLab.dll" else "ComfyQuestRuntime"
        directory = ROOT / "network/mod" / project / "bin/Release"
        candidates = list(directory.rglob(name))
        if len(candidates) != 1:
            raise RuntimeError("plugin_output_not_unique:" + name)
        target = plugins / name
        shutil.copy2(candidates[0], target)
        entries.append({"name": name, "path": target.relative_to(out).as_posix(),
                        "sha256": sha(target), "bytes": target.stat().st_size})
    probe = out / "probe"
    probe.mkdir()
    for name in ("architectural_live_probe.py", "creator_dm_live_probe.py"):
        shutil.copy2(ROOT / "tools/quest-studio" / name, probe / name)
    host = out / "studio/Comfy.Quest.Studio.Host"
    if not host.is_file():
        raise RuntimeError("linux_host_missing")
    # Windows cannot retain Unix executable bits in its publish directory.
    archive = out / "studio-linux-x64.tar.gz"
    with tarfile.open(archive, "w:gz") as tar:
        for path in sorted((out / "studio").rglob("*")):
            if not path.is_file():
                continue
            info = tar.gettarinfo(str(path), arcname=path.relative_to(out / "studio").as_posix())
            info.uid = info.gid = 0
            info.uname = info.gname = ""
            info.mode = 0o755 if path == host else 0o644
            with path.open("rb") as stream:
                tar.addfile(info, stream)
    manifest = {"schema": "comfy-quest-creator-dm-release/v1", "source_revision": revision,
                "version": version, "plugins": entries, "packages": packages,
                "studio": {"path": archive.name, "sha256": sha(archive),
                           "bytes": archive.stat().st_size},
                "proof_level": "release-artifacts; installed live proof is recorded separately"}
    (out / "release.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"source_revision": revision, "version": version,
                      "manifest": str(out / "release.json")}), flush=True)


if __name__ == "__main__":
    main()

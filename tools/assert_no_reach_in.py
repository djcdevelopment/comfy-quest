#!/usr/bin/env python3
"""Fail when executable or configuration files reach into another checkout."""

from __future__ import annotations

import argparse
import re
import tempfile
from pathlib import Path


PYTHON_EXECUTABLE_NAMES = {
    "render_quest_lab.py",
    "generate_seam_catalog.py",
    "generate_gallery.py",
    "generate_journal.py",
    "check_lab_patches.py",
    "generate_grimoire.py",
    "assert_no_reach_in.py",
}
TEXT_SUFFIXES = {
    ".csproj", ".props", ".targets", ".ps1", ".psm1", ".yml", ".yaml", ".json"
}
SKIP_PARTS = {".git", "bin", "obj", "dist", "packages-local", "__pycache__"}
ABSOLUTE_WORK_ROOT = re.compile(r"C:" + r"[\\/]" + "work" + r"[\\/]", re.IGNORECASE)
BASELINE_SOURCE_URL = re.compile(
    "github.com/djcdevelopment/" + "baseline" + r"/(?:blob|tree)/", re.IGNORECASE
)
RELATIVE_PATH = re.compile(
    r"(?<![A-Za-z0-9_.-])(?:\.\.[\\/]){2,}"
    r"[A-Za-z0-9_.() -]+(?:[\\/][A-Za-z0-9_.() -]+)*"
)


def is_inside(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def scan(root: Path) -> list[str]:
    root = root.resolve()
    findings: list[str] = []
    for path in sorted(root.rglob("*")):
        if not path.is_file() or (
            path.suffix.lower() not in TEXT_SUFFIXES
            and path.name not in PYTHON_EXECUTABLE_NAMES
        ):
            continue
        relative = path.relative_to(root)
        if any(part in SKIP_PARTS for part in relative.parts):
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        for line_number, line in enumerate(text.splitlines(), 1):
            if ABSOLUTE_WORK_ROOT.search(line):
                findings.append(f"{relative}:{line_number}: absolute work-root path")
            if BASELINE_SOURCE_URL.search(line):
                findings.append(f"{relative}:{line_number}: baseline source URL")
            for match in RELATIVE_PATH.finditer(line):
                token = match.group(0).rstrip(".,;:'\"<>)")
                resolved = (path.parent / token.replace("\\", "/")).resolve()
                if not is_inside(resolved, root):
                    findings.append(
                        f"{relative}:{line_number}: parent traversal exits repository"
                    )
    return findings


def self_test() -> int:
    with tempfile.TemporaryDirectory(prefix="comfy-quest-reach-in-") as temporary:
        root = Path(temporary)
        bad_value = "C:" + "\\" + "work" + "\\" + "foreign" + "\\" + "tool.ps1"
        (root / "bad.ps1").write_text(f"$p = '{bad_value}'\n", encoding="utf-8")
        if not scan(root):
            print("FAIL: injected reach-in was not detected")
            return 1
        print("PASS: injected reach-in was rejected")
        return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--root", type=Path, default=Path(__file__).resolve().parents[1]
    )
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    findings = scan(args.root)
    if findings:
        print("\n".join(findings))
        return 1
    print("PASS: no cross-repository reach-ins")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

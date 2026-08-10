#!/usr/bin/env python3
"""Read-only Quest Lab compatibility doctor and privacy-minimal support capsule."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


SCHEMA = "comfy-questlab-doctor/v1"
EXPECTED_COUNTS = {
    "AtlasRows": 91,
    "UniqueSignatures": 90,
    "UniqueMethods": 77,
    "CreatorSafeEvents": 34,
    "CreatorSafeSignatures": 57,
}
STARTUP_RE = re.compile(
    r"quest lab (?P<version>\d+\.\d+\.\d+) \((?P<release>[^)]+)\) .*?"
    r"(?P<hooked>\d+)/(?P<attempted>\d+) seams hooked"
)


class DoctorError(RuntimeError):
    pass


def default_repo() -> Path:
    return Path(__file__).resolve().parents[2]


def default_valheim() -> Path:
    return Path(r"C:\Program Files (x86)\Steam\steamapps\common\Valheim")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def json_file(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as exc:
        raise DoctorError(f"unreadable JSON: {exc}") from exc
    if not isinstance(value, dict):
        raise DoctorError("JSON root is not an object")
    return value


def check(name: str, status: str, detail: str, remedy: str = "") -> dict[str, str]:
    return {"name": name, "status": status, "detail": detail, "remedy": remedy}


def source_identity(repo: Path) -> tuple[str, str]:
    source = (repo / "network" / "mod" / "ComfyQuestLab" / "ComfyQuestLab.cs").read_text(
        encoding="utf-8-sig"
    )
    version = re.search(r'PluginVersion\s*=\s*"([^"]+)"', source)
    release = re.search(r'ReleaseId\s*=\s*"([^"]+)"', source)
    if not version or not release:
        raise DoctorError("source plugin identity is missing")
    return version.group(1), release.group(1)


def capability_check(repo: Path) -> tuple[dict[str, Any], dict[str, str]]:
    path = repo / "tools" / "component-packets" / "samples" / "quest-capability-manifest.json"
    manifest = json_file(path)
    counts = manifest.get("Counts")
    events = manifest.get("CreatorSafeEvents")
    problems = []
    if manifest.get("Schema") != "comfy-quest-capabilities/v1":
        problems.append("schema is not v1")
    if not isinstance(counts, dict):
        problems.append("Counts is absent")
        counts = {}
    for name, expected in EXPECTED_COUNTS.items():
        if counts.get(name) != expected:
            problems.append(f"{name}={counts.get(name)!r}, expected {expected}")
    if not isinstance(events, list) or len(events) != EXPECTED_COUNTS["CreatorSafeEvents"]:
        problems.append("creator event array is not exactly 34 entries")
    status = "fail" if problems else "pass"
    detail = "; ".join(problems) if problems else "91 rows / 90 signatures / 34 safe events agree"
    return manifest, check(
        "capability-catalog",
        status,
        detail,
        "Regenerate the atlas/capability artifacts and fix drift before releasing." if problems else "",
    )


def quest_health(quest_dir: Path, safe_events: set[str]) -> tuple[dict[str, Any], dict[str, str]]:
    files = sorted(quest_dir.glob("*.json")) if quest_dir.is_dir() else []
    errors: list[dict[str, Any]] = []
    quests = 0
    armed = 0
    for index, path in enumerate(files, 1):
        try:
            root = json_file(path)
            if root.get("schema_version") != 1 or not isinstance(root.get("quests"), list):
                raise DoctorError("schema_version/quests shape is not schema 1")
            for q_index, quest in enumerate(root["quests"]):
                quests += 1
                if not isinstance(quest, dict):
                    raise DoctorError(f"quest[{q_index}] is not an object")
                if not all(isinstance(quest.get(field), str) and quest[field].strip() for field in ("quest_id", "name", "guild")):
                    raise DoctorError(f"quest[{q_index}] is missing quest_id, name, or guild")
                trigger = quest.get("trigger")
                if trigger is None:
                    continue
                if not isinstance(trigger, dict) or not isinstance(trigger.get("event"), str):
                    raise DoctorError(f"quest[{q_index}].trigger has no event")
                if trigger["event"] not in safe_events and trigger["event"] != "hit":
                    raise DoctorError(f"quest[{q_index}] uses unsupported event {trigger['event']!r}")
                armed += 1
        except DoctorError as exc:
            # File numbers, not creator filenames, keep an exported capsule useful without
            # leaking a private quest title or guild naming convention.
            errors.append({"file_index": index, "error": str(exc)})
    result = {"files": len(files), "quests": quests, "armed": armed, "errors": errors}
    return result, check(
        "quest-files",
        "fail" if errors else ("pass" if files else "warn"),
        f"{len(files)} file(s), {quests} quest(s), {armed} supported trigger(s), {len(errors)} error(s)",
        "Open Quest Lab's Quests tab for the exact per-file parser error." if errors else "",
    )


def tree_ledgers(directory: Path) -> tuple[dict[str, Any], dict[str, str]]:
    files = sorted(directory.glob("*.json")) if directory.is_dir() else []
    summaries = []
    unreadable = 0
    pending = 0
    records = 0
    for path in files:
        item: dict[str, Any] = {"sha256": sha256(path)}
        try:
            ledger = json_file(path)
            trees = ledger.get("Trees")
            expected = ledger.get("RecordCount") or ledger.get("RemovedCount") or 0
            actual = len(trees) if isinstance(trees, list) else -1
            complete = (
                ledger.get("Schema") == "comfy-questlab-tree-recovery/v1"
                and isinstance(expected, int)
                and expected == actual
            )
            item.update(
                {
                    "plugin_release": ledger.get("PluginRelease"),
                    "profile": ledger.get("ProfileId"),
                    "restored": ledger.get("Restored") is True,
                    "expected_records": expected,
                    "records_read": actual,
                    "structurally_complete": complete,
                }
            )
            if not complete:
                unreadable += 1
            elif not item["restored"]:
                pending += 1
                records += actual
        except DoctorError as exc:
            unreadable += 1
            item.update({"structurally_complete": False, "error": str(exc)})
        summaries.append(item)
    status = "fail" if unreadable else ("warn" if pending else "pass")
    remedy = "Do not clear or rebuild until live identify reads every expected record." if unreadable else ""
    return {
        "ledgers": summaries,
        "pending": pending,
        "pending_records": records,
        "unreadable": unreadable,
    }, check(
        "tree-recovery",
        status,
        f"{len(files)} ledger(s), {pending} pending / {records} tree(s), {unreadable} structurally unreadable",
        remedy,
    )


def latest_receipts(receipt_root: Path) -> dict[str, Any]:
    result: dict[str, Any] = {}
    suite_dir = receipt_root / "suites"
    for suite in ("creator-events", "all-schools"):
        paths = sorted(suite_dir.glob(f"{suite}-*.json"), key=lambda path: path.stat().st_mtime)
        if not paths:
            continue
        path = paths[-1]
        try:
            receipt = json_file(path)
            result[suite] = {
                "sha256": sha256(path),
                "release_id": receipt.get("release_id"),
                "state": receipt.get("state"),
                "verdict": receipt.get("verdict"),
                "witnessed_events": receipt.get("witnessed_events"),
                "required_events": receipt.get("required_events"),
                "double_completions": receipt.get("double_completions"),
            }
        except DoctorError as exc:
            result[suite] = {"error": str(exc), "sha256": sha256(path)}
    request_dir = receipt_root / "requests"
    identify = sorted(request_dir.glob("gallery-identify-*.json"), key=lambda path: path.stat().st_mtime)
    if identify:
        path = identify[-1]
        try:
            receipt = json_file(path)
            result["gallery-identify"] = {
                "sha256": sha256(path),
                "release_id": receipt.get("release_id"),
                "state": receipt.get("state"),
                "completed_utc": receipt.get("completed_utc"),
                "detail": receipt.get("detail"),
            }
        except DoctorError as exc:
            result["gallery-identify"] = {"error": str(exc), "sha256": sha256(path)}
    return result


def collect(repo: Path, valheim: Path) -> dict[str, Any]:
    repo = repo.resolve()
    valheim = valheim.resolve()
    checks: list[dict[str, str]] = []
    version, release = source_identity(repo)
    capabilities, capability_result = capability_check(repo)
    checks.append(capability_result)
    manifest_version = json_file(repo / "network" / "mod" / "ComfyQuestLab" / "manifest.json").get("version_number")
    checks.append(
        check(
            "source-identity",
            "pass" if manifest_version == version else "fail",
            f"plugin {version}, release {release}, manifest {manifest_version}",
            "Make source and package manifest versions agree." if manifest_version != version else "",
        )
    )
    built = repo / "network" / "mod" / "ComfyQuestLab" / "bin" / "Release" / "ComfyQuestLab.dll"
    installed = valheim / "BepInEx" / "plugins" / "ComfyQuestLab.dll"
    built_hash = sha256(built) if built.is_file() else None
    installed_hash = sha256(installed) if installed.is_file() else None
    if not installed_hash:
        checks.append(check("installed-dll", "fail", "ComfyQuestLab.dll is not installed", "Install the verified release DLL."))
    elif not built_hash:
        checks.append(check("installed-dll", "warn", f"installed SHA-256 {installed_hash}; no local build to compare"))
    else:
        checks.append(
            check(
                "installed-dll",
                "pass" if built_hash == installed_hash else "fail",
                f"installed {installed_hash}; built {built_hash}",
                "Close Valheim and deploy the exact verified DLL." if built_hash != installed_hash else "",
            )
        )
    log_path = valheim / "BepInEx" / "LogOutput.log"
    last_live = None
    if log_path.is_file():
        text = log_path.read_text(encoding="utf-8", errors="replace")
        matches = list(STARTUP_RE.finditer(text))
        if matches:
            match = matches[-1]
            last_live = match.groupdict()
    if last_live is None:
        checks.append(check("last-live-identity", "warn", "no Quest Lab startup identity found"))
    else:
        matches_source = last_live["version"] == version and last_live["release"] == release
        all_hooked = last_live["hooked"] == last_live["attempted"]
        status = "pass" if matches_source and all_hooked else "warn"
        detail = (
            f"live {last_live['release']}, seams {last_live['hooked']}/{last_live['attempted']}; "
            f"source {release}"
        )
        remedy = "Start the exact installed build and collect a fresh identity/receipt." if not matches_source else ""
        checks.append(check("last-live-identity", status, detail, remedy))
    config_root = valheim / "BepInEx" / "config" / "comfy-quest-lab"
    safe_events = set(capabilities.get("CreatorSafeEvents", []))
    quest_summary, quest_result = quest_health(config_root / "quests", safe_events)
    checks.append(quest_result)
    ledger_summary, ledger_result = tree_ledgers(config_root / "tree-recovery")
    checks.append(ledger_result)
    receipts = latest_receipts(config_root / "receipts")
    for suite, required in (("creator-events", 34), ("all-schools", 8)):
        receipt = receipts.get(suite)
        passed = bool(
            receipt
            and receipt.get("release_id") == release
            and receipt.get("state") == "complete"
            and receipt.get("verdict") == "pass"
            and receipt.get("witnessed_events") == required
            and receipt.get("double_completions") == 0
        )
        checks.append(
            check(
                f"exact-release-{suite}",
                "pass" if passed else "warn",
                "exact-release passing receipt present" if passed else "exact-release passing receipt not found",
                f"Run and export the bounded {suite} suite against {release}." if not passed else "",
            )
        )
    verdict = "fail" if any(item["status"] == "fail" for item in checks) else (
        "warn" if any(item["status"] == "warn" for item in checks) else "pass"
    )
    return {
        "schema": SCHEMA,
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "verdict": verdict,
        "source": {"plugin_version": version, "release_id": release},
        "dll": {"built_sha256": built_hash, "installed_sha256": installed_hash},
        "last_live": last_live,
        "capabilities": capabilities.get("Counts", {}),
        "quests": quest_summary,
        "tree_recovery": ledger_summary,
        "receipts": receipts,
        "checks": checks,
        "privacy": {
            "raw_logs_included": False,
            "quest_contents_included": False,
            "player_names_included": False,
            "absolute_paths_included": False,
        },
    }


def write_bundle(path: Path, report: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    report_bytes = (json.dumps(report, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")
    readme = (
        "Quest Lab support capsule\n\n"
        "Contains the read-only doctor report only. Raw logs, quest contents, player names, "
        "and absolute paths are deliberately excluded.\n"
    ).encode("utf-8")
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("questlab-doctor.json", report_bytes)
        archive.writestr("README.txt", readme)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=default_repo())
    parser.add_argument("--valheim-root", type=Path, default=default_valheim())
    parser.add_argument("--output", type=Path)
    parser.add_argument("--support-bundle", type=Path)
    args = parser.parse_args()
    try:
        report = collect(args.repo, args.valheim_root)
        encoded = json.dumps(report, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(encoded, encoding="utf-8")
        if args.support_bundle:
            write_bundle(args.support_bundle, report)
        print(encoded, end="")
        return 1 if report["verdict"] == "fail" else 0
    except (DoctorError, OSError) as exc:
        print(f"questlab-doctor: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())

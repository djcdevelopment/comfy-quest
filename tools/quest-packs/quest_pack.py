#!/usr/bin/env python3
"""Build, verify, preview, install, and uninstall local-first Quest Lab packs.

The pack is deliberately data-only. It may carry schema-1 quest views, bounded scenario
recipes, PlanBuild blueprints, prose, screenshots, and receipts; it can never carry code.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import uuid
import zipfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import Any, Iterable


PACK_SCHEMA = "comfy-quest-pack/v1"
SOURCE_SCHEMA = "comfy-quest-pack-source/v1"
INSTALL_SCHEMA = "comfy-quest-pack-install/v1"
SUITE_SCHEMA = "comfy-questlab-suite-receipt/v1"
ACCEPTANCE_SCHEMA = "comfy-questlab-gallery-acceptance/v1"
DOCTOR_SCHEMA = "comfy-questlab-doctor/v1"
PACING_SCHEMA = "comfy-questlab-pacing-report/v1"
CERTIFICATION_SCHEMA = "comfy-quest-pack-certification/v1"
CONTRACT_REQUEST_SCHEMA = "comfy-quest-pack-contract-request/v1"
CONTRACT_RESULT_SCHEMA = "comfy-quest-pack-contract-validation/v1"
SOURCE_FILE = "quest-pack.source.json"
MANIFEST_FILE = "quest-pack.json"
GENERATED_GUIDE = "docs/QUEST-PACK-GETTING-STARTED.md"
MAX_FILES = 512
MAX_FILE_BYTES = 16 * 1024 * 1024
MAX_TOTAL_BYTES = 64 * 1024 * 1024
MAX_GENERATED_GUIDE_BYTES = 64 * 1024
FIXED_ZIP_TIME = (2020, 1, 1, 0, 0, 0)
PACK_ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{1,63}$")
VERSION_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$")
KINDS: dict[str, set[str]] = {
    "quests": {".json"},
    "scenarios": {".json"},
    "blueprints": {".blueprint"},
    "docs": {".md", ".txt"},
    "screenshots": {".png", ".jpg", ".jpeg", ".webp"},
    "receipts": {".json"},
}
LIVE_EXPECTATIONS = {
    "combat": "kill",
    "harvest": "resource_damaged",
    "inventory": "item_picked_up",
    "building": "piece_placed",
    "crafting": "station_fuel_added",
    "progression": "skill_raised",
    "world": "player_teleported",
    "social": "sign_written",
}
VISUAL_CHECKS = {
    "solid_marble_floor",
    "marble_floor_not_snow_coated",
    "scale_acceptable",
    "hall_width_acceptable",
    "roof_canopy_acceptable",
    "hanging_braziers_acceptable",
    "natural_tree_clearance_acceptable",
    "runes_acceptable",
    "rune_banners_acceptable",
    "sign_lighting_acceptable",
    "welcome_camp_acceptable",
    "quest_grid_readable",
}


class PackError(RuntimeError):
    pass


def repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def default_capability_manifest() -> Path:
    return repo_root() / "tools" / "component-packets" / "samples" / "quest-capability-manifest.json"


def contract_project() -> Path:
    return Path(__file__).resolve().parent / "QuestPackContract" / "QuestPackContract.csproj"


def canonical_json(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_json_bytes(value: bytes, label: str) -> dict[str, Any]:
    try:
        parsed = json.loads(value.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PackError(f"{label}: invalid JSON: {exc}") from exc
    if not isinstance(parsed, dict):
        raise PackError(f"{label}: root must be an object")
    return parsed


def read_json_file(path: Path) -> dict[str, Any]:
    try:
        return read_json_bytes(path.read_bytes(), str(path))
    except OSError as exc:
        raise PackError(f"{path}: cannot read: {exc}") from exc


def safe_member(name: str) -> PurePosixPath:
    if not name or "\\" in name:
        raise PackError(f"unsafe pack path: {name!r}")
    path = PurePosixPath(name)
    if path.is_absolute() or any(part in ("", ".", "..") for part in path.parts):
        raise PackError(f"unsafe pack path: {name!r}")
    if ":" in path.parts[0]:
        raise PackError(f"unsafe pack path: {name!r}")
    return path


def validate_identity(metadata: dict[str, Any]) -> tuple[str, str]:
    if metadata.get("schema") != SOURCE_SCHEMA:
        raise PackError(f"{SOURCE_FILE}: schema must be {SOURCE_SCHEMA}")
    pack_id = str(metadata.get("pack_id", ""))
    version = str(metadata.get("version", ""))
    if not PACK_ID_RE.fullmatch(pack_id):
        raise PackError("pack_id must be 2-64 lowercase letters, numbers, dots, dashes, or underscores")
    if not VERSION_RE.fullmatch(version):
        raise PackError("version must be 1-64 filename-safe characters")
    for field in ("name", "creator", "license"):
        if not isinstance(metadata.get(field), str) or not metadata[field].strip():
            raise PackError(f"{SOURCE_FILE}: {field} must be non-empty text")
    if "description" in metadata and not isinstance(metadata["description"], str):
        raise PackError(f"{SOURCE_FILE}: description must be text")
    return pack_id, version


def load_catalog(path: Path) -> tuple[dict[str, Any], set[str], dict[str, list[str]], str]:
    manifest = read_json_file(path)
    events = manifest.get("CreatorSafeEvents")
    if not isinstance(events, list) or not events or not all(isinstance(item, str) for item in events):
        raise PackError(f"{path}: CreatorSafeEvents must be a non-empty string array")
    aliases = manifest.get("TriggerAliases", {})
    if not isinstance(aliases, dict):
        raise PackError(f"{path}: TriggerAliases must be an object")
    normalized_aliases: dict[str, list[str]] = {}
    safe = set(events)
    for alias, targets in aliases.items():
        if (
            not isinstance(alias, str)
            or not alias
            or not isinstance(targets, list)
            or not targets
            or not all(isinstance(target, str) and target in safe for target in targets)
        ):
            raise PackError(f"{path}: TriggerAliases contains an invalid mapping")
        normalized_aliases[alias] = sorted(set(targets))
    return manifest, safe, normalized_aliases, sha256_file(path)


def load_safe_events(path: Path) -> tuple[set[str], str]:
    _, events, _, digest = load_catalog(path)
    return events, digest


def validate_quest_view(
    data: bytes,
    label: str,
    safe_events: set[str],
) -> tuple[list[str], set[str]]:
    root = read_json_bytes(data, label)
    if root.get("schema_version") != 1:
        raise PackError(f"{label}: schema_version must be 1")
    quests = root.get("quests")
    if not isinstance(quests, list) or not quests:
        raise PackError(f"{label}: quests must be a non-empty array")
    ids: list[str] = []
    required_events: set[str] = set()
    for index, quest in enumerate(quests):
        if not isinstance(quest, dict):
            raise PackError(f"{label}: quest[{index}] must be an object")
        for field in ("quest_id", "name", "guild"):
            if not isinstance(quest.get(field), str) or not quest[field].strip():
                raise PackError(f"{label}: quest[{index}].{field} must be non-empty text")
        ids.append(quest["quest_id"])
        trigger = quest.get("trigger")
        if trigger is None:
            continue
        if not isinstance(trigger, dict):
            raise PackError(f"{label}: quest[{index}].trigger must be null or an object")
        event = trigger.get("event")
        if not isinstance(event, str) or not event.strip():
            raise PackError(f"{label}: quest[{index}].trigger.event must be non-empty text")
        if event not in safe_events and event != "hit":
            raise PackError(f"{label}: quest[{index}] uses unsupported creator event {event!r}")
        required_events.add(event)
        where = trigger.get("where")
        if where is not None:
            if not isinstance(where, dict):
                raise PackError(f"{label}: quest[{index}].trigger.where must be an object")
            for key, value in where.items():
                if not isinstance(key, str) or not key.strip():
                    raise PackError(f"{label}: trigger.where field names must be non-empty text")
                if value is None or isinstance(value, (dict, list)) or not isinstance(value, (str, int, float, bool)):
                    raise PackError(f"{label}: trigger.where.{key} must be a scalar")
    duplicates = sorted({quest_id for quest_id in ids if ids.count(quest_id) > 1})
    if duplicates:
        raise PackError(f"{label}: duplicate quest_id values: {', '.join(duplicates)}")
    return ids, required_events


def is_int(value: Any, minimum: int = 0) -> bool:
    return isinstance(value, int) and not isinstance(value, bool) and value >= minimum


def suite_common_errors(
    receipt: dict[str, Any], suite: str, evidence_kind: str, required: int
) -> list[str]:
    errors: list[str] = []
    expected = {
        "schema": SUITE_SCHEMA,
        "suite": suite,
        "evidence_kind": evidence_kind,
        "state": "complete",
        "verdict": "pass",
        "required_events": required,
        "witnessed_events": required,
        "completed_example_quests": required,
        "double_completions": 0,
    }
    for field, value in expected.items():
        if receipt.get(field) != value:
            errors.append("suite." + field)
    for field in ("plugin_version", "release_id", "machine", "started_utc", "finished_utc"):
        if not isinstance(receipt.get(field), str) or not receipt[field].strip():
            errors.append("suite." + field)
    expectations = receipt.get("expectations")
    witnesses = receipt.get("witnesses")
    if not isinstance(witnesses, list):
        errors.append("suite.witnesses")
    if not isinstance(expectations, list) or len(expectations) != required:
        errors.append("suite.expectations")
        return sorted(set(errors))
    for item in expectations:
        if not isinstance(item, dict):
            errors.append("suite.expectation_shape")
            continue
        if item.get("witnessed") is not True:
            errors.append("suite.expectation_witnessed")
        if item.get("quest_completed") is not True:
            errors.append("suite.expectation_completed")
        if not is_int(item.get("canonical_action_count"), 1):
            errors.append("suite.expectation_actions")
        for field in ("first_signature", "first_action_key", "first_witness_utc", "first_completion_utc"):
            if not isinstance(item.get(field), str) or not item[field]:
                errors.append("suite.expectation_" + field)
    return sorted(set(errors))


def creator_receipt_errors(receipt: dict[str, Any], safe_events: set[str]) -> list[str]:
    errors = suite_common_errors(receipt, "creator-events", "synthetic-contract", len(safe_events))
    expectations = receipt.get("expectations")
    observed = {
        item.get("event")
        for item in (expectations if isinstance(expectations, list) else [])
        if isinstance(item, dict)
    }
    if observed != safe_events:
        errors.append("creator.exact_catalog_coverage")
    witnesses = receipt.get("witnesses")
    if not isinstance(witnesses, list) or len(witnesses) != len(safe_events):
        errors.append("creator.witnesses")
    else:
        for witness in witnesses:
            if not isinstance(witness, dict):
                errors.append("creator.witness_shape")
            elif witness.get("evaluated") is not True or witness.get("source") != "synthetic-contract":
                errors.append("creator.exact_evaluator")
    return sorted(set(errors))


def live_receipt_errors(receipt: dict[str, Any]) -> list[str]:
    errors = suite_common_errors(receipt, "all-schools", "live-gameplay", len(LIVE_EXPECTATIONS))
    expectations = receipt.get("expectations")
    observed = {
        item.get("school"): item.get("event")
        for item in (expectations if isinstance(expectations, list) else [])
        if isinstance(item, dict)
    }
    if observed != LIVE_EXPECTATIONS:
        errors.append("live.exact_school_matrix")
    if not str(receipt.get("runtime_profile", "")).startswith("extended"):
        errors.append("live.extended_profile")
    raw = receipt.get("raw_witnesses")
    canonical = receipt.get("canonical_actions")
    coalesced = receipt.get("coalesced_witnesses")
    if not is_int(raw) or not is_int(canonical) or raw < canonical:
        errors.append("live.witness_accounting")
    if not is_int(coalesced, 1):
        errors.append("live.coalescing")
    return sorted(set(errors))


def public_evidence_record(
    path: str,
    data: bytes,
    kind: str,
    status: str,
    receipt: dict[str, Any] | None,
    findings: list[str],
    witnessed_events: list[str] | None = None,
    extra: dict[str, Any] | None = None,
) -> dict[str, Any]:
    record: dict[str, Any] = {
        "path": path,
        "sha256": sha256_bytes(data),
        "kind": kind,
        "status": status,
        "findings": sorted(set(findings)),
    }
    if receipt is not None:
        version = receipt.get("plugin_version")
        release = receipt.get("release_id")
        if isinstance(version, str) and version:
            record["plugin_version"] = version
        if isinstance(release, str) and release:
            record["release_id"] = release
    if witnessed_events is not None:
        record["witnessed_events"] = sorted(set(witnessed_events))
    if extra:
        record.update(extra)
    return record


def analyze_evidence(data: bytes, path: str, safe_events: set[str]) -> dict[str, Any]:
    try:
        receipt = read_json_bytes(data, path)
    except PackError:
        return public_evidence_record(
            path, data, "unreadable-json", "rejected", None, ["evidence.invalid_json"]
        )
    schema = receipt.get("schema")
    if schema == SUITE_SCHEMA and receipt.get("suite") == "creator-events":
        errors = creator_receipt_errors(receipt, safe_events)
        witnessed = [
            item.get("event")
            for item in receipt.get("expectations", [])
            if isinstance(item, dict) and isinstance(item.get("event"), str)
        ]
        return public_evidence_record(
            path,
            data,
            "creator-events-synthetic-contract",
            "accepted" if not errors else "rejected",
            receipt,
            errors,
            witnessed,
        )
    if schema == SUITE_SCHEMA and receipt.get("suite") == "all-schools":
        errors = live_receipt_errors(receipt)
        witnessed = [
            item.get("event")
            for item in receipt.get("expectations", [])
            if isinstance(item, dict) and isinstance(item.get("event"), str)
        ]
        return public_evidence_record(
            path,
            data,
            "all-schools-live-gameplay",
            "accepted" if not errors else "rejected",
            receipt,
            errors,
            witnessed,
            {"dedupe_verified": not errors and receipt.get("double_completions") == 0},
        )
    if schema == DOCTOR_SCHEMA:
        checks = receipt.get("checks")
        privacy = receipt.get("privacy")
        errors = []
        if receipt.get("verdict") != "pass":
            errors.append("doctor.verdict")
        if not isinstance(checks, list) or not checks or any(
            not isinstance(item, dict) or item.get("status") != "pass" for item in checks
        ):
            errors.append("doctor.checks")
        required_checks = {
            "capability-catalog",
            "source-identity",
            "installed-dll",
            "last-live-identity",
            "quest-files",
            "tree-recovery",
            "exact-release-creator-events",
            "exact-release-all-schools",
        }
        observed_checks = {
            item.get("name")
            for item in (checks if isinstance(checks, list) else [])
            if isinstance(item, dict)
        }
        if not required_checks.issubset(observed_checks):
            errors.append("doctor.required_checks")
        capabilities = receipt.get("capabilities")
        if (
            not isinstance(capabilities, dict)
            or capabilities.get("AtlasRows") != 91
            or capabilities.get("UniqueSignatures") != 90
            or capabilities.get("CreatorSafeEvents") != 34
        ):
            errors.append("doctor.capabilities")
        if not isinstance(privacy, dict) or any(
            privacy.get(field) is not False
            for field in (
                "raw_logs_included",
                "quest_contents_included",
                "player_names_included",
                "absolute_paths_included",
            )
        ):
            errors.append("doctor.privacy")
        source = receipt.get("source") if isinstance(receipt.get("source"), dict) else {}
        safe_receipt = {
            "plugin_version": source.get("plugin_version"),
            "release_id": source.get("release_id"),
        }
        return public_evidence_record(
            path,
            data,
            "compatibility-doctor",
            "accepted" if not errors else "rejected",
            safe_receipt,
            errors,
        )
    if schema == PACING_SCHEMA:
        privacy = receipt.get("privacy")
        aggregate = receipt.get("aggregate")
        recommendations = aggregate.get("recommendations") if isinstance(aggregate, dict) else None
        errors = []
        if not is_int(receipt.get("runs_analyzed"), 1):
            errors.append("pacing.runs")
        pacing_privacy = {"player_identity", "targets", "positions", "raw_action_keys"}
        if (
            not isinstance(privacy, dict)
            or set(privacy) != pacing_privacy
            or any(value is not False for value in privacy.values())
        ):
            errors.append("pacing.privacy")
        if not isinstance(recommendations, list):
            errors.append("pacing.recommendations")
            recommendations = []
        releases = sorted(
            {
                item.get("release_id")
                for item in receipt.get("runs", [])
                if isinstance(item, dict) and isinstance(item.get("release_id"), str)
            }
        )
        return public_evidence_record(
            path,
            data,
            "pacing-clinic",
            "accepted" if not errors else "rejected",
            None,
            errors,
            extra={"runs": receipt.get("runs_analyzed"), "recommendations": len(recommendations), "release_ids": releases},
        )
    if schema == ACCEPTANCE_SCHEMA:
        observations = receipt.get("observations")
        errors = []
        if (
            not isinstance(observations, dict)
            or set(observations) != VISUAL_CHECKS
            or not all(value is True for value in observations.values())
        ):
            errors.append("gallery.observations")
        if not isinstance(receipt.get("accepted_by"), str) or not receipt["accepted_by"].strip():
            errors.append("gallery.human_acceptance")
        return public_evidence_record(
            path,
            data,
            "gallery-human-acceptance",
            "accepted" if not errors else "rejected",
            None,
            errors,
        )
    return public_evidence_record(
        path, data, "unsupported-evidence", "not-certifying", receipt, ["evidence.schema_unsupported"]
    )


def collect_source(root: Path) -> list[tuple[str, str, bytes]]:
    collected: list[tuple[str, str, bytes]] = []
    total = 0
    for kind, extensions in KINDS.items():
        directory = root / kind
        if not directory.exists():
            continue
        if directory.is_symlink() or not directory.is_dir():
            raise PackError(f"{directory}: must be a real directory, not a link")
        for path in sorted(directory.rglob("*"), key=lambda item: item.as_posix().lower()):
            if path.is_symlink():
                raise PackError(f"{path}: links and special files are not allowed")
            if path.is_dir():
                continue
            if not path.is_file():
                raise PackError(f"{path}: links and special files are not allowed")
            if path.suffix.lower() not in extensions:
                raise PackError(f"{path}: extension is not allowed in {kind}/")
            relative = path.relative_to(root).as_posix()
            safe_member(relative)
            data = path.read_bytes()
            if len(data) > MAX_FILE_BYTES:
                raise PackError(f"{relative}: exceeds the {MAX_FILE_BYTES}-byte per-file limit")
            total += len(data)
            if total > MAX_TOTAL_BYTES - MAX_GENERATED_GUIDE_BYTES:
                raise PackError(
                    f"pack source exceeds the {MAX_TOTAL_BYTES - MAX_GENERATED_GUIDE_BYTES}-byte payload limit"
                )
            collected.append((kind, relative, data))
            if len(collected) > MAX_FILES - 1:
                raise PackError(f"pack source exceeds the {MAX_FILES - 1}-file limit")
    if not any(kind == "quests" for kind, _, _ in collected):
        raise PackError("a quest pack must contain at least one quests/*.json file")
    return collected


def zip_write(archive: zipfile.ZipFile, name: str, data: bytes) -> None:
    info = zipfile.ZipInfo(name, FIXED_ZIP_TIME)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o100644 << 16
    archive.writestr(info, data, compresslevel=9)


def contract_source_files() -> list[dict[str, str]]:
    root = repo_root()
    paths = [
        root / "network/mod/ComfyNetworkSense/Core/Models/TrackedQuest.cs",
        root / "network/mod/ComfyNetworkSense/Core/Models/QuestEvent.cs",
        root / "network/mod/ComfyNetworkSense/Core/Services/QuestViewLoader.cs",
        root / "network/mod/ComfyNetworkSense/Core/Services/QuestTriggerEvaluator.cs",
        root / "network/mod/ComfyNetworkSense/Core/Services/QuestEventCatalog.g.cs",
        root / "network/mod/ComfyQuestLab/Core/LabQuestSet.cs",
    ]
    return [
        {"path": path.relative_to(root).as_posix(), "sha256": sha256_file(path)}
        for path in paths
    ]


def run_exact_contract(quest_payload: list[tuple[str, bytes]]) -> dict[str, Any]:
    request_files = []
    for path, data in quest_payload:
        try:
            text = data.decode("utf-8-sig")
        except UnicodeDecodeError as exc:
            raise PackError(f"{path}: quest JSON is not UTF-8") from exc
        request_files.append({"path": path, "json": text})
    request = {"schema": CONTRACT_REQUEST_SCHEMA, "files": request_files}
    command = [
        "dotnet",
        "run",
        "--project",
        str(contract_project()),
        "--configuration",
        "Release",
        "--no-launch-profile",
    ]
    try:
        completed = subprocess.run(
            command,
            cwd=repo_root(),
            input=canonical_json(request),
            capture_output=True,
            timeout=120,
            check=False,
        )
    except FileNotFoundError as exc:
        raise PackError(
            "exact certification requires the .NET 8 SDK; install it or use build for an explicitly uncertified pack"
        ) from exc
    except subprocess.TimeoutExpired as exc:
        raise PackError("exact loader/evaluator certification timed out") from exc
    try:
        result = read_json_bytes(completed.stdout, "QuestPackContract output")
    except PackError as exc:
        detail = completed.stderr.decode("utf-8", errors="replace").strip().splitlines()
        suffix = (": " + detail[-1]) if detail else ""
        raise PackError("exact loader/evaluator contract could not run" + suffix) from exc
    if result.get("schema") != CONTRACT_RESULT_SCHEMA:
        raise PackError("exact loader/evaluator returned an unsupported result schema")
    if completed.returncode not in (0, 2):
        raise PackError("exact loader/evaluator contract failed before producing a verdict")
    sources = contract_source_files()
    result["source_files"] = sources
    result["contract_sha256"] = sha256_bytes(canonical_json({"result": result, "sources": sources}))
    return result


def badge(
    badge_id: str,
    scope: str,
    basis: str,
    evidence: list[dict[str, Any]] | None = None,
) -> dict[str, Any]:
    return {
        "id": badge_id,
        "scope": scope,
        "basis": basis,
        "evidence": sorted(
            evidence or [], key=lambda item: (str(item.get("path", "")), str(item.get("sha256", "")))
        ),
    }


def evidence_ref(record: dict[str, Any]) -> dict[str, str]:
    return {"path": record["path"], "sha256": record["sha256"]}


def make_certification(
    metadata: dict[str, Any],
    quest_ids: list[str],
    trigger_events: set[str],
    catalog_sha: str,
    safe_events: set[str],
    aliases: dict[str, list[str]],
    evidence_records: list[dict[str, Any]],
    contract: dict[str, Any] | None,
) -> dict[str, Any]:
    accepted_creator = [
        item
        for item in evidence_records
        if item["kind"] == "creator-events-synthetic-contract" and item["status"] == "accepted"
    ]
    accepted_live = [
        item
        for item in evidence_records
        if item["kind"] == "all-schools-live-gameplay" and item["status"] == "accepted"
    ]
    contract_witnessed = {
        event for item in accepted_creator for event in item.get("witnessed_events", [])
    }
    live_witnessed = {
        event for item in accepted_live for event in item.get("witnessed_events", [])
    }
    coverage = []
    for trigger in sorted(trigger_events):
        canonical = aliases.get(trigger, [trigger])
        coverage.append(
            {
                "trigger": trigger,
                "accepted_canonical_events": canonical,
                "synthetic_contract_witnessed": sorted(set(canonical) & contract_witnessed),
                "live_gameplay_witnessed": sorted(set(canonical) & live_witnessed),
            }
        )

    badges = [badge("current-catalog-compatible", "pack", "static-validation")]
    findings: list[dict[str, Any]] = []
    if contract is None:
        findings.append(
            {
                "code": "contract.not_run",
                "severity": "info",
                "detail": "This pack was built without the shipping loader/evaluator certification pass.",
                "remedy": "Use the publish command to earn exact-contract badges.",
            }
        )
        verdict = "uncertified"
        publishable = False
    else:
        loader_ok = not contract.get("errors")
        evaluator_ok = loader_ok and contract.get("unsupported_quests") == 0
        if loader_ok:
            badges.append(
                badge(
                    "shipping-loader-validated",
                    "pack",
                    "exact-contract",
                    [{"contract_sha256": contract.get("contract_sha256")}],
                )
            )
        if evaluator_ok:
            badges.append(
                badge(
                    "shipping-evaluator-bindable",
                    "pack",
                    "exact-contract",
                    [{"contract_sha256": contract.get("contract_sha256")}],
                )
            )
        if not loader_ok:
            findings.append(
                {
                    "code": "contract.loader_rejected",
                    "severity": "error",
                    "detail": "At least one quest file failed the shipping schema-1 loader.",
                    "remedy": "Fix the contract errors in the report and certify again.",
                }
            )
        elif not evaluator_ok:
            findings.append(
                {
                    "code": "contract.unbindable_trigger",
                    "severity": "error",
                    "detail": "At least one in-game trigger did not bind through the shipping evaluator.",
                    "remedy": "Use a creator-safe event/filter shape and certify again.",
                }
            )
        verdict = "pass" if loader_ok and evaluator_ok else "fail"
        publishable = verdict == "pass"

    if coverage and all(item["synthetic_contract_witnessed"] for item in coverage):
        badges.append(
            badge(
                "all-pack-triggers-contract-witnessed",
                "pack",
                "synthetic-contract",
                [evidence_ref(item) for item in accepted_creator],
            )
        )
    elif coverage:
        missing = [item["trigger"] for item in coverage if not item["synthetic_contract_witnessed"]]
        findings.append(
            {
                "code": "evidence.contract_missing",
                "severity": "info",
                "detail": "No accepted synthetic-contract evidence covers: " + ", ".join(missing),
                "remedy": "Include a passing exact creator-events suite receipt from the tested release.",
            }
        )
    if coverage and all(item["live_gameplay_witnessed"] for item in coverage):
        badges.append(
            badge(
                "all-pack-triggers-live-witnessed",
                "pack",
                "live-gameplay",
                [evidence_ref(item) for item in accepted_live],
            )
        )
    elif coverage:
        missing = [item["trigger"] for item in coverage if not item["live_gameplay_witnessed"]]
        findings.append(
            {
                "code": "evidence.live_missing",
                "severity": "info",
                "detail": "No accepted live-gameplay evidence covers: " + ", ".join(missing),
                "remedy": "Run the bounded live suite or keep the pack honestly contract-only.",
            }
        )
    if accepted_live:
        badges.append(
            badge(
                "all-schools-live-witnessed",
                "runtime",
                "live-gameplay",
                [evidence_ref(item) for item in accepted_live],
            )
        )
    dedupe = [item for item in accepted_live if item.get("dedupe_verified") is True]
    if dedupe:
        badges.append(
            badge(
                "same-action-dedupe-live-verified",
                "runtime",
                "live-gameplay",
                [evidence_ref(item) for item in dedupe],
            )
        )
    doctors = [
        item
        for item in evidence_records
        if item["kind"] == "compatibility-doctor" and item["status"] == "accepted"
    ]
    if doctors:
        badges.append(
            badge(
                "runtime-compatibility-doctor-passed",
                "runtime",
                "read-only-doctor",
                [evidence_ref(item) for item in doctors],
            )
        )
    galleries = [
        item
        for item in evidence_records
        if item["kind"] == "gallery-human-acceptance" and item["status"] == "accepted"
    ]
    if galleries:
        badges.append(
            badge(
                "gallery-human-accepted",
                "gallery",
                "human-observation",
                [evidence_ref(item) for item in galleries],
            )
        )
    for item in evidence_records:
        if item["status"] == "rejected":
            findings.append(
                {
                    "code": "evidence.rejected",
                    "severity": "warning",
                    "evidence": item["path"],
                    "detail": "Included evidence did not satisfy its exact schema and claim checks.",
                    "remedy": "Inspect the evidence findings, rerun that bounded check, and replace the receipt.",
                }
            )
        if item["kind"] == "pacing-clinic" and item["status"] == "accepted" and item.get("recommendations", 0):
            findings.append(
                {
                    "code": "pacing.recommendations_present",
                    "severity": "info",
                    "evidence": item["path"],
                    "detail": f"The included Pacing Clinic report has {item['recommendations']} recommendation(s).",
                    "remedy": "Review the recommendations before presenting this pack as a polished course.",
                }
            )

    evidence_level = (
        "live-gameplay"
        if accepted_live
        else (
            "synthetic-contract"
            if accepted_creator
            else ("exact-loader-evaluator" if contract and contract.get("verdict") == "pass" else "static")
        )
    )
    report = {
        "schema": CERTIFICATION_SCHEMA,
        "verdict": verdict,
        "publishable": publishable,
        "evidence_level": evidence_level,
        "pack": {
            "pack_id": metadata["pack_id"],
            "version": metadata["version"],
            "quest_ids": sorted(quest_ids),
            "quest_count": len(quest_ids),
            "source_metadata_sha256": sha256_bytes(canonical_json(metadata)),
        },
        "catalog": {
            "schema": "comfy-quest-capabilities/v1",
            "sha256": catalog_sha,
            "creator_safe_events": len(safe_events),
        },
        "coverage": coverage,
        "contract": contract or {"schema": CONTRACT_RESULT_SCHEMA, "verdict": "not-run"},
        "badges": sorted(badges, key=lambda item: item["id"]),
        "evidence": sorted(evidence_records, key=lambda item: item["path"]),
        "findings": sorted(findings, key=lambda item: (item["severity"], item["code"], item.get("evidence", ""))),
        "privacy": {
            "absolute_paths": False,
            "machine_names": False,
            "player_names": False,
            "raw_action_keys": False,
            "raw_receipt_contents": False,
            "targets_or_positions": False,
            "relative_pack_paths": True,
        },
    }
    report["report_sha256"] = sha256_bytes(canonical_json(report))
    return report


def getting_started(metadata: dict[str, Any], certification: dict[str, Any]) -> bytes:
    badge_ids = [item["id"] for item in certification.get("badges", [])]
    badges = "\n".join("- `" + item + "`" for item in badge_ids) or "- None; this pack is explicitly uncertified."
    required = [item["trigger"] for item in certification.get("coverage", [])]
    events = ", ".join("`" + item + "`" for item in required) or "none (manual quests only)"
    description = str(metadata.get("description", "")).strip()
    description_block = description + "\n\n" if description else ""
    text = f"""# {metadata['name'].strip()}

{description_block}Pack `{metadata['pack_id']}` version `{metadata['version']}` by {metadata['creator'].strip()}.
License: {metadata['license'].strip()}.

This is a data-only Quest Lab pack. It contains no executable code and needs no Derek-specific
service, account, or filesystem layout.

## Check before installing

Use the pack tool from a Quest Lab checkout. `diagnose` compares the pack with the current
34-event catalog; the install preview verifies hashes and reports every destination without
changing it.

```powershell
python tools/quest-packs/quest_pack.py inspect PACK.questpack --report PACK.questpack.certification.json
python tools/quest-packs/quest_pack.py diagnose PACK.questpack
python tools/quest-packs/quest_pack.py install PACK.questpack --quest-dir PATH_TO_QUESTS --dry-run
```

Only when the preview says `\"ready\": true`:

```powershell
python tools/quest-packs/quest_pack.py install PACK.questpack --quest-dir PATH_TO_QUESTS
```

Quest Lab can reload installed schema-1 quest files without changing the originals in this pack.

## Remove safely

```powershell
python tools/quest-packs/quest_pack.py uninstall {metadata['pack_id']} --version {metadata['version']} --quest-dir PATH_TO_QUESTS
```

Uninstall refuses if an installed quest or pack asset was edited, so creator work is never
silently deleted.

## Compatibility and evidence

- Quest schema: `1`
- Required trigger names: {events}
- Certification verdict: `{certification['verdict']}`
- Evidence level: `{certification['evidence_level']}`

Badges:

{badges}

Badges describe only the included, hash-matched evidence. In particular, live-gameplay and
same-action-dedupe badges are absent unless an exact passing live receipt is bundled. The public
certification sidecar omits machine/player names, absolute paths, raw action keys, targets, and
raw receipt contents.
"""
    return text.encode("utf-8")


def build_pack(
    source: Path,
    output: Path,
    capability_manifest: Path,
    contract: dict[str, Any] | None = None,
) -> dict[str, Any]:
    source = source.resolve()
    metadata = read_json_file(source / SOURCE_FILE)
    pack_id, version = validate_identity(metadata)
    _, safe_events, aliases, catalog_sha = load_catalog(capability_manifest)
    payload = collect_source(source)
    all_quest_ids: list[str] = []
    required_events: set[str] = set()
    evidence_records: list[dict[str, Any]] = []
    files: list[dict[str, Any]] = []
    for kind, relative, data in payload:
        if kind == "quests":
            ids, events = validate_quest_view(data, relative, safe_events)
            all_quest_ids.extend(ids)
            required_events.update(events)
        elif kind == "receipts":
            evidence_records.append(analyze_evidence(data, relative, safe_events))
        files.append(
            {
                "path": relative,
                "kind": kind,
                "bytes": len(data),
                "sha256": sha256_bytes(data),
            }
        )
    folded = [quest_id.casefold() for quest_id in all_quest_ids]
    duplicates = sorted(
        {all_quest_ids[index] for index, key in enumerate(folded) if folded.count(key) > 1},
        key=str.casefold,
    )
    if duplicates:
        raise PackError("quest_id values must be unique across the pack: " + ", ".join(duplicates))
    certification = make_certification(
        metadata,
        all_quest_ids,
        required_events,
        catalog_sha,
        safe_events,
        aliases,
        evidence_records,
        contract,
    )
    if any(relative.casefold() == GENERATED_GUIDE.casefold() for _, relative, _ in payload):
        raise PackError(f"{GENERATED_GUIDE} is reserved for the generated handoff guide")
    guide = getting_started(metadata, certification)
    if len(guide) > MAX_GENERATED_GUIDE_BYTES:
        raise PackError("generated getting-started guide exceeds its reserved size")
    payload.append(("docs", GENERATED_GUIDE, guide))
    files.append(
        {
            "path": GENERATED_GUIDE,
            "kind": "docs",
            "bytes": len(guide),
            "sha256": sha256_bytes(guide),
        }
    )
    # Kept for schema-1 consumers that already display the original flat evidence list. Exact
    # contract/static badges live in certification.badges because they do not point at one payload
    # receipt. New consumers should read the full certification report.
    certifications = []
    for claim in certification["badges"]:
        for evidence in claim["evidence"]:
            if "path" in evidence:
                certifications.append(
                    {
                        "badge": claim["id"],
                        "evidence": evidence["path"],
                        "sha256": evidence["sha256"],
                    }
                )
    manifest = {
        "schema": PACK_SCHEMA,
        "pack_id": pack_id,
        "name": metadata["name"].strip(),
        "version": version,
        "creator": metadata["creator"].strip(),
        "license": metadata["license"].strip(),
        "description": metadata.get("description", "").strip(),
        "requirements": {
            "quest_schema": 1,
            "creator_events": sorted(required_events),
            "capability_manifest_sha256": catalog_sha,
        },
        "quest_ids": sorted(all_quest_ids),
        "certification": certification,
        "certifications": sorted(certifications, key=lambda item: (item["badge"], item["evidence"])),
        "files": files,
    }
    manifest_bytes = canonical_json(manifest)
    if len(manifest_bytes) > MAX_FILE_BYTES:
        raise PackError(f"generated {MANIFEST_FILE} exceeds the per-file limit")
    if len(manifest_bytes) + sum(len(data) for _, _, data in payload) > MAX_TOTAL_BYTES:
        raise PackError("generated pack exceeds the total uncompressed size limit")
    output = output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".tmp-" + uuid.uuid4().hex)
    try:
        with zipfile.ZipFile(temporary, "w") as archive:
            zip_write(archive, MANIFEST_FILE, manifest_bytes)
            for _, relative, data in payload:
                zip_write(archive, relative, data)
        os.replace(temporary, output)
    finally:
        if temporary.exists():
            temporary.unlink()
    return {
        "manifest": manifest,
        "package": str(output),
        "package_sha256": sha256_file(output),
        "certification": certification,
    }


def source_certification(source: Path, capability_manifest: Path) -> dict[str, Any]:
    source = source.resolve()
    metadata = read_json_file(source / SOURCE_FILE)
    validate_identity(metadata)
    _, safe_events, aliases, catalog_sha = load_catalog(capability_manifest)
    payload = collect_source(source)
    quest_ids: list[str] = []
    trigger_events: set[str] = set()
    quest_payload: list[tuple[str, bytes]] = []
    evidence_records: list[dict[str, Any]] = []
    for kind, relative, data in payload:
        if kind == "quests":
            ids, events = validate_quest_view(data, relative, safe_events)
            quest_ids.extend(ids)
            trigger_events.update(events)
            quest_payload.append((relative, data))
        elif kind == "receipts":
            evidence_records.append(analyze_evidence(data, relative, safe_events))
    folded = [quest_id.casefold() for quest_id in quest_ids]
    duplicates = sorted(
        {quest_ids[index] for index, key in enumerate(folded) if folded.count(key) > 1},
        key=str.casefold,
    )
    if duplicates:
        raise PackError("quest_id values must be unique across the pack: " + ", ".join(duplicates))
    contract = run_exact_contract(quest_payload)
    return make_certification(
        metadata,
        quest_ids,
        trigger_events,
        catalog_sha,
        safe_events,
        aliases,
        evidence_records,
        contract,
    )


def require_badges(report: dict[str, Any], required: Iterable[str]) -> None:
    required_set = {item for item in required if item}
    present = {item.get("id") for item in report.get("badges", []) if isinstance(item, dict)}
    missing = sorted(required_set - present)
    if missing:
        raise PackError(
            "required certification badge(s) not earned: "
            + ", ".join(missing)
            + "; inspect the certification findings for the smallest missing evidence"
        )


def write_json_atomic(path: Path, value: dict[str, Any]) -> None:
    path = path.resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp-" + uuid.uuid4().hex)
    try:
        temporary.write_bytes(canonical_json(value))
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()


def publish_pack(
    source: Path,
    output: Path,
    report_path: Path,
    capability_manifest: Path,
    required_badges: Iterable[str],
) -> dict[str, Any]:
    output = output.resolve()
    report_path = report_path.resolve()
    if output == report_path:
        raise PackError("package and certification report must use different paths")
    certification = source_certification(source, capability_manifest)
    if not certification.get("publishable"):
        raise PackError("shipping loader/evaluator certification failed; no package was written")
    require_badges(certification, required_badges)
    output.parent.mkdir(parents=True, exist_ok=True)
    report_path.parent.mkdir(parents=True, exist_ok=True)
    temporary_package = output.with_name(output.name + ".publishing-" + uuid.uuid4().hex)
    temporary_report = report_path.with_name(report_path.name + ".publishing-" + uuid.uuid4().hex)
    try:
        built = build_pack(source, temporary_package, capability_manifest, certification["contract"])
        if built["certification"] != certification:
            raise PackError("source changed during certification; no public artifact was written")
        public_report = dict(certification)
        public_report["artifact"] = {
            "filename": output.name,
            "sha256": built["package_sha256"],
            "bytes": temporary_package.stat().st_size,
        }
        public_report["published_report_sha256"] = sha256_bytes(canonical_json(public_report))
        temporary_report.write_bytes(canonical_json(public_report))
        os.replace(temporary_package, output)
        os.replace(temporary_report, report_path)
    finally:
        if temporary_package.exists():
            temporary_package.unlink()
        if temporary_report.exists():
            temporary_report.unlink()
    return {
        "schema": CERTIFICATION_SCHEMA,
        "verdict": "pass",
        "package": str(output),
        "package_sha256": built["package_sha256"],
        "report": str(report_path),
        "report_sha256": sha256_file(report_path),
        "badges": [item["id"] for item in certification["badges"]],
    }


def read_verified_pack(
    path: Path,
    capability_manifest: Path | None = None,
    allow_incompatible: bool = False,
) -> tuple[dict[str, Any], dict[str, bytes], list[str]]:
    path = path.resolve()
    warnings: list[str] = []
    try:
        with zipfile.ZipFile(path, "r") as archive:
            infos = archive.infolist()
            names = [info.filename for info in infos]
            if len(names) != len(set(names)):
                raise PackError("pack contains duplicate member names")
            if len(names) != len({name.casefold() for name in names}):
                raise PackError("pack contains case-colliding member names")
            if len(names) > MAX_FILES + 1:
                raise PackError(f"pack exceeds the {MAX_FILES}-file limit")
            for info in infos:
                safe_member(info.filename)
                if info.is_dir():
                    raise PackError(f"pack contains an unexpected directory entry: {info.filename}")
                file_type = (info.external_attr >> 16) & 0o170000
                if file_type not in (0, 0o100000):
                    raise PackError(f"pack contains a link or special member: {info.filename}")
                if info.file_size > MAX_FILE_BYTES:
                    raise PackError(f"{info.filename}: exceeds the per-file limit")
            if sum(info.file_size for info in infos) > MAX_TOTAL_BYTES:
                raise PackError("pack exceeds the total uncompressed size limit")
            if MANIFEST_FILE not in names:
                raise PackError(f"pack is missing {MANIFEST_FILE}")
            payload = {name: archive.read(name) for name in names if name != MANIFEST_FILE}
            manifest = read_json_bytes(archive.read(MANIFEST_FILE), MANIFEST_FILE)
    except (OSError, zipfile.BadZipFile, RuntimeError) as exc:
        if isinstance(exc, PackError):
            raise
        raise PackError(f"{path}: cannot read pack: {exc}") from exc
    if manifest.get("schema") != PACK_SCHEMA:
        raise PackError(f"pack schema must be {PACK_SCHEMA}")
    pack_id = str(manifest.get("pack_id", ""))
    version = str(manifest.get("version", ""))
    if not PACK_ID_RE.fullmatch(pack_id) or not VERSION_RE.fullmatch(version):
        raise PackError("pack identity is invalid")
    entries = manifest.get("files")
    if not isinstance(entries, list):
        raise PackError("manifest files must be an array")
    declared: dict[str, dict[str, Any]] = {}
    declared_folded: set[str] = set()
    for entry in entries:
        if not isinstance(entry, dict) or not isinstance(entry.get("path"), str):
            raise PackError("manifest contains a malformed file entry")
        name = entry["path"]
        safe_member(name)
        if name in declared:
            raise PackError(f"manifest repeats {name}")
        if name.casefold() in declared_folded:
            raise PackError(f"manifest contains case-colliding paths including {name}")
        declared_folded.add(name.casefold())
        kind = str(entry.get("kind", ""))
        if kind not in KINDS or not name.startswith(kind + "/"):
            raise PackError(f"{name}: kind/path mismatch")
        if PurePosixPath(name).suffix.lower() not in KINDS[kind]:
            raise PackError(f"{name}: extension is not allowed for {kind}")
        declared[name] = entry
    if set(declared) != set(payload):
        missing = sorted(set(declared) - set(payload))
        extra = sorted(set(payload) - set(declared))
        raise PackError(f"manifest/payload mismatch; missing={missing}, extra={extra}")
    for name, data in payload.items():
        entry = declared[name]
        if entry.get("bytes") != len(data) or entry.get("sha256") != sha256_bytes(data):
            raise PackError(f"{name}: size or SHA-256 mismatch")
    requirements = manifest.get("requirements")
    if not isinstance(requirements, dict) or requirements.get("quest_schema") != 1:
        raise PackError("pack requires an unsupported quest schema")
    catalog_path = capability_manifest or default_capability_manifest()
    _, safe_events, _, current_sha = load_catalog(catalog_path)
    required = requirements.get("creator_events")
    if not isinstance(required, list) or not all(isinstance(event, str) for event in required):
        raise PackError("requirements.creator_events must be a string array")
    unsupported = sorted(set(required) - safe_events - {"hit"})
    if unsupported:
        if allow_incompatible:
            warnings.append("current Quest Lab does not support events: " + ", ".join(unsupported))
        else:
            raise PackError("current Quest Lab does not support events: " + ", ".join(unsupported))
    expected_sha = requirements.get("capability_manifest_sha256")
    if expected_sha != current_sha:
        warnings.append("capability catalog hash changed; every required event remains available")

    actual_ids: list[str] = []
    actual_events: set[str] = set()
    for name, data in payload.items():
        if declared[name]["kind"] != "quests":
            continue
        ids, events = validate_quest_view(
            data, name, safe_events | (set(required) if allow_incompatible else set())
        )
        actual_ids.extend(ids)
        actual_events.update(events)
    folded_ids = [quest_id.casefold() for quest_id in actual_ids]
    if len(folded_ids) != len(set(folded_ids)):
        raise PackError("quest_id values must be unique across the pack")
    if sorted(actual_ids) != manifest.get("quest_ids"):
        raise PackError("manifest quest_ids do not match the quest payload")
    if sorted(actual_events) != required:
        raise PackError("manifest creator event requirements do not match the quest payload")
    evidence = manifest.get("certifications", [])
    if not isinstance(evidence, list):
        raise PackError("certifications must be an array")
    for certification in evidence:
        if not isinstance(certification, dict):
            raise PackError("certification entry must be an object")
        evidence_path = certification.get("evidence")
        if evidence_path not in payload or certification.get("sha256") != sha256_bytes(payload[evidence_path]):
            raise PackError("certification evidence is missing or does not match")
    report = manifest.get("certification")
    if report is None:
        warnings.append("legacy pack has no exact certification report; evidence badges are untrusted display metadata")
    else:
        if not isinstance(report, dict) or report.get("schema") != CERTIFICATION_SCHEMA:
            raise PackError("embedded certification report schema is invalid")
        report_copy = dict(report)
        report_digest = report_copy.pop("report_sha256", None)
        if report_digest != sha256_bytes(canonical_json(report_copy)):
            raise PackError("embedded certification report hash does not match")
        pack = report.get("pack")
        if (
            not isinstance(pack, dict)
            or pack.get("pack_id") != pack_id
            or pack.get("version") != version
            or pack.get("quest_ids") != sorted(actual_ids)
        ):
            raise PackError("embedded certification report names different pack contents")
        privacy = report.get("privacy")
        if not isinstance(privacy, dict) or any(
            privacy.get(field) is not False
            for field in (
                "absolute_paths",
                "machine_names",
                "player_names",
                "raw_action_keys",
                "raw_receipt_contents",
                "targets_or_positions",
            )
        ):
            raise PackError("embedded certification report is not public-safe")
        contract = report.get("contract")
        if isinstance(contract, dict) and contract.get("verdict") in ("pass", "fail"):
            contract_copy = dict(contract)
            contract_digest = contract_copy.pop("contract_sha256", None)
            sources = contract_copy.get("source_files")
            if contract_digest != sha256_bytes(canonical_json({"result": contract_copy, "sources": sources})):
                raise PackError("embedded exact-contract result hash does not match")
        report_catalog = report.get("catalog")
        if isinstance(report_catalog, dict) and report_catalog.get("sha256") == current_sha:
            current_evidence = sorted(
                [
                    analyze_evidence(data, name, safe_events)
                    for name, data in payload.items()
                    if declared[name]["kind"] == "receipts"
                ],
                key=lambda item: item["path"],
            )
            if report.get("evidence") != current_evidence:
                raise PackError("embedded certification evidence summary does not match payload receipts")
        expected_flat = []
        claims = report.get("badges")
        if not isinstance(claims, list):
            raise PackError("embedded certification badges must be an array")
        for claim in claims:
            if not isinstance(claim, dict) or not isinstance(claim.get("evidence"), list):
                raise PackError("embedded certification badge is malformed")
            for item in claim["evidence"]:
                if isinstance(item, dict) and "path" in item:
                    expected_flat.append(
                        {
                            "badge": claim.get("id"),
                            "evidence": item.get("path"),
                            "sha256": item.get("sha256"),
                        }
                    )
        expected_flat.sort(key=lambda item: (str(item["badge"]), str(item["evidence"])))
        actual_flat = sorted(
            evidence, key=lambda item: (str(item.get("badge")), str(item.get("evidence")))
        )
        if actual_flat != expected_flat:
            raise PackError("flat certification claims do not match the embedded report")
    return manifest, payload, warnings


def quest_target_name(pack_id: str, source: str, digest: str) -> str:
    stem = PurePosixPath(source).stem
    safe_stem = re.sub(r"[^A-Za-z0-9._-]+", "-", stem).strip("-._") or "quests"
    return f"{pack_id}--{safe_stem}-{digest[:8]}.json"


def install_plan(manifest: dict[str, Any], quest_dir: Path, pack_root: Path) -> dict[str, Any]:
    pack_id = manifest["pack_id"]
    version = manifest["version"]
    install_dir = (pack_root / pack_id / version).resolve()
    quest_dir = quest_dir.resolve()
    quest_files = []
    for entry in manifest["files"]:
        if entry["kind"] != "quests":
            continue
        target = quest_dir / quest_target_name(pack_id, entry["path"], entry["sha256"])
        quest_files.append(
            {
                "source": entry["path"],
                "target": str(target),
                "sha256": entry["sha256"],
                "conflict": target.exists(),
            }
        )
    return {
        "schema": INSTALL_SCHEMA,
        "operation": "install-preview",
        "pack_id": pack_id,
        "version": version,
        "install_dir": str(install_dir),
        "install_dir_conflict": install_dir.exists(),
        "quest_files": quest_files,
        "ready": not install_dir.exists() and not any(item["conflict"] for item in quest_files),
    }


def install_pack(
    package: Path,
    quest_dir: Path,
    pack_root: Path | None,
    capability_manifest: Path,
    dry_run: bool,
) -> dict[str, Any]:
    manifest, payload, warnings = read_verified_pack(package, capability_manifest)
    quest_dir = quest_dir.resolve()
    pack_root = (pack_root or (quest_dir.parent / "quest-packs")).resolve()
    plan = install_plan(manifest, quest_dir, pack_root)
    plan["warnings"] = warnings
    plan["package_sha256"] = sha256_file(package)
    if dry_run:
        return plan
    if not plan["ready"]:
        raise PackError("install preview found a conflict; no files were changed")
    quest_dir.mkdir(parents=True, exist_ok=True)
    pack_root.mkdir(parents=True, exist_ok=True)
    install_dir = Path(plan["install_dir"])
    staging = pack_root / (".staging-" + uuid.uuid4().hex)
    created_quests: list[Path] = []
    try:
        staging.mkdir()
        for name, data in payload.items():
            target = staging.joinpath(*PurePosixPath(name).parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        manifest_bytes = canonical_json(manifest)
        (staging / MANIFEST_FILE).write_bytes(manifest_bytes)
        for item in plan["quest_files"]:
            source_data = payload[item["source"]]
            target = Path(item["target"])
            # The preview is useful to a human, but the exclusive create is the actual
            # no-overwrite guarantee if another process races us after preflight.
            with target.open("xb") as stream:
                stream.write(source_data)
                stream.flush()
                os.fsync(stream.fileno())
            created_quests.append(target)
        payload_files = [
            {"source": name, "sha256": sha256_bytes(data)}
            for name, data in sorted(payload.items())
        ]
        receipt = {
            "schema": INSTALL_SCHEMA,
            "installed_utc": datetime.now(timezone.utc).isoformat(),
            "pack_id": manifest["pack_id"],
            "version": manifest["version"],
            "package_sha256": plan["package_sha256"],
            "manifest_sha256": sha256_bytes(manifest_bytes),
            "quest_dir": str(quest_dir),
            "quest_files": [
                {"source": item["source"], "target": item["target"], "sha256": item["sha256"]}
                for item in plan["quest_files"]
            ],
            "payload_files": payload_files,
        }
        (staging / "install-receipt.json").write_bytes(canonical_json(receipt))
        install_dir.parent.mkdir(parents=True, exist_ok=True)
        staging.rename(install_dir)
        return {**receipt, "install_dir": str(install_dir), "warnings": warnings}
    except Exception:
        for target in created_quests:
            if target.exists():
                target.unlink()
        if staging.exists():
            shutil.rmtree(staging)
        raise


def resolve_install(pack_root: Path, pack_id: str, version: str | None) -> Path:
    if not PACK_ID_RE.fullmatch(pack_id):
        raise PackError("invalid pack_id")
    pack_dir = (pack_root.resolve() / pack_id).resolve()
    if pack_dir.parent != pack_root.resolve():
        raise PackError("pack path escaped the install root")
    if version:
        if not VERSION_RE.fullmatch(version):
            raise PackError("invalid version")
        selected = pack_dir / version
        if not selected.is_dir():
            raise PackError(f"pack {pack_id} version {version} is not installed")
        return selected
    versions = sorted(path for path in pack_dir.iterdir() if path.is_dir()) if pack_dir.is_dir() else []
    if len(versions) != 1:
        raise PackError(f"pack {pack_id} has {len(versions)} installed versions; specify --version")
    return versions[0]


def uninstall_pack(pack_id: str, quest_dir: Path, pack_root: Path | None, version: str | None) -> dict[str, Any]:
    quest_dir = quest_dir.resolve()
    pack_root = (pack_root or (quest_dir.parent / "quest-packs")).resolve()
    install_dir = resolve_install(pack_root, pack_id, version)
    receipt = read_json_file(install_dir / "install-receipt.json")
    if receipt.get("schema") != INSTALL_SCHEMA or receipt.get("pack_id") != pack_id:
        raise PackError("install receipt does not belong to the requested pack")
    targets: list[tuple[Path, str]] = []
    for item in receipt.get("quest_files", []):
        if not isinstance(item, dict):
            raise PackError("install receipt has a malformed quest entry")
        target = Path(str(item.get("target", ""))).resolve()
        if target.parent != quest_dir:
            raise PackError("install receipt names a quest outside the selected quest directory")
        if target.exists() and sha256_file(target) != item.get("sha256"):
            raise PackError(f"refusing to remove modified quest: {target}")
        targets.append((target, str(item.get("sha256", ""))))
    manifest_path = install_dir / MANIFEST_FILE
    if not manifest_path.is_file() or sha256_file(manifest_path) != receipt.get("manifest_sha256"):
        raise PackError(f"refusing to remove modified pack manifest: {manifest_path}")
    for item in receipt.get("payload_files", []):
        if not isinstance(item, dict) or not isinstance(item.get("source"), str):
            raise PackError("install receipt has a malformed payload entry")
        member = safe_member(item["source"])
        target = install_dir.joinpath(*member.parts).resolve()
        if install_dir not in target.parents or not target.is_file():
            raise PackError(f"refusing to remove missing pack payload: {target}")
        if sha256_file(target) != item.get("sha256"):
            raise PackError(f"refusing to remove modified pack payload: {target}")
    removed = []
    for target, _ in targets:
        if target.exists():
            target.unlink()
            removed.append(str(target))
    shutil.rmtree(install_dir)
    pack_dir = install_dir.parent
    if pack_dir.is_dir() and not any(pack_dir.iterdir()):
        pack_dir.rmdir()
    return {
        "schema": INSTALL_SCHEMA,
        "operation": "uninstall",
        "pack_id": pack_id,
        "version": receipt.get("version"),
        "removed_quest_files": removed,
        "removed_install_dir": str(install_dir),
    }


def verify_public_report(
    report_path: Path, package_path: Path, embedded: dict[str, Any] | None
) -> dict[str, Any]:
    report = read_json_file(report_path)
    if report.get("schema") != CERTIFICATION_SCHEMA:
        raise PackError("public certification report schema is invalid")
    report_copy = dict(report)
    digest = report_copy.pop("published_report_sha256", None)
    if digest != sha256_bytes(canonical_json(report_copy)):
        raise PackError("public certification report hash does not match")
    artifact = report.get("artifact")
    if (
        not isinstance(artifact, dict)
        or artifact.get("filename") != package_path.name
        or artifact.get("sha256") != sha256_file(package_path)
        or artifact.get("bytes") != package_path.stat().st_size
    ):
        raise PackError("public certification report names a different package artifact")
    base = dict(report)
    base.pop("artifact", None)
    base.pop("published_report_sha256", None)
    if not isinstance(embedded, dict) or base != embedded:
        raise PackError("public certification report does not match the embedded report")
    return {
        "path": str(report_path.resolve()),
        "sha256": sha256_file(report_path),
        "verdict": "pass",
    }


def summary(
    path: Path, capability_manifest: Path | None, report_path: Path | None = None
) -> dict[str, Any]:
    manifest, _, warnings = read_verified_pack(path, capability_manifest)
    certification = manifest.get("certification")
    result = {
        "schema": PACK_SCHEMA,
        "package": str(path.resolve()),
        "package_sha256": sha256_file(path),
        "pack_id": manifest["pack_id"],
        "name": manifest.get("name"),
        "version": manifest["version"],
        "creator": manifest.get("creator"),
        "quest_ids": manifest.get("quest_ids", []),
        "required_events": manifest.get("requirements", {}).get("creator_events", []),
        "certifications": manifest.get("certifications", []),
        "certification": {
            "verdict": certification.get("verdict"),
            "publishable": certification.get("publishable"),
            "evidence_level": certification.get("evidence_level"),
            "report_sha256": certification.get("report_sha256"),
            "badges": [item.get("id") for item in certification.get("badges", [])],
            "findings": certification.get("findings", []),
        }
        if isinstance(certification, dict)
        else None,
        "files": len(manifest.get("files", [])),
        "warnings": warnings,
        "verdict": "pass",
    }
    if report_path is not None:
        result["public_report"] = verify_public_report(report_path, path, certification)
    return result


def diagnose(path: Path, capability_manifest: Path) -> dict[str, Any]:
    manifest, payload, warnings = read_verified_pack(
        path, capability_manifest, allow_incompatible=True
    )
    _, safe_events, _, current_sha = load_catalog(capability_manifest)
    required = manifest.get("requirements", {}).get("creator_events", [])
    embedded = manifest.get("certification")
    embedded_contract = embedded.get("contract") if isinstance(embedded, dict) else None
    evidence = sorted(
        [
            analyze_evidence(data, name, safe_events)
            for name, data in payload.items()
            if name.startswith("receipts/")
        ],
        key=lambda item: item["path"],
    )
    unsupported = sorted(set(required) - safe_events - {"hit"})
    built_sha = manifest.get("requirements", {}).get("capability_manifest_sha256")
    findings: list[dict[str, str]] = []
    for warning in warnings:
        findings.append(
            {
                "code": "compatibility.warning",
                "severity": "warning",
                "detail": warning,
                "remedy": "Review current event coverage and republish when moving to a new catalog.",
            }
        )
    if unsupported:
        findings.append(
            {
                "code": "compatibility.unsupported_events",
                "severity": "error",
                "detail": "Current Quest Lab does not support: " + ", ".join(unsupported),
                "remedy": "Replace those triggers with current creator-safe canonical events.",
            }
        )
    rejected = [item["path"] for item in evidence if item["status"] == "rejected"]
    if rejected:
        findings.append(
            {
                "code": "evidence.rejected",
                "severity": "warning",
                "detail": f"{len(rejected)} included evidence file(s) failed exact claim validation.",
                "remedy": "Rerun the named bounded suite and replace its receipt before republishing.",
            }
        )
    contract_sources_current = bool(
        isinstance(embedded_contract, dict)
        and embedded_contract.get("source_files") == contract_source_files()
    )
    if isinstance(embedded_contract, dict) and embedded_contract.get("verdict") in ("pass", "fail") and not contract_sources_current:
        findings.append(
            {
                "code": "compatibility.contract_sources_changed",
                "severity": "warning",
                "detail": "The shipping loader/evaluator sources changed after this pack was certified.",
                "remedy": "Republish the pack with the current Quest Lab contract before carrying exact-contract badges forward.",
            }
        )
    verdict = "fail" if unsupported else ("warn" if findings else "pass")
    return {
        "schema": "comfy-quest-pack-diagnosis/v1",
        "verdict": verdict,
        "package": {"filename": path.name, "sha256": sha256_file(path)},
        "pack": {
            "pack_id": manifest["pack_id"],
            "version": manifest["version"],
            "quest_count": len(manifest.get("quest_ids", [])),
            "required_events": required,
        },
        "compatibility": {
            "built_catalog_sha256": built_sha,
            "current_catalog_sha256": current_sha,
            "exact_catalog": built_sha == current_sha,
            "unsupported_events": unsupported,
            "exact_contract_report": isinstance(embedded_contract, dict)
            and embedded_contract.get("verdict") in ("pass", "fail"),
            "contract_sources_current": contract_sources_current,
        },
        "badges": [item.get("id") for item in embedded.get("badges", [])]
        if isinstance(embedded, dict)
        else [],
        "evidence": evidence,
        "findings": findings,
        "privacy": {
            "absolute_paths": False,
            "machine_names": False,
            "player_names": False,
            "raw_receipt_contents": False,
        },
    }


def emit(value: Any) -> None:
    print(json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False))


def parser() -> argparse.ArgumentParser:
    cli = argparse.ArgumentParser(description=__doc__)
    sub = cli.add_subparsers(dest="command", required=True)
    build = sub.add_parser("build", help="build a deterministic, explicitly uncertified .questpack")
    build.add_argument("source", type=Path)
    build.add_argument("--output", type=Path, required=True)
    build.add_argument("--capability-manifest", type=Path, default=default_capability_manifest())
    inspect = sub.add_parser("inspect", help="verify and describe a pack")
    inspect.add_argument("package", type=Path)
    inspect.add_argument("--capability-manifest", type=Path, default=default_capability_manifest())
    inspect.add_argument("--report", type=Path, help="also verify the published public sidecar")
    certify = sub.add_parser("certify", help="run the shipping loader/evaluator and emit a public-safe report")
    certify.add_argument("source", type=Path)
    certify.add_argument("--output", type=Path)
    certify.add_argument("--capability-manifest", type=Path, default=default_capability_manifest())
    certify.add_argument("--require-badge", action="append", default=[])
    publish = sub.add_parser("publish", help="certify and reproducibly package a creator release")
    publish.add_argument("source", type=Path)
    publish.add_argument("--output", type=Path, required=True)
    publish.add_argument("--report", type=Path)
    publish.add_argument("--capability-manifest", type=Path, default=default_capability_manifest())
    publish.add_argument("--require-badge", action="append", default=[])
    diagnose_parser = sub.add_parser("diagnose", help="explain current compatibility without installing")
    diagnose_parser.add_argument("package", type=Path)
    diagnose_parser.add_argument("--capability-manifest", type=Path, default=default_capability_manifest())
    install = sub.add_parser("install", help="preview or install without overwriting files")
    install.add_argument("package", type=Path)
    install.add_argument("--quest-dir", type=Path, required=True)
    install.add_argument("--pack-root", type=Path)
    install.add_argument("--capability-manifest", type=Path, default=default_capability_manifest())
    install.add_argument("--dry-run", action="store_true")
    uninstall = sub.add_parser("uninstall", help="remove an unchanged installed pack")
    uninstall.add_argument("pack_id")
    uninstall.add_argument("--quest-dir", type=Path, required=True)
    uninstall.add_argument("--pack-root", type=Path)
    uninstall.add_argument("--version")
    return cli


def main(argv: Iterable[str] | None = None) -> int:
    args = parser().parse_args(argv)
    try:
        if args.command == "build":
            emit(build_pack(args.source, args.output, args.capability_manifest))
        elif args.command == "inspect":
            emit(summary(args.package, args.capability_manifest, args.report))
        elif args.command == "certify":
            report = source_certification(args.source, args.capability_manifest)
            if args.output:
                write_json_atomic(args.output, report)
            emit(report)
            present = {item.get("id") for item in report.get("badges", []) if isinstance(item, dict)}
            missing = sorted(set(args.require_badge) - present)
            if report.get("verdict") != "pass" or missing:
                if missing:
                    print("quest-pack: required badge(s) not earned: " + ", ".join(missing), file=sys.stderr)
                return 2
        elif args.command == "publish":
            report_path = args.report or args.output.with_name(args.output.name + ".certification.json")
            emit(
                publish_pack(
                    args.source,
                    args.output,
                    report_path,
                    args.capability_manifest,
                    args.require_badge,
                )
            )
        elif args.command == "diagnose":
            emit(diagnose(args.package, args.capability_manifest))
        elif args.command == "install":
            emit(install_pack(args.package, args.quest_dir, args.pack_root, args.capability_manifest, args.dry_run))
        elif args.command == "uninstall":
            emit(uninstall_pack(args.pack_id, args.quest_dir, args.pack_root, args.version))
        return 0
    except PackError as exc:
        print(f"quest-pack: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

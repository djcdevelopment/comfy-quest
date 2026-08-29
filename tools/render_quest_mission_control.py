#!/usr/bin/env python3
"""Render the offline Quest Mission Control page from tracked status and proven sources."""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any


REPO = Path(__file__).resolve().parents[1]
SOURCE = REPO / "docs" / "quest-mission-control.json"
OUTPUT = REPO / "docs" / "quest-mission-control.html"
LEDGER = REPO / "docs" / "creator-requirements-ledger.json"
PHASES = REPO / "docs" / "creator-os-phases.json"
REQUIREMENTS = REPO / "docs" / "creator-portfolio-requirements.md"
SCHEMA = "comfy-quest-mission-control/v1"
LEDGER_SCHEMA = "comfy-quest-creator-requirements-ledger/v1"
SESSION_SCHEMA = "comfy-quest-mission-control-session/v2"
ALLOWED_PHASE_STATES = {"complete", "active", "planned"}
ALLOWED_LANE_STATES = {"complete", "active", "planned"}
ALLOWED_QUEUE_STATES = {"complete", "implemented", "ready", "human", "gated", "deferred"}
# badge() and the status-dot stylesheet only style these. An unlisted value renders an
# unstyled badge or a grey dot and says nothing about it (audit A6).
ALLOWED_MACHINE_STATES = {"ready", "online", "on-demand"}
ALLOWED_ENVIRONMENT_STATES = {"confirmed", "implemented", "automated", "attention"}
# A seat lap with no recorded freshness renders as an actionable sequence. Session 3's lap
# passed structural validation while describing controls ADR 0008 removed (audit B7), so
# freshness is state here, not an inference from shape.
ALLOWED_LAP_STATES = {"seat-ready", "stale"}
# The closed disposition set, and the companion fields each one has to carry. `evidence` is a
# list of repository paths; the rest are text. `parked` needs two: why it is parked, and the
# ruling that has not been made. Without the second it becomes a catch-all for future work,
# blocked work, abandoned work, and work that is merely not in this lane.
REQUIREMENT_DISPOSITIONS = {
    "active": ("lane", "lane_authority"),
    "parked": ("reason", "pending_ruling"),
    "deferred": ("phase", "lane_authority"),
    "met": ("evidence",),
}
# Where a lane recorded in the ledger came from. Never "the ledger": it is a recorder, not a
# scheduler. `queue-realization` reads the lane off a work item already scheduled there, and
# only `active` may use it -- a future lane can only come from the phase authority.
LANE_AUTHORITIES = {"phase-authority", "queue-realization"}
# A ledger entry with no lane carries none of these. That is what stops a parked requirement
# from drifting toward its apparently obvious destination.
LANELESS_DISPOSITIONS = {"parked", "met"}
REQUIREMENT_ID = re.compile(r"\b(?:FR|NFR)-[A-Z]+-\d+\b")
# The files that advertise the authority reading order, and the fences the block sits in.
# The previous handoff went stale while the entry path still pointed at it, so the pointers
# are pinned to the manifest rather than trusted to stay current on their own.
POINTER_FILES = (
    "README.md",
    "docs/PLAN.md",
    "docs/handoff-2026-08-24.md",
    "docs/creator-os.md",
)
POINTER_BEGIN = "<!-- reading-order:begin -->"
POINTER_END = "<!-- reading-order:end -->"
POINTER_ENTRY = re.compile(r"^>?\s*(\d+)\.\s+`([^`]+)`")
SOURCE_INTENT_POINTER_FILES = (
    "docs/PLAN.md",
    "docs/handoff-2026-08-24.md",
    "docs/five-intent-program-plan.md",
)
SOURCE_INTENT_BEGIN = "<!-- source-intents:begin -->"
SOURCE_INTENT_END = "<!-- source-intents:end -->"
JOURNEY_POINTER_FILES = ("docs/PLAN.md",)
JOURNEY_BEGIN = "<!-- 4a-journey:begin -->"
JOURNEY_END = "<!-- 4a-journey:end -->"
BASELINE_REPOSITORY = "djcdevelopment/baseline"
SOURCE_INTENT_PATHS = {
    "01": "docs/arch/01_arcane_sight_runtime_observability.md",
    "02": "docs/arch/02_quest_lab_apprenticeship_spellbook.md",
    "03": "docs/arch/03_studio_live_valheim_creator_loop.md",
    "04": "docs/arch/04_community_artifact_ecosystem.md",
    "05": "docs/arch/05_adaptive_event_semantics.md",
}
JOURNEY_STEP_IDS = (
    "publish-guild-pack",
    "locked-b-refusal",
    "run-a",
    "run-b",
    "return-a",
    "reset-a",
    "verify-b-unchanged",
    "rerun-a",
    "retention-boundary",
)
JOURNEY_EVIDENCE_IDS = (
    "artifact-identity",
    "experience-state",
    "run-lineage",
    "runtime-correlation",
    "retention-proof",
)
BOLD_REQUIREMENT_ID = re.compile(r"\*\*((?:FR|NFR)-[A-Z]+-\d+)\b")


class MissionControlError(RuntimeError):
    """A bounded status or source-integrity failure."""


def read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as exc:
        raise MissionControlError(f"cannot read {path.relative_to(REPO)}: {exc}") from exc


def load_manifest(path: Path = SOURCE, *, validate_projection_state: bool = True) -> dict[str, Any]:
    try:
        value = json.loads(read_text(path))
    except json.JSONDecodeError as exc:
        raise MissionControlError(f"invalid mission-control JSON: {exc}") from exc
    validate_manifest(value, validate_projection_state=validate_projection_state)
    return value


def require_text(value: Any, where: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise MissionControlError(f"{where} must be non-empty text")
    return value


def require_list(value: Any, where: str) -> list[Any]:
    if not isinstance(value, list) or not value:
        raise MissionControlError(f"{where} must be a non-empty list")
    return value


def require_sha256(value: Any, where: str) -> str:
    digest = require_text(value, where)
    if not re.fullmatch(r"[0-9a-f]{64}", digest):
        raise MissionControlError(f"{where} must be a lowercase SHA-256")
    return digest


def source_path(relative: str) -> Path:
    candidate = (REPO / require_text(relative, "source path")).resolve()
    try:
        candidate.relative_to(REPO.resolve())
    except ValueError as exc:
        raise MissionControlError(f"source escapes repository: {relative}") from exc
    if not candidate.is_file():
        raise MissionControlError(f"source does not exist: {relative}")
    return candidate


def validate_source_pin(item: dict[str, Any], where: str) -> None:
    path = source_path(require_text(item.get("source"), f"{where}.source"))
    marker = require_text(item.get("source_contains"), f"{where}.source_contains")
    if marker not in read_text(path):
        raise MissionControlError(f"{where} source pin is stale: {marker!r} not found in {path.relative_to(REPO)}")


def validate_source_intents(value: dict[str, Any]) -> dict[str, Any]:
    authority = value.get("source_intents")
    if not isinstance(authority, dict):
        raise MissionControlError("source_intents must be an object")
    repository = require_text(authority.get("repository"), "source_intents.repository")
    if repository != BASELINE_REPOSITORY:
        raise MissionControlError(
            f"source_intents.repository must be {BASELINE_REPOSITORY}; got {repository!r}"
        )
    revision = require_text(authority.get("revision"), "source_intents.revision")
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise MissionControlError("source_intents.revision must be a lowercase 40-character SHA")
    documents = require_list(authority.get("documents"), "source_intents.documents")
    ids: list[str] = []
    paths: set[str] = set()
    for index, document in enumerate(documents):
        where = f"source_intents.documents[{index}]"
        if not isinstance(document, dict):
            raise MissionControlError(f"{where} must be an object")
        intent_id = require_text(document.get("id"), f"{where}.id")
        ids.append(intent_id)
        require_text(document.get("title"), f"{where}.title")
        path = require_text(document.get("path"), f"{where}.path")
        expected_path = SOURCE_INTENT_PATHS.get(intent_id)
        if path != expected_path:
            raise MissionControlError(
                f"{where}.path must be {expected_path!r} for intent {intent_id}; got {path!r}"
            )
        if path in paths:
            raise MissionControlError(f"duplicate source-intent path: {path}")
        paths.add(path)
        byte_count = document.get("bytes")
        if isinstance(byte_count, bool) or not isinstance(byte_count, int) or byte_count <= 0:
            raise MissionControlError(f"{where}.bytes must be a positive integer")
        digest = require_text(document.get("sha256"), f"{where}.sha256")
        if not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise MissionControlError(f"{where}.sha256 must be a lowercase SHA-256")
    if tuple(ids) != tuple(SOURCE_INTENT_PATHS):
        raise MissionControlError(
            f"source_intents.documents must contain ordered ids {list(SOURCE_INTENT_PATHS)}; got {ids}"
        )
    return authority


def fenced_body(path: Path, begin: str, end: str) -> str:
    text = read_text(path)
    if text.count(begin) != 1 or text.count(end) != 1:
        raise MissionControlError(
            f"{path.relative_to(REPO)} must contain exactly one block fenced by {begin} and {end}"
        )
    body = text.split(begin, 1)[1].split(end, 1)[0]
    lines = []
    for line in body.splitlines():
        lines.append(re.sub(r"^>\s?", "", line).rstrip())
    return "\n".join(lines).strip()


def reading_order_projection(value: dict[str, Any]) -> str:
    return "\n".join(
        f"{item['position']}. `{item['source']}` — **{item['role']}.** {item['detail']}"
        for item in value["reading_order"]
    )


def source_intent_projection(value: dict[str, Any]) -> str:
    authority = value["source_intents"]
    repository = authority["repository"]
    revision = authority["revision"]
    commit_url = f"https://github.com/{repository}/commit/{revision}"
    lines = [
        f"Pinned source authority: [`{repository}@{revision}`]({commit_url}).",
        "",
        "| Intent | Immutable source | Bytes | SHA-256 |",
        "| --- | --- | ---: | --- |",
    ]
    for document in authority["documents"]:
        blob_url = f"https://github.com/{repository}/blob/{revision}/{document['path']}"
        lines.append(
            f"| {document['id']} | [{document['title']}]({blob_url}) | "
            f"{document['bytes']} | `{document['sha256']}` |"
        )
    return "\n".join(lines)


def validate_acceptance_journey(journey: Any) -> dict[str, Any]:
    if not isinstance(journey, dict):
        raise MissionControlError("4A.acceptance_journey must be an object")
    if require_text(journey.get("work_item"), "4A.acceptance_journey.work_item") != "queue.full-width-journey":
        raise MissionControlError("4A.acceptance_journey.work_item must be queue.full-width-journey")
    precondition = require_text(journey.get("precondition"), "4A.acceptance_journey.precondition")
    for marker in ("<Valheim>/BepInEx/", "NFR-SEAT-003"):
        if marker not in precondition:
            raise MissionControlError(f"4A.acceptance_journey.precondition must name {marker}")
    steps = require_list(journey.get("steps"), "4A.acceptance_journey.steps")
    step_ids: list[str] = []
    for index, step in enumerate(steps):
        where = f"4A.acceptance_journey.steps[{index}]"
        if not isinstance(step, dict):
            raise MissionControlError(f"{where} must be an object")
        step_ids.append(require_text(step.get("id"), f"{where}.id"))
        require_text(step.get("detail"), f"{where}.detail")
    if tuple(step_ids) != JOURNEY_STEP_IDS:
        raise MissionControlError(
            f"4A acceptance journey must be the ordered sequence {list(JOURNEY_STEP_IDS)}; got {step_ids}"
        )
    evidence = require_list(journey.get("evidence"), "4A.acceptance_journey.evidence")
    evidence_ids: list[str] = []
    for index, item in enumerate(evidence):
        where = f"4A.acceptance_journey.evidence[{index}]"
        if not isinstance(item, dict):
            raise MissionControlError(f"{where} must be an object")
        evidence_ids.append(require_text(item.get("id"), f"{where}.id"))
        require_text(item.get("detail"), f"{where}.detail")
    if tuple(evidence_ids) != JOURNEY_EVIDENCE_IDS:
        raise MissionControlError(
            f"4A acceptance evidence must be {list(JOURNEY_EVIDENCE_IDS)}; got {evidence_ids}"
        )
    return journey


def load_4a_journey(path: Path = PHASES) -> dict[str, Any]:
    try:
        value = json.loads(read_text(path))
    except json.JSONDecodeError as exc:
        raise MissionControlError(f"invalid lane vocabulary JSON: {exc}") from exc
    lanes = require_list(value.get("lanes"), "lane vocabulary lanes")
    lane = next((item for item in lanes if isinstance(item, dict) and item.get("id") == "4A"), None)
    if lane is None:
        raise MissionControlError("lane vocabulary has no 4A lane")
    return validate_acceptance_journey(lane.get("acceptance_journey"))


def journey_projection(journey: dict[str, Any]) -> str:
    lines = [f"**Precondition:** {journey['precondition']}", ""]
    lines.extend(
        f"{index}. **{step['id']}** — {step['detail']}"
        for index, step in enumerate(journey["steps"], start=1)
    )
    lines.extend(["", "**Required correlated evidence:**"])
    lines.extend(f"- **{item['id']}** — {item['detail']}" for item in journey["evidence"])
    return "\n".join(lines)


def projection_specs(value: dict[str, Any]) -> dict[str, list[tuple[str, str, str]]]:
    projections: dict[str, list[tuple[str, str, str]]] = {}
    reading = reading_order_projection(value)
    for relative in POINTER_FILES:
        projections.setdefault(relative, []).append((POINTER_BEGIN, POINTER_END, reading))
    source = source_intent_projection(value)
    for relative in SOURCE_INTENT_POINTER_FILES:
        projections.setdefault(relative, []).append((SOURCE_INTENT_BEGIN, SOURCE_INTENT_END, source))
    journey = journey_projection(load_4a_journey())
    for relative in JOURNEY_POINTER_FILES:
        projections.setdefault(relative, []).append((JOURNEY_BEGIN, JOURNEY_END, journey))
    return projections


def tracked_markdown_with(marker: str) -> set[str]:
    result = subprocess.run(
        ["git", "ls-files", "-z", "--", "*.md"],
        cwd=REPO,
        check=True,
        capture_output=True,
    )
    found: set[str] = set()
    for raw in result.stdout.split(b"\0"):
        if not raw:
            continue
        relative = raw.decode("utf-8")
        if marker in read_text(REPO / relative):
            found.add(Path(relative).as_posix())
    return found


def validate_projection_registration() -> None:
    registrations = (
        (POINTER_BEGIN, set(POINTER_FILES)),
        (SOURCE_INTENT_BEGIN, set(SOURCE_INTENT_POINTER_FILES)),
        (JOURNEY_BEGIN, set(JOURNEY_POINTER_FILES)),
    )
    for marker, expected in registrations:
        actual = tracked_markdown_with(marker)
        if actual != expected:
            raise MissionControlError(
                f"projection marker {marker} is registered for {sorted(expected)} but appears in {sorted(actual)}"
            )


def validate_projections(value: dict[str, Any]) -> None:
    validate_projection_registration()
    for relative, specs in projection_specs(value).items():
        path = source_path(relative)
        for begin, end, expected in specs:
            actual = fenced_body(path, begin, end)
            if actual != expected:
                raise MissionControlError(
                    f"{relative} contains a stale projection fenced by {begin}; run {Path(__file__).name}"
                )


def replace_fenced_projection(text: str, begin: str, end: str, body: str) -> str:
    if text.count(begin) != 1 or text.count(end) != 1:
        raise MissionControlError(f"projection must contain exactly one {begin} and {end}")
    begin_at = text.index(begin)
    end_at = text.index(end, begin_at + len(begin))
    line_start = text.rfind("\n", 0, begin_at) + 1
    prefix = text[line_start:begin_at]
    rendered_body = "\n".join(f"{prefix}{line}" if line else prefix.rstrip() for line in body.splitlines())
    return (
        text[: begin_at + len(begin)]
        + "\n"
        + rendered_body
        + "\n"
        + prefix
        + text[end_at:]
    )


def write_projections(value: dict[str, Any]) -> None:
    updates: list[tuple[Path, str]] = []
    for relative, specs in projection_specs(value).items():
        path = source_path(relative)
        updated = read_text(path)
        for begin, end, body in specs:
            updated = replace_fenced_projection(updated, begin, end, body)
        updates.append((path, updated))
    # Resolve and render every projection before touching the first file. A stale or missing
    # fence therefore cannot leave a partially rewritten authority chain.
    for path, updated in updates:
        path.write_text(updated, encoding="utf-8", newline="\n")


def pointer_block(path: Path) -> list[tuple[int, str]]:
    """The ordered (position, source) pairs a pointer file advertises."""
    text = read_text(path)
    if text.count(POINTER_BEGIN) != 1 or text.count(POINTER_END) != 1:
        raise MissionControlError(
            f"{path.relative_to(REPO)} must contain exactly one reading-order block fenced by "
            f"{POINTER_BEGIN} and {POINTER_END}"
        )
    body = text.split(POINTER_BEGIN, 1)[1].split(POINTER_END, 1)[0]
    entries: list[tuple[int, str]] = []
    for line in body.splitlines():
        match = POINTER_ENTRY.match(line.strip())
        if match:
            entries.append((int(match.group(1)), match.group(2)))
    return entries


def validate_reading_order(value: dict[str, Any], *, validate_pointer_state: bool = True) -> None:
    """One declared authority chain, and every advertisement of it must agree."""
    entries = require_list(value.get("reading_order"), "reading_order")
    declared: list[tuple[int, str]] = []
    for index, item in enumerate(entries):
        where = f"reading_order[{index}]"
        if not isinstance(item, dict):
            raise MissionControlError(f"{where} must be an object")
        require_text(item.get("role"), f"{where}.role")
        require_text(item.get("detail"), f"{where}.detail")
        if item.get("position") != index + 1:
            raise MissionControlError(
                f"{where}.position is {item.get('position')!r}; the reading order is a sequence "
                "and its positions are 1..n in order"
            )
        validate_source_pin(item, where)
        declared.append((index + 1, item["source"]))
    if validate_pointer_state:
        for relative in POINTER_FILES:
            advertised = pointer_block(source_path(relative))
            if advertised != declared:
                raise MissionControlError(
                    f"{relative} advertises a stale reading order: it lists "
                    f"{[source for _, source in advertised]} but the manifest declares "
                    f"{[source for _, source in declared]}"
                )


def requirement_ids(path: Path = REQUIREMENTS) -> set[str]:
    """The authoritative id set, extracted from the uniform bold form the document uses."""
    found = set(BOLD_REQUIREMENT_ID.findall(read_text(path)))
    if not found:
        raise MissionControlError(f"no requirement identifiers found in {path.relative_to(REPO)}")
    return found


def load_lane_vocabulary(path: Path = PHASES) -> dict[str, Any]:
    """Lane ids, the declared non-lane work-item values, and per-requirement lane ownership."""
    try:
        value = json.loads(read_text(path))
    except json.JSONDecodeError as exc:
        raise MissionControlError(f"invalid lane vocabulary JSON: {exc}") from exc
    lanes = require_list(value.get("lanes"), "lane vocabulary lanes")
    lane_ids: list[str] = []
    owned: dict[str, str] = {}
    for index, lane in enumerate(lanes):
        if not isinstance(lane, dict):
            raise MissionControlError(f"lanes[{index}] must be an object")
        lane_id = require_text(lane.get("id"), f"lanes[{index}].id")
        require_text(lane.get("slug"), f"lanes[{index}].slug")
        lane_state = require_text(lane.get("state"), f"lanes[{index}].state")
        if lane_state not in ALLOWED_LANE_STATES:
            raise MissionControlError(
                f"invalid lane state at lanes[{index}]: {lane_state!r} is not one of "
                f"{sorted(ALLOWED_LANE_STATES)}"
            )
        if lane_state == "complete":
            require_text(lane.get("completed_on"), f"lanes[{index}].completed_on")
            source_path(require_text(
                lane.get("completion_evidence"), f"lanes[{index}].completion_evidence"))
        elif "completed_on" in lane or "completion_evidence" in lane:
            raise MissionControlError(
                f"lanes[{index}] is {lane_state!r} but carries completion metadata"
            )
        if lane_id == "4A":
            validate_acceptance_journey(lane.get("acceptance_journey"))
        lane_ids.append(lane_id)
        for position, requirement in enumerate(lane.get("requirements", [])):
            owned[require_text(requirement, f"lanes[{index}].requirements[{position}]")] = lane_id
    if len(set(lane_ids)) != len(lane_ids):
        raise MissionControlError("lane vocabulary contains a duplicate lane id")
    work = value.get("work_item_lane_assignment_values")
    if not isinstance(work, dict):
        raise MissionControlError("lane vocabulary must declare work_item_lane_assignment_values")
    assignment_states: set[str] = set()
    for index, item in enumerate(require_list(work.get("assignment_states"), "assignment_states")):
        if not isinstance(item, dict):
            raise MissionControlError(f"assignment_states[{index}] must be an object")
        assignment_states.add(require_text(item.get("value"), f"assignment_states[{index}].value"))
        require_text(item.get("meaning"), f"assignment_states[{index}].meaning")
        # The eligibility rule is what keeps an assignment state from becoming an opt-out, and
        # `not` is what keeps a reader from citing it as a lane.
        require_text(item.get("rule"), f"assignment_states[{index}].rule")
        require_text(item.get("not"), f"assignment_states[{index}].not")
    collision = assignment_states & set(lane_ids)
    if collision:
        raise MissionControlError(
            f"lane assignment state collides with a lane id: {sorted(collision)}; an assignment "
            "state is not a lane and may never be readable as one"
        )
    return {
        "lane_ids": set(lane_ids),
        "assignment_states": assignment_states,
        "owned": owned,
        "cited": set(REQUIREMENT_ID.findall(read_text(path))),
    }


def load_requirements_ledger(path: Path = LEDGER) -> dict[str, Any]:
    try:
        value = json.loads(read_text(path))
    except json.JSONDecodeError as exc:
        raise MissionControlError(f"invalid requirements ledger JSON: {exc}") from exc
    if not isinstance(value, dict) or value.get("schema") != LEDGER_SCHEMA:
        raise MissionControlError(f"requirements ledger schema must be {LEDGER_SCHEMA}")
    return value


def validate_program_invariant(
    manifest: dict[str, Any],
    *,
    ledger: dict[str, Any] | None = None,
    lanes: dict[str, Any] | None = None,
    document_ids: set[str] | None = None,
) -> None:
    """No executable roadmap item without lineage; no active requirement without a disposition.

    Five failures have to be reachable, and each one has a negative test:
    an active requirement claimed by no work item; a work item citing a requirement that does
    not exist; a work item with no lane disposition; a requirement with no explicit
    disposition; and a requirements-document edit with no matching ledger change.
    """
    ledger = load_requirements_ledger() if ledger is None else ledger
    lanes = load_lane_vocabulary() if lanes is None else lanes
    document_ids = requirement_ids() if document_ids is None else document_ids
    lane_ids = lanes["lane_ids"]

    missing_citation = sorted(lanes["cited"] - document_ids)
    if missing_citation:
        raise MissionControlError(
            f"lane vocabulary cites requirements that no longer exist: {missing_citation}"
        )

    # Check 4 - every requirement carries an explicit disposition and its companion field.
    entries: dict[str, dict[str, Any]] = {}
    for index, entry in enumerate(require_list(ledger.get("requirements"), "ledger.requirements")):
        where = f"ledger.requirements[{index}]"
        if not isinstance(entry, dict):
            raise MissionControlError(f"{where} must be an object")
        entry_id = require_text(entry.get("id"), f"{where}.id")
        if entry_id in entries:
            raise MissionControlError(f"duplicate requirement in ledger: {entry_id}")
        disposition = entry.get("disposition")
        if disposition not in REQUIREMENT_DISPOSITIONS:
            raise MissionControlError(
                f"requirement {entry_id} lacks an explicit disposition: {disposition!r} is not "
                f"one of {sorted(REQUIREMENT_DISPOSITIONS)}"
            )
        for companion in REQUIREMENT_DISPOSITIONS[disposition]:
            if companion == "evidence":
                for position, reference in enumerate(
                    require_list(entry.get("evidence"), f"{where}.evidence")
                ):
                    source_path(require_text(reference, f"{where}.evidence[{position}]"))
            else:
                require_text(entry.get(companion), f"{where}.{companion}")
        if disposition in LANELESS_DISPOSITIONS:
            stray = sorted({"lane", "phase", "lane_authority"} & set(entry))
            if stray:
                raise MissionControlError(
                    f"requirement {entry_id} is {disposition} and carries {stray}; a "
                    f"{disposition} requirement has no lane, which is what keeps it from "
                    "drifting toward its apparently obvious destination"
                )
            entries[entry_id] = entry
            continue
        recorded = entry["lane"] if disposition == "active" else entry["phase"]
        if recorded not in lane_ids:
            if recorded in lanes["assignment_states"]:
                raise MissionControlError(
                    f"requirement {entry_id} records {recorded!r} as a lane; that is a work-item "
                    "lane assignment state, not a lane, and it schedules nothing"
                )
            raise MissionControlError(f"requirement {entry_id} records unknown lane {recorded!r}")
        authority = entry["lane_authority"]
        if authority not in LANE_AUTHORITIES:
            raise MissionControlError(
                f"requirement {entry_id} names unknown lane_authority {authority!r}; the ledger "
                "may record where a lane came from, never that it came from the ledger"
            )
        if disposition == "deferred" and authority != "phase-authority":
            raise MissionControlError(
                f"requirement {entry_id} is deferred to {recorded} on {authority!r} authority; a "
                "future lane can only come from the phase authority"
            )
        if authority == "phase-authority" and lanes["owned"].get(entry_id) != recorded:
            raise MissionControlError(
                f"requirement {entry_id} claims phase authority for lane {recorded}, but the "
                f"lane vocabulary records {lanes['owned'].get(entry_id)!r}"
            )
        if authority == "queue-realization" and entry_id in lanes["owned"]:
            raise MissionControlError(
                f"requirement {entry_id} is owned by lane {lanes['owned'][entry_id]} in the lane "
                "vocabulary, so its lane_authority is phase-authority, not queue-realization"
            )
        entries[entry_id] = entry

    # Check 5 - the anti-reintroduction clause. Editing the requirements document without
    # touching the ledger fails here, the way a stale source_contains pin fails when prose moves.
    if set(entries) != document_ids:
        added = sorted(document_ids - set(entries))
        dropped = sorted(set(entries) - document_ids)
        raise MissionControlError(
            "requirements document and ledger disagree; "
            f"in the document with no ledger entry: {added}; "
            f"in the ledger but no longer in the document: {dropped}"
        )

    # The lane vocabulary wins wherever it names an owner. This ledger records a disposition;
    # it never moves a requirement between lanes, and never places one the vocabulary has not.
    for entry_id, lane_id in lanes["owned"].items():
        entry = entries[entry_id]
        claimed = entry.get("lane") or entry.get("phase")
        if claimed != lane_id:
            raise MissionControlError(
                f"requirement {entry_id} is owned by lane {lane_id} in the lane vocabulary but "
                f"the ledger records {entry['disposition']} / {claimed!r}"
            )

    claims: dict[str, set[str]] = {}
    for index, item in enumerate(manifest["queue"]):
        where = f"queue[{index}] ({item.get('id')})"

        # Check 3 - a work item with no lane disposition.
        lane = item.get("lane")
        if not isinstance(lane, str) or not lane.strip():
            raise MissionControlError(f"{where} has no lane disposition")
        if lane not in lane_ids and lane not in lanes["assignment_states"]:
            raise MissionControlError(
                f"{where} claims unknown lane {lane!r}; the vocabulary is "
                f"{sorted(lane_ids)} plus assignment states {sorted(lanes['assignment_states'])}"
            )
        if lane == "unassigned":
            require_text(item.get("lane_note"), f"queue[{index}].lane_note")
        if lane == "pre-lane" and item.get("state") not in {"complete", "gated"}:
            raise MissionControlError(
                f"{where} is `pre-lane` but its state is {item.get('state')!r}; pre-lane is for "
                "completed or blocked pre-vocabulary work, not for schedulable work"
            )

        # Check 2 - a work item referencing a requirement that does not exist.
        claimed = item.get("requirements")
        if not isinstance(claimed, list):
            raise MissionControlError(f"{where} must carry a requirements list, even if empty")
        for position, entry_id in enumerate(claimed):
            require_text(entry_id, f"queue[{index}].requirements[{position}]")
            if entry_id not in entries:
                raise MissionControlError(
                    f"{where} references requirement {entry_id}, which is in no ledger entry"
                )
            claims.setdefault(entry_id, set()).add(lane)

        # The other half of the invariant: no executable item without lineage.
        if item.get("state") != "complete" and not claimed:
            raise MissionControlError(f"{where} is executable but claims no requirement")

    # Check 1 - an active requirement claimed by no work item in its lane.
    for entry_id, entry in entries.items():
        if entry["disposition"] != "active":
            continue
        if entry["lane"] not in claims.get(entry_id, set()):
            raise MissionControlError(
                f"active requirement {entry_id} is claimed by no work item in lane {entry['lane']}"
            )


def validate_guardrail_taxonomy() -> None:
    text = read_text(REPO / "docs" / "five-intent-program-plan.md")
    product_heading = "### Six product guardrails"
    communication_heading = "### Communication guardrail"
    if text.count(product_heading) != 1 or text.count(communication_heading) != 1:
        raise MissionControlError(
            "five-intent program plan must distinguish six product guardrails from one communication guardrail"
        )
    product = text.split(product_heading, 1)[1].split(communication_heading, 1)[0]
    communication = text.split(communication_heading, 1)[1].split("\n## ", 1)[0]
    product_numbers = re.findall(r"(?m)^(\d+)\.\s+\*\*", product)
    communication_numbers = re.findall(r"(?m)^(\d+)\.\s+\*\*", communication)
    if product_numbers != ["1", "2", "3", "4", "5", "6"]:
        raise MissionControlError(
            f"product guardrails must be numbered 1..6; got {product_numbers}"
        )
    if communication_numbers != ["7"] or "Answer at the reporter's altitude" not in communication:
        raise MissionControlError(
            "communication guardrail must be item 7, Answer at the reporter's altitude"
        )


def validate_architectural_build(value: Any) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise MissionControlError("next_attack must be an object")
    if require_text(value.get("status"), "next_attack.status") != "demo-ready-am4-warm":
        raise MissionControlError("next_attack.status must be demo-ready-am4-warm")
    if require_text(value.get("fixture"), "next_attack.fixture") != "tn0304":
        raise MissionControlError("next_attack.fixture must be tn0304")
    require_text(value.get("completed_utc"), "next_attack.completed_utc")
    if value.get("capsule_schema") != "creator-os-architectural-build-capsule/v0":
        raise MissionControlError("next_attack carries an unsupported capsule schema")

    journey = require_list(value.get("journey"), "next_attack.journey")
    if len(journey) != 11:
        raise MissionControlError("next_attack.journey must contain the eleven accepted stages")
    for index, step in enumerate(journey):
        require_text(step, f"next_attack.journey[{index}]")

    acceptance = value.get("acceptance")
    expected_acceptance = {
        "footprint_m": [7.953375, 7.4676],
        "wall_datum_m": 2.2225,
        "ridge_m": 5.8166,
        "pitch_degrees": 43.907838,
        "ridge_reconciliation_m": -0.029171,
        "pieces": {"floors": 16, "walls": 16, "roofs": 8, "total": 40},
    }
    if acceptance != expected_acceptance:
        raise MissionControlError("next_attack.acceptance must pin the accepted tn0304 envelope")

    evidence = value.get("evidence")
    if not isinstance(evidence, dict):
        raise MissionControlError("next_attack.evidence must be an object")
    evidence_path = source_path(require_text(evidence.get("source"), "next_attack.evidence.source"))
    if evidence.get("receipt_schema") != "comfy-quest-architectural-warm-evidence-index/v1":
        raise MissionControlError("next_attack.evidence has an unsupported receipt schema")
    staging_path = source_path(require_text(
        evidence.get("staging_source"), "next_attack.evidence.staging_source"))
    if evidence.get("staging_receipt_schema") != "comfy-quest-architectural-build-evidence-index/v1":
        raise MissionControlError("next_attack.evidence has an unsupported staging evidence schema")
    cold_path = source_path(require_text(
        evidence.get("cold_acceptance_source"),
        "next_attack.evidence.cold_acceptance_source"))
    if evidence.get("cold_acceptance_receipt_schema") != "comfy-quest-architectural-live-evidence-index/v1":
        raise MissionControlError("next_attack.evidence has an unsupported cold acceptance schema")
    demo_path = source_path(require_text(
        evidence.get("demo_source"), "next_attack.evidence.demo_source"))
    if evidence.get("demo_receipt_schema") != "comfy-quest-architectural-demo-evidence-index/v1":
        raise MissionControlError("next_attack.evidence has an unsupported demo evidence schema")
    demo_index_sha = require_sha256(
        evidence.get("demo_index_sha256"), "next_attack.evidence.demo_index_sha256")
    demo_receipt_sha = require_sha256(
        evidence.get("demo_receipt_sha256"), "next_attack.evidence.demo_receipt_sha256")
    if evidence.get("stage_receipt_schema") != "comfy-quest-studio-build-stage/v1":
        raise MissionControlError("next_attack.evidence has an unsupported stage receipt schema")
    capsule_sha = require_sha256(evidence.get("capsule_sha256"), "next_attack.evidence.capsule_sha256")
    stage_id = require_sha256(evidence.get("stage_id"), "next_attack.evidence.stage_id")
    capture_sha = require_sha256(evidence.get("capture_sha256"), "next_attack.evidence.capture_sha256")
    blueprint_sha = require_sha256(evidence.get("blueprint_sha256"), "next_attack.evidence.blueprint_sha256")
    first_build_sha = require_sha256(
        evidence.get("first_build_receipt_sha256"),
        "next_attack.evidence.first_build_receipt_sha256")
    first_diff_sha = require_sha256(
        evidence.get("first_diff_receipt_sha256"),
        "next_attack.evidence.first_diff_receipt_sha256")
    first_lap_sha = require_sha256(
        evidence.get("first_warm_lap_sha256"),
        "next_attack.evidence.first_warm_lap_sha256")
    reuse_diff_sha = require_sha256(
        evidence.get("reuse_diff_receipt_sha256"),
        "next_attack.evidence.reuse_diff_receipt_sha256")
    reuse_lap_sha = require_sha256(
        evidence.get("reuse_warm_lap_sha256"),
        "next_attack.evidence.reuse_warm_lap_sha256")
    rollback_before_sha = require_sha256(
        evidence.get("rollback_before_sha256"),
        "next_attack.evidence.rollback_before_sha256")
    screenshot_sha = require_sha256(
        evidence.get("screenshot_sha256"),
        "next_attack.evidence.screenshot_sha256")

    try:
        receipt = json.loads(read_text(evidence_path))
    except json.JSONDecodeError as exc:
        raise MissionControlError(f"invalid architectural-build evidence JSON: {exc}") from exc
    if (receipt.get("schema") != evidence["receipt_schema"]
            or receipt.get("result") != "passed"
            or receipt.get("state") != "active-warm"):
        raise MissionControlError("architectural-build evidence receipt is not a passing supported receipt")
    if receipt.get("fixture") != value["fixture"] or receipt.get("architecture") != acceptance:
        raise MissionControlError("architectural-build evidence disagrees with mission-control acceptance")
    identity = receipt.get("identity", {})
    artifacts = receipt.get("canonical_artifacts", {})
    if identity.get("capsule_sha256") != capsule_sha or identity.get("stage_id") != stage_id:
        raise MissionControlError("architectural-build evidence identity disagrees with mission control")
    if artifacts.get("capture", {}).get("sha256") != capture_sha:
        raise MissionControlError("architectural-build capture hash disagrees with mission control")
    if artifacts.get("blueprint", {}).get("sha256") != blueprint_sha:
        raise MissionControlError("architectural-build blueprint hash disagrees with mission control")

    try:
        staging_receipt = json.loads(read_text(staging_path))
        cold_receipt = json.loads(read_text(cold_path))
        demo_receipt = json.loads(read_text(demo_path))
    except json.JSONDecodeError as exc:
        raise MissionControlError(f"invalid architectural prerequisite evidence JSON: {exc}") from exc
    if (staging_receipt.get("schema") != evidence["staging_receipt_schema"]
            or staging_receipt.get("result") != "passed"):
        raise MissionControlError("architectural staging evidence is not a passing supported receipt")
    if (cold_receipt.get("schema") != evidence["cold_acceptance_receipt_schema"]
            or cold_receipt.get("result") != "passed"):
        raise MissionControlError("architectural cold acceptance evidence is not a passing supported receipt")
    if hashlib.sha256(demo_path.read_bytes()).hexdigest() != demo_index_sha:
        raise MissionControlError("architectural demo evidence index hash disagrees with mission control")
    if (demo_receipt.get("schema") != evidence["demo_receipt_schema"]
            or demo_receipt.get("result") != "passed"
            or demo_receipt.get("state") != "operator-ready-warm"):
        raise MissionControlError("architectural demo evidence is not a passing operator-ready receipt")
    demo_identity = demo_receipt.get("identity", {})
    demo_artifacts = demo_receipt.get("canonical_artifacts", {})
    demo = demo_receipt.get("operator_demo", {})
    if (demo_identity.get("capsule_sha256") != capsule_sha
            or demo_identity.get("stage_id") != stage_id
            or demo_artifacts.get("capture_sha256") != capture_sha
            or demo_artifacts.get("blueprint_sha256") != blueprint_sha
            or demo.get("operations") != ["status", "blueprint_check", "blueprint_count",
                                           "blueprint_diff", "status"]
            or demo.get("build_action") != "reused"
            or demo.get("standing_piece_count") != 40
            or demo.get("diff_result") != "MATCH"
            or demo.get("canonical_stage_already_present") is not True
            or demo.get("remote_studio_reused") is not True
            or demo.get("ssh_tunnel_reused") is not True
            or demo.get("screenshot", {}).get("capture_kind") != "x11-window"
            or demo.get("screenshot", {}).get("width") != 1920
            or demo.get("screenshot", {}).get("height") != 1080
            or demo.get("screenshot", {}).get("sha256") != screenshot_sha
            or demo_receipt.get("evidence", {}).get("demo_receipt_sha256") != demo_receipt_sha):
        raise MissionControlError("architectural operator demo evidence disagrees with mission control")

    first = receipt.get("first_lap", {})
    second = receipt.get("second_lap", {})
    rollback = receipt.get("rollback_snapshot", {})
    if (first.get("build_action") != "created" or first.get("standing_piece_count") != 40
            or first.get("diff", {}).get("result") != "MATCH"
            or first.get("build_receipt_sha256") != first_build_sha
            or first.get("diff_receipt_sha256") != first_diff_sha
            or first.get("warm_lap_sha256") != first_lap_sha):
        raise MissionControlError("architectural first warm lap disagrees with mission control")
    if (second.get("build_action") != "reused" or second.get("standing_piece_count") != 40
            or second.get("game_was_running") is not True
            or second.get("game_launched") is not False
            or second.get("diff", {}).get("result") != "MATCH"
            or second.get("diff_receipt_sha256") != reuse_diff_sha
            or second.get("warm_lap_sha256") != reuse_lap_sha
            or second.get("screenshot_sha256") != screenshot_sha):
        raise MissionControlError("architectural reuse lap disagrees with mission control")
    if (rollback.get("state") != "retained-not-applied"
            or rollback.get("before_sha256") != rollback_before_sha):
        raise MissionControlError("architectural warm rollback snapshot disagrees with mission control")

    safety = value.get("safety")
    expected_safety = {
        "creator_session_started": True,
        "mailbox_request_written": True,
        "world_mutation_performed": True,
        "valheim_started": True,
        "canonical_artifacts_unchanged": True,
        "build_created_once": True,
        "running_client_reused": True,
        "existing_build_reused": True,
        "marked_pieces_retained": True,
        "creator_build_disabled": True,
        "valheim_running": True,
        "rollback_snapshot_retained": True,
        "ordinary_lap_performed_teardown": False,
        "operator_demo_game_launched": False,
        "operator_demo_session_opened": False,
        "operator_demo_mailbox_request_written": False,
        "operator_demo_world_mutation_performed": False,
        "canonical_stage_idempotent": True,
        "control_plane_reused": True,
        "exact_read_only_sequence": True,
    }
    if safety != expected_safety:
        raise MissionControlError("next_attack.safety must retain the accepted warm-loop boundary")
    warm_state = receipt.get("warm_state", {})
    if (warm_state.get("valheim_running") is not True
            or warm_state.get("marked_pieces_retained") != 40
            or warm_state.get("creator_build_enabled") is not False
            or warm_state.get("canonical_artifacts_unchanged") is not True
            or warm_state.get("pending_runtime_mailbox") is not False
            or warm_state.get("pending_lab_mailbox") is not False):
        raise MissionControlError("architectural warm-state evidence disagrees with mission control")
    skipped = warm_state.get("identity_matching_lap_skips")
    expected_skipped = ["plugin_deploy", "world_entry", "blueprint_build",
                        "blueprint_clear", "valheim_stop", "state_restore"]
    if skipped != expected_skipped:
        raise MissionControlError("architectural warm lap does not retain the no-teardown contract")
    require_text(value.get("boundary"), "next_attack.boundary")
    for index, route in enumerate(require_list(value.get("studio_routes"), "next_attack.studio_routes")):
        require_text(route, f"next_attack.studio_routes[{index}]")
    return value


def validate_manifest(value: Any, *, validate_projection_state: bool = True) -> None:
    if not isinstance(value, dict) or value.get("schema") != SCHEMA:
        raise MissionControlError(f"mission-control schema must be {SCHEMA}")
    page = value.get("page")
    if not isinstance(page, dict):
        raise MissionControlError("page must be an object")
    for key in ("id", "title", "subtitle", "observed_on", "program_commit", "program_commit_label"):
        require_text(page.get(key), f"page.{key}")
    if not re.fullmatch(r"[a-z0-9][a-z0-9.-]{1,79}", page["id"]):
        raise MissionControlError(f"unsafe page id: {page['id']}")
    validate_source_intents(value)

    ids: set[str] = set()

    def take_id(item: dict[str, Any], where: str) -> None:
        item_id = require_text(item.get("id"), f"{where}.id")
        if not re.fullmatch(r"[a-z0-9][a-z0-9.-]{1,79}", item_id):
            raise MissionControlError(f"unsafe stable id at {where}: {item_id}")
        if item_id in ids:
            raise MissionControlError(f"duplicate stable id: {item_id}")
        ids.add(item_id)

    for group in ("machines", "environment", "phases", "queue", "decisions", "commands", "reading_order"):
        for index, item in enumerate(require_list(value.get(group), group)):
            if not isinstance(item, dict):
                raise MissionControlError(f"{group}[{index}] must be an object")
            take_id(item, f"{group}[{index}]")

    phases = value["phases"]
    if [item.get("number") for item in phases] != [1, 2, 3, 4, 5]:
        raise MissionControlError("phases must contain the ordered five-phase program")
    for index, phase in enumerate(phases):
        if phase.get("state") not in ALLOWED_PHASE_STATES:
            raise MissionControlError(f"invalid phase state at phases[{index}]")
        validate_source_pin(phase, f"phases[{index}]")

    for index, item in enumerate(value["queue"]):
        if item.get("state") not in ALLOWED_QUEUE_STATES:
            raise MissionControlError(f"invalid queue state at queue[{index}]")
        validate_source_pin(item, f"queue[{index}]")
    for index, item in enumerate(value["decisions"]):
        validate_source_pin(item, f"decisions[{index}]")

    architectural_build = validate_architectural_build(value.get("next_attack"))
    take_id(architectural_build, "next_attack")

    strategy = value.get("strategy")
    if not isinstance(strategy, dict):
        raise MissionControlError("strategy must be an object")
    take_id(strategy, "strategy")
    validate_source_pin(strategy, "strategy")
    for key in ("title", "summary", "golden_rule", "readiness_title"):
        require_text(strategy.get(key), f"strategy.{key}")
    principles = require_list(strategy.get("principles"), "strategy.principles")
    for index, principle in enumerate(principles):
        if not isinstance(principle, dict):
            raise MissionControlError(f"strategy.principles[{index}] must be an object")
        take_id(principle, f"strategy.principles[{index}]")
        require_text(principle.get("title"), f"strategy.principles[{index}].title")
        require_text(principle.get("detail"), f"strategy.principles[{index}].detail")
    for index, item in enumerate(require_list(strategy.get("readiness"), "strategy.readiness")):
        require_text(item, f"strategy.readiness[{index}]")

    recovery = value.get("recovery")
    if not isinstance(recovery, dict):
        raise MissionControlError("recovery must be an object")
    take_id(recovery, "recovery")
    cold = recovery.get("cold_load")
    if not isinstance(cold, dict):
        raise MissionControlError("recovery.cold_load must be an object")
    take_id(cold, "recovery.cold_load")
    validate_source_pin(cold, "recovery.cold_load")
    creator = recovery.get("creator_flow")
    if not isinstance(creator, dict):
        raise MissionControlError("recovery.creator_flow must be an object")
    source_path(require_text(creator.get("source"), "recovery.creator_flow.source"))
    require_text(creator.get("heading"), "recovery.creator_flow.heading")
    revision = recovery.get("revision_flow")
    if not isinstance(revision, dict):
        raise MissionControlError("recovery.revision_flow must be an object")
    take_id(revision, "recovery.revision_flow")
    validate_source_pin(revision, "recovery.revision_flow")
    require_text(revision.get("source_sequence"), "recovery.revision_flow.source_sequence")
    edit_path = source_path(require_text(revision.get("edit_source"), "recovery.revision_flow.edit_source"))
    edit_marker = require_text(revision.get("edit_source_contains"), "recovery.revision_flow.edit_source_contains")
    if edit_marker not in read_text(edit_path):
        raise MissionControlError("recovery.revision_flow edit source pin is stale")
    require_text(revision.get("suggested_edit"), "recovery.revision_flow.suggested_edit")
    source_path(require_text(recovery.get("expectations"), "recovery.expectations"))

    phase3 = value.get("phase3_lap")
    if not isinstance(phase3, dict):
        raise MissionControlError("phase3_lap must be an object")
    source_path(require_text(phase3.get("source"), "phase3_lap.source"))
    require_text(phase3.get("sequence_heading"), "phase3_lap.sequence_heading")
    require_text(phase3.get("verdicts_heading"), "phase3_lap.verdicts_heading")
    lap_state = phase3.get("state")
    if lap_state not in ALLOWED_LAP_STATES:
        raise MissionControlError(
            f"phase3_lap.state must be one of {sorted(ALLOWED_LAP_STATES)}; a lap with no "
            "recorded freshness renders as an actionable sequence"
        )
    if lap_state == "stale":
        require_text(phase3.get("stale_reason"), "phase3_lap.stale_reason")
        require_text(phase3.get("rederive_before_running"), "phase3_lap.rederive_before_running")
        source_path(require_text(phase3.get("decision"), "phase3_lap.decision"))

    allowed_states = {
        "machines": ALLOWED_MACHINE_STATES,
        "environment": ALLOWED_ENVIRONMENT_STATES,
    }
    for key in ("machines", "environment"):
        for index, item in enumerate(value[key]):
            require_text(item.get("label"), f"{key}[{index}].label")
            state = require_text(item.get("state"), f"{key}[{index}].state")
            if state not in allowed_states[key]:
                raise MissionControlError(
                    f"invalid {key} state at {key}[{index}]: {state!r} has no badge or status "
                    f"style, so it would render unlabelled"
                )
            require_text(item.get("detail"), f"{key}[{index}].detail")
            require_text(item.get("fact_kind"), f"{key}[{index}].fact_kind")

    for index, command in enumerate(value["commands"]):
        require_text(command.get("command"), f"commands[{index}].command")
    require_list(value.get("cautions"), "cautions")

    validate_guardrail_taxonomy()
    validate_reading_order(value, validate_pointer_state=validate_projection_state)
    validate_program_invariant(value)
    if validate_projection_state:
        validate_projections(value)


def markdown_section(path: Path, heading: str) -> list[str]:
    lines = read_text(path).splitlines()
    wanted = re.compile(rf"^##\s+{re.escape(heading)}\s*$", re.IGNORECASE)
    start = next((index + 1 for index, line in enumerate(lines) if wanted.match(line.strip())), None)
    if start is None:
        raise MissionControlError(f"heading {heading!r} not found in {path.relative_to(REPO)}")
    end = next((index for index in range(start, len(lines)) if lines[index].startswith("## ")), len(lines))
    return lines[start:end]


def list_items(lines: list[str], ordered: bool) -> list[str]:
    marker = re.compile(r"^\s*\d+\.\s+(.*)$" if ordered else r"^\s*-\s+(.*)$")
    items: list[str] = []
    current: list[str] = []
    for line in lines:
        match = marker.match(line)
        if match:
            if current:
                items.append(" ".join(current).strip())
            current = [match.group(1).strip()]
            continue
        if current:
            stripped = line.strip()
            if not stripped:
                items.append(" ".join(current).strip())
                current = []
            elif not line.startswith((" ", "\t")):
                items.append(" ".join(current).strip())
                current = []
            else:
                current.append(stripped)
    if current:
        items.append(" ".join(current).strip())
    if not items:
        kind = "ordered" if ordered else "unordered"
        raise MissionControlError(f"no {kind} items found in selected Markdown section")
    return items


def inline_markdown(value: str) -> str:
    code_tokens: list[str] = []

    def protect_code(match: re.Match[str]) -> str:
        token = f"QMCODETOKEN{len(code_tokens)}Q"
        code_tokens.append(f"<code>{html.escape(match.group(1).strip(), quote=True)}</code>")
        return token

    protected = re.sub(r"``\s*(.*?)\s*``", protect_code, value)
    protected = re.sub(r"`([^`]+)`", protect_code, protected)
    escaped = html.escape(protected, quote=True)
    escaped = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", escaped)
    escaped = re.sub(r"\*([^*]+?)\*", r"<em>\1</em>", escaped)
    for index, token in enumerate(code_tokens):
        escaped = escaped.replace(f"QMCODETOKEN{index}Q", token)
    return escaped


def href_for(relative: str) -> str:
    target = source_path(relative)
    return Path(os.path.relpath(target, OUTPUT.parent)).as_posix()


def source_link(relative: str, label: str = "Source") -> str:
    return f'<a class="source-link" href="{html.escape(href_for(relative), quote=True)}">{html.escape(label)}</a>'


def badge(state: str) -> str:
    labels = {
        "complete": "Complete",
        "implemented": "Implemented",
        "active": "Active",
        "planned": "Planned",
        "ready": "Ready",
        "human": "Needs Derek",
        "gated": "Gated",
        "deferred": "Deferred",
        "online": "Online",
        "on-demand": "On demand",
        "confirmed": "Confirmed",
        "attention": "Attention",
    }
    return f'<span class="badge badge-{html.escape(state)}">{html.escape(labels.get(state, state.title()))}</span>'


def checklist_item(item_id: str, body: str, *, source: str | None = None, source_label: str = "Source") -> str:
    link = f" {source_link(source, source_label)}" if source else ""
    safe_id = html.escape(item_id, quote=True)
    return (
        f'<li class="check-item" data-check-item="{safe_id}">'
        f'<input type="checkbox" id="check-{safe_id}" data-check-id="{safe_id}">'
        f'<label for="check-{safe_id}"><span>{body}</span>{link}</label></li>'
    )


def evidence_receipts(expectations_path: Path) -> list[dict[str, Any]]:
    try:
        value = json.loads(read_text(expectations_path))
        first = value["imported_fork"]["first_revision_receipts"]
        completion = value["behavior"]["receipt_assertions"]
    except (KeyError, TypeError, json.JSONDecodeError) as exc:
        raise MissionControlError("Creator OS expectation contract no longer exposes the pinned proof chain") from exc
    receipts = [*first, *completion]
    if not all(isinstance(item, dict) and item.get("operation") and item.get("status") for item in receipts):
        raise MissionControlError("Creator OS proof chain contains an invalid receipt assertion")
    return receipts


def later_revision_expectation(expectations_path: Path) -> dict[str, Any]:
    try:
        value = json.loads(read_text(expectations_path))
        later = value["imported_fork"]["later_same_fork_revision"]
        receipt = later["changed_content_receipt"]
    except (KeyError, TypeError, json.JSONDecodeError) as exc:
        raise MissionControlError("Creator OS contract no longer exposes replay proof") from exc
    if later.get("capture_source_hash_is_preserved") is not True:
        raise MissionControlError("Creator OS capture authority is no longer hash-pinned")
    if not isinstance(receipt, dict) or not receipt.get("operation") or not receipt.get("status"):
        raise MissionControlError("Creator OS replay-diff receipt is invalid")
    return later


def current_git_subject(commit: str) -> str:
    try:
        result = subprocess.run(
            ["git", "show", "-s", "--format=%s", commit],
            cwd=REPO,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
    except (OSError, subprocess.CalledProcessError) as exc:
        raise MissionControlError(f"program commit is not available: {commit}") from exc
    return result.stdout.strip()


def assert_repo_identity() -> None:
    shell = shutil.which("pwsh") or shutil.which("powershell")
    if shell is None:
        raise MissionControlError("PowerShell is required to run tools/Assert-RepoIdentity.ps1 before writing")
    try:
        subprocess.run(
            [shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(REPO / "tools" / "Assert-RepoIdentity.ps1")],
            cwd=REPO,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
    except subprocess.CalledProcessError as exc:
        detail = (exc.stderr or exc.stdout or "repository identity check failed").strip()
        raise MissionControlError(detail) from exc


def render(manifest: dict[str, Any]) -> str:
    page = manifest["page"]
    subject = current_git_subject(page["program_commit"])
    if subject != page["program_commit_label"]:
        raise MissionControlError("program commit label does not match Git history")

    creator = manifest["recovery"]["creator_flow"]
    creator_steps = list_items(markdown_section(source_path(creator["source"]), creator["heading"]), ordered=True)
    revision = manifest["recovery"]["revision_flow"]
    revision_steps = [item.strip() for item in revision["source_sequence"].split(" -> ") if item.strip()]
    revision_source = read_text(source_path(revision["source"]))
    phase3 = manifest["phase3_lap"]
    phase3_source = source_path(phase3["source"])
    phase3_is_stale = phase3["state"] == "stale"
    phase3_steps: list[str] = []
    phase3_verdicts: list[str] = []
    if not phase3_is_stale:
        phase3_steps = list_items(markdown_section(phase3_source, phase3["sequence_heading"]), ordered=True)
        phase3_verdicts = list_items(markdown_section(phase3_source, phase3["verdicts_heading"]), ordered=False)
    if len(creator_steps) != 6:
        raise MissionControlError(f"Creator Session loop must remain six derived steps, found {len(creator_steps)}")
    if len(revision_steps) < 2 or revision["source_sequence"] not in revision_source:
        raise MissionControlError("Portfolio dogfood loop must remain a source-declared sequence")
    if not phase3_is_stale and (len(phase3_steps) != 5 or len(phase3_verdicts) != 3):
        raise MissionControlError("Phase 3 runbook must expose five sequence steps and exactly three seat verdicts")

    expectations_relative = manifest["recovery"]["expectations"]
    expectations_path = source_path(expectations_relative)
    receipts = evidence_receipts(expectations_path)
    later_revision_expectation(expectations_path)
    source_hash = hashlib.sha256(SOURCE.read_bytes()).hexdigest()[:12]

    strategy = manifest["strategy"]
    strategy_cards = "".join(
        f'''<article class="strategy-card"><span class="eyebrow">Operating rule</span><h3>{html.escape(item["title"])}</h3><p>{html.escape(item["detail"])}</p></article>'''
        for item in strategy["principles"]
    )
    readiness_items = "".join(f"<li>{html.escape(item)}</li>" for item in strategy["readiness"])

    machine_cards = "".join(
        f'''<article class="machine-card">
          <div class="card-heading"><div><span class="eyebrow">{html.escape(item["fact_kind"])}</span><h3>{html.escape(item["label"])}</h3></div>{badge(item["state"])}</div>
          <strong>{html.escape(item["role"])}</strong><p>{html.escape(item["detail"])}</p>
          <label class="session-confirm"><input type="checkbox" data-check-id="machine.{html.escape(item["id"], quote=True)}"> Confirmed this sitting</label>
        </article>'''
        for item in manifest["machines"]
    )

    environment_cards = "".join(
        f'''<li><span class="status-dot status-{html.escape(item["state"])}" aria-hidden="true"></span><div><strong>{html.escape(item["label"])}</strong><p>{html.escape(item["detail"])}</p><span class="fact-kind">{html.escape(item["fact_kind"])}</span></div></li>'''
        for item in manifest["environment"]
    )

    phases = "".join(
        f'''<li class="phase phase-{html.escape(item["state"])}">
          <div class="phase-rail"><span>{item["number"]}</span></div>
          <div class="phase-copy"><div class="card-heading"><h3>{html.escape(item["title"])}</h3>{badge(item["state"])}</div><p>{html.escape(item["summary"])}</p>{source_link(item["source"])}</div>
        </li>'''
        for item in manifest["phases"]
    )

    cold = manifest["recovery"]["cold_load"]
    cold_item = checklist_item(
        cold["id"],
        f'<strong>{html.escape(cold["title"])}</strong><small>{html.escape(cold["instruction"])}</small>',
        source=cold["source"],
        source_label="Operating notes",
    )
    creator_items = "".join(
        checklist_item(
            f"recovery.creator.{index}",
            f'<strong>Step {index}</strong><small>{inline_markdown(step)}</small>',
            source=creator["source"] if index == 1 else None,
            source_label="Derived sequence",
        )
        for index, step in enumerate(creator_steps, 1)
    )
    revision_items = "".join(
        checklist_item(
            f"recovery.revision.{index}",
            f'<strong>{html.escape(step)}</strong><small>Stage {index} of the portfolio dogfood loop.</small>',
            source=revision["source"] if index == 1 else None,
            source_label="Derived sequence",
        )
        for index, step in enumerate(revision_steps, 1)
    )
    revision_proof = (
        '<div class="revision-proof"><span><strong>4A machine gate</strong> Scoped reset receipt plus a distinct rerun identity</span>'
        '<span><strong>4A seat gate</strong> No repository edit, console, file relay, log reading, or machine-fact check by Derek</span></div>'
    )

    proof_rows = "".join(
        f'''<li><code>{html.escape(item["operation"])}</code><span>{html.escape(item["status"])}</span>{f'<small>{html.escape(item.get("error", ""))}</small>' if item.get("error") else ""}</li>'''
        for item in receipts
    )

    queue_cards = "".join(
        f'''<article class="queue-card"><div class="card-heading"><h3>{html.escape(item["title"])}</h3>{badge(item["state"])}</div><p>{html.escape(item["detail"])}</p>{source_link(item["source"])}</article>'''
        for item in manifest["queue"]
    )

    architectural_build = manifest["next_attack"]
    build_acceptance = architectural_build["acceptance"]
    build_evidence = architectural_build["evidence"]
    build_steps = "".join(
        f'''<li><span>{index}</span><div><strong>{html.escape(step)}</strong></div></li>'''
        for index, step in enumerate(architectural_build["journey"], 1)
    )
    build_artifacts = "".join(
        f'''<li><code>{html.escape(label)}</code><span>SHA-256</span><small>{html.escape(digest)}</small></li>'''
        for label, digest in (
            ("capsule", build_evidence["capsule_sha256"]),
            ("stage receipt", build_evidence["stage_id"]),
            ("capture", build_evidence["capture_sha256"]),
            ("blueprint", build_evidence["blueprint_sha256"]),
            ("first build", build_evidence["first_build_receipt_sha256"]),
            ("warm reuse diff", build_evidence["reuse_diff_receipt_sha256"]),
            ("operator demo", build_evidence["demo_receipt_sha256"]),
            ("rollback snapshot", build_evidence["rollback_before_sha256"]),
        )
    )
    architectural_build_panel = f'''
    <div class="recovery-grid">
      <article class="panel flow-panel"><div class="card-heading"><div><span class="eyebrow">Demo-ready on AM4</span><h3>Architectural capsule &rarr; Studio Build &rarr; exact live build &rarr; reusable operator demo</h3></div><span class="badge badge-complete">PASS</span></div><p>The tn0304 handoff crossed the real Studio and Lab, built once, and now reopens through one bounded command while reusing the canonical stage, control plane, running client, and standing structure.</p><ol class="build-sequence">{build_steps}</ol><div class="revision-proof build-metrics"><span><strong>Footprint</strong>{build_acceptance["footprint_m"][0]} &times; {build_acceptance["footprint_m"][1]} m</span><span><strong>Wall datum</strong>{build_acceptance["wall_datum_m"]} m</span><span><strong>True ridge</strong>{build_acceptance["ridge_m"]} m</span><span><strong>Roof pitch</strong>{build_acceptance["pitch_degrees"]}&deg;</span><span><strong>Reconciliation</strong>&minus;0.029171 m</span><span><strong>Standing pieces</strong>40 &middot; 16 floors / 16 walls / 8 roofs</span></div><div class="guardrail"><strong>Warm boundary:</strong> {html.escape(architectural_build["boundary"])}</div></article>
      <aside class="panel proof-panel build-proof"><span class="eyebrow">Immutable demo evidence</span><h3>{html.escape(build_evidence["demo_receipt_schema"])}</h3><p>Observed {html.escape(architectural_build["completed_utc"])}. The second operator lap reused Studio, its tunnel, the staged pair, Valheim, and all 40 standing pieces; it ran only status, check, count, diff, and status.</p><ol class="proof-list">{build_artifacts}</ol>{source_link(build_evidence["demo_source"], "Tracked operator-demo index")}{source_link(build_evidence["source"], "Tracked warm-state index")}</aside>
    </div>'''

    decision_cards = "".join(
        f'''<article class="decision-card"><span class="eyebrow">Decision in force</span><h3>{html.escape(item["title"])}</h3><p>{html.escape(item["question"])}</p><small>{html.escape(item["recommendation"])}</small>{source_link(item["source"])}</article>'''
        for item in manifest["decisions"]
    )

    command_cards = "".join(
        f'''<article class="command-card"><div><strong>{html.escape(item["label"])}</strong><small>{html.escape(item["detail"])}</small></div><div class="command-row"><code>{html.escape(item["command"])}</code><button type="button" class="copy-button" data-copy="{html.escape(item["command"], quote=True)}">Copy</button></div></article>'''
        for item in manifest["commands"]
    )

    # Each lane already declares the question it answers and how it fails. Both were in the
    # vocabulary and rendered nowhere, so the page showed what is next and never why any of it
    # exists. A reader who cannot see the question cannot tell whether the work still serves it.
    lanes = json.loads(read_text(PHASES))
    lane_items = "".join(
        f'<li><span>{html.escape(lane["id"])}</span><p><strong>{html.escape(lane["question"])}</strong>'
        f'<small>Fails if: {html.escape(lane["failure_mode"])}</small></p></li>'
        for lane in lanes["lanes"]
    )

    reading_items = "".join(
        f'<li><span>{item["position"]}</span><p><strong>{html.escape(item["role"])}</strong> '
        f'{html.escape(item["detail"])} {source_link(item["source"], item["source"])}</p></li>'
        for item in manifest["reading_order"]
    )

    caution_items = "".join(f"<li>{html.escape(item)}</li>" for item in manifest["cautions"])
    if phase3_is_stale:
        # Refuse to render a stale lap as something a seat could follow. The steps are not
        # emitted at all: a reader who can see them will follow them.
        phase3_panel = (
            '<details class="future"><summary>Parked Phase 3 exit lap · stale, do not run it'
            '</summary><div>'
            f'<p class="guardrail"><strong>Blocked.</strong> {html.escape(phase3["stale_reason"])}</p>'
            f'<p>{html.escape(phase3["summary"])}</p>'
            f'<p><strong>Before it is run:</strong> {html.escape(phase3["rederive_before_running"])}</p>'
            f'{source_link(phase3["decision"], "Superseding decision")} '
            f'{source_link(phase3["source"], "Stale runbook")}</div></details>'
        )
    else:
        phase3_sequence = "".join(
            f'<li><span>{index}</span><p>{inline_markdown(step)}</p></li>'
            for index, step in enumerate(phase3_steps, 1)
        )
        phase3_judgments = "".join(f"<li>{inline_markdown(item)}</li>" for item in phase3_verdicts)
        phase3_panel = (
            '<details class="future"><summary>Preview the derived Phase 3 exit lap</summary><div>'
            f'<p>{html.escape(phase3["summary"])}</p>'
            f'<ol class="derived-sequence">{phase3_sequence}</ol>'
            '<h3>Exactly three human verdicts</h3>'
            f'<ul class="judgment-list">{phase3_judgments}</ul>'
            f'{source_link(phase3["source"], "Derived runbook")}</div></details>'
        )

    css = r'''
:root{color-scheme:dark;--ink:#f5f1e8;--muted:#aaa99f;--dim:#74766f;--panel:#111713;--panel-2:#18201b;--panel-3:#202b24;--line:#344139;--gold:#e9a83f;--gold-soft:#ffd98b;--green:#65d68b;--violet:#c9a7ff;--red:#ff8b7d;--blue:#7fc8ff;--shadow:0 18px 50px rgba(0,0,0,.34);--radius:18px}
.build-sequence{list-style:none;padding:0;margin:15px 0;display:grid;gap:9px}.build-sequence li{display:grid;grid-template-columns:28px 1fr;gap:9px}.build-sequence li>span{width:25px;height:25px;display:grid;place-items:center;border:1px solid var(--line);border-radius:50%;font-size:.7rem;color:var(--gold)}
*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:radial-gradient(circle at 16% 0,rgba(233,168,63,.11),transparent 30rem),radial-gradient(circle at 90% 18%,rgba(100,214,139,.07),transparent 34rem),#090d0b;color:var(--ink);font-family:Inter,ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif;line-height:1.5}.skip-link{position:fixed;top:8px;left:8px;z-index:20;transform:translateY(-150%);padding:10px 14px;background:var(--gold);color:#151008;border-radius:8px}.skip-link:focus{transform:none}a{color:var(--gold-soft);text-underline-offset:3px}button,input,select,textarea{font:inherit}.shell{width:min(1540px,calc(100% - 40px));margin:auto}.masthead{padding:34px 0 22px;border-bottom:1px solid rgba(255,255,255,.07);background:rgba(9,13,11,.84);backdrop-filter:blur(18px);position:sticky;top:0;z-index:10}.masthead-row{display:flex;align-items:flex-end;justify-content:space-between;gap:30px}.brand{display:flex;align-items:center;gap:16px}.sigil{width:52px;height:52px;display:grid;place-items:center;border:1px solid rgba(233,168,63,.52);border-radius:15px;color:var(--gold);font:700 28px Georgia,serif;box-shadow:inset 0 0 24px rgba(233,168,63,.08)}h1,h2,h3,p{margin-top:0}h1{font:650 clamp(1.55rem,2.3vw,2.25rem) Georgia,serif;margin-bottom:3px;letter-spacing:.01em}.subtitle{margin:0;color:var(--muted);font-size:.93rem}.snapshot{text-align:right}.snapshot strong{display:block;color:var(--green);font-size:.9rem}.snapshot small{color:var(--muted)}.topnav{display:flex;gap:18px;margin-top:18px;overflow:auto}.topnav a{font-size:.78rem;color:var(--muted);text-transform:uppercase;letter-spacing:.12em;text-decoration:none;white-space:nowrap}.topnav a:hover,.topnav a:focus-visible{color:var(--ink)}main{padding:36px 0 76px}.hero-grid{display:grid;grid-template-columns:minmax(0,1.55fr) minmax(330px,.75fr);gap:22px}.panel{background:linear-gradient(145deg,rgba(24,32,27,.97),rgba(14,20,16,.98));border:1px solid var(--line);border-radius:var(--radius);box-shadow:var(--shadow)}.now{padding:30px;position:relative;overflow:hidden}.now:after{content:"";position:absolute;right:-90px;top:-90px;width:250px;height:250px;border-radius:50%;border:1px solid rgba(233,168,63,.15);box-shadow:0 0 0 34px rgba(233,168,63,.035),0 0 0 68px rgba(233,168,63,.025);pointer-events:none}.eyebrow{display:block;color:var(--gold);font-size:.68rem;font-weight:750;letter-spacing:.16em;text-transform:uppercase;margin-bottom:7px}.now h2,.section-head h2{font:600 clamp(1.35rem,2vw,1.8rem) Georgia,serif;margin-bottom:8px}.lede{color:var(--muted);max-width:70ch}.next-callout{border-left:3px solid var(--gold);padding:13px 16px;margin:22px 0;background:rgba(233,168,63,.07);border-radius:0 10px 10px 0}.next-callout strong{display:block}.next-callout span{color:var(--muted);font-size:.9rem}.progress-line{display:flex;align-items:center;gap:13px;margin-top:20px}.progress-track{height:7px;flex:1;background:#080b09;border-radius:99px;overflow:hidden}.progress-fill{height:100%;width:0;background:linear-gradient(90deg,var(--gold),var(--green));transition:width .2s ease}.progress-copy{color:var(--muted);font-size:.78rem;min-width:94px;text-align:right}.session-panel{padding:24px}.session-panel h2{font-size:1rem;margin-bottom:6px}.session-panel p{font-size:.84rem;color:var(--muted)}textarea{width:100%;min-height:145px;resize:vertical;background:#090d0b;border:1px solid var(--line);border-radius:10px;color:var(--ink);padding:12px;margin:9px 0}textarea:focus,select:focus,button:focus-visible,input:focus-visible,a:focus-visible{outline:2px solid var(--gold);outline-offset:3px}.button-row{display:flex;flex-wrap:wrap;gap:8px}.button{border:1px solid var(--line);border-radius:9px;background:var(--panel-3);color:var(--ink);padding:8px 11px;cursor:pointer;font-size:.78rem}.button:hover{border-color:var(--gold)}.button-danger{color:var(--red)}#session-status{display:block;min-height:1.4em;margin-top:9px;color:var(--green);font-size:.76rem}.section{margin-top:42px}.section-head{display:flex;align-items:end;justify-content:space-between;gap:20px;margin-bottom:18px}.section-head p{margin:0;color:var(--muted);max-width:72ch}.strategy-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:14px}.strategy-card{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:19px}.strategy-card h3{margin:0;font-size:1rem}.strategy-card p{color:var(--muted);font-size:.82rem;margin:7px 0}.readiness-panel{margin-top:14px;padding:20px}.readiness-list{margin:10px 0 0;padding-left:21px;columns:2;column-gap:42px}.readiness-list li{break-inside:avoid;margin-bottom:8px;color:var(--muted);font-size:.84rem}.recovery-grid{display:grid;grid-template-columns:minmax(0,1.45fr) minmax(300px,.65fr);gap:22px}.flow-panel{padding:24px}.flow-panel h3{margin-bottom:5px}.flow-panel>p{color:var(--muted);font-size:.9rem}.check-list{list-style:none;margin:20px 0 0;padding:0;display:grid;gap:10px}.check-item{display:grid;grid-template-columns:26px 1fr;gap:10px;padding:14px;border:1px solid var(--line);background:rgba(0,0,0,.13);border-radius:12px;transition:border-color .15s,opacity .15s}.check-item:has(input:checked){border-color:rgba(101,214,139,.42);opacity:.66}.check-item input,.session-confirm input{accent-color:var(--green);width:18px;height:18px;margin-top:3px}.check-item label{cursor:pointer}.check-item label>span{display:grid;gap:3px}.check-item small{color:var(--muted);line-height:1.45}.check-item:has(input:checked) label>span{text-decoration:line-through;text-decoration-color:rgba(101,214,139,.65)}.source-link{display:inline-block;margin-top:7px;font-size:.72rem;color:var(--gold-soft)}.checkpoint-c{margin-top:34px}.revision-proof{display:grid;grid-template-columns:repeat(2,1fr);gap:8px;margin-top:14px}.revision-proof span{padding:10px 12px;background:rgba(101,214,139,.05);border:1px solid rgba(101,214,139,.18);border-radius:9px;color:var(--muted);font-size:.78rem}.revision-proof strong{display:block;color:var(--green);font-size:.65rem;text-transform:uppercase;letter-spacing:.09em}.verdict-box{margin:14px 0 22px;padding:16px;border:1px solid rgba(201,167,255,.35);background:rgba(201,167,255,.06);border-radius:12px}.verdict-box label{display:block;font-weight:700;margin-bottom:9px}.verdict-box select{width:100%;background:#0b100d;color:var(--ink);border:1px solid var(--line);border-radius:9px;padding:9px}.proof-panel{padding:24px}.proof-panel h3{margin-bottom:4px}.proof-panel>p{color:var(--muted);font-size:.85rem}.proof-list{list-style:none;padding:0;margin:16px 0;display:grid;gap:7px}.proof-list li{display:grid;grid-template-columns:1fr auto;gap:10px;padding:9px 10px;background:#0b100d;border-radius:8px;border-left:2px solid var(--green)}.proof-list code{color:var(--ink)}.proof-list span{color:var(--green);font-size:.75rem;text-transform:uppercase;letter-spacing:.08em}.proof-list small{grid-column:1/-1;color:var(--red)}.guardrail{padding:13px;border-radius:10px;background:rgba(255,139,125,.07);border:1px solid rgba(255,139,125,.22);font-size:.83rem;color:#d8c7c0}.machine-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}.machine-card,.queue-card,.decision-card,.command-card{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:19px}.machine-card h3,.queue-card h3,.decision-card h3{margin:0;font-size:1rem}.machine-card>strong{font-size:.84rem}.machine-card p,.queue-card p,.decision-card p{color:var(--muted);font-size:.82rem;margin:7px 0}.session-confirm{display:flex;gap:8px;align-items:center;margin-top:15px;color:var(--muted);font-size:.76rem}.card-heading{display:flex;align-items:start;justify-content:space-between;gap:12px}.badge{display:inline-flex;align-items:center;border-radius:99px;padding:4px 8px;border:1px solid var(--line);font-size:.64rem;font-weight:750;text-transform:uppercase;letter-spacing:.08em;white-space:nowrap}.badge-complete,.badge-ready,.badge-confirmed,.badge-online{color:var(--green);border-color:rgba(101,214,139,.35);background:rgba(101,214,139,.07)}.badge-implemented{color:var(--blue);border-color:rgba(127,200,255,.35);background:rgba(127,200,255,.07)}.badge-active,.badge-human,.badge-attention{color:var(--gold-soft);border-color:rgba(233,168,63,.4);background:rgba(233,168,63,.08)}.badge-planned,.badge-gated,.badge-deferred,.badge-on-demand{color:var(--muted)}.environment{margin:16px 0 0;padding:18px 22px}.environment h3{font-size:.83rem;color:var(--muted);text-transform:uppercase;letter-spacing:.11em}.environment ul{list-style:none;padding:0;margin:0;display:grid;grid-template-columns:repeat(2,1fr);gap:14px 24px}.environment li{display:grid;grid-template-columns:11px 1fr;gap:10px}.environment strong{font-size:.82rem}.environment p{font-size:.77rem;color:var(--muted);margin:2px 0}.fact-kind{font-size:.62rem;text-transform:uppercase;color:var(--dim);letter-spacing:.09em}.status-dot{width:9px;height:9px;margin-top:6px;border-radius:50%;background:var(--dim);box-shadow:0 0 0 3px rgba(255,255,255,.03)}.status-confirmed{background:var(--green)}.status-automated,.status-implemented{background:var(--blue)}.status-attention{background:var(--gold)}.phase-list{list-style:none;padding:0;margin:0;display:grid;grid-template-columns:repeat(5,1fr);gap:1px;background:var(--line);border:1px solid var(--line);border-radius:var(--radius);overflow:hidden}.phase{display:grid;grid-template-rows:auto 1fr;background:var(--panel);min-width:0}.phase-rail{height:46px;display:flex;align-items:center;padding:0 18px;border-bottom:1px solid var(--line);position:relative}.phase-rail:after{content:"";height:2px;background:var(--line);position:absolute;left:47px;right:0}.phase:last-child .phase-rail:after{display:none}.phase-rail span{width:26px;height:26px;display:grid;place-items:center;border-radius:50%;background:var(--panel-3);border:1px solid var(--line);font-size:.76rem;z-index:1}.phase-complete .phase-rail span{background:var(--green);color:#07120b;border-color:var(--green)}.phase-active .phase-rail span{background:var(--gold);color:#1a1204;border-color:var(--gold);box-shadow:0 0 22px rgba(233,168,63,.24)}.phase-copy{padding:17px}.phase-copy h3{font-size:.9rem;margin:0}.phase-copy p{color:var(--muted);font-size:.77rem;margin:9px 0}.queue-grid{display:grid;grid-template-columns:repeat(2,1fr);gap:14px}.decision-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:14px}.decision-card small{display:block;color:var(--gold-soft);margin:10px 0;font-size:.76rem}.command-list{display:grid;gap:10px}.command-card{display:grid;grid-template-columns:minmax(200px,.55fr) 1fr;gap:18px;align-items:center}.command-card small{display:block;color:var(--muted);margin-top:3px}.command-row{display:flex;min-width:0}.command-row code{flex:1;overflow:auto;padding:10px;background:#080b09;border:1px solid var(--line);border-radius:9px 0 0 9px;color:var(--blue);white-space:nowrap}.copy-button{border:1px solid var(--line);border-left:0;background:var(--panel-3);color:var(--ink);border-radius:0 9px 9px 0;padding:0 13px;cursor:pointer}.copy-button:hover{color:var(--gold)}details.future{border:1px solid var(--line);border-radius:14px;background:var(--panel)}details.future summary{cursor:pointer;padding:19px;font-weight:700}details.future>div{padding:0 20px 22px}.derived-sequence,.reading-order{list-style:none;padding:0;margin:15px 0;display:grid;gap:9px}.derived-sequence li,.reading-order li{display:grid;grid-template-columns:28px 1fr;gap:9px}.derived-sequence li>span,.reading-order li>span{width:25px;height:25px;display:grid;place-items:center;border:1px solid var(--line);border-radius:50%;font-size:.7rem;color:var(--gold)}.derived-sequence p,.reading-order p{font-size:.82rem;color:var(--muted);margin:1px 0}.judgment-list{color:var(--muted);font-size:.84rem}.cautions{display:grid;grid-template-columns:repeat(2,1fr);gap:10px;margin:0;padding:0;list-style:none}.cautions li{padding:14px 16px;border-left:2px solid var(--red);background:rgba(255,139,125,.05);color:#cfcbc3;font-size:.82rem}.footer{border-top:1px solid var(--line);padding:27px 0 45px;color:var(--dim);font-size:.76rem}.footer-row{display:flex;justify-content:space-between;gap:20px}.local-only{color:var(--violet)}code{font-family:"Cascadia Code","SFMono-Regular",Consolas,monospace}body.compact .section:not(#recovery),body.compact .machine-grid,body.compact .environment{display:none}@media(max-width:1100px){.hero-grid,.recovery-grid{grid-template-columns:1fr}.strategy-grid{grid-template-columns:repeat(2,1fr)}.phase-list{grid-template-columns:1fr}.phase{grid-template-columns:50px 1fr;grid-template-rows:1fr}.phase-rail{height:100%;border:0;border-right:1px solid var(--line);padding:14px 11px}.phase-rail:after{width:2px;height:auto;top:45px;bottom:0;left:24px;right:auto}.machine-grid,.decision-grid{grid-template-columns:1fr}.queue-grid{grid-template-columns:1fr 1fr}}@media(max-width:720px){.shell{width:min(100% - 24px,1540px)}.masthead{position:static;padding-top:20px}.masthead-row,.section-head,.footer-row{align-items:start;flex-direction:column}.snapshot{text-align:left}.strategy-grid,.machine-grid,.queue-grid,.environment ul,.decision-grid,.cautions,.revision-proof{grid-template-columns:1fr}.readiness-list{columns:1}.command-card{grid-template-columns:1fr}.now,.session-panel,.flow-panel,.proof-panel{padding:19px}.topnav{padding-bottom:4px}}@media(prefers-reduced-motion:reduce){html{scroll-behavior:auto}.progress-fill{transition:none}}@media print{.masthead{position:static}.topnav,.session-panel,.button,.copy-button,.session-confirm{display:none!important}.panel,.strategy-card,.machine-card,.queue-card,.decision-card{box-shadow:none;break-inside:avoid}body{background:#fff;color:#111}.subtitle,.lede,p,small,.phase-copy p{color:#444!important}.shell{width:100%}}
'''

    js = f'''
(() => {{
  'use strict';
  const schema={json.dumps(SESSION_SCHEMA)};
  const pageId={json.dumps(page["id"])};
  const storageKey=`quest-mission-control.${{pageId}}.v2`;
  const status=document.querySelector('#session-status');
  const checks=[...document.querySelectorAll('[data-check-id]')];
  const knownIds=new Set(checks.map(item=>item.dataset.checkId));
  const notes=document.querySelector('#session-notes');
  const verdict=document.querySelector('#lane-verdict');
  let state={{schema,page_id:pageId,saved_at:null,checks:{{}},notes:'',seat_verdict:'unrecorded'}};
  let saveTimer=0;

  function announce(message,bad=false){{status.textContent=message;status.style.color=bad?'var(--red)':'var(--green)'}}
  function bounded(value,limit){{return typeof value==='string'?value.slice(0,limit):''}}
  function normalize(input){{
    if(!input||input.schema!==schema||input.page_id!==pageId)throw new Error('This session file belongs to another page or schema.');
    const next={{schema,page_id:pageId,saved_at:bounded(input.saved_at,64)||null,checks:{{}},notes:bounded(input.notes,20000),seat_verdict:['unrecorded','no-relay','needed-relay','mixed'].includes(input.seat_verdict)?input.seat_verdict:'unrecorded'}};
    if(input.checks&&typeof input.checks==='object')for(const [key,value] of Object.entries(input.checks))if(knownIds.has(key)&&value===true)next.checks[key]=true;
    return next;
  }}
  function apply(){{
    checks.forEach(item=>item.checked=state.checks[item.dataset.checkId]===true);
    notes.value=state.notes;
    verdict.value=state.seat_verdict;
    updateProgress();
  }}
  function updateProgress(){{
    const lane=checks.filter(item=>item.dataset.checkId==='recovery.cold-load'||item.dataset.checkId.startsWith('recovery.creator.')||item.dataset.checkId.startsWith('recovery.revision.'));
    const done=lane.filter(item=>item.checked).length;
    const percent=lane.length?Math.round(done/lane.length*100):0;
    document.querySelector('#lane-progress').style.width=`${{percent}}%`;
    const bar=document.querySelector('#lane-progressbar');
    bar.setAttribute('aria-valuenow',String(percent));
    document.querySelector('#progress-copy').textContent=`${{done}} of ${{lane.length}} checked`;
  }}
  function persist(message='Saved in this browser'){{
    clearTimeout(saveTimer);
    saveTimer=setTimeout(()=>{{
      state.saved_at=new Date().toISOString();
      try{{localStorage.setItem(storageKey,JSON.stringify(state));announce(message)}}catch(error){{announce('Browser storage is unavailable; export the session to retain it.',true)}}
    }},100);
  }}
  function load(){{
    try{{const raw=localStorage.getItem(storageKey);if(raw)state=normalize(JSON.parse(raw))}}catch(error){{announce('Saved browser state could not be read; starting clean.',true)}}
    apply();
  }}
  checks.forEach(item=>item.addEventListener('change',()=>{{if(item.checked)state.checks[item.dataset.checkId]=true;else delete state.checks[item.dataset.checkId];updateProgress();persist()}}));
  notes.addEventListener('input',()=>{{state.notes=bounded(notes.value,20000);persist()}});
  verdict.addEventListener('change',()=>{{state.seat_verdict=verdict.value;persist('Seat verdict saved in this browser')}});

  document.querySelector('#export-session').addEventListener('click',()=>{{
    state.saved_at=new Date().toISOString();
    const blob=new Blob([JSON.stringify(state,null,2)+'\\n'],{{type:'application/json'}});
    const link=document.createElement('a');link.href=URL.createObjectURL(blob);link.download=`quest-session-${{new Date().toISOString().slice(0,10)}}.json`;document.body.appendChild(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(link.href),0);announce('Session JSON exported');
  }});
  document.querySelector('#import-session').addEventListener('click',()=>document.querySelector('#session-file').click());
  document.querySelector('#session-file').addEventListener('change',async event=>{{
    const file=event.target.files&&event.target.files[0];event.target.value='';if(!file)return;
    if(file.size>262144){{announce('Session file is too large.',true);return}}
    try{{state=normalize(JSON.parse(await file.text()));apply();persist('Session imported and saved locally')}}catch(error){{announce(error.message||'Session import failed.',true)}}
  }});
  document.querySelector('#reset-session').addEventListener('click',()=>{{
    if(!confirm("Clear this browser's Creator OS checkmarks, verdict, and notes?"))return;
    state={{schema,page_id:pageId,saved_at:null,checks:{{}},notes:'',seat_verdict:'unrecorded'}};try{{localStorage.removeItem(storageKey)}}catch(error){{}}apply();announce('Local session cleared');
  }});
  document.querySelector('#focus-current').addEventListener('click',event=>{{document.body.classList.toggle('compact');event.currentTarget.textContent=document.body.classList.contains('compact')?'Show full program':'Focus current lane'}});

  async function copyText(value){{
    if(navigator.clipboard&&window.isSecureContext){{await navigator.clipboard.writeText(value);return}}
    const area=document.createElement('textarea');area.value=value;area.style.position='fixed';area.style.opacity='0';document.body.appendChild(area);area.select();const ok=document.execCommand('copy');area.remove();if(!ok)throw new Error('copy unavailable');
  }}
  document.querySelectorAll('[data-copy]').forEach(button=>button.addEventListener('click',async()=>{{try{{await copyText(button.dataset.copy);button.textContent='Copied';announce('Command copied');setTimeout(()=>button.textContent='Copy',1200)}}catch(error){{announce('Copy is unavailable; select the command manually.',true)}}}}));
  load();
}})();
'''

    rendered = f'''<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="dark">
<meta name="quest-mission-control-schema" content="{SCHEMA}">
<meta name="quest-mission-control-source" content="{source_hash}">
<title>{html.escape(page["title"])}</title>
<style>{css}</style>
</head>
<body>
<a class="skip-link" href="#main">Skip to mission control</a>
<header class="masthead"><div class="shell">
  <div class="masthead-row"><div class="brand"><div class="sigil" aria-hidden="true">Q</div><div><h1>{html.escape(page["title"])}</h1><p class="subtitle">{html.escape(page["subtitle"])}</p></div></div>
  <div class="snapshot"><strong>Program reconciled through {html.escape(page["program_commit"])}</strong><small>{html.escape(page["observed_on"])} · {html.escape(page["program_commit_label"])}</small></div></div>
  <nav class="topnav" aria-label="Mission control sections"><a href="#strategy">Strategy</a><a href="#build">Build proof</a><a href="#program">Roadmap</a><a href="#queue">Queue</a><a href="#machines">Machines</a><a href="#recovery">Prepared seat gate</a><a href="#decisions">Decisions</a><a href="#commands">Reference commands</a></nav>
</div></header>
<main id="main" class="shell">
  <div class="hero-grid">
    <section class="panel now" aria-labelledby="now-title"><span class="eyebrow">Now · full-width construction</span><h2 id="now-title">Build it. Drive it. Then call the seat.</h2><p class="lede">{html.escape(strategy["summary"])}</p>
      <div class="next-callout"><strong>Golden rule</strong><span>{html.escape(strategy["golden_rule"])}</span></div>
      <div class="progress-line"><div id="lane-progressbar" class="progress-track" role="progressbar" aria-label="Prepared seat-gate checklist progress" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0"><div id="lane-progress" class="progress-fill"></div></div><span id="progress-copy" class="progress-copy">0 checked</span></div>
    </section>
    <aside class="panel session-panel" aria-labelledby="session-title"><span class="eyebrow local-only">Private to this browser</span><h2 id="session-title">Session notebook</h2><p>Checkmarks, the cold-load verdict, and notes stay in localStorage. Export JSON when the observation should travel.</p><label for="session-notes" class="eyebrow">Notes</label><textarea id="session-notes" maxlength="20000" placeholder="Record exact reactions, blockers, and anything that produces ‘can’t answer why’." spellcheck="true"></textarea><div class="button-row"><button id="export-session" class="button" type="button">Export</button><button id="import-session" class="button" type="button">Import</button><button id="reset-session" class="button button-danger" type="button">Reset</button><button id="focus-current" class="button" type="button">Focus current lane</button><input id="session-file" type="file" accept="application/json,.json" hidden></div><span id="session-status" role="status" aria-live="polite"></span></aside>
  </div>

  <section id="strategy" class="section" aria-labelledby="strategy-title"><div class="section-head"><div><span class="eyebrow">Why and how</span><h2 id="strategy-title">{html.escape(strategy["title"])}</h2></div><p>{html.escape(strategy["summary"])}</p></div><div class="strategy-grid">{strategy_cards}</div><article class="panel readiness-panel"><div class="card-heading"><div><span class="eyebrow">Golden readiness gate</span><h3>{html.escape(strategy["readiness_title"])}</h3></div>{source_link(strategy["source"], "Operating strategy")}</div><ul class="readiness-list">{readiness_items}</ul></article></section>

  <section id="build" class="section" aria-labelledby="build-title"><div class="section-head"><div><span class="eyebrow">Latest autonomous attack</span><h2 id="build-title">The architectural handoff is operator-demo ready on AM4.</h2></div><p>The exact structure was built once; Studio and identity-matching R&amp;D laps now reuse it without paying teardown and rebuild costs.</p></div>{architectural_build_panel}</section>

  <section id="recovery" class="section" aria-labelledby="recovery-title"><div class="section-head"><div><span class="eyebrow">Use only after autonomous readiness</span><h2 id="recovery-title">{html.escape(manifest["recovery"]["title"])}</h2></div><p>{html.escape(manifest["recovery"]["summary"])}</p></div>
    <div class="recovery-grid"><article class="panel flow-panel"><span class="eyebrow">Checkpoint A · exact session precondition</span><h3>Prepare the acceptance session</h3><ul class="check-list">{cold_item}</ul>
      <div class="verdict-box"><label for="cold-verdict">{html.escape(cold["verdict"])}</label><select id="cold-verdict"><option value="unrecorded">Not recorded</option><option value="obvious">Yes · immediately obvious</option><option value="not-obvious">No · not immediately obvious</option><option value="mixed">Mixed · explain in notes</option></select></div>
      <span class="eyebrow">Checkpoint B · source-derived creator loop</span><h3>{html.escape(creator["title"])}</h3><p>{html.escape(creator["summary"])}</p><div class="guardrail"><strong>Precondition:</strong> begin the imported-fork path where its source says it begins. If recovery leaves the character elsewhere, record that state; do not invent a reset or silently skip the ascent beat.</div><ol class="check-list">{creator_items}</ol>
      <span class="eyebrow checkpoint-c">Checkpoint C · same fork, changed content</span><h3>{html.escape(revision["title"])}</h3><p>{html.escape(revision["summary"])}</p><div class="next-callout"><strong>Bounded suggested edit</strong><span>{html.escape(revision["suggested_edit"])}</span>{source_link(revision["edit_source"], "Project source")}</div><ol class="check-list">{revision_items}</ol>{revision_proof}
    </article>
    <aside class="panel proof-panel"><span class="eyebrow">Acceptance evidence</span><h3>The product’s expected proof chain</h3><p>These receipt assertions come directly from the checked-in Demo World contract. Run-specific identities are deliberately not pinned.</p><ol class="proof-list">{proof_rows}</ol>{source_link(expectations_relative, "Expectation contract")}<p class="guardrail">A checked box is not proof. Use Runtime receipts and the Studio cockpit for machine facts; preserve Derek’s exact words for the human judgment.</p></aside></div>
  </section>

  <section id="machines" class="section" aria-labelledby="machines-title"><div class="section-head"><div><span class="eyebrow">Lab topology</span><h2 id="machines-title">Machines and readiness</h2></div><p>Reported availability is separated from repository-owned role claims.</p></div><div class="machine-grid">{machine_cards}</div><article class="panel environment"><h3>Observed on {html.escape(page["observed_on"])}</h3><ul>{environment_cards}</ul></article></section>

  <section id="program" class="section" aria-labelledby="program-title"><div class="section-head"><div><span class="eyebrow">Five-intent program</span><h2 id="program-title">Guild dogfooding is the adoption path.</h2></div><p>Phase state is a cited program snapshot, not a live inference from checkboxes.</p></div><ol class="phase-list">{phases}</ol></section>

  <section id="queue" class="section" aria-labelledby="queue-title"><div class="section-head"><div><span class="eyebrow">Adoption path</span><h2 id="queue-title">Execute the Guild creative-system lap, then judge and release it</h2></div><p>The installed 4A loop is proven. The 4B steward, abstraction, campaign, and one-operation start are implemented; installed kills, successor completion, terminal evidence, reset/rerun, and cleanup/restoration remain open. Standalone portability is bounded follow-up, and world packaging waits until R&amp;D stabilizes.</p></div><div class="queue-grid">{queue_cards}</div>
    {phase3_panel}
  </section>

  <section id="decisions" class="section" aria-labelledby="decisions-title"><div class="section-head"><div><span class="eyebrow">Resolved direction</span><h2 id="decisions-title">Decisions in force</h2></div><p>These constraints keep machine work scalable and protect the only capacity that does not scale with hardware.</p></div><div class="decision-grid">{decision_cards}</div></section>

  <section id="commands" class="section" aria-labelledby="commands-title"><div class="section-head"><div><span class="eyebrow">Reference, not a seat primer</span><h2 id="commands-title">Machine-owned entrypoints</h2></div><p>These remain for automation and diagnostics. They are not a checklist Derek must relay. This page cannot arm Runtime, mutate Valheim, or execute repository tools.</p></div><div class="command-list">{command_cards}</div><p><a href="http://127.0.0.1:8085/quest-studio">Open Quest Studio on this machine</a> · <a href="../README.md">Repository README</a></p>
    <article class="panel flow-panel"><span class="eyebrow">Why any of this</span><h3>{html.escape(lanes["citation_rule"])}</h3><p>Every lane answers one question and fails one way. Cite a lane by slug, never by bare ordinal.</p><ol class="reading-order">{lane_items}</ol>{source_link("docs/creator-os-phases.json", "Lane vocabulary")}</article>
    <article class="panel flow-panel"><span class="eyebrow">Cold start</span><h3>Authority reading order</h3><p>One declared chain. A renamed authority or a stale pointer fails the drift gate rather than misleading the next reader.</p><ol class="reading-order">{reading_items}</ol></article></section>

  <section class="section" aria-labelledby="cautions-title"><div class="section-head"><div><span class="eyebrow">Do not relearn these</span><h2 id="cautions-title">Guardrails</h2></div></div><ul class="cautions">{caution_items}</ul></section>
</main>
<footer class="footer"><div class="shell footer-row"><span>Generated from tracked status and cited repository sources · manifest {source_hash}</span><span>Local session schema: {SESSION_SCHEMA}</span></div></footer>
<script>{js}</script>
</body></html>'''
    # Post-render substitutions, applied in order. Every group must match exactly the
    # recorded number of times: a table nobody asserts on is a table an ordinary template
    # edit disconnects silently (audit A3), and a generic key can also start matching
    # manifest text that was never meant to be rewritten (audit A4). Multiple spellings in
    # one group are encoding fallbacks for the same string, not separate rules.
    replacements: tuple[tuple[tuple[str, ...], str, int], ...] = (
        (('Checkmarks, the cold-load verdict, and notes',), 'Checkmarks, the seat-capacity verdict, and notes', 1),
        (('cold-verdict',), 'lane-verdict', 2),
        (('value="obvious"',), 'value="no-relay"', 1),
        (('value="not-obvious"',), 'value="needed-relay"', 1),
        (('Yes · immediately obvious', 'Yes � immediately obvious'), 'No manual relay needed', 1),
        (('No · not immediately obvious', 'No � not immediately obvious'), 'Manual relay was needed', 1),
        (('Mixed · explain in notes', 'Mixed � explain in notes'), 'Mixed; explain in notes', 1),
        (('Checkpoint B · source-derived creator loop', 'Checkpoint B � source-derived creator loop'), 'Checkpoint B · source-derived Creator Session', 1),
        (('begin the imported-fork path where its source says it begins. If recovery leaves the character elsewhere, record that state; do not invent a reset or silently skip the ascent beat.',), 'Prepare establishes every machine, world, session, install-hash, and backup precondition used by later steps. Do not substitute a human file relay or console command.', 1),
        (('Checkpoint C · same fork, changed content', 'Checkpoint C � same fork, changed content'), 'Checkpoint C · portfolio dogfood', 1),
        (('Bounded suggested edit',), 'Dogfood foundation gate', 1),
        (('Project source',), 'Operating source', 1),
        (('Acceptance evidence',), 'Machine evidence', 1),
        (('The product’s expected proof chain', 'The product�s expected proof chain'), 'The Creator OS expected proof chain', 1),
        (('These receipt assertions come directly from the checked-in Demo World contract. Run-specific identities are deliberately not pinned.',), 'These assertions come from the checked-in Creator OS contract. Run-specific identities and hashes are deliberately not pinned.', 1),
        (('Use Runtime receipts and the Studio cockpit for machine facts; preserve Derek’s exact words for the human judgment.', 'Use Runtime receipts and the Studio cockpit for machine facts; preserve Derek�s exact words for the human judgment.'), 'Use correlated receipts, installed hashes, capture authority, and MATCH for machine facts; preserve exact seat observations only for human judgment.', 1),
        (('‘can’t answer why’', '�can�t answer why�'), "'can't answer why'", 1),
    )
    for spellings, replacement, expected in replacements:
        matched = sum(rendered.count(spelling) for spelling in spellings)
        if matched != expected:
            raise MissionControlError(
                f"post-render replacement {spellings[0]!r} matched {matched} times, "
                f"expected {expected}: the template no longer emits it, or manifest text "
                "now collides with it"
            )
        for spelling in spellings:
            rendered = rendered.replace(spelling, replacement)
    return rendered.replace("\ufffd", "·")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="fail if the committed HTML is stale")
    parser.add_argument("--out", type=Path, default=OUTPUT, help="render to another path")
    args = parser.parse_args(argv)
    try:
        output = args.out.resolve()
        if args.check:
            rendered = render(load_manifest())
            if not output.is_file() or output.read_text(encoding="utf-8") != rendered:
                print(f"STALE: {output}; run {Path(__file__).name}", file=sys.stderr)
                return 1
            print(f"OK: {output.relative_to(REPO) if output.is_relative_to(REPO) else output}")
            return 0
        assert_repo_identity()
        value = load_manifest(validate_projection_state=False)
        if output == OUTPUT.resolve():
            write_projections(value)
            validate_manifest(value)
        rendered = render(value)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(rendered, encoding="utf-8", newline="\n")
        print(f"Wrote {output.relative_to(REPO) if output.is_relative_to(REPO) else output}")
        return 0
    except MissionControlError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Parse Quest Lab event archives into privacy-safe reports and spreadsheet exports."""

from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import re
import sys
import zipfile
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable, Sequence
from xml.sax.saxutils import escape as xml_escape


ARCHIVE_SCHEMA = "comfy-questlab-events/v1"
REPORT_SCHEMA = "comfy-questlab-event-report/v1"
SCHOOL_ORDER = (
    "combat",
    "harvest",
    "inventory",
    "building",
    "crafting",
    "progression",
    "world",
    "social",
)
CSV_HEADER = (
    "schema",
    "session_id",
    "sequence",
    "timestamp_utc",
    "school",
    "creator_event",
    "target",
    "detail",
    "usability",
    "diagnostic_seam",
    "action_identity",
)
MAX_FILE_BYTES = 128 * 1024 * 1024
MAX_TOTAL_BYTES = 512 * 1024 * 1024
MAX_FILES = 256
MAX_RECORDS = 1_000_000
MAX_SPREADSHEET_ROWS = 25_000
MAX_WORKBOOK_EXPANDED_BYTES = 64 * 1024 * 1024
MAX_BUNDLE_EXPANDED_BYTES = 96 * 1024 * 1024
TOKEN_RE = re.compile(r"^[a-z][a-z0-9_]{0,127}$")
PRIVATE_FIELD_KEYS = {
    "address",
    "charactername",
    "chattext",
    "ip",
    "message",
    "playerid",
    "playername",
    "position",
    "server",
    "serveraddress",
    "signtext",
    "steamid",
    "text",
    "worldname",
    "zdoid",
}


class EventExportError(RuntimeError):
    """A user-actionable archive or export failure."""


@dataclass
class EventRecord:
    timestamp: datetime
    timestamp_utc: str
    school: str
    creator_event: str
    target: str
    usability: str
    session_id: str
    sequence: int | None
    action_identity: str
    detail: str
    diagnostic_seam: str
    fields: dict[str, str]
    release_id: str
    source_ordinal: int
    line_number: int
    raw_witness_count: int = 1
    mirror_count: int = 1
    redacted_fields: int = 0


@dataclass
class ReadResult:
    records: list[EventRecord] = field(default_factory=list)
    session_headers: dict[str, dict[str, Any]] = field(default_factory=dict)
    session_ends: dict[str, dict[str, Any]] = field(default_factory=dict)
    archive_notices: list[dict[str, Any]] = field(default_factory=list)
    input_files: int = 0
    duplicate_input_records: int = 0
    truncated_tail_records_ignored: int = 0


def _normalized_key(value: Any) -> str:
    return re.sub(r"[^a-z0-9]", "", str(value).lower())


def _lookup(row: dict[str, Any], *names: str, default: Any = None) -> Any:
    wanted = {_normalized_key(name) for name in names}
    for key, value in row.items():
        if _normalized_key(key) in wanted:
            return value
    return default


def _text(value: Any, label: str, maximum: int, required: bool = False) -> str:
    if value is None:
        value = ""
    if not isinstance(value, str):
        value = str(value)
    if required and not value.strip():
        raise EventExportError(f"{label} is missing")
    if len(value) > maximum:
        raise EventExportError(f"{label} exceeds {maximum} characters")
    return value


def parse_timestamp(value: Any, label: str) -> tuple[datetime, str]:
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        try:
            parsed = datetime.fromtimestamp(float(value), tz=timezone.utc)
        except (OverflowError, OSError, ValueError) as exc:
            raise EventExportError(f"{label} is not a valid Unix timestamp") from exc
    else:
        raw = _text(value, label, 128, required=True).strip()
        if re.fullmatch(r"\d{2}:\d{2}:\d{2}(?:\.\d+)?", raw):
            raise EventExportError(
                f"{label} is time-only; Quest Lab exports require an ISO-8601 date and timezone"
            )
        try:
            parsed = datetime.fromisoformat(raw[:-1] + "+00:00" if raw.endswith("Z") else raw)
        except ValueError as exc:
            raise EventExportError(f"{label} is not ISO-8601") from exc
        if parsed.tzinfo is None:
            raise EventExportError(f"{label} has no timezone")
        parsed = parsed.astimezone(timezone.utc)
    rendered = parsed.isoformat(timespec="microseconds").replace("+00:00", "Z")
    return parsed, rendered


def _positive_count(value: Any, label: str, default: int = 1) -> int:
    if value in (None, ""):
        return default
    if isinstance(value, bool):
        raise EventExportError(f"{label} must be a positive integer")
    try:
        parsed = int(value)
    except (TypeError, ValueError) as exc:
        raise EventExportError(f"{label} must be a positive integer") from exc
    if parsed < 1:
        raise EventExportError(f"{label} must be a positive integer")
    return parsed


def _nonnegative_count(value: Any, label: str) -> int:
    if isinstance(value, bool):
        raise EventExportError(f"{label} must be a non-negative integer")
    try:
        parsed = int(value)
    except (TypeError, ValueError) as exc:
        raise EventExportError(f"{label} must be a non-negative integer") from exc
    if parsed < 0:
        raise EventExportError(f"{label} must be a non-negative integer")
    return parsed


def _optional_sequence(value: Any, label: str, strict: bool) -> int | None:
    if value in (None, ""):
        if strict:
            raise EventExportError(f"{label} is missing")
        return None
    if isinstance(value, bool):
        raise EventExportError(f"{label} must be a positive integer")
    try:
        sequence = int(value)
    except (TypeError, ValueError) as exc:
        raise EventExportError(f"{label} must be a positive integer") from exc
    if sequence < 1:
        raise EventExportError(f"{label} must be a positive integer")
    return sequence


def _privacy_fields(value: Any, include_private: bool, label: str) -> tuple[dict[str, str], int]:
    if value in (None, ""):
        return {}, 0
    if isinstance(value, str):
        try:
            value = json.loads(value)
        except json.JSONDecodeError as exc:
            raise EventExportError(f"{label} is not a JSON object") from exc
    if not isinstance(value, dict):
        raise EventExportError(f"{label} must be an object")
    output: dict[str, str] = {}
    redacted = 0
    for key, item in value.items():
        name = _text(key, f"{label} field name", 128, required=True)
        if _normalized_key(name) in PRIVATE_FIELD_KEYS and not include_private:
            redacted += 1
            continue
        if isinstance(item, (dict, list)):
            rendered = json.dumps(item, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
        elif item is None:
            rendered = ""
        elif isinstance(item, bool):
            rendered = "true" if item else "false"
        else:
            rendered = str(item)
        output[name] = _text(rendered, f"{label}.{name}", 4096)
    return dict(sorted(output.items(), key=lambda pair: pair[0].lower())), redacted


def _schema(row: dict[str, Any]) -> str:
    return _text(_lookup(row, "schema"), "schema", 128)


def normalize_event(
    row: dict[str, Any],
    *,
    source_ordinal: int,
    line_number: int,
    strict: bool,
    session_header: dict[str, Any] | None,
    include_private: bool,
) -> EventRecord:
    if strict:
        csv_shape = "creator_event" in row or "timestamp_utc" in row
        required_fields = (
            ("schema", "session_id", "sequence", "timestamp_utc", "school", "creator_event", "target", "usability")
            if csv_shape
            else ("schema", "recordType", "sessionId", "sequence", "timestampUtc", "school", "creatorEvent", "target", "usability")
        )
        missing = [name for name in required_fields if name not in row]
        if missing:
            raise EventExportError("strict event record is missing " + ", ".join(missing))
    schema = _schema(row)
    if strict and schema != ARCHIVE_SCHEMA:
        raise EventExportError(f"schema must be {ARCHIVE_SCHEMA!r}, got {schema or 'missing'!r}")
    if schema and schema != ARCHIVE_SCHEMA:
        raise EventExportError(f"unsupported schema {schema!r}")

    timestamp_value = _lookup(
        row,
        "timestampUtc",
        "timestamp_utc",
        "observedUtc",
        "observed_utc",
        "atUtc",
        "at_utc",
        "timestamp",
        "at",
    )
    timestamp, timestamp_utc = parse_timestamp(timestamp_value, "timestampUtc")
    school = _text(_lookup(row, "school", "category"), "school", 64, required=True).strip().lower()
    if school not in SCHOOL_ORDER:
        raise EventExportError(f"school {school!r} is not one of {', '.join(SCHOOL_ORDER)}")

    event_value = _lookup(row, "creatorEvent", "creator_event", "eventName", "event_name")
    if event_value in (None, ""):
        candidate = _lookup(row, "event")
        if isinstance(candidate, dict):
            event_value = _lookup(candidate, "name", "creatorEvent", "creator_event")
        else:
            event_value = candidate
    creator_event = _text(event_value, "creatorEvent", 128, required=True).strip().lower()
    if not TOKEN_RE.fullmatch(creator_event):
        raise EventExportError("creatorEvent must be a lower-case creator event token")

    target = _text(_lookup(row, "target", "subject"), "target", 1024).strip()
    usability = _text(_lookup(row, "usability", default="unknown"), "usability", 64).strip() or "unknown"
    session_id = _text(
        _lookup(row, "sessionId", "session_id", "runId", "run_id")
        or (session_header or {}).get("sessionId"),
        "sessionId",
        256,
        required=strict,
    ).strip()
    sequence = _optional_sequence(_lookup(row, "sequence", "seq"), "sequence", strict)
    action_identity = _text(
        _lookup(
            row,
            "actionIdentity",
            "action_identity",
            "stableActionId",
            "stable_action_id",
            "actionId",
            "action_id",
            "dedupeKey",
            "dedupe_key",
            "actionKey",
            "action_key",
        ),
        "actionIdentity",
        1024,
    ).strip()
    detail = _text(_lookup(row, "detail", "description"), "detail", 4096)
    diagnostic_seam = _text(
        _lookup(row, "diagnosticSeam", "diagnostic_seam", "seam"),
        "diagnosticSeam",
        512,
    )
    fields_value = _lookup(row, "fields", "fieldsJson", "fields_json")
    fields, redacted = _privacy_fields(fields_value, include_private, "fields")
    for name in ("weapon_skill", "projectile"):
        direct = _lookup(row, name)
        if direct not in (None, "") and name not in fields:
            fields[name] = str(direct).lower() if isinstance(direct, bool) else str(direct)
    raw_count_value = _lookup(row, "rawWitnessCount", "raw_witness_count", "rawCount", "raw_count")
    if raw_count_value in (None, ""):
        coalesced = _lookup(row, "coalescedWitnessCount", "coalesced_witness_count")
        raw_count_value = int(coalesced) + 1 if coalesced not in (None, "") else 1
    raw_witness_count = _positive_count(raw_count_value, "rawWitnessCount")
    release_id = _text(
        _lookup(row, "releaseId", "release_id") or (session_header or {}).get("releaseId"),
        "releaseId",
        256,
    ).strip()
    return EventRecord(
        timestamp=timestamp,
        timestamp_utc=timestamp_utc,
        school=school,
        creator_event=creator_event,
        target=target,
        usability=usability,
        session_id=session_id,
        sequence=sequence,
        action_identity=action_identity,
        detail=detail,
        diagnostic_seam=diagnostic_seam,
        fields=fields,
        release_id=release_id,
        source_ordinal=source_ordinal,
        line_number=line_number,
        raw_witness_count=raw_witness_count,
        redacted_fields=redacted,
    )


def validate_session(row: dict[str, Any], strict: bool) -> dict[str, Any]:
    if strict:
        required = ("schema", "recordType", "sessionId", "startedUtc", "releaseId", "segment", "fields")
        missing = [name for name in required if name not in row]
        if missing:
            raise EventExportError("strict session record is missing " + ", ".join(missing))
    schema = _schema(row)
    if strict and schema != ARCHIVE_SCHEMA:
        raise EventExportError(f"session schema must be {ARCHIVE_SCHEMA!r}")
    if schema and schema != ARCHIVE_SCHEMA:
        raise EventExportError(f"unsupported session schema {schema!r}")
    session_id = _text(_lookup(row, "sessionId", "session_id"), "sessionId", 256, required=True).strip()
    started_value = _lookup(row, "startedUtc", "started_utc")
    _, started_utc = parse_timestamp(started_value, "startedUtc")
    release_id = _text(_lookup(row, "releaseId", "release_id"), "releaseId", 256, required=strict).strip()
    segment_value = _lookup(row, "segment")
    segment = _positive_count(segment_value, "segment") if segment_value not in (None, "") else None
    if strict and segment is None:
        raise EventExportError("segment is missing")
    privacy = _lookup(row, "fields", default={})
    if privacy is None:
        privacy = {}
    if not isinstance(privacy, dict):
        raise EventExportError("session fields must be an object")
    if strict:
        for required in ("details", "diagnosticIdentity"):
            value = _lookup(privacy, required)
            if not isinstance(value, bool):
                raise EventExportError(f"session fields.{required} must be boolean")
    return {
        "sessionId": session_id,
        "startedUtc": started_utc,
        "releaseId": release_id,
        "segment": segment,
        "fields": privacy,
    }


def validate_session_end(row: dict[str, Any], strict: bool) -> dict[str, Any]:
    if strict:
        required = ("schema", "recordType", "sessionId", "endedUtc", "eventCount", "segments", "reason")
        missing = [name for name in required if name not in row]
        if missing:
            raise EventExportError("strict sessionEnd record is missing " + ", ".join(missing))
    schema = _schema(row)
    if strict and schema != ARCHIVE_SCHEMA:
        raise EventExportError(f"sessionEnd schema must be {ARCHIVE_SCHEMA!r}")
    if schema and schema != ARCHIVE_SCHEMA:
        raise EventExportError(f"unsupported sessionEnd schema {schema!r}")
    session_id = _text(_lookup(row, "sessionId", "session_id"), "sessionId", 256, required=True).strip()
    _, ended_utc = parse_timestamp(_lookup(row, "endedUtc", "ended_utc"), "endedUtc")
    event_count = _nonnegative_count(_lookup(row, "eventCount", "event_count"), "eventCount")
    segments = _positive_count(_lookup(row, "segments"), "segments")
    reason = _text(_lookup(row, "reason"), "reason", 128, required=True).strip()
    if strict and reason != "clean-shutdown":
        raise EventExportError("sessionEnd reason must be 'clean-shutdown'")
    return {
        "sessionId": session_id,
        "endedUtc": ended_utc,
        "eventCount": event_count,
        "segments": segments,
        "reason": reason,
    }


def validate_archive_notice(row: dict[str, Any], strict: bool) -> dict[str, Any]:
    if strict:
        required = (
            "schema",
            "recordType",
            "sessionId",
            "timestampUtc",
            "reason",
            "droppedSinceLastNotice",
            "totalDroppedEventCount",
        )
        missing = [name for name in required if name not in row]
        if missing:
            raise EventExportError("strict archiveNotice record is missing " + ", ".join(missing))
    schema = _schema(row)
    if strict and schema != ARCHIVE_SCHEMA:
        raise EventExportError(f"archiveNotice schema must be {ARCHIVE_SCHEMA!r}")
    if schema and schema != ARCHIVE_SCHEMA:
        raise EventExportError(f"unsupported archiveNotice schema {schema!r}")
    session_id = _text(_lookup(row, "sessionId", "session_id"), "sessionId", 256, required=True).strip()
    _, timestamp_utc = parse_timestamp(_lookup(row, "timestampUtc", "timestamp_utc"), "timestampUtc")
    reason = _text(_lookup(row, "reason"), "reason", 128, required=True).strip()
    if strict and reason != "queue-capacity":
        raise EventExportError("archiveNotice reason must be 'queue-capacity'")
    dropped = _positive_count(
        _lookup(row, "droppedSinceLastNotice", "dropped_since_last_notice"),
        "droppedSinceLastNotice",
    )
    total = _positive_count(
        _lookup(row, "totalDroppedEventCount", "total_dropped_event_count"),
        "totalDroppedEventCount",
    )
    if total < dropped:
        raise EventExportError("totalDroppedEventCount cannot be smaller than droppedSinceLastNotice")
    return {
        "sessionId": session_id,
        "timestampUtc": timestamp_utc,
        "reason": reason,
        "droppedSinceLastNotice": dropped,
        "totalDroppedEventCount": total,
    }


def _headers_compatible(left: dict[str, Any], right: dict[str, Any]) -> bool:
    keys = ("sessionId", "startedUtc", "releaseId", "fields")
    return all(left.get(key) == right.get(key) for key in keys)


def _file_guard(path: Path) -> None:
    try:
        size = path.stat().st_size
    except OSError as exc:
        raise EventExportError(f"{path.name}: cannot read file metadata: {exc}") from exc
    if size > MAX_FILE_BYTES:
        raise EventExportError(f"{path.name}: exceeds the {MAX_FILE_BYTES // (1024 * 1024)} MiB input limit")


def read_jsonl(
    path: Path,
    source_ordinal: int,
    strict: bool,
    include_private: bool,
    allow_truncated_tail: bool,
) -> tuple[
    list[EventRecord],
    dict[str, dict[str, Any]],
    dict[str, dict[str, Any]],
    list[dict[str, Any]],
    int,
]:
    _file_guard(path)
    records: list[EventRecord] = []
    headers: dict[str, dict[str, Any]] = {}
    endings: dict[str, dict[str, Any]] = {}
    notices: list[dict[str, Any]] = []
    active_header: dict[str, Any] | None = None
    saw_record = False
    truncated_tail_records = 0
    try:
        stream = path.open("r", encoding="utf-8-sig", newline="")
    except OSError as exc:
        raise EventExportError(f"{path.name}: cannot open: {exc}") from exc
    with stream:
        for line_number, raw in enumerate(stream, 1):
            if not raw.strip():
                continue
            try:
                row = json.loads(raw)
            except json.JSONDecodeError as exc:
                if allow_truncated_tail and not raw.endswith(("\n", "\r")) and stream.read() == "":
                    truncated_tail_records = 1
                    break
                raise EventExportError(
                    f"{path.name}:{line_number}: invalid JSON at column {exc.colno}: {exc.msg}"
                ) from exc
            if not isinstance(row, dict):
                raise EventExportError(f"{path.name}:{line_number}: record must be an object")
            record_type = _text(_lookup(row, "recordType", "record_type"), "recordType", 32).strip().lower()
            if not saw_record and strict and record_type != "session":
                raise EventExportError(f"{path.name}:{line_number}: first record must be a session header")
            saw_record = True
            try:
                if record_type == "session":
                    active_header = validate_session(row, strict)
                    previous = headers.get(active_header["sessionId"])
                    if previous and not _headers_compatible(previous, active_header):
                        raise EventExportError("session header disagrees with another segment")
                    headers[active_header["sessionId"]] = active_header
                    continue
                if record_type == "sessionend":
                    ending = validate_session_end(row, strict)
                    if active_header and ending["sessionId"] != active_header["sessionId"]:
                        raise EventExportError("sessionEnd sessionId disagrees with its session header")
                    previous = endings.get(ending["sessionId"])
                    if previous and previous != ending:
                        raise EventExportError("sessionEnd disagrees with another segment")
                    endings[ending["sessionId"]] = ending
                    continue
                if record_type == "archivenotice":
                    notice = validate_archive_notice(row, strict)
                    if active_header and notice["sessionId"] != active_header["sessionId"]:
                        raise EventExportError("archiveNotice sessionId disagrees with its session header")
                    notices.append(notice)
                    continue
                if record_type not in ("", "event"):
                    raise EventExportError(f"unsupported recordType {record_type!r}")
                if strict and record_type != "event":
                    raise EventExportError("recordType must be 'event'")
                event = normalize_event(
                    row,
                    source_ordinal=source_ordinal,
                    line_number=line_number,
                    strict=strict,
                    session_header=active_header,
                    include_private=include_private,
                )
                if active_header and event.session_id != active_header["sessionId"]:
                    raise EventExportError("event sessionId disagrees with its session header")
                records.append(event)
            except EventExportError as exc:
                raise EventExportError(f"{path.name}:{line_number}: {exc}") from exc
    if strict and not saw_record:
        raise EventExportError(f"{path.name}: archive is empty")
    if strict and active_header is None:
        raise EventExportError(f"{path.name}: session header is missing")
    return records, headers, endings, notices, truncated_tail_records


def read_csv_file(
    path: Path, source_ordinal: int, strict: bool, include_private: bool
) -> list[EventRecord]:
    _file_guard(path)
    try:
        stream = path.open("r", encoding="utf-8-sig", newline="")
    except OSError as exc:
        raise EventExportError(f"{path.name}: cannot open: {exc}") from exc
    records: list[EventRecord] = []
    with stream:
        reader = csv.DictReader(stream)
        if reader.fieldnames is None:
            raise EventExportError(f"{path.name}: CSV header is missing")
        if len(set(reader.fieldnames)) != len(reader.fieldnames):
            raise EventExportError(f"{path.name}: CSV header contains duplicate columns")
        if strict and tuple(reader.fieldnames) != CSV_HEADER:
            raise EventExportError(
                f"{path.name}: strict CSV header must be exactly {','.join(CSV_HEADER)}"
            )
        for line_number, row in enumerate(reader, 2):
            if None in row:
                raise EventExportError(f"{path.name}:{line_number}: row has more values than the header")
            if not any(value not in (None, "") for value in row.values()):
                continue
            try:
                records.append(
                    normalize_event(
                        row,
                        source_ordinal=source_ordinal,
                        line_number=line_number,
                        strict=strict,
                        session_header=None,
                        include_private=include_private,
                    )
                )
            except EventExportError as exc:
                raise EventExportError(f"{path.name}:{line_number}: {exc}") from exc
    return records


def collect_paths(inputs: Iterable[Path]) -> list[Path]:
    found: list[Path] = []
    for path in inputs:
        if path.is_dir():
            found.extend(path.glob("questlab-events*.jsonl"))
            found.extend(path.glob("questlab-events*.csv"))
        elif path.is_file():
            if path.suffix.lower() not in (".jsonl", ".csv"):
                raise EventExportError(f"unsupported input extension: {path.name}")
            found.append(path)
        else:
            raise EventExportError(f"input does not exist: {path}")
    unique = {path.resolve(): path.resolve() for path in found}
    paths = sorted(unique.values(), key=lambda item: item.name.lower())
    if len(paths) > MAX_FILES:
        raise EventExportError(f"input expands to {len(paths)} files; limit is {MAX_FILES}")
    total_bytes = sum(path.stat().st_size for path in paths)
    if total_bytes > MAX_TOTAL_BYTES:
        raise EventExportError(
            f"input totals {total_bytes} bytes; limit is {MAX_TOTAL_BYTES // (1024 * 1024)} MiB"
        )
    return paths


def _mirror_key(row: EventRecord) -> tuple[str, int] | None:
    if row.session_id and row.sequence is not None:
        return row.session_id, row.sequence
    return None


def _mirror_signature(row: EventRecord) -> tuple[Any, ...]:
    return (
        row.timestamp_utc,
        row.school,
        row.creator_event,
        row.target,
        row.usability,
        row.action_identity,
        row.raw_witness_count,
    )


def _mirrors_compatible(left: EventRecord, right: EventRecord) -> bool:
    if _mirror_signature(left) != _mirror_signature(right):
        return False
    for left_value, right_value in (
        (left.fields, right.fields),
        (left.detail, right.detail),
        (left.diagnostic_seam, right.diagnostic_seam),
    ):
        if left_value and right_value and left_value != right_value:
            return False
    return True


def _prefer_richer(left: EventRecord, right: EventRecord) -> EventRecord:
    left_score = bool(left.detail) + bool(left.diagnostic_seam) + len(left.fields) + bool(left.release_id)
    right_score = bool(right.detail) + bool(right.diagnostic_seam) + len(right.fields) + bool(right.release_id)
    chosen, other = (right, left) if right_score > left_score else (left, right)
    chosen.mirror_count = left.mirror_count + right.mirror_count
    chosen.redacted_fields = max(left.redacted_fields, right.redacted_fields)
    if not chosen.release_id:
        chosen.release_id = other.release_id
    if not chosen.detail:
        chosen.detail = other.detail
    if not chosen.diagnostic_seam:
        chosen.diagnostic_seam = other.diagnostic_seam
    if other.fields:
        chosen.fields = dict(sorted({**other.fields, **chosen.fields}.items()))
    return chosen


def read_inputs(
    inputs: Iterable[Path],
    strict: bool = False,
    include_private: bool = False,
    allow_truncated_tail: bool = False,
) -> ReadResult:
    paths = collect_paths(inputs)
    if not paths:
        raise EventExportError("no Quest Lab JSONL or CSV archives found")
    result = ReadResult(input_files=len(paths))
    by_witness: dict[tuple[str, int], EventRecord] = {}
    unkeyed: list[EventRecord] = []
    for source_ordinal, path in enumerate(paths, 1):
        if path.suffix.lower() == ".jsonl":
            records, headers, endings, notices, truncated = read_jsonl(
                path, source_ordinal, strict, include_private, allow_truncated_tail
            )
            result.truncated_tail_records_ignored += truncated
            result.archive_notices.extend(notices)
            for session_id, header in headers.items():
                existing = result.session_headers.get(session_id)
                if existing and not _headers_compatible(existing, header):
                    raise EventExportError(f"{path.name}: session header disagrees with another segment")
                if existing:
                    seen = set(existing.get("seenSegments", []))
                    if existing.get("segment") is not None:
                        seen.add(existing["segment"])
                    if header.get("segment") is not None:
                        seen.add(header["segment"])
                    existing["seenSegments"] = sorted(seen)
                else:
                    result.session_headers[session_id] = header
            for session_id, ending in endings.items():
                existing = result.session_ends.get(session_id)
                if existing and existing != ending:
                    raise EventExportError(f"{path.name}: sessionEnd disagrees with another segment")
                result.session_ends[session_id] = ending
        else:
            records = read_csv_file(path, source_ordinal, strict, include_private)
        for row in records:
            if len(by_witness) + len(unkeyed) >= MAX_RECORDS:
                raise EventExportError(f"input exceeds the {MAX_RECORDS:,}-record limit")
            key = _mirror_key(row)
            if key is None:
                unkeyed.append(row)
                continue
            previous = by_witness.get(key)
            if previous is None:
                by_witness[key] = row
                continue
            if not _mirrors_compatible(previous, row):
                safe = identity_hash(row.session_id, str(row.sequence), row.creator_event)
                raise EventExportError(
                    f"witness identity collision {safe}: session/sequence payloads disagree"
                )
            by_witness[key] = _prefer_richer(previous, row)
            result.duplicate_input_records += 1
    result.records = list(by_witness.values()) + unkeyed
    result.records.sort(
        key=lambda row: (
            row.timestamp,
            row.session_id,
            row.sequence if row.sequence is not None else 2**63 - 1,
            row.source_ordinal,
            row.line_number,
        )
    )
    if not result.records:
        raise EventExportError("archives contain no event records")
    return result


def identity_hash(*parts: str) -> str:
    digest = hashlib.sha256("\x00".join(parts).encode("utf-8")).hexdigest()[:20]
    return "sha256:" + digest


def parse_filter_time(value: str | None, label: str) -> datetime | None:
    if value is None:
        return None
    parsed, _ = parse_timestamp(value, label)
    return parsed


def filter_records(
    records: Sequence[EventRecord],
    *,
    schools: set[str] | None = None,
    events: set[str] | None = None,
    target: str | None = None,
    since: datetime | None = None,
    until: datetime | None = None,
) -> list[EventRecord]:
    schools = {item.lower() for item in schools or set()}
    events = {item.lower() for item in events or set()}
    needle = (target or "").lower()
    if schools - set(SCHOOL_ORDER):
        raise EventExportError(f"unknown school filter(s): {', '.join(sorted(schools - set(SCHOOL_ORDER)))}")
    if since and until and since > until:
        raise EventExportError("--since must be earlier than or equal to --until")
    return [
        row
        for row in records
        if (not schools or row.school in schools)
        and (not events or row.creator_event in events)
        and (not needle or needle in row.target.lower())
        and (since is None or row.timestamp >= since)
        and (until is None or row.timestamp <= until)
    ]


def _action_key(row: EventRecord) -> tuple[str, ...]:
    if row.action_identity:
        return "stable", row.session_id, row.creator_event, row.action_identity
    if row.session_id and row.sequence is not None:
        return "witness", row.session_id, str(row.sequence)
    return "source", str(row.source_ordinal), str(row.line_number)


def _action_hash(row: EventRecord) -> str:
    key = _action_key(row)
    return identity_hash(*key)


def _session_hash(session_id: str) -> str:
    return identity_hash("session", session_id) if session_id else ""


def _fields_json(fields: dict[str, str]) -> str:
    return json.dumps(fields, sort_keys=True, ensure_ascii=False, separators=(",", ":")) if fields else "{}"


def coalesce(records: Sequence[EventRecord], include_diagnostics: bool = False) -> list[dict[str, Any]]:
    groups: dict[tuple[str, ...], list[EventRecord]] = defaultdict(list)
    for row in records:
        groups[_action_key(row)].append(row)
    actions: list[dict[str, Any]] = []
    for rows in groups.values():
        rows.sort(key=lambda item: (item.timestamp, item.source_ordinal, item.line_number))
        first = rows[0]
        expected = (first.school, first.creator_event, first.target, _fields_json(first.fields))
        for row in rows[1:]:
            actual = (row.school, row.creator_event, row.target, _fields_json(row.fields))
            if actual != expected:
                raise EventExportError(
                    f"stable action identity collision {_action_hash(first)}: canonical payloads disagree"
                )
        raw_witnesses = sum(row.raw_witness_count for row in rows)
        release_ids = sorted({row.release_id for row in rows if row.release_id})
        action: dict[str, Any] = {
            "timestamp_utc": first.timestamp_utc,
            "last_timestamp_utc": rows[-1].timestamp_utc,
            "school": first.school,
            "creator_event": first.creator_event,
            "target": first.target,
            "fields_json": _fields_json(first.fields),
            "usability": first.usability,
            "raw_witnesses": raw_witnesses,
            "coalesced_witnesses": max(0, raw_witnesses - 1),
            "source_records": len(rows),
            "action_id": _action_hash(first),
            "session_id": _session_hash(first.session_id),
            "release_id": release_ids[0] if len(release_ids) == 1 else ("mixed" if release_ids else ""),
        }
        if include_diagnostics:
            action["detail"] = first.detail
            action["diagnostic_seams"] = " | ".join(
                sorted({row.diagnostic_seam for row in rows if row.diagnostic_seam})
            )
        actions.append(action)
    actions.sort(key=lambda item: (item["timestamp_utc"], item["creator_event"], item["action_id"]))
    return actions


def normalized_witnesses(records: Sequence[EventRecord], include_diagnostics: bool) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for item in records:
        row: dict[str, Any] = {
            "timestamp_utc": item.timestamp_utc,
            "school": item.school,
            "creator_event": item.creator_event,
            "target": item.target,
            "fields_json": _fields_json(item.fields),
            "usability": item.usability,
            "raw_witnesses": item.raw_witness_count,
            "action_id": _action_hash(item),
            "session_id": _session_hash(item.session_id),
            "witness_id": identity_hash(
                "witness",
                item.session_id,
                str(item.sequence) if item.sequence is not None else str(item.source_ordinal),
                str(item.line_number) if item.sequence is None else "",
            ),
        }
        if include_diagnostics:
            row["detail"] = item.detail
            row["diagnostic_seam"] = item.diagnostic_seam
        rows.append(row)
    return rows


def summarize_actions(actions: Sequence[dict[str, Any]], key_name: str) -> list[dict[str, Any]]:
    grouped: dict[tuple[str, ...], list[dict[str, Any]]] = defaultdict(list)
    for action in actions:
        key = (action["school"],) if key_name == "school" else (action["school"], action["creator_event"])
        grouped[key].append(action)
    output: list[dict[str, Any]] = []
    for key, rows in grouped.items():
        row: dict[str, Any] = {"school": key[0]}
        if key_name != "school":
            row["creator_event"] = key[1]
        row.update(
            {
                "canonical_actions": len(rows),
                "raw_witnesses": sum(item["raw_witnesses"] for item in rows),
                "coalesced_witnesses": sum(item["coalesced_witnesses"] for item in rows),
                "distinct_targets": len({item["target"] for item in rows}),
                "first_seen_utc": min(item["timestamp_utc"] for item in rows),
                "last_seen_utc": max(item["last_timestamp_utc"] for item in rows),
            }
        )
        output.append(row)
    output.sort(
        key=lambda row: (
            SCHOOL_ORDER.index(row["school"]),
            row.get("creator_event", ""),
        )
    )
    return output


def combined_summary(
    actions: Sequence[dict[str, Any]],
    event_summary: Sequence[dict[str, Any]],
    school_summary: Sequence[dict[str, Any]],
) -> list[dict[str, Any]]:
    raw = sum(row["raw_witnesses"] for row in actions)
    total = {
        "level": "total",
        "school": "all",
        "creator_event": "all",
        "canonical_actions": len(actions),
        "raw_witnesses": raw,
        "coalesced_witnesses": max(0, raw - len(actions)),
        "distinct_targets": len({row["target"] for row in actions}),
        "first_seen_utc": min((row["timestamp_utc"] for row in actions), default=""),
        "last_seen_utc": max((row["last_timestamp_utc"] for row in actions), default=""),
    }
    schools = [dict({"level": "school", "creator_event": "all"}, **row) for row in school_summary]
    events = [dict({"level": "event"}, **row) for row in event_summary]
    return [total] + schools + events


def metadata_rows(report: dict[str, Any]) -> list[dict[str, str]]:
    totals = report["totals"]
    values: list[tuple[str, Any]] = [
        ("report_schema", report["schema"]),
        ("archive_schema", report["archive_schema"]),
        ("generated_utc", report["generated_utc"]),
        ("first_seen_utc", totals["first_seen_utc"] or ""),
        ("last_seen_utc", totals["last_seen_utc"] or ""),
        ("input_files", totals["input_files"]),
        ("input_event_records", totals["input_event_records"]),
        ("duplicate_input_records_ignored", totals["duplicate_input_records_ignored"]),
        ("truncated_tail_records_ignored", totals["truncated_tail_records_ignored"]),
        ("archive_notices", totals["archive_notices"]),
        ("dropped_event_count", totals["dropped_event_count"]),
        ("data_loss_detected", str(totals["data_loss_detected"]).lower()),
        ("filtered_event_records", totals["filtered_event_records"]),
        ("raw_witnesses", totals["raw_witnesses"]),
        ("canonical_actions", totals["canonical_actions"]),
        ("coalesced_witnesses", totals["coalesced_witnesses"]),
        ("sessions", totals["sessions"]),
        ("clean_shutdown_sessions", totals["clean_shutdown_sessions"]),
        ("sessions_without_end_record_in_inputs", totals["sessions_without_end_record_in_inputs"]),
        ("release_ids", ",".join(report["release_ids"])),
        ("filters_json", json.dumps(report["filters"], sort_keys=True, ensure_ascii=False, separators=(",", ":"))),
        ("raw_action_identities_exported", "false"),
        ("raw_session_ids_exported", "false"),
        ("source_paths_exported", "false"),
        ("diagnostics_included", str(report["privacy"]["diagnostics_included"]).lower()),
        ("private_fields_included", str(report["privacy"]["private_fields_included"]).lower()),
        ("redacted_field_values", report["privacy"]["redacted_field_values"]),
    ]
    return [{"key": key, "value": str(value)} for key, value in values]


def build_report(
    read: ReadResult,
    records: Sequence[EventRecord],
    *,
    filters: dict[str, Any],
    include_diagnostics: bool = False,
    include_private: bool = False,
) -> dict[str, Any]:
    actions = coalesce(records, include_diagnostics)
    witnesses = normalized_witnesses(records, include_diagnostics)
    raw_witnesses = sum(item["raw_witnesses"] for item in actions)
    event_summary = summarize_actions(actions, "event")
    school_summary = summarize_actions(actions, "school")
    releases = sorted({row.release_id for row in records if row.release_id})
    sessions = {_session_hash(row.session_id) for row in records if row.session_id}
    completed_sessions = {
        _session_hash(session_id)
        for session_id in read.session_ends
        if _session_hash(session_id) in sessions
    }
    dropped_by_session: dict[str, int] = {}
    for notice in read.archive_notices:
        alias = _session_hash(notice["sessionId"])
        dropped_by_session[alias] = max(
            dropped_by_session.get(alias, 0), notice["totalDroppedEventCount"]
        )
    totals = {
        "input_files": read.input_files,
        "input_event_records": len(read.records),
        "duplicate_input_records_ignored": read.duplicate_input_records,
        "truncated_tail_records_ignored": read.truncated_tail_records_ignored,
        "archive_notices": len(read.archive_notices),
        "dropped_event_count": sum(dropped_by_session.values()),
        "data_loss_detected": bool(read.archive_notices or read.truncated_tail_records_ignored),
        "filtered_event_records": len(records),
        "raw_witnesses": raw_witnesses,
        "canonical_actions": len(actions),
        "coalesced_witnesses": max(0, raw_witnesses - len(actions)),
        "distinct_events": len({item["creator_event"] for item in actions}),
        "distinct_schools": len({item["school"] for item in actions}),
        "sessions": len(sessions),
        "clean_shutdown_sessions": len(completed_sessions),
        "sessions_without_end_record_in_inputs": max(0, len(sessions) - len(completed_sessions)),
        "first_seen_utc": min((item["timestamp_utc"] for item in actions), default=None),
        "last_seen_utc": max((item["last_timestamp_utc"] for item in actions), default=None),
    }
    report = {
        "schema": REPORT_SCHEMA,
        "generated_utc": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
        "archive_schema": ARCHIVE_SCHEMA,
        "release_ids": releases,
        "filters": filters,
        "totals": totals,
        "event_summary": event_summary,
        "school_summary": school_summary,
        "summary": combined_summary(actions, event_summary, school_summary),
        "actions": actions,
        "witnesses": witnesses,
        "privacy": {
            "raw_action_identities": False,
            "raw_session_ids": False,
            "source_paths": False,
            "diagnostics_included": include_diagnostics,
            "private_fields_included": include_private,
            "redacted_field_values": sum(row.redacted_fields for row in records),
        },
    }
    report["metadata"] = metadata_rows(report)
    return report


ACTION_COLUMNS = (
    "timestamp_utc",
    "last_timestamp_utc",
    "school",
    "creator_event",
    "target",
    "fields_json",
    "usability",
    "raw_witnesses",
    "coalesced_witnesses",
    "source_records",
    "action_id",
    "session_id",
    "release_id",
)
WITNESS_COLUMNS = (
    "timestamp_utc",
    "school",
    "creator_event",
    "target",
    "fields_json",
    "usability",
    "raw_witnesses",
    "action_id",
    "session_id",
    "witness_id",
)
EVENT_SUMMARY_COLUMNS = (
    "school",
    "creator_event",
    "canonical_actions",
    "raw_witnesses",
    "coalesced_witnesses",
    "distinct_targets",
    "first_seen_utc",
    "last_seen_utc",
)
SCHOOL_SUMMARY_COLUMNS = (
    "school",
    "canonical_actions",
    "raw_witnesses",
    "coalesced_witnesses",
    "distinct_targets",
    "first_seen_utc",
    "last_seen_utc",
)
SUMMARY_COLUMNS = (
    "level",
    "school",
    "creator_event",
    "canonical_actions",
    "raw_witnesses",
    "coalesced_witnesses",
    "distinct_targets",
    "first_seen_utc",
    "last_seen_utc",
)
METADATA_COLUMNS = ("key", "value")


def _columns(report: dict[str, Any], view: str) -> tuple[str, ...]:
    diagnostics = report["privacy"]["diagnostics_included"]
    if view == "actions":
        return ACTION_COLUMNS + (("detail", "diagnostic_seams") if diagnostics else ())
    if view == "witnesses":
        return WITNESS_COLUMNS + (("detail", "diagnostic_seam") if diagnostics else ())
    if view == "event-summary":
        return EVENT_SUMMARY_COLUMNS
    if view == "school-summary":
        return SCHOOL_SUMMARY_COLUMNS
    if view == "summary":
        return SUMMARY_COLUMNS
    if view == "metadata":
        return METADATA_COLUMNS
    raise EventExportError(f"unknown table view {view!r}")


def _rows(report: dict[str, Any], view: str) -> list[dict[str, Any]]:
    return {
        "actions": report["actions"],
        "witnesses": report["witnesses"],
        "event-summary": report["event_summary"],
        "school-summary": report["school_summary"],
        "summary": report["summary"],
        "metadata": report["metadata"],
    }[view]


def spreadsheet_size_estimate(report: dict[str, Any], bundle: bool = False) -> int:
    """Conservative expanded-size estimate before building any in-memory XML/CSV."""
    views = ("actions", "summary", "metadata", "witnesses")
    cell_count = 0
    payload_bytes = 0
    for view in views:
        columns = _columns(report, view)
        rows = _rows(report, view)
        cell_count += len(columns) * (len(rows) + 1)
        payload_bytes += sum(len(name.encode("utf-8")) for name in columns)
        for row in rows:
            for name in columns:
                payload_bytes += len(str(row.get(name, "")).encode("utf-8"))
    # Cell tags, references, XML escaping, relationships, styles, and ZIP staging all add
    # overhead beyond payload. A bundle also materializes CSV and JSON projections.
    workbook = 512 * 1024 + payload_bytes + cell_count * 96
    return workbook + payload_bytes * 3 + cell_count * 32 if bundle else workbook


def validate_spreadsheet_bounds(report: dict[str, Any], bundle: bool = False) -> None:
    for label, view in (
        ("Events", "actions"),
        ("Summary", "summary"),
        ("Metadata", "metadata"),
        ("Raw Witnesses", "witnesses"),
    ):
        count = len(_rows(report, view))
        if count > MAX_SPREADSHEET_ROWS:
            raise EventExportError(
                f"{label} has {count:,} rows; spreadsheet limit is {MAX_SPREADSHEET_ROWS:,}. "
                "Filter the archive or use --format csv/json."
            )
    estimate = spreadsheet_size_estimate(report, bundle)
    limit = MAX_BUNDLE_EXPANDED_BYTES if bundle else MAX_WORKBOOK_EXPANDED_BYTES
    if estimate > limit:
        kind = "bundle" if bundle else "workbook"
        raise EventExportError(
            f"estimated expanded {kind} size is {estimate:,} bytes; limit is {limit:,}. "
            "Filter the archive or use --format csv/json."
        )


def spreadsheet_safe(value: Any) -> Any:
    if not isinstance(value, str) or not value:
        return value
    stripped = value.lstrip(" \t\r\n")
    if value[0] in "\t\r\n" or (stripped and stripped[0] in "=+-@"):
        return "'" + value
    return value


def render_csv(report: dict[str, Any], view: str = "actions") -> str:
    buffer = io.StringIO(newline="")
    columns = _columns(report, view)
    writer = csv.DictWriter(buffer, fieldnames=columns, extrasaction="ignore", lineterminator="\r\n")
    writer.writeheader()
    for row in _rows(report, view):
        writer.writerow({key: spreadsheet_safe(row.get(key, "")) for key in columns})
    return buffer.getvalue()


def render_summary(report: dict[str, Any]) -> str:
    totals = report["totals"]
    lines = [
        "Quest Lab event export",
        f"  {totals['canonical_actions']} canonical actions from {totals['raw_witnesses']} raw witnesses",
        f"  {totals['coalesced_witnesses']} duplicate witnesses coalesced; {totals['duplicate_input_records_ignored']} mirrored input rows ignored",
        f"  {totals['distinct_events']} creator events across {totals['distinct_schools']} schools and {totals['sessions']} sessions",
        f"  range: {totals['first_seen_utc'] or 'none'} to {totals['last_seen_utc'] or 'none'}",
    ]
    if report["filters"]:
        lines.append("  filters: " + json.dumps(report["filters"], sort_keys=True, ensure_ascii=False))
    if totals["data_loss_detected"]:
        lines.append(
            f"  DATA LOSS FLAG: {totals['dropped_event_count']} queue-dropped events; "
            f"{totals['truncated_tail_records_ignored']} crash-tail record ignored"
        )
    lines.append("")
    lines.append("School        Actions  Raw  Coalesced")
    for row in report["school_summary"]:
        lines.append(
            f"{row['school']:<13} {row['canonical_actions']:>7} {row['raw_witnesses']:>4} {row['coalesced_witnesses']:>10}"
        )
    return "\n".join(lines) + "\n"


def _xlsx_cell(reference: str, value: Any, style: int = 0) -> str:
    style_attr = f' s="{style}"' if style else ""
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return f'<c r="{reference}"{style_attr}><v>{value}</v></c>'
    rendered = str(spreadsheet_safe("" if value is None else value))
    rendered = "".join(ch for ch in rendered if ch in "\t\n\r" or ord(ch) >= 32)
    if len(rendered) > 32000:
        rendered = rendered[:31980] + "... [truncated]"
    return (
        f'<c r="{reference}"{style_attr} t="inlineStr"><is><t xml:space="preserve">'
        + xml_escape(rendered, {'"': "&quot;"})
        + "</t></is></c>"
    )


def _column_name(index: int) -> str:
    output = ""
    while index:
        index, remainder = divmod(index - 1, 26)
        output = chr(65 + remainder) + output
    return output


def _worksheet(columns: Sequence[str], rows: Sequence[dict[str, Any]], widths: Sequence[float]) -> str:
    last = _column_name(len(columns))
    xml_rows: list[str] = []
    header = "".join(_xlsx_cell(f"{_column_name(i)}1", name, 1) for i, name in enumerate(columns, 1))
    xml_rows.append(f'<row r="1">{header}</row>')
    for row_number, row in enumerate(rows, 2):
        cells = "".join(
            _xlsx_cell(f"{_column_name(column_number)}{row_number}", row.get(name, ""))
            for column_number, name in enumerate(columns, 1)
        )
        xml_rows.append(f'<row r="{row_number}">{cells}</row>')
    cols = "".join(
        f'<col min="{index}" max="{index}" width="{width}" customWidth="1"/>'
        for index, width in enumerate(widths, 1)
    )
    bottom = max(1, len(rows) + 1)
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
        f'<dimension ref="A1:{last}{bottom}"/><sheetViews><sheetView workbookViewId="0">'
        '<pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/>'
        '</sheetView></sheetViews><sheetFormatPr defaultRowHeight="15"/>'
        f'<cols>{cols}</cols><sheetData>{"".join(xml_rows)}</sheetData>'
        f'<autoFilter ref="A1:{last}{bottom}"/></worksheet>'
    )


def make_xlsx(report: dict[str, Any]) -> bytes:
    validate_spreadsheet_bounds(report)
    readme_rows = [
        {"topic": "Quest Lab Event Workbook", "value": "Upload or open this .xlsx in Google Sheets; no add-on, macro, or credential is required."},
        {"topic": "Events", "value": "One row per stable creator action. raw_witnesses preserves local/RPC or overload witness volume."},
        {"topic": "Raw Witnesses", "value": "One row per unique archived sequence; paired JSONL/CSV mirrors are counted once."},
        {"topic": "Privacy", "value": "Action/session identities are SHA-256 aliases; paths are omitted; diagnostic text is opt-in."},
        {"topic": "Formula safety", "value": "User-controlled cells that could begin a spreadsheet formula are forced to literal text."},
        {"topic": "Archive schema", "value": report["archive_schema"]},
        {"topic": "Report schema", "value": report["schema"]},
        {"topic": "Generated UTC", "value": report["generated_utc"]},
    ]
    sheets: list[tuple[str, tuple[str, ...], list[dict[str, Any]], tuple[float, ...]]] = [
        ("Events", _columns(report, "actions"), report["actions"], (24, 24, 13, 27, 25, 35, 15, 14, 18, 14, 29, 29, 26, 35, 35)),
        ("Summary", SUMMARY_COLUMNS, report["summary"], (12, 13, 27, 18, 15, 20, 18, 24, 24)),
        ("Metadata", METADATA_COLUMNS, report["metadata"], (38, 105)),
        ("Raw Witnesses", _columns(report, "witnesses"), report["witnesses"], (24, 13, 27, 25, 35, 15, 14, 29, 29, 29, 35, 35)),
        ("Read Me", ("topic", "value"), readme_rows, (25, 105)),
    ]
    content_types = [
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>',
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">',
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>',
        '<Default Extension="xml" ContentType="application/xml"/>',
        '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>',
        '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>',
        '<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>',
    ]
    for index in range(1, len(sheets) + 1):
        content_types.append(
            f'<Override PartName="/xl/worksheets/sheet{index}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
        )
    content_types.append("</Types>")
    workbook_sheets = "".join(
        f'<sheet name="{xml_escape(name)}" sheetId="{index}" r:id="rId{index}"/>'
        for index, (name, _, _, _) in enumerate(sheets, 1)
    )
    workbook_rels = "".join(
        f'<Relationship Id="rId{index}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{index}.xml"/>'
        for index in range(1, len(sheets) + 1)
    ) + (
        f'<Relationship Id="rId{len(sheets) + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>'
    )
    timestamp = report["generated_utc"]
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("[Content_Types].xml", "".join(content_types))
        archive.writestr(
            "_rels/.rels",
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
            '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>'
            "</Relationships>",
        )
        archive.writestr(
            "docProps/core.xml",
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" '
            'xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" '
            'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">'
            '<dc:title>Quest Lab Event Export</dc:title><dc:creator>Quest Lab</dc:creator>'
            f'<dcterms:created xsi:type="dcterms:W3CDTF">{xml_escape(timestamp)}</dcterms:created>'
            "</cp:coreProperties>",
        )
        archive.writestr(
            "xl/workbook.xml",
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
            'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
            f"<sheets>{workbook_sheets}</sheets></workbook>",
        )
        archive.writestr(
            "xl/_rels/workbook.xml.rels",
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            + workbook_rels
            + "</Relationships>",
        )
        archive.writestr(
            "xl/styles.xml",
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
            '<fonts count="2"><font><sz val="11"/><name val="Aptos"/></font>'
            '<font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Aptos"/></font></fonts>'
            '<fills count="3"><fill><patternFill patternType="none"/></fill>'
            '<fill><patternFill patternType="gray125"/></fill>'
            '<fill><patternFill patternType="solid"><fgColor rgb="FF17324D"/><bgColor indexed="64"/></patternFill></fill></fills>'
            '<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>'
            '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
            '<cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>'
            '<xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/></cellXfs>'
            '<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>'
            "</styleSheet>",
        )
        for index, (_, columns, rows, widths) in enumerate(sheets, 1):
            archive.writestr(f"xl/worksheets/sheet{index}.xml", _worksheet(columns, rows, widths[: len(columns)]))
    return output.getvalue()


def make_bundle(report: dict[str, Any]) -> bytes:
    validate_spreadsheet_bounds(report, bundle=True)
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("questlab-events.xlsx", make_xlsx(report))
        for filename, view in (
            ("events.csv", "actions"),
            ("summary.csv", "summary"),
            ("metadata.csv", "metadata"),
            ("raw-witnesses.csv", "witnesses"),
        ):
            archive.writestr(f"tables/{filename}", render_csv(report, view).encode("utf-8-sig"))
        archive.writestr(
            "questlab-events.json",
            (json.dumps(report, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8"),
        )
        archive.writestr(
            "README.txt",
            (
                "QUEST LAB / GOOGLE SHEETS\n\n"
                "Upload questlab-events.xlsx to Google Sheets for the complete multi-tab workbook.\n"
                "The tables directory contains UTF-8 CSV fallbacks. No macro, script, network call,\n"
                "API key, or Google credential is present. Raw action/session identities and source\n"
                "paths are not exported. Spreadsheet-formula prefixes are forced to literal text.\n"
            ).encode("utf-8"),
        )
    return output.getvalue()


def _split_filters(values: Sequence[str] | None) -> set[str]:
    return {
        item.strip().lower()
        for value in values or []
        for item in value.split(",")
        if item.strip()
    }


def write_binary(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", type=Path, nargs="+", help="JSONL/CSV archives or archive directories")
    parser.add_argument("--strict", action="store_true", help="require the comfy-questlab-events/v1 archive contract")
    parser.add_argument(
        "--allow-truncated-tail",
        action="store_true",
        help="skip one crash-truncated final JSONL line and report that omission",
    )
    parser.add_argument("--school", action="append", help="school filter; repeat or comma-separate")
    parser.add_argument("--event", action="append", help="creator-event filter; repeat or comma-separate")
    parser.add_argument("--target", help="case-insensitive target substring")
    parser.add_argument("--since", help="inclusive ISO-8601 lower bound")
    parser.add_argument("--until", help="inclusive ISO-8601 upper bound")
    parser.add_argument("--include-diagnostics", action="store_true", help="include detail and low-level seam columns")
    parser.add_argument("--include-private-fields", action="store_true", help="explicitly retain private-looking fields")
    parser.add_argument("--format", choices=("summary", "json", "csv"), default="summary")
    parser.add_argument(
        "--csv-view",
        choices=("actions", "witnesses", "summary", "metadata", "event-summary", "school-summary"),
        default="actions",
    )
    parser.add_argument("--output", type=Path, help="write the selected format instead of stdout")
    parser.add_argument("--sheets", type=Path, help="also write a Google-Sheets-compatible .xlsx workbook")
    parser.add_argument("--bundle", type=Path, help="also write a .zip with workbook, CSV tables, JSON, and import guide")
    args = parser.parse_args(argv)
    try:
        since = parse_filter_time(args.since, "--since")
        until = parse_filter_time(args.until, "--until")
        schools = _split_filters(args.school)
        events = _split_filters(args.event)
        read = read_inputs(
            args.inputs,
            args.strict,
            args.include_private_fields,
            args.allow_truncated_tail,
        )
        filtered = filter_records(
            read.records,
            schools=schools,
            events=events,
            target=args.target,
            since=since,
            until=until,
        )
        filters = {
            key: value
            for key, value in {
                "schools": sorted(schools) or None,
                "events": sorted(events) or None,
                "target_contains": args.target or None,
                "since_utc": since.isoformat().replace("+00:00", "Z") if since else None,
                "until_utc": until.isoformat().replace("+00:00", "Z") if until else None,
            }.items()
            if value is not None
        }
        report = build_report(
            read,
            filtered,
            filters=filters,
            include_diagnostics=args.include_diagnostics,
            include_private=args.include_private_fields,
        )
        if args.format == "json":
            rendered = json.dumps(report, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
        elif args.format == "csv":
            rendered = render_csv(report, args.csv_view)
        else:
            rendered = render_summary(report)
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            encoding = "utf-8-sig" if args.format == "csv" else "utf-8"
            args.output.write_text(rendered, encoding=encoding, newline="")
        else:
            print(rendered, end="")
        if args.sheets:
            if args.sheets.suffix.lower() != ".xlsx":
                raise EventExportError("--sheets output must end in .xlsx")
            write_binary(args.sheets, make_xlsx(report))
        if args.bundle:
            if args.bundle.suffix.lower() != ".zip":
                raise EventExportError("--bundle output must end in .zip")
            write_binary(args.bundle, make_bundle(report))
    except EventExportError as exc:
        print(f"questlab-events: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

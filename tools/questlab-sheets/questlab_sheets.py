#!/usr/bin/env python3
"""Local-first Quest Lab event parser and optional Google Sheets exporter.

The parser and CSV export use only the Python standard library. Google export is
loaded lazily and exists only when the operator has deliberately installed the
optional Google client libraries and supplied a Desktop OAuth client file.
"""

from __future__ import annotations

import argparse
import csv
import ctypes
import hashlib
import html
import io
import json
import os
import re
import secrets
import sys
import tempfile
import threading
import urllib.error
import urllib.parse
import urllib.request
import unicodedata
import webbrowser
from collections import Counter
from dataclasses import dataclass
from datetime import datetime, timezone
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any, Iterator, Mapping, Sequence


SCHEMA = "comfy-questlab-events/v1"
DRIVE_FILE_SCOPE = "https://www.googleapis.com/auth/drive.file"
SHEETS_PORT = 47631
SHEETS_HOME = f"http://127.0.0.1:{SHEETS_PORT}/"
MAX_PARTS = 128
MAX_PART_BYTES = 64 * 1024 * 1024
MAX_EVENTS = 250_000
MAX_LINE_BYTES = 256 * 1024
MAX_GOOGLE_REQUEST_BYTES = 1_500_000
CSV_COLUMNS = (
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
SESSION_FILE = re.compile(
    r"^questlab-events-(?P<session>[A-Za-z0-9][A-Za-z0-9._-]{0,95}?)"
    r"(?:-part(?P<part>[0-9]{3}))?\.jsonl$"
)
SAFE_SHEET_ID = re.compile(r"^[A-Za-z0-9_-]{10,256}$")
ALLOWED_AUTH_URIS = {
    "https://accounts.google.com/o/oauth2/auth",
    "https://accounts.google.com/o/oauth2/v2/auth",
}
GOOGLE_TOKEN_URI = "https://oauth2.googleapis.com/token"
GOOGLE_REVOKE_URI = "https://oauth2.googleapis.com/revoke"


class QuestLabSheetsError(RuntimeError):
    """A user-actionable, token-safe failure."""


class _RecordDecodeError(QuestLabSheetsError):
    """A JSON/UTF-8 decode failure that may be a crash-truncated final line."""


@dataclass(frozen=True)
class EventRow:
    schema: str
    session_id: str
    sequence: int
    timestamp_utc: str
    school: str
    creator_event: str
    target: str
    detail: str
    usability: str
    diagnostic_seam: str
    action_identity: str

    def values(self) -> list[Any]:
        return [
            self.schema,
            self.session_id,
            self.sequence,
            self.timestamp_utc,
            self.school,
            self.creator_event,
            self.target,
            self.detail,
            self.usability,
            self.diagnostic_seam,
            self.action_identity,
        ]


@dataclass(frozen=True)
class EventSession:
    session_id: str
    started_utc: str
    release_id: str
    runtime_profile: str
    include_details: bool
    include_diagnostic_identity: bool
    events: tuple[EventRow, ...]
    source_files: tuple[str, ...]
    source_sha256: str
    archive_state: str
    ended_utc: str
    end_reason: str
    dropped_event_count: int
    archive_notice_count: int
    declared_event_count: int | None
    declared_segments: int | None
    observed_segments: tuple[int, ...]
    crash_tail: bool
    warnings: tuple[str, ...]

    @property
    def title(self) -> str:
        stamp = self.started_utc.replace("T", " ").replace("Z", " UTC")
        if len(stamp) > 25:
            stamp = stamp[:25]
        short_id = self.session_id[:18]
        return _safe_title(f"Quest Lab events - {stamp} - {short_id}")


@dataclass(frozen=True)
class SessionListing:
    key: str
    session: EventSession | None
    error: str = ""
    source_files: tuple[str, ...] = ()


@dataclass(frozen=True)
class ExportResult:
    spreadsheet_id: str
    spreadsheet_url: str
    rows_written: int
    values_requests: int
    receipt_path: str


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


def default_state_dir() -> Path:
    if os.name == "nt":
        root = os.environ.get("LOCALAPPDATA")
        if not root:
            raise QuestLabSheetsError("LOCALAPPDATA is unavailable; pass --state-dir explicitly")
        return Path(root) / "ComfyQuestLab" / "google-sheets"
    root = os.environ.get("XDG_STATE_HOME")
    return (Path(root) if root else Path.home() / ".local" / "state") / "comfy-quest-lab" / "google-sheets"


def default_events_dir() -> Path:
    configured = os.environ.get("QUESTLAB_EVENT_ARCHIVE")
    if configured:
        return Path(configured)
    if os.name == "nt":
        return Path(r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\config\comfy-quest-lab\event-archive")
    return Path.home() / ".config" / "comfy-quest-lab" / "event-archive"


def _safe_title(value: str) -> str:
    cleaned = " ".join(value.replace("\n", " ").replace("\r", " ").split())
    return cleaned[:100] or "Quest Lab events"


def _required_text(record: Mapping[str, Any], key: str, context: str) -> str:
    value = record.get(key)
    if not isinstance(value, str) or not value.strip():
        raise QuestLabSheetsError(f"{context}: {key} must be a non-empty string")
    return value.strip()


def _present_text(record: Mapping[str, Any], key: str, context: str) -> str:
    if key not in record or not isinstance(record[key], str):
        raise QuestLabSheetsError(f"{context}: {key} must be a string")
    return str(record[key])


def _parse_record(line: bytes, context: str) -> Mapping[str, Any]:
    if len(line) > MAX_LINE_BYTES:
        raise QuestLabSheetsError(f"{context}: row exceeds {MAX_LINE_BYTES} bytes")
    try:
        value = json.loads(line.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise _RecordDecodeError(f"{context}: invalid UTF-8 JSON: {exc}") from None
    if not isinstance(value, dict):
        raise QuestLabSheetsError(f"{context}: each JSONL row must be an object")
    if value.get("schema") != SCHEMA:
        raise QuestLabSheetsError(f"{context}: expected schema {SCHEMA}")
    return value


def parse_session(paths: Sequence[Path]) -> EventSession:
    if not paths:
        raise QuestLabSheetsError("no event session files were selected")
    if len(paths) > MAX_PARTS:
        raise QuestLabSheetsError(f"session has more than the {MAX_PARTS}-part safety limit")

    header: Mapping[str, Any] | None = None
    events: list[EventRow] = []
    seen_sequences: set[int] = set()
    session_id = ""
    runtime_profile = ""
    observed_segments: list[int] = []
    archive_notice_count = 0
    notice_dropped_total = 0
    session_end: Mapping[str, Any] | None = None
    crash_tail = False
    warnings: list[str] = []
    source_digest = hashlib.sha256()

    for path_index, path in enumerate(paths):
        if not path.is_file():
            raise QuestLabSheetsError(f"event part is missing: {path.name}")
        if path.stat().st_size > MAX_PART_BYTES:
            raise QuestLabSheetsError(f"{path.name}: exceeds the {MAX_PART_BYTES // (1024 * 1024)} MiB safety limit")
        source_digest.update(path.name.encode("utf-8"))
        source_digest.update(b"\0")
        part_had_header = False
        part_had_event = False
        with path.open("rb") as source:
            for line_number, line in enumerate(source, start=1):
                source_digest.update(line)
                if not line.strip():
                    continue
                context = f"{path.name}:{line_number}"
                try:
                    record = _parse_record(line, context)
                except _RecordDecodeError:
                    # The writer can be interrupted midway through its final WriteLine. Only
                    # that narrow shape is recoverable: final selected file, no line ending,
                    # no clean sessionEnd already observed. Earlier or newline-terminated
                    # corruption remains fatal evidence corruption.
                    if path_index == len(paths) - 1 and not line.endswith(b"\n") and session_end is None:
                        crash_tail = True
                        warnings.append(
                            "crash tail detected; ignored one incomplete final JSONL line in the highest segment"
                        )
                        break
                    raise
                record_type = record.get("recordType")
                if session_end is not None:
                    raise QuestLabSheetsError(f"{context}: record appeared after sessionEnd")
                if record_type == "session":
                    if part_had_header or part_had_event:
                        raise QuestLabSheetsError(f"{context}: duplicate session header in one part")
                    part_had_header = True
                    candidate_id = _required_text(record, "sessionId", context)
                    candidate_fields = record.get("fields")
                    if (not isinstance(candidate_fields, dict)
                            or not isinstance(candidate_fields.get("details"), bool)
                            or not isinstance(candidate_fields.get("diagnosticIdentity"), bool)):
                        raise QuestLabSheetsError(
                            f"{context}: fields.details and fields.diagnosticIdentity must be booleans"
                        )
                    segment = record.get("segment")
                    if isinstance(segment, bool) or not isinstance(segment, int) or segment < 1:
                        raise QuestLabSheetsError(f"{context}: segment must be a positive integer")
                    if segment in observed_segments:
                        raise QuestLabSheetsError(f"{context}: duplicate segment {segment}")
                    filename_match = SESSION_FILE.fullmatch(path.name)
                    if filename_match:
                        filename_segment = int(filename_match.group("part") or "1")
                        if segment != filename_segment:
                            raise QuestLabSheetsError(
                                f"{context}: header segment {segment} disagrees with the filename"
                            )
                    observed_segments.append(segment)
                    if header is None:
                        header = record
                        session_id = candidate_id
                        runtime_profile = _required_text(record, "runtimeProfile", context)
                    else:
                        if candidate_id != session_id:
                            raise QuestLabSheetsError(f"{context}: sessionId changed between parts")
                        for key in ("startedUtc", "releaseId", "runtimeProfile", "fields"):
                            if record.get(key) != header.get(key):
                                raise QuestLabSheetsError(f"{context}: {key} changed between parts")
                    continue
                if record_type == "archiveNotice":
                    if header is None:
                        raise QuestLabSheetsError(f"{context}: archiveNotice appeared before the session header")
                    if record.get("sessionId") != session_id:
                        raise QuestLabSheetsError(f"{context}: archiveNotice sessionId does not match the header")
                    _required_text(record, "timestampUtc", context)
                    if record.get("reason") != "queue-capacity":
                        raise QuestLabSheetsError(f"{context}: unknown archiveNotice reason")
                    since_last = record.get("droppedSinceLastNotice")
                    total_dropped = record.get("totalDroppedEventCount")
                    if (isinstance(since_last, bool) or not isinstance(since_last, int) or since_last < 1
                            or isinstance(total_dropped, bool) or not isinstance(total_dropped, int)
                            or total_dropped < since_last or total_dropped < notice_dropped_total
                            or total_dropped - notice_dropped_total != since_last):
                        raise QuestLabSheetsError(f"{context}: invalid or decreasing archive drop counts")
                    notice_dropped_total = total_dropped
                    archive_notice_count += 1
                    part_had_event = True
                    continue
                if record_type == "sessionEnd":
                    if header is None:
                        raise QuestLabSheetsError(f"{context}: sessionEnd appeared before the session header")
                    if path_index != len(paths) - 1:
                        raise QuestLabSheetsError(f"{context}: sessionEnd must be in the final selected segment")
                    if record.get("sessionId") != session_id:
                        raise QuestLabSheetsError(f"{context}: sessionEnd sessionId does not match the header")
                    for key in ("startedUtc", "releaseId", "runtimeProfile"):
                        if record.get(key) != header.get(key):
                            raise QuestLabSheetsError(f"{context}: sessionEnd {key} does not match the header")
                    _required_text(record, "endedUtc", context)
                    if record.get("reason") != "clean-shutdown":
                        raise QuestLabSheetsError(f"{context}: unknown sessionEnd reason")
                    for key in ("eventCount", "droppedEventCount", "segments"):
                        number = record.get(key)
                        if isinstance(number, bool) or not isinstance(number, int) or number < 0:
                            raise QuestLabSheetsError(f"{context}: {key} must be a non-negative integer")
                    if record["segments"] < 1:
                        raise QuestLabSheetsError(f"{context}: segments must be positive")
                    if record["droppedEventCount"] < notice_dropped_total:
                        raise QuestLabSheetsError(f"{context}: sessionEnd droppedEventCount is below its notices")
                    session_end = record
                    continue
                if record_type != "event":
                    raise QuestLabSheetsError(
                        f"{context}: recordType must be session, event, archiveNotice, or sessionEnd"
                    )
                if header is None:
                    raise QuestLabSheetsError(f"{context}: event appeared before the session header")
                if record.get("sessionId") != session_id:
                    raise QuestLabSheetsError(f"{context}: event sessionId does not match the header")
                sequence = record.get("sequence")
                if isinstance(sequence, bool) or not isinstance(sequence, int) or sequence < 1:
                    raise QuestLabSheetsError(f"{context}: sequence must be a positive integer")
                if sequence in seen_sequences:
                    raise QuestLabSheetsError(f"{context}: duplicate sequence {sequence}")
                seen_sequences.add(sequence)
                part_had_event = True
                fields = header["fields"]
                include_details = fields["details"] is True
                include_identity = fields["diagnosticIdentity"] is True
                if include_details != ("detail" in record):
                    raise QuestLabSheetsError(f"{context}: detail presence disagrees with session privacy fields")
                for private_key in ("diagnosticSeam", "actionIdentity"):
                    if include_identity != (private_key in record):
                        raise QuestLabSheetsError(
                            f"{context}: {private_key} presence disagrees with session privacy fields"
                        )
                events.append(EventRow(
                    schema=SCHEMA,
                    session_id=session_id,
                    sequence=sequence,
                    timestamp_utc=_required_text(record, "timestampUtc", context),
                    school=_required_text(record, "school", context),
                    creator_event=_required_text(record, "creatorEvent", context),
                    target=_present_text(record, "target", context),
                    detail=_present_text(record, "detail", context) if include_details else "",
                    usability=_required_text(record, "usability", context),
                    diagnostic_seam=_present_text(record, "diagnosticSeam", context) if include_identity else "",
                    action_identity=_present_text(record, "actionIdentity", context) if include_identity else "",
                ))
                if len(events) > MAX_EVENTS:
                    raise QuestLabSheetsError(f"session exceeds the {MAX_EVENTS}-event safety limit")
        if not part_had_header:
            raise QuestLabSheetsError(f"{path.name}: each part must begin with a session header")

    if header is None:
        raise QuestLabSheetsError("session header is missing")
    events.sort(key=lambda row: row.sequence)
    sorted_segments = sorted(observed_segments)
    if observed_segments != sorted_segments:
        raise QuestLabSheetsError("archive parts were not supplied in segment order")
    expected_observed = list(range(sorted_segments[0], sorted_segments[-1] + 1))
    missing_segment = sorted_segments != expected_observed or sorted_segments[0] != 1
    if missing_segment:
        warnings.append("one or more retained archive segments are missing")
    sequences = [row.sequence for row in events]
    if sequences:
        expected_sequences = list(range(sequences[0], sequences[-1] + 1))
        if sequences != expected_sequences or sequences[0] != 1:
            if missing_segment:
                warnings.append("event sequence is partial because retained archive segments are missing")
            else:
                raise QuestLabSheetsError("event sequence has a gap; refusing silently corrupted evidence")

    declared_event_count: int | None = None
    declared_segments: int | None = None
    ended_utc = ""
    end_reason = ""
    dropped_event_count = notice_dropped_total
    if session_end is None:
        warnings.append("sessionEnd is absent; the session may still be active or ended uncleanly")
        if dropped_event_count > 0:
            warnings.append(f"archive queue dropped at least {dropped_event_count} event(s) before this tail")
        archive_state = "partial" if missing_segment else "active-or-unclean"
    else:
        declared_event_count = int(session_end["eventCount"])
        declared_segments = int(session_end["segments"])
        ended_utc = str(session_end["endedUtc"])
        end_reason = str(session_end["reason"])
        dropped_event_count = int(session_end["droppedEventCount"])
        if declared_segments < sorted_segments[-1]:
            raise QuestLabSheetsError("sessionEnd segments is below an observed archive segment")
        if declared_event_count < len(events):
            raise QuestLabSheetsError("sessionEnd eventCount is below the retained event rows")
        all_segments_present = sorted_segments == list(range(1, declared_segments + 1))
        if not all_segments_present:
            warnings.append("sessionEnd declares archive segments that are no longer retained")
            missing_segment = True
        if all_segments_present and declared_event_count != len(events):
            raise QuestLabSheetsError("sessionEnd eventCount does not match the selected archive")
        if dropped_event_count > 0:
            warnings.append(f"archive queue dropped {dropped_event_count} event(s)")
        archive_state = (
            "partial" if missing_segment else
            ("complete-with-drops" if dropped_event_count > 0 else "complete")
        )

    fields = header["fields"]
    return EventSession(
        session_id=session_id,
        started_utc=_required_text(header, "startedUtc", "session header"),
        release_id=_required_text(header, "releaseId", "session header"),
        runtime_profile=runtime_profile,
        include_details=fields.get("details") is True,
        include_diagnostic_identity=fields.get("diagnosticIdentity") is True,
        events=tuple(events),
        source_files=tuple(path.name for path in paths),
        source_sha256=source_digest.hexdigest(),
        archive_state=archive_state,
        ended_utc=ended_utc,
        end_reason=end_reason,
        dropped_event_count=dropped_event_count,
        archive_notice_count=archive_notice_count,
        declared_event_count=declared_event_count,
        declared_segments=declared_segments,
        observed_segments=tuple(sorted_segments),
        crash_tail=crash_tail,
        warnings=tuple(warnings),
    )


def discover_sessions(events_dir: Path) -> list[SessionListing]:
    if not events_dir.is_dir():
        return []
    root = events_dir.resolve()
    groups: dict[str, list[tuple[int, Path]]] = {}
    for path in events_dir.iterdir():
        match = SESSION_FILE.fullmatch(path.name)
        if not match or not path.is_file():
            continue
        try:
            resolved = path.resolve(strict=True)
            resolved.relative_to(root)
        except (OSError, ValueError):
            # The dashboard never follows an event-looking symlink or junction outside its
            # fixed archive root. Explicit CLI paths remain an operator-owned local action.
            continue
        part = int(match.group("part") or "0")
        groups.setdefault(match.group("session"), []).append((part, resolved))
    listings: list[SessionListing] = []
    for filename_session, candidates in groups.items():
        candidates.sort(key=lambda item: (item[0], item[1].name))
        parts = [item[1] for item in candidates]
        names = tuple(path.name for path in parts)
        try:
            session = parse_session(parts)
            if session.session_id != filename_session:
                raise QuestLabSheetsError("archive filename does not agree with its sessionId")
            listings.append(SessionListing(
                # Stable while an active archive grows. Dashboard.selected() discovers and
                # reparses on every click, so export uses the current canonical rows and
                # their one-pass source digest rather than the page-render snapshot.
                key=hashlib.sha256(("questlab-session\0" + session.session_id).encode("utf-8")).hexdigest(),
                session=session,
                source_files=names,
            ))
        except QuestLabSheetsError as exc:
            key_material = filename_session + "\0" + "\0".join(names)
            listings.append(SessionListing(
                key=hashlib.sha256(key_material.encode("utf-8")).hexdigest()[:20],
                session=None,
                error=str(exc),
                source_files=names,
            ))
    listings.sort(
        key=lambda item: (
            item.session is not None,
            item.session.started_utc if item.session is not None else "",
            item.session.session_id if item.session is not None else item.key,
        ),
        reverse=True,
    )
    return listings


def csv_text(session: EventSession) -> str:
    output = io.StringIO(newline="")
    writer = csv.writer(output, lineterminator="\r\n")
    writer.writerow(CSV_COLUMNS)
    for event in session.events:
        writer.writerow([_csv_safe(value) for value in event.values()])
    return output.getvalue()


def _csv_safe(value: Any) -> Any:
    if not isinstance(value, str):
        return value
    # CSV is often double-clicked into Excel or imported with automatic interpretation.
    # Keep user-controlled sign/chat/name text from becoming a formula in that path.
    index = 0
    while index < len(value) and (
        value[index].isspace() or unicodedata.category(value[index]) == "Cc"
    ):
        index += 1
    probe = value[index:]
    if probe.startswith(("=", "+", "-", "@")):
        return "'" + value
    return value


def write_csv_atomic(session: EventSession, output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".tmp")
    temporary.write_text(csv_text(session), encoding="utf-8-sig", newline="")
    os.replace(temporary, output)


def workbook_values(session: EventSession, exported_utc: str) -> dict[str, list[list[Any]]]:
    school_counts = Counter(row.school for row in session.events)
    event_counts = Counter(row.creator_event for row in session.events)
    events = [list(CSV_COLUMNS)] + [row.values() for row in session.events]
    summary: list[list[Any]] = [
        ["QUEST LAB EVENT SESSION", "VALUE"],
        ["Archive state", session.archive_state],
        ["Started (UTC)", session.started_utc],
        ["Ended (UTC)", session.ended_utc or "not recorded"],
        ["Exported (UTC)", exported_utc],
        ["Event rows", len(session.events)],
        ["Dropped archive events", session.dropped_event_count],
        ["Bindable rows", sum(1 for row in session.events if row.usability == "today")],
        [],
        ["SCHOOL", "EVENT COUNT"],
    ]
    summary.extend([[school, count] for school, count in sorted(school_counts.items())])
    summary.extend([[], ["CREATOR EVENT", "EVENT COUNT"]])
    summary.extend([[name, count] for name, count in sorted(event_counts.items())])
    metadata: list[list[Any]] = [
        ["FIELD", "VALUE"],
        ["Schema", SCHEMA],
        ["Session ID", session.session_id],
        ["Quest Lab release", session.release_id],
        ["Runtime profile", session.runtime_profile],
        ["Archive state", session.archive_state],
        ["Started (UTC)", session.started_utc],
        ["Ended (UTC)", session.ended_utc or "not recorded"],
        ["End reason", session.end_reason or "not recorded"],
        ["Exported (UTC)", exported_utc],
        ["Details included", session.include_details],
        ["Diagnostic identity included", session.include_diagnostic_identity],
        ["Source SHA-256", session.source_sha256],
        ["Source parts", len(session.source_files)],
        ["Observed segment numbers", ", ".join(str(item) for item in session.observed_segments)],
        ["Declared segments", session.declared_segments if session.declared_segments is not None else "not recorded"],
        ["Declared event rows", session.declared_event_count if session.declared_event_count is not None else "not recorded"],
        ["Dropped archive events", session.dropped_event_count],
        ["Archive notices", session.archive_notice_count],
        ["Crash tail detected", "yes" if session.crash_tail else "no"],
        ["Archive warnings", " | ".join(session.warnings) if session.warnings else "none"],
        ["Source filenames", ", ".join(session.source_files)],
        ["Write mode", "RAW (formula interpretation disabled)"],
        ["OAuth scope", DRIVE_FILE_SCOPE],
    ]
    return {"Events": events, "Summary": summary, "Metadata": metadata}


def chunk_value_ranges(sheet: str, rows: Sequence[Sequence[Any]]) -> Iterator[dict[str, Any]]:
    if not rows:
        return
    chunk: list[Sequence[Any]] = []
    start = 1
    for row in rows:
        candidate = chunk + [row]
        body = {"values": candidate}
        size = len(json.dumps(body, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
        if chunk and size > MAX_GOOGLE_REQUEST_BYTES:
            yield {"range": f"'{sheet}'!A{start}", "values": list(chunk)}
            start += len(chunk)
            chunk = [row]
        else:
            chunk = candidate
    if chunk:
        yield {"range": f"'{sheet}'!A{start}", "values": list(chunk)}


class GoogleSheetsExporter:
    """Creates only new spreadsheets and populates them with RAW values."""

    def __init__(self, service: Any, receipt_dir: Path):
        self._service = service
        self._receipt_dir = receipt_dir

    def export(self, session: EventSession) -> ExportResult:
        exported_utc = utc_now()
        values = workbook_values(session, exported_utc)
        create_body = {
            "properties": {"title": session.title},
            "sheets": [
                {"properties": {"sheetId": 0, "title": "Events", "gridProperties": {
                    "rowCount": max(1000, len(values["Events"]) + 10), "columnCount": len(CSV_COLUMNS)}}},
                {"properties": {"sheetId": 1, "title": "Summary", "gridProperties": {
                    "rowCount": max(100, len(values["Summary"]) + 10), "columnCount": 2}}},
                {"properties": {"sheetId": 2, "title": "Metadata", "gridProperties": {
                    "rowCount": max(100, len(values["Metadata"]) + 10), "columnCount": 2}}},
            ],
        }
        created = self._service.spreadsheets().create(
            body=create_body, fields="spreadsheetId"
        ).execute()
        spreadsheet_id = created.get("spreadsheetId") if isinstance(created, dict) else None
        if not isinstance(spreadsheet_id, str) or not SAFE_SHEET_ID.fullmatch(spreadsheet_id):
            raise QuestLabSheetsError("Google created a spreadsheet but returned an invalid identifier")

        request_count = 0
        try:
            for sheet, rows in values.items():
                for data in chunk_value_ranges(sheet, rows):
                    self._service.spreadsheets().values().batchUpdate(
                        spreadsheetId=spreadsheet_id,
                        body={"valueInputOption": "RAW", "data": [data]},
                    ).execute()
                    request_count += 1
            self._service.spreadsheets().batchUpdate(
                spreadsheetId=spreadsheet_id,
                body={"requests": _formatting_requests(len(values["Events"]))},
            ).execute()
        except Exception as exc:
            url = google_sheet_url(spreadsheet_id)
            raise QuestLabSheetsError(
                "Google created the workbook but population did not finish. "
                f"Inspect or delete the partial workbook at {url}. Error: {_safe_exception(exc)}"
            ) from None

        url = google_sheet_url(spreadsheet_id)
        receipt = {
            "schema": "comfy-questlab-sheets-export/v1",
            "exportedUtc": exported_utc,
            "sessionId": session.session_id,
            "sourceSha256": session.source_sha256,
            "spreadsheetId": spreadsheet_id,
            "spreadsheetUrl": url,
            "eventRows": len(session.events),
            "archiveState": session.archive_state,
            "droppedEventCount": session.dropped_event_count,
            "valuesRequests": request_count,
            "valueInputOption": "RAW",
            "oauthScope": DRIVE_FILE_SCOPE,
        }
        receipt_path = self._write_receipt(session, receipt)
        return ExportResult(spreadsheet_id, url, len(session.events), request_count, str(receipt_path))

    def _write_receipt(self, session: EventSession, receipt: Mapping[str, Any]) -> Path:
        self._receipt_dir.mkdir(parents=True, exist_ok=True)
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        safe_session = re.sub(r"[^A-Za-z0-9._-]", "_", session.session_id)[:48]
        target = self._receipt_dir / f"sheets-export-{stamp}-{safe_session}.json"
        _write_private_text(target, json.dumps(receipt, indent=2, sort_keys=True) + "\n")
        return target


def google_sheet_url(spreadsheet_id: str) -> str:
    if not SAFE_SHEET_ID.fullmatch(spreadsheet_id):
        raise QuestLabSheetsError("refusing an invalid Google spreadsheet identifier")
    return f"https://docs.google.com/spreadsheets/d/{spreadsheet_id}/edit"


def _formatting_requests(event_row_count: int) -> list[dict[str, Any]]:
    header = {
        "userEnteredFormat": {
            "backgroundColor": {"red": 0.10, "green": 0.20, "blue": 0.34},
            "textFormat": {"foregroundColor": {"red": 1, "green": 1, "blue": 1}, "bold": True},
        }
    }
    requests: list[dict[str, Any]] = []
    for sheet_id, columns in ((0, len(CSV_COLUMNS)), (1, 2), (2, 2)):
        requests.append({"updateSheetProperties": {
            "properties": {"sheetId": sheet_id, "gridProperties": {"frozenRowCount": 1}},
            "fields": "gridProperties.frozenRowCount",
        }})
        requests.append({"repeatCell": {
            "range": {"sheetId": sheet_id, "startRowIndex": 0, "endRowIndex": 1,
                      "startColumnIndex": 0, "endColumnIndex": columns},
            "cell": header,
            "fields": "userEnteredFormat(backgroundColor,textFormat)",
        }})
    if event_row_count > 1:
        requests.append({"setBasicFilter": {"filter": {
            "range": {"sheetId": 0, "startRowIndex": 0, "endRowIndex": event_row_count,
                      "startColumnIndex": 0, "endColumnIndex": len(CSV_COLUMNS)}
        }}})
    widths = [190, 185, 80, 190, 105, 190, 180, 320, 110, 220, 220]
    for index, width in enumerate(widths):
        requests.append({"updateDimensionProperties": {
            "range": {"sheetId": 0, "dimension": "COLUMNS", "startIndex": index, "endIndex": index + 1},
            "properties": {"pixelSize": width},
            "fields": "pixelSize",
        }})
    for sheet_id in (1, 2):
        requests.append({"updateDimensionProperties": {
            "range": {"sheetId": sheet_id, "dimension": "COLUMNS", "startIndex": 0, "endIndex": 1},
            "properties": {"pixelSize": 210}, "fields": "pixelSize",
        }})
        requests.append({"updateDimensionProperties": {
            "range": {"sheetId": sheet_id, "dimension": "COLUMNS", "startIndex": 1, "endIndex": 2},
            "properties": {"pixelSize": 440}, "fields": "pixelSize",
        }})
    return requests


def _safe_exception(exc: Exception) -> str:
    text = " ".join(str(exc).split())
    # OAuth/token objects sometimes include response material. Keep errors useful but bounded,
    # and never include repr(), headers, request bodies, or local credential paths.
    return (text[:300] if text else type(exc).__name__).replace("Bearer ", "[redacted] ")


class _DataBlob(ctypes.Structure):
    _fields_ = [("cbData", ctypes.c_ulong), ("pbData", ctypes.POINTER(ctypes.c_ubyte))]


class DpapiProtector:
    """Windows current-user DPAPI without an additional package."""

    @staticmethod
    def _blob(data: bytes) -> tuple[_DataBlob, Any]:
        buffer = (ctypes.c_ubyte * len(data)).from_buffer_copy(data)
        return _DataBlob(len(data), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte))), buffer

    def protect(self, plain: bytes) -> bytes:
        source, keepalive = self._blob(plain)
        output = _DataBlob()
        ok = ctypes.windll.crypt32.CryptProtectData(
            ctypes.byref(source), "Comfy Quest Lab Google OAuth", None, None, None, 0,
            ctypes.byref(output),
        )
        if not ok:
            raise QuestLabSheetsError("Windows could not protect the local Google token")
        try:
            return ctypes.string_at(output.pbData, output.cbData)
        finally:
            ctypes.windll.kernel32.LocalFree(output.pbData)

    def unprotect(self, protected: bytes) -> bytes:
        source, keepalive = self._blob(protected)
        output = _DataBlob()
        ok = ctypes.windll.crypt32.CryptUnprotectData(
            ctypes.byref(source), None, None, None, None, 0, ctypes.byref(output)
        )
        if not ok:
            raise QuestLabSheetsError("Windows could not unlock the local Google token for this user")
        try:
            return ctypes.string_at(output.pbData, output.cbData)
        finally:
            ctypes.windll.kernel32.LocalFree(output.pbData)


class TokenStore:
    def __init__(self, state_dir: Path, protector: Any | None = None):
        self.state_dir = state_dir
        self._protector = protector if protector is not None else (DpapiProtector() if os.name == "nt" else None)

    @property
    def client_path(self) -> Path:
        return self.state_dir / "desktop-oauth-client.json"

    @property
    def token_path(self) -> Path:
        return self.state_dir / ("oauth-token.dpapi" if self._protector is not None else "oauth-token.json")

    def has_client(self) -> bool:
        return self.client_path.is_file()

    def has_token(self) -> bool:
        return self.token_path.is_file()

    def validate_client(self) -> None:
        if not self.has_client():
            raise QuestLabSheetsError(f"Desktop OAuth client is not configured at {self.client_path}")
        try:
            value = json.loads(self.client_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise QuestLabSheetsError(f"Desktop OAuth client file is unreadable: {_safe_exception(exc)}") from None
        installed = value.get("installed") if isinstance(value, dict) else None
        if not isinstance(installed, dict) or "web" in value:
            raise QuestLabSheetsError("OAuth client must be a Google Desktop app client, not a Web client")
        if installed.get("auth_uri") not in ALLOWED_AUTH_URIS:
            raise QuestLabSheetsError("OAuth client auth_uri is not the official Google authorization endpoint")
        if installed.get("token_uri") != GOOGLE_TOKEN_URI:
            raise QuestLabSheetsError("OAuth client token_uri is not the official Google token endpoint")
        client_id = installed.get("client_id")
        if not isinstance(client_id, str) or not client_id.endswith(".apps.googleusercontent.com"):
            raise QuestLabSheetsError("OAuth client_id is not a Google Desktop client identifier")
        redirect_uris = installed.get("redirect_uris")
        if not isinstance(redirect_uris, list) or not redirect_uris:
            raise QuestLabSheetsError("OAuth Desktop client does not declare a loopback redirect")
        for redirect in redirect_uris:
            try:
                parsed = urllib.parse.urlsplit(redirect)
            except (TypeError, ValueError):
                raise QuestLabSheetsError("OAuth Desktop client contains an invalid redirect") from None
            if parsed.scheme != "http" or parsed.hostname not in ("localhost", "127.0.0.1", "::1"):
                raise QuestLabSheetsError("OAuth Desktop client redirects must stay on loopback")

    def save_token_text(self, text: str) -> None:
        self.state_dir.mkdir(parents=True, exist_ok=True)
        data = text.encode("utf-8")
        if self._protector is not None:
            data = self._protector.protect(data)
            _write_private_bytes(self.token_path, data)
        else:
            _write_private_text(self.token_path, text)

    def load_token_text(self) -> str:
        data = self.token_path.read_bytes()
        if self._protector is not None:
            data = self._protector.unprotect(data)
        try:
            return data.decode("utf-8")
        except UnicodeDecodeError:
            raise QuestLabSheetsError("the saved Google token is unreadable") from None

    def delete_token(self) -> None:
        try:
            self.token_path.unlink()
        except FileNotFoundError:
            pass


def _write_private_bytes(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    handle, temp_name = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    try:
        if os.name != "nt":
            os.chmod(temp_name, 0o600)
        with os.fdopen(handle, "wb") as output:
            output.write(data)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temp_name, path)
        if os.name != "nt":
            os.chmod(path, 0o600)
    finally:
        try:
            os.unlink(temp_name)
        except FileNotFoundError:
            pass


def _write_private_text(path: Path, text: str) -> None:
    _write_private_bytes(path, text.encode("utf-8"))


class GoogleAuth:
    def __init__(self, store: TokenStore):
        self.store = store

    @staticmethod
    def dependency_error() -> str | None:
        try:
            import google.auth.transport.requests  # noqa: F401
            import google.oauth2.credentials  # noqa: F401
            import google_auth_oauthlib.flow  # noqa: F401
            import googleapiclient.discovery  # noqa: F401
        except ImportError:
            return "optional Google libraries are not installed"
        return None

    def connect(self) -> None:
        missing = self.dependency_error()
        if missing:
            raise QuestLabSheetsError(missing + "; install requirements-google.txt")
        self.store.validate_client()
        from google_auth_oauthlib.flow import InstalledAppFlow

        flow = InstalledAppFlow.from_client_secrets_file(
            str(self.store.client_path),
            scopes=[DRIVE_FILE_SCOPE],
            autogenerate_code_verifier=True,
        )
        credentials = flow.run_local_server(
            host="127.0.0.1",
            port=0,
            open_browser=True,
            access_type="offline",
            authorization_prompt_message="Opening Google authorization in your browser...",
            success_message="Quest Lab is connected. You can close this tab and return to the exporter.",
        )
        self._require_narrow_scope(json.loads(credentials.to_json()))
        self.store.save_token_text(credentials.to_json())

    def service(self) -> Any:
        missing = self.dependency_error()
        if missing:
            raise QuestLabSheetsError(missing + "; install requirements-google.txt")
        if not self.store.has_token():
            raise QuestLabSheetsError("Google is not connected; use Connect Google once first")
        from google.auth.transport.requests import Request
        from google.oauth2.credentials import Credentials
        from googleapiclient.discovery import build

        token_info = json.loads(self.store.load_token_text())
        self._require_narrow_scope(token_info)
        credentials = Credentials.from_authorized_user_info(token_info, [DRIVE_FILE_SCOPE])
        if not credentials.valid:
            if credentials.expired and credentials.refresh_token:
                try:
                    credentials.refresh(Request())
                except Exception as exc:
                    raise QuestLabSheetsError(
                        "Google authorization could not be refreshed. Disconnect and connect again, or ask your Workspace admin. "
                        + _safe_exception(exc)
                    ) from None
                self.store.save_token_text(credentials.to_json())
            else:
                raise QuestLabSheetsError("Google authorization expired; disconnect and connect again")
        return build("sheets", "v4", credentials=credentials, cache_discovery=False)

    @staticmethod
    def _require_narrow_scope(token_info: Mapping[str, Any]) -> None:
        scopes = token_info.get("scopes")
        if not isinstance(scopes, list) or set(scopes) != {DRIVE_FILE_SCOPE}:
            raise QuestLabSheetsError(
                "saved Google authorization is not exactly the per-file drive.file scope; disconnect and connect again"
            )
        if token_info.get("token_uri") != GOOGLE_TOKEN_URI:
            raise QuestLabSheetsError("saved Google authorization does not use the official Google token endpoint")
        client_id = token_info.get("client_id")
        if not isinstance(client_id, str) or not client_id.endswith(".apps.googleusercontent.com"):
            raise QuestLabSheetsError("saved Google authorization does not name a Google Desktop client")

    def disconnect(self, revoke: bool = True) -> str:
        if not self.store.has_token():
            return "No local Google authorization was stored."
        revoked = False
        if revoke:
            try:
                data = json.loads(self.store.load_token_text())
                token = data.get("refresh_token") or data.get("token")
                if isinstance(token, str) and token:
                    request = urllib.request.Request(
                        GOOGLE_REVOKE_URI,
                        data=urllib.parse.urlencode({"token": token}).encode("ascii"),
                        headers={"Content-Type": "application/x-www-form-urlencoded"},
                        method="POST",
                    )
                    with urllib.request.urlopen(request, timeout=15) as response:
                        revoked = response.status == HTTPStatus.OK
            except Exception:
                revoked = False
        self.store.delete_token()
        if revoke and not revoked:
            return "Local token deleted. Google revocation could not be confirmed; review your Google Account connections."
        return "Google authorization revoked and the protected local token was deleted."


class Dashboard:
    def __init__(self, events_dir: Path, state_dir: Path):
        self.events_dir = events_dir.resolve()
        self.state_dir = state_dir.resolve()
        self.store = TokenStore(self.state_dir)
        self.auth = GoogleAuth(self.store)
        self.csrf = secrets.token_urlsafe(32)

    def listings(self) -> list[SessionListing]:
        return discover_sessions(self.events_dir)

    def selected(self, key: str) -> EventSession:
        matches = [
            listing.session
            for listing in self.listings()
            if listing.session is not None and secrets.compare_digest(listing.key, key)
        ]
        if len(matches) > 1:
            raise QuestLabSheetsError("the selected event session identity is ambiguous; export refused")
        if len(matches) == 1:
            return matches[0]
        raise QuestLabSheetsError("the selected event session no longer exists")

    def status(self) -> str:
        if GoogleAuth.dependency_error():
            return "LOCAL ONLY - Google libraries are not installed"
        if not self.store.has_client():
            return "LOCAL ONLY - Desktop OAuth client setup is required"
        if not self.store.has_token():
            return "READY TO CONNECT - Google has not been authorized"
        return "GOOGLE CONNECTED - one-click Sheet export is ready"


def make_handler(dashboard: Dashboard) -> type[BaseHTTPRequestHandler]:
    class Handler(BaseHTTPRequestHandler):
        server_version = "QuestLabSheets/1"

        def log_message(self, format: str, *args: Any) -> None:
            # Never print query strings, POST bodies, OAuth material, or file paths.
            sys.stderr.write("questlab-sheets: " + (format % args).split("?")[0] + "\n")

        def _secure_headers(self, content_type: str, length: int | None = None) -> None:
            self.send_header("Content-Type", content_type)
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("X-Frame-Options", "DENY")
            self.send_header("Referrer-Policy", "no-referrer")
            self.send_header("Content-Security-Policy", "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'")
            if length is not None:
                self.send_header("Content-Length", str(length))

        def _valid_host(self) -> bool:
            return self.headers.get("Host", "") == f"127.0.0.1:{SHEETS_PORT}"

        def _error(self, status: int, message: str) -> None:
            self._html(status, page("Quest Lab Sheets", f"<div class='alert'><strong>Could not continue</strong><p>{html.escape(message)}</p></div><p><a href='/'>Back to sessions</a></p>"))

        def _html(self, status: int, body: str) -> None:
            encoded = body.encode("utf-8")
            self.send_response(status)
            self._secure_headers("text/html; charset=utf-8", len(encoded))
            self.end_headers()
            self.wfile.write(encoded)

        def _form(self) -> dict[str, str]:
            try:
                length = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                raise QuestLabSheetsError("invalid request length") from None
            if length < 0 or length > 8192:
                raise QuestLabSheetsError("request is outside the local dashboard safety limit")
            content_type = self.headers.get("Content-Type", "")
            if not content_type.startswith("application/x-www-form-urlencoded"):
                raise QuestLabSheetsError("request must be a dashboard form")
            values = urllib.parse.parse_qs(self.rfile.read(length).decode("utf-8"), keep_blank_values=True)
            return {key: entries[-1] for key, entries in values.items() if entries}

        def _csrf_ok(self, value: str | None) -> bool:
            return isinstance(value, str) and secrets.compare_digest(value, dashboard.csrf)

        def do_GET(self) -> None:  # noqa: N802
            if not self._valid_host():
                self._error(HTTPStatus.BAD_REQUEST, "invalid local Host header")
                return
            parsed = urllib.parse.urlsplit(self.path)
            query = urllib.parse.parse_qs(parsed.query)
            if parsed.path == "/":
                try:
                    self._html(HTTPStatus.OK, dashboard_page(dashboard))
                except QuestLabSheetsError as exc:
                    self._error(HTTPStatus.UNPROCESSABLE_ENTITY, str(exc))
                return
            if parsed.path == "/download.csv":
                if not self._csrf_ok((query.get("csrf") or [None])[-1]):
                    self._error(HTTPStatus.FORBIDDEN, "dashboard session expired; refresh the page")
                    return
                try:
                    session = dashboard.selected((query.get("id") or [""])[-1])
                    data = csv_text(session).encode("utf-8-sig")
                    filename = re.sub(r"[^A-Za-z0-9._-]", "_", session.session_id)[:64]
                    self.send_response(HTTPStatus.OK)
                    self._secure_headers("text/csv; charset=utf-8", len(data))
                    self.send_header("Content-Disposition", f'attachment; filename="questlab-events-{filename}.csv"')
                    self.end_headers()
                    self.wfile.write(data)
                except QuestLabSheetsError as exc:
                    self._error(HTTPStatus.UNPROCESSABLE_ENTITY, str(exc))
                return
            self._error(HTTPStatus.NOT_FOUND, "unknown local dashboard route")

        def do_POST(self) -> None:  # noqa: N802
            if not self._valid_host():
                self._error(HTTPStatus.BAD_REQUEST, "invalid local Host header")
                return
            if self.headers.get("Origin") not in (None, f"http://127.0.0.1:{SHEETS_PORT}"):
                self._error(HTTPStatus.FORBIDDEN, "cross-origin requests are not accepted")
                return
            try:
                form = self._form()
                if not self._csrf_ok(form.get("csrf")):
                    raise QuestLabSheetsError("dashboard session expired; refresh the page")
                if self.path == "/connect-google":
                    dashboard.auth.connect()
                    self._html(HTTPStatus.OK, page("Google connected", "<div class='good'><strong>Connected.</strong><p>Future exports need one click. Quest Lab can create and edit only files it creates or you explicitly open with it.</p></div><p><a href='/'>Back to sessions</a></p>"))
                    return
                if self.path == "/disconnect-google":
                    message = dashboard.auth.disconnect(revoke=True)
                    self._html(HTTPStatus.OK, page("Google disconnected", f"<div class='good'><strong>Disconnected.</strong><p>{html.escape(message)}</p></div><p><a href='/'>Back to sessions</a></p>"))
                    return
                if self.path == "/export-google":
                    session = dashboard.selected(form.get("id", ""))
                    service = dashboard.auth.service()
                    result = GoogleSheetsExporter(service, dashboard.state_dir / "receipts").export(session)
                    self.send_response(HTTPStatus.SEE_OTHER)
                    self.send_header("Location", result.spreadsheet_url)
                    self._secure_headers("text/plain; charset=utf-8", 0)
                    self.end_headers()
                    return
                raise QuestLabSheetsError("unknown local dashboard action")
            except QuestLabSheetsError as exc:
                self._error(HTTPStatus.UNPROCESSABLE_ENTITY, str(exc))
            except Exception as exc:
                self._error(HTTPStatus.BAD_GATEWAY, "Google export failed safely: " + _safe_exception(exc))

    return Handler


def page(title: str, body: str) -> str:
    return f"""<!doctype html><html lang='en'><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'><title>{html.escape(title)}</title>
<style>
:root{{--ink:#122033;--muted:#5c6b7a;--line:#d8e0ea;--blue:#155eef;--surface:#fff;--wash:#f4f7fb;--good:#0b6b3a;--warn:#875b00}}
*{{box-sizing:border-box}}body{{margin:0;background:var(--wash);color:var(--ink);font:15px/1.45 system-ui,-apple-system,Segoe UI,sans-serif}}
main{{max-width:1120px;margin:40px auto;padding:0 22px}}h1{{font-size:28px;margin:0}}h2{{font-size:18px;margin:0 0 6px}}p{{margin:7px 0}}.top{{display:flex;align-items:flex-start;justify-content:space-between;gap:24px;margin-bottom:22px}}
.status{{padding:8px 12px;border:1px solid var(--line);background:#fff;border-radius:8px;font-weight:700}}.card{{background:var(--surface);border:1px solid var(--line);border-radius:12px;padding:18px;margin:12px 0;box-shadow:0 2px 8px #16365d12}}
.meta{{color:var(--muted)}}.actions{{display:flex;gap:8px;flex-wrap:wrap;margin-top:14px}}button,.button{{border:0;border-radius:7px;padding:9px 13px;font-weight:700;cursor:pointer;text-decoration:none;display:inline-block;background:#e7edf5;color:var(--ink)}}button.primary{{background:var(--blue);color:#fff}}button:disabled{{opacity:.45;cursor:not-allowed}}.good{{border-left:5px solid var(--good);padding:14px;background:#eef9f2}}.alert{{border-left:5px solid #b42318;padding:14px;background:#fff1f0}}code{{background:#e8edf4;padding:2px 5px;border-radius:4px;overflow-wrap:anywhere}}form{{display:inline}}.setup{{border-left:5px solid var(--warn)}}
</style></head><body><main>{body}</main></body></html>"""


def dashboard_page(dashboard: Dashboard) -> str:
    listings = dashboard.listings()
    missing = GoogleAuth.dependency_error()
    connected = dashboard.store.has_token() and not missing
    configured = dashboard.store.has_client()
    csrf = html.escape(dashboard.csrf, quote=True)
    cards: list[str] = []
    for listing in listings:
        session = listing.session
        key = html.escape(listing.key, quote=True)
        if session is None:
            cards.append(
                "<section class='card'><h2>Unreadable event session</h2>"
                f"<p class='alert'><strong>Export disabled:</strong> {html.escape(listing.error)}</p>"
                f"<p class='meta'>{html.escape(', '.join(listing.source_files))}</p></section>"
            )
            continue
        sheet_button = (
            f"<form method='post' action='/export-google'><input type='hidden' name='csrf' value='{csrf}'>"
            f"<input type='hidden' name='id' value='{key}'><button class='primary' type='submit'>Create Google Sheet</button></form>"
            if connected else
            "<button class='primary' type='button' disabled title='Complete Google setup first'>Create Google Sheet</button>"
        )
        warning_html = (
            f"<p class='alert'><strong>Archive warning:</strong> {html.escape(' | '.join(session.warnings))}</p>"
            if session.warnings else ""
        )
        cards.append(
            "<section class='card'>"
            f"<h2>{html.escape(session.title)}</h2>"
            f"<p class='meta'>{len(session.events):,} events · {html.escape(session.release_id)} · {len(session.source_files)} local part(s) · archive {html.escape(session.archive_state)}</p>"
            + warning_html
            +
            f"<p class='meta'>Source SHA-256 <code>{html.escape(session.source_sha256)}</code></p>"
            "<div class='actions'>"
            f"<a class='button' href='/download.csv?id={key}&amp;csrf={csrf}'>Download CSV</a>{sheet_button}"
            "</div></section>"
        )
    if not cards:
        cards.append("<section class='card'><h2>No event sessions yet</h2><p class='meta'>Play with event archiving enabled, then refresh this page. Local CSV remains available without Google.</p></section>")

    setup = ""
    if missing:
        setup = (
            "<section class='card setup'><h2>Optional Google setup</h2>"
            "<p>Local parsing and CSV already work. To enable Sheets, install the optional libraries:</p>"
            "<p><code>python -m pip install -r requirements-google.txt</code></p></section>"
        )
    elif not configured:
        setup = (
            "<section class='card setup'><h2>Optional Google setup</h2>"
            "<p>Create a Google Cloud <strong>Desktop app</strong> OAuth client with the Sheets API enabled, then copy its downloaded JSON to:</p>"
            f"<p><code>{html.escape(str(dashboard.store.client_path))}</code></p>"
            "<p>No client credential is bundled or uploaded. Quest Lab validates Google's endpoints before using the file.</p></section>"
        )
    elif not connected:
        setup = (
            "<section class='card setup'><h2>Connect Google once</h2>"
            "<p>Your system browser will open Google's consent page. Quest Lab requests only per-file Drive access, not all Sheets or Drive files.</p>"
            f"<form method='post' action='/connect-google'><input type='hidden' name='csrf' value='{csrf}'>"
            "<button class='primary' type='submit'>Connect Google</button></form></section>"
        )
    else:
        setup = (
            "<section class='card'><h2>Google connection</h2>"
            "<p class='meta'>Protected for this OS user. Export creates a new workbook; it never searches or edits an existing one.</p>"
            f"<form method='post' action='/disconnect-google'><input type='hidden' name='csrf' value='{csrf}'>"
            "<button type='submit'>Disconnect and revoke</button></form></section>"
        )

    body = (
        "<div class='top'><div><h1>Quest Lab exports</h1>"
        f"<p class='meta'>Authoritative JSONL stays in <code>{html.escape(str(dashboard.events_dir))}</code>.</p></div>"
        f"<div class='status'>{html.escape(dashboard.status())}</div></div>"
        + setup + "".join(cards)
        + "<p class='meta'>Local-only dashboard · fixed 127.0.0.1 listener · no public upload · Google values are written as RAW text.</p>"
    )
    return page("Quest Lab exports", body)


def serve(events_dir: Path, state_dir: Path, open_browser: bool) -> None:
    dashboard = Dashboard(events_dir, state_dir)
    try:
        server = ThreadingHTTPServer(("127.0.0.1", SHEETS_PORT), make_handler(dashboard))
    except OSError as exc:
        raise QuestLabSheetsError(f"could not bind the fixed local exporter at {SHEETS_HOME}: {_safe_exception(exc)}") from None
    print(f"Quest Lab Sheets is local-only at {SHEETS_HOME}")
    print("Press Ctrl+C to stop. No Google request occurs until you click Connect or Create Google Sheet.")
    if open_browser:
        threading.Timer(0.25, lambda: webbrowser.open(SHEETS_HOME)).start()
    try:
        server.serve_forever(poll_interval=0.25)
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


def inspect_payload(session: EventSession) -> dict[str, Any]:
    return {
        "schema": SCHEMA,
        "sessionId": session.session_id,
        "startedUtc": session.started_utc,
        "releaseId": session.release_id,
        "runtimeProfile": session.runtime_profile,
        "archiveState": session.archive_state,
        "endedUtc": session.ended_utc or None,
        "endReason": session.end_reason or None,
        "droppedEventCount": session.dropped_event_count,
        "archiveNoticeCount": session.archive_notice_count,
        "declaredEventRows": session.declared_event_count,
        "declaredSegments": session.declared_segments,
        "observedSegments": list(session.observed_segments),
        "crashTail": session.crash_tail,
        "warnings": list(session.warnings),
        "eventRows": len(session.events),
        "firstSequence": session.events[0].sequence if session.events else None,
        "lastSequence": session.events[-1].sequence if session.events else None,
        "sourceFiles": list(session.source_files),
        "sourceSha256": session.source_sha256,
        "schoolCounts": dict(sorted(Counter(row.school for row in session.events).items())),
        "eventCounts": dict(sorted(Counter(row.creator_event for row in session.events).items())),
        "fields": {
            "details": session.include_details,
            "diagnosticIdentity": session.include_diagnostic_identity,
        },
    }


def resolve_cli_session(inputs: Sequence[str]) -> EventSession:
    paths = [Path(value).resolve() for value in inputs]
    if len(paths) == 1 and paths[0].is_dir():
        listings = discover_sessions(paths[0])
        if len(listings) != 1:
            raise QuestLabSheetsError("a directory input must contain exactly one event session; pass explicit part files otherwise")
        if listings[0].session is None:
            raise QuestLabSheetsError(listings[0].error)
        return listings[0].session
    return parse_session(paths)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Parse Quest Lab event sessions and optionally export them to Google Sheets.")
    commands = parser.add_subparsers(dest="command", required=True)

    inspect = commands.add_parser("inspect", help="validate JSONL and print a normalized summary")
    inspect.add_argument("paths", nargs="+", help="one session's JSONL part files, in order")

    csv_command = commands.add_parser("to-csv", help="write a formula-safe RFC 4180 CSV")
    csv_command.add_argument("paths", nargs="+", help="one session's JSONL part files, in order")
    csv_command.add_argument("--output", required=True, type=Path)

    serve_command = commands.add_parser("serve", help=f"open the local export dashboard at {SHEETS_HOME}")
    serve_command.add_argument("--events-dir", type=Path, default=default_events_dir())
    serve_command.add_argument("--state-dir", type=Path, default=None)
    serve_command.add_argument("--no-browser", action="store_true")

    doctor = commands.add_parser("doctor", help="report local readiness without printing secrets or making network calls")
    doctor.add_argument("--events-dir", type=Path, default=default_events_dir())
    doctor.add_argument("--state-dir", type=Path, default=None)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        if args.command == "inspect":
            print(json.dumps(inspect_payload(resolve_cli_session(args.paths)), indent=2, sort_keys=True))
        elif args.command == "to-csv":
            session = resolve_cli_session(args.paths)
            write_csv_atomic(session, args.output.resolve())
            print(f"wrote {len(session.events)} events to {args.output.resolve()}")
        elif args.command == "serve":
            serve(args.events_dir.resolve(), (args.state_dir or default_state_dir()).resolve(), not args.no_browser)
        elif args.command == "doctor":
            state_dir = (args.state_dir or default_state_dir()).resolve()
            store = TokenStore(state_dir)
            listings = discover_sessions(args.events_dir.resolve())
            payload = {
                "schema": "comfy-questlab-sheets-doctor/v1",
                "dashboard": SHEETS_HOME,
                "eventsDirectoryExists": args.events_dir.is_dir(),
                "sessions": sum(1 for item in listings if item.session is not None),
                "unreadableSessions": sum(1 for item in listings if item.session is None),
                "googleLibraries": GoogleAuth.dependency_error() is None,
                "desktopOauthClient": store.has_client(),
                "protectedLocalToken": store.has_token(),
                "oauthScope": DRIVE_FILE_SCOPE,
                "networkRequestsMade": 0,
            }
            print(json.dumps(payload, indent=2, sort_keys=True))
        return 0
    except QuestLabSheetsError as exc:
        print(f"questlab-sheets: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())

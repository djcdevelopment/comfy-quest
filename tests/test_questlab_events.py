from __future__ import annotations

import csv
import importlib.util
import io
import json
import sys
import tempfile
import unittest
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from xml.etree import ElementTree


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "questlab-events" / "questlab_events.py"
SPEC = importlib.util.spec_from_file_location("questlab_events", SCRIPT)
EVENTS = importlib.util.module_from_spec(SPEC)
assert SPEC.loader
sys.modules[SPEC.name] = EVENTS
SPEC.loader.exec_module(EVENTS)


SESSION = "20260809T160000123Z-p1234"


def header(segment: int = 1) -> dict:
    return {
        "schema": EVENTS.ARCHIVE_SCHEMA,
        "recordType": "session",
        "sessionId": SESSION,
        "startedUtc": "2026-08-09T16:00:00.123Z",
        "releaseId": "questlab-r24",
        "segment": segment,
        "fields": {"details": True, "diagnosticIdentity": True},
    }


def event(
    sequence: int,
    *,
    at: str | None = None,
    school: str = "combat",
    name: str = "kill",
    target: str = "Greyling",
    action: str = "action-1",
) -> dict:
    return {
        "schema": EVENTS.ARCHIVE_SCHEMA,
        "recordType": "event",
        "sessionId": SESSION,
        "sequence": sequence,
        "timestampUtc": at or f"2026-08-09T16:00:0{sequence}.000Z",
        "school": school,
        "creatorEvent": name,
        "target": target,
        "usability": "today",
        "detail": "safe detail",
        "diagnosticSeam": "Character.OnDeath",
        "actionIdentity": action,
    }


def session_end(event_count: int, segments: int = 1) -> dict:
    return {
        "schema": EVENTS.ARCHIVE_SCHEMA,
        "recordType": "sessionEnd",
        "sessionId": SESSION,
        "endedUtc": "2026-08-09T16:01:00.000Z",
        "eventCount": event_count,
        "segments": segments,
        "reason": "clean-shutdown",
    }


def archive_notice(dropped: int, total: int) -> dict:
    return {
        "schema": EVENTS.ARCHIVE_SCHEMA,
        "recordType": "archiveNotice",
        "sessionId": SESSION,
        "timestampUtc": "2026-08-09T16:00:30.000Z",
        "reason": "queue-capacity",
        "droppedSinceLastNotice": dropped,
        "totalDroppedEventCount": total,
    }


class QuestLabEventExportTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def jsonl(self, name: str, rows: list[dict], final_newline: bool = True) -> Path:
        path = self.root / name
        value = "\n".join(json.dumps(row) for row in rows)
        path.write_text(value + ("\n" if final_newline else ""), encoding="utf-8")
        return path

    def csv_archive(self, name: str, rows: list[dict]) -> Path:
        path = self.root / name
        with path.open("w", encoding="utf-8", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=EVENTS.CSV_HEADER, lineterminator="\r\n")
            writer.writeheader()
            for item in rows:
                writer.writerow(
                    {
                        "schema": item["schema"],
                        "session_id": item["sessionId"],
                        "sequence": item["sequence"],
                        "timestamp_utc": item["timestampUtc"],
                        "school": item["school"],
                        "creator_event": item["creatorEvent"],
                        "target": item["target"],
                        "detail": item.get("detail", ""),
                        "usability": item["usability"],
                        "diagnostic_seam": item.get("diagnosticSeam", ""),
                        "action_identity": item.get("actionIdentity", ""),
                    }
                )
        return path

    def report(self, paths: list[Path], **kwargs) -> dict:
        read = EVENTS.read_inputs(paths, strict=True, **kwargs)
        return EVENTS.build_report(read, read.records, filters={})

    def test_strict_archive_coalesces_stable_action_and_preserves_raw_count(self) -> None:
        path = self.jsonl("questlab-events-session.jsonl", [header(), event(1), event(2), session_end(2)])
        report = self.report([path])
        self.assertEqual(report["totals"]["raw_witnesses"], 2)
        self.assertEqual(report["totals"]["canonical_actions"], 1)
        self.assertEqual(report["totals"]["coalesced_witnesses"], 1)
        self.assertEqual(report["totals"]["clean_shutdown_sessions"], 1)
        self.assertEqual(report["actions"][0]["raw_witnesses"], 2)
        encoded = json.dumps(report)
        self.assertNotIn(SESSION, encoded)
        self.assertNotIn("action-1", encoded)
        self.assertNotIn("Character.OnDeath", encoded)

    def test_rotated_segments_are_independently_self_describing(self) -> None:
        first = self.jsonl("questlab-events-session.jsonl", [header(1), event(1)])
        second = self.jsonl("questlab-events-session-part002.jsonl", [header(2), event(2), session_end(2, 2)])
        read = EVENTS.read_inputs([first, second], strict=True)
        self.assertEqual(len(read.records), 2)
        self.assertEqual(read.session_headers[SESSION]["seenSegments"], [1, 2])
        self.assertIn(SESSION, read.session_ends)

    def test_jsonl_and_csv_mirrors_do_not_double_count(self) -> None:
        item = event(1)
        source = self.jsonl("questlab-events-session.jsonl", [header(), item])
        projection = self.csv_archive("questlab-events-session.csv", [item])
        read = EVENTS.read_inputs([source, projection], strict=True)
        report = EVENTS.build_report(read, read.records, filters={})
        self.assertEqual(len(read.records), 1)
        self.assertEqual(read.duplicate_input_records, 1)
        self.assertEqual(report["totals"]["raw_witnesses"], 1)

    def test_formula_neutralized_csv_is_the_same_authoritative_jsonl_witness(self) -> None:
        item = event(1, target="=IMPORTXML(\"https://bad.invalid\")", action="@action-1")
        item["detail"] = "+detail"
        item["diagnosticSeam"] = "-diagnostic"
        source = self.jsonl("questlab-events-formula-mirror.jsonl", [header(), item])
        projected = dict(item)
        for key in ("target", "detail", "diagnosticSeam", "actionIdentity"):
            projected[key] = "'" + projected[key]
        projection = self.csv_archive("questlab-events-formula-mirror.csv", [projected])

        read = EVENTS.read_inputs([source, projection], strict=True)

        self.assertEqual(len(read.records), 1)
        self.assertEqual(read.duplicate_input_records, 1)
        self.assertEqual(read.records[0].source_format, "jsonl")
        self.assertEqual(read.records[0].target, item["target"])
        self.assertEqual(read.records[0].action_identity, item["actionIdentity"])

    def test_malformed_json_names_file_line_and_column(self) -> None:
        path = self.root / "questlab-events-bad.jsonl"
        path.write_text(json.dumps(header()) + "\n" + '{"schema": nope}\n', encoding="utf-8")
        with self.assertRaisesRegex(EVENTS.EventExportError, r"questlab-events-bad\.jsonl:2: invalid JSON at column"):
            EVENTS.read_inputs([path], strict=True)

    def test_crash_truncated_tail_requires_explicit_flag_and_is_reported(self) -> None:
        path = self.root / "questlab-events-crash.jsonl"
        path.write_text(
            json.dumps(header()) + "\n" + json.dumps(event(1)) + "\n" + '{"schema":"comfy',
            encoding="utf-8",
        )
        with self.assertRaisesRegex(EVENTS.EventExportError, "invalid JSON"):
            EVENTS.read_inputs([path], strict=True)
        read = EVENTS.read_inputs([path], strict=True, allow_truncated_tail=True)
        report = EVENTS.build_report(read, read.records, filters={})
        self.assertEqual(read.truncated_tail_records_ignored, 1)
        self.assertEqual(report["totals"]["truncated_tail_records_ignored"], 1)

    def test_archive_notice_preserves_bounded_queue_loss_in_summary(self) -> None:
        path = self.jsonl(
            "questlab-events-overflow.jsonl",
            [header(), event(1), archive_notice(7, 7), archive_notice(2, 9), session_end(1)],
        )
        report = self.report([path])
        self.assertEqual(report["totals"]["archive_notices"], 2)
        self.assertEqual(report["totals"]["dropped_event_count"], 9)
        self.assertTrue(report["totals"]["data_loss_detected"])
        metadata = {row["key"]: row["value"] for row in report["metadata"]}
        self.assertEqual(metadata["dropped_event_count"], "9")

    def test_strict_contract_rejects_legacy_but_tolerant_mode_imports_it(self) -> None:
        legacy = self.jsonl(
            "legacy.jsonl",
            [
                {
                    "at": "2026-08-09T16:00:01Z",
                    "category": "harvest",
                    "eventName": "resource_damaged",
                    "target": "Beech1",
                }
            ],
        )
        read = EVENTS.read_inputs([legacy])
        self.assertEqual(read.records[0].creator_event, "resource_damaged")
        with self.assertRaisesRegex(EVENTS.EventExportError, "first record must be a session"):
            EVENTS.read_inputs([legacy], strict=True)

    def test_strict_csv_header_is_exact(self) -> None:
        path = self.root / "bad.csv"
        path.write_text("schema,creator_event,timestamp_utc\n", encoding="utf-8")
        with self.assertRaisesRegex(EVENTS.EventExportError, "strict CSV header must be exactly"):
            EVENTS.read_inputs([path], strict=True)

    def test_filters_school_event_target_and_inclusive_time(self) -> None:
        rows = [
            header(),
            event(1),
            event(2, school="harvest", name="resource_damaged", target="AncientTree", action="action-2"),
            event(3, at="2026-08-09T16:00:03Z", school="harvest", name="resource_damaged", target="Beech1", action="action-3"),
        ]
        path = self.jsonl("questlab-events-filter.jsonl", rows)
        read = EVENTS.read_inputs([path], strict=True)
        filtered = EVENTS.filter_records(
            read.records,
            schools={"harvest"},
            events={"resource_damaged"},
            target="beech",
            since=EVENTS.parse_filter_time("2026-08-09T16:00:03Z", "since"),
            until=EVENTS.parse_filter_time("2026-08-09T16:00:03Z", "until"),
        )
        self.assertEqual([row.sequence for row in filtered], [3])

    def test_private_fields_are_redacted_by_default(self) -> None:
        item = event(1)
        item["fields"] = {"amount": "10", "player_name": "Derek", "chat_text": "secret"}
        path = self.jsonl("questlab-events-private.jsonl", [header(), item])
        read = EVENTS.read_inputs([path], strict=True)
        report = EVENTS.build_report(read, read.records, filters={})
        encoded = json.dumps(report)
        self.assertIn("amount", encoded)
        self.assertNotIn("Derek", encoded)
        self.assertNotIn("secret", encoded)
        self.assertEqual(report["privacy"]["redacted_field_values"], 2)

    def test_stable_identity_collision_fails_without_disclosing_identity(self) -> None:
        path = self.jsonl(
            "questlab-events-collision.jsonl",
            [header(), event(1), event(2, target="Troll")],
        )
        read = EVENTS.read_inputs([path], strict=True)
        with self.assertRaises(EVENTS.EventExportError) as caught:
            EVENTS.build_report(read, read.records, filters={})
        self.assertIn("stable action identity collision sha256:", str(caught.exception))
        self.assertNotIn("action-1", str(caught.exception))

    def test_csv_and_xlsx_force_formula_like_targets_to_literal_text(self) -> None:
        item = event(1, target="  =IMPORTXML(\"https://bad.invalid\")")
        path = self.jsonl("questlab-events-formula.jsonl", [header(), item])
        report = self.report([path])
        parsed = list(csv.DictReader(io.StringIO(EVENTS.render_csv(report))))
        self.assertTrue(parsed[0]["target"].startswith("'="))
        workbook = EVENTS.make_xlsx(report)
        with zipfile.ZipFile(io.BytesIO(workbook)) as archive:
            names = set(archive.namelist())
            self.assertIn("xl/workbook.xml", names)
            workbook_xml = archive.read("xl/workbook.xml").decode("utf-8")
            for expected in ("Events", "Summary", "Metadata", "Raw Witnesses"):
                self.assertIn(f'name="{expected}"', workbook_xml)
            events_xml = archive.read("xl/worksheets/sheet1.xml").decode("utf-8")
            self.assertNotIn("<f>", events_xml)
            self.assertIn("'=IMPORTXML", events_xml)
            ElementTree.fromstring(events_xml)

    def test_bundle_has_documented_sheets_tables_and_json(self) -> None:
        path = self.jsonl("questlab-events-bundle.jsonl", [header(), event(1)])
        report = self.report([path])
        with zipfile.ZipFile(io.BytesIO(EVENTS.make_bundle(report))) as archive:
            self.assertEqual(
                set(archive.namelist()),
                {
                    "questlab-events.xlsx",
                    "tables/events.csv",
                    "tables/summary.csv",
                    "tables/metadata.csv",
                    "tables/raw-witnesses.csv",
                    "questlab-events.json",
                    "README.txt",
                },
            )
            metadata = archive.read("tables/metadata.csv").decode("utf-8-sig")
            self.assertIn("generated_utc", metadata)

    def test_workbook_and_bundle_bounds_fail_before_building(self) -> None:
        path = self.jsonl("questlab-events-bounds.jsonl", [header(), event(1)])
        report = self.report([path])
        report["actions"] = report["actions"] * (EVENTS.MAX_SPREADSHEET_ROWS + 1)
        with self.assertRaisesRegex(EVENTS.EventExportError, "spreadsheet limit"):
            EVENTS.make_xlsx(report)
        with self.assertRaisesRegex(EVENTS.EventExportError, "spreadsheet limit"):
            EVENTS.make_bundle(report)

        report = self.report([path])
        original = EVENTS.MAX_WORKBOOK_EXPANDED_BYTES
        try:
            EVENTS.MAX_WORKBOOK_EXPANDED_BYTES = 1
            with self.assertRaisesRegex(EVENTS.EventExportError, "expanded workbook size"):
                EVENTS.make_xlsx(report)
        finally:
            EVENTS.MAX_WORKBOOK_EXPANDED_BYTES = original

    def test_time_only_timestamp_is_rejected_descriptively(self) -> None:
        item = event(1)
        item["timestampUtc"] = "16:00:01"
        path = self.jsonl("questlab-events-time.jsonl", [header(), item])
        with self.assertRaisesRegex(EVENTS.EventExportError, "time-only"):
            EVENTS.read_inputs([path], strict=True)


if __name__ == "__main__":
    unittest.main()

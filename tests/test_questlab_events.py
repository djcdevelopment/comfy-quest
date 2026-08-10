from __future__ import annotations

import csv
import copy
import importlib.util
import io
import json
import sys
import tempfile
import unittest
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from unittest import mock
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
        "runtimeProfile": "extended",
        "runtimeProfileSemantics": "startup-default",
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


def session_end(event_count: int, segments: int = 1, dropped: int = 0) -> dict:
    return {
        "schema": EVENTS.ARCHIVE_SCHEMA,
        "recordType": "sessionEnd",
        "sessionId": SESSION,
        "releaseId": "questlab-r24",
        "runtimeProfile": "extended",
        "runtimeProfileSemantics": "startup-default",
        "startedUtc": "2026-08-09T16:00:00.123Z",
        "endedUtc": "2026-08-09T16:01:00.000Z",
        "eventCount": event_count,
        "droppedEventCount": dropped,
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

    def test_retained_final_segment_is_explicitly_partial_and_end_drops_are_loss(self) -> None:
        retained = self.jsonl(
            "questlab-events-session-part002.jsonl",
            [header(2), event(2), session_end(2, 2, dropped=3)],
        )
        read = EVENTS.read_inputs([retained], strict=True)
        report = EVENTS.build_report(read, read.records, filters={})
        self.assertEqual(report["totals"]["partial_sessions"], 1)
        self.assertEqual(report["totals"]["dropped_event_count"], 3)
        self.assertTrue(report["totals"]["data_loss_detected"])
        self.assertIn("1 retention-partial session(s)", EVENTS.render_summary(report))

    def test_jsonl_and_csv_mirrors_do_not_double_count(self) -> None:
        item = event(1)
        source = self.jsonl("questlab-events-session.jsonl", [header(), item])
        projection = self.csv_archive("questlab-events-session.csv", [item])
        read = EVENTS.read_inputs([source, projection], strict=True)
        report = EVENTS.build_report(read, read.records, filters={})
        self.assertEqual(len(read.records), 1)
        self.assertEqual(read.duplicate_input_records, 1)
        self.assertEqual(report["totals"]["raw_witnesses"], 1)

    def test_repeated_sequence_in_the_same_authoritative_format_is_corruption(self) -> None:
        path = self.jsonl(
            "questlab-events-duplicate-sequence.jsonl",
            [header(), event(1), event(1)],
        )
        with self.assertRaisesRegex(EVENTS.EventExportError, "duplicate jsonl witness identity"):
            EVENTS.read_inputs([path], strict=True)

    def test_formula_neutralized_csv_is_the_same_authoritative_jsonl_witness(self) -> None:
        item = event(1, target="\u0080=IMPORTXML(\"https://bad.invalid\")", action="@action-1")
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

        earlier = self.root / "questlab-events-crash-earlier.jsonl"
        earlier.write_text(json.dumps(header()) + "\n" + '{"schema":"comfy', encoding="utf-8")
        final = self.jsonl(
            "questlab-events-crash-earlier-part002.jsonl",
            [header(2), event(2)],
        )
        with self.assertRaisesRegex(EVENTS.EventExportError, "invalid JSON"):
            EVENTS.read_inputs(
                [earlier, final], strict=True, allow_truncated_tail=True
            )

    def test_archive_notice_preserves_bounded_queue_loss_in_summary(self) -> None:
        path = self.jsonl(
            "questlab-events-overflow.jsonl",
            [header(), event(1), archive_notice(7, 7), archive_notice(2, 9), session_end(1, dropped=9)],
        )
        report = self.report([path])
        self.assertEqual(report["totals"]["archive_notices"], 2)
        self.assertEqual(report["totals"]["dropped_event_count"], 9)
        self.assertTrue(report["totals"]["data_loss_detected"])
        metadata = {row["key"]: row["value"] for row in report["metadata"]}
        self.assertEqual(metadata["dropped_event_count"], "9")

    def test_strict_json_rejects_coerced_writer_types(self) -> None:
        bad_header = header()
        bad_header["startedUtc"] = 1786291200
        bad_event_text = event(1)
        bad_event_text["school"] = 7
        bad_event_sequence = event(1)
        bad_event_sequence["sequence"] = 1.5
        bad_end = session_end(1)
        bad_end["eventCount"] = "1"
        cases = (
            ([bad_header, event(1)], "startedUtc must be an ISO-8601 UTC string"),
            ([header(), bad_event_text], "school must be a string"),
            ([header(), bad_event_sequence], "sequence must be a positive integer"),
            ([header(), event(1), bad_end], "eventCount must be a non-negative integer"),
        )
        for index, (rows, expected) in enumerate(cases):
            with self.subTest(expected=expected):
                path = self.jsonl(f"questlab-events-bad-type-{index}.jsonl", rows)
                with self.assertRaisesRegex(EVENTS.EventExportError, expected):
                    EVENTS.read_inputs([path], strict=True)

    def test_strict_json_rejects_alias_collisions_and_privacy_flag_mismatches(self) -> None:
        alias_rows = (
            ({**header(), "session_id": "shadow"}, "strict session record contains colliding keys"),
            ({**event(1), "eventName": "shadow"}, "strict event record contains unsupported field.*eventName"),
            ({**session_end(1), "event_count": 999}, "strict sessionEnd record contains colliding keys"),
            ({**archive_notice(1, 1), "session_id": "shadow"}, "strict archiveNotice record contains colliding keys"),
        )
        for index, (bad_row, expected) in enumerate(alias_rows):
            if bad_row.get("recordType") == "session":
                rows = [bad_row, event(1)]
            elif bad_row.get("recordType") == "event":
                rows = [header(), bad_row]
            elif bad_row.get("recordType") == "sessionEnd":
                rows = [header(), event(1), bad_row]
            else:
                rows = [header(), event(1), bad_row]
            with self.subTest(expected=expected):
                path = self.jsonl(f"questlab-events-alias-collision-{index}.jsonl", rows)
                with self.assertRaisesRegex(EVENTS.EventExportError, expected):
                    EVENTS.read_inputs([path], strict=True)

        private_off = header()
        private_off["fields"] = {"details": False, "diagnosticIdentity": False}
        leaked = event(1)
        with self.assertRaisesRegex(EVENTS.EventExportError, "detail presence disagrees"):
            EVENTS.read_inputs(
                [self.jsonl("questlab-events-private-flag-leak.jsonl", [private_off, leaked])],
                strict=True,
            )

        exact = event(1)
        exact.pop("detail")
        exact.pop("diagnosticSeam")
        exact.pop("actionIdentity")
        read = EVENTS.read_inputs(
            [self.jsonl("questlab-events-private-flags-off.jsonl", [private_off, exact])],
            strict=True,
        )
        self.assertEqual(len(read.records), 1)

        unsupported = event(1)
        unsupported["fields"] = {"email": "creator@example.invalid"}
        with self.assertRaisesRegex(EVENTS.EventExportError, "unsupported field.*fields"):
            EVENTS.read_inputs(
                [self.jsonl("questlab-events-unsupported-fields.jsonl", [header(), unsupported])],
                strict=True,
            )

    def test_strict_session_sequences_segments_and_drop_notices_reconcile(self) -> None:
        gap = self.jsonl(
            "questlab-events-sequence-gap.jsonl",
            [header(), event(1), event(3), session_end(2)],
        )
        with self.assertRaisesRegex(EVENTS.EventExportError, "sequence has an unexplained gap"):
            EVENTS.read_inputs([gap], strict=True)

        duplicate_a = self.jsonl("questlab-events-duplicate-a.jsonl", [header(), event(1)])
        duplicate_b = self.jsonl("questlab-events-duplicate-b.jsonl", [header(), event(2)])
        with self.assertRaisesRegex(EVENTS.EventExportError, "duplicate archive segment 1"):
            EVENTS.read_inputs([duplicate_a, duplicate_b], strict=True)

        wrong_delta = self.jsonl(
            "questlab-events-wrong-drop-delta.jsonl",
            [header(), event(1), archive_notice(7, 7), archive_notice(2, 10)],
        )
        with self.assertRaisesRegex(EVENTS.EventExportError, "delta disagrees"):
            EVENTS.read_inputs([wrong_delta], strict=True)

        nonmonotonic = self.jsonl(
            "questlab-events-nonmonotonic-drops.jsonl",
            [header(), event(1), archive_notice(7, 7), archive_notice(2, 7)],
        )
        with self.assertRaisesRegex(EVENTS.EventExportError, "totals must increase"):
            EVENTS.read_inputs([nonmonotonic], strict=True)

    def test_active_retained_session_is_partial_and_empty_clean_session_is_valid(self) -> None:
        retained = self.jsonl("questlab-events-active-part002.jsonl", [header(2), event(2)])
        read = EVENTS.read_inputs([retained], strict=True)
        report = EVENTS.build_report(read, read.records, filters={})
        self.assertIn(SESSION, read.partial_session_ids)
        self.assertTrue(report["totals"]["data_loss_detected"])

        empty = self.jsonl("questlab-events-empty.jsonl", [header(), session_end(0)])
        empty_read = EVENTS.read_inputs([empty], strict=True)
        empty_report = EVENTS.build_report(empty_read, empty_read.records, filters={})
        self.assertEqual(empty_report["totals"]["sessions"], 1)
        self.assertEqual(empty_report["totals"]["clean_shutdown_sessions"], 1)
        self.assertEqual(empty_report["totals"]["canonical_actions"], 0)

    def test_directory_fast_path_prefers_jsonl_and_mirror_requires_exact_detail(self) -> None:
        item = event(1)
        source = self.jsonl("questlab-events-directory.jsonl", [header(), item])
        projection = self.csv_archive("questlab-events-directory.csv", [item])
        read = EVENTS.read_inputs([self.root], strict=True)
        self.assertEqual(read.input_files, 1)
        self.assertEqual(read.duplicate_input_records, 0)

        blank = dict(item)
        blank["detail"] = ""
        projection.unlink()
        projection = self.csv_archive("questlab-events-directory.csv", [blank])
        with self.assertRaisesRegex(EVENTS.EventExportError, "witness identity collision"):
            EVENTS.read_inputs([source, projection], strict=True)

    def test_strict_session_end_must_match_identity_counts_drops_and_be_final(self) -> None:
        corruptions = []
        wrong_identity = session_end(1)
        wrong_identity["runtimeProfile"] = "diagnostic"
        corruptions.append(([header(), event(1), wrong_identity], "runtimeProfile disagrees"))
        wrong_semantics = session_end(1)
        wrong_semantics["runtimeProfileSemantics"] = "per-row"
        corruptions.append((
            [header(), event(1), wrong_semantics],
            "runtimeProfileSemantics must be 'startup-default'",
        ))
        corruptions.append(([header(), event(1), session_end(999)], "eventCount disagrees"))
        corruptions.append((
            [header(), event(1), archive_notice(2, 2), session_end(1, dropped=0)],
            "droppedEventCount is smaller",
        ))
        corruptions.append(([header(), event(1), session_end(1), event(2)], "after sessionEnd"))

        for index, (rows, expected) in enumerate(corruptions):
            with self.subTest(expected=expected):
                path = self.jsonl(f"questlab-events-corrupt-end-{index}.jsonl", rows)
                with self.assertRaisesRegex(EVENTS.EventExportError, expected):
                    EVENTS.read_inputs([path], strict=True)

        first = self.jsonl(
            "questlab-events-duplicate-end.jsonl",
            [header(1), event(1), session_end(2, 2)],
        )
        final = self.jsonl(
            "questlab-events-duplicate-end-part002.jsonl",
            [header(2), event(2), session_end(2, 2)],
        )
        with self.assertRaisesRegex(EVENTS.EventExportError, "duplicate sessionEnd"):
            EVENTS.read_inputs([first, final], strict=True)

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
        item["fields"] = {
            "amount": "10",
            "player_name": "Derek",
            "chat_text": "secret",
            "payload": {
                "safe": "retained",
                "player_name": "Nested Derek",
                "items": [{"email": "creator@example.invalid"}],
            },
        }
        path = self.jsonl("questlab-events-private.jsonl", [header(), item])
        read = EVENTS.read_inputs([path])
        report = EVENTS.build_report(read, read.records, filters={})
        encoded = json.dumps(report)
        self.assertIn("amount", encoded)
        self.assertNotIn("Derek", encoded)
        self.assertNotIn("secret", encoded)
        self.assertNotIn("creator@example.invalid", encoded)
        self.assertIn("retained", encoded)
        self.assertEqual(report["privacy"]["redacted_field_values"], 4)

    def test_streaming_line_and_total_bounds_do_not_trust_preflight_stat_only(self) -> None:
        path = self.jsonl("questlab-events-bounded.jsonl", [header(), event(1)])
        with mock.patch.object(EVENTS, "MAX_LINE_BYTES", 64), self.assertRaisesRegex(
            EVENTS.EventExportError, "line exceeds"
        ):
            EVENTS.read_inputs([path], strict=True)

        with (
            mock.patch.object(EVENTS, "MAX_TOTAL_BYTES", 100),
            mock.patch.object(EVENTS, "collect_paths", return_value=[path]),
            self.assertRaisesRegex(EVENTS.EventExportError, "total limit while reading"),
        ):
            EVENTS.read_inputs([path], strict=True)

    def test_stable_identity_collision_fails_without_disclosing_identity(self) -> None:
        path = self.jsonl(
            "questlab-events-collision.jsonl",
            [header(), event(1), event(2, target="Troll")],
        )
        with self.assertRaises(EVENTS.EventExportError) as caught:
            EVENTS.read_inputs([path], strict=True)
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

        self.assertEqual(EVENTS.spreadsheet_safe("\x01=hidden"), "'\x01=hidden")

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

    def test_xlsx_replaces_xml_invalid_scalars_before_size_and_render(self) -> None:
        path = self.jsonl("questlab-events-xml-scalar.jsonl", [header(), event(1)])
        report = self.report([path])
        report["actions"][0]["target"] = "bad\uFFFE\ud800value"
        report["witnesses"][0]["target"] = "bad\uFFFE\ud800value"
        workbook = EVENTS.make_xlsx(report)
        with zipfile.ZipFile(io.BytesIO(workbook)) as archive:
            for name in (
                "xl/worksheets/sheet1.xml",
                "xl/worksheets/sheet4.xml",
            ):
                content = archive.read(name).decode("utf-8")
                ElementTree.fromstring(content)
                self.assertIn("bad\uFFFD\uFFFDvalue", content)

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

        escaped = self.report([path])
        escaped["actions"][0]["target"] = "&" * 2000
        escaped["witnesses"][0]["target"] = "&" * 2000
        plain = copy.deepcopy(escaped)
        plain["actions"][0]["target"] = "x" * 2000
        plain["witnesses"][0]["target"] = "x" * 2000
        escaped_size = EVENTS.spreadsheet_size_estimate(escaped)
        self.assertGreater(escaped_size, EVENTS.spreadsheet_size_estimate(plain) + 12000)
        original = EVENTS.MAX_WORKBOOK_EXPANDED_BYTES
        try:
            EVENTS.MAX_WORKBOOK_EXPANDED_BYTES = escaped_size - 1
            with self.assertRaisesRegex(EVENTS.EventExportError, "expanded workbook size"):
                EVENTS.make_xlsx(escaped)
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

"""Executable contracts for the local-first Quest Lab Sheets companion."""

from __future__ import annotations

import csv
import hashlib
import importlib.util
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[1]
TOOL = ROOT / "tools" / "questlab-sheets" / "questlab_sheets.py"
README = ROOT / "tools" / "questlab-sheets" / "README.md"
START = ROOT / "tools" / "questlab-sheets" / "Start-QuestLabSheets.ps1"
PANEL = ROOT / "network" / "mod" / "ComfyQuestLab" / "Ui" / "LabPanel.cs"

SPEC = importlib.util.spec_from_file_location("questlab_sheets", TOOL)
SHEETS = importlib.util.module_from_spec(SPEC)
assert SPEC.loader
sys.modules[SPEC.name] = SHEETS
SPEC.loader.exec_module(SHEETS)


def header(session: str = "20260809T120000Z-demo", segment: int = 1) -> dict:
    return {
        "schema": SHEETS.SCHEMA,
        "recordType": "session",
        "sessionId": session,
        "startedUtc": "2026-08-09T12:00:00Z",
        "releaseId": "questlab-v0.2.0-test",
        "runtimeProfile": "extended",
        "segment": segment,
        "fields": {"details": True, "diagnosticIdentity": False},
    }


def event(sequence: int, **changes: object) -> dict:
    value = {
        "schema": SHEETS.SCHEMA,
        "recordType": "event",
        "sessionId": "20260809T120000Z-demo",
        "sequence": sequence,
        "timestampUtc": f"2026-08-09T12:00:{sequence:02d}Z",
        "school": "harvest",
        "creatorEvent": "resource_damaged",
        "target": "Birch",
        "detail": "bronze axe",
        "usability": "today",
    }
    value.update(changes)
    return value


def session_end(*, event_count: int, segments: int, dropped: int = 0) -> dict:
    return {
        "schema": SHEETS.SCHEMA,
        "recordType": "sessionEnd",
        "sessionId": "20260809T120000Z-demo",
        "releaseId": "questlab-v0.2.0-test",
        "runtimeProfile": "extended",
        "startedUtc": "2026-08-09T12:00:00Z",
        "endedUtc": "2026-08-09T12:10:00Z",
        "eventCount": event_count,
        "droppedEventCount": dropped,
        "segments": segments,
        "reason": "clean-shutdown",
    }


def archive_notice(total: int, since_last: int | None = None) -> dict:
    return {
        "schema": SHEETS.SCHEMA,
        "recordType": "archiveNotice",
        "sessionId": "20260809T120000Z-demo",
        "timestampUtc": "2026-08-09T12:05:00Z",
        "reason": "queue-capacity",
        "droppedSinceLastNotice": total if since_last is None else since_last,
        "totalDroppedEventCount": total,
    }


def write_jsonl(path: Path, records: list[dict]) -> None:
    path.write_text("".join(json.dumps(row, separators=(",", ":")) + "\n" for row in records), encoding="utf-8")


class FakeRequest:
    def __init__(self, response: object = None, failure: Exception | None = None):
        self.response = response if response is not None else {}
        self.failure = failure

    def execute(self) -> object:
        if self.failure:
            raise self.failure
        return self.response


class FakeValues:
    def __init__(self, calls: list[tuple]):
        self.calls = calls

    def batchUpdate(self, **kwargs: object) -> FakeRequest:  # noqa: N802
        self.calls.append(("values.batchUpdate", kwargs))
        return FakeRequest({"totalUpdatedRows": 1})


class FakeSpreadsheets:
    def __init__(self, calls: list[tuple], spreadsheet_id: str = "safe_sheet_id_12345"):
        self.calls = calls
        self.spreadsheet_id = spreadsheet_id
        self._values = FakeValues(calls)

    def create(self, **kwargs: object) -> FakeRequest:
        self.calls.append(("create", kwargs))
        return FakeRequest({"spreadsheetId": self.spreadsheet_id})

    def values(self) -> FakeValues:
        return self._values

    def batchUpdate(self, **kwargs: object) -> FakeRequest:  # noqa: N802
        self.calls.append(("format.batchUpdate", kwargs))
        return FakeRequest({})


class FakeService:
    def __init__(self, spreadsheet_id: str = "safe_sheet_id_12345"):
        self.calls: list[tuple] = []
        self._spreadsheets = FakeSpreadsheets(self.calls, spreadsheet_id)

    def spreadsheets(self) -> FakeSpreadsheets:
        return self._spreadsheets


class FakeProtector:
    def protect(self, plain: bytes) -> bytes:
        return b"protected:" + plain[::-1]

    def unprotect(self, protected: bytes) -> bytes:
        assert protected.startswith(b"protected:")
        return protected[len(b"protected:"):][::-1]


class QuestLabSheetsTests(unittest.TestCase):
    def make_session(self, root: Path) -> SHEETS.EventSession:
        first = root / "questlab-events-20260809T120000Z-demo.jsonl"
        second = root / "questlab-events-20260809T120000Z-demo-part002.jsonl"
        write_jsonl(first, [header(), event(1), event(2, school="combat", creatorEvent="kill", target="Greyling")])
        write_jsonl(second, [header(segment=2), event(3, detail="=IMPORTXML(\"https://bad\")"), session_end(event_count=3, segments=2)])
        return SHEETS.parse_session([first, second])

    def test_parser_combines_parts_and_emits_stable_summary(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            session = self.make_session(root)
            self.assertEqual(session.session_id, "20260809T120000Z-demo")
            self.assertEqual([row.sequence for row in session.events], [1, 2, 3])
            self.assertEqual(session.source_files, (
                "questlab-events-20260809T120000Z-demo.jsonl",
                "questlab-events-20260809T120000Z-demo-part002.jsonl",
            ))
            self.assertEqual(session.archive_state, "complete")
            self.assertEqual(session.runtime_profile, "extended")
            payload = SHEETS.inspect_payload(session)
            self.assertEqual(payload["eventRows"], 3)
            self.assertEqual(payload["schoolCounts"], {"combat": 1, "harvest": 2})
            self.assertRegex(payload["sourceSha256"], r"^[0-9a-f]{64}$")

    def test_parser_refuses_gaps_duplicates_schema_drift_and_missing_header(self) -> None:
        cases = [
            ([header(), event(2)], "sequence has a gap"),
            ([header(), event(1), event(1)], "duplicate sequence"),
            ([header(), {**event(1), "schema": "wrong"}], "expected schema"),
            ([event(1)], "before the session header"),
        ]
        for records, expected in cases:
            with self.subTest(expected=expected), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / "questlab-events-session.jsonl"
                write_jsonl(path, records)
                with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, expected):
                    SHEETS.parse_session([path])

    def test_archive_notice_and_clean_end_are_metadata_not_event_rows(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "questlab-events-session.jsonl"
            write_jsonl(path, [
                header(), event(1), archive_notice(2), event(2),
                session_end(event_count=2, segments=1, dropped=2),
            ])
            session = SHEETS.parse_session([path])
            self.assertEqual(len(session.events), 2)
            self.assertEqual(session.archive_state, "complete-with-drops")
            self.assertEqual(session.archive_notice_count, 1)
            self.assertEqual(session.dropped_event_count, 2)
            self.assertIn("archive queue dropped 2 event(s)", session.warnings)

    def test_missing_session_end_remains_exportable_but_explicitly_incomplete(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "questlab-events-active.jsonl"
            write_jsonl(path, [header(), event(1)])
            session = SHEETS.parse_session([path])
            self.assertEqual(session.archive_state, "active-or-unclean")
            self.assertTrue(any("sessionEnd is absent" in warning for warning in session.warnings))
            metadata = SHEETS.workbook_values(session, "2026-08-09T13:00:00Z")["Metadata"]
            self.assertIn(["Archive state", "active-or-unclean"], metadata)

    def test_incomplete_final_line_is_an_explicit_crash_tail(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "questlab-events-20260809T120000Z-demo.jsonl"
            write_jsonl(path, [header(), event(1)])
            with path.open("ab") as output:
                output.write(b'{"schema":"comfy-questlab-events/v1","recordType":"event"')
            session = SHEETS.parse_session([path])
            self.assertEqual([row.sequence for row in session.events], [1])
            self.assertEqual(session.archive_state, "active-or-unclean")
            self.assertTrue(session.crash_tail)
            self.assertTrue(any("crash tail detected" in warning for warning in session.warnings))
            payload = SHEETS.inspect_payload(session)
            self.assertTrue(payload["crashTail"])
            metadata = SHEETS.workbook_values(session, "2026-08-09T13:00:00Z")["Metadata"]
            self.assertIn(["Crash tail detected", "yes"], metadata)

    def test_newline_terminated_or_nonfinal_malformed_rows_still_fail(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            newline_terminated = root / "questlab-events-newline.jsonl"
            write_jsonl(newline_terminated, [header(), event(1)])
            with newline_terminated.open("ab") as output:
                output.write(b'{"schema":\n')
            with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, "invalid UTF-8 JSON"):
                SHEETS.parse_session([newline_terminated])

            first = root / "questlab-events-20260809T120000Z-demo.jsonl"
            second = root / "questlab-events-20260809T120000Z-demo-part002.jsonl"
            write_jsonl(first, [header(), event(1)])
            with first.open("ab") as output:
                output.write(b'{"schema":')
            write_jsonl(second, [header(segment=2), event(2)])
            with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, "invalid UTF-8 JSON"):
                SHEETS.parse_session([first, second])

    def test_active_session_surfaces_notice_drop_count_without_session_end(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "questlab-events-active.jsonl"
            write_jsonl(path, [header(), event(1), archive_notice(3)])
            session = SHEETS.parse_session([path])
            self.assertEqual(session.archive_state, "active-or-unclean")
            self.assertEqual(session.dropped_event_count, 3)
            self.assertIn("archive queue dropped at least 3 event(s) before this tail", session.warnings)

    def test_session_end_cannot_undercount_retained_rows_or_observed_segments(self) -> None:
        cases = (
            ([header(), event(1), event(2), session_end(event_count=1, segments=1)], "below the retained"),
            ([header(segment=2), event(1), session_end(event_count=1, segments=1)], "below an observed"),
        )
        for records, expected in cases:
            with self.subTest(expected=expected), tempfile.TemporaryDirectory() as temporary:
                suffix = "-part002" if records[0]["segment"] == 2 else ""
                path = Path(temporary) / f"questlab-events-20260809T120000Z-demo{suffix}.jsonl"
                write_jsonl(path, records)
                with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, expected):
                    SHEETS.parse_session([path])

    def test_missing_retained_segments_are_partial_not_silently_complete(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "questlab-events-20260809T120000Z-demo-part002.jsonl"
            write_jsonl(path, [header(segment=2), event(3), session_end(event_count=3, segments=2)])
            session = SHEETS.parse_session([path])
            self.assertEqual(session.archive_state, "partial")
            self.assertEqual(session.observed_segments, (2,))
            self.assertTrue(any("segments" in warning for warning in session.warnings))

    def test_filename_and_header_segment_must_agree(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "questlab-events-20260809T120000Z-demo-part002.jsonl"
            write_jsonl(path, [header(segment=3), event(1)])
            with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, "disagrees with the filename"):
                SHEETS.parse_session([path])

    def test_event_private_fields_must_match_the_header_policy(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "questlab-events-session.jsonl"
            private_header = header()
            private_header["fields"] = {"details": False, "diagnosticIdentity": False}
            with_detail = event(1)
            write_jsonl(path, [private_header, with_detail])
            with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, "detail presence disagrees"):
                SHEETS.parse_session([path])

            private_header["fields"] = {"details": True, "diagnosticIdentity": True}
            missing_identity = event(1)
            write_jsonl(path, [private_header, missing_identity])
            with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, "diagnosticSeam presence disagrees"):
                SHEETS.parse_session([path])

    def test_dashboard_discovers_only_named_sessions_below_fixed_root(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.make_session(root)
            write_jsonl(root / "not-a-session.jsonl", [header(), event(1)])
            listings = SHEETS.discover_sessions(root)
            self.assertEqual(len(listings), 1)
            dashboard = SHEETS.Dashboard(root, root / "state")
            self.assertEqual(dashboard.selected(listings[0].key).session_id, listings[0].session.session_id)
            with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, "no longer exists"):
                dashboard.selected("../../outside")

    def test_active_append_keeps_selection_stable_and_reparses_current_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path = root / "questlab-events-20260809T120000Z-demo.jsonl"
            write_jsonl(path, [header(), event(1)])
            dashboard = SHEETS.Dashboard(root, root / "state")
            rendered = dashboard.listings()[0]
            assert rendered.session is not None
            old_sha = rendered.session.source_sha256
            with path.open("a", encoding="utf-8", newline="") as output:
                output.write(json.dumps(event(2), separators=(",", ":")) + "\n")
            selected = dashboard.selected(rendered.key)
            self.assertEqual([row.sequence for row in selected.events], [1, 2])
            self.assertNotEqual(selected.source_sha256, old_sha)
            self.assertEqual(dashboard.listings()[0].key, rendered.key)

    def test_ambiguous_selection_identity_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            session = self.make_session(root)
            listing = SHEETS.SessionListing(key="same", session=session)
            dashboard = SHEETS.Dashboard(root, root / "state")
            with mock.patch.object(dashboard, "listings", return_value=[listing, listing]):
                with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, "ambiguous"):
                    dashboard.selected("same")

    def test_source_digest_is_computed_from_the_single_parse_stream(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path = root / "questlab-events-20260809T120000Z-demo.jsonl"
            write_jsonl(path, [header(), event(1)])
            real_open = Path.open
            binary_reads = 0

            def counted_open(candidate: Path, *args: object, **kwargs: object):
                nonlocal binary_reads
                if candidate == path and args and args[0] == "rb":
                    binary_reads += 1
                return real_open(candidate, *args, **kwargs)

            with mock.patch.object(Path, "open", new=counted_open):
                session = SHEETS.parse_session([path])
            expected = hashlib.sha256(path.name.encode("utf-8") + b"\0" + path.read_bytes()).hexdigest()
            self.assertEqual(session.source_sha256, expected)
            self.assertEqual(binary_reads, 1)

    def test_one_malformed_archive_does_not_hide_healthy_dashboard_sessions(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.make_session(root)
            broken = root / "questlab-events-broken.jsonl"
            write_jsonl(broken, [{**header("broken"), "sessionId": "different"}])
            listings = SHEETS.discover_sessions(root)
            self.assertEqual(sum(item.session is not None for item in listings), 1)
            self.assertEqual(sum(item.session is None for item in listings), 1)
            self.assertIn("filename does not agree", next(item.error for item in listings if item.session is None))
            rendered = SHEETS.dashboard_page(SHEETS.Dashboard(root, root / "state"))
            self.assertIn("Unreadable event session", rendered)
            self.assertIn("Export disabled", rendered)

    def test_csv_is_rfc4180_and_formula_shaped_text_is_neutralized(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            session = self.make_session(Path(temporary))
            rows = list(csv.reader(io.StringIO(SHEETS.csv_text(session))))
            self.assertEqual(tuple(rows[0]), SHEETS.CSV_COLUMNS)
            self.assertEqual(rows[3][7], "'=IMPORTXML(\"https://bad\")")
            self.assertEqual(len(rows), 4)

    def test_csv_neutralizes_formula_triggers_after_whitespace_and_controls(self) -> None:
        for value in ("  =SUM(A1:A2)", "\n+cmd", "\t@external", "\r-2+3", "\x01=hidden"):
            with self.subTest(value=value):
                self.assertEqual(SHEETS._csv_safe(value), "'" + value)

    def test_workbook_has_events_summary_metadata_and_utc_counts(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            session = self.make_session(Path(temporary))
            workbook = SHEETS.workbook_values(session, "2026-08-09T13:00:00Z")
            self.assertEqual(set(workbook), {"Events", "Summary", "Metadata"})
            self.assertEqual(tuple(workbook["Events"][0]), SHEETS.CSV_COLUMNS)
            self.assertIn(["SCHOOL", "EVENT COUNT"], workbook["Summary"])
            self.assertIn(["combat", 1], workbook["Summary"])
            self.assertIn(["Write mode", "RAW (formula interpretation disabled)"], workbook["Metadata"])
            self.assertIn(["OAuth scope", SHEETS.DRIVE_FILE_SCOPE], workbook["Metadata"])

    def test_google_export_is_create_only_raw_batched_formatted_and_receipted(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            session = self.make_session(root)
            service = FakeService()
            with mock.patch.object(SHEETS, "utc_now", return_value="2026-08-09T13:00:00Z"):
                result = SHEETS.GoogleSheetsExporter(service, root / "receipts").export(session)
            self.assertEqual(result.rows_written, 3)
            self.assertEqual(result.spreadsheet_url, "https://docs.google.com/spreadsheets/d/safe_sheet_id_12345/edit")
            names = [name for name, _ in service.calls]
            self.assertEqual(names[0], "create")
            self.assertEqual(names[-1], "format.batchUpdate")
            self.assertEqual(names.count("format.batchUpdate"), 1)
            value_calls = [kwargs for name, kwargs in service.calls if name == "values.batchUpdate"]
            self.assertTrue(value_calls)
            self.assertTrue(all(call["body"]["valueInputOption"] == "RAW" for call in value_calls))
            create_body = service.calls[0][1]["body"]
            self.assertEqual([sheet["properties"]["title"] for sheet in create_body["sheets"]], ["Events", "Summary", "Metadata"])
            receipt = json.loads(Path(result.receipt_path).read_text(encoding="utf-8"))
            self.assertEqual(receipt["sourceSha256"], session.source_sha256)
            self.assertEqual(receipt["oauthScope"], SHEETS.DRIVE_FILE_SCOPE)
            self.assertNotIn("token", json.dumps(receipt).lower())

    def test_google_sheet_identifier_and_url_are_not_an_arbitrary_redirect(self) -> None:
        self.assertEqual(
            SHEETS.google_sheet_url("safe_sheet_id_12345"),
            "https://docs.google.com/spreadsheets/d/safe_sheet_id_12345/edit",
        )
        for invalid in ("https://evil.example", "../bad", "short", "a/b"):
            with self.subTest(invalid=invalid), self.assertRaises(SHEETS.QuestLabSheetsError):
                SHEETS.google_sheet_url(invalid)

    def test_value_batches_stay_under_internal_payload_limit(self) -> None:
        rows = [[str(index), "x" * 2000] for index in range(2000)]
        chunks = list(SHEETS.chunk_value_ranges("Events", rows))
        self.assertGreater(len(chunks), 1)
        self.assertEqual(sum(len(chunk["values"]) for chunk in chunks), len(rows))
        for chunk in chunks:
            encoded = json.dumps({"values": chunk["values"]}, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
            self.assertLessEqual(len(encoded), SHEETS.MAX_GOOGLE_REQUEST_BYTES)

    def test_desktop_client_validation_pins_google_endpoints(self) -> None:
        good = {
            "installed": {
                "client_id": "unit.apps.googleusercontent.com",
                "client_secret": "test-placeholder-not-a-real-secret",
                "auth_uri": "https://accounts.google.com/o/oauth2/auth",
                "token_uri": SHEETS.GOOGLE_TOKEN_URI,
                "redirect_uris": ["http://localhost"],
            }
        }
        with tempfile.TemporaryDirectory() as temporary:
            store = SHEETS.TokenStore(Path(temporary), protector=FakeProtector())
            store.state_dir.mkdir(exist_ok=True)
            store.client_path.write_text(json.dumps(good), encoding="utf-8")
            store.validate_client()
            bad = json.loads(json.dumps(good))
            bad["installed"]["token_uri"] = "https://evil.example/token"
            store.client_path.write_text(json.dumps(bad), encoding="utf-8")
            with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, "official Google token"):
                store.validate_client()
            bad = json.loads(json.dumps(good))
            bad["installed"]["redirect_uris"] = ["https://evil.example/callback"]
            store.client_path.write_text(json.dumps(bad), encoding="utf-8")
            with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, "stay on loopback"):
                store.validate_client()

    def test_saved_authorization_must_have_exactly_drive_file_scope(self) -> None:
        valid = {
            "scopes": [SHEETS.DRIVE_FILE_SCOPE],
            "token_uri": SHEETS.GOOGLE_TOKEN_URI,
            "client_id": "unit.apps.googleusercontent.com",
        }
        SHEETS.GoogleAuth._require_narrow_scope(valid)
        for scopes in (
            [],
            ["https://www.googleapis.com/auth/spreadsheets"],
            [SHEETS.DRIVE_FILE_SCOPE, "https://www.googleapis.com/auth/drive"],
        ):
            with self.subTest(scopes=scopes), self.assertRaisesRegex(
                SHEETS.QuestLabSheetsError, "exactly the per-file"
            ):
                SHEETS.GoogleAuth._require_narrow_scope({**valid, "scopes": scopes})
        with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, "official Google token"):
            SHEETS.GoogleAuth._require_narrow_scope({**valid, "token_uri": "https://evil.example/token"})
        with self.assertRaisesRegex(SHEETS.QuestLabSheetsError, "Google Desktop client"):
            SHEETS.GoogleAuth._require_narrow_scope({**valid, "client_id": "attacker.example"})

    def test_token_store_protects_round_trips_and_deletes_without_printing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            store = SHEETS.TokenStore(Path(temporary), protector=FakeProtector())
            plain = '{"refresh_token":"sensitive-test-value"}'
            store.save_token_text(plain)
            self.assertNotIn(b"sensitive-test-value", store.token_path.read_bytes())
            self.assertEqual(store.load_token_text(), plain)
            store.delete_token()
            self.assertFalse(store.token_path.exists())

    def test_oauth_and_dashboard_security_contract_is_explicit(self) -> None:
        source = TOOL.read_text(encoding="utf-8")
        for marker in (
            'DRIVE_FILE_SCOPE = "https://www.googleapis.com/auth/drive.file"',
            'host="127.0.0.1"',
            "port=0",
            "autogenerate_code_verifier=True",
            'ThreadingHTTPServer(("127.0.0.1", SHEETS_PORT)',
            "secrets.token_urlsafe(32)",
            "secrets.compare_digest(value, dashboard.csrf)",
            'valueInputOption": "RAW"',
            'GOOGLE_REVOKE_URI = "https://oauth2.googleapis.com/revoke"',
            "self.store.delete_token()",
            "path.resolve(strict=True)",
            "resolved.relative_to(root)",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, source)
        self.assertNotIn("0.0.0.0", source)
        self.assertNotIn("client_secret =", source)

    def test_panel_handoff_and_start_script_use_only_fixed_loopback_url(self) -> None:
        panel = PANEL.read_text(encoding="utf-8")
        start = START.read_text(encoding="utf-8")
        for marker in (
            'const string SheetsExporterUrl = "http://127.0.0.1:47631/";',
            'new GUIContent("Exports",',
            "Application.OpenURL(SheetsExporterUrl);",
        ):
            self.assertIn(marker, panel)
        self.assertIn("$PSScriptRoot", start)
        self.assertIn("http://127.0.0.1:47631/", start)
        self.assertNotIn("Invoke-Expression", start)

    def test_documentation_uses_only_official_google_design_sources(self) -> None:
        readme = README.read_text(encoding="utf-8")
        self.assertIn("developers.google.com/identity/protocols/oauth2/native-app", readme)
        self.assertIn("developers.google.com/workspace/sheets/api/reference/rest/v4/spreadsheets/create", readme)
        self.assertIn("developers.google.com/workspace/sheets/api/limits", readme)
        self.assertIn("support.google.com/a/answer/7281227", readme)
        self.assertNotIn("stackoverflow.com", readme.lower())


if __name__ == "__main__":
    unittest.main()

import importlib.util
import datetime as dt
import json
import os
import tempfile
import threading
import time
import unittest
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]
PROVIDER_PATH = ROOT / "tools" / "workbench-provider" / "comfy_quest_workbench.py"
SPEC = importlib.util.spec_from_file_location("comfy_quest_workbench", PROVIDER_PATH)
assert SPEC and SPEC.loader
PROVIDER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROVIDER)


class WorkbenchProviderTests(unittest.TestCase):
    def test_provider_exports_only_bounded_runtime_tools(self):
        self.assertEqual(
            [tool.__name__ for tool in PROVIDER.get_tools()],
            [
                "quest_runtime_status",
                "quest_runtime_receipts",
                "quest_runtime_creator_request",
                "quest_runtime_run_control",
            ],
        )
        source = PROVIDER_PATH.read_text(encoding="utf-8")
        for forbidden in ("subprocess", "SendKeys", "console", "shell=True", "eval(", "exec("):
            if forbidden == "console":
                self.assertNotIn("console command", source.lower())
            else:
                self.assertNotIn(forbidden, source)

    def test_status_redacts_participant_identity_and_reports_fixed_lanes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "active").mkdir()
            (root / "status").mkdir()
            (root / "inbox").mkdir()
            (root / "inbox-dev").mkdir()
            (root / "active" / "active-set.json").write_text(json.dumps({
                "schema": "comfy-quest-active/v1",
                "pack_id": "guild-safe",
                "content_hash": "a" * 64,
                "experience_id": "experience-a",
                "unexpected": "not exported",
            }), encoding="utf-8")
            (root / "status" / "runs.json").write_text(json.dumps({
                "observed_utc": "2026-08-27T12:00:00+00:00",
                "runs": [{
                    "run_id": "run-a",
                    "scope_id": "scope-a",
                    "experience_id": "experience-a",
                    "binding_zdo": "42:7",
                    "content_hash": "a" * 64,
                    "participant_ids": ["private-player-one", "private-player-two"],
                }],
            }), encoding="utf-8")
            (root / "status" / "world-entry.json").write_text(json.dumps({
                "schema": "comfy-quest-world-entry-receipt/v1",
                "request_id": "entry-a",
                "creator_session_id": "era17-session-a",
                "state": "entered",
                "detail": "world_entry_complete",
                "machine": "OMEN",
                "expected_world_uid": "-523956327",
                "world_uid": "-523956327",
                "character_name": "must not be exported",
            }), encoding="utf-8")
            (root / "inbox" / "guild-safe-1.0.0.questpack").write_bytes(b"pack")
            with patch.dict(os.environ, {
                "COMFY_QUEST_RUNTIME_ROOT": str(root),
                "COMFY_QUEST_PROVIDER_SHA256": "b" * 64,
            }):
                status = PROVIDER.quest_runtime_status()
            self.assertEqual("b" * 64, status["provider"]["release_sha256"])
            self.assertEqual(["guild-safe-1.0.0.questpack"], status["production_inbox"])
            self.assertEqual(2, status["runs"][0]["participant_count"])
            self.assertNotIn("participant_ids", status["runs"][0])
            self.assertNotIn("unexpected", status["active"])
            self.assertNotIn("runtime_root", status)
            self.assertEqual("-523956327", status["world_entry"]["world_uid"])
            self.assertEqual("era17-session-a", status["world_entry"]["creator_session_id"])
            self.assertNotIn("character_name", status["world_entry"])

    def test_provider_requires_the_release_profile_to_supply_its_runtime_mount(self):
        with patch.dict(os.environ, {}, clear=True):
            with self.assertRaisesRegex(RuntimeError, "COMFY_QUEST_RUNTIME_ROOT"):
                PROVIDER.quest_runtime_status()

    def test_receipts_are_bounded_filterable_and_do_not_descend_into_other_stores(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            receipts = root / "receipts"
            receipts.mkdir()
            for index, operation in enumerate(("check", "event", "event")):
                (receipts / f"20260827T12000{index}Z-{index}.json").write_text(json.dumps({
                    "schema": "comfy-quest-runtime-receipt/v1",
                    "id": f"receipt-{index}",
                    "operation": operation,
                    "status": "matched" if operation == "event" else "accepted",
                    "experience_id": "experience-a" if index != 1 else "experience-b",
                    "run_id": "run-a",
                    "event_target": "Wood",
                }), encoding="utf-8")
            nested = receipts / "creator-requests"
            nested.mkdir()
            (nested / "must-not-leak.json").write_text(json.dumps({
                "schema": "comfy-quest-runtime-receipt/v1",
                "operation": "event",
                "experience_id": "experience-a",
            }), encoding="utf-8")
            (receipts / "malformed.json").write_text("not json", encoding="utf-8")
            with patch.dict(os.environ, {"COMFY_QUEST_RUNTIME_ROOT": str(root)}):
                result = PROVIDER.quest_runtime_receipts(
                    limit=1, operation="event", experience_id="experience-a")
            self.assertEqual(1, len(result["receipts"]))
            self.assertEqual("receipt-2", result["receipts"][0]["id"])
            self.assertEqual(["malformed.json"], result["unreadable_receipts"])

    def test_creator_request_is_correlated_and_identity_pinned(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            mailbox = root / "requests" / "creator-request.json"
            receipts = root / "receipts" / "creator-requests"
            receipts.mkdir(parents=True)

            def runtime():
                request = self._wait_for_request(mailbox)
                mailbox.unlink()
                (receipts / f"{request['request_id']}.json").write_text(json.dumps({
                    "schema": "comfy-quest-runtime-request-receipt/v1",
                    "request_id": request["request_id"],
                    "operation": request["operation"],
                    "state": "completed",
                    "detail": "dev_channel_armed",
                    "machine": request["expected_machine"],
                    "world_uid": request["expected_world_uid"],
                    "creator_session_id": request["creator_session_id"],
                }), encoding="utf-8")

            thread = threading.Thread(target=runtime, daemon=True)
            thread.start()
            with patch.dict(os.environ, {"COMFY_QUEST_RUNTIME_ROOT": str(root)}):
                result = PROVIDER.quest_runtime_creator_request(
                    "arm", "OMEN", "-523956327", "era17-broken-way-r2", 5)
            thread.join(timeout=2)
            self.assertTrue(result["ok"])
            self.assertEqual("dev_channel_armed", result["receipt"]["detail"])
            self.assertEqual("era17-broken-way-r2", result["receipt"]["creator_session_id"])

    def test_creator_request_refuses_unknown_operation_and_occupied_mailbox(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            mailbox = root / "requests" / "creator-request.json"
            mailbox.parent.mkdir(parents=True)
            mailbox.write_text("{}", encoding="utf-8")
            with patch.dict(os.environ, {"COMFY_QUEST_RUNTIME_ROOT": str(root)}):
                with self.assertRaisesRegex(ValueError, "allowlist"):
                    PROVIDER.quest_runtime_creator_request(
                        "keypress", "OMEN", "-523956327", "era17-session", 2)
                with self.assertRaisesRegex(RuntimeError, "already occupied"):
                    PROVIDER.quest_runtime_creator_request(
                        "status", "OMEN", "-523956327", "era17-session", 2)

    def test_creator_request_surfaces_the_runtime_world_mismatch_diagnostic(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            mailbox = root / "requests" / "creator-request.json"
            receipts = root / "receipts" / "creator-requests"
            receipts.mkdir(parents=True)

            def runtime():
                request = self._wait_for_request(mailbox)
                mailbox.unlink()
                (receipts / f"{request['request_id']}.json").write_text(json.dumps({
                    "schema": "comfy-quest-runtime-request-receipt/v1",
                    "request_id": request["request_id"],
                    "operation": request["operation"],
                    "state": "rejected",
                    "detail": "creator_world_mismatch",
                    "machine": request["expected_machine"],
                    "world_uid": "-523956327",
                    "creator_session_id": request["creator_session_id"],
                }), encoding="utf-8")

            thread = threading.Thread(target=runtime, daemon=True)
            thread.start()
            with patch.dict(os.environ, {"COMFY_QUEST_RUNTIME_ROOT": str(root)}):
                result = PROVIDER.quest_runtime_creator_request(
                    "status", "OMEN", "-523956328", "era17-session", 5)
            thread.join(timeout=2)
            self.assertFalse(result["ok"])
            self.assertEqual("creator_world_mismatch", result["receipt"]["detail"])

    def test_run_control_lists_candidates_through_pack_receipt_scope(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            mailbox = root / "requests" / "run-control.json"
            receipts = root / "receipts" / "run-control" / "pack"
            receipts.mkdir(parents=True)

            def runtime():
                request = self._wait_for_request(mailbox)
                mailbox.unlink()
                (receipts / f"{request['request_id']}.json").write_text(json.dumps({
                    "schema": "comfy-quest-runtime-run-control-receipt/v1",
                    "request_id": request["request_id"],
                    "operation": request["operation"],
                    "state": "completed",
                    "detail": "binding_candidates_ready",
                    "machine": request["expected_machine"],
                    "world_uid": request["expected_world_uid"],
                    "creator_session_id": request["creator_session_id"],
                    "binding_candidates": [{
                        "binding_zdo": "42:7", "target_kind": "sign", "distance_metres": 2.5,
                    }],
                }), encoding="utf-8")

            thread = threading.Thread(target=runtime, daemon=True)
            thread.start()
            with patch.dict(os.environ, {"COMFY_QUEST_RUNTIME_ROOT": str(root)}):
                result = PROVIDER.quest_runtime_run_control(
                    "list_binding_candidates", "OMEN", "-523956327", "era17-session",
                    timeout_seconds=5)
            thread.join(timeout=2)
            self.assertTrue(result["ok"])
            self.assertEqual("42:7", result["receipt"]["binding_candidates"][0]["binding_zdo"])

    def test_run_control_enforces_operation_specific_fields_before_write(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with patch.dict(os.environ, {"COMFY_QUEST_RUNTIME_ROOT": str(root)}):
                with self.assertRaisesRegex(ValueError, "experience_id"):
                    PROVIDER.quest_runtime_run_control(
                        "bind_selected_experience", "OMEN", "-523956327", "era17-session",
                        binding_zdo="42:7", timeout_seconds=2)
                with self.assertRaisesRegex(ValueError, "confirm_reset"):
                    PROVIDER.quest_runtime_run_control(
                        "apply_reset", "OMEN", "-523956327", "era17-session",
                        run_id="run-a", preview_token="preview-a", timeout_seconds=2)
                with self.assertRaisesRegex(ValueError, "binding_zdo"):
                    PROVIDER.quest_runtime_run_control(
                        "list_binding_candidates", "OMEN", "-523956327", "era17-session",
                        binding_zdo="42:7", timeout_seconds=2)
            self.assertFalse((root / "requests" / "run-control.json").exists())

    def test_timed_out_call_expires_before_return_and_retracts_its_mailbox(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            mailbox = root / "requests" / "creator-request.json"
            captured = []

            def observe_without_claiming():
                captured.append(self._wait_for_request(mailbox))

            thread = threading.Thread(target=observe_without_claiming, daemon=True)
            thread.start()
            with patch.dict(os.environ, {"COMFY_QUEST_RUNTIME_ROOT": str(root)}):
                with self.assertRaisesRegex(TimeoutError, "within 2 seconds"):
                    PROVIDER.quest_runtime_creator_request(
                        "status", "OMEN", "-523956327", "era17-session", 2)
            thread.join(timeout=2)
            self.assertFalse(mailbox.exists())
            request = self.assert_single(captured)
            created = dt.datetime.fromisoformat(request["created_utc"])
            expires = dt.datetime.fromisoformat(request["expires_utc"])
            self.assertGreater((expires - created).total_seconds(), 1)
            self.assertLess((expires - created).total_seconds(), 2)

    @staticmethod
    def _wait_for_request(path: Path) -> dict:
        deadline = time.monotonic() + 2
        while time.monotonic() < deadline:
            if path.exists():
                return json.loads(path.read_text(encoding="utf-8"))
            time.sleep(0.01)
        raise AssertionError(f"request did not appear: {path}")

    @staticmethod
    def assert_single(values: list):
        if len(values) != 1:
            raise AssertionError(f"expected exactly one value, got {len(values)}")
        return values[0]


if __name__ == "__main__":
    unittest.main()

import importlib.util
import datetime as dt
import inspect
import json
import os
import shutil
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
    BLUEPRINT_NAME = "first-portal-progression-shelter"

    def test_provider_exports_only_bounded_product_tools(self):
        self.assertEqual(
            [tool.__name__ for tool in PROVIDER.get_tools()],
            [
                "quest_runtime_status",
                "quest_runtime_receipts",
                "quest_runtime_creator_request",
                "quest_runtime_run_control",
                "quest_lab_replay",
            ],
        )
        source = PROVIDER_PATH.read_text(encoding="utf-8")
        for forbidden in (
            "subprocess", "SendKeys", "console", "shell=True", "eval(", "exec(",
            "ssh", "tmux",
        ):
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
                self._unlink_with_retry(mailbox)
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
                self._unlink_with_retry(mailbox)
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
                self._unlink_with_retry(mailbox)
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

    def test_lab_replay_has_no_path_or_lifecycle_inputs_and_declares_release_mounts(self):
        parameters = inspect.signature(PROVIDER.quest_lab_replay).parameters
        self.assertEqual(
            [
                "blueprint_name", "expected_machine", "expected_world_uid",
                "creator_session_id", "build_mode", "radius_metres", "timeout_seconds",
            ],
            list(parameters),
        )
        packager = (
            ROOT / "tools" / "workbench-provider" / "New-QuestWorkbenchProvider.ps1"
        ).read_text(encoding="utf-8")
        readme = (
            ROOT / "tools" / "workbench-provider" / "README.md"
        ).read_text(encoding="utf-8")
        for declaration in (
            "COMFY_QUEST_RUNTIME_ROOT",
            "COMFY_QUEST_LAB_ROOT",
            "COMFY_QUEST_REVIEWED_GODBUILD_ROOT",
            "quest_lab_replay",
        ):
            self.assertIn(declaration, packager)
            self.assertIn(declaration, readme)
        self.assertNotIn("SshAlias", packager)
        self.assertNotIn("AM4", readme)

    def test_lab_replay_stages_reviewed_pair_and_executes_proven_order(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            thread, captured, failures = self._start_lab_responder(lab)
            thread.start()
            result, runtime_calls, runtime_state = self._invoke_replay(
                runtime, lab, reviewed)
            thread.join(timeout=2)
            self.assertFalse(thread.is_alive())
            self.assertEqual([], failures)
            self.assertTrue(result["ok"])
            self.assertEqual("blueprint_replay_match", result["detail"])
            self.assertEqual(12, result["reviewed"]["piece_count"])
            self.assertEqual(
                [
                    "blueprint_check", "blueprint_count",
                    "blueprint_build", "blueprint_diff",
                ],
                [request["operation"] for request in captured],
            )
            check, count, build, after = captured
            for request in captured:
                self.assertEqual("am4", request["expected_machine"])
                self.assertEqual("-7600395338659582326", request["expected_world_uid"])
                self.assertEqual("creator-session-a", request["creator_session_id"])
                self.assertEqual(self.BLUEPRINT_NAME, request["blueprint_name"])
                self.assertNotIn("path", request)
            self.assertNotIn("build_mode", check)
            self.assertNotIn("build_mode", count)
            self.assertNotIn("selection", count)
            self.assertNotIn("radius_metres", count)
            self.assertEqual("ground", build["build_mode"])
            self.assertNotIn("selection", build)
            self.assertEqual("lab", after["selection"])
            self.assertEqual("20", after["radius_metres"])
            self.assertNotIn("build_mode", after)

            source = reviewed / self.BLUEPRINT_NAME
            target = lab / "blueprints"
            for leaf in (
                f"{self.BLUEPRINT_NAME}.capture.json",
                f"{self.BLUEPRINT_NAME}.blueprint",
            ):
                self.assertEqual((source / leaf).read_bytes(), (target / leaf).read_bytes())
                self.assertFalse((target / f"{leaf}.workbench-staging").exists())
                self.assertFalse((target / f"{leaf}.workbench-prior").exists())
            self.assertTrue(result["built"])
            self.assertEqual(["status", "build_on", "build_off"], runtime_calls)
            self.assertFalse(runtime_state["build_enabled"])
            self.assertTrue(result["cleanup"]["required"])
            self.assertTrue(result["cleanup"]["attempted"])
            self.assertTrue(result["cleanup"]["verified_disabled"])
            self.assertEqual("completed", result["authority"]["status"]["state"])
            self.assertEqual("completed", result["authority"]["build_on"]["state"])
            self.assertEqual(4, len(result["receipts"]))
            for receipt in result["receipts"]:
                self.assertNotIn("artifact_path", receipt)
                self.assertNotIn("blueprint_path", receipt)
                self.assertNotIn("detail", receipt)

    def test_lab_replay_retry_returns_success_without_a_second_build(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            first_thread, first_captured, first_failures = self._start_lab_responder(lab)
            first_thread.start()
            try:
                first, first_runtime_calls, first_runtime_state = self._invoke_replay(
                    runtime, lab, reviewed)
            except BaseException:
                first_thread.join(timeout=2)
                if first_failures:
                    raise first_failures[0]
                raise
            first_thread.join(timeout=2)
            self.assertTrue(first["ok"])
            self.assertTrue(first["built"])
            self.assertEqual([], first_failures)
            self.assertEqual(
                ["status", "build_on", "build_off"], first_runtime_calls)
            self.assertFalse(first_runtime_state["build_enabled"])
            self.assertEqual(1, sum(
                request["operation"] == "blueprint_build" for request in first_captured))

            match = (
                f"capture diff {self.BLUEPRINT_NAME}: MATCH — expected 12, selected 12, "
                "missing 0, extra 0"
            )
            thread, captured, failures = self._start_lab_responder(
                lab,
                operations=("blueprint_check", "blueprint_count", "blueprint_diff"),
                step_details={
                    1: (
                        f"{self.BLUEPRINT_NAME}: 12 piece(s) standing in loaded zones\n"
                        "  wood_pole 12\n"
                        "Loaded zones only — stand near the build for a true count."
                    ),
                    2: match,
                },
            )
            thread.start()
            try:
                retry, retry_runtime_calls, retry_runtime_state = self._invoke_replay(
                    runtime, lab, reviewed)
            except BaseException:
                thread.join(timeout=2)
                if failures:
                    raise failures[0]
                raise
            thread.join(timeout=2)
            self.assertFalse(thread.is_alive())
            self.assertEqual([], failures)
            self.assertTrue(retry["ok"])
            self.assertFalse(retry["built"])
            self.assertEqual(
                ["status", "build_on", "build_off"], retry_runtime_calls)
            self.assertFalse(retry_runtime_state["build_enabled"])
            self.assertEqual("blueprint_replay_already_match", retry["detail"])
            self.assertEqual(
                ["blueprint_check", "blueprint_count", "blueprint_diff"],
                [request["operation"] for request in captured],
            )

    def test_lab_replay_partial_marked_state_refuses_without_building(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            partial = (
                f"capture diff {self.BLUEPRINT_NAME}: DIFFERENT — expected 12, selected 5, "
                "missing 7, extra 0"
            )
            thread, captured, failures = self._start_lab_responder(
                lab,
                operations=("blueprint_check", "blueprint_count", "blueprint_diff"),
                step_details={
                    1: (
                        f"{self.BLUEPRINT_NAME}: 5 piece(s) standing in loaded zones\n"
                        "  wood_pole 5\n"
                        "Loaded zones only — stand near the build for a true count."
                    ),
                    2: partial,
                },
            )
            thread.start()
            result, runtime_calls, runtime_state = self._invoke_replay(
                runtime, lab, reviewed)
            thread.join(timeout=2)
            self.assertFalse(thread.is_alive())
            self.assertEqual([], failures)
            self.assertFalse(result["ok"])
            self.assertFalse(result["built"])
            self.assertEqual("blueprint_diff_preflight", result["failed_step"])
            self.assertEqual("blueprint_replay_state_not_empty_or_match", result["detail"])
            self.assertEqual(["status", "build_on", "build_off"], runtime_calls)
            self.assertFalse(runtime_state["build_enabled"])
            self.assertEqual(
                ["blueprint_check", "blueprint_count", "blueprint_diff"],
                [request["operation"] for request in captured],
            )

    def test_lab_replay_refuses_manifest_drift_before_staging_or_mutation(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            blueprint = (
                reviewed / self.BLUEPRINT_NAME / f"{self.BLUEPRINT_NAME}.blueprint"
            )
            blueprint.write_bytes(blueprint.read_bytes() + b"# drift\n")
            with patch.dict(os.environ, self._replay_env(runtime, lab, reviewed), clear=True):
                with self.assertRaisesRegex(ValueError, "artifact hash mismatch"):
                    PROVIDER.quest_lab_replay(
                        self.BLUEPRINT_NAME,
                        "am4",
                        "-7600395338659582326",
                        "creator-session-a",
                        timeout_seconds=2,
                    )
            self.assertFalse((lab / "blueprints").exists())
            self.assertFalse((lab / "requests" / "questlab-batch-request.json").exists())

    def test_lab_replay_requires_the_exact_manifest_post_build_proof(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            manifest_path = reviewed / self.BLUEPRINT_NAME / "manifest.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            manifest["replay"]["post_build_proof"] = "blueprint_diff must report MISMATCH"
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            with patch.dict(os.environ, self._replay_env(runtime, lab, reviewed), clear=True):
                with self.assertRaisesRegex(ValueError, "does not authorize"):
                    PROVIDER.quest_lab_replay(
                        self.BLUEPRINT_NAME,
                        "am4",
                        "-7600395338659582326",
                        "creator-session-a",
                        timeout_seconds=2,
                    )
            self.assertFalse((lab / "blueprints").exists())

    def test_lab_replay_refuses_sky_before_staging_or_mutation(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            with patch.dict(os.environ, self._replay_env(runtime, lab, reviewed), clear=True):
                with self.assertRaisesRegex(ValueError, "must be ground"):
                    PROVIDER.quest_lab_replay(
                        self.BLUEPRINT_NAME,
                        "am4",
                        "-7600395338659582326",
                        "creator-session-a",
                        build_mode="sky",
                        timeout_seconds=2,
                    )
            self.assertFalse((lab / "blueprints").exists())
            self.assertFalse((lab / "requests" / "questlab-batch-request.json").exists())

    def test_lab_replay_refuses_a_different_runtime_creator_session_before_staging(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(
                temporary, entered_session="creator-session-other")
            with patch.dict(os.environ, self._replay_env(runtime, lab, reviewed), clear=True):
                with self.assertRaisesRegex(RuntimeError, "Creator Session differs"):
                    PROVIDER.quest_lab_replay(
                        self.BLUEPRINT_NAME,
                        "am4",
                        "-7600395338659582326",
                        "creator-session-a",
                        timeout_seconds=2,
                    )
            self.assertFalse((lab / "blueprints").exists())

    def test_lab_replay_rejected_build_on_from_stale_entry_never_stages_or_calls_lab(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            result, runtime_calls, runtime_state = self._invoke_replay(
                runtime,
                lab,
                reviewed,
                build_on_state="rejected",
                build_on_detail="private_world_confirmation_required",
                build_on_enabled=False,
            )
            self.assertFalse(result["ok"])
            self.assertEqual("runtime_creator_build_on", result["failed_step"])
            self.assertEqual("private_world_confirmation_required", result["detail"])
            self.assertEqual(["status", "build_on"], runtime_calls)
            self.assertFalse(runtime_state["build_enabled"])
            self.assertFalse(result["cleanup"]["attempted"])
            self.assertTrue(result["cleanup"]["verified_disabled"])
            self.assertFalse((lab / "blueprints").exists())
            self.assertFalse((lab / "requests" / "questlab-batch-request.json").exists())

    def test_lab_replay_closed_live_session_fails_status_before_build_on_or_staging(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            result, runtime_calls, runtime_state = self._invoke_replay(
                runtime,
                lab,
                reviewed,
                status_state="rejected",
                status_detail="creator_session_unavailable",
            )
            self.assertFalse(result["ok"])
            self.assertEqual("runtime_creator_status", result["failed_step"])
            self.assertEqual("creator_session_unavailable", result["detail"])
            self.assertEqual(["status"], runtime_calls)
            self.assertFalse(runtime_state["build_enabled"])
            self.assertFalse((lab / "blueprints").exists())
            self.assertFalse((lab / "requests" / "questlab-batch-request.json").exists())

    def test_lab_replay_preserves_preexisting_creator_build_mode(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            thread, captured, failures = self._start_lab_responder(lab)
            thread.start()
            result, runtime_calls, runtime_state = self._invoke_replay(
                runtime, lab, reviewed, initial_build_enabled=True)
            thread.join(timeout=2)
            self.assertFalse(thread.is_alive())
            self.assertEqual([], failures)
            self.assertTrue(result["ok"])
            self.assertEqual(4, len(captured))
            self.assertEqual(["status", "build_on"], runtime_calls)
            self.assertTrue(runtime_state["build_enabled"])
            self.assertFalse(result["cleanup"]["required"])
            self.assertFalse(result["cleanup"]["attempted"])
            self.assertTrue(result["cleanup"]["preserved_preexisting_enabled"])
            self.assertEqual(
                "preexisting_creator_build_preserved", result["cleanup"]["detail"])

    def test_lab_replay_refuses_success_when_build_off_is_not_verified_false(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            thread, captured, failures = self._start_lab_responder(lab)
            thread.start()
            result, runtime_calls, runtime_state = self._invoke_replay(
                runtime,
                lab,
                reviewed,
                build_off_state="failed",
                build_off_detail="creator_build_state_mismatch",
                build_off_enabled=True,
            )
            thread.join(timeout=2)
            self.assertFalse(thread.is_alive())
            self.assertEqual([], failures)
            self.assertEqual(4, len(captured))
            self.assertFalse(result["ok"])
            self.assertEqual("runtime_creator_build_off", result["failed_step"])
            self.assertEqual("creator_build_cleanup_not_verified", result["detail"])
            self.assertTrue(result["lab_outcome"]["ok"])
            self.assertEqual(["status", "build_on", "build_off"], runtime_calls)
            self.assertTrue(runtime_state["build_enabled"])
            self.assertFalse(result["cleanup"]["verified_disabled"])

    def test_lab_replay_stops_after_a_rejected_check(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            thread, captured, failures = self._start_lab_responder(
                lab,
                operations=("blueprint_check",),
                states={"blueprint_check": "rejected"},
                details={"blueprint_check": "blueprint_source_invalid"},
            )
            thread.start()
            result, runtime_calls, runtime_state = self._invoke_replay(
                runtime, lab, reviewed)
            thread.join(timeout=2)
            self.assertFalse(thread.is_alive())
            self.assertEqual([], failures)
            self.assertFalse(result["ok"])
            self.assertEqual("blueprint_check", result["failed_step"])
            self.assertEqual("blueprint_source_invalid", result["detail"])
            self.assertEqual(["status", "build_on", "build_off"], runtime_calls)
            self.assertFalse(runtime_state["build_enabled"])
            self.assertTrue(result["cleanup"]["verified_disabled"])
            self.assertEqual(["blueprint_check"], [item["operation"] for item in captured])

    def test_lab_replay_does_not_export_a_free_form_lab_path_diagnostic(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            thread, captured, failures = self._start_lab_responder(
                lab,
                operations=("blueprint_check",),
                states={"blueprint_check": "failed"},
                details={
                    "blueprint_check": (
                        "no blueprint named at C:\\Users\\operator\\private\\blueprints"
                    ),
                },
            )
            thread.start()
            result, runtime_calls, runtime_state = self._invoke_replay(
                runtime, lab, reviewed)
            thread.join(timeout=2)
            self.assertFalse(thread.is_alive())
            self.assertEqual([], failures)
            self.assertEqual(["blueprint_check"], [item["operation"] for item in captured])
            self.assertFalse(result["ok"])
            self.assertEqual("blueprint_check_failed", result["detail"])
            self.assertEqual(["status", "build_on", "build_off"], runtime_calls)
            self.assertFalse(runtime_state["build_enabled"])
            self.assertNotIn("detail", self.assert_single(result["receipts"]))

    def test_lab_replay_requires_match_after_a_completed_build(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            thread, captured, failures = self._start_lab_responder(
                lab,
                step_details={
                    3: (
                        f"capture diff {self.BLUEPRINT_NAME}: DIFFERENT - expected 12, "
                        "selected 11, missing 1, extra 0"
                    ),
                },
            )
            thread.start()
            result, runtime_calls, runtime_state = self._invoke_replay(
                runtime, lab, reviewed)
            thread.join(timeout=2)
            self.assertFalse(thread.is_alive())
            self.assertEqual([], failures)
            self.assertFalse(result["ok"])
            self.assertEqual("blueprint_diff", result["failed_step"])
            self.assertEqual("blueprint_diff_completed_without_match", result["detail"])
            self.assertEqual(["status", "build_on", "build_off"], runtime_calls)
            self.assertFalse(runtime_state["build_enabled"])
            self.assertEqual(4, len(captured))

    def test_lab_replay_timeout_leaves_its_expired_check_for_lab_to_reject(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, lab, reviewed = self._replay_fixture(temporary)
            mailbox = lab / "requests" / "questlab-batch-request.json"
            captured = []

            def observe_without_claiming():
                captured.append(self._wait_for_request(mailbox))

            thread = threading.Thread(target=observe_without_claiming, daemon=True)
            thread.start()
            error, runtime_calls, runtime_state = self._invoke_replay(
                runtime,
                lab,
                reviewed,
                capture_exception=True,
                timeout_seconds=2,
            )
            thread.join(timeout=2)
            self.assertIsInstance(error, TimeoutError)
            self.assertRegex(str(error), "Quest Lab.*expired mailbox request remains")
            self.assertTrue(mailbox.exists())
            self.assertEqual(["status", "build_on", "build_off"], runtime_calls)
            self.assertFalse(runtime_state["build_enabled"])
            request = self.assert_single(captured)
            self.assertEqual("blueprint_check", request["operation"])
            created = dt.datetime.fromisoformat(request["created_utc"])
            expires = dt.datetime.fromisoformat(request["expires_utc"])
            self.assertGreater((expires - created).total_seconds(), 1)
            self.assertLess((expires - created).total_seconds(), 2)
            self.assertLessEqual(expires, dt.datetime.now(dt.timezone.utc))

    def test_timed_out_call_leaves_expired_mailbox_for_runtime_to_reject(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            mailbox = root / "requests" / "creator-request.json"
            captured = []

            def observe_without_claiming():
                captured.append(self._wait_for_request(mailbox))

            thread = threading.Thread(target=observe_without_claiming, daemon=True)
            thread.start()
            with patch.dict(os.environ, {"COMFY_QUEST_RUNTIME_ROOT": str(root)}):
                with self.assertRaisesRegex(
                    TimeoutError, "expired mailbox request remains"
                ):
                    PROVIDER.quest_runtime_creator_request(
                        "status", "OMEN", "-523956327", "era17-session", 2)
            thread.join(timeout=2)
            self.assertTrue(mailbox.exists())
            request = self.assert_single(captured)
            created = dt.datetime.fromisoformat(request["created_utc"])
            expires = dt.datetime.fromisoformat(request["expires_utc"])
            self.assertGreater((expires - created).total_seconds(), 1)
            self.assertLess((expires - created).total_seconds(), 2)
            self.assertLessEqual(expires, dt.datetime.now(dt.timezone.utc))

    def _replay_fixture(
        self,
        temporary: str,
        *,
        entered_session: str = "creator-session-a",
    ) -> tuple[Path, Path, Path]:
        root = Path(temporary)
        runtime = root / "runtime"
        lab = root / "lab"
        reviewed = root / "reviewed"
        (runtime / "status").mkdir(parents=True)
        lab.mkdir()
        reviewed.mkdir()
        shutil.copytree(
            ROOT / "examples" / "worldbuild" / self.BLUEPRINT_NAME,
            reviewed / self.BLUEPRINT_NAME,
        )
        (runtime / "status" / "world-entry.json").write_text(json.dumps({
            "schema": "comfy-quest-world-entry-receipt/v1",
            "request_id": "world-entry-a",
            "creator_session_id": entered_session,
            "state": "entered",
            "detail": "world_entry_complete",
            "machine": "am4",
            "world_uid": "-7600395338659582326",
        }), encoding="utf-8")
        return runtime, lab, reviewed

    @staticmethod
    def _replay_env(runtime: Path, lab: Path, reviewed: Path) -> dict[str, str]:
        return {
            "COMFY_QUEST_RUNTIME_ROOT": str(runtime),
            "COMFY_QUEST_LAB_ROOT": str(lab),
            "COMFY_QUEST_REVIEWED_GODBUILD_ROOT": str(reviewed),
            "COMFY_QUEST_PROVIDER_SHA256": "c" * 64,
        }

    def _invoke_replay(
        self,
        runtime: Path,
        lab: Path,
        reviewed: Path,
        *,
        initial_build_enabled: bool = False,
        status_state: str = "completed",
        status_detail: str = "dev_channel_disarmed",
        build_on_state: str = "completed",
        build_on_detail: str = "creator_build_enabled",
        build_on_enabled: bool | None = None,
        build_off_state: str = "completed",
        build_off_detail: str = "creator_build_disabled",
        build_off_enabled: bool = False,
        capture_exception: bool = False,
        **replay_arguments,
    ) -> tuple[dict | BaseException, list[str], dict[str, bool]]:
        calls: list[str] = []
        runtime_state = {"build_enabled": initial_build_enabled}

        def creator_request(operation, machine, world, session, timeout):
            self.assertEqual("am4", machine)
            self.assertEqual("-7600395338659582326", world)
            self.assertEqual("creator-session-a", session)
            self.assertGreaterEqual(timeout, 2)
            calls.append(operation)
            state = "completed"
            detail = "dev_channel_disarmed"
            if operation == "status":
                state = status_state
                detail = status_detail
            elif operation == "build_on":
                state = build_on_state
                detail = build_on_detail
                if build_on_enabled is not None:
                    runtime_state["build_enabled"] = build_on_enabled
                elif state == "completed":
                    runtime_state["build_enabled"] = True
            elif operation == "build_off":
                state = build_off_state
                runtime_state["build_enabled"] = build_off_enabled
                detail = build_off_detail
            receipt = {
                "schema": "comfy-quest-runtime-request-receipt/v1",
                "request_id": f"runtime-{operation}-{len(calls)}",
                "operation": operation,
                "state": state,
                "detail": detail,
                "machine": machine,
                "world_uid": world,
                "creator_session_id": session,
                "completed_utc": "2026-08-28T08:00:00+00:00",
                "creator_build_enabled": runtime_state["build_enabled"],
            }
            return {
                "schema": "comfy-quest-workbench-creator-result/v1",
                "ok": state == "completed",
                "request_id": receipt["request_id"],
                "receipt": receipt,
            }

        arguments = {
            "blueprint_name": self.BLUEPRINT_NAME,
            "expected_machine": "am4",
            "expected_world_uid": "-7600395338659582326",
            "creator_session_id": "creator-session-a",
            "timeout_seconds": 5,
        }
        arguments.update(replay_arguments)
        with patch.object(
            PROVIDER, "quest_runtime_creator_request", side_effect=creator_request
        ):
            with patch.dict(
                os.environ, self._replay_env(runtime, lab, reviewed), clear=True
            ):
                try:
                    result = PROVIDER.quest_lab_replay(**arguments)
                except BaseException as error:
                    if not capture_exception:
                        raise
                    result = error
        return result, calls, runtime_state

    def _start_lab_responder(
        self,
        lab: Path,
        *,
        operations: tuple[str, ...] = (
            "blueprint_check", "blueprint_count", "blueprint_build", "blueprint_diff",
        ),
        states: dict[str, str] | None = None,
        details: dict[str, str] | None = None,
        step_states: dict[int, str] | None = None,
        step_details: dict[int, str] | None = None,
    ) -> tuple[threading.Thread, list[dict], list[BaseException]]:
        states = states or {}
        details = details or {}
        step_states = step_states or {}
        step_details = step_details or {}
        mailbox = lab / "requests" / "questlab-batch-request.json"
        receipts = lab / "receipts" / "requests"
        receipts.mkdir(parents=True, exist_ok=True)
        captured: list[dict] = []
        failures: list[BaseException] = []
        capture = lab / "blueprints" / f"{self.BLUEPRINT_NAME}.capture.json"
        blueprint = lab / "blueprints" / f"{self.BLUEPRINT_NAME}.blueprint"
        default_details = {
            "blueprint_check": "blueprint check\nReady.",
            "blueprint_count": (
                "no blueprint-built pieces in the loaded area. Only loaded zones are "
                "counted — stand near the build."
            ),
            "blueprint_build": "12 pieces standing; sign 1",
            "blueprint_diff": (
                f"capture diff {self.BLUEPRINT_NAME}: MATCH — expected 12, selected 12, "
                "missing 0, extra 0"
            ),
        }

        def respond():
            try:
                for step, expected_operation in enumerate(operations):
                    request = self._wait_for_request(mailbox)
                    captured.append(request)
                    self._unlink_with_retry(mailbox)
                    if request["operation"] != expected_operation:
                        raise AssertionError(
                            f"expected {expected_operation}, got {request['operation']}")
                    operation = request["operation"]
                    default_detail = default_details[operation]
                    receipt = {
                        "schema": "comfy-questlab-batch-request-receipt/v1",
                        "request_id": request["request_id"],
                        "operation": operation,
                        "state": step_states.get(step, states.get(operation, "completed")),
                        "detail": step_details.get(
                            step, details.get(operation, default_detail)),
                        "machine": request["expected_machine"],
                        "plugin_version": "0.2.0",
                        "release_id": "questlab-v0.2.0-test",
                        "creator_session_id": request["creator_session_id"],
                        "world_uid": request["expected_world_uid"],
                        "completed_utc": "2026-08-28T08:00:00+00:00",
                        "artifact_path": (
                            (
                                "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Valheim"
                                "\\BepInEx\\config\\comfy-quest-lab\\blueprints\\"
                                f"{capture.name}"
                            ) if operation in {"blueprint_check", "blueprint_diff"}
                            else ""
                        ),
                        "blueprint_path": (
                            (
                                "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Valheim"
                                "\\BepInEx\\config\\comfy-quest-lab\\blueprints\\"
                                f"{blueprint.name}"
                            ) if operation == "blueprint_check" else ""
                        ),
                        "evidence_path": "",
                        "suite_receipt_path": "",
                    }
                    (receipts / f"{request['request_id']}.json").write_text(
                        json.dumps(receipt), encoding="utf-8")
            except BaseException as error:
                failures.append(error)

        return threading.Thread(target=respond, daemon=True), captured, failures

    @staticmethod
    def _wait_for_request(path: Path, timeout_seconds: float = 10) -> dict:
        # The synthetic authority must outlive the provider's normal five-second wait.
        deadline = time.monotonic() + timeout_seconds
        while time.monotonic() < deadline:
            if path.exists():
                try:
                    return json.loads(path.read_text(encoding="utf-8"))
                except (OSError, json.JSONDecodeError):
                    pass
            time.sleep(0.01)
        raise AssertionError(f"request did not appear: {path}")

    @staticmethod
    def _unlink_with_retry(path: Path, timeout_seconds: float = 2) -> None:
        """Model Runtime/Lab mailbox claiming without Windows sharing flakiness."""
        deadline = time.monotonic() + timeout_seconds
        while True:
            try:
                path.unlink()
                return
            except PermissionError:
                if time.monotonic() >= deadline:
                    raise
                time.sleep(0.01)

    @staticmethod
    def assert_single(values: list):
        if len(values) != 1:
            raise AssertionError(f"expected exactly one value, got {len(values)}")
        return values[0]


if __name__ == "__main__":
    unittest.main()

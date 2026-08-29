"""Safety and syntax guards for the bounded i5 Quest Lab request sender."""

from __future__ import annotations

import json
import re
import subprocess
import tempfile
import threading
import time
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "tools" / "questlab-batch" / "Invoke-I5QuestLabBatch.ps1"
CONTROLLER = REPO / "network" / "mod" / "ComfyQuestLab" / "Core" / "LabBatchController.cs"
CAPABILITIES = (
    REPO / "tools" / "component-packets" / "samples" / "quest-capability-manifest.json"
)
EXPECTED_OPERATIONS = {
    "prepare",
    "run",
    "reset",
    "report",
    "export",
    "gallery_build",
    "gallery_compare",
    "gallery_identify",
    "gallery_evidence",
    "gallery_clear",
    "gallery_rebuild",
    "blueprint_capture",
    "blueprint_inspect",
    "blueprint_diff",
    "blueprint_check",
    "blueprint_build",
    "blueprint_count",
    "blueprint_clear",
    "signature_hunt_prepare",
    "signature_hunt_status",
    "signature_hunt_clear",
}


class I5QuestLabBatchSurfaceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = SCRIPT.read_text(encoding="utf-8")
        cls.controller = CONTROLLER.read_text(encoding="utf-8")

    def test_operation_surface_is_exactly_allowlisted(self) -> None:
        match = re.search(
            r"\[ValidateSet\((.*?)\)\]\s*\[string\]\$Operation",
            self.source,
            flags=re.DOTALL,
        )
        self.assertIsNotNone(match)
        values = set(re.findall(r"'([a-z_]+)'", match.group(1)))
        self.assertEqual(values, EXPECTED_OPERATIONS)

    def test_blueprint_clear_waits_for_valheim_to_retire_destroyed_zdos(self) -> None:
        dispatch = self.controller[
            self.controller.index('if (operation == "blueprint_clear")') :
            self.controller.index('if (operation == "blueprint_build")')
        ]
        self.assertIn("host.StartCoroutine(RequestRoutine(", dispatch)
        self.assertIn("ClearBlueprintAndAwaitRemoval", dispatch)
        self.assertNotIn("WriteRequestReceipt(request", dispatch)
        self.assertIn("const int maxRetirementFrames = 120", self.controller)
        self.assertIn("StandingPieceCount(blueprintName) > 0", self.controller)
        self.assertIn("BlueprintClearAccepted(detail)", dispatch)

    def test_signature_hunt_sender_is_parameter_free_and_copies_only_fixture_evidence(self) -> None:
        self.assertIn("'signature_hunt_prepare', 'signature_hunt_status', 'signature_hunt_clear'", self.source)
        self.assertIn("$evidenceDirectory = if ($signatureHuntEvidence) { 'fixtures' }", self.source)
        self.assertIn("comfy-questlab-signature-hunt-fixture/v1", self.source)
        self.assertIn("^signature-hunt-[A-Za-z0-9._-]+\\.json$", self.source)

    def test_uses_verified_config_lane_and_batchmode_reads(self) -> None:
        self.assertIn("Deploy-ToI5.ps1", self.source)
        self.assertIn("tools\\i5-deploy\\Deploy-ToI5.ps1", self.source)
        self.assertIn("-ValheimConfig", self.source)
        self.assertGreaterEqual(self.source.count("BatchMode=yes"), 3)
        self.assertIn("comfy-questlab-batch-request/v1", self.source)
        self.assertIn("[switch]$DryRun", self.source)
        self.assertIn("suite verdict:", self.source)
        self.assertIn("same-action double completion", self.source)
        self.assertRegex(
            self.source,
            r"\[ValidateSet\('i5', 'omen'\)\]\s*\[string\]\$Lane",
        )
        self.assertIn(
            'Join-Path $repoRoot ("captures\\questlab\\{0}" -f $Lane)',
            self.source,
        )
        self.assertNotIn("DeployScriptPath", self.source)

    def test_suite_allowlist_contains_exactly_the_two_suites_and_all_scenarios(self) -> None:
        match = re.search(
            r"\[ValidateSet\(([^)]*)\)\]\s*\[string\]\$Suite",
            self.source,
            flags=re.DOTALL,
        )
        self.assertIsNotNone(match)
        values = set(re.findall(r"'([a-z0-9_-]+)'", match.group(1)))
        capabilities = json.loads(CAPABILITIES.read_text(encoding="utf-8"))
        expected = {"all-schools", "creator-events"} | {
            "scenario-" + event for event in capabilities["CreatorSafeEvents"]
        }
        self.assertEqual(values, expected)

    def test_dry_run_stops_before_any_i5_process(self) -> None:
        dry_run_branch = self.source.index("if ($DryRun)")
        self.assertLess(dry_run_branch, self.source.index("& powershell.exe"))
        self.assertLess(dry_run_branch, self.source.index("& ssh"))

        with tempfile.TemporaryDirectory() as output_directory:
            result = subprocess.run(
                [
                    "powershell.exe",
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    str(SCRIPT),
                    "run",
                    "-Suite",
                    "creator-events",
                    "-OutputDirectory",
                    output_directory,
                    "-DryRun",
                    "-Lane",
                    "omen",
                ],
                cwd=REPO,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            receipts = list(Path(output_directory).glob("*-request.json"))
            self.assertEqual(len(receipts), 1)
            envelope = json.loads(receipts[0].read_text(encoding="utf-8"))
            self.assertEqual(envelope["schema"], "comfy-questlab-batch-request/v1")
            self.assertEqual(envelope["operation"], "run")
            self.assertEqual(envelope["suite"], "creator-events")

    def test_one_event_scenario_uses_the_same_bounded_run_envelope(self) -> None:
        with tempfile.TemporaryDirectory() as output_directory:
            result = subprocess.run(
                [
                    "powershell.exe",
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    str(SCRIPT),
                    "run",
                    "-Suite",
                    "scenario-sign_written",
                    "-OutputDirectory",
                    output_directory,
                    "-DryRun",
                    "-Lane",
                    "omen",
                ],
                cwd=REPO,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            envelope = json.loads(next(Path(output_directory).glob("*-request.json")).read_text())
            self.assertEqual(envelope["operation"], "run")
            self.assertEqual(envelope["suite"], "scenario-sign_written")
            self.assertNotIn("path", envelope)
            self.assertNotIn("command", envelope)

    def test_gallery_defaults_select_grand_but_compare_retains_wide_baseline(self) -> None:
        cases = (
            ("gallery_build", {"profile": "marble-grand"}),
            (
                "gallery_compare",
                {"profile": "marble-wide", "compare_profile": "marble-grand"},
            ),
        )
        for operation, expected in cases:
            with (
                self.subTest(operation=operation),
                tempfile.TemporaryDirectory() as output_directory,
            ):
                result = subprocess.run(
                    [
                        "powershell.exe",
                        "-NoProfile",
                        "-ExecutionPolicy",
                        "Bypass",
                        "-File",
                        str(SCRIPT),
                        operation,
                        "-OutputDirectory",
                        output_directory,
                        "-DryRun",
                        "-Lane",
                        "omen",
                    ],
                    cwd=REPO,
                    capture_output=True,
                    text=True,
                    check=False,
                )
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                receipts = list(Path(output_directory).glob("*-request.json"))
                self.assertEqual(len(receipts), 1)
                envelope = json.loads(receipts[0].read_text(encoding="utf-8"))
                for field, value in expected.items():
                    self.assertEqual(envelope[field], value)

    def test_gallery_evidence_is_read_only_and_selector_bounded(self) -> None:
        with tempfile.TemporaryDirectory() as output_directory:
            result = subprocess.run(
                [
                    "powershell.exe",
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    str(SCRIPT),
                    "gallery_evidence",
                    "-Selector",
                    "marble-grand",
                    "-OutputDirectory",
                    output_directory,
                    "-DryRun",
                    "-Lane",
                    "omen",
                ],
                cwd=REPO,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            envelope = json.loads(next(Path(output_directory).glob("*-request.json")).read_text())
            self.assertEqual(envelope["operation"], "gallery_evidence")
            self.assertEqual(envelope["selector"], "marble-grand")
            self.assertNotIn("profile", envelope)
            self.assertNotIn("path", envelope)

        self.assertIn("comfy-questlab-gallery-truth/v1", self.source)
        self.assertIn("{ 'fixtures' } else { 'truth' }", self.source)
        self.assertIn("receipts/$evidenceDirectory/", self.source)

    def test_blueprint_capture_envelope_is_bounded_and_identity_pinned(self) -> None:
        with tempfile.TemporaryDirectory() as output_directory:
            result = subprocess.run(
                [
                    "powershell.exe",
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    str(SCRIPT),
                    "blueprint_capture",
                    "-BlueprintName",
                    "human-hall",
                    "-RadiusMetres",
                    "24",
                    "-Selection",
                    "mine",
                    "-ExpectedMachine",
                    "OMEN",
                    "-ExpectedWorldUid",
                    "-7600395338659582326",
                    "-CreatorSessionId",
                    "creator-20260824-abcd1234",
                    "-OutputDirectory",
                    output_directory,
                    "-DryRun",
                    "-Lane",
                    "omen",
                ],
                cwd=REPO,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            envelope = json.loads(
                next(Path(output_directory).glob("*-request.json")).read_text()
            )
            self.assertEqual(envelope["operation"], "blueprint_capture")
            self.assertEqual(envelope["blueprint_name"], "human-hall")
            self.assertEqual(envelope["radius_metres"], "24")
            self.assertEqual(envelope["selection"], "mine")
            self.assertEqual(envelope["expected_machine"], "OMEN")
            self.assertEqual(envelope["expected_world_uid"], "-7600395338659582326")
            self.assertEqual(
                envelope["creator_session_id"], "creator-20260824-abcd1234"
            )
            for forbidden in ("path", "prefab", "command", "key"):
                self.assertNotIn(forbidden, envelope)

        self.assertIn("comfy-quest-lab/blueprints/", self.source)
        self.assertIn("artifact_path", self.source)
        self.assertIn("[string]$OmenValheimRoot", self.source)

    def test_local_sender_round_trip_validates_correlated_identity_receipt(self) -> None:
        def run_case(receipt_machine: str) -> subprocess.CompletedProcess[str]:
            with tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary) / "Valheim"
                request_path = (
                    root
                    / "BepInEx"
                    / "config"
                    / "comfy-quest-lab"
                    / "requests"
                    / "questlab-batch-request.json"
                )
                receipt_root = (
                    root
                    / "BepInEx"
                    / "config"
                    / "comfy-quest-lab"
                    / "receipts"
                    / "requests"
                )
                root.mkdir(parents=True)
                output = Path(temporary) / "evidence"
                consumer_error: list[BaseException] = []

                def consume() -> None:
                    try:
                        deadline = time.monotonic() + 8
                        while not request_path.exists() and time.monotonic() < deadline:
                            time.sleep(0.02)
                        request = json.loads(request_path.read_text(encoding="utf-8"))
                        receipt_root.mkdir(parents=True)
                        receipt = {
                            "schema": "comfy-questlab-batch-request-receipt/v1",
                            "request_id": request["request_id"],
                            "operation": request["operation"],
                            "state": "completed",
                            "machine": receipt_machine,
                            "creator_session_id": request["creator_session_id"],
                            "world_uid": request["expected_world_uid"],
                            "detail": "fixture identify completed",
                            "evidence_path": "",
                            "artifact_path": "",
                            "blueprint_path": "",
                            "suite_receipt_path": "",
                        }
                        (receipt_root / f"{request['request_id']}.json").write_text(
                            json.dumps(receipt), encoding="utf-8"
                        )
                    except BaseException as error:  # surfaced on the test thread below
                        consumer_error.append(error)

                thread = threading.Thread(target=consume)
                thread.start()
                result = subprocess.run(
                    [
                        "powershell.exe",
                        "-NoProfile",
                        "-ExecutionPolicy",
                        "Bypass",
                        "-File",
                        str(SCRIPT),
                        "gallery_identify",
                        "-Lane",
                        "omen",
                        "-OmenValheimRoot",
                        str(root),
                        "-ExpectedMachine",
                        "OMEN",
                        "-ExpectedWorldUid",
                        "-7600395338659582326",
                        "-CreatorSessionId",
                        "creator-sender-roundtrip",
                        "-OutputDirectory",
                        str(output),
                        "-WaitSeconds",
                        "10",
                    ],
                    cwd=REPO,
                    capture_output=True,
                    text=True,
                    check=False,
                )
                thread.join(timeout=10)
                if consumer_error:
                    raise consumer_error[0]
                return result

        accepted = run_case("OMEN")
        self.assertEqual(0, accepted.returncode, accepted.stdout + accepted.stderr)
        self.assertIn("request state: completed", accepted.stdout)

        rejected = run_case("WRONG-MACHINE")
        self.assertNotEqual(0, rejected.returncode)
        self.assertIn("Quest Lab receipt machine mismatch", rejected.stdout + rejected.stderr)

    def test_no_generic_execution_or_keystroke_primitive_exists(self) -> None:
        for forbidden in (
            "Invoke-Expression",
            "SendKeys",
            "WScript.Shell",
            "Terminal.Console",
            "keybd_event",
        ):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, self.source)

    def test_powershell_parser_accepts_the_script(self) -> None:
        command = (
            "$tokens=$null; $errors=$null; "
            "[System.Management.Automation.Language.Parser]::ParseFile("
            f"'{str(SCRIPT).replace("'", "''")}', [ref]$tokens, [ref]$errors) | Out-Null; "
            "if ($errors.Count) { $errors | ForEach-Object Message; exit 1 }"
        )
        result = subprocess.run(
            ["powershell.exe", "-NoProfile", "-Command", command],
            cwd=REPO,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()

"""Guards for the install-wide, bounded Creator Session control plane."""

from __future__ import annotations

import json
import re
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SESSION = ROOT / "tools" / "creator-session" / "Invoke-CreatorSession.ps1"
RUNTIME = ROOT / "tools" / "creator-session" / "Invoke-RuntimeCreatorRequest.ps1"
PLUGIN = ROOT / "network" / "mod" / "ComfyQuestRuntime" / "ComfyQuestRuntime.cs"


class CreatorSessionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.session = SESSION.read_text(encoding="utf-8")
        cls.runtime = RUNTIME.read_text(encoding="utf-8")
        cls.plugin = PLUGIN.read_text(encoding="utf-8")

    def test_creator_session_actions_are_closed(self) -> None:
        match = re.search(
            r"\[ValidateSet\((.*?)\)\]\s*\[string\]\$Action",
            self.session,
            flags=re.DOTALL,
        )
        self.assertIsNotNone(match)
        self.assertEqual(
            set(re.findall(r"'([A-Za-z]+)'", match.group(1))),
            {
                "Prepare",
                "Status",
                "GalleryRebuild",
                "Capture",
                "Replay",
                "Arm",
                "Disarm",
                "BuildOn",
                "BuildOff",
                "Close",
            },
        )

    def test_install_mutations_are_leased_pinned_and_recoverable(self) -> None:
        self.assertIn("[IO.FileShare]::None", self.session)
        self.assertIn("Installed bytes changed during Creator Session", self.session)
        self.assertIn("Get-InboxPins", self.session)
        self.assertIn("world_backup", self.session)
        self.assertIn("runtime_config", self.session)
        self.assertIn("rollback", self.session)
        self.assertIn("Prepare requires Valheim to be closed", self.session)
        self.assertIn("Restore requires Valheim to be closed", self.session)
        self.assertIn("'-OmenValheimRoot', [string]$context.valheim_root", self.session)
        self.assertIn("[string]$OmenValheimRoot", self.runtime)

    def test_live_sequences_follow_machine_preconditions(self) -> None:
        identify = self.session.index("@('gallery_identify')")
        rebuild = self.session.index("@('gallery_rebuild'")
        evidence = self.session.index("@('gallery_evidence'")
        self.assertLess(identify, rebuild)
        self.assertLess(rebuild, evidence)

        check = self.session.index("'blueprint_check', '-BlueprintName'")
        build = self.session.index("'blueprint_build', '-BlueprintName'")
        diff = self.session.index("'blueprint_diff', '-BlueprintName'")
        self.assertLess(check, build)
        self.assertLess(build, diff)
        self.assertIn("translation-independent zero diff", self.session)

        capture = self.session.index("$captureArtifact = Get-ChildItem")
        imported = self.session.index("Invoke-GodbuildImport $captureArtifact.FullName", capture)
        drift_checked = self.session.index("Invoke-GodbuildImport $captureArtifact.FullName -Check", capture)
        self.assertLess(capture, imported)
        self.assertLess(imported, drift_checked)
        stage = self.session.index("$godbuildDirectory = Stage-ReviewedGodbuild")
        self.assertLess(stage, check)
        self.assertIn("Reviewed Godbuild hash mismatch", self.session)
        self.assertIn("[IO.File]::Replace", self.session)

    def test_runtime_request_is_closed_to_session_and_private_build_control(self) -> None:
        match = re.search(
            r"\[ValidateSet\((.*?)\)\]\s*\[string\]\$Operation",
            self.runtime,
            flags=re.DOTALL,
        )
        self.assertIsNotNone(match)
        self.assertEqual(
            set(re.findall(r"'([a-z_]+)'", match.group(1))),
            {"status", "arm", "disarm", "build_on", "build_off"},
        )
        self.assertIn("comfy-quest-runtime-request/v1", self.runtime)
        for forbidden in (
            "Invoke-Expression",
            "SendKeys",
            "WScript.Shell",
            "Terminal.Console",
            "keybd_event",
        ):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, self.runtime)
                self.assertNotIn(forbidden, self.session)
        close = self.session[self.session.index("} elseif ($Action -eq 'Close')") :]
        self.assertLess(close.index("@('build_off')"), close.index("@('disarm')"))

    def test_private_build_control_uses_direct_bounded_player_apis(self) -> None:
        self.assertIn("SetCreatorBuildMode,CreatorBuildModeEnabled", self.plugin)
        self.assertIn("player.SetNoPlacementCost(enabled)", self.plugin)
        self.assertIn("player.SetGodMode(enabled)", self.plugin)
        self.assertIn("player.NoCostCheat()&&player.InGodMode()", self.plugin)
        self.assertNotIn("Console.instance", self.plugin)
        self.assertNotIn("ZInput.Simulate", self.plugin)

    def test_dry_run_reports_the_pinned_plan_without_deploying(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "Valheim"
            root.mkdir()
            (root / ".comfy-quest-creator-fixture").write_text("owned\n")
            result = subprocess.run(
                [
                    "powershell.exe",
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    str(SESSION),
                    "GalleryRebuild",
                    "-ValheimRoot",
                    str(root),
                    "-FixtureMode",
                    "-DryRun",
                    "-ExpectedMachine",
                    "OMEN",
                    "-WorldUid",
                    "-7600395338659582326",
                ],
                cwd=ROOT,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            plan = json.loads(result.stdout[result.stdout.index("{") :])
            self.assertEqual(plan["action"], "GalleryRebuild")
            self.assertEqual(plan["expected_machine"], "OMEN")
            self.assertEqual(plan["world_uid"], "-7600395338659582326")
            self.assertFalse((root / "BepInEx" / "plugins").exists())

    def test_fixture_lifecycle_deploys_verifies_closes_and_restores(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            root = workspace / "Valheim"
            source = root / "fixture-source"
            plugins = root / "BepInEx" / "plugins"
            config = root / "BepInEx" / "config" / "djcdevelopment.valheim.comfyquestruntime.cfg"
            evidence = workspace / "evidence"
            source.mkdir(parents=True)
            plugins.mkdir(parents=True)
            config.parent.mkdir(parents=True)
            (root / ".comfy-quest-creator-fixture").write_text("owned\n", encoding="utf-8")

            payloads = {
                "ComfyQuestLab.dll": b"fixture-lab-r31\n",
                "ComfyQuestRuntime.dll": b"fixture-runtime-overhead\n",
                "ComfyQuestContracts.dll": b"fixture-contracts\n",
                "Newtonsoft.Json.dll": b"fixture-json\n",
            }
            for name, payload in payloads.items():
                (source / name).write_bytes(payload)

            prior_lab = b"prior-lab-install\n"
            (plugins / "ComfyQuestLab.dll").write_bytes(prior_lab)
            prior_config = b"[Safety]\r\nPrivateWorldConfirmed = false\r\n[Display]\r\nScale = 1.0\r\n"
            config.write_bytes(prior_config)

            common = [
                "-ValheimRoot",
                str(root),
                "-FixtureMode",
                "-ExpectedMachine",
                "OMEN",
                "-WorldUid",
                "-7600395338659582326",
                "-SessionId",
                "fixture-lifecycle",
            ]

            def invoke(action: str, *extra: str) -> subprocess.CompletedProcess[str]:
                return subprocess.run(
                    [
                        "powershell.exe",
                        "-NoProfile",
                        "-ExecutionPolicy",
                        "Bypass",
                        "-File",
                        str(SESSION),
                        action,
                        *common,
                        *extra,
                    ],
                    cwd=ROOT,
                    capture_output=True,
                    text=True,
                    check=False,
                )

            prepared = invoke("Prepare", "-NoBuild", "-EvidenceRoot", str(evidence))
            self.assertEqual(0, prepared.returncode, prepared.stdout + prepared.stderr)
            manifest = json.loads((root / "BepInEx" / "config" / "comfy-quest-creator" / "session.json").read_text())
            self.assertEqual("active", manifest["state"])
            self.assertEqual("fixture-lifecycle", manifest["session_id"])
            self.assertEqual(4, len(manifest["plugins"]))
            for name, payload in payloads.items():
                self.assertEqual(payload, (plugins / name).read_bytes())
            self.assertIn(b"PrivateWorldConfirmed = true", config.read_bytes())

            status = invoke("Status")
            self.assertEqual(0, status.returncode, status.stdout + status.stderr)
            status_receipt = json.loads(status.stdout[status.stdout.index("{") :])
            self.assertEqual("active", status_receipt["state"])
            self.assertTrue(status_receipt["plugin_hashes_match"])

            closed = invoke("Close", "-Restore")
            self.assertEqual(0, closed.returncode, closed.stdout + closed.stderr)
            self.assertEqual(prior_lab, (plugins / "ComfyQuestLab.dll").read_bytes())
            for name in payloads:
                if name != "ComfyQuestLab.dll":
                    self.assertFalse((plugins / name).exists())
            self.assertEqual(prior_config, config.read_bytes())
            closed_manifest = json.loads((evidence / "session.closed.json").read_text())
            self.assertEqual("closed", closed_manifest["state"])
            self.assertTrue(closed_manifest["restored"])

    def test_powershell_parsers_accept_both_entrypoints(self) -> None:
        for script in (SESSION, RUNTIME):
            with self.subTest(script=script.name):
                escaped = str(script).replace("'", "''")
                command = (
                    "$tokens=$null; $errors=$null; "
                    "[Management.Automation.Language.Parser]::ParseFile("
                    f"'{escaped}', [ref]$tokens, [ref]$errors) | Out-Null; "
                    "if ($errors.Count) { $errors | ForEach-Object Message; exit 1 }"
                )
                result = subprocess.run(
                    ["powershell.exe", "-NoProfile", "-Command", command],
                    cwd=ROOT,
                    capture_output=True,
                    text=True,
                    check=False,
                )
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()

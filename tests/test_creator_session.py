"""Guards for the install-wide, bounded Creator Session control plane."""

from __future__ import annotations

import json
import hashlib
import re
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SESSION = ROOT / "tools" / "creator-session" / "Invoke-CreatorSession.ps1"
RUNTIME = ROOT / "tools" / "creator-session" / "Invoke-RuntimeCreatorRequest.ps1"
PLUGIN = ROOT / "network" / "mod" / "ComfyQuestRuntime" / "ComfyQuestRuntime.cs"
WORLD_ENTRY = (
    ROOT / "network" / "mod" / "ComfyQuestRuntime" / "RuntimeWorldEntryController.cs"
)


class CreatorSessionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.session = SESSION.read_text(encoding="utf-8")
        cls.runtime = RUNTIME.read_text(encoding="utf-8")
        cls.plugin = PLUGIN.read_text(encoding="utf-8")
        cls.world_entry = WORLD_ENTRY.read_text(encoding="utf-8")

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
                "Launch",
                "Stop",
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
        self.assertIn("character_backup", self.session)
        self.assertIn("RestoreGameState requires the exact pinned world pair", self.session)
        self.assertIn("restored_game_state", self.session)
        self.assertIn("character_profile", self.session)
        self.assertIn("world_entry_quarantine", self.session)
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
        self.assertIn("Godbuild recovery file already exists", self.session)
        self.assertIn("$item.Previous", self.session)

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
                self.assertNotIn(forbidden, self.world_entry)
        close = self.session[self.session.index("} elseif ($Action -eq 'Close')") :]
        self.assertLess(close.index("@('build_off')"), close.index("@('disarm')"))

    def test_private_build_control_uses_direct_bounded_player_apis(self) -> None:
        self.assertIn("SetCreatorBuildMode,CreatorBuildModeEnabled", self.plugin)
        self.assertIn("player.SetNoPlacementCost(enabled)", self.plugin)
        self.assertIn("player.SetGodMode(enabled)", self.plugin)
        self.assertIn("player.NoCostCheat()&&player.InGodMode()", self.plugin)
        self.assertNotIn("Console.instance", self.plugin)
        self.assertNotIn("ZInput.Simulate", self.plugin)

    def test_world_entry_is_exact_bounded_and_machine_owned(self) -> None:
        for expected in (
            "comfy-quest-world-entry-request/v1",
            "'-applaunch', '892970', '-console'",
            "Get-InteractiveSessionFacts",
            "Get-WorldMetadata",
            "Pinned world UID",
            "Get-CharacterProfileMetadata",
            "has no unique saved state for world UID",
            "Valheim exited before the pinned world-entry receipt arrived",
            "World entry rejected:",
            "Stop-ValheimProcess",
            "CloseMainWindow",
            "[Math]::Max(120, $WaitSeconds)",
            "stopped_forcibly",
            "quarantined_partial_saves",
            "forced_process_ids = @($forced | ForEach-Object { $_.Id })",
        ):
            self.assertIn(expected, self.session)
        for expected in (
            "RuntimeWorldEntryRequestPolicy.Validate",
            "DuplicatePropertyNameHandling.Error",
            "world_entry_unknown_field",
            "profile.GetFilename()",
            "matches.Count == 0",
            "matches.Count != 1",
            "world.m_worldName",
            "request.WorldDisplayName",
            "world.m_uid.ToString",
            "exact.Length != 1",
            "ZNet.SetServer(server: true, openServer: false, publicServer: false",
            "world_entry_loaded_world_mismatch",
            'Write("entered", "world_entry_complete"',
        ):
            self.assertIn(expected, self.world_entry)
        for forbidden in (
            "SendKeys",
            "keybd_event",
            "ZInput.Simulate",
            "+connect",
            "server_address",
        ):
            self.assertNotIn(forbidden, self.world_entry)

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
            self.assertEqual(plan["character_profile"], "questyfour")
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
            stale_request = (
                root
                / "BepInEx"
                / "config"
                / "comfy-quest-runtime"
                / "requests"
                / "world-entry.json"
            )
            stale_status = stale_request.parents[1] / "status" / "world-entry.json"
            stale_request.parent.mkdir(parents=True)
            stale_status.parent.mkdir(parents=True)
            stale_request.write_bytes(b"stale-request\n")
            stale_status.write_bytes(b"stale-status\n")

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

            # A late preparation failure restores every byte already touched, including
            # quarantined one-shot state, and never leaves an active install manifest.
            config.mkdir()
            failed_prepare = invoke("Prepare", "-NoBuild", "-EvidenceRoot", str(evidence))
            self.assertNotEqual(0, failed_prepare.returncode)
            self.assertIn("Prepare failed and rolled back", failed_prepare.stderr)
            self.assertEqual(prior_lab, (plugins / "ComfyQuestLab.dll").read_bytes())
            for name in payloads:
                if name != "ComfyQuestLab.dll":
                    self.assertFalse((plugins / name).exists())
            self.assertEqual(b"stale-request\n", stale_request.read_bytes())
            self.assertEqual(b"stale-status\n", stale_status.read_bytes())
            self.assertFalse(
                (root / "BepInEx" / "config" / "comfy-quest-creator" / "session.json").exists()
            )
            failure = json.loads((evidence / "preparation-failure.json").read_text())
            self.assertEqual("rolled_back", failure["state"])
            stale_request.unlink()
            config.rmdir()
            config.write_bytes(prior_config)

            prepared = invoke("Prepare", "-NoBuild", "-EvidenceRoot", str(evidence))
            self.assertEqual(0, prepared.returncode, prepared.stdout + prepared.stderr)
            manifest = json.loads((root / "BepInEx" / "config" / "comfy-quest-creator" / "session.json").read_text())
            self.assertEqual("active", manifest["state"])
            self.assertEqual("fixture-lifecycle", manifest["session_id"])
            self.assertEqual(4, len(manifest["plugins"]))
            for name, payload in payloads.items():
                self.assertEqual(payload, (plugins / name).read_bytes())
            self.assertIn(b"PrivateWorldConfirmed = true", config.read_bytes())
            self.assertFalse(stale_request.exists())
            self.assertFalse(stale_status.exists())

            game_state = root / "fixture-game-state"
            game_state.mkdir()
            world_db = game_state / "ComfyQuestDemo.db"
            world_fwl = game_state / "ComfyQuestDemo.fwl"
            character = game_state / "questyfour.fch"
            original_state = {
                world_db: b"original-world-db\n",
                world_fwl: b"original-world-fwl\n",
                character: b"original-character\n",
            }
            snapshot_root = evidence / "backup"
            snapshot_records = {}
            for source_path, payload in original_state.items():
                source_path.write_bytes(payload)
                kind = "character" if source_path == character else "world"
                backup_path = snapshot_root / kind / source_path.name
                backup_path.parent.mkdir(parents=True, exist_ok=True)
                backup_path.write_bytes(payload)
                snapshot_records[source_path] = {
                    "source": str(source_path),
                    "backup": str(backup_path),
                    "sha256": hashlib.sha256(payload).hexdigest(),
                }
            manifest["world_backup"] = [snapshot_records[world_db], snapshot_records[world_fwl]]
            manifest["character_backup"] = snapshot_records[character]
            context_path = root / "BepInEx" / "config" / "comfy-quest-creator" / "session.json"
            context_path.write_text(json.dumps(manifest), encoding="utf-8")

            status = invoke("Status")
            self.assertEqual(0, status.returncode, status.stdout + status.stderr)
            status_receipt = json.loads(status.stdout[status.stdout.index("{") :])
            self.assertEqual("active", status_receipt["state"])
            self.assertTrue(status_receipt["plugin_hashes_match"])

            # Process recovery remains available if another writer changes installed bytes;
            # mutation/restore actions still refuse that drift.
            (plugins / "ComfyQuestRuntime.dll").write_bytes(b"unexpected-runtime-drift\n")
            stopped = invoke("Stop")
            self.assertEqual(0, stopped.returncode, stopped.stdout + stopped.stderr)
            stopped_receipt = json.loads(stopped.stdout[stopped.stdout.index("{") :])
            self.assertEqual(["ComfyQuestRuntime.dll"], stopped_receipt["plugin_hash_mismatches"])
            (plugins / "ComfyQuestRuntime.dll").write_bytes(payloads["ComfyQuestRuntime.dll"])
            stale_request.write_bytes(b"current-session-request\n")
            stale_status.write_bytes(b"current-session-status\n")

            for source_path in original_state:
                source_path.write_bytes(b"mutated-by-technical-lap\n")

            # Quarantined one-shot state is validated before any install or game mutation.
            # A corrupt prior status cannot be reported as restored or leave a partial close.
            world_entry_backup = Path(manifest["world_entry_quarantine"][0]["backup"])
            world_entry_backup.write_bytes(b"corrupt-world-entry-snapshot\n")
            refused_world_entry_close = invoke("Close", "-Restore", "-RestoreGameState")
            self.assertNotEqual(0, refused_world_entry_close.returncode)
            self.assertIn("world-entry status snapshot hash mismatch", refused_world_entry_close.stderr)
            for source_path in original_state:
                self.assertEqual(b"mutated-by-technical-lap\n", source_path.read_bytes())
            for name, payload in payloads.items():
                self.assertEqual(payload, (plugins / name).read_bytes())
            self.assertIn(b"PrivateWorldConfirmed = true", config.read_bytes())
            self.assertEqual(b"current-session-request\n", stale_request.read_bytes())
            self.assertEqual(b"current-session-status\n", stale_status.read_bytes())
            world_entry_backup.write_bytes(b"stale-status\n")

            # All three snapshots are validated before Close mutates either install or game
            # state, so a corrupt late snapshot cannot leave a half-restored world pair.
            world_fwl_backup = Path(snapshot_records[world_fwl]["backup"])
            world_fwl_backup.write_bytes(b"corrupt-snapshot\n")
            refused_close = invoke("Close", "-Restore", "-RestoreGameState")
            self.assertNotEqual(0, refused_close.returncode)
            self.assertIn("world .fwl snapshot hash mismatch", refused_close.stderr)
            for source_path in original_state:
                self.assertEqual(b"mutated-by-technical-lap\n", source_path.read_bytes())
            for name, payload in payloads.items():
                self.assertEqual(payload, (plugins / name).read_bytes())
            self.assertIn(b"PrivateWorldConfirmed = true", config.read_bytes())
            self.assertEqual(b"current-session-request\n", stale_request.read_bytes())
            self.assertEqual(b"current-session-status\n", stale_status.read_bytes())
            world_fwl_backup.write_bytes(original_state[world_fwl])

            closed = invoke("Close", "-Restore", "-RestoreGameState")
            self.assertEqual(0, closed.returncode, closed.stdout + closed.stderr)
            self.assertEqual(prior_lab, (plugins / "ComfyQuestLab.dll").read_bytes())
            for name in payloads:
                if name != "ComfyQuestLab.dll":
                    self.assertFalse((plugins / name).exists())
            self.assertEqual(prior_config, config.read_bytes())
            for source_path, payload in original_state.items():
                self.assertEqual(payload, source_path.read_bytes())
            self.assertFalse(stale_request.exists())
            self.assertEqual(b"stale-status\n", stale_status.read_bytes())
            closed_manifest = json.loads((evidence / "session.closed.json").read_text())
            self.assertEqual("closed", closed_manifest["state"])
            self.assertTrue(closed_manifest["restored"])
            self.assertTrue(closed_manifest["restored_world_entry_state"])
            self.assertTrue(closed_manifest["restored_game_state"])

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

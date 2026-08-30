import hashlib
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace
import socket
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
PROBE = ROOT / "tools" / "quest-studio" / "architectural_live_probe.py"
DEMO = ROOT / "tools" / "quest-studio" / "Invoke-ArchitecturalDemo.ps1"
SPEC = importlib.util.spec_from_file_location("architectural_live_probe", PROBE)
assert SPEC and SPEC.loader
live = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(live)


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class ArchitecturalLiveHarnessTests(unittest.TestCase):
    def test_valheim_screenshot_selects_the_exact_x11_window(self):
        tree = """
        0x2200019 "Steam": ("steamwebhelper" "steam") 1515x900+1530+584
        0x2a00005 "Valheim": ("valheim.x86_64" "valheim.x86_64") 1920x1080+1+26
        0x220001e "Friends List": ("steamwebhelper" "steam") 339x650+0+0
        """
        window = live.parse_valheim_window(tree)
        self.assertEqual(0x2A00005, window["id"])
        self.assertEqual("0x2a00005", window["id_hex"])
        self.assertEqual(1920, window["width"])
        self.assertEqual(1080, window["height"])
        with self.assertRaisesRegex(RuntimeError, "missing_or_ambiguous"):
            live.parse_valheim_window(tree + tree.splitlines()[2] + "\n")

    def test_prepare_and_restore_are_byte_exact_and_keep_the_staged_pair(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            valheim = root / "valheim"
            run = root / "run"
            unity = root / "unity"
            plugin = valheim / "BepInEx" / "plugins" / "ComfyQuestLab.dll"
            runtime = valheim / "BepInEx" / "config" / "comfy-quest-runtime"
            lab = valheim / "BepInEx" / "config" / "comfy-quest-lab"
            blueprints = lab / "blueprints"
            candidate = root / "candidate.dll"
            name = "tn0304-test"
            pieces_hash = "3" * 64
            for directory in (plugin.parent, runtime, blueprints,
                              unity / "worlds_local", unity / "characters_local", run):
                directory.mkdir(parents=True, exist_ok=True)
            (valheim / "valheim.x86_64").write_bytes(b"executable")
            plugin.write_bytes(b"prior-lab")
            candidate.write_bytes(b"candidate-lab")
            (runtime / "prior.json").write_text('{"prior":true}\n', encoding="utf-8")
            (lab / "prior.txt").write_text("prior\n", encoding="utf-8")
            capture = blueprints / f"{name}.capture.json"
            capture.write_text(json.dumps({
                "Schema": "comfy-questlab-capture/v1",
                "Name": name,
                "Selection": "lab",
                "PieceCount": 40,
                "PiecesSha256": pieces_hash,
            }), encoding="utf-8")
            blueprint = blueprints / f"{name}.blueprint"
            blueprint.write_text("#Pieces\nwood_floor;Building;0;0;0;0;0;0;1;\n",
                                 encoding="utf-8")
            world = unity / "worlds_local" / "ComfyQuestDemo.db"
            character = unity / "characters_local" / "questyfour.fch"
            world.write_bytes(b"world-before")
            character.write_bytes(b"character-before")
            args = SimpleNamespace(
                valheim_root=valheim,
                run_root=run,
                unity_root=unity,
                machine=socket.gethostname(),
                world="ComfyQuestDemo",
                world_uid="-7600395338659582326",
                world_display_name="Comfy Quest Demo",
                character="questyfour",
                session="architectural-live-test",
                blueprint=name,
                piece_count=40,
                capture_sha256=digest(capture),
                blueprint_sha256=digest(blueprint),
                canonical_pieces_sha256=pieces_hash,
                candidate_lab=candidate,
                candidate_lab_sha256=digest(candidate),
            )

            prepared = live.prepare(args)
            self.assertEqual("READY", prepared["status"])
            self.assertEqual(candidate.read_bytes(), plugin.read_bytes())
            self.assertTrue((runtime / "requests" / "world-entry.json").is_file())

            # A warm client restart refreshes only the consumed entry request. It does not
            # redeploy the plugin, restage the pair, or replace the rollback snapshot.
            (runtime / "requests" / "world-entry.json").unlink()
            reentered = live.reenter(args)
            self.assertEqual("READY", reentered["status"])
            self.assertEqual(candidate.read_bytes(), plugin.read_bytes())
            self.assertTrue((runtime / "requests" / "world-entry.json").is_file())

            world.write_bytes(b"world-mutated")
            character.write_bytes(b"character-mutated")
            (runtime / "generated-receipt.json").write_text("generated")
            (lab / "generated-receipt.json").write_text("generated")
            (unity / "Player.log").write_text("\n".join([
                "[world-entry] entered: world_entry_complete",
                "completed — blueprint_check",
                "completed — blueprint_build",
                "completed — blueprint_diff",
                "completed — blueprint_clear",
                "Game - OnApplicationQuit",
                "World saved",
            ]), encoding="utf-8")
            restored = live.restore(args)

            self.assertEqual("PASS", restored["status"])
            self.assertEqual(b"prior-lab", plugin.read_bytes())
            self.assertEqual(b"world-before", world.read_bytes())
            self.assertEqual(b"character-before", character.read_bytes())
            self.assertEqual(args.capture_sha256, digest(capture))
            self.assertEqual(args.blueprint_sha256, digest(blueprint))
            self.assertFalse((runtime / "requests" / "world-entry.json").exists())
            self.assertFalse((runtime / "generated-receipt.json").exists())
            self.assertFalse((lab / "generated-receipt.json").exists())

    def test_live_contract_uses_placed_build_and_placed_diff_before_clear(self):
        source = PROBE.read_text(encoding="utf-8")
        build = source.index('driver.request("lab", "blueprint_build"')
        diff = source.index('driver.request("lab", "blueprint_diff"')
        clear = source.index('driver.request("lab", "blueprint_clear"')
        self.assertLess(build, diff)
        self.assertLess(diff, clear)
        self.assertIn('"build_mode": "at"', source)
        self.assertIn('"yaw_degrees": number(self.args.yaw)', source)
        self.assertIn("canonical_artifact_drift", source)

    def test_ground_mode_never_fabricates_an_exact_world_transform(self):
        args = SimpleNamespace(
            blueprint="field-lodge", placement_mode="ground",
            x=12.5, y=1.25, z=-3.75, yaw=0.0,
        )
        driver = live.LiveDriver.__new__(live.LiveDriver)
        driver.args = args
        self.assertEqual(
            {"blueprint_name": "field-lodge", "build_mode": "ground"},
            driver.lab_fields("blueprint_build", placed=True))
        self.assertEqual(
            {"blueprint_name": "field-lodge", "selection": "lab"},
            driver.lab_fields("blueprint_diff", placed=True))
        self.assertTrue(driver.placement_matches({}))
        self.assertFalse(driver.placement_matches({"placement": {"x": 12.5}}))

    def test_warm_lap_reuses_a_matching_build_and_retains_it(self):
        source = PROBE.read_text(encoding="utf-8")
        warm = source[source.index("def run_warm"):source.index("def safe_replace_tree")]
        self.assertIn('build_action = "reused"', warm)
        self.assertIn('build_action = "created"', warm)
        self.assertIn("if failure is not None and driver.build_attempted", warm)
        self.assertIn('"marked_pieces_retained": failure is None', warm)
        self.assertIn('"valheim_left_running": running', warm)
        self.assertNotIn("blueprint_clear_not_completed", warm)

        screenshot = source[source.index("def capture_screenshot"):source.index("def run_live")]
        self.assertIn('"-window_id", str(window["id"])', screenshot)
        self.assertNotIn("xdpyinfo", screenshot)

    def test_demo_driver_is_bounded_to_warm_reuse_and_studio_control(self):
        source = DEMO.read_text(encoding="utf-8")
        self.assertIn("[ValidateSet('Open', 'Status', 'StopStudio')]", source)
        self.assertIn("comfy-quest-architectural-demo/v1", source)
        self.assertIn("architectural_demo_warm_reuse_contract_failed", source)
        self.assertIn("existing_build_reused", source)
        self.assertIn("warm_close_is_separate", source)
        self.assertIn("if (-not [string]::IsNullOrEmpty($Body))", source)
        self.assertIn("$warm.sequence | ForEach-Object", source)
        self.assertIn("status|blueprint_check|blueprint_count|blueprint_diff|status", source)
        self.assertIn("canonical_stage_idempotent", source)
        self.assertIn("control_plane_reused", source)
        self.assertNotIn("& $warmScript -Close", source)
        self.assertNotIn("Invoke-ArchitecturalLiveJourney.ps1", source)
        self.assertNotIn("Invoke-CreatorSession.ps1", source)
        self.assertNotIn("blueprint_build\"", source)
        self.assertNotIn("blueprint_clear\"", source)


if __name__ == "__main__":
    unittest.main()

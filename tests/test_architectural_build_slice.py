import hashlib
import importlib.util
import json
import tempfile
import zipfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
CAPSULE = ROOT / "artifacts" / "architectural-build" / "tn0304-architectural-build-capsule.zip"
PROBE_PATH = ROOT / "tools" / "quest-studio" / "probe_architectural_stage.py"
PROBE_SPEC = importlib.util.spec_from_file_location("probe_architectural_stage", PROBE_PATH)
PROBE = importlib.util.module_from_spec(PROBE_SPEC)
assert PROBE_SPEC.loader is not None
PROBE_SPEC.loader.exec_module(PROBE)


class ArchitecturalBuildSliceTests(unittest.TestCase):
    def test_capsule_is_hash_pinned_and_closed(self):
        if not CAPSULE.is_file():
            self.skipTest("requires the locally staged architectural capsule")
        with zipfile.ZipFile(CAPSULE) as archive:
            names = set(archive.namelist())
            self.assertEqual({"capsule.json", "solved-building.graph.json", "constraint-model.json",
                              "interpretation-receipt.json", "compilation-receipt.json", "pieces.json",
                              "architectural-candidate.capture.json", "prefab-geometry.json"}, names)
            capsule = json.loads(archive.read("capsule.json"))
            self.assertEqual("creator-os-architectural-build-capsule/v0", capsule["schema"])
            self.assertEqual(40, capsule["piece_count"])
            for name, pin in capsule["members"].items():
                data = archive.read(name)
                self.assertEqual(len(data), pin["bytes"])
                self.assertEqual(hashlib.sha256(data).hexdigest(), pin["sha256"])

    def test_studio_build_boundary_is_separate_and_world_safe(self):
        endpoints = (ROOT / "src" / "Quest.Studio" / "QuestStudioEndpoints.cs").read_text(encoding="utf-8")
        service = (ROOT / "src" / "Quest.Studio" / "QuestStudioBuilds.cs").read_text(encoding="utf-8")
        page = (ROOT / "src" / "Quest.Studio" / "QuestStudioPage.cs").read_text(encoding="utf-8")
        for route in ("/api/v2/quest-studio/builds", "/api/v2/quest-studio/builds/import",
                      "/placement", "/stage"):
            self.assertIn(route, endpoints)
        for marker in ("MaxCompressedBytes", "MaxExpandedBytes", "MaxMembers", "MaxPieces",
                       "architectural_importer_unavailable", "revision_conflict", "stage_collision",
                       "comfy-quest-studio-build-stage/v1", "NoWorldMutation",
                       "MailboxRequestWritten", "WorldMutationPerformed"):
            self.assertIn(marker, service)
        for marker in ("Architectural Build", "build-architecture", "build-pieces",
                       "build-save-placement", "Stage canonical Lab pair", "no mailbox",
                       "buildFeature(build", "prefabGeometry(build"):
            self.assertIn(marker, page)
        for hardcoded_view_value in ("Footprint 7.953375", "40 pieces</strong>",
                                     "wood_floor × 16"):
            self.assertNotIn(hardcoded_view_value, page)

    def test_build_journey_exposes_explicit_warm_client_contract(self):
        journey = (ROOT / "tools" / "quest-studio" /
                   "Invoke-ArchitecturalBuildJourney.ps1").read_text(encoding="utf-8")
        for marker in ("[switch]$AllowWarmClient", "--allow-warm-client",
                       "valheim_process_reused", "warm_client_allowed"):
            self.assertIn(marker, journey)


class ArchitecturalStageWarmProofTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.valheim = self.root / "valheim"
        self.blueprints = (self.valheim / "BepInEx" / "config" /
                           "comfy-quest-lab" / "blueprints")
        self.blueprints.mkdir(parents=True)
        self.state = self.root / "state"
        build = self.state / "quest-studio" / "builds" / "build-1"
        build.mkdir(parents=True)
        artifacts = {}
        for kind, suffix in (("capture", ".capture.json"),
                             ("blueprint", ".blueprint")):
            path = self.blueprints / ("tn0304" + suffix)
            path.write_bytes(kind.encode())
            artifacts[kind] = {
                "path": str(path.resolve()),
                "name": path.name,
                "bytes": path.stat().st_size,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            }
        stage = {
            "schema": PROBE.STAGE_SCHEMA,
            "placement_applied": False,
            "creator_session_started": False,
            "mailbox_request_written": False,
            "world_mutation_performed": False,
            "artifacts": artifacts,
        }
        (build / "latest-stage.json").write_text(json.dumps(stage), encoding="utf-8")
        self.process = [{"pid": 42, "command": "valheim.x86_64",
                         "arguments": "/home/derek/valheim/valheim.x86_64"}]
        world_files = {
            "worlds_local/ComfyQuestDemo.db": self.pin(b"world"),
            "characters_local/questyfour.fch": self.pin(b"character"),
        }
        blueprint_files = {
            artifact["name"]: self.pin((self.blueprints / artifact["name"]).read_bytes())
            for artifact in artifacts.values()
        }
        self.before = {
            "machine": "am4",
            "valheim_root": str(self.valheim.resolve()),
            "valheim_processes": self.process,
            "world_and_character_files": world_files,
            "lab_control_files": {},
            "creator_session_files": {},
            "blueprint_files": blueprint_files,
        }
        self.after = dict(self.before)
        self.after["blueprint_files"] = blueprint_files
        self.before_path = self.root / "before.json"

    def tearDown(self):
        self.temporary.cleanup()

    @staticmethod
    def pin(data: bytes) -> dict:
        return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
                "mtime_ns": 1}

    def verify(self):
        self.before_path.write_text(json.dumps(self.before), encoding="utf-8")
        with mock.patch.object(PROBE, "snapshot", return_value=self.after):
            return PROBE.verify(self.before_path, self.valheim, self.state,
                                "ComfyQuestDemo", "questyfour", True)

    def test_same_process_and_append_only_event_archive_pass(self):
        archive = (self.valheim / "BepInEx" / "config" / "comfy-quest-lab" /
                   "event-archive" / "events.jsonl")
        archive.parent.mkdir(parents=True)
        old = b'{"event":1}\n'
        archive.write_bytes(old + b'{"event":2}\n')
        self.before["lab_control_files"] = {"event-archive/events.jsonl": self.pin(old)}
        self.after["lab_control_files"] = {
            "event-archive/events.jsonl": self.pin(archive.read_bytes())}
        proof = self.verify()
        self.assertEqual("PASS", proof["status"])
        self.assertTrue(proof["assertions"]["valheim_process_reused"])
        self.assertTrue(proof["assertions"]["warm_event_archive_only"])

    def test_process_identity_drift_fails(self):
        self.after["valheim_processes"] = [{**self.process[0], "pid": 43}]
        proof = self.verify()
        self.assertIn("valheim_process_changed", proof["failures"])

    def test_non_archive_lab_drift_fails(self):
        self.after["lab_control_files"] = {"requests/build.json": self.pin(b"request")}
        proof = self.verify()
        self.assertIn("lab_control_files_changed", proof["failures"])

    def test_world_or_character_drift_fails(self):
        self.after["world_and_character_files"] = {
            **self.before["world_and_character_files"],
            "worlds_local/ComfyQuestDemo.db": self.pin(b"changed"),
        }
        proof = self.verify()
        self.assertIn("world_and_character_files_changed", proof["failures"])


if __name__ == "__main__":
    unittest.main()

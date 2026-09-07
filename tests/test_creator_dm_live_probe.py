"""The Linux lap must restore the user's actual prior bytes, including failed setup."""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

TOOLS = Path(__file__).resolve().parents[1] / "tools" / "quest-studio"
sys.path.insert(0, str(TOOLS))
import creator_dm_live_probe as probe


class CreatorDmSessionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game, self.unity = self.root / "game", self.root / "unity"
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        for name in probe.PLUGINS:
            self.put(self.game / "BepInEx/plugins" / name, "original " + name)
            self.put(self.bundle / name, "candidate " + name)
        self.put(self.game / "BepInEx/config/comfy-quest-runtime/state.json", "original runtime")
        self.put(self.game / "BepInEx/config/djcdevelopment.valheim.comfyquestruntime.cfg",
                 "[Safety]\nPrivateWorldConfirmed = false\n[Other]\nKeep = yes\n")
        for suffix in (".db", ".fwl", ".db.old"):
            self.put(self.unity / "worlds_local" / (probe.WORLD + suffix), "original " + suffix)
        self.put(self.unity / "characters_local" / (probe.CHARACTER + ".fch"), "original character")
        self.manifest = self.bundle / "manifest.json"
        self.manifest.write_text(json.dumps({
            "schema": "comfy-quest-creator-dm-release/v1", "source_revision": "a" * 40,
            "plugins": [{"name": name, "path": name,
                         "bytes": (self.bundle / name).stat().st_size,
                         "sha256": probe.base.sha256(self.bundle / name)} for name in probe.PLUGINS]}))
        self.session = probe.Session(self.game, self.unity, self.root / "recovery", "am4", "proof-one")
        self.addCleanup(patch.stopall)
        patch.object(probe.socket, "gethostname", return_value="am4").start()
        patch.object(probe.base, "process_snapshot", return_value=[]).start()
        self.original_game = probe.tree_hash(self.game)
        self.original_unity = probe.tree_hash(self.unity)

    @staticmethod
    def put(path, text):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)

    def assertOriginal(self):
        self.assertEqual(self.original_game, probe.tree_hash(self.game))
        self.assertEqual(self.original_unity, probe.tree_hash(self.unity))

    def test_world_checkpoint_keeps_original_recovery_and_restores_everything(self):
        self.session.prepare(self.manifest)
        self.put(self.unity / "worlds_local" / (probe.WORLD + ".db"), "saved Field Lodge")
        checkpoint = self.session.checkpoint()
        db = next(p for p in checkpoint["world_backup"] if p["source"].endswith(".db"))
        self.assertEqual("saved Field Lodge", Path(db["backup"]).read_text())
        context = probe.base.read_json(self.game / probe.SESSION)
        self.assertEqual(db["sha256"], context["world_backup"][0]["sha256"])
        self.put(self.unity / "worlds_local" / (probe.WORLD + ".db.new"), "game output")
        self.put(self.game / "BepInEx/config/comfy-quest-runtime/actions/new.json", "cast")
        self.assertTrue(self.session.restore()["all_original_bytes_restored"])
        self.assertOriginal()
        self.assertTrue(self.session.restore()["replayed"])
        self.assertOriginal()

    def test_failed_second_plugin_install_restores_first_and_prior_session_absence(self):
        original_copy = probe.base.atomic_copy
        count = 0
        def failing_copy(source, target):
            nonlocal count
            count += 1
            if count == 2:
                raise OSError("injected failed deployment")
            original_copy(source, target)
        with patch.object(probe.base, "atomic_copy", side_effect=failing_copy):
            with self.assertRaisesRegex(OSError, "injected"):
                self.session.prepare(self.manifest)
        self.assertOriginal()
        self.assertEqual("restored", self.session.read()["state"])

    def test_restore_replay_releases_only_its_own_leftover_lock(self):
        self.session.prepare(self.manifest)
        self.session.restore()
        self.put(self.session.lock, json.dumps({
            "session_id": self.session.session, "run_root": str(self.session.run)}))
        self.assertTrue(self.session.restore()["replayed"])
        self.assertFalse(self.session.lock.exists())
        self.put(self.session.lock, json.dumps({"session_id": "another"}))
        self.session.restore()
        self.assertTrue(self.session.lock.exists())

    def test_bad_artifact_is_refused_before_any_install_change(self):
        self.put(self.bundle / probe.PLUGINS[0], "tampered")
        with self.assertRaisesRegex(RuntimeError, "artifact_hash_mismatch"):
            self.session.prepare(self.manifest)
        self.assertOriginal()
        self.assertFalse(self.session.lock.exists())

    def test_wrong_machine_and_running_game_refuse_before_any_change(self):
        with patch.object(probe.socket, "gethostname", return_value="other"):
            with self.assertRaisesRegex(RuntimeError, "machine_identity"):
                self.session.prepare(self.manifest)
        with patch.object(probe.base, "process_snapshot", return_value=[{"pid": 1}]):
            with self.assertRaisesRegex(RuntimeError, "must_be_stopped"):
                self.session.prepare(self.manifest)
        self.assertOriginal()

    def test_active_session_and_install_owner_are_preserved(self):
        self.put(self.game / probe.SESSION, json.dumps({"state": "active"}))
        before = probe.tree_hash(self.game)
        with self.assertRaisesRegex(RuntimeError, "already_active"):
            self.session.prepare(self.manifest)
        self.assertEqual(before, probe.tree_hash(self.game))
        (self.game / probe.SESSION).unlink()
        self.put(self.session.lock, json.dumps({"session_id": "another"}))
        before = probe.tree_hash(self.game)
        with self.assertRaisesRegex(RuntimeError, "owned_by_another"):
            self.session.prepare(self.manifest)
        self.assertEqual(before, probe.tree_hash(self.game))

    def test_corrupt_backup_blocks_restoration_without_touching_current_install(self):
        self.session.prepare(self.manifest)
        self.put(self.session.run / "backup/valheim/BepInEx/plugins" / probe.PLUGINS[0], "corrupted")
        before = probe.tree_hash(self.game)
        with self.assertRaisesRegex(RuntimeError, "backup_hash_mismatch"):
            self.session.restore()
        self.assertEqual(before, probe.tree_hash(self.game))
        self.assertTrue(self.session.lock.exists())

    def test_restore_cannot_claim_another_sessions_lock(self):
        self.session.prepare(self.manifest)
        self.put(self.session.lock, json.dumps({"session_id": "another", "run_root": "elsewhere"}))
        before = probe.tree_hash(self.game)
        with self.assertRaisesRegex(RuntimeError, "owned_by_another"):
            self.session.restore()
        self.assertEqual(before, probe.tree_hash(self.game))

    def test_traversal_and_overlapping_recovery_are_refused(self):
        with self.assertRaisesRegex(RuntimeError, "relative_path_invalid"):
            probe.contained(self.game, "../unowned")
        with self.assertRaisesRegex(RuntimeError, "overlaps_install"):
            probe.Session(self.game, self.unity, self.game / "backup", "am4", "lap")


if __name__ == "__main__":
    unittest.main()

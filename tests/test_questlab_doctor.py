from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "questlab-doctor" / "questlab_doctor.py"
SPEC = importlib.util.spec_from_file_location("questlab_doctor", SCRIPT)
DOCTOR = importlib.util.module_from_spec(SPEC)
assert SPEC.loader
SPEC.loader.exec_module(DOCTOR)


def write(path: Path, value: str | bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(value if isinstance(value, bytes) else value.encode("utf-8"))


def write_json(path: Path, value: object) -> None:
    write(path, json.dumps(value))


class QuestLabDoctorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.repo = self.root / "repo"
        self.valheim = self.root / "valheim"
        self.release = "questlab-v0.2.0-test"
        write(
            self.repo / "network/mod/ComfyQuestLab/ComfyQuestLab.cs",
            f'public const string PluginVersion = "0.2.0";\npublic const string ReleaseId = "{self.release}";\n',
        )
        write_json(
            self.repo / "network/mod/ComfyQuestLab/manifest.json",
            {"version_number": "0.2.0"},
        )
        write_json(
            self.repo / "tools/component-packets/samples/quest-capability-manifest.json",
            {
                "Schema": "comfy-quest-capabilities/v1",
                "Counts": DOCTOR.EXPECTED_COUNTS,
                "CreatorSafeEvents": ["kill"] + [f"event_{i}" for i in range(33)],
            },
        )
        dll = b"exact dll"
        write(self.repo / "network/mod/ComfyQuestLab/bin/Release/ComfyQuestLab.dll", dll)
        write(self.valheim / "BepInEx/plugins/ComfyQuestLab.dll", dll)
        write(
            self.valheim / "BepInEx/LogOutput.log",
            f"[Info :ComfyQuestLab] quest lab 0.2.0 ({self.release}) — 87/87 seams hooked\n",
        )
        write_json(
            self.valheim / "BepInEx/config/comfy-quest-lab/quests/example.json",
            {
                "schema_version": 1,
                "quests": [
                    {"quest_id": "first", "name": "First", "guild": "Test", "trigger": {"event": "kill"}}
                ],
            },
        )
        for suite, count in (("creator-events", 34), ("all-schools", 8)):
            write_json(
                self.valheim / f"BepInEx/config/comfy-quest-lab/receipts/suites/{suite}-1.json",
                {
                    "release_id": self.release,
                    "state": "complete",
                    "verdict": "pass",
                    "witnessed_events": count,
                    "double_completions": 0,
                },
            )

    def tearDown(self) -> None:
        self.temp.cleanup()

    def test_healthy_fixture_passes_without_private_paths(self) -> None:
        report = DOCTOR.collect(self.repo, self.valheim)
        self.assertEqual(report["verdict"], "pass")
        encoded = json.dumps(report)
        self.assertNotIn(str(self.root), encoded)
        self.assertFalse(report["privacy"]["raw_logs_included"])

    def test_installed_dll_mismatch_fails(self) -> None:
        write(self.valheim / "BepInEx/plugins/ComfyQuestLab.dll", b"different")
        report = DOCTOR.collect(self.repo, self.valheim)
        self.assertEqual(report["verdict"], "fail")
        result = next(item for item in report["checks"] if item["name"] == "installed-dll")
        self.assertEqual(result["status"], "fail")

    def test_stale_live_release_warns(self) -> None:
        write(
            self.valheim / "BepInEx/LogOutput.log",
            "[Info :ComfyQuestLab] quest lab 0.2.0 (older) — 87/87 seams hooked\n",
        )
        report = DOCTOR.collect(self.repo, self.valheim)
        self.assertEqual(report["verdict"], "warn")
        result = next(item for item in report["checks"] if item["name"] == "last-live-identity")
        self.assertIn("Start the exact installed build", result["remedy"])

    def test_partial_tree_ledger_fails_closed(self) -> None:
        write_json(
            self.valheim / "BepInEx/config/comfy-quest-lab/tree-recovery/ledger.json",
            {
                "Schema": "comfy-questlab-tree-recovery/v1",
                "RecordCount": 2,
                "RemovedCount": 2,
                "Restored": False,
                "Trees": [{"Prefab": "Beech1"}],
            },
        )
        report = DOCTOR.collect(self.repo, self.valheim)
        self.assertEqual(report["verdict"], "fail")
        self.assertEqual(report["tree_recovery"]["unreadable"], 1)

    def test_complete_pending_tree_ledger_is_warning_not_failure(self) -> None:
        write_json(
            self.valheim / "BepInEx/config/comfy-quest-lab/tree-recovery/ledger.json",
            {
                "Schema": "comfy-questlab-tree-recovery/v1",
                "RecordCount": 2,
                "RemovedCount": 2,
                "Restored": False,
                "Trees": [{"Prefab": "Beech1"}, {"Prefab": "Beech2"}],
            },
        )
        report = DOCTOR.collect(self.repo, self.valheim)
        self.assertEqual(report["verdict"], "warn")
        self.assertEqual(report["tree_recovery"]["pending_records"], 2)

    def test_invalid_quest_reports_index_not_filename_or_content(self) -> None:
        private = self.valheim / "BepInEx/config/comfy-quest-lab/quests/private-guild-name.json"
        write_json(private, {"schema_version": 1, "quests": [{"quest_id": "x", "name": "Secret"}]})
        report = DOCTOR.collect(self.repo, self.valheim)
        encoded = json.dumps(report)
        self.assertNotIn("private-guild-name", encoded)
        self.assertNotIn("Secret", encoded)
        self.assertEqual(report["verdict"], "fail")

    def test_support_capsule_contains_only_report_and_privacy_note(self) -> None:
        report = DOCTOR.collect(self.repo, self.valheim)
        bundle = self.root / "support.zip"
        DOCTOR.write_bundle(bundle, report)
        with zipfile.ZipFile(bundle) as archive:
            self.assertEqual(set(archive.namelist()), {"questlab-doctor.json", "README.txt"})


if __name__ == "__main__":
    unittest.main()

from __future__ import annotations

import hashlib
import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "quest-packs" / "quest_pack.py"
SPEC = importlib.util.spec_from_file_location("quest_pack", SCRIPT)
PACK = importlib.util.module_from_spec(SPEC)
assert SPEC.loader
SPEC.loader.exec_module(PACK)
CAPABILITIES = ROOT / "tools" / "component-packets" / "samples" / "quest-capability-manifest.json"


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2), encoding="utf-8")


def quest_view(event: str = "kill", quest_id: str = "first_blood") -> dict:
    return {
        "schema_version": 1,
        "player": {"name": "Creator", "discord": None},
        "created_at": "2026-08-09T00:00:00Z",
        "picker_version": 1,
        "quests": [
            {
                "quest_id": quest_id,
                "name": "First Blood",
                "guild": "Test",
                "era": 1,
                "category": "Combat",
                "trigger": {"event": event, "target": "Greyling", "where": {"projectile": False}},
            }
        ],
    }


class QuestPackTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.root / "source"
        write_json(
            self.source / PACK.SOURCE_FILE,
            {
                "schema": PACK.SOURCE_SCHEMA,
                "pack_id": "test.first-course",
                "name": "First Course",
                "version": "1.0.0",
                "creator": "Test Creator",
                "license": "CC-BY-4.0",
                "description": "A deterministic fixture.",
            },
        )
        write_json(self.source / "quests" / "first.json", quest_view())
        (self.source / "blueprints").mkdir()
        (self.source / "blueprints" / "court.blueprint").write_text("# fixture\npiece;0;0;0\n", encoding="utf-8")

    def tearDown(self) -> None:
        self.temp.cleanup()

    def build(self, name: str = "pack.questpack") -> Path:
        output = self.root / name
        PACK.build_pack(self.source, output, CAPABILITIES)
        return output

    def test_build_is_byte_deterministic_and_manifest_is_complete(self) -> None:
        first = self.build("first.questpack")
        second = self.build("second.questpack")
        self.assertEqual(first.read_bytes(), second.read_bytes())
        manifest, payload, warnings = PACK.read_verified_pack(first, CAPABILITIES)
        self.assertEqual(warnings, [])
        self.assertEqual(manifest["pack_id"], "test.first-course")
        self.assertEqual(manifest["quest_ids"], ["first_blood"])
        self.assertEqual(manifest["requirements"]["creator_events"], ["kill"])
        self.assertEqual(set(payload), {"quests/first.json", "blueprints/court.blueprint"})

    def test_tampered_payload_fails_hash_verification(self) -> None:
        package = self.build()
        tampered = self.root / "tampered.questpack"
        with zipfile.ZipFile(package) as source, zipfile.ZipFile(tampered, "w") as target:
            for info in source.infolist():
                data = source.read(info.filename)
                if info.filename == "quests/first.json":
                    data += b" "
                target.writestr(info.filename, data)
        with self.assertRaisesRegex(PACK.PackError, "SHA-256 mismatch"):
            PACK.read_verified_pack(tampered, CAPABILITIES)

    def test_unlisted_payload_and_path_traversal_fail(self) -> None:
        package = self.build()
        extra = self.root / "extra.questpack"
        with zipfile.ZipFile(package) as source, zipfile.ZipFile(extra, "w") as target:
            for info in source.infolist():
                target.writestr(info.filename, source.read(info.filename))
            target.writestr("docs/unlisted.md", b"no")
        with self.assertRaisesRegex(PACK.PackError, "manifest/payload mismatch"):
            PACK.read_verified_pack(extra, CAPABILITIES)
        traversal = self.root / "traversal.questpack"
        with zipfile.ZipFile(traversal, "w") as target:
            target.writestr("../outside", b"no")
        with self.assertRaisesRegex(PACK.PackError, "unsafe pack path"):
            PACK.read_verified_pack(traversal, CAPABILITIES)

    def test_unsupported_event_and_nested_where_fail_build(self) -> None:
        write_json(self.source / "quests" / "first.json", quest_view("raw_method_name"))
        with self.assertRaisesRegex(PACK.PackError, "unsupported creator event"):
            self.build()
        nested = quest_view()
        nested["quests"][0]["trigger"]["where"] = {"bad": {"nested": True}}
        write_json(self.source / "quests" / "first.json", nested)
        with self.assertRaisesRegex(PACK.PackError, "must be a scalar"):
            self.build()

    def test_duplicate_ids_across_files_fail_build(self) -> None:
        write_json(self.source / "quests" / "second.json", quest_view(quest_id="first_blood"))
        with self.assertRaisesRegex(PACK.PackError, "unique across the pack"):
            self.build()

    def test_receipts_create_only_evidence_backed_badges(self) -> None:
        write_json(
            self.source / "receipts" / "suite.json",
            {
                "schema": PACK.SUITE_SCHEMA,
                "suite": "all-schools",
                "evidence_kind": "live-gameplay",
                "state": "complete",
                "verdict": "pass",
                "double_completions": 0,
            },
        )
        manifest, _, _ = PACK.read_verified_pack(self.build(), CAPABILITIES)
        self.assertEqual(
            [item["badge"] for item in manifest["certifications"]],
            ["all-schools-live-witnessed", "same-action-dedupe-verified"],
        )

    def test_install_preview_has_no_side_effects(self) -> None:
        package = self.build()
        quest_dir = self.root / "valheim" / "quests"
        result = PACK.install_pack(package, quest_dir, None, CAPABILITIES, True)
        self.assertTrue(result["ready"])
        self.assertFalse(quest_dir.exists())

    def test_install_never_overwrites_and_uninstall_removes_only_its_files(self) -> None:
        package = self.build()
        quest_dir = self.root / "valheim" / "quests"
        installed = PACK.install_pack(package, quest_dir, None, CAPABILITIES, False)
        target = Path(installed["quest_files"][0]["target"])
        self.assertTrue(target.is_file())
        self.assertEqual(hashlib.sha256(target.read_bytes()).hexdigest(), installed["quest_files"][0]["sha256"])
        with self.assertRaisesRegex(PACK.PackError, "conflict"):
            PACK.install_pack(package, quest_dir, None, CAPABILITIES, False)
        result = PACK.uninstall_pack("test.first-course", quest_dir, None, None)
        self.assertEqual(result["removed_quest_files"], [str(target)])
        self.assertFalse(target.exists())

    def test_uninstall_refuses_a_creator_modified_quest(self) -> None:
        package = self.build()
        quest_dir = self.root / "valheim" / "quests"
        installed = PACK.install_pack(package, quest_dir, None, CAPABILITIES, False)
        target = Path(installed["quest_files"][0]["target"])
        target.write_text("creator changed this", encoding="utf-8")
        with self.assertRaisesRegex(PACK.PackError, "modified quest"):
            PACK.uninstall_pack("test.first-course", quest_dir, None, None)
        self.assertTrue(target.exists())
        self.assertTrue(Path(installed["install_dir"]).exists())

    def test_uninstall_refuses_modified_pack_assets(self) -> None:
        package = self.build()
        quest_dir = self.root / "valheim" / "quests"
        installed = PACK.install_pack(package, quest_dir, None, CAPABILITIES, False)
        blueprint = Path(installed["install_dir"]) / "blueprints" / "court.blueprint"
        blueprint.write_text("creator changed this", encoding="utf-8")
        with self.assertRaisesRegex(PACK.PackError, "modified pack payload"):
            PACK.uninstall_pack("test.first-course", quest_dir, None, None)
        self.assertTrue(blueprint.exists())


if __name__ == "__main__":
    unittest.main()

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import unittest
import zipfile
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
BUNDLE = REPO / "examples" / "demo-world" / "first-portal"


class DemoWorldFirstPortalTests(unittest.TestCase):
    def test_generated_runtime_v2_bundle_is_current(self) -> None:
        subprocess.run(
            [sys.executable, str(REPO / "tools" / "quest-studio" / "build_demo_world_first_portal.py"), "--check"],
            cwd=REPO,
            check=True,
        )

    def test_manifest_pins_every_tutorial_file(self) -> None:
        manifest = json.loads((BUNDLE / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual("comfy-quest-tutorial-bundle/v1", manifest["schema"])
        self.assertEqual("minimal-event", manifest["role"])
        for entry in manifest["files"].values():
            data = (BUNDLE / entry["path"]).read_bytes()
            self.assertEqual(len(data), entry["byte_count"], entry["path"])
            self.assertEqual(hashlib.sha256(data).hexdigest(), entry["sha256"], entry["path"])

    def test_source_compiled_behavior_and_pack_use_production_contracts(self) -> None:
        source = json.loads((BUNDLE / "studio-project.json").read_text(encoding="utf-8"))
        compiled = json.loads((BUNDLE / "experience.json").read_text(encoding="utf-8"))
        expected = json.loads((BUNDLE / "expected.json").read_text(encoding="utf-8"))
        self.assertEqual(3, source["schema_version"])
        self.assertEqual("comfy-quest-experience/v1", compiled["schema"])
        route = source["nodes"][0]["routes"][0]
        self.assertEqual("player_teleported", route["event"])
        self.assertIsNone(route["target"])
        self.assertEqual("complete", route["outcome"])
        self.assertEqual(expected["behavior"]["message"], route["actions"][0]["text"])
        target = expected["demo_world"]["binding_target"]
        self.assertEqual("generated_cast_here_tutorial_sign", target["role"])
        self.assertEqual("CAST HERE", target["text_heading"])
        self.assertEqual("fixed_center_crosshair", target["targeting"])
        self.assertTrue(target["backtick_requires_f9_drawer_open"])
        self.assertFalse(target["ui_mouse_cursor_selects_world_target"])
        self.assertTrue(target["must_be_immediately_visible_from_hub_arrival"])
        guidance = target["guidance_path"]
        self.assertEqual("itemstandh", guidance["breadcrumb_prefab"])
        self.assertEqual(3, guidance["breadcrumb_count"])
        self.assertEqual("arcane_sight_only", guidance["breadcrumb_visual"])
        self.assertEqual("piece_groundtorch", guidance["destination_prefab"])
        self.assertTrue(guidance["infinite_fuel"])
        self.assertTrue(guidance["walking_lane_remains_clear"])
        self.assertEqual("Creator Hub ascent portal", expected["demo_world"]["unavoidable_ascent_portal"]["to"])
        self.assertEqual(0, expected["demo_world"]["unavoidable_ascent_portal"]["upper_portal_yaw"])
        self.assertTrue(expected["demo_world"]["unavoidable_ascent_portal"]["arrival_faces_binding_target"])
        self.assertEqual("unbound_no_progress", expected["demo_world"]["unavoidable_ascent_portal"]["imported_fork_state"])
        self.assertEqual("nearest obvious portal", expected["demo_world"]["tutorial_completion_portal"]["role"])
        self.assertTrue(expected["demo_world"]["tutorial_completion_portal"]["any_portal_is_valid"])
        self.assertEqual("load_selected", expected["canonical_artifact"]["activation_receipt"]["operation"])
        self.assertEqual(
            ["matching_prebound_ascent", "portable_cast_then_any_portal"],
            [path["id"] for path in expected["canonical_artifact"]["accepted_paths"]],
        )
        self.assertFalse(expected["replay_precondition"]["world_restore_alone_resets_completion"])

        with zipfile.ZipFile(BUNDLE / "demo-world-first-portal-1.0.0.questpack") as archive:
            self.assertEqual(
                {"manifest.json", "experiences/demo-world-first-portal.json"},
                set(archive.namelist()),
            )
            for entry in archive.infolist():
                self.assertEqual(3, entry.create_system)
                self.assertEqual(zipfile.ZIP_STORED, entry.compress_type)
                self.assertEqual((1980, 1, 1, 0, 0, 0), entry.date_time)
            runtime_manifest = json.loads(archive.read("manifest.json"))
            self.assertEqual("comfy-quest-pack/v2", runtime_manifest["schema"])
            self.assertEqual("demo-world-first-portal", runtime_manifest["pack_id"])
            self.assertEqual(compiled, json.loads(archive.read("experiences/demo-world-first-portal.json")))

    def test_import_surface_accepts_browser_json_not_a_server_path(self) -> None:
        endpoints = (REPO / "src" / "Quest.Studio" / "QuestStudioEndpoints.cs").read_text(encoding="utf-8")
        workspace = (REPO / "src" / "Quest.Studio" / "QuestStudioWorkspace.cs").read_text(encoding="utf-8")
        page = (REPO / "src" / "Quest.Studio" / "QuestStudioPage.cs").read_text(encoding="utf-8")
        self.assertIn('MapPost("/api/v2/quest-studio/projects/import"', endpoints)
        self.assertIn("StudioImportRequest? body", endpoints)
        self.assertIn("MaxImportRequestBytes = 1024 * 1024 + 1024", endpoints)
        self.assertIn("RequestSizeLimitAttribute(MaxImportRequestBytes)", endpoints)
        self.assertIn("record StudioImportRequest(System.Text.Json.JsonElement? Project)", workspace)
        self.assertNotIn("record StudioImportRequest(string", workspace)
        self.assertIn('type="file" accept=".json,application/json"', page)
        self.assertIn("file.size>1048576", page)
        self.assertIn("source=JSON.parse(await file.text())", page)
        self.assertIn("JSON.stringify({project:source})", page)


if __name__ == "__main__":
    unittest.main()

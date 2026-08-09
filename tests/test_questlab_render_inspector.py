"""Guards for Quest Lab's read-only live renderer observability."""

from __future__ import annotations

import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "network" / "mod" / "ComfyQuestLab"
INSPECTOR = MOD / "Core" / "LabRenderInspector.cs"
PLUGIN = MOD / "ComfyQuestLab.cs"


class QuestLabRenderInspectorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.inspector = INSPECTOR.read_text(encoding="utf-8")
        cls.plugin = PLUGIN.read_text(encoding="utf-8")

    def test_command_exposes_exact_prefab_inspection(self) -> None:
        self.assertIn('string.Equals(arg, "inspect"', self.plugin)
        self.assertIn("LabRenderInspector.Inspect(args.Length >= 3 ? args[2] : null)", self.plugin)
        self.assertIn("questlab_prefabs inspect <exact-name>", self.plugin)

    def test_inspection_reads_shared_materials_without_instantiating_them(self) -> None:
        self.assertIn("Material[] materials = renderer.sharedMaterials;", self.inspector)
        self.assertNotIn("renderer.material;", self.inspector)
        self.assertNotIn("renderer.materials;", self.inspector)

    def test_startup_prefab_state_is_captured_before_world_instances(self) -> None:
        self.assertIn('AccessTools.Method(typeof(ZNetScene), "Awake")', self.inspector)
        self.assertIn("CaptureStartupBaselines();", self.inspector)
        self.assertIn("CaptureStartupBaselines(__instance);", self.inspector)
        self.assertIn("LabRenderInspectorPatches.Apply(_harmony);", self.plugin)
        for prefab in (
            "blackmarble_floor",
            "blackmarble_2x2x1",
            "blackmarble_tile_floor_2x2",
            "stone_floor_2x2",
        ):
            with self.subTest(prefab=prefab):
                self.assertIn(f'"{prefab}"', self.inspector)

    def test_material_report_includes_direct_illumination_evidence(self) -> None:
        for marker in (
            "shader.GetPropertyCount()",
            "material.shaderKeywords",
            "material.globalIlluminationFlags",
            "material.GetColor(id)",
            "material.GetTexture(id)",
            "renderer.HasPropertyBlock()",
            "block.HasProperty(candidate.Id)",
            "root.GetComponentsInChildren<Light>(true)",
            'value.Contains("emiss")',
            'value.Contains("glow")',
            'value.Contains("illum")',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.inspector)

    def test_live_instances_are_grouped_by_rendered_state_and_lab_mark(self) -> None:
        for marker in (
            "WearNTear.GetAllInstances()",
            "zdo.GetPrefab() != prefabHash",
            "LabGalleryBuilder.IsGalleryPiece(zdo)",
            "Dictionary<string, LiveGroup>",
            "same as current prefab",
            "LIVE STATE DIFFERS FROM PREFAB",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.inspector)

    def test_prefab_and_clone_paths_compare_relative_to_their_roots(self) -> None:
        self.assertIn('return ".";', self.inspector)
        self.assertIn('return "./" + string.Join("/", parts);', self.inspector)
        self.assertNotIn('return root.name + "/"', self.inspector)

    def test_artifact_is_bounded_to_an_exact_safe_prefab_name(self) -> None:
        self.assertIn("SafePrefabName(prefabName)", self.inspector)
        self.assertIn("value.Length > MaxNameLength", self.inspector)
        self.assertIn("char.IsLetterOrDigit(c) || c == '_' || c == '-'", self.inspector)
        self.assertIn('"comfy-questlab-render-inspection/v1"', self.inspector)


if __name__ == "__main__":
    unittest.main()

"""Guards for Quest Lab's read-only Gallery Truth Lens."""

from __future__ import annotations

import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "network" / "mod" / "ComfyQuestLab"
LENS = MOD / "Core" / "LabTruthLens.cs"
INSPECTOR = MOD / "Core" / "LabRenderInspector.cs"
PLUGIN = MOD / "ComfyQuestLab.cs"
CONTRACT = MOD / "Core" / "LabBatchContract.cs"
CONTROLLER = MOD / "Core" / "LabBatchController.cs"


class QuestLabTruthLensTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.lens = LENS.read_text(encoding="utf-8")
        cls.inspector = INSPECTOR.read_text(encoding="utf-8")
        cls.plugin = PLUGIN.read_text(encoding="utf-8")
        cls.contract = CONTRACT.read_text(encoding="utf-8")
        cls.controller = CONTROLLER.read_text(encoding="utf-8")

    def test_console_and_remote_surfaces_are_explicit(self) -> None:
        self.assertIn('verb == "evidence"', self.plugin)
        self.assertIn("LabTruthLens.Capture(value).Summary", self.plugin)
        self.assertIn('"gallery_evidence"', self.contract)
        self.assertIn('operation == "gallery_evidence"', self.controller)
        self.assertIn("request.selector, request.request_id", self.controller)

    def test_capture_is_read_only_and_fixed_directory(self) -> None:
        self.assertIn('"comfy-questlab-gallery-truth/v1"', self.lens)
        self.assertIn('Path.Combine("receipts", "truth")', self.lens)
        self.assertIn("No camera or world state changed", self.lens)
        self.assertNotIn("Object.Instantiate", self.lens)
        self.assertNotIn("Teleport", self.lens)
        self.assertNotIn("zdo.Set(", self.lens)
        self.assertNotIn("Destroy", self.lens)

    def test_named_views_are_deterministic_plans(self) -> None:
        for view in (
            "overview-north",
            "overview-east",
            "overhead",
            "arrival-eye",
            "roof-underside",
        ):
            with self.subTest(view=view):
                self.assertIn(f'Id = "{view}"', self.lens)
        self.assertIn("human visual acceptance is authoritative", self.lens)
        self.assertIn("visibleSnow", self.lens)
        self.assertIn("human-frame-required", self.lens)

    def test_objective_assertions_cover_the_live_failures(self) -> None:
        for assertion in (
            "loaded-world-bounds",
            "floor-weather-protection",
            "ceiling-fixture-clearance",
            "fresh-prefab-configuration",
            "named-view-plan",
        ):
            with self.subTest(assertion=assertion):
                self.assertIn(f'Id = "{assertion}"', self.lens)
        self.assertIn("WearNTear.RoofCheck", self.lens)
        self.assertIn("fixture.Clearance = roofUnderface - fixture.Bounds.max.y", self.lens)
        self.assertIn("TryWorldRenderBounds", self.lens)
        self.assertIn("TryWorldMeshBounds", self.lens)
        self.assertIn("not smoke/fire particle render bounds", self.lens)

    def test_render_comparison_ignores_activation_only_differences(self) -> None:
        self.assertIn("ConfigurationDigest", self.inspector)
        self.assertIn("Signature(state, false)", self.inspector)
        self.assertIn("ConfiguredEnabled", self.inspector)
        self.assertIn("SummarizeConfiguration", self.inspector)
        self.assertIn("SurfaceName", self.inspector)
        for marker in ('value.Contains("snow")', 'value.Contains("wet")', 'value.Contains("rain")'):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.inspector)

    def test_render_sampling_is_bounded_and_honest(self) -> None:
        self.assertIn("const int RenderSamplesPerGroup = 2", self.lens)
        self.assertIn("group.Samples >= RenderSamplesPerGroup", self.lens)
        self.assertIn("sampleLimit", self.lens)
        self.assertIn("Differences are diagnostic, not automatically defects", self.lens)


if __name__ == "__main__":
    unittest.main()

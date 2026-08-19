from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
RUNTIME = ROOT / "network" / "mod" / "ComfyQuestRuntime"
SIGHT = RUNTIME / "RuntimeArcaneSight.cs"
PLUGIN = RUNTIME / "ComfyQuestRuntime.cs"
ENGINE = RUNTIME / "RuntimeExperienceEngine.cs"
BINDING = RUNTIME / "RuntimeCharmBinding.cs"


class QuestRuntimeArcaneSightTests(unittest.TestCase):
    def test_runtime_scope_is_documented_from_the_shipping_code(self) -> None:
        binding = BINDING.read_text(encoding="utf-8")
        engine = ENGINE.read_text(encoding="utf-8")
        bindings = engine[
            engine.index("IReadOnlyList<WearNTear> Bindings"):
            engine.index("static RuntimeReceipt EventReceipt")
        ]
        self.assertIn("Physics.Raycast(cam.position,cam.forward,out var hit,10f)", binding)
        self.assertIn("WearNTear.GetAllInstances()", bindings)
        self.assertNotIn("Vector3.Distance", bindings)

    def test_runtime_preserves_the_active_set_activation_id(self) -> None:
        engine = ENGINE.read_text(encoding="utf-8")
        for marker in (
            '[JsonProperty("activation_id")] public string ActivationId { get; set; }',
            "ActivationId = set.ActivationId",
            "public string ActivationId;",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, engine)

    def test_arcane_sight_is_read_only_and_restores_visual_state(self) -> None:
        sight = SIGHT.read_text(encoding="utf-8")
        for marker in (
            "CharmPolicy.ValidateReference(reference)",
            "WearNTear.GetAllInstances()",
            "MaterialPropertyBlock",
            "WorldToScreenPoint",
            "view.IsOwner()",
            "LOCAL OWNER",
            "loaded scene, no fixed radius",
            "SetPropertyBlock(pair.Value)",
            "UnityEngine.Object.Destroy(lamp.gameObject)",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, sight)
        self.assertNotIn("zdo.Set(", sight)
        self.assertNotIn("SetOwner(", sight)
        self.assertNotIn("DestroyZDO(", sight)

    def test_f9_drawer_owns_the_arcane_sight_lifecycle(self) -> None:
        plugin = PLUGIN.read_text(encoding="utf-8")
        for marker in (
            "arcaneSight=new RuntimeArcaneSight(runtimeRoot)",
            "arcaneSight?.Tick()",
            "arcaneSight?.Draw(helpStyle)",
            "DrawArcaneSight()",
            "arcaneSight?.Enable()",
            "arcaneSight?.Disable()",
            '"ARCANE SIGHT - ON"',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, plugin)

    def test_runtime_docs_name_loaded_scene_scope(self) -> None:
        readme = (RUNTIME / "README.md").read_text(encoding="utf-8")
        self.assertIn("client-local **Arcane Sight**", readme)
        self.assertIn("no fixed", readme)
        self.assertIn("loaded `WearNTear` instance set", readme)


if __name__ == "__main__":
    unittest.main()

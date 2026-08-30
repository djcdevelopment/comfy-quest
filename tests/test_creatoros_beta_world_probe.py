import importlib.util
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
PROBE = ROOT / "tools" / "quest-studio" / "creatoros_beta_world_probe.py"


class CreatorOsBetaWorldProbeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        import sys
        sys.path.insert(0, str(PROBE.parent))
        spec = importlib.util.spec_from_file_location("creatoros_beta_world_probe", PROBE)
        assert spec and spec.loader
        cls.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.module)

    def test_exact_anchor_candidate_requires_one_nearby_sign(self):
        chosen = self.module.exact_anchor_candidate([
            {"binding_zdo": "12:34", "target_kind": "sign", "distance_metres": 7.0},
            {"binding_zdo": "56:78", "target_kind": "player_built_piece", "distance_metres": 2.0},
        ], "12:34")
        self.assertEqual("12:34", chosen["binding_zdo"])
        with self.assertRaisesRegex(RuntimeError, "missing_or_ambiguous"):
            self.module.exact_anchor_candidate([], "12:34")
        with self.assertRaisesRegex(RuntimeError, "missing_or_ambiguous"):
            self.module.exact_anchor_candidate([
                {"binding_zdo": "12:34", "target_kind": "sign", "distance_metres": 7.0},
                {"binding_zdo": "12:34", "target_kind": "sign", "distance_metres": 7.0},
            ], "12:34")
        with self.assertRaisesRegex(RuntimeError, "distance_invalid"):
            self.module.exact_anchor_candidate([
                {"binding_zdo": "12:34", "target_kind": "sign", "distance_metres": 25.1},
            ], "12:34")

    def test_probe_is_closed_over_the_fresh_beta_identity(self):
        source = PROBE.read_text(encoding="utf-8")
        for value in (
                "CreatorOSBeta1", "4257656027",
                "slayers-signature-hunt", "slayers-air-drop",
                "ad94f708efa9afbb8267cae6336226fc2c59c9d86c777d2309b932842f09c734",
                "signature_hunt_prepare", "bind_selected_experience",
                "placement_mode = \"ground\""):
            self.assertIn(value, source)
        self.assertNotIn("subprocess.run", source)
        self.assertNotIn("shell=True", source)


if __name__ == "__main__":
    unittest.main()

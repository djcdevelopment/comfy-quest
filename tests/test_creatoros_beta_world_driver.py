from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tools" / "release" / "New-CreatorOsBetaWorld.ps1"


class CreatorOsBetaWorldDriverTests(unittest.TestCase):
    def test_driver_is_identity_pinned_and_restores_am4(self):
        source = DRIVER.read_text(encoding="utf-8")
        for value in (
                "CreatorOSBeta1", "4257656027", "creatoros-beta1-world-artifact/v1",
                "creatoros_beta_world_probe.py", "placement_mode = 'ground'",
                "Game - OnApplicationQuit", "World saved", "am4-restoration.json",
                "SupportsShouldProcess", "Assert-RepoIdentity.ps1"):
            self.assertIn(value, source)
        self.assertIn("if ($pluginsDeployed)", source)
        self.assertIn("if ($prepared)", source)
        self.assertIn("AM4 beta world pair remained after restoration", source)
        self.assertNotIn("rm -rf", source)
        self.assertNotIn("git clean", source)


if __name__ == "__main__":
    unittest.main()

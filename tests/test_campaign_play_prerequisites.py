import pathlib
import re
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "quest-studio" / "Invoke-CampaignPlayPrerequisites.ps1"


class CampaignPlayPrerequisiteSurfaceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = SCRIPT.read_text(encoding="utf-8-sig")

    def test_public_surface_accepts_only_operation_and_install_identity(self) -> None:
        match = re.search(
            r"param\((.*?)\)\s*\$ErrorActionPreference",
            self.source,
            flags=re.DOTALL,
        )
        self.assertIsNotNone(match)
        parameter_block = match.group(1)
        self.assertEqual(
            re.findall(r"\[string\]\$(\w+)", parameter_block),
            ["OperationId", "ValheimRoot"],
        )
        for forbidden in (
            "WorldUid",
            "WorldName",
            "Character",
            "Prefab",
            "Binding",
            "Command",
            "OutputDirectory",
        ):
            self.assertNotIn("$" + forbidden, parameter_block)

    def test_choreography_revalidates_then_launches_arms_and_prepares_fixture(self) -> None:
        status = self.source.index("'Status', '-SessionId'")
        launch = self.source.index("'Launch', '-SessionId'")
        arm = self.source.index("'Arm', '-SessionId'")
        fixture = self.source.index("'signature_hunt_prepare'")
        self.assertLess(status, launch)
        self.assertLess(launch, arm)
        self.assertLess(arm, fixture)
        self.assertIn("tools\\Assert-RepoIdentity.ps1", self.source)

    def test_world_fixture_and_binding_are_fixed_machine_sources(self) -> None:
        self.assertIn("$worldUid -ne '-7600395338659582326'", self.source)
        self.assertIn("world_name -ne 'ComfyQuestDemo'", self.source)
        self.assertIn("character_profile -ne 'questyfour'", self.source)
        self.assertIn("comfy-questlab-signature-hunt-fixture/v1", self.source)
        self.assertIn("fixture.objects.expected -ne 20", self.source)
        self.assertIn("fixture.objects.standing_at_capture -ne 20", self.source)
        self.assertIn("fixture.targets[0].matcher_target -ne '$enemy_deathsquito'", self.source)
        self.assertIn("fixture.targets[1].matcher_target -ne '$enemy_drake'", self.source)
        self.assertIn("binding_anchor.role -ne 'marker-loadout-sign'", self.source)
        self.assertIn("binding_anchor.target_kind -ne 'sign'", self.source)
        self.assertIn("fixture_receipt_sha256", self.source)


if __name__ == "__main__":
    unittest.main()

from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "Quest.Studio.E2E.Tests" / "Quest.Studio.E2E.Tests.csproj"
TEST = ROOT / "src" / "Quest.Studio.E2E.Tests" / "QuestStudioSyntheticE2ETests.cs"
RUNNER = ROOT / "tools" / "quest-studio" / "Test-QuestStudioE2E.ps1"
README = ROOT / "README.md"
CI = ROOT / ".github" / "workflows" / "ci.yml"


class QuestStudioE2ETests(unittest.TestCase):
    def test_playwright_is_pinned_and_runtime_contracts_use_the_owning_source(self) -> None:
        root = ET.parse(PROJECT).getroot()
        packages = {
            item.attrib["Include"]: item.attrib["Version"]
            for item in root.findall(".//PackageReference")
        }
        self.assertEqual("1.61.0", packages["Microsoft.Playwright"])
        self.assertEqual("net9.0", root.findtext(".//TargetFramework"))
        projects = [item.attrib["Include"] for item in root.findall(".//ProjectReference")]
        self.assertEqual(
            [r"..\..\network\mod\ComfyQuestContracts\ComfyQuestContracts.csproj"],
            projects,
        )

    def test_runtime_fixture_is_sentinel_guarded_and_contract_native(self) -> None:
        source = TEST.read_text(encoding="utf-8")
        for expected in (
            '.quest-studio-synthetic-e2e',
            "synthetic_e2e_sentinel_missing",
            "synthetic_e2e_runtime_root_mismatch",
            "new QuestPackStore(run.RuntimeRoot).CheckInbox()",
            "new RuntimeReceiptStore(run.RuntimeRoot)",
            "LoadLatest()",
            "ReadExperience(candidate)",
            "RouteForEvent(eventName)",
        ):
            self.assertIn(expected, source)
        self.assertNotIn('start.Environment["COMFY_VALHEIM_DIR"] = @"', source)
        for hardcoded_stage in ('"beat-4"', '"beat-5"', '"beat-8"', '"advance-4"', '"advance-8"'):
            self.assertNotIn(hardcoded_stage, source)

    def test_tutorial_captures_ride_the_proving_journey_and_stay_opt_in(self) -> None:
        # The screenshot-led Woodbound tutorial is generated from the same synthetic
        # journey that proves the authoring path — never by asking a player to recreate
        # captures — and the lane is a no-op unless explicitly enabled.
        source = TEST.read_text(encoding="utf-8")
        self.assertIn('Environment.GetEnvironmentVariable("QUEST_STUDIO_TUTORIAL_SHOTS")', source)
        self.assertIn("if (string.IsNullOrWhiteSpace(TutorialShots)) return;", source)
        for shot in (
            "01-wake-the-charm",
            "02-browse-player-actions",
            "03-two-offerings",
            "04-seal-the-rite",
            "05-rehearse-the-rite",
            "06-live-proof",
        ):
            self.assertIn(f'"{shot}"', source)
        capture = (ROOT / "tools" / "quest-studio" / "Capture-WoodboundTutorial.ps1").read_text(
            encoding="utf-8"
        )
        self.assertIn("QUEST_STUDIO_TUTORIAL_SHOTS", capture)
        self.assertIn("Test-QuestStudioE2E.ps1", capture)
        tutorial = (ROOT / "docs" / "tutorials" / "the-woodbound-signal.md").read_text(
            encoding="utf-8"
        )
        for shot in ("01-wake-the-charm", "06-live-proof"):
            self.assertIn(f"woodbound/{shot}.png", tutorial)

    def test_browser_journey_covers_catalog_guidance_and_server_rehearsal(self) -> None:
        source = TEST.read_text(encoding="utf-8")
        for expected in (
            'page.Locator("#browse-events")',
            'page.Locator("#include-extended")',
            '"34 of 34"',
            'Name = "Run guided rehearsal"',
            'page.Locator("#library-toggle")',
            '"The Woodbound Signal"',
            'AddPickerBeatAsync(page, "item_dropped"',
            'AddPickerBeatAsync(page, "item_picked_up"',
            "ValidateWoodboundBrowserPack(run)",
        ):
            self.assertIn(expected, source)
        self.assertNotIn('page.Locator("#scenario").SelectOptionAsync', source)

    def test_data_downloads_are_browser_driven_and_contract_verified(self) -> None:
        source = TEST.read_text(encoding="utf-8")
        for expected in (
            'page.Locator("#tool-data").IsHiddenAsync()',
            'page.Locator("[data-tool=\'data\']")',
            "RunAndWaitForDownloadAsync",
            'page.Locator("#download-bundle")',
            'page.Locator("#download-questpack")',
            "comfy-quest-studio-export/v1",
            'archive.GetEntry("project/draft.json")',
            'archive.GetEntry("compiled/experience.json")',
            'archive.GetEntry("evidence/runtime-status.json")',
            "SHA256.HashData(bytes)",
            "new QuestPackStore(validationRoot).Inspect(candidatePath)",
            "candidate.IsValid",
            "SyntheticRuntimeFixture.AssertOwnedRoot",
        ):
            self.assertIn(expected, source)

    def test_usage_opt_out_keyboard_picker_and_narrow_smoke_are_covered(self) -> None:
        source = TEST.read_text(encoding="utf-8")
        for expected in (
            'page.Locator("#event-search").PressAsync("ArrowDown")',
            'page.Keyboard.PressAsync("ArrowDown")',
            'page.Locator("#event-search").FillAsync("stamina")',
            'page.Locator("#usage-enabled").UncheckAsync()',
            "aggregateBeforeOptedOutOperation",
            "Assert.Equal(aggregateBeforeOptedOutOperation, File.ReadAllBytes(usagePath))",
            "SetViewportSizeAsync(430, 780)",
            'page.Locator("#library-toggle").GetAttributeAsync("aria-expanded")',
        ):
            self.assertIn(expected, source)

    def test_local_runner_checks_identity_and_manages_playwright_chromium(self) -> None:
        source = RUNNER.read_text(encoding="utf-8")
        for expected in (
            "Assert-RepoIdentity.ps1",
            "Quest.Studio.E2E.Tests",
            "playwright.ps1",
            "install chromium",
            "Get-FileHash",
            "NUGET_PACKAGES",
            "COMFY_QUEST_E2E_DOTNET",
            "COMFY_QUEST_E2E_HOST_DLL",
            "COMFY_QUEST_E2E_KEEP_ARTIFACTS",
            "dotnetExe publish",
            "quest-studio-e2e\\host",
        ):
            self.assertIn(expected, source)

    def test_synthetic_e2e_stays_local_only_and_documented_as_non_live_proof(self) -> None:
        readme = README.read_text(encoding="utf-8")
        workflow = CI.read_text(encoding="utf-8")
        self.assertIn("Test-QuestStudioE2E.ps1", readme)
        self.assertIn("synthetic", readme.lower())
        self.assertIn("does not", readme.lower())
        self.assertNotIn("Quest.Studio.E2E.Tests", workflow)
        self.assertNotIn("Test-QuestStudioE2E.ps1", workflow)


if __name__ == "__main__":
    unittest.main()

"""Creator-package guards for ComfyQuestLab."""

from __future__ import annotations

import configparser
import re
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "network" / "mod" / "ComfyQuestLab"
CONFIG = MOD / "djcdevelopment.valheim.comfyquestlab.cfg"
PACKAGER = REPO / "tools" / "questlab-package" / "New-QuestLabZip.ps1"
CSPROJ = MOD / "ComfyQuestLab.csproj"


class QuestLabPackageTests(unittest.TestCase):
    def test_default_config_covers_every_bound_setting(self) -> None:
        parser = configparser.ConfigParser()
        parser.optionxform = str
        parser.read(CONFIG, encoding="utf-8")
        packaged = {
            (section, key)
            for section in parser.sections()
            for key in parser[section]
        }

        source = "\n".join(
            (MOD / name).read_text(encoding="utf-8")
            for name in ("ComfyQuestLab.cs", "Core/LabRuneLight.cs")
        )
        bound = set(
            re.findall(
                r'config\.Bind\(\s*"([^"]+)"\s*,\s*"([^"]+)"',
                source,
                flags=re.MULTILINE,
            )
        )
        self.assertEqual(packaged, bound)

    def test_reviewed_defaults_are_creator_safe(self) -> None:
        parser = configparser.ConfigParser()
        parser.optionxform = str
        parser.read(CONFIG, encoding="utf-8")
        self.assertEqual(parser["Lab"]["eventProfile"], "extended")
        self.assertEqual(parser["Lab"]["panelScale"], "1")
        self.assertEqual(parser["Lab"]["panelX"], "80")
        self.assertEqual(parser["Lab"]["panelY"], "90")
        self.assertEqual(parser["Lab"]["panelWidth"], "900")
        self.assertEqual(parser["Lab"]["panelHeight"], "620")
        self.assertEqual(parser["Lab"]["observeStamina"], "false")
        self.assertEqual(parser["Lab"]["galleryPiecesPerFrame"], "24")
        self.assertEqual(parser["Quests"]["questCooldownSeconds"], "60")

    def test_packager_copies_the_canonical_config(self) -> None:
        source = PACKAGER.read_text(encoding="utf-8")
        self.assertIn(
            "network\\mod\\ComfyQuestLab\\djcdevelopment.valheim.comfyquestlab.cfg",
            source,
        )
        self.assertNotIn('$config = "enabled = true', source)

    def test_packager_carries_the_allowlisted_local_event_companion(self) -> None:
        source = PACKAGER.read_text(encoding="utf-8")
        self.assertIn("$sheetsSource = Join-Path $root 'tools\\questlab-sheets'", source)
        for filename in (
            "questlab_sheets.py",
            "Start-QuestLabSheets.ps1",
            "requirements-google.txt",
            "README.md",
        ):
            with self.subTest(filename=filename):
                self.assertIn(filename, source)
        self.assertIn("explicit file allowlist", source)
        self.assertNotIn("Copy-Item $sheetsSource -Recurse", source)

        self.assertIn("$eventsSource = Join-Path $root 'tools\\questlab-events'", source)
        for filename in ("questlab_events.py", "README.md"):
            with self.subTest(parser_filename=filename):
                self.assertIn(filename, source)
        self.assertNotIn("Copy-Item $eventsSource -Recurse", source)

    def test_release_dll_does_not_change_with_the_containing_git_commit(self) -> None:
        # SourceLink puts the repository revision into the PDB and its checksum into the
        # PE debug directory. That makes a docs-only landing change the DLL hash even when
        # every compiled source byte is unchanged, defeating exact-package live receipts.
        project = CSPROJ.read_text(encoding="utf-8")
        self.assertIn("<Deterministic>true</Deterministic>", project)
        self.assertIn("<EnableSourceLink>false</EnableSourceLink>", project)

    def test_release_metadata_agrees(self) -> None:
        source = (MOD / "ComfyQuestLab.cs").read_text(encoding="utf-8")
        version = re.search(
            r'public const string PluginVersion = "([^"]+)";', source
        ).group(1)
        release_id = re.search(
            r'public const string ReleaseId = "([^"]+)";', source
        ).group(1)
        manifest = (MOD / "manifest.json").read_text(encoding="utf-8")
        self.assertIn(f'"version_number": "{version}"', manifest)
        self.assertIn(f"### {version} ", (MOD / "CHANGELOG.md").read_text(encoding="utf-8"))
        self.assertNotEqual(release_id, "dev")

        assembly_info = (MOD / "Properties/AssemblyInfo.cs").read_text(encoding="utf-8")
        self.assertIn("AssemblyVersion(ComfyQuestLab.ComfyQuestLab.PluginVersion)", assembly_info)
        self.assertIn('"QuestLabReleaseId"', assembly_info)

    def test_bundled_readme_names_both_install_destinations(self) -> None:
        readme = (MOD / "README.md").read_text(encoding="utf-8")
        self.assertIn("Valheim/BepInEx/plugins/", readme)
        self.assertIn("Valheim/BepInEx/config/", readme)

    def test_packager_rewrites_repository_links_for_extracted_readme(self) -> None:
        source = PACKAGER.read_text(encoding="utf-8")
        self.assertIn("$readmeLinks = [ordered]@{", source)
        self.assertIn("questlab-sheets/Start-QuestLabSheets.ps1", source)
        self.assertIn("questlab-sheets/README.md", source)
        self.assertIn("https://github.com/djcdevelopment/comfy-quest/blob/main/", source)
        self.assertIn("package README retains a repository-relative link", source)
        self.assertIn("Quest Lab package README target is missing", source)
        self.assertIn("Quest Lab package README has a broken local link", source)
        self.assertIn("network/mod/ComfyQuestLab/Patches/HarvestPatches.cs", source)
        self.assertNotIn("Copy-Item $readme -Destination $staging", source)


if __name__ == "__main__":
    unittest.main()

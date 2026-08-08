"""Creator-package guards for ComfyQuestLab."""

from __future__ import annotations

import configparser
import re
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "network" / "mod" / "ComfyQuestLab"
CONFIG = MOD / "djcdevelopment.valheim.comfyquestlab.cfg"
PACKAGER = REPO / "tools" / "workbench" / "New-WorkbenchZip.ps1"
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


if __name__ == "__main__":
    unittest.main()

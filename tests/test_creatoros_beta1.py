from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import unittest
import zipfile
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
ROOT = REPO / "creatoros" / "beta1"


class CreatorOsBeta1Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.campaign = json.loads((ROOT / "campaign.json").read_text(encoding="utf-8"))
        cls.venue = json.loads((ROOT / "venue.json").read_text(encoding="utf-8"))
        cls.view = json.loads((ROOT / "quest-view.json").read_text(encoding="utf-8"))

    def test_generated_content_is_current(self) -> None:
        subprocess.run(
            [sys.executable, str(REPO / "tools" / "creatoros" / "build_creatoros_beta1.py"), "--check"],
            cwd=REPO,
            check=True,
        )

    def test_field_lodge_is_a_published_immutable_venue(self) -> None:
        self.assertEqual("creatoros-guild-venue/v1", self.venue["schema"])
        self.assertEqual("slayers-field-lodge", self.venue["venue_id"])
        self.assertEqual("published", self.venue["state"])
        self.assertEqual("immutable", self.venue["lifecycle"]["release_policy"])
        self.assertEqual(40, self.venue["structure"]["piece_count"])
        self.assertEqual("marker-loadout-sign", self.venue["entry_anchor"]["role"])
        self.assertEqual(["slayers-signature-hunt"], self.venue["allowed_campaign_ids"])
        for field in ("capsule_sha256", "canonical_pieces_sha256", "blueprint_sha256"):
            self.assertRegex(self.venue["source"][field], r"^[0-9a-f]{64}$")

    def test_campaign_pack_has_exact_linear_lineage_and_message_only_actions(self) -> None:
        self.assertEqual("creatoros-campaign/v1", self.campaign["schema"])
        self.assertEqual("immutable-beta", self.campaign["release_state"])
        self.assertTrue(self.campaign["compatibility"]["post_1_0_revalidation_required"])
        package = ROOT / self.campaign["pack"]["path"]
        self.assertEqual(hashlib.sha256(package.read_bytes()).hexdigest(), self.campaign["pack"]["sha256"])
        with zipfile.ZipFile(package) as archive:
            self.assertEqual(
                {
                    "manifest.json",
                    "experiences/slayers-air-drop.json",
                    "experiences/slayers-cold-shot.json",
                },
                set(archive.namelist()),
            )
            manifest = json.loads(archive.read("manifest.json"))
            self.assertEqual(self.campaign["pack"]["content_hash"], manifest["content_hash"])
            documents = {
                json.loads(archive.read(name))["id"]: json.loads(archive.read(name))
                for name in archive.namelist()
                if name.startswith("experiences/")
            }
        self.assertEqual(
            ["slayers-cold-shot"], documents["slayers-air-drop"]["successor_experience_ids"]
        )
        self.assertEqual(
            ["slayers-air-drop"], documents["slayers-cold-shot"]["prerequisites"]
        )
        for document in documents.values():
            for stage in document["stages"]:
                self.assertTrue(all(action["type"] == "message" for action in stage["entry_actions"]))
                for transition in stage["transitions"]:
                    self.assertEqual("kill", transition["when"]["event"])
                    self.assertTrue(all(action["type"] == "message" for action in transition["actions"]))

    def test_networksense_projection_carries_exact_release_lineage(self) -> None:
        lineage = self.view["release_lineage"]
        self.assertEqual("creatoros-quest-view-lineage/v1", lineage["schema"])
        self.assertEqual(self.campaign["composition_hash"], lineage["composition_hash"])
        self.assertEqual(self.campaign["pack"]["content_hash"], lineage["pack_content_hash"])
        self.assertEqual(self.campaign["venue"]["sha256"], lineage["venue_sha256"])
        self.assertEqual(
            {"air_drop": "slayers-air-drop", "cold_shot": "slayers-cold-shot"},
            lineage["experience_ids"],
        )
        self.assertEqual(["air_drop", "cold_shot"], [quest["quest_id"] for quest in self.view["quests"]])

    def test_content_manifest_hashes_every_generated_payload(self) -> None:
        manifest = json.loads((ROOT / "content-manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(self.campaign["composition_hash"], manifest["composition_hash"])
        for record in manifest["generated"]:
            payload = (ROOT / record["path"]).read_bytes()
            self.assertEqual(len(payload), record["bytes"])
            self.assertEqual(hashlib.sha256(payload).hexdigest(), record["sha256"])


if __name__ == "__main__":
    unittest.main()

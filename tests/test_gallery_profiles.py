"""Drift guards for Quest Lab Gallery v2 profiles and generated artifacts."""

from __future__ import annotations

import json
import struct
import subprocess
import sys
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
TOOLS = REPO / "tools" / "component-packets"
SUMMARY = TOOLS / "samples" / "gallery-profiles.json"
GENERATOR = TOOLS / "generate_gallery.py"
PLAN = REPO / "network" / "mod" / "ComfyQuestLab" / "Core" / "LabGalleryPlan.g.cs"
BUILDER = REPO / "network" / "mod" / "ComfyQuestLab" / "Core" / "LabGalleryBuilder.cs"
CONTROLLER = REPO / "network" / "mod" / "ComfyQuestLab" / "Core" / "LabBatchController.cs"


def png_size(path: Path) -> tuple[int, int]:
    data = path.read_bytes()[:24]
    if len(data) != 24 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise AssertionError(f"not a PNG: {path}")
    return struct.unpack(">II", data[16:24])


class GalleryProfileTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.model = json.loads(SUMMARY.read_text(encoding="utf-8"))
        cls.profiles = {profile["id"]: profile for profile in cls.model["Profiles"]}

    def test_profile_contract_is_explicit(self) -> None:
        self.assertEqual(self.model["Schema"], "comfy-questlab-gallery-profiles/v2")
        self.assertEqual(self.model["DefaultProfile"], "marble-grand")
        self.assertEqual(self.model["ProfileCount"], 3)
        self.assertEqual(
            list(self.profiles), ["classic", "marble-wide", "marble-grand"]
        )

    def test_v2_profiles_are_solid_marble_and_material_closed(self) -> None:
        for profile_id in ("marble-wide", "marble-grand"):
            with self.subTest(profile=profile_id):
                profile = self.profiles[profile_id]
                self.assertTrue(profile["solidMarbleFloor"])
                self.assertEqual(profile["floorMaterials"], ["blackmarble_floor"])

    def test_scale_and_halls_grow_monotonically(self) -> None:
        classic = self.profiles["classic"]
        wide = self.profiles["marble-wide"]
        grand = self.profiles["marble-grand"]
        for field in ("ringRadius", "runeHeight", "hallWidth", "footprintRadius"):
            with self.subTest(field=field):
                self.assertLess(classic[field], wide[field])
                self.assertLess(wide[field], grand[field])
        self.assertGreaterEqual(wide["hallWidth"], classic["hallWidth"] * 2)
        self.assertLess(classic["platformClearance"], wide["platformClearance"])
        self.assertLess(wide["platformClearance"], grand["platformClearance"])
        self.assertGreaterEqual(grand["platformClearance"], 3.0)

    def test_v2_profiles_have_one_horizontal_header_per_rune(self) -> None:
        self.assertEqual(self.profiles["classic"]["counts"]["runeNameSigns"], 0)
        for profile_id in ("marble-wide", "marble-grand"):
            with self.subTest(profile=profile_id):
                self.assertEqual(
                    self.profiles[profile_id]["counts"]["runeNameSigns"], 8
                )

    def test_estimates_account_for_every_placed_object(self) -> None:
        # Build places one object for each generated floor/fixture/beam, two per armoury
        # entry (stand + loose item), eight stations, three entrance/arrival portals,
        # and five material supplies.
        for profile in self.profiles.values():
            counts = profile["counts"]
            expected = (
                counts["floorTiles"]
                + counts["fixtures"]
                + counts["runeBeams"]
                + counts["armouryStands"] * 2
                + 8
                + 3
                + 5
            )
            self.assertEqual(counts["estimatedPlacedObjects"], expected)

    def test_generated_plan_retains_profile_and_compatibility_contracts(self) -> None:
        source = PLAN.read_text(encoding="utf-8")
        self.assertIn("public const int PlanVersion = 2;", source)
        self.assertIn('public const string DefaultProfileId = "marble-grand";', source)
        self.assertIn("public float PlatformClearance;", source)
        self.assertIn("public int EstimatedPlacedObjects, RuneNameSigns;", source)
        self.assertEqual(source.count('Orient = "rune-name"'), 16)
        self.assertIn("public static Profile Find(string id)", source)
        self.assertIn("public static Monument[] Monuments", source)

    def test_runtime_reports_clearance_and_horizontal_headers(self) -> None:
        source = BUILDER.read_text(encoding="utf-8")
        self.assertIn('Append(" m terrain clearance, ")', source)
        self.assertIn('+ " horizontal rune headers."', source)

    def test_preview_set_is_complete(self) -> None:
        for profile_id in self.profiles:
            with self.subTest(profile=profile_id):
                self.assertEqual(
                    png_size(TOOLS / "samples" / f"gallery-plan-{profile_id}.png"),
                    (720, 720),
                )
        self.assertEqual(png_size(TOOLS / "samples" / "gallery-plan.png"), (720, 720))
        self.assertEqual(
            png_size(TOOLS / "samples" / "gallery-plan-comparison.png"),
            (1440, 480),
        )

    def test_committed_outputs_are_fresh(self) -> None:
        result = subprocess.run(
            [sys.executable, str(GENERATOR), "--check"],
            cwd=REPO,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_clear_claims_uninstantiated_marked_zdos_before_destroy(self) -> None:
        source = BUILDER.read_text(encoding="utf-8")
        helper = source[source.index("static bool DestroyMarkedZdo") :]
        claim = helper.index("zdo.SetOwner(ZDOMan.GetSessionID());")
        destroy = helper.index("ZDOMan.instance.DestroyZDO(zdo);")
        self.assertLess(claim, destroy)
        self.assertEqual(source.count("DestroyMarkedZdo(zdo)"), 2)

    def test_batch_prepare_refreshes_consumable_targets(self) -> None:
        builder = BUILDER.read_text(encoding="utf-8")
        controller = CONTROLLER.read_text(encoding="utf-8")
        self.assertIn("public string PrepareBatchTargets()", builder)
        self.assertIn("Restock(LabCategory.Combat)", builder)
        self.assertIn("Restock(LabCategory.Harvest)", builder)
        self.assertIn("_gallery.PrepareBatchTargets()", controller)

    def test_request_receipts_pin_release_and_do_not_leak_stale_suite_paths(self) -> None:
        controller = CONTROLLER.read_text(encoding="utf-8")
        self.assertIn('\\"plugin_version\\"', controller)
        self.assertIn('\\"release_id\\"', controller)
        self.assertIn("RequestExposesSuiteReceipt(request.operation)", controller)
        self.assertIn('operation == "reset"', controller)


if __name__ == "__main__":
    unittest.main()

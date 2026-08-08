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
PLUGIN = REPO / "network" / "mod" / "ComfyQuestLab" / "ComfyQuestLab.cs"


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

    def test_selected_profile_keeps_scale_but_quarters_the_walk(self) -> None:
        classic = self.profiles["classic"]
        wide = self.profiles["marble-wide"]
        grand = self.profiles["marble-grand"]
        for field in ("runeHeight", "hallWidth"):
            with self.subTest(field=field):
                self.assertLess(classic[field], wide[field])
                self.assertLess(wide[field], grand[field])
        self.assertGreaterEqual(wide["hallWidth"], classic["hallWidth"] * 2)
        self.assertEqual(grand["ringRadius"], 27.0)
        self.assertEqual(grand["spokeLength"], 9.0)
        self.assertLessEqual(grand["spokeLength"], 37.0 / 4.0)
        self.assertLess(grand["footprintRadius"], classic["footprintRadius"])
        self.assertLess(classic["platformClearance"], wide["platformClearance"])
        self.assertLess(wide["platformClearance"], grand["platformClearance"])
        self.assertGreaterEqual(grand["platformClearance"], 3.0)

    def test_v2_profiles_have_one_horizontal_header_per_rune(self) -> None:
        # Profile ids are not school names; pin the generated eight-school lettering
        # explicitly so adding a school or renaming one changes the physical count.
        expected_signs = sum(
            map(
                len,
                (
                    "combat",
                    "harvest",
                    "inventory",
                    "building",
                    "crafting",
                    "progression",
                    "world",
                    "social",
                ),
            )
        )
        for field in ("runeNameHeaders", "runeNameSigns", "runeNameLights"):
            self.assertEqual(self.profiles["classic"]["counts"][field], 0)
        for profile_id in ("marble-wide", "marble-grand"):
            with self.subTest(profile=profile_id):
                counts = self.profiles[profile_id]["counts"]
                self.assertEqual(counts["runeNameHeaders"], 8)
                self.assertEqual(counts["runeNameSigns"], expected_signs)
                self.assertEqual(counts["runeNameLights"], 8)

    def test_estimates_account_for_every_placed_object(self) -> None:
        # Build places one object for each generated floor/fixture/beam/course drop,
        # plus eight school stations and three entrance/arrival portals.
        for profile in self.profiles.values():
            counts = profile["counts"]
            expected = (
                counts["floorTiles"]
                + counts["fixtures"]
                + counts["runeBeams"]
                + counts["courseDrops"]
                + 11
            )
            self.assertEqual(counts["estimatedPlacedObjects"], expected)

    def test_generated_plan_retains_profile_and_compatibility_contracts(self) -> None:
        source = PLAN.read_text(encoding="utf-8")
        self.assertIn("public const int PlanVersion = 3;", source)
        self.assertIn('public const string DefaultProfileId = "marble-grand";', source)
        self.assertIn("public float PlatformClearance;", source)
        self.assertIn("HallWidth, SpokeLength, FootprintRadius", source)
        self.assertIn("public CourseDrop[] CourseDrops;", source)
        self.assertIn("RuneNameHeaders, RuneNameSigns, RuneNameLights;", source)
        self.assertEqual(source.count('Orient = "rune-name-lit"'), 16)
        self.assertEqual(source.count('Orient = "rune-name",'), 104)
        self.assertEqual(source.count('LightSchool = "combat"'), 2)
        self.assertIn("public static Profile Find(string id)", source)
        self.assertIn("public static Monument[] Monuments", source)

    def test_compact_course_stages_every_interaction_at_point_of_use(self) -> None:
        plan = PLAN.read_text(encoding="utf-8")
        builder = BUILDER.read_text(encoding="utf-8")
        for marker in (
            'Text = "sign here", X = 3.5f, Z = 6f',
            'Prefab = "AxeBronze", Note = "bronze axe beside the arrival birch", X = 6.2f',
            'Prefab = "Bow", Note = "bow on the player side of the combat spoke"',
            'Prefab = "ArrowWood", Note = "arrows beside the combat bow"',
            'Prefab = "Hammer", Note = "hammer in front of the building bench"',
            'Prefab = "Wood", Note = "wood beside the building hammer"',
            'Prefab = "Coal", Note = "coal directly in front of the smelter"',
            'Prefab = "CookedMeat"',
            'Prefab = "QueensJam"',
            'Prefab = "Honey"',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, plan)
        self.assertIn("foreach (LabGalleryPlan.CourseDrop item", builder)
        self.assertIn("SetStack(drop, item.Stack)", builder)

    def test_runtime_reports_clearance_and_horizontal_headers(self) -> None:
        source = BUILDER.read_text(encoding="utf-8")
        self.assertIn('Append(" m terrain clearance, ")', source)
        self.assertIn('Append(" m hub-to-station walks, ")', source)
        self.assertIn('+ " horizontal rune headers ("', source)
        self.assertIn("LabRuneLight.Mark(headerView.GetZDO(), fixture.LightSchool)", source)
        self.assertIn("LabRuneLight.Apply(built, fixture.LightSchool)", source)

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

    def test_clear_returns_to_verified_natural_terrain_before_deleting(self) -> None:
        source = BUILDER.read_text(encoding="utf-8")
        lifecycle = source[
            source.index("public IEnumerator ClearSafely") : source.index(
                "string ClearMarked"
            )
        ]
        self.assertIn(
            "TryTerrainRetreat(player, selector, out retreat, out retreatError)",
            lifecycle,
        )
        self.assertIn("player.TeleportTo(retreat, facing, false)", lifecycle)
        self.assertIn("while (player.IsTeleporting()", lifecycle)
        self.assertIn("ReachedTerrainRetreat(player, retreat)", lifecycle)
        self.assertIn("else if (!string.IsNullOrEmpty(retreatError))", lifecycle)
        self.assertLess(
            lifecycle.index("ReachedTerrainRetreat(player, retreat)"),
            lifecycle.index("ClearMarked(selector)"),
        )
        self.assertIn("while (StandingPieceCount(selector) > 0", lifecycle)
        self.assertLess(
            lifecycle.index("ClearMarked(selector)"),
            lifecycle.index("_lastLifecycleSucceeded = remaining == 0"),
        )
        self.assertIn("ZoneSystem.instance.GetGroundHeight(at, out height)", source)
        self.assertIn("finally {\n      _running = false;", lifecycle)

    def test_console_and_batch_clear_use_the_safe_coroutine(self) -> None:
        controller = CONTROLLER.read_text(encoding="utf-8")
        plugin = PLUGIN.read_text(encoding="utf-8")
        self.assertIn("_gallery.ClearSafely(request.selector)", controller)
        self.assertIn("_gallery.LastLifecycleSucceeded", controller)
        self.assertIn("StartCoroutine(_gallery.ClearSafely(value));", plugin)

    def test_setup_and_batch_prepare_reset_one_fresh_course(self) -> None:
        builder = BUILDER.read_text(encoding="utf-8")
        controller = CONTROLLER.read_text(encoding="utf-8")
        plugin = PLUGIN.read_text(encoding="utf-8")
        self.assertIn("public IEnumerator ResetSite", builder)
        self.assertIn('IEnumerator clear = ClearSafely("all")', builder)
        self.assertIn("_gallery.ResetSite(host, LabGalleryPlan.DefaultProfileId)", controller)
        self.assertIn("_gallery.ResetSite(this, LabGalleryPlan.DefaultProfileId)", plugin)
        self.assertNotIn("PrepareBatchTargets", controller)
        self.assertNotIn("PrepareBatchSupplies", controller)

    def test_request_receipts_pin_release_and_do_not_leak_stale_suite_paths(self) -> None:
        controller = CONTROLLER.read_text(encoding="utf-8")
        self.assertIn('\\"plugin_version\\"', controller)
        self.assertIn('\\"release_id\\"', controller)
        self.assertIn("RequestExposesSuiteReceipt(request.operation)", controller)
        self.assertIn('operation == "reset"', controller)


if __name__ == "__main__":
    unittest.main()

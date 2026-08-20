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
MOD = REPO / "network" / "mod" / "ComfyQuestLab"
SUMMARY = TOOLS / "samples" / "gallery-profiles.json"
GENERATOR = TOOLS / "generate_gallery.py"
PLAN = MOD / "Core" / "LabGalleryPlan.g.cs"
BUILDER = MOD / "Core" / "LabGalleryBuilder.cs"
TREE_RECOVERY = MOD / "Core" / "LabTreeRecovery.cs"
TREE_CONTRACT = MOD / "Core" / "LabTreeRecoveryContract.cs"
CONTROLLER = MOD / "Core" / "LabBatchController.cs"
PLUGIN = MOD / "ComfyQuestLab.cs"


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
        self.assertEqual(grand["platformClearance"], 6.0)
        self.assertTrue(grand["pruneNaturalTrees"])
        self.assertEqual(grand["roofClearance"], 16.0)
        self.assertEqual(grand["roofMaterials"], ["blackmarble_floor"])
        self.assertGreater(grand["counts"]["roofTiles"], 0)
        self.assertLess(grand["counts"]["roofTiles"], grand["counts"]["floorTiles"])
        self.assertEqual(grand["counts"]["ceilingFixtures"], 9)
        self.assertEqual(grand["ceilingFixtureHeights"], [16.0])
        self.assertEqual((grand["groundPortalX"], grand["groundPortalZ"]), (8.0, 0.0))

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

        generator = GENERATOR.read_text(encoding="utf-8")
        self.assertIn(
            'along = spec["plaza_radius"] + (inner_end - spec["plaza_radius"]) * 0.55',
            generator,
        )
        self.assertIn('y = spec["wall_courses"] * 2.0 + 0.75', generator)

    def test_estimates_account_for_every_placed_object(self) -> None:
        # Build places one object for each generated floor/fixture/beam/course drop and
        # ground-welcome fixture, plus eight school stations and three portals.
        for profile in self.profiles.values():
            counts = profile["counts"]
            expected = (
                counts["floorTiles"]
                + counts["roofTiles"]
                + counts["ceilingFixtures"]
                + counts["fixtures"]
                + counts["runeBeams"]
                + counts["courseDrops"]
                + counts["welcomeFixtures"]
                + 11
            )
            self.assertEqual(counts["estimatedPlacedObjects"], expected)

    def test_generated_plan_retains_profile_and_compatibility_contracts(self) -> None:
        source = PLAN.read_text(encoding="utf-8")
        self.assertIn("public const int PlanVersion = 9;", source)
        self.assertIn('public const string DefaultProfileId = "marble-grand";', source)
        self.assertIn(
            "public float PlatformClearance, RoofClearance, GroundPortalX, GroundPortalZ;",
            source,
        )
        self.assertIn("HallWidth, SpokeLength, FootprintRadius", source)
        self.assertIn("public CourseDrop[] CourseDrops;", source)
        self.assertIn("public WelcomeFixture[] WelcomeFixtures;", source)
        self.assertIn("public Tile[] RoofTiles;", source)
        self.assertIn("public CeilingFixture[] CeilingFixtures;", source)
        self.assertIn("public bool SolidMarbleFloor, PruneNaturalTrees;", source)
        self.assertIn("public bool AtGround;", source)
        self.assertIn("RuneNameHeaders, RuneNameSigns, RuneNameLights;", source)
        self.assertIn("Orient, Text, LightSchool, TextGlowSchool;", source)
        self.assertIn("public bool InfiniteFuel;", source)
        self.assertEqual(source.count('Orient = "rune-name-lit"'), 16)
        self.assertEqual(source.count('Orient = "rune-name",'), 104)
        self.assertEqual(source.count('LightSchool = "combat"'), 2)
        self.assertEqual(source.count('TextGlowSchool = "combat"'), 12)
        self.assertIn("public static Profile Find(string id)", source)
        self.assertIn("public static Monument[] Monuments", source)

    def test_compact_course_stages_every_interaction_at_point_of_use(self) -> None:
        plan = PLAN.read_text(encoding="utf-8")
        grand = plan[plan.index('Id = "marble-grand"') :]
        builder = BUILDER.read_text(encoding="utf-8")
        for marker in (
            'Text = "<size=30><b><color=#ffb2d9>CAST HERE</color></b></size>\\nFirst Portal tutorial\\n<color=#8fdc8f>open F9 · use the fixed center crosshair</color>", LightSchool = "social", X = 3.5f, Y = 1.7f, Z = 6f',
            'Prefab = "wood_pole2", X = 3.5f, Y = 0f, Z = 6f',
            'Prefab = "piece_groundtorch_blue", X = 2f, Y = 0f, Z = 6f, Yaw = 0f, Orient = "tutorial-beacon", Text = "", LightSchool = "social", InfiniteFuel = true',
            'Prefab = "piece_groundtorch_blue", X = 5f, Y = 0f, Z = 6f, Yaw = 0f, Orient = "tutorial-beacon", Text = "", LightSchool = "social", InfiniteFuel = true',
            'Prefab = "Birch1", Kind = "prop", Note = "ground Birch and bronze axe before the ascent portal"',
            'X = 5f, Y = 0f, Z = 2.5f, Yaw = 0f, AtGround = true',
            'Prefab = "AxeBronze", Note = "bronze axe beside the ground welcome Birch", X = 5.5f',
            'Stack = 1, AtGround = true',
            'Prefab = "Bow", Note = "bow on the player side of the combat spoke"',
            'Prefab = "ArrowWood", Note = "arrows beside the combat bow"',
            'Prefab = "Hammer", Note = "hammer in front of the building bench"',
            'Prefab = "Wood", Note = "wood beside the building hammer"',
            'Prefab = "Coal", Note = "coal directly in front of the smelter"',
            'Prefab = "piece_table", AttachedItem = "", Note = "welcome picnic table"',
            'Prefab = "itemstandh", AttachedItem = "CookedMeat"',
            'Prefab = "itemstandh", AttachedItem = "QueensJam"',
            'Prefab = "itemstandh", AttachedItem = "Bread|CarrotSoup|Sausages|TurnipStew"',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, grand)
        self.assertIn("foreach (LabGalleryPlan.CourseDrop item", builder)
        self.assertIn("foreach (LabGalleryPlan.WelcomeFixture fixture", builder)
        self.assertIn("AttachDisplayItem(built, fixture.AttachedItem)", builder)
        self.assertIn("SetStack(drop, item.Stack)", builder)
        self.assertEqual(self.profiles["marble-grand"]["counts"]["welcomeFixtures"], 6)
        self.assertEqual(self.profiles["marble-grand"]["counts"]["estimatedPlacedObjects"], 1914)

    def test_runtime_reports_clearance_and_horizontal_headers(self) -> None:
        source = BUILDER.read_text(encoding="utf-8")
        self.assertIn('Append(" m terrain clearance, ")', source)
        self.assertIn('profile.RoofTiles.Length + " roof tiles, "', source)
        self.assertIn('profile.CeilingFixtures.Length + " hanging fixtures, "', source)
        self.assertIn('Append(" m hub-to-station walks, ")', source)
        self.assertIn('+ " horizontal rune headers ("', source)
        self.assertIn('fixture.Orient == "rune-name-lit"', source)
        self.assertIn("LabRuneLight.BannerFaceStyle", source)
        self.assertIn("GlowSignText(built, fixture.TextGlowSchool)", source)
        self.assertIn(
            "LightPiece(station, monument.Station.LightSchool, LabRuneLight.SignFaceStyle)",
            source,
        )
        light = (MOD / "Core" / "LabRuneLight.cs").read_text(encoding="utf-8")
        self.assertIn('public const string SignFaceStyle = "sign-face";', light)
        self.assertIn('public const string BannerFaceStyle = "banner-face";', light)
        self.assertIn('public const string SignTextGlowMark = "comfyQuestLabSignTextGlow";', light)
        self.assertIn("material.EnableKeyword(ShaderUtilities.Keyword_Glow)", light)
        self.assertIn("colour.r * 2.2f", light)
        self.assertIn("? new Vector3(0f, 0f, -0.32f)", light)
        self.assertIn("if (signFace)", light)
        self.assertIn("ApplyTextGlow(host, school)", light)
        self.assertIn("UnityEngine.Object.Destroy(existing.gameObject)", light)
        self.assertLess(
            light.index("bool signFace"),
            light.index("lamp = new GameObject(LampChildName)"),
        )
        self.assertNotIn("Mathf.Min(configuredRange, 1.6f)", light)
        self.assertIn("Mathf.Min(configuredRange, 5.5f)", light)

    def test_roof_is_real_marked_geometry_with_durable_braziers(self) -> None:
        builder = BUILDER.read_text(encoding="utf-8")
        structure = (MOD / "Patches" / "GalleryStructurePatches.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("foreach (LabGalleryPlan.Tile tile in profile.RoofTiles)", builder)
        self.assertIn("floorY + profile.RoofClearance + BaseLift(tile.Prefab)", builder)
        self.assertIn('Quaternion.identity, "floor"', builder)
        self.assertIn('Quaternion.identity, "roof"', builder)
        self.assertIn('"ceiling-brazier"', builder)
        self.assertIn('GalleryRoleMark = "comfyQuestLabGalleryRole"', builder)
        self.assertIn("WearNTear.RoofCheck", builder)
        self.assertIn('AppendLine(" loaded marked floor slabs have a non-leaky piece above them.")', builder)
        self.assertIn(
            "foreach (LabGalleryPlan.CeilingFixture fixture in profile.CeilingFixtures)",
            builder,
        )
        self.assertIn("fixture.Y - topFromPivot - CeilingAttachmentClearance", builder)
        self.assertIn("fixtureMetrics.Center.y + fixtureMetrics.Size.y * 0.5f", builder)
        self.assertIn("vertical mesh offsets", builder)
        self.assertIn("live ceiling fixture check", builder)
        self.assertIn("TryWorldMeshBounds", builder)
        self.assertIn("fixture bodies are below the slab", builder)
        self.assertIn("GalleryStructurePatches.MarkAndLight", builder)
        self.assertIn('InfiniteBrazierMark = "comfyQuestLabInfiniteBrazier"', structure)
        self.assertIn("fireplace.m_infiniteFuel = true", structure)
        self.assertIn("typeof(Fireplace)", structure)

    def test_tree_pruning_is_write_ahead_bounded_and_recoverable(self) -> None:
        source = TREE_RECOVERY.read_text(encoding="utf-8")
        contract = TREE_CONTRACT.read_text(encoding="utf-8")
        builder = BUILDER.read_text(encoding="utf-8")
        plugin = PLUGIN.read_text(encoding="utf-8")
        write = source.index("WriteLedger(path, ledger); // Write before")
        read_back = source.index("LabTreeRecoveryLedger persisted = ReadLedger(path);")
        validate = source.index("RecordsAreComplete(persisted, out ledgerError)")
        destroy = source.index("view.Destroy();")
        self.assertLess(write, destroy)
        self.assertLess(write, read_back)
        self.assertLess(read_back, validate)
        self.assertLess(validate, destroy)
        self.assertIn("public sealed class LabTreeRecoveryRecord", contract)
        self.assertIn("public sealed class LabTreeRecoveryLedger", contract)
        self.assertIn(
            "public LabTreeRecoveryRecord[] Trees = new LabTreeRecoveryRecord[0];",
            contract,
        )
        self.assertNotIn("List<LabTreeRecoveryRecord> Trees", contract)
        self.assertIn("DataContractJsonSerializer", contract)
        self.assertIn('[DataMember(Name = "Trees", Order = 12)]', contract)
        self.assertIn("public int RecordCount;", contract)
        self.assertIn("public string RecordsSha256;", contract)
        self.assertIn("LabTreeRecoveryContract.Deserialize", source)
        self.assertIn("LabTreeRecoveryContract.Serialize", source)
        self.assertNotIn("JsonUtility", source)
        self.assertIn("ledger.RecordCount = ledger.Trees.Length", source)
        self.assertIn("ledger.RecordsSha256 = RecordsDigest(ledger.Trees)", source)
        self.assertIn("Quaternion.Euler(record.Rx, record.Ry, record.Rz)", source)
        self.assertIn("if (!RecordsAreComplete(ledger, out ledgerError))", source)
        self.assertIn("FindObjectsByType<TreeBase>", source)
        self.assertIn("InsideFootprint(profile, origin", source)
        self.assertIn("const float CanopyMargin = 12f", source)
        self.assertIn("prefab.GetComponent<TreeBase>() == null", source)
        self.assertIn("LabGalleryBuilder.IsGalleryPiece(zdo)", source)
        self.assertNotIn("Damage(", source)
        self.assertIn("AlreadyPresent(existing, record)", source)
        self.assertIn("string ledgerId = NextLedgerId(buildId)", source)
        self.assertIn("File.Replace(temporary, path, null)", source)
        self.assertIn("File.Move(temporary, path)", source)
        self.assertIn("LabTreeRecovery.Prune(profile, origin, _activeBuildId)", builder)
        self.assertIn("LabTreeRecovery.Restore(selector)", builder)
        self.assertIn('verb == "restore-trees"', plugin)
        self.assertIn("StandingPieceCount(selector) > 0", plugin)

    def test_mounted_welcome_food_is_real_and_does_not_litter_on_clear(self) -> None:
        source = BUILDER.read_text(encoding="utf-8")
        attach = source[source.index("void AttachDisplayItem") : source.index("void SetStack")]
        self.assertIn("zdo.Set(ZDOVars.s_item, prefabName)", attach)
        self.assertIn("ItemDrop.SaveToZDO(data, zdo)", attach)
        self.assertIn('view.InvokeRPC(ZNetView.Everybody, "SetVisualItem"', attach)
        self.assertIn("foreach (string itemName in candidates)", attach)
        self.assertIn("welcome table used visible fallback", attach)
        destroy = source[source.index("static bool DestroyMarkedZdo") : source.index("static bool MatchesSelector")]
        self.assertIn("SuppressMountedItemDrop(zdo, view)", destroy)
        self.assertIn("zdo.Set(ZDOVars.s_item, string.Empty)", destroy)

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
        self.assertIn('IEnumerator clear = ClearSafely("all", false)', builder)
        self.assertIn("_gallery.ResetSite(host, LabGalleryPlan.DefaultProfileId)", controller)
        self.assertIn("_gallery.ResetSite(this, LabGalleryPlan.DefaultProfileId)", plugin)
        self.assertNotIn("PrepareBatchTargets", controller)
        self.assertNotIn("PrepareBatchSupplies", controller)

    def test_rebuild_height_ignores_retiring_gallery_colliders(self) -> None:
        source = BUILDER.read_text(encoding="utf-8")
        build = source[source.index("public IEnumerator Build(") : source.index("// ---- clear")]
        self.assertIn("TryNaturalTerrainHeight(origin, out originTerrain)", build)
        self.assertIn("TryNaturalTerrainHeight(at, out ground)", build)
        self.assertIn("TryNaturalTerrainHeight(arrival, out arrivalGround)", build)
        self.assertIn("could not resolve natural terrain at the build", build)
        self.assertIn("could not resolve natural terrain for the", build)
        self.assertNotIn("TryGroundHeight(", build)
        self.assertIn("DestroyQuiescenceFrames = 2", source)
        self.assertIn("frame < DestroyQuiescenceFrames", source)

    def test_request_receipts_pin_release_and_do_not_leak_stale_suite_paths(self) -> None:
        controller = CONTROLLER.read_text(encoding="utf-8")
        self.assertIn('\\"plugin_version\\"', controller)
        self.assertIn('\\"release_id\\"', controller)
        self.assertIn("RequestExposesSuiteReceipt(request.operation)", controller)
        self.assertIn('operation == "reset"', controller)


if __name__ == "__main__":
    unittest.main()

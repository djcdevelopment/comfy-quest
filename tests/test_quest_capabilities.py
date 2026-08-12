"""Drift guards for the Quest Lab atlas-to-creator capability contract."""

from __future__ import annotations

import json
import importlib.util
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from collections import defaultdict
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
MANIFEST = (
    REPO
    / "tools"
    / "component-packets"
    / "samples"
    / "quest-capability-manifest.json"
)
GENERATOR = REPO / "tools" / "component-packets" / "generate_seam_catalog.py"
RULES = REPO / "tools" / "component-packets" / "quest-capability-rules.json"
AUTHORING = REPO / "tools" / "component-packets" / "quest-event-authoring.json"
PATCH_CHECKER = REPO / "tools" / "component-packets" / "check_lab_patches.py"
PATCHES = REPO / "network" / "mod" / "ComfyQuestLab" / "Patches"
JOURNAL_GENERATOR = REPO / "tools" / "component-packets" / "generate_journal.py"
TOME_GENERATOR = REPO / "tools" / "component-packets" / "render_quest_lab.py"
JOURNAL_SOURCE = REPO / "tools" / "component-packets" / "journal-pages.json"
JOURNAL_OUTPUT = REPO / "network" / "mod" / "ComfyQuestLab" / "Ui" / "LabJournal.g.cs"
TOME_OUTPUT = REPO / "docs" / "generated" / "questlab.html"

SPEC = importlib.util.spec_from_file_location("quest_capability_generator", GENERATOR)
CAPABILITY_GENERATOR = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(CAPABILITY_GENERATOR)


class QuestCapabilityManifestTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        cls.signatures = cls.manifest["Signatures"]
        cls.by_method = defaultdict(list)
        for signature in cls.signatures:
            cls.by_method[signature["MethodId"]].append(signature)

    def test_known_atlas_cardinality_is_explicit(self) -> None:
        self.assertEqual(
            self.manifest["Counts"],
            {
                "AtlasRows": 91,
                "UniqueSignatures": 90,
                "UniqueMethods": 77,
                "CanonicalEvents": 43,
                "CreatorSafeEvents": 34,
                "CreatorSafeSignatures": 57,
            },
        )
        self.assertEqual(sum(row["AtlasRowCount"] for row in self.signatures), 91)
        self.assertEqual(len({row["SignatureId"] for row in self.signatures}), 90)
        self.assertEqual(len(self.by_method), 77)

    def test_duplicate_player_death_row_is_preserved_as_provenance(self) -> None:
        death = next(
            row for row in self.signatures if row["SignatureId"] == "Player.OnDeath()"
        )
        self.assertEqual(death["AtlasRowCount"], 2)
        self.assertEqual(set(death["AtlasCategories"]), {"combat", "progression"})
        self.assertEqual(death["CanonicalCategory"], "progression")
        self.assertEqual(death["CanonicalEvent"], "player_died")

    def test_overloads_keep_exact_signature_identity(self) -> None:
        self.assertEqual(len(self.by_method["Inventory.AddItem"]), 7)
        self.assertEqual(len(self.by_method["Inventory.RemoveItem"]), 4)
        self.assertEqual(len(self.by_method["WorldGenerator.GetBiome"]), 2)
        self.assertEqual(len(self.by_method["ZoneSystem.SetGlobalKey"]), 3)
        self.assertIn(
            "Inventory.AddItem(ItemData, Vector2i)",
            {row["SignatureId"] for row in self.by_method["Inventory.AddItem"]},
        )

    def test_every_school_has_a_safe_creator_event(self) -> None:
        schools = {
            row["CanonicalCategory"] for row in self.signatures if row["CreatorSafe"]
        }
        self.assertEqual(
            schools,
            {
                "combat",
                "harvest",
                "inventory",
                "building",
                "crafting",
                "progression",
                "world",
                "social",
            },
        )

    def test_safe_events_have_stable_names_and_primary_routes(self) -> None:
        safe_routes = defaultdict(set)
        for row in self.signatures:
            self.assertRegex(row["CanonicalEvent"], re.compile(r"^[a-z][a-z0-9_]*$"))
            if row["CreatorSafe"]:
                safe_routes[row["CanonicalEvent"]].add(row["Route"])
        self.assertEqual(set(safe_routes), set(self.manifest["CreatorSafeEvents"]))
        self.assertFalse(
            {event for event, routes in safe_routes.items() if "primary" not in routes}
        )
        self.assertEqual(
            self.manifest["TriggerAliases"],
            {"hit": ["damage_dealt", "resource_damaged"]},
        )

    def test_every_safe_event_has_creator_target_and_field_metadata(self) -> None:
        creator_events = self.manifest["CreatorEvents"]
        self.assertEqual(
            [row["Name"] for row in creator_events],
            self.manifest["CreatorSafeEvents"],
        )
        for row in creator_events:
            with self.subTest(event=row["Name"]):
                self.assertTrue(row["TargetKind"])
                self.assertTrue(row["TargetDescription"])
                self.assertTrue(row["ExampleTarget"])
                names = [field["Name"] for field in row["Fields"]]
                self.assertEqual(len(names), len(set(names)))
                self.assertFalse(
                    set(names) & {"event", "target", "weapon_skill", "projectile"}
                )
                for field in row["Fields"]:
                    self.assertTrue(field["Description"])
                    self.assertTrue(field["Example"])
                    self.assertIsInstance(field["DraftByDefault"], bool)

    def test_known_local_rpc_routes_share_event_and_dedupe_group(self) -> None:
        pairs = (
            ("Character.Damage", "Character.RPC_Damage"),
            ("Character.Heal", "Character.RPC_Heal"),
            ("Character.Stagger", "Character.RPC_Stagger"),
            ("Destructible.Damage", "Destructible.RPC_Damage"),
            ("MineRock5.Damage", "MineRock5.RPC_Damage"),
            ("Smelter.OnAddFuel", "Smelter.RPC_AddFuel"),
            ("Smelter.OnAddOre", "Smelter.RPC_AddOre"),
            ("TreeBase.Damage", "TreeBase.RPC_Damage"),
            ("TreeLog.Damage", "TreeLog.RPC_Damage"),
            ("ZoneSystem.SetGlobalKey", "ZoneSystem.RPC_SetGlobalKey"),
        )
        for local_method, rpc_method in pairs:
            with self.subTest(local=local_method, rpc=rpc_method):
                local = self.by_method[local_method][0]
                rpc = self.by_method[rpc_method][0]
                self.assertEqual(local["CanonicalEvent"], rpc["CanonicalEvent"])
                self.assertEqual(local["DedupeGroup"], rpc["DedupeGroup"])

    def test_committed_outputs_are_fresh(self) -> None:
        result = subprocess.run(
            [sys.executable, str(GENERATOR), "--check"],
            cwd=REPO,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_in_game_and_web_tomes_are_fresh_and_do_not_repeat_scaffold_claims(self) -> None:
        for generator in (JOURNAL_GENERATOR, TOME_GENERATOR):
            with self.subTest(generator=generator.name):
                result = subprocess.run(
                    [sys.executable, str(generator), "--check"],
                    cwd=REPO,
                    capture_output=True,
                    text=True,
                    check=False,
                )
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

        forbidden = (
            "The only category with any ground a quest can stand on today",
            "nothing today will run it",
            "the combat seams are the ones that work",
        )
        for path in (JOURNAL_SOURCE, JOURNAL_OUTPUT, TOME_OUTPUT):
            text = path.read_text(encoding="utf-8")
            for claim in forbidden:
                with self.subTest(path=path.name, claim=claim):
                    self.assertNotIn(claim, text)

    def test_every_creator_safe_signature_has_a_runtime_patch(self) -> None:
        result = subprocess.run(
            [sys.executable, str(PATCH_CHECKER)],
            cwd=REPO,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("creator-safe runtime coverage 57/57", result.stdout)
        self.assertIn("practical atlas runtime coverage 86/86", result.stdout)

    def test_durable_archive_is_owned_only_by_the_catalog_router(self) -> None:
        mod = REPO / "network" / "mod" / "ComfyQuestLab"
        runtime_callers = []
        for path in mod.rglob("*.cs"):
            source = path.read_text(encoding="utf-8")
            if "ComfyQuestLab.ObserveRuntimeEvent(" in source:
                runtime_callers.append(path.relative_to(mod).as_posix())
        self.assertEqual(runtime_callers, ["Core/LabEventRouter.cs"])

        plugin = (mod / "ComfyQuestLab.cs").read_text(encoding="utf-8")
        self.assertIn("public static void ObserveRuntimeEvent", plugin)
        self.assertIn("if (archive && self._eventArchive != null)", plugin)
        self.assertEqual(plugin.count("_eventArchive.TryRecord(row, actionIdentity)"), 1)

        router = (mod / "Core" / "LabEventRouter.cs").read_text(encoding="utf-8")
        self.assertEqual(router.count("ComfyQuestLab.ObserveRuntimeEvent("), 2)
        self.assertNotIn("quest.reloaded", router)
        self.assertNotIn("quest.fired", router)

        archive = (mod / "Core" / "LabEventArchive.cs").read_text(encoding="utf-8")
        csv_retry = archive.index("if (!TryDelete(csv)) continue;")
        json_delete = archive.index("TryDelete(candidate);", csv_retry)
        self.assertLess(csv_retry, json_delete)

    def test_missing_creator_patch_turns_the_guard_red(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            copied = Path(temporary) / "Patches"
            shutil.copytree(PATCHES, copied)
            combat = copied / "CombatPatches.cs"
            source = combat.read_text(encoding="utf-8")
            source = source.replace(
                'LabPatching.TryPatch(harmony, typeof(Character), "Damage"',
                'LabPatching.SkipPatch(harmony, typeof(Character), "Damage"',
                1,
            )
            combat.write_text(source, encoding="utf-8")
            result = subprocess.run(
                [sys.executable, str(PATCH_CHECKER), "--patches", str(copied)],
                cwd=REPO,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(
                "creator-safe signature has no runtime patch: Character.Damage(HitData)",
                result.stdout,
            )

    def test_missing_policy_turns_the_guard_red(self) -> None:
        rules = json.loads(RULES.read_text(encoding="utf-8"))
        grouped_rule = next(
            rule
            for rule in rules["Rules"]
            if "Chat.OnNewChatMessage" in rule["Methods"]
        )
        grouped_rule["Methods"].remove("Chat.OnNewChatMessage")
        with tempfile.TemporaryDirectory() as temporary:
            mutated_rules = Path(temporary) / "quest-capability-rules.json"
            mutated_rules.write_text(json.dumps(rules), encoding="utf-8")
            original_rules = CAPABILITY_GENERATOR.RULES
            try:
                CAPABILITY_GENERATOR.RULES = mutated_rules
                with self.assertRaisesRegex(
                    CAPABILITY_GENERATOR.CapabilityError,
                    "capability policy drift.*Chat.OnNewChatMessage",
                ):
                    CAPABILITY_GENERATOR.build_model()
            finally:
                CAPABILITY_GENERATOR.RULES = original_rules

    def test_missing_creator_metadata_turns_the_guard_red(self) -> None:
        authoring = json.loads(AUTHORING.read_text(encoding="utf-8"))
        authoring["Events"] = [
            row for row in authoring["Events"] if row["Name"] != "sign_written"
        ]
        with tempfile.TemporaryDirectory() as temporary:
            mutated = Path(temporary) / "quest-event-authoring.json"
            mutated.write_text(json.dumps(authoring), encoding="utf-8")
            original = CAPABILITY_GENERATOR.AUTHORING
            try:
                CAPABILITY_GENERATOR.AUTHORING = mutated
                atlas, rules, _, signatures = CAPABILITY_GENERATOR.build_model()
                with self.assertRaisesRegex(
                    CAPABILITY_GENERATOR.CapabilityError,
                    "creator authoring drift.*sign_written",
                ):
                    CAPABILITY_GENERATOR.build_manifest(atlas, rules, signatures)
            finally:
                CAPABILITY_GENERATOR.AUTHORING = original


if __name__ == "__main__":
    unittest.main()

"""Drift guards for the Quest Lab atlas-to-creator capability contract."""

from __future__ import annotations

import json
import importlib.util
import re
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


if __name__ == "__main__":
    unittest.main()

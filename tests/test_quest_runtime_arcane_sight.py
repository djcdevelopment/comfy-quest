from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
RUNTIME = ROOT / "network" / "mod" / "ComfyQuestRuntime"
SIGHT = RUNTIME / "RuntimeArcaneSight.cs"
PLUGIN = RUNTIME / "ComfyQuestRuntime.cs"
ENGINE = RUNTIME / "RuntimeExperienceEngine.cs"
BINDING = RUNTIME / "RuntimeCharmBinding.cs"
DEV_CHANNEL = ROOT / "network" / "mod" / "ComfyQuestContracts" / "RuntimeDevChannel.cs"


class QuestRuntimeArcaneSightTests(unittest.TestCase):
    def test_runtime_scope_is_documented_from_the_shipping_code(self) -> None:
        binding = BINDING.read_text(encoding="utf-8")
        engine = ENGINE.read_text(encoding="utf-8")
        bindings = engine[
            engine.index("IReadOnlyList<WearNTear> Bindings"):
            engine.index("static RuntimeReceipt EventReceipt")
        ]
        self.assertIn("Physics.Raycast(cam.position,cam.forward,out var hit,10f)", binding)
        self.assertIn("WearNTear.GetAllInstances()", bindings)
        self.assertNotIn("Vector3.Distance", bindings)

    def test_runtime_preserves_the_active_set_activation_id(self) -> None:
        engine = ENGINE.read_text(encoding="utf-8")
        for marker in (
            "ActivationId = set.ActivationId",
            "public string ActivationId;",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, engine)

    def test_runtime_evidence_is_correlated_bounded_and_replay_safe(self) -> None:
        engine = ENGINE.read_text(encoding="utf-8")
        for marker in (
            "const int MaxRecentEvidence = 8;",
            '"evt-" + Guid.NewGuid().ToString("N").Substring(0, 12)',
            "CorrelationId = correlationId",
            "TriggerEvaluator.Explain(decision.Transition.When, currentState?.History, decision.EvaluationContext)",
            "var evidence = decision.IsPendingReplay",
            "Evidence = evidence",
            "RejectedEvidence = rejectedEvidence",
            "StageEnteredUtc = currentState?.StageEnteredUtc",
            'return " - in stage "',
            "public IReadOnlyList<string> RecentEvidence()",
            "recentEvidence.RemoveRange(0, recentEvidence.Count - MaxRecentEvidence)",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, engine)
        self.assertLess(engine.index("workflows.Begin"), engine.index("TriggerEvaluator.Explain"))
        self.assertLess(engine.index("TriggerEvaluator.Explain"), engine.index("workflows.Complete"))

    def test_activation_change_reports_one_bounded_orphan_scan(self) -> None:
        engine = ENGINE.read_text(encoding="utf-8")
        orphan = engine[
            engine.index("void ReportOrphanedBindings"):
            engine.index("WorldAuthority World")
        ]
        self.assertEqual(1, orphan.count("WearNTear.GetAllInstances()"))
        self.assertIn(
            "!string.Equals(previousContentHash, cachedActive.ContentHash",
            engine,
        )
        for marker in (
            'Operation = "activation"',
            'Status = "orphaned_bindings"',
            "CandidateCount = count",
            'bindings now OTHER VERSION — re-CAST or roll back',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, orphan)

    def test_runtime_readers_bind_the_contract_active_set_without_shadow_copies(self) -> None:
        # active-set.json has one schema owner: ComfyQuestContracts.ActiveSet. A private
        # nested copy would silently fork the schema the moment a field is added to one
        # reader and not the others (that fork already happened once, for activation_id).
        for path in (ENGINE, BINDING, SIGHT, PLUGIN):
            with self.subTest(reader=path.name):
                text = path.read_text(encoding="utf-8")
                self.assertNotIn("sealed class ActiveSet", text)
                self.assertIn("using ComfyQuestContracts;", text)

    def test_arcane_sight_is_read_only_and_restores_visual_state(self) -> None:
        sight = SIGHT.read_text(encoding="utf-8")
        for marker in (
            "CharmPolicy.ValidateReference(reference)",
            "WearNTear.GetAllInstances()",
            "MaterialPropertyBlock",
            "WorldToScreenPoint",
            "view.IsOwner()",
            "LOCAL OWNER",
            "loaded scene, no fixed radius",
            "SetPropertyBlock(pair.Value)",
            "UnityEngine.Object.Destroy(lamp.gameObject)",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, sight)
        self.assertNotIn("zdo.Set(", sight)
        self.assertNotIn("SetOwner(", sight)
        self.assertNotIn("DestroyZDO(", sight)

    def test_f9_drawer_owns_the_arcane_sight_lifecycle(self) -> None:
        plugin = PLUGIN.read_text(encoding="utf-8")
        for marker in (
            "arcaneSight=new RuntimeArcaneSight(runtimeRoot)",
            "arcaneSight?.Tick()",
            "arcaneSight?.Draw(helpStyle)",
            "DrawArcaneSight()",
            "arcaneSight?.Enable()",
            "arcaneSight?.Disable()",
            '"ARCANE SIGHT - ON"',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, plugin)

    def test_f9_drawer_surfaces_recent_runtime_evidence(self) -> None:
        plugin = PLUGIN.read_text(encoding="utf-8")
        for marker in (
            "DrawRecentEvidence();",
            '"RECENT RUNTIME EVIDENCE"',
            "engine?.RecentEvidence()",
            '"No gameplay evidence yet."',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, plugin)

    def test_creator_loop_is_session_armed_and_game_owned(self) -> None:
        plugin = PLUGIN.read_text(encoding="utf-8")
        channel = DEV_CHANNEL.read_text(encoding="utf-8")
        for marker in (
            "RuntimeDevChannelCoordinator",
            '"ARM DEV CHANNEL"',
            "privateWorldConfirmed.Value",
            "devChannel.Poll",
            "devChannel?.Disarm",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, plugin)
        self.assertIn("only an armed", channel)
        self.assertIn('Path.Combine(runtimeRoot,"inbox-dev")', channel)
        self.assertIn('Stage("dev_activation"', channel)

    def test_dev_rebind_updates_only_the_existing_five_string_reference(self) -> None:
        binding = BINDING.read_text(encoding="utf-8")
        written = set(__import__("re").findall(r'zdo\.Set\(Prefix\+"([^"]+)"', binding))
        self.assertEqual(
            {"packId", "experienceId", "bindingId", "version", "contentHash"},
            written,
        )
        self.assertIn("RebindDevActive", binding)
        self.assertIn('set.SourceChannel,"dev"', binding)
        self.assertIn('dev?"inbox-dev":"inbox"', binding)

    def test_arcane_sight_labels_activation_epoch_and_binding(self) -> None:
        sight = SIGHT.read_text(encoding="utf-8")
        for marker in (
            "ShortActivationId(activeSet?.ActivationId)",
            "ShortBindingZdo(zdo.m_uid.ToString())",
            "marker.Current && !string.IsNullOrWhiteSpace(marker.ActivationId)",
            "ZDO {marker.BindingZdo}",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, sight)

    def test_runtime_docs_name_loaded_scene_scope(self) -> None:
        readme = (RUNTIME / "README.md").read_text(encoding="utf-8")
        self.assertIn("client-local **Arcane Sight**", readme)
        self.assertIn("no fixed", readme)
        self.assertIn("loaded `WearNTear` instance set", readme)
        # "No fixed radius" describes binding discovery; authored event predicates may still
        # evaluate spatial relationships through the single observation seam.
        self.assertIn('"No fixed radius" describes binding discovery only', readme)
        self.assertIn("authored event predicates may evaluate", readme)
        self.assertIn("without ever filtering\nwhich bindings participate", readme)

    def test_observation_is_one_seam_and_binding_discovery_keeps_no_radius(self) -> None:
        observation = (RUNTIME / "RuntimeObservation.cs").read_text(encoding="utf-8")
        binding = BINDING.read_text(encoding="utf-8")
        engine = ENGINE.read_text(encoding="utf-8")
        router = (RUNTIME / "RuntimeEventRouter.cs").read_text(encoding="utf-8")
        for marker in (
            "public static void StampLocalPlayer(RuntimeEvent runtimeEvent)",
            "player.transform.position",
            "binding.GetPosition()",
            "facts.Spatial.SpawnedPositions = positions;",
            # Encounter tallies are resolved in the same single pass, never elsewhere.
            "facts.Encounter = new EncounterFacts { SpawnsByAction = tallies };",
            "spawned.TryForOwner(ownerKey, out var records)",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, observation)
        # The retired name must not linger anywhere in the shipping mod.
        for name, text in (("engine", engine), ("router", router), ("binding", binding)):
            with self.subTest(stale=name):
                self.assertNotIn("RuntimeSpatialObservation", text)
        self.assertFalse((RUNTIME / "RuntimeSpatialObservation.cs").exists())
        # Every routed witness and engine timer event carries the local witness position.
        self.assertEqual(2, router.count("RuntimeObservation.StampLocalPlayer("))
        self.assertIn("RuntimeObservation.StampLocalPlayer(elapsed)", engine)
        self.assertIn("RuntimeObservation.Facts(", engine)
        # Distance math lives only in the pure Contracts SpatialEvaluator and tally meaning only
        # in AdaptiveEvaluator: the engine and the Charm binding surface resolve neither, and the
        # binding surface never learns about positions or spawned objects at all.
        self.assertNotIn("Vector3.Distance", observation)
        self.assertNotIn("Vector3.Distance", engine)
        # The engine still destroys objects for clear_spawned, but it never derives a fact tally.
        self.assertNotIn("SpawnTally", engine)
        self.assertNotIn("EncounterFacts {", engine)
        for marker in ("RuntimeObservation", "PosX", "GetPosition", "SpawnedObject"):
            with self.subTest(binding_marker=marker):
                self.assertNotIn(marker, binding)


if __name__ == "__main__":
    unittest.main()

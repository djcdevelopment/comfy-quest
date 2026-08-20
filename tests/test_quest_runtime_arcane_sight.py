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
            # The summary names the intersection it counts; a bare "locally owned" read as
            # contradicting the OTHER VERSION / LOCAL OWNER labels during the live lap.
            "active + locally owned",
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
            "engine?.RecentEvidenceLines()",
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

    def test_a_running_deadline_is_visible_without_opening_the_creator_drawer(self) -> None:
        plugin = PLUGIN.read_text(encoding="utf-8")
        engine = ENGINE.read_text(encoding="utf-8")
        # The banner draws before the drawer's early return, so a deadline is player-visible.
        self.assertIn("void OnGUI(){EnsureStyles();DrawDeadline();if(!drawerVisible)return;", plugin)
        # Urgency is a fact beside the line, never parsed back out of the rendered copy —
        # a wording change must not be able to kill the red state.
        self.assertIn(
            "UnityEngine.GUI.Label(rect,line,engine.DeadlineUrgent()?deadlineUrgentStyle:deadlineStyle)",
            plugin,
        )
        self.assertNotIn('" second"', plugin)
        self.assertIn("public bool DeadlineUrgent() {", engine)
        self.assertIn("engine?.Deadline()", plugin)
        # One separator convention with TriggerDeadline.Label: "1/2, 6 seconds remaining".
        self.assertIn('(progress == null ? "" : progress + ", ")', engine)
        # Every pinned texture is still released.
        for texture in ("deadlineBackground", "deadlineUrgentBackground"):
            with self.subTest(texture=texture):
                self.assertIn(f"Destroy(ref {texture});", plugin)
                self.assertIn(f'{texture}=Solid("runtime-deadline', plugin)
        # The countdown is cached once a second, never recomputed per frame.
        self.assertIn("RefreshDeadline(now);", engine)
        self.assertIn("public string Deadline() {", engine)
        self.assertIn("TriggerCountdown.Read(transition.When, progress.History, now)", engine)
        self.assertIn("timers.Pending(now, 1)", engine)
        # The existing stage-elapsed line keeps its exact shape; remaining time is appended.
        self.assertIn('return " - in stage "', engine)
        self.assertIn('+ (string.IsNullOrWhiteSpace(running) ? "" : " - " + running)', engine)

    def test_rejected_branches_explain_themselves_in_runtime_evidence(self) -> None:
        engine = ENGINE.read_text(encoding="utf-8")
        self.assertIn("MatchedLine(currentStage.Id, decision.Transition, matchedProgress, rejectedEvidence)", engine)
        self.assertIn("static string RejectedLine(", engine)
        self.assertIn('" Not " + string.Join("; not ", reasons)', engine)
        self.assertIn("static string Unmet(TriggerClauseTrace trace)", engine)

    def test_creator_loop_copy_is_a_contract_fact_on_the_plumbing_channel(self) -> None:
        plugin = PLUGIN.read_text(encoding="utf-8")
        engine = ENGINE.read_text(encoding="utf-8")
        contract = (
            ROOT / "network" / "mod" / "ComfyQuestContracts" / "RuntimeContract.cs"
        ).read_text(encoding="utf-8")
        notice = (
            ROOT / "network" / "mod" / "ComfyQuestContracts" / "CreatorLoopNotice.cs"
        ).read_text(encoding="utf-8")
        # Creator plumbing speaks TopLeft; Center belongs to the authored story and the
        # countdown. The engine keeps exactly one Center writer: the message action.
        self.assertIn("MessageHud.MessageType.TopLeft", plugin)
        self.assertNotIn("MessageType.Center", plugin)
        self.assertEqual(1, engine.count("MessageHud.MessageType.Center"))
        # The plugin renders CreatorLoopNotice; it composes no creator copy inline.
        for marker in (
            "CreatorLoopNotice.Check(",
            "CreatorLoopNotice.CheckFailed(",
            "CreatorLoopNotice.NothingToLoad(",
            "CreatorLoopNotice.AlreadyPlaying(",
            "CreatorLoopNotice.Loaded(",
            "CreatorLoopNotice.LoadFailed(",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, plugin)
        self.assertNotIn("pack(s)", plugin)
        self.assertNotIn('"Activated {', plugin)
        self.assertNotIn('"Load failed: "', plugin)
        # The check path can no longer die silently, and an idle repeat load neither
        # activates a fresh epoch nor goes unreceipted.
        self.assertIn('Operation="check",Status="rejected"', plugin)
        self.assertIn('Status="already_active"', plugin)
        self.assertIn("OrphanedBindingsAfterActivation()", plugin)
        self.assertIn("public int OrphanedBindingsAfterActivation() {", engine)
        self.assertIn(
            "StringComparison.OrdinalIgnoreCase))return valid[0];return ActivateCandidate(valid[0]);",
            contract,
        )
        # F11 refreshes the same inbox state F10 populates, so the ladder is factual.
        self.assertIn("public string LoadLatest(){try{RefreshInbox();", plugin)
        # Every hotkey respects game text focus, mirroring the Lab guard without
        # referencing its assembly.
        self.assertIn("static bool TypingInGame()", plugin)
        self.assertLess(
            plugin.index("if(TypingInGame())return;"),
            plugin.index("checkHotkey.Value.IsDown()"),
        )
        # Discoverability: one session-scoped hint, and the drawer buttons teach their keys.
        self.assertIn('"Comfy Quest ready. Press "+drawerHotkey.Value', plugin)
        self.assertIn('"CHECK FOR UPDATES · "+checkHotkey.Value', plugin)
        self.assertIn('"LOAD VALIDATED UPDATE · "+loadHotkey.Value', plugin)
        # The notice type carries both altitudes; the xUnit matrix proves the headline
        # never leaks identity, and Detail is where identity is allowed to live.
        self.assertIn("public string Headline", notice)
        self.assertIn("public string Detail", notice)
        # Titles ride the existing compile pass; no second file read, no manifest change.
        self.assertIn("titles.Add(compiled.Document.Title)", contract)
        self.assertNotIn('"title"', contract)
        cfg = (RUNTIME / "LiveTest" / "djcdevelopment.valheim.comfyquestruntime.cfg").read_text(
            encoding="utf-8"
        )
        self.assertIn("CharmGestureHotkey = BackQuote", cfg)

    def test_evidence_rows_carry_their_kind_as_a_fact_in_the_design_tokens(self) -> None:
        plugin = PLUGIN.read_text(encoding="utf-8")
        engine = ENGINE.read_text(encoding="utf-8")
        notice = (
            ROOT / "network" / "mod" / "ComfyQuestContracts" / "CreatorLoopNotice.cs"
        ).read_text(encoding="utf-8")
        # The taxonomy lives in Contracts; kinds are tagged where lines are composed,
        # never parsed back out of rendered copy (the DeadlineUrgent lesson, again).
        self.assertIn(
            "public enum CreatorEvidenceKind { Plumbing, Story, Cast, Warning }", notice
        )
        self.assertIn(
            "MatchedLine(currentStage.Id, decision.Transition, matchedProgress, rejectedEvidence),\n            CreatorEvidenceKind.Story);",
            engine,
        )
        self.assertIn(
            'TransitionLine(currentStage.Id, decision.Transition), CreatorEvidenceKind.Story);',
            engine,
        )
        self.assertIn('ActionLine(action, "failed: " + e.Message), CreatorEvidenceKind.Warning);', engine)
        self.assertIn('bindings now OTHER VERSION — re-CAST or roll back",\n          CreatorEvidenceKind.Warning);', engine)
        self.assertIn("public IReadOnlyList<CreatorEvidenceLine> RecentEvidenceLines()", engine)
        # The drawer renders kinds; it never classifies by inspecting the text.
        self.assertIn("engine?.RecentEvidenceLines()", plugin)
        self.assertIn("void DrawEvidenceRow(CreatorEvidenceLine line)", plugin)
        self.assertIn('AddOutcome(CreatorEvidenceKind.Cast,"CAST · "+status)', plugin)
        self.assertNotIn('Contains("CAST', plugin)
        self.assertNotIn('StartsWith("Matched', plugin)
        # The two load-bearing design tokens: bright amber primary (dark ink) and the
        # urgent red; and the new textures are released like every other.
        self.assertIn('Solid("runtime-primary",new UnityEngine.Color(.914f,.659f,.247f,1f))', plugin)
        self.assertIn('Solid("runtime-deadline-urgent",new UnityEngine.Color(.561f,.086f,.086f,.92f))', plugin)
        for texture in ("primaryBackground", "castRowBackground"):
            with self.subTest(texture=texture):
                self.assertIn(f"Destroy(ref {texture});", plugin)
        # Grammar: green is state, never a button — the primary action is amber.
        self.assertIn("primaryStyle=ButtonStyle(primaryBackground", plugin)
        # Kinds travel WITH receipts (additive nullable v1 field), stamped at the writer:
        # the engine stamps every receipt, a successful CAST is the cast row, and failed
        # charm work is a warning. No reader ever re-derives a kind from rendered copy.
        receipts_contract = (
            ROOT / "network" / "mod" / "ComfyQuestContracts" / "RuntimeReceipts.cs"
        ).read_text(encoding="utf-8")
        binding = BINDING.read_text(encoding="utf-8")
        self.assertIn('[JsonProperty("evidence_kind", NullValueHandling=NullValueHandling.Ignore)]', receipts_contract)
        self.assertIn("receipt.EvidenceKind = CreatorEvidenceLine.KindName(kind);", engine)
        self.assertIn('Status="inscribed"', binding)
        self.assertIn("EvidenceKind=CreatorEvidenceLine.KindName(CreatorEvidenceKind.Cast)", binding)
        self.assertIn("EvidenceKind=CreatorEvidenceLine.KindName(CreatorEvidenceKind.Warning)", binding)
        # The drawer's status card answers "which revision is running?" title-first, from
        # the one shared ActiveTitle fact; the quiet inbox probe is a display read only.
        self.assertIn("CreatorLoopNotice.ActiveTitle(TitleSource(),active)", plugin)
        self.assertIn('"Nothing is playing yet."', plugin)
        self.assertIn('"Now playing"', plugin)

    def test_idle_loop_responses_reassert_and_the_card_owns_the_idle_state(self) -> None:
        # Phase 3 exit session 1: five idle F10 and three idle F11 presses were accepted
        # (check/accepted, load/already_active) yet nothing legible reached the player,
        # who concluded the keys were broken. Valheim's MessageHud.UpdateMessage merges a
        # repeated TopLeft text into the line already on screen and only renders its "xN"
        # counter when the summed message amounts exceed one — an amount of 0 made every
        # idle repeat invisible. The idle response now rides that native counter (amount
        # 1), idleness is a contract fact tagged where the sentence is composed, and the
        # drawer's status card makes "up to date" a first-class state instead of silence.
        plugin = PLUGIN.read_text(encoding="utf-8")
        notice = (
            ROOT / "network" / "mod" / "ComfyQuestContracts" / "CreatorLoopNotice.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("void Report(string message,bool reassert=false)", plugin)
        self.assertIn(
            "ShowMessage(MessageHud.MessageType.TopLeft,message,reassert?1:0)", plugin
        )
        self.assertIn("statusIdle=notice.Idle", plugin)
        self.assertIn("Report(status,statusIdle)", plugin)
        self.assertIn("public bool Idle { get; set; }", notice)
        # The card's state colors ride the QuestCardState fact, never the rendered line.
        self.assertIn("CreatorLoopNotice.Card(TitleSource(),active,inboxChecked)", plugin)
        self.assertIn("QuestCardState.UpdateReady=>stateReadyStyle", plugin)
        self.assertIn('"Now playing — up to date"', notice)

    def test_drawer_composition_leads_with_the_status_card_and_recedes_machinery(self) -> None:
        # Session 1's verdict — "kernel UI on f9 press looks the same" — held the lap
        # open for the design canvas's composition, not just its tokens: status card
        # first, content-update ladder and creator actions next, evidence feed last,
        # with captures, arcane sight, and rollback machinery behind one disclosure.
        plugin = PLUGIN.read_text(encoding="utf-8")
        self.assertLess(plugin.index("DrawStatusCard();"), plugin.index("DrawUpdateWorkflow();"))
        self.assertLess(plugin.index("DrawUpdateWorkflow();"), plugin.index("DrawRecentEvidence();"))
        self.assertIn("if(showMaintenance){DrawOutcomes();", plugin)

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

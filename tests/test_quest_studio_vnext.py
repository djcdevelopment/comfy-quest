import re
import json
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PAGE = ROOT / "src" / "Quest.Studio" / "QuestStudioPage.cs"
WORKSPACE = ROOT / "src" / "Quest.Studio" / "QuestStudioWorkspace.cs"
SERVICE = ROOT / "src" / "Quest.Studio" / "QuestStudioService.cs"
ENDPOINTS = ROOT / "src" / "Quest.Studio" / "QuestStudioEndpoints.cs"
PUBLISHER = ROOT / "src" / "Quest.Studio" / "QuestPackPublisher.cs"
EASY_PATCHES = ROOT / "network" / "mod" / "ComfyQuestRuntime" / "RuntimeEasyEventPatches.cs"
CORE_ACTION_PATCHES = ROOT / "network" / "mod" / "ComfyQuestRuntime" / "RuntimeCoreActionPatches.cs"
COMBAT_PATCHES = ROOT / "network" / "mod" / "ComfyQuestRuntime" / "RuntimeKillPatches.cs"
HARVEST_PATCHES = ROOT / "network" / "mod" / "ComfyQuestRuntime" / "RuntimeHarvestPatches.cs"
PROGRESSION_PATCHES = ROOT / "network" / "mod" / "ComfyQuestRuntime" / "RuntimeProgressionPatches.cs"
WORLD_STATE_PATCHES = ROOT / "network" / "mod" / "ComfyQuestRuntime" / "RuntimeWorldStatePatches.cs"
RUNTIME_PLUGIN = ROOT / "network" / "mod" / "ComfyQuestRuntime" / "ComfyQuestRuntime.cs"
SIGNAL_CATALOG = ROOT / "network" / "mod" / "ComfyQuestContracts" / "CreatorSignalCatalog.g.cs"
CAPABILITY_RULES = ROOT / "tools" / "component-packets" / "quest-capability-rules.json"
CAPABILITY_MANIFEST = ROOT / "tools" / "component-packets" / "samples" / "quest-capability-manifest.json"


def raw_constant(name: str) -> str:
    source = PAGE.read_text(encoding="utf-8")
    match = re.search(rf'public const string {name} = """\n(.*?)\n""";', source, re.S)
    if not match:
        raise AssertionError(f"missing raw constant {name}")
    return match.group(1)


class QuestStudioVNextTests(unittest.TestCase):
    def test_browser_script_is_valid_javascript(self) -> None:
        script = raw_constant("Js")
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "studio.js"
            path.write_text(script, encoding="utf-8")
            result = subprocess.run(
                ["node", "--check", str(path)], capture_output=True, text=True, check=False
            )
        self.assertEqual(0, result.returncode, result.stderr)

    def test_page_guides_creators_through_three_soft_stages(self) -> None:
        html = raw_constant("Html")
        script = raw_constant("Js")
        for stage, label in (
            ("author", "Author"),
            ("rehearse", "Rehearse"),
            ("publish", "Publish &amp; Play"),
        ):
            self.assertIn(f'data-stage="{stage}"', html)
            self.assertIn(f'data-stage-panel="{stage}"', html)
            self.assertIn(label, html)
        for expected in (
            "Shape the quest one beat at a time",
            "Rehearse this quest",
            "Play this revision",
            "Publish immutable version",
            "Runtime validates and pulls",
            "Live proof",
        ):
            self.assertIn(expected, html)
        self.assertIn("function showStage(name,moveFocus=true)", script)
        self.assertIn("workspace.scrollTop=0", script)
        self.assertIn("workspace.scrollLeft=0", script)
        self.assertIn("currentStage='author'", script)
        self.assertNotIn('data-tab="', html)

    def test_secondary_text_meets_normal_text_contrast_on_work_surfaces(self) -> None:
        css = raw_constant("Css")
        colors = dict(re.findall(r"--([a-z-]+):(#[0-9a-f]{6})", css))

        def luminance(color: str) -> float:
            channels = [int(color[index:index + 2], 16) / 255 for index in (1, 3, 5)]
            linear = [
                value / 12.92 if value <= 0.04045
                else ((value + 0.055) / 1.055) ** 2.4
                for value in channels
            ]
            return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2]

        def contrast(left: str, right: str) -> float:
            high, low = sorted((luminance(left), luminance(right)), reverse=True)
            return (high + 0.05) / (low + 0.05)

        for surface in ("bg", "panel", "panel-raised"):
            self.assertGreaterEqual(contrast(colors["dim"], colors[surface]), 4.5)
        # The creator-loop insets: --inset and the raised chip surface. The design's own
        # #6b7488 dim failed this matrix, which is why the Studio token is #7d87a0.
        for tone in ("ink-soft", "green", "amber"):
            for runtime_surface in (colors["inset"], colors["panel-raised"]):
                self.assertGreaterEqual(contrast(colors[tone], runtime_surface), 4.5)
        self.assertIn(".preview-keys{margin-top:22px;font-size:11.5px;color:var(--dim)}", css)

    def test_status_card_and_receipt_kinds_speak_the_design_language(self) -> None:
        html = raw_constant("Html")
        css = raw_constant("Css")
        script = raw_constant("Js")
        service = SERVICE.read_text(encoding="utf-8")
        workspace = WORKSPACE.read_text(encoding="utf-8")
        # The status card answers "which revision is running?" title-first, from the one
        # shared CreatorLoopNotice.ActiveTitle contract fact.
        for element_id in ("quest-status-title", "quest-status-version", "quest-status-state"):
            with self.subTest(element_id=element_id):
                self.assertIn(f'id="{element_id}"', html)
        for marker in (
            "d?.active_title||d?.active_pack_id",
            "'Now playing'",
            "'An earlier telling is playing'",
            "'Nothing is playing yet'",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, script)
        body = script[script.index("function renderStatusCard"):]
        self.assertIn("}", body[: body.index("\n")])
        self.assertIn("CreatorLoopNotice.ActiveTitle(", workspace)
        self.assertIn("ActiveTitle = status.ActiveTitle", service)
        # Receipt rows carry their kind from the receipt itself and fail closed to
        # plumbing; the page renders the kind, it never re-derives one from copy.
        self.assertIn('receipt.EvidenceKind ?? "plumbing"', service)
        self.assertIn("kind=r.kind||'plumbing'", script)
        for kind_class in ("kind-story", "kind-cast", "kind-warning", "kind-plumbing"):
            with self.subTest(kind_class=kind_class):
                self.assertIn(f".receipt-row.{kind_class}", css)
        # Fonts stay local-first: the design faces lead the stacks, nothing loads over
        # the network, and the page keeps exactly its two local link tags.
        self.assertIn("--sans:'Source Sans 3',Inter,ui-sans-serif", css)
        self.assertIn("--serif:Grenze,Georgia", css)
        self.assertIn("--mono:'JetBrains Mono',ui-monospace", css)
        self.assertNotIn("https://", css)
        self.assertNotIn("@import", css)

    def test_creator_lane_hides_implementation_plumbing(self) -> None:
        html = raw_constant("Html")
        simple = html[
            html.index('id="stage-author"'):html.index('id="stage-rehearse"')
        ]
        for hidden_concern in ("ZDO", "Route ID", "Priority", "Actor role", "Charm target"):
            self.assertNotIn(hidden_concern, simple)
        self.assertIn("Studio handles the Runtime plumbing", simple)
        self.assertIn("Open graph editor", simple)

    def test_advanced_graph_identity_and_json_live_behind_one_tools_surface(self) -> None:
        html = raw_constant("Html")
        script = raw_constant("Js")
        advanced = html[html.index('id="advanced-tools"'):]
        for expected in (
            "Advanced tools",
            "Graph editor",
            "Certified JSON",
            "Data &amp; history",
            "Runtime identity",
            "Package compatibility",
            "Charm surface override",
            "Route ID",
            "Priority",
        ):
            self.assertIn(expected, advanced)
        self.assertNotIn('data-tool-tab="settings"', advanced)
        self.assertIn("function showTool(name)", script)
        self.assertIn("function closeTools()", script)

    def test_graph_editor_uses_the_desktop_width_and_has_an_accessible_splitter(self) -> None:
        html = raw_constant("Html")
        css = raw_constant("Css")
        script = raw_constant("Js")
        self.assertIn('id="graph-resizer"', html)
        self.assertIn('role="separator"', html)
        self.assertIn('aria-label="Resize graph inspector"', html)
        self.assertIn(".advanced-workspace.graph-active{max-width:none}", css)
        self.assertIn("grid-template-columns:minmax(560px,1fr) 10px", css)
        self.assertIn("cursor:col-resize", css)
        self.assertIn("GRAPH_INSPECTOR_WIDTH_KEY", script)
        self.assertIn("function bindGraphResizer()", script)
        self.assertIn("e.key==='ArrowLeft'", script)

    def test_runtime_return_path_projects_active_identity_and_server_labels(self) -> None:
        html = raw_constant("Html")
        script = raw_constant("Js")
        workspace = WORKSPACE.read_text(encoding="utf-8")
        service = SERVICE.read_text(encoding="utf-8")
        for element_id in (
            "active-revision",
            "advanced-active-revision",
            "advanced-runtime-receipts",
        ):
            self.assertIn(f'id="{element_id}"', html)
        self.assertIn("Active in game", html)
        self.assertIn("matches this draft", script)
        self.assertIn("differs from this draft", script)
        self.assertIn("r.route_label,r.effect_label", script)
        self.assertIn("data-live-node", script)
        self.assertIn("liveNodeId=row.dataset.liveNode", script)
        for function_name in (
            "activeRevisionText",
            "renderActiveRevision",
            "runtimeReceiptHtml",
            "bindRuntimeReceiptHover",
            "setRuntimeReceipts",
        ):
            body = script[script.index(f"function {function_name}") :]
            self.assertIn("}", body[: body.index("\n")], function_name)
        self.assertIn("RuntimeRouteLabels", workspace)
        self.assertIn("RuntimeEffectLabels", workspace)
        self.assertIn('ActiveRelation = activeRelation', workspace)
        self.assertIn("active?.ActivationId", service)
        self.assertIn("status.RouteLabels.TryGetValue", service)
        self.assertIn("status.EffectLabels.TryGetValue", service)
        for expected in ("Validation", "Transfer", "Activation", "Rebind", "Runtime observed"):
            self.assertIn(expected, service)
        self.assertIn("line.status==='FAIL'", script)
        self.assertIn("d.pass_lines||[]", script)

    def test_play_revision_is_revision_guarded_and_uses_the_dev_only_route(self) -> None:
        script = raw_constant("Js")
        workspace = WORKSPACE.read_text(encoding="utf-8")
        publisher = PUBLISHER.read_text(encoding="utf-8")
        routes = ENDPOINTS.read_text(encoding="utf-8")
        play = script[script.index("async function playRevision()") :]
        play = play[: play.index("\n")]
        self.assertIn("expected_revision:context.revision", play)
        self.assertIn("context.revision!==checked.request_context.revision", play)
        self.assertIn("setPublishLock(true)", play)
        self.assertIn("setPublishLock(false)", play)
        self.assertIn("PublishDevAsync", workspace)
        self.assertIn('"inbox-dev"', publisher)
        self.assertIn('/api/v2/quest-studio/projects/{projectId}/play', routes)

    def test_play_revision_stays_locked_until_runtime_is_connected_and_armed(self) -> None:
        script = raw_constant("Js")
        journey = script[script.index("function renderJourney()") :]
        journey = journey[: journey.index("\n")]
        self.assertIn("devReady=runtimeState?.dev_connected&&runtimeState?.dev_armed", journey)
        self.assertIn("$('#play-project').disabled=!project||!devReady", journey)

        workspace = WORKSPACE.read_text(encoding="utf-8")
        self.assertIn('"certified" => !devConnected', workspace)
        self.assertIn('devStatus?.Armed != true', workspace)
        self.assertIn('Runtime is armed. Play this revision when ready', workspace)
        play = workspace[workspace.index("public async Task<StudioPublishResult> PlayRevisionAsync") :]
        play = play[: play.index("public object History")]
        self.assertIn('StudioPublishResult.Fail("dev_channel_disconnected")', play)
        self.assertIn('StudioPublishResult.Fail("dev_channel_not_armed")', play)
        self.assertLess(play.index('dev_channel_not_armed'), play.index('_publisher.PublishDevAsync'))

    def test_grimoire_picker_scales_the_creator_catalog_without_exposing_seams(self) -> None:
        html = raw_constant("Html")
        script = raw_constant("Js")
        css = raw_constant("Css")
        for expected in (
            'id="event-picker"',
            'id="event-search" type="search"',
            'id="event-categories"',
            'id="include-extended"',
            'role="list"',
            "Search 34 creator-safe meanings",
            "91 low-level game seams",
        ):
            self.assertIn(expected, html)
        for expected in (
            "function normalizeCatalog(raw)",
            "quick_presets",
            "creator_events",
            "engine_events",
            "fixed_where",
            "actor_roles",
            "target_policy",
            "production_available",
            "Research",
            "synthetic catalog example",
            "function filteredCreatorEvents()",
            "function choosePickerEvent(name)",
        ):
            self.assertIn(expected, script)
        self.assertIn(".event-picker::backdrop", css)
        self.assertIn("@media(max-width:680px)", css)

    def test_creator_inspector_uses_progressive_constraints_completion_and_all_effects(self) -> None:
        html = raw_constant("Html")
        script = raw_constant("Js")
        for expected in (
            "Player action",
            "Make this action specific",
            "Completion",
            'id="beat-window-row" hidden',
            "Afterward",
            'id="beat-effect-type"',
        ):
            self.assertIn(expected, html + script)
        for effect in (
            "message",
            "timer_start",
            "timer_cancel",
            "grant_item",
            "spawn",
            "clear_spawned",
        ):
            self.assertIn(f"a.type==='{effect}'", script)
        self.assertIn("delete source.actor_role", script)
        self.assertIn("delete source.timer_id", script)
        self.assertIn("where.timer_id", script)
        self.assertIn("e.target.checked?'true':null", script)
        self.assertIn("function eventSeedWhere(def)", script)
        self.assertIn("eventSeedWhere(d)", script)
        self.assertIn("normalizedTarget(d.definition", script)
        self.assertIn("fixedWhere[x.name]==null", script)
        self.assertIn("function normalizeProjectRoutes(value)", script)
        self.assertIn("delete r.actor_role", script)
        self.assertIn("delete r.timer_id", script)
        self.assertIn("fields.timer_id=String", script)

    def test_catalog_target_policies_constrain_creator_and_graph_editors(self) -> None:
        html = raw_constant("Html")
        script = raw_constant("Js")
        self.assertIn('id="route-target-row"', html)
        self.assertIn("function targetPolicy(def)", script)
        self.assertIn("function normalizedTarget(def,value)", script)
        self.assertIn("function targetEditor(def,value,scope,id='')", script)
        self.assertIn("policy==='closed'", script)
        self.assertIn("policy==='fixed-output'", script)
        self.assertIn("policy==='none'", script)
        self.assertIn("r.target=normalizedTarget(d,null)", script)
        self.assertIn("r.where=eventSeedWhere(d)", script)

    def test_guided_rehearsal_is_project_generated_and_reports_path_limits(self) -> None:
        html = raw_constant("Html")
        script = raw_constant("Js")
        self.assertIn("Studio will generate and evaluator-check", script)
        self.assertIn("steps=mode==='guided'?[]:clone(rehearsalSteps)", script)
        self.assertIn("generated_steps", script)
        self.assertIn("available_paths", script)
        self.assertIn("Coverage limits", script)
        self.assertIn("More rehearsal options", html)

    def test_data_history_and_usage_stay_in_advanced_tools(self) -> None:
        html = raw_constant("Html")
        script = raw_constant("Js")
        author = html[html.index('id="stage-author"'):html.index('id="stage-rehearse"')]
        self.assertNotIn("Download project bundle", author)
        for expected in (
            "Download project bundle",
            "Download current questpack",
            "Certified history",
            "Local usage insights",
            "no titles, messages, targets, searches, identities",
            'id="include-live-evidence"',
        ):
            self.assertIn(expected, html)
        for route in (
            "/export",
            "/questpack",
            "/api/v2/quest-studio/usage/settings",
            "/api/v2/quest-studio/usage/export",
            "/api/v2/quest-studio/usage/reset",
        ):
            self.assertIn(route, script)
        self.assertIn("report.available!==false", script)
        self.assertIn("if(!(await saveNow()))", script)
        self.assertLess(
            script.index("if(!(await saveNow()))", script.index("async function downloadBundle")),
            script.index("downloadPost", script.index("async function downloadBundle")),
        )

    def test_publish_is_revision_guarded_and_reloads_a_conflicting_draft(self) -> None:
        script = raw_constant("Js")
        publish = script[script.index("async function publish()") :]
        publish = publish[: publish.index("\n")]
        self.assertIn("expected_revision:context.revision", publish)
        self.assertIn("setPublishLock(true)", publish)
        self.assertIn("setPublishLock(false)", publish)
        self.assertIn("context.revision!==certified.request_context.revision", publish)
        self.assertIn("e.status===409&&e.conflict&&e.project", publish)
        self.assertIn("project=e.project", publish)
        self.assertIn("Publish stopped because this project changed", publish)

    def test_async_responses_cannot_repopulate_a_newer_or_different_draft(self) -> None:
        script = raw_constant("Js")
        self.assertIn("function requestContext()", script)
        self.assertIn("function isCurrentContext(context)", script)
        self.assertIn("operationSequences={rehearsal:0,certify:0,runtime:0,project:0}", script)
        self.assertIn("function beginOperation(name)", script)
        self.assertIn("function isLatestOperation(name,sequence)", script)
        for function_name in ("runRehearsal", "certify", "refreshRuntime"):
            body = script[script.index(f"function {function_name}") :]
            body = body[: body.index("\n")]
            self.assertIn("requestContext()", body, function_name)
            self.assertIn("isCurrentContext(context)", body, function_name)
            self.assertIn("beginOperation(", body, function_name)
            self.assertIn("isLatestOperation(", body, function_name)
        compiled = script[script.index("async function downloadCompiledJson()") :]
        compiled = compiled[: compiled.index("\n")]
        self.assertIn("await certify(false)", compiled)
        self.assertNotIn("if(!compiledJson)", compiled)

    def test_project_gets_and_mutations_do_not_overwrite_intervening_edits(self) -> None:
        script = raw_constant("Js")
        self.assertIn("function captureDraftContext()", script)
        self.assertIn("function isSameDraftContext(context)", script)
        for function_name in ("loadProjects", "openProject"):
            body = script[script.index(f"function {function_name}") :]
            body = body[: body.index("\n")]
            self.assertIn("context=captureDraftContext()", body, function_name)
            self.assertIn("!isSameDraftContext(context)||dirty", body, function_name)
        load = script[script.index("async function loadProjects") :]
        load = load[: load.index("\n")]
        self.assertIn("openProject(target)", load)
        self.assertNotIn("openProject(target,true)", load)
        for marker in ("#create-project", "#duplicate-project", "#bump-version"):
            at = script.index(marker)
            body = script[at : at + 700]
            self.assertIn("beginOperation('project')", body, marker)

    def test_autosave_conflict_preserves_the_local_draft(self) -> None:
        script = raw_constant("Js")
        save = script[script.index("async function saveNow()") :]
        save = save[: save.index("\n")]
        self.assertIn("e.status===409&&e.project", save)
        self.assertIn("dirty=true", save)
        self.assertIn("Your local edits are preserved", save)
        self.assertNotIn("project=e.project", save)

    def test_route_migration_is_lossless_and_manual_invalid_targets_do_not_widen(self) -> None:
        script = raw_constant("Js")
        migration = script[script.index("function normalizeProjectRoutes(value)") :]
        migration = migration[: migration.index("\n")]
        self.assertIn("where.actor_role=String(r.actor_role)", migration)
        self.assertIn("where.timer_id=String(r.timer_id)", migration)
        self.assertIn("delete r.actor_role", migration)
        self.assertIn("delete r.timer_id", migration)
        self.assertNotIn("fixed_where", migration)
        self.assertNotIn("normalizedTarget", migration)
        self.assertIn("function manualTargetValue(def,raw)", script)
        self.assertIn("if(!result.ok)", script)
        self.assertIn("Target not supported", script)

    def test_degraded_usage_controls_keep_recovery_actions_available(self) -> None:
        script = raw_constant("Js")
        usage = script[script.index("function renderUsage(report)") :]
        usage = usage[: usage.index("\n")]
        self.assertIn("report.settings_available!==false", usage)
        self.assertIn("report.aggregate_available!==false", usage)
        self.assertIn("$('#usage-enabled').disabled=false", usage)
        self.assertIn("$('#reset-usage').disabled=false", usage)
        self.assertIn("$('#download-usage').disabled=!available", usage)

    def test_saved_generation_gates_server_actions_and_source_has_no_mojibake(self) -> None:
        source = PAGE.read_text(encoding="utf-8")
        script = raw_constant("Js")
        self.assertIn("async function saveNow()", script)
        self.assertIn("return editGeneration===generation", script)
        for function_name in (
            "openProject",
            "runRehearsal",
            "certify",
            "downloadBundle",
            "downloadQuestpack",
        ):
            body = script[script.index(f"function {function_name}") :]
            body = body[: body.index("\n")]
            self.assertIn("await saveNow()", body, function_name)
        for marker in ("Â", "â", "Ã", "�"):
            self.assertNotIn(marker, source)

    def test_page_exposes_soft_readiness_and_accessible_status(self) -> None:
        html = raw_constant("Html")
        script = raw_constant("Js")
        self.assertIn('rel="icon" href="data:image/svg+xml,', html)
        self.assertGreaterEqual(html.count('aria-live="polite"'), 2)
        for element_id in (
            "author-state",
            "rehearse-state",
            "publish-state",
            "draft-readiness",
            "preview-readiness",
            "runtime-readiness",
            "rehearsal-badge",
        ):
            self.assertIn(f'id="{element_id}"', html)
        self.assertIn("Rehearsal is recommended, not required", script)
        self.assertNotIn("disabled=!rehearsalResult", script)

    def test_browser_script_only_queries_ids_present_in_the_page(self) -> None:
        html = raw_constant("Html")
        script = raw_constant("Js")
        ids = re.findall(r'\bid="([a-zA-Z0-9_-]+)"', html)
        self.assertEqual(len(ids), len(set(ids)), "page contains duplicate element IDs")
        queried = set(re.findall(r"\$\('#([a-zA-Z0-9_-]+)'\)", script))
        self.assertEqual(set(), queried - set(ids))

    def test_runtime_easy_adapters_are_local_and_privacy_minimal(self) -> None:
        patches = EASY_PATCHES.read_text(encoding="utf-8")
        self.assertIn('typeof(Chat), "SendText"', patches)
        self.assertIn('typeof(Humanoid), "DropItem"', patches)
        self.assertIn('typeof(Humanoid), "Pickup"', patches)
        self.assertIn('typeof(Humanoid), "EquipItem"', patches)
        self.assertIn('typeof(Player), "ConsumeItem"', patches)
        self.assertIn('typeof(Character), "Heal"', patches)
        self.assertIn('typeof(Character), "RPC_Heal"', patches)
        self.assertIn("__instance != Player.m_localPlayer", patches)
        self.assertIn(
            "RuntimeEasyEventPatches.Apply(harmony)",
            RUNTIME_PLUGIN.read_text(encoding="utf-8"),
        )

    def test_runtime_core_actions_require_success_and_correlate_alternate_witnesses(self) -> None:
        runtime = CORE_ACTION_PATCHES.read_text(encoding="utf-8")
        for signature in (
            "Container.TakeAll(Humanoid)",
            "Humanoid.UnequipItem(ItemData, bool)",
            "WearNTear.Destroy(HitData, bool)",
            "Player.RemovePiece()",
            "WearNTear.RPC_Remove(long, bool)",
            "Player.Repair(ItemData, Piece)",
            "WearNTear.RPC_Repair(long)",
            "Player.TeleportTo(Vector3, Quaternion, bool)",
        ):
            self.assertIn(signature, runtime)
        self.assertIn("RPC_TakeAllRespons", runtime)
        self.assertIn("CoreActionProof.ContainerEmptied", runtime)
        self.assertIn("!__instance.IsItemEquiped(__0)", runtime)
        self.assertIn("__0.GetAttacker() != Player.m_localPlayer", runtime)
        self.assertIn("CoreActionProof.PieceRepaired", runtime)
        self.assertIn("ZNet.GetUID() != sender", runtime)
        self.assertIn(
            "RuntimeCoreActionPatches.Apply(harmony)",
            RUNTIME_PLUGIN.read_text(encoding="utf-8"),
        )

    def test_runtime_combat_and_harvest_require_authoritative_outcomes(self) -> None:
        combat = COMBAT_PATCHES.read_text(encoding="utf-8")
        harvest = HARVEST_PATCHES.read_text(encoding="utf-8")
        for signature in (
            "Humanoid.BlockAttack(HitData, Character)",
            "Character.Damage(HitData)",
            "Character.RPC_Damage(long, HitData)",
            "Character.Stagger(Vector3)",
            "Character.RPC_Stagger(long, Vector3)",
        ):
            self.assertIn(signature, combat)
        for signature in (
            "Destructible.Damage(HitData)",
            "Destructible.RPC_Damage(long, HitData)",
            "MineRock.Damage(HitData)",
            "MineRock5.Damage(HitData)",
            "MineRock5.RPC_Damage(long, HitData, int)",
            "TreeBase.Damage(HitData)",
            "TreeBase.RPC_Damage(long, HitData)",
            "TreeLog.Damage(HitData)",
            "TreeLog.RPC_Damage(long, HitData)",
            "Pickable.Interact(Humanoid, bool, bool)",
            "Pickable.RPC_Pick(long, int)",
        ):
            self.assertIn(signature, harvest)
        self.assertIn("CombatHarvestProof.DamageDealt", combat)
        self.assertIn("CombatHarvestProof.CharacterStaggered", combat)
        self.assertIn("CombatHarvestProof.ResourceDamaged", harvest)
        self.assertIn("CombatHarvestProof.ResourcePicked", harvest)
        self.assertIn("after < state.Before", harvest)
        self.assertIn("RuntimeHarvestPatches.Apply(harmony)", RUNTIME_PLUGIN.read_text(encoding="utf-8"))

    def test_runtime_progression_and_world_state_reject_no_op_witnesses(self) -> None:
        progression = PROGRESSION_PATCHES.read_text(encoding="utf-8")
        world = WORLD_STATE_PATCHES.read_text(encoding="utf-8")
        for signature in (
            "Player.AddStamina(float)",
            "Player.OnDeath()",
            "Player.RaiseSkill(SkillType, float)",
            "Player.SetMaxHealth(float, bool)",
            "Player.UseStamina(float)",
            "Skills.LowerAllSkills(float)",
        ):
            self.assertIn(signature, progression)
        for signature in (
            "ZoneSystem.RPC_SetGlobalKey(long, string)",
            "ZoneSystem.RemoveGlobalKey(GlobalKeys)",
            "ZoneSystem.RemoveGlobalKey(string)",
            "ZoneSystem.SetGlobalKey(GlobalKeys)",
            "ZoneSystem.SetGlobalKey(GlobalKeys, float)",
            "ZoneSystem.SetGlobalKey(string)",
        ):
            self.assertIn(signature, world)
        self.assertIn("ProgressionProof.ValueChanged", progression)
        self.assertIn("ProgressionProof.ValueIncreased", progression)
        self.assertIn("ProgressionProof.ValueDecreased", progression)
        self.assertIn("ProgressionProof.SkillRaised", progression)
        self.assertIn("ProgressionProof.SkillsLowered", progression)
        self.assertIn("ProgressionProof.PlayerDied", progression)
        self.assertIn("ZNet.instance.IsServer()", world)
        self.assertIn("WorldStateProof.GlobalKeySet", world)
        self.assertIn("WorldStateProof.GlobalKeyRemoved", world)
        plugin = RUNTIME_PLUGIN.read_text(encoding="utf-8")
        self.assertIn("RuntimeProgressionPatches.Apply(harmony)", plugin)
        self.assertIn("RuntimeWorldStatePatches.Apply(harmony)", plugin)

    def test_generated_fast_signal_catalog_matches_the_reviewed_policy(self) -> None:
        rules = json.loads(CAPABILITY_RULES.read_text(encoding="utf-8"))
        manifest = json.loads(CAPABILITY_MANIFEST.read_text(encoding="utf-8"))
        expected = [signal["Id"] for signal in rules["FastSignals"]]
        generated = SIGNAL_CATALOG.read_text(encoding="utf-8")
        actual = re.findall(r'new Definition\("([a-z]+)"', generated)
        self.assertEqual(expected, actual)
        self.assertEqual(
            ["say", "shout", "drop", "pickup", "equip", "consume", "heal", "wait"],
            actual,
        )
        self.assertEqual(expected, [signal["Id"] for signal in manifest["FastSignals"]])
        for signal in manifest["FastSignals"]:
            if signal["Id"] != "wait":
                self.assertEqual("core", signal["LabProfile"])
                self.assertEqual("primary", signal["LabRoute"])

    def test_v2_routes_keep_game_mutation_out_of_the_browser(self) -> None:
        routes = ENDPOINTS.read_text(encoding="utf-8")
        self.assertIn('/api/v2/quest-studio/projects/{projectId}/rehearse', routes)
        self.assertIn('/api/v2/quest-studio/projects/{projectId}/runtime-status', routes)
        self.assertIn('/api/v2/quest-studio/projects/{projectId}/play', routes)
        self.assertNotIn('/api/v2/quest-studio/check', routes)
        self.assertNotIn('/api/v2/quest-studio/load', routes)
        self.assertNotIn('/api/v2/quest-studio/bind', routes)

    def test_studio_catalog_is_limited_to_runtime_implemented_effects(self) -> None:
        workspace = WORKSPACE.read_text(encoding="utf-8")
        expected = {
            "message",
            "timer_start",
            "timer_cancel",
            "grant_item",
            "spawn",
            "clear_spawned",
        }
        match = re.search(r"CatalogEffects\(\) => new object\[\]\s*\{(.*?)\n\s*\};", workspace, re.S)
        self.assertIsNotNone(match)
        actual = set(re.findall(r'id = "([a-z_]+)"', match.group(1)))
        self.assertEqual(expected, actual)
        self.assertIn("Synthetic rehearsal only; this does not prove", workspace)
        self.assertIn('"signal-circuit" => SignalCircuitTemplate', workspace)
        self.assertIn("RepeatCount = repeatCount", workspace)
        self.assertIn("WithinSeconds = withinSeconds", workspace)


if __name__ == "__main__":
    unittest.main()

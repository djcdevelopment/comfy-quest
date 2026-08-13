import re
import json
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PAGE = ROOT / "src" / "Quest.Studio" / "QuestStudioPage.cs"
WORKSPACE = ROOT / "src" / "Quest.Studio" / "QuestStudioWorkspace.cs"
ENDPOINTS = ROOT / "src" / "Quest.Studio" / "QuestStudioEndpoints.cs"
EASY_PATCHES = ROOT / "network" / "mod" / "ComfyQuestRuntime" / "RuntimeEasyEventPatches.cs"
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

    def test_page_exposes_the_browser_first_r_and_d_loop(self) -> None:
        html = raw_constant("Html")
        for expected in (
            "Quest projects",
            "Quest beats",
            "Do this how many times?",
            "Within seconds (optional)",
            "Advanced graph",
            "Browser rehearsal",
            "Runtime cockpit",
            "F10 Check",
            "F11 Load",
            "Publish to Runtime inbox",
        ):
            self.assertIn(expected, html)

    def test_simple_lane_hides_implementation_plumbing(self) -> None:
        html = raw_constant("Html")
        simple = html[html.index('id="simple-builder"'):html.index('id="advanced-builder"')]
        for hidden_concern in ("ZDO", "Route ID", "Priority", "Actor role", "Charm target"):
            self.assertNotIn(hidden_concern, simple)
        self.assertIn("Studio handles routes, IDs, targets, and Charm compatibility", simple)

    def test_runtime_easy_adapters_are_local_and_privacy_minimal(self) -> None:
        patches = EASY_PATCHES.read_text(encoding="utf-8")
        self.assertIn('typeof(Chat), "SendText"', patches)
        self.assertIn('typeof(Humanoid), "DropItem"', patches)
        self.assertIn('typeof(Humanoid), "Pickup"', patches)
        self.assertIn('typeof(Humanoid), "EquipItem"', patches)
        self.assertIn('typeof(Player), "ConsumeItem"', patches)
        self.assertIn('typeof(Character), "Heal"', patches)
        self.assertIn("__instance != Player.m_localPlayer", patches)
        self.assertNotIn("RPC_Heal", patches)
        self.assertIn(
            "harmony.PatchAll(typeof(RuntimeEasyEventPatches))",
            RUNTIME_PLUGIN.read_text(encoding="utf-8"),
        )

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
        self.assertNotIn('/api/v2/quest-studio/check', routes)
        self.assertNotIn('/api/v2/quest-studio/load', routes)
        self.assertNotIn('/api/v2/quest-studio/bind', routes)

    def test_studio_catalog_is_limited_to_runtime_implemented_actions(self) -> None:
        workspace = WORKSPACE.read_text(encoding="utf-8")
        expected = {
            "message",
            "timer_start",
            "timer_cancel",
            "grant_item",
            "spawn",
            "clear_spawned",
        }
        match = re.search(r"SupportedActions =\s*\{([^}]+)\}", workspace)
        self.assertIsNotNone(match)
        actual = set(re.findall(r'"([a-z_]+)"', match.group(1)))
        self.assertEqual(expected, actual)
        self.assertIn("Browser rehearsal only; this does not prove", workspace)
        self.assertIn('"signal-circuit" => SignalCircuitTemplate', workspace)
        self.assertIn("RepeatCount = repeatCount", workspace)
        self.assertIn("WithinSeconds = withinSeconds", workspace)


if __name__ == "__main__":
    unittest.main()

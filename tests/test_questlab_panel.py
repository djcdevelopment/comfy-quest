"""Source guards for Quest Lab's interactive panel ownership and readable grid."""

from __future__ import annotations

import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "network" / "mod" / "ComfyQuestLab"
PANEL = MOD / "Ui" / "LabPanel.cs"
INPUT = MOD / "Core" / "InputGuard.cs"
PATCHES = MOD / "Patches" / "LabPanelInputPatches.cs"
PLUGIN = MOD / "ComfyQuestLab.cs"
PROJECT = MOD / "ComfyQuestLab.csproj"


class QuestLabPanelTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.panel = PANEL.read_text(encoding="utf-8")
        cls.input = INPUT.read_text(encoding="utf-8")
        cls.patches = PATCHES.read_text(encoding="utf-8")
        cls.plugin = PLUGIN.read_text(encoding="utf-8")

    def test_pointer_ownership_is_an_explicit_acquire_release_lifecycle(self) -> None:
        for marker in (
            "AcquirePanelInput()",
            "MaintainPanelInput()",
            "ReleasePanelInput()",
            "_previousCursorLock = Cursor.lockState",
            "_previousCursorVisible = Cursor.visible",
            "Cursor.lockState = CursorLockMode.None",
            "Cursor.visible = true",
            "Cursor.lockState = _previousCursorLock",
            "Cursor.visible = _previousCursorVisible",
            "ZInput.ResetAllButtonStates()",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.input)

        self.assertIn("InputGuard.AcquirePanelInput();", self.panel)
        self.assertIn("InputGuard.ReleasePanelInput();", self.panel)
        self.assertIn("InputGuard.MaintainPanelInput();", self.patches)
        self.assertIn("InputGuard.PanelOwnsInput", self.patches)

    def test_close_keys_release_even_when_the_filter_has_focus(self) -> None:
        panel_open = self.plugin.index(
            "if (_panel.IsOpen) {\n      InputGuard.MaintainPanelInput();"
        )
        close = self.plugin.index("_panel.Close();", panel_open)
        typing_guard = self.plugin.index("InputGuard.ShouldIgnoreKeystrokes()", panel_open)
        self.assertLess(close, typing_guard)
        self.assertIn("Input.GetKeyDown(KeyCode.Escape)", self.plugin[panel_open:typing_guard])
        self.assertIn("LabConfig.PanelShortcut.Value.IsDown()", self.plugin[panel_open:typing_guard])

    def test_panel_uses_an_opaque_high_contrast_surface(self) -> None:
        self.assertIn('SolidTexture("questlab-window"', self.panel)
        self.assertIn("new Color(0.02f, 0.03f, 0.05f, 0.97f)", self.panel)
        self.assertIn("label.fontSize = 14", self.panel)
        self.assertIn("label.normal.textColor", self.panel)

    def test_live_events_are_a_columnar_grid(self) -> None:
        for heading in ("TIME", "SCHOOL", "CREATOR EVENT", "TARGET / DETAIL", "QUEST USE"):
            with self.subTest(heading=heading):
                self.assertIn(f'new GUIContent("{heading}")', self.panel)
        self.assertIn("DrawGridRow(rows[i], i)", self.panel)
        self.assertIn('return "BINDABLE";', self.panel)
        self.assertIn('return "DIAGNOSTIC";', self.panel)

    def test_quests_are_a_school_colored_expandable_grid(self) -> None:
        for heading in ("SCHOOL", "QUEST", "EVENT -> TARGET", "STATE", "FIRES"):
            with self.subTest(heading=heading):
                self.assertIn(f'new GUIContent("{heading}")', self.panel)
        for marker in (
            "DrawQuestGridRow(set.Quests[i], i)",
            "LabRunes.For(category)",
            "LabRunes.ColorFor(category)",
            "QuestStateColor(quest.Armed)",
            'new GUIContent(expanded ? "-" : "+"',
            "DrawQuestDetails(quest, eventName, target, cooldown)",
            'GUILayout.Label("verdict  /  " + quest.ArmedLine())',
            'GUILayout.Toggle(_showQuestFolder,',
            'new GUIContent("Folder", "show or hide the quest directory")',
            '" LOAD ERROR"',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.panel)
        self.assertNotIn("void DrawQuests()", self.panel)

    def test_window_has_a_bounded_drag_resize_handle(self) -> None:
        for marker in (
            "DrawResizeHandle();",
            "GUIUtility.GUIToScreenPoint",
            "_requestedWidth",
            "_requestedHeight",
            "ClampWindow(_window, _drawScale)",
            "Mathf.Min(MinWidth, maxWidth)",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.panel)

    def test_panel_zoom_scales_layout_mouse_and_persists_in_config(self) -> None:
        self.assertIn('"panelScale"', self.plugin)
        for marker in (
            'new GUIContent("-", "zoom out")',
            'new GUIContent("+", "zoom in")',
            '"reset zoom to 100%"',
            "SetPanelScale(1f)",
            "Matrix4x4.Scale(new Vector3(_drawScale, _drawScale, 1f))",
            "(mouse - _resizeStartMouse) / _drawScale",
            "Screen.width / Mathf.Max(MinPanelScale, scale)",
            "LabConfig.PanelScale.Value = scale",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.panel)

    def test_pause_freezes_rows_instead_of_blank_screen(self) -> None:
        for marker in (
            "readonly List<LabEvent> _pausedRows",
            "_pausedRows.AddRange(",
            "new List<LabEvent>(_pausedRows)",
            '" visible row"',
            'new GUIContent("Clear log", "discard retained event rows")',
            'new GUIContent("×", "clear the search")',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.panel)
        self.assertNotIn("? new List<LabEvent>()", self.panel)

    def test_hover_help_and_spellbook_grid_make_clipped_detail_discoverable(self) -> None:
        for marker in (
            "string tooltip = GUI.tooltip;",
            '"Hover for details  ·  F6/Esc close',
            'new GUIContent("WORLD ACTION")',
            'new GUIContent("QUEST USE")',
            'new GUIContent("TRUE NAME", "exact Valheim method")',
            "DrawSpellGridRow(current.Spells[i], i)",
            'return "BINDABLE  /  "',
            'return "DIAGNOSTIC";',
            '"BUILD COVERAGE  /  "',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.panel)

    def test_window_position_and_size_persist_on_close(self) -> None:
        for key in ("panelX", "panelY", "panelWidth", "panelHeight"):
            with self.subTest(key=key):
                self.assertIn(f'"{key}"', self.plugin)
        for marker in (
            "_window = SavedWindow();",
            "SaveWindow();",
            "LabConfig.PanelX.Value",
            "LabConfig.PanelY.Value",
            "LabConfig.PanelWidth.Value",
            "LabConfig.PanelHeight.Value",
            "float.IsNaN(rect.x) || float.IsInfinity(rect.x)",
            "float.IsNaN(rect.width) || float.IsInfinity(rect.width)",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.panel)

    def test_native_panel_does_not_add_a_jotunn_dependency(self) -> None:
        project = PROJECT.read_text(encoding="utf-8")
        self.assertNotIn("Jotunn", project)
        self.assertNotIn("Jötunn", project)


if __name__ == "__main__":
    unittest.main()

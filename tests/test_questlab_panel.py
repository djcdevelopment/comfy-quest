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

    def test_window_has_a_bounded_drag_resize_handle(self) -> None:
        for marker in (
            "DrawResizeHandle();",
            "GUIUtility.GUIToScreenPoint",
            "_requestedWidth",
            "_requestedHeight",
            "ClampWindow(_window)",
            "Mathf.Min(MinWidth, maxWidth)",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.panel)

    def test_native_panel_does_not_add_a_jotunn_dependency(self) -> None:
        project = PROJECT.read_text(encoding="utf-8")
        self.assertNotIn("Jotunn", project)
        self.assertNotIn("Jötunn", project)


if __name__ == "__main__":
    unittest.main()

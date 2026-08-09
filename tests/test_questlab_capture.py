"""Static guards for the Unity-facing half of bounded Quest Lab capture.

The serializer/diff/projection behavior is exercised by the linked .NET tests. These
guards pin the runtime boundary that those Unity-free tests cannot load: capture stays
creator/mark scoped, bounded, read-only, and replay goes through existing marks.
"""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT / "network" / "mod" / "ComfyQuestLab"


class QuestLabCaptureBoundaryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.builder = (MOD / "Core" / "LabBlueprintBuilder.cs").read_text(encoding="utf-8")
        cls.contract = (MOD / "Core" / "LabCaptureContract.cs").read_text(encoding="utf-8")
        cls.plugin = (MOD / "ComfyQuestLab.cs").read_text(encoding="utf-8")

    def test_selection_is_bounded_and_never_arbitrary(self) -> None:
        self.assertIn("public const float MaxRadius = 40f;", self.contract)
        self.assertIn("public const int MaxPieces = 2048;", self.contract)
        self.assertIn('selection != "mine" && selection != "lab"', self.builder)
        self.assertIn('zdo.GetLong("creator", 0L) == playerId', self.builder)
        self.assertIn("LabMarks.IsLabBuilt(zdo)", self.builder)
        self.assertNotIn('selection == "all"', self.builder)

    def test_capture_path_has_no_world_mutation(self) -> None:
        start = self.builder.index("public string Capture(")
        end = self.builder.index("public string Inspect(", start)
        capture = self.builder[start:end]
        for mutation in ("Instantiate(", "DestroyZDO(", ".Destroy(", "ClaimOwnership("):
            self.assertNotIn(mutation, capture)

    def test_replay_uses_existing_durable_mark(self) -> None:
        self.assertIn("view.GetZDO().Set(LabMarks.BlueprintMark, mark);", self.builder)
        self.assertIn("LabCaptureContract.BlueprintMatches", self.builder)
        self.assertIn("refusing to build capture pair", self.builder)

    def test_command_surface_is_fixed_not_remote_console(self) -> None:
        for verb in ("capture", "inspect", "diff", "check", "build", "count", "clear"):
            self.assertIn(f'verb == "{verb}"', self.plugin)
        self.assertNotIn("System.Diagnostics.Process", self.builder)
        self.assertNotIn("Console.ReadLine", self.builder)


if __name__ == "__main__":
    unittest.main()

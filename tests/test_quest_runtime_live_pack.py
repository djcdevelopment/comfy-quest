import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


class QuestRuntimeLivePackTests(unittest.TestCase):
    def test_native_acceptance_pack_is_deterministic(self):
        result = subprocess.run(
            ["python", "tools/quest-runtime/build_live_test_pack.py", "--check"],
            cwd=ROOT,
            capture_output=True,
            text=True,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("omen-inscription-proof-1.6.0.questpack", result.stdout)

    def test_direct_write_mode_is_guarded(self):
        source = (ROOT / "tools/quest-runtime/build_live_test_pack.py").read_text(
            encoding="utf-8"
        )
        wrapper = (ROOT / "tools/quest-runtime/Build-LiveTestPack.ps1").read_text(
            encoding="utf-8"
        )
        self.assertIn("COMFY_QUEST_RUNTIME_PACK_WRITE", source)
        self.assertIn("Assert-RepoIdentity.ps1", wrapper)


if __name__ == "__main__":
    unittest.main()

from __future__ import annotations

import hashlib
import json
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
IMPORTER = ROOT / "tools" / "blueprints" / "import_capture.py"


def fixture() -> dict:
    piece = {
        "Prefab": "wood_floor", "Category": "Building",
        "X": 0, "Y": 0, "Z": 0, "Qx": 0, "Qy": 0, "Qz": 0, "Qw": 1,
        "HasSignText": True, "SignText": "Human spacing",
        "HasItemStand": False, "ItemPrefab": "", "ItemVariant": 0,
        "ItemQuality": 0, "ItemType": 0, "RuneSchool": "combat",
        "RuneStyle": "compact", "TextGlowSchool": "combat",
    }
    signature = "\t".join([
        "wood_floor", "Building", "0", "0", "0", "0", "0", "0", "1",
        "1", "Human spacing", "0", "", "0", "0", "0", "combat", "compact", "combat",
    ])
    return {
        "Schema": "comfy-questlab-capture/v1", "Name": "human-spacing",
        "Selection": "mine", "RadiusMetres": 12, "PieceCount": 1,
        "PiecesSha256": hashlib.sha256(signature.encode()).hexdigest(), "Pieces": [piece],
    }


class GodbuildImportTests(unittest.TestCase):
    def run_import(self, capture: Path, output: Path, *extra: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["python", str(IMPORTER), str(capture), "--output-root", str(output), *extra],
            cwd=ROOT, capture_output=True, text=True, check=False,
        )

    def test_import_is_deterministic_lossless_and_checkable(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "source.capture.json"
            source.write_text(json.dumps(fixture()), encoding="utf-8")
            first = self.run_import(source, root / "out")
            self.assertEqual(0, first.returncode, first.stderr)
            folder = root / "out" / "human-spacing"
            self.assertEqual(
                {"human-spacing.capture.json", "human-spacing.blueprint", "plan.json", "preview.svg", "manifest.json"},
                {path.name for path in folder.iterdir()},
            )
            plan = json.loads((folder / "plan.json").read_text(encoding="utf-8"))
            self.assertEqual("Human spacing", plan["pieces"][0]["metadata"]["sign_text"])
            self.assertEqual("combat", plan["pieces"][0]["metadata"]["rune_school"])
            manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
            self.assertTrue(all(item["silent"] is False for item in manifest["unsupported"]))
            self.assertEqual(0, self.run_import(source, root / "out", "--check").returncode)
            (folder / "plan.json").write_text("{}\n", encoding="utf-8")
            drift = self.run_import(source, root / "out", "--check")
            self.assertNotEqual(0, drift.returncode)
            self.assertIn("plan.json", drift.stderr)

    def test_rejects_unknown_fields_and_a_forged_piece_hash(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            value = fixture()
            value["Pieces"][0]["Scale"] = [2, 2, 2]
            source = root / "bad.capture.json"
            source.write_text(json.dumps(value), encoding="utf-8")
            result = self.run_import(source, root / "out")
            self.assertNotEqual(0, result.returncode)
            self.assertIn("keys differ", result.stderr)
            self.assertFalse((root / "out").exists())


if __name__ == "__main__":
    unittest.main()

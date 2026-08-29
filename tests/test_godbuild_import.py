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

    def test_scar_architectural_source_requires_canonical_derivative(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            value = fixture()
            value["Name"] = "architectural-source"
            value["Selection"] = "architectural-import-candidate"
            value["PiecesSha256"] = "1" * 64
            value["Pieces"][0]["X"] = -0.25
            value["Pieces"][0]["Z"] = 2.0
            second = json.loads(json.dumps(value["Pieces"][0]))
            second.update({
                "Prefab": "wood_beam", "X": 1.75, "Z": -1.0,
                "Qy": 0.7071068, "Qw": 0.7071067,
            })
            value["Pieces"].append(second)
            value["PieceCount"] = 2
            source = root / "architectural.capture.json"
            source.write_text(json.dumps(value), encoding="utf-8")

            rejected = self.run_import(source, root / "rejected")
            self.assertNotEqual(0, rejected.returncode)
            self.assertIn("Selection must be 'mine' or 'lab'", rejected.stderr)
            self.assertFalse((root / "rejected").exists())

            derived = self.run_import(
                source, root / "derived",
                "--derive-architectural-name", "architectural-live",
                "--derive-yaw-degrees", "90",
            )
            self.assertEqual(0, derived.returncode, derived.stderr)
            folder = root / "derived" / "architectural-live"
            capture = folder / "architectural-live.capture.json"
            artifact = json.loads(capture.read_text(encoding="utf-8"))
            self.assertEqual("lab", artifact["Selection"])
            self.assertEqual(2, artifact["PieceCount"])
            self.assertTrue(all(piece[axis] >= 0 for piece in artifact["Pieces"]
                                for axis in ("X", "Y", "Z")))
            half_turn = next(piece for piece in artifact["Pieces"]
                             if piece["Prefab"] == "wood_beam")
            self.assertEqual((0.0, 1.0, 0.0, 0.0), tuple(
                half_turn[key] for key in ("Qx", "Qy", "Qz", "Qw")
            ))
            checked = self.run_import(capture, root / "derived", "--check")
            self.assertEqual(0, checked.returncode, checked.stderr)

            regenerated = self.run_import(capture, root / "regenerated")
            self.assertEqual(0, regenerated.returncode, regenerated.stderr)
            fresh = root / "regenerated" / "architectural-live"
            for name in ("architectural-live.capture.json", "architectural-live.blueprint"):
                self.assertEqual((folder / name).read_bytes(), (fresh / name).read_bytes())

    def test_source_less_bundle_runs_only_the_hash_pinned_authoritative_importer(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            bundle = root / "bundle"
            bundled_importer = bundle / "tools" / "blueprints" / "import_capture.py"
            bundled_importer.parent.mkdir(parents=True)
            payload = IMPORTER.read_bytes()
            bundled_importer.write_bytes(payload)
            manifest = bundle / "standalone-rnd-bundle.json"
            manifest.write_text(json.dumps({
                "schema": "comfy-quest-standalone-rnd-bundle/v1",
                "repository_id": "djcdevelopment/comfy-quest",
                "source_revision": "1" * 40,
                "source_dirty": True,
                "source_tree_sha256": "2" * 64,
                "host_bundle_sha256": "3" * 64,
                "host_bundle_bytes": 1,
                "files": {
                    "tools/blueprints/import_capture.py": {
                        "bytes": len(payload),
                        "sha256": hashlib.sha256(payload).hexdigest(),
                    },
                },
            }), encoding="utf-8")
            source = root / "source.capture.json"
            source.write_text(json.dumps(fixture()), encoding="utf-8")

            result = subprocess.run([
                "python", str(bundled_importer), str(source),
                "--output-root", str(root / "out"),
                "--standalone-bundle-manifest", str(manifest),
            ], cwd=bundle, capture_output=True, text=True, check=False)

            self.assertEqual(0, result.returncode, result.stderr)
            self.assertTrue((root / "out" / "human-spacing" / "manifest.json").is_file())

            bundled_importer.write_bytes(payload + b"\n# drift\n")
            rejected = subprocess.run([
                "python", str(bundled_importer), str(source),
                "--output-root", str(root / "drifted"),
                "--standalone-bundle-manifest", str(manifest),
            ], cwd=bundle, capture_output=True, text=True, check=False)
            self.assertNotEqual(0, rejected.returncode)
            self.assertIn("standalone importer bytes differ", rejected.stderr)
            self.assertFalse((root / "drifted").exists())

    def test_standalone_manifest_rejects_ambient_or_extra_importer_members(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            bundle = root / "bundle"
            bundled_importer = bundle / "tools" / "blueprints" / "import_capture.py"
            bundled_importer.parent.mkdir(parents=True)
            payload = IMPORTER.read_bytes()
            bundled_importer.write_bytes(payload)
            manifest = bundle / "standalone-rnd-bundle.json"
            manifest.write_text(json.dumps({
                "schema": "comfy-quest-standalone-rnd-bundle/v1",
                "repository_id": "djcdevelopment/comfy-quest",
                "source_revision": "1" * 40,
                "source_dirty": False,
                "source_tree_sha256": "2" * 64,
                "host_bundle_sha256": "3" * 64,
                "host_bundle_bytes": 1,
                "files": {
                    "tools/blueprints/import_capture.py": {
                        "bytes": len(payload),
                        "sha256": hashlib.sha256(payload).hexdigest(),
                    },
                    "tools/blueprints/second_importer.py": {
                        "bytes": 1, "sha256": "4" * 64,
                    },
                },
            }), encoding="utf-8")
            source = root / "source.capture.json"
            source.write_text(json.dumps(fixture()), encoding="utf-8")

            rejected = subprocess.run([
                "python", str(bundled_importer), str(source),
                "--output-root", str(root / "out"),
                "--standalone-bundle-manifest", str(manifest),
            ], cwd=bundle, capture_output=True, text=True, check=False)

            self.assertNotEqual(0, rejected.returncode)
            self.assertIn("files must pin only the authoritative importer", rejected.stderr)
            self.assertFalse((root / "out").exists())


if __name__ == "__main__":
    unittest.main()

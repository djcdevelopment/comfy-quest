"""Executable contract tests for Quest Lab Gallery Truth receipts."""

from __future__ import annotations

import copy
import json
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
VERIFIER = REPO / "tools" / "component-packets" / "verify_questlab_truth.py"


def fixture() -> dict:
    return {
        "schema": "comfy-questlab-gallery-truth/v1",
        "pluginRelease": "fixture-r0",
        "generatedAt": "2026-08-09T12:00:00Z",
        "selector": "marble-grand",
        "verdict": "warn",
        "capturePolicy": "read-only; named views are plans; human visual acceptance is authoritative",
        "environment": {"name": "Clear", "wet": "false", "visibleSnow": "human-frame-required"},
        "subjects": [
            {
                "profile": "marble-grand",
                "build": "fixture-build",
                "verdict": "warn",
                "markedObjects": 10,
                "loadedObjects": 8,
                "worldBounds": {
                    "min": [0, 1, 2],
                    "max": [10, 11, 12],
                    "center": [5, 6, 7],
                    "size": [10, 10, 10],
                },
                "roles": {"floor": 5, "roof": 5},
                "weatherExposure": {"loadedFloors": 5, "roofProtectedFloors": 4},
                "ceilingFixtures": [],
                "renderComparisons": [],
                "namedViews": [
                    {"id": name, "purpose": name, "lens": [0, 2, -8], "target": [0, 2, 0], "up": [0, 1, 0], "fieldOfView": 60}
                    for name in ("overview-north", "overview-east", "overhead", "arrival-eye")
                ],
                "assertions": [
                    {"id": name, "verdict": "warn" if name == "floor-weather-protection" else "pass", "detail": name}
                    for name in (
                        "loaded-world-bounds",
                        "floor-weather-protection",
                        "ceiling-fixture-clearance",
                        "fresh-prefab-configuration",
                        "named-view-plan",
                    )
                ],
            }
        ],
    }


class QuestLabTruthVerifierTests(unittest.TestCase):
    def run_verifier(self, payload: dict, *extra: str) -> subprocess.CompletedProcess[str]:
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "truth.json"
            path.write_text(json.dumps(payload), encoding="utf-8")
            return subprocess.run(
                ["python", str(VERIFIER), str(path), *extra],
                cwd=REPO,
                capture_output=True,
                text=True,
                check=False,
            )

    def test_warning_receipt_verifies_without_claiming_visual_acceptance(self) -> None:
        result = self.run_verifier(fixture())
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        summary = json.loads(result.stdout)
        self.assertEqual(summary["verdict"], "warn")
        self.assertTrue(summary["human_visual_acceptance_required"])
        self.assertEqual(summary["named_views"], 4)

    def test_fail_receipt_requires_explicit_shape_only_mode(self) -> None:
        payload = fixture()
        payload["verdict"] = "fail"
        payload["subjects"][0]["assertions"][0]["verdict"] = "fail"
        self.assertNotEqual(self.run_verifier(payload).returncode, 0)
        self.assertEqual(self.run_verifier(payload, "--allow-fail").returncode, 0)

    def test_missing_view_and_fake_snow_claim_fail_closed(self) -> None:
        missing = fixture()
        missing["subjects"][0]["namedViews"].pop()
        self.assertNotEqual(self.run_verifier(missing).returncode, 0)

        fake_snow = copy.deepcopy(fixture())
        fake_snow["environment"]["visibleSnow"] = "none"
        self.assertNotEqual(self.run_verifier(fake_snow).returncode, 0)


if __name__ == "__main__":
    unittest.main()

from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "questlab-pacing" / "questlab_pacing.py"
SPEC = importlib.util.spec_from_file_location("questlab_pacing", SCRIPT)
PACING = importlib.util.module_from_spec(SPEC)
assert SPEC.loader
SPEC.loader.exec_module(PACING)


def iso(at: datetime) -> str:
    return at.isoformat()


def receipt(gap_school: str | None = None, noisy_school: str | None = None) -> dict:
    start = datetime(2026, 8, 9, tzinfo=timezone.utc)
    expectations = []
    witnesses = []
    elapsed = 10
    for index, school in enumerate(PACING.SCHOOL_ORDER):
        if school == gap_school:
            elapsed += 90
        else:
            elapsed += 10
        event = f"{school}_event"
        at = start + timedelta(seconds=elapsed)
        actions = 9 if school == noisy_school else 1
        expectations.append(
            {
                "school": school,
                "event": event,
                "first_witness_utc": iso(at),
                "first_completion_utc": iso(at + timedelta(milliseconds=2)),
                "canonical_action_count": actions,
                "quest_completion_count": actions,
            }
        )
        for _ in range(actions):
            witnesses.append({"school": school, "event": event, "target": "private"})
    return {
        "schema": PACING.SUITE_SCHEMA,
        "suite": "all-schools",
        "evidence_kind": "live-gameplay",
        "release_id": "questlab-test",
        "verdict": "pass",
        "started_utc": iso(start),
        "finished_utc": iso(start + timedelta(seconds=elapsed + 5)),
        "raw_witnesses": len(witnesses) + 3,
        "canonical_actions": len(witnesses),
        "coalesced_witnesses": 3,
        "expectations": expectations,
        "witnesses": witnesses,
    }


class QuestLabPacingTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def write(self, name: str, value: dict) -> Path:
        path = self.root / name
        path.write_text(json.dumps(value), encoding="utf-8")
        return path

    def test_reports_hesitation_noise_and_privacy(self) -> None:
        path = self.write("all-schools-01.json", receipt("crafting", "progression"))
        report = PACING.analyze([path])
        kinds = {(item["kind"], item["school"]) for item in report["aggregate"]["recommendations"]}
        self.assertIn(("navigation-friction", "crafting"), kinds)
        self.assertIn(("noisy-trigger", "progression"), kinds)
        encoded = json.dumps(report)
        self.assertNotIn("private", encoded)
        self.assertFalse(report["privacy"]["targets"])

    def test_aggregates_multiple_runs_and_completion_orders(self) -> None:
        first = self.write("all-schools-01.json", receipt())
        second = self.write("all-schools-02.json", receipt())
        report = PACING.analyze([first, second])
        self.assertEqual(report["runs_analyzed"], 2)
        self.assertEqual(report["aggregate"]["completion_orders"][0]["runs"], 2)
        combat = report["aggregate"]["schools"][0]
        self.assertEqual(combat["median_first_witness_seconds"], 20.0)

    def test_first_gap_is_startup_delay_not_station_friction(self) -> None:
        value = receipt()
        for item in value["expectations"]:
            item["first_witness_utc"] = iso(datetime.fromisoformat(item["first_witness_utc"]) + timedelta(seconds=80))
            item["first_completion_utc"] = iso(datetime.fromisoformat(item["first_completion_utc"]) + timedelta(seconds=80))
        value["finished_utc"] = iso(datetime.fromisoformat(value["finished_utc"]) + timedelta(seconds=80))
        path = self.write("all-schools-01.json", value)
        report = PACING.analyze([path])
        self.assertEqual(report["aggregate"]["startup_delay_runs"], 1)
        self.assertEqual(report["aggregate"]["recommendations"][0]["kind"], "startup-delay")

    def test_directory_input_discovers_only_all_school_receipts(self) -> None:
        self.write("all-schools-01.json", receipt())
        self.write("creator-events-01.json", {"schema": PACING.SUITE_SCHEMA})
        report = PACING.analyze([self.root])
        self.assertEqual(report["runs_analyzed"], 1)

    def test_rejects_synthetic_or_malformed_receipts(self) -> None:
        synthetic = receipt()
        synthetic["suite"] = "creator-events"
        path = self.write("bad.json", synthetic)
        with self.assertRaisesRegex(PACING.PacingError, "only live all-schools"):
            PACING.analyze([path])


if __name__ == "__main__":
    unittest.main()

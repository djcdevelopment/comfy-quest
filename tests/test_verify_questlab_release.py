"""Mutation tests for the Quest Lab live release verifier."""

from __future__ import annotations

import copy
import importlib.util
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "tools" / "component-packets" / "verify_questlab_release.py"
SPEC = importlib.util.spec_from_file_location("verify_questlab_release", SCRIPT)
assert SPEC and SPEC.loader
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)


def expectation(school: str, event: str) -> dict:
    return {
        "school": school,
        "event": event,
        "quest_id": f"questlab_{school}_{event}",
        "instruction": "do it",
        "witnessed": True,
        "quest_completed": True,
        "canonical_action_count": 1,
        "quest_completion_count": 1,
        "first_signature": "Type.Method()",
        "first_target": "sample",
        "first_action_key": f"action:{school}:{event}",
        "first_witness_utc": "2026-08-08T13:00:00Z",
        "first_completion_utc": "2026-08-08T13:00:01Z",
    }


def suite_receipt(suite: str, events: dict[str, str]) -> dict:
    evidence_kind = "synthetic-contract" if suite == "creator-events" else "live-gameplay"
    expectations = [expectation(school, event) for school, event in events.items()]
    return {
        "schema": VERIFIER.SUITE_SCHEMA,
        "run_id": "test-run",
        "suite": suite,
        "suite_name": "test",
        "evidence_kind": evidence_kind,
        "machine": "test-machine",
        "plugin_version": "0.2.0",
        "release_id": "questlab-v0.2.0-test",
        "runtime_profile": "synthetic contract" if suite == "creator-events" else "extended (volatile batch override)",
        "started_utc": "2026-08-08T13:00:00Z",
        "finished_utc": "2026-08-08T13:01:00Z",
        "generated_utc": "2026-08-08T13:01:00Z",
        "state": "complete",
        "verdict": "pass",
        "required_events": len(events),
        "witnessed_events": len(events),
        "completed_example_quests": len(events),
        "raw_witnesses": len(events) + 1,
        "canonical_actions": len(events),
        "coalesced_witnesses": 1,
        "double_completions": 0,
        "unexpected_canonical_actions": 0,
        "expectations": expectations,
        "witnesses": [
            {
                "school": item["school"],
                "event": item["event"],
                "signature": item["first_signature"],
                "target": "sample",
                "action_key": item["first_action_key"],
                "source": evidence_kind,
                "at_utc": item["first_witness_utc"],
                "evaluated": True,
                "raw_witness_count": 1,
            }
            for item in expectations
        ],
    }


class QuestLabReleaseVerifierTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        manifest = VERIFIER.read_json(VERIFIER.CAPABILITY_MANIFEST)
        cls.creator_events = {name: name for name in manifest["CreatorSafeEvents"]}

    def test_valid_creator_and_live_receipts_pass(self) -> None:
        creator = suite_receipt("creator-events", self.creator_events)
        live = suite_receipt("all-schools", VERIFIER.LIVE_EXPECTATIONS)
        self.assertEqual(
            VERIFIER.validate_creator_events(creator, "0.2.0", "questlab-v0.2.0-test"), []
        )
        self.assertEqual(
            VERIFIER.validate_all_schools(live, "0.2.0", "questlab-v0.2.0-test"), []
        )

    def test_missing_catalog_event_and_double_completion_turn_red(self) -> None:
        creator = suite_receipt("creator-events", self.creator_events)
        creator["expectations"].pop()
        creator["double_completions"] = 1
        errors = VERIFIER.validate_creator_events(
            creator, "0.2.0", "questlab-v0.2.0-test"
        )
        self.assertTrue(any("double completion" in error for error in errors))
        self.assertTrue(any("exactly cover" in error for error in errors))

    def test_live_matrix_and_coalescing_are_required(self) -> None:
        live = suite_receipt("all-schools", VERIFIER.LIVE_EXPECTATIONS)
        live["expectations"][0]["event"] = "damage_dealt"
        live["coalesced_witnesses"] = 0
        errors = VERIFIER.validate_all_schools(
            live, "0.2.0", "questlab-v0.2.0-test"
        )
        self.assertTrue(any("eight-school matrix" in error for error in errors))
        self.assertTrue(any("coalescing" in error for error in errors))

    def test_gallery_requires_every_lifecycle_op_and_explicit_human_acceptance(self) -> None:
        receipts = [
            {
                "schema": VERIFIER.REQUEST_SCHEMA,
                "request_id": f"{operation}-1",
                "operation": operation,
                "state": "completed",
                "machine": "test-machine",
                "plugin_version": "0.2.0",
                "release_id": "questlab-v0.2.0-test",
                "detail": (
                    "cleared 20 piece(s) matching 'all'"
                    if operation == "gallery_clear"
                    else "loaded gallery structures: marble-wide 2243"
                    if operation == "gallery_identify"
                    else "gallery marble-wide completed"
                ),
            }
            for operation in VERIFIER.GALLERY_OPERATIONS
        ]
        acceptance = {
            "schema": VERIFIER.ACCEPTANCE_SCHEMA,
            "selected_profile": "marble-wide",
            "accepted_by": "human",
            "accepted_utc": "2026-08-08T13:30:00Z",
            "comparison_request_id": "gallery_compare-1",
            "observations": {name: True for name in VERIFIER.VISUAL_CHECKS},
        }
        errors, selected = VERIFIER.validate_gallery(
            receipts,
            acceptance,
            expected_machine="test-machine",
            expected_version="0.2.0",
            expected_release="questlab-v0.2.0-test",
        )
        self.assertEqual(errors, [])
        self.assertEqual(selected, "marble-wide")

        rejected = copy.deepcopy(acceptance)
        rejected["observations"]["hall_width_acceptable"] = False
        errors, _ = VERIFIER.validate_gallery(
            receipts[:-1],
            rejected,
            expected_machine="test-machine",
            expected_version="0.2.0",
            expected_release="questlab-v0.2.0-test",
        )
        self.assertTrue(any("build, compare, identify, clear, and rebuild" in error for error in errors))
        self.assertTrue(any("hall_width_acceptable" in error for error in errors))

        wrong_lane = copy.deepcopy(receipts)
        wrong_lane[0]["machine"] = "another-machine"
        errors, _ = VERIFIER.validate_gallery(
            wrong_lane,
            acceptance,
            expected_machine="test-machine",
            expected_version="0.2.0",
            expected_release="questlab-v0.2.0-test",
        )
        self.assertTrue(any("different machine" in error for error in errors))

        wrong_release = copy.deepcopy(receipts)
        wrong_release[0]["release_id"] = "questlab-v0.2.0-old"
        errors, _ = VERIFIER.validate_gallery(
            wrong_release,
            acceptance,
            expected_machine="test-machine",
            expected_version="0.2.0",
            expected_release="questlab-v0.2.0-test",
        )
        self.assertTrue(any("release id mismatch" in error for error in errors))

    def test_checked_in_acceptance_template_cannot_accidentally_pass(self) -> None:
        sample = VERIFIER.read_json(
            REPO
            / "tools"
            / "component-packets"
            / "samples"
            / "questlab-gallery-acceptance.sample.json"
        )
        errors, _ = VERIFIER.validate_gallery([], sample)
        self.assertTrue(any("gallery acceptance has no human" in error for error in errors))
        for name in VERIFIER.VISUAL_CHECKS:
            self.assertTrue(any(name in error for error in errors))


if __name__ == "__main__":
    unittest.main()

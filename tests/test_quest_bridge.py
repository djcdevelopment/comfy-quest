"""End-to-end tests for the ADR 0018 quest bridge port (tools/quest-bridge/).

Fixture-driven: a saved EventLog GET /events response stands in for the live
service, which is private-plane only. The live proof (a real in-game completion
through this same path) is QB-1's remaining content and cannot run here.
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PYTHON = sys.executable
TOOLS = ROOT / "tools" / "quest-bridge"
FETCH = TOOLS / "fetch_completions.py"
CONSUMER = TOOLS / "bridge_consumer.py"
INBOX_CLI = TOOLS / "review_inbox.py"
FIXTURE = ROOT / "tests" / "fixtures" / "quest-bridge" / "events-response.json"


def run(*args, expect_rc=0):
    result = subprocess.run(
        [PYTHON, *map(str, args)], capture_output=True, text=True, cwd=str(ROOT)
    )
    if result.returncode != expect_rc:
        raise AssertionError(
            f"rc={result.returncode} (wanted {expect_rc})\n"
            f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}"
        )
    return result


class QuestBridgeEndToEnd(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.inbox = Path(self._tmp.name) / "bridge-inbox"
        self.review = self.inbox / "bridge-review"

    def tearDown(self):
        self._tmp.cleanup()

    def fetch(self):
        return run(FETCH, "--from-file", FIXTURE, "--out", self.inbox)

    def consume(self):
        return run(CONSUMER, self.inbox)

    def submission_ids(self):
        return sorted(p.stem for p in self.inbox.glob("*.json"))

    def test_fetch_writes_one_thin_submission_per_quest_completed_row(self):
        result = self.fetch()
        ids = self.submission_ids()
        self.assertEqual(len(ids), 2, ids)
        # The killing_blow row is not a quest completion and must be skipped.
        self.assertIn("skipped 1 non-quest_completed row(s)", result.stdout)

        neck = next(i for i in ids if "neck-romancer" in i)
        payload = json.loads((self.inbox / f"{neck}.json").read_text(encoding="utf-8"))
        self.assertEqual(payload["schema_version"], 2)
        self.assertEqual(payload["submission_type"], "quest_proof")
        self.assertEqual(payload["quest"]["name"], "Neck Romancer")
        self.assertEqual(payload["player"]["player_id"], "-306950268")
        self.assertEqual(
            payload["evidence"]["eventlog"]["event_id"],
            "8c9f2a1e-4b7d-4f7e-9a55-0e3d6c2b1f00",
        )
        # No evidence envelope in the thin contract (ADR 0018).
        self.assertNotIn("screenshot", json.dumps(payload["evidence"]))
        self.assertNotIn("position", payload)

    def test_fetch_is_deterministic_across_reruns(self):
        self.fetch()
        first = self.submission_ids()
        self.fetch()
        self.assertEqual(first, self.submission_ids())

    def test_consumer_renders_review_with_eventlog_evidence_and_command(self):
        self.fetch()
        self.consume()

        ids = self.submission_ids()
        neck = next(i for i in ids if "neck-romancer" in i)
        review = (self.review / f"{neck}.md").read_text(encoding="utf-8")
        self.assertIn("Quest: Neck Romancer (neck_romancer)", review)
        self.assertIn("killed Neck with Clubs (melee)", review)
        self.assertIn("8c9f2a1e-4b7d-4f7e-9a55-0e3d6c2b1f00", review)
        self.assertIn("/quest submit id:neck_romancer image: participants:", review)

        # A row written before the producer forwarded quest_name falls back to the id.
        boar = next(i for i in ids if "boar-sniper" in i)
        boar_review = (self.review / f"{boar}.md").read_text(encoding="utf-8")
        self.assertIn("Quest: boar_sniper", boar_review)
        self.assertIn("(ranged)", boar_review)

        index = json.loads((self.review / "index.json").read_text(encoding="utf-8"))
        self.assertEqual(index["count"], 2)

    def test_consumer_rejects_schema_one_outbox_payloads(self):
        self.inbox.mkdir(parents=True)
        legacy = (
            ROOT / "recipes" / "quest-submission-bridge" / "bridge-consumer"
            / "mikers-demo" / "outbox" / "20260701-210000-slayer-rank-thrall-demo.json"
        )
        (self.inbox / legacy.name).write_text(
            legacy.read_text(encoding="utf-8"), encoding="utf-8"
        )
        result = run(CONSUMER, self.inbox, expect_rc=1)
        self.assertIn("schema_version must be 2", result.stderr)

    def test_review_workflow_accept_and_export(self):
        self.fetch()
        self.consume()
        neck = next(i for i in self.submission_ids() if "neck-romancer" in i)

        run(INBOX_CLI, self.inbox, "accept", neck)
        run(INBOX_CLI, self.inbox, "export", neck)

        export = (self.review / "export" / f"{neck}.txt").read_text(encoding="utf-8")
        self.assertIn("/quest submit id:neck_romancer image: participants:", export)
        self.assertIn("durable EventLog event 8c9f2a1e-4b7d-4f7e-9a55-0e3d6c2b1f00", export)
        self.assertNotIn("Attach these files", export)

        state = json.loads(
            (self.review / "state" / f"{neck}.json").read_text(encoding="utf-8")
        )
        self.assertEqual(state["status"], "exported")

        events = (self.review / "events.jsonl").read_text(encoding="utf-8").strip().splitlines()
        self.assertEqual(len(events), 2)  # accept + export

    def test_reconsume_preserves_review_state(self):
        self.fetch()
        self.consume()
        neck = next(i for i in self.submission_ids() if "neck-romancer" in i)
        run(INBOX_CLI, self.inbox, "accept", neck)

        self.fetch()
        self.consume()
        state = json.loads(
            (self.review / "state" / f"{neck}.json").read_text(encoding="utf-8")
        )
        self.assertEqual(state["status"], "accepted")


if __name__ == "__main__":
    unittest.main()

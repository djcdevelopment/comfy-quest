import copy
import importlib.util
import json
import re
import unittest
from html.parser import HTMLParser
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
RENDERER = REPO / "tools" / "render_quest_mission_control.py"
OUTPUT = REPO / "docs" / "quest-mission-control.html"


def load_renderer():
    spec = importlib.util.spec_from_file_location("quest_mission_control_renderer", RENDERER)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class SurfaceParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids = set()
        self.duplicate_ids = set()
        self.labels_for = set()
        self.checkbox_ids = set()
        self.check_ids = []
        self.script_sources = []
        self.stylesheet_links = []
        self.hrefs = []
        self.buttons_without_type = 0
        self.main_count = 0
        self.h1_count = 0
        self.lang = None

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        element_id = values.get("id")
        if element_id:
            if element_id in self.ids:
                self.duplicate_ids.add(element_id)
            self.ids.add(element_id)
        if tag == "html":
            self.lang = values.get("lang")
        elif tag == "main":
            self.main_count += 1
        elif tag == "h1":
            self.h1_count += 1
        elif tag == "label" and values.get("for"):
            self.labels_for.add(values["for"])
        elif tag == "input" and values.get("type") == "checkbox":
            if element_id:
                self.checkbox_ids.add(element_id)
            if values.get("data-check-id"):
                self.check_ids.append(values["data-check-id"])
        elif tag == "script" and values.get("src"):
            self.script_sources.append(values["src"])
        elif tag == "link" and values.get("rel") == "stylesheet":
            self.stylesheet_links.append(values.get("href"))
        elif tag == "a" and values.get("href"):
            self.hrefs.append(values["href"])
        elif tag == "button" and values.get("type") != "button":
            self.buttons_without_type += 1


class QuestMissionControlTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.renderer = load_renderer()
        cls.manifest = cls.renderer.load_manifest()
        cls.rendered = cls.renderer.render(cls.manifest)
        cls.committed = OUTPUT.read_text(encoding="utf-8")

    def test_generated_page_is_current(self):
        self.assertEqual(self.rendered, self.committed)
        self.assertIn('meta name="quest-mission-control-schema"', self.committed)
        self.assertIn(
            f"Program reconciled through {self.manifest['page']['program_commit']}",
            self.committed,
        )
        self.assertIn("Automate the machine loop. Spend the seat on design.", self.committed)
        self.assertIn("Safe-top overhead bar verified live", self.committed)
        self.assertIn("First human-spaced module captured", self.committed)
        self.assertIn("Guild dogfooding is the adoption path.", self.committed)
        self.assertIn("Portfolio requirements", self.committed)
        self.assertIn("complete", self.renderer.ALLOWED_QUEUE_STATES)

    def test_manifest_rejects_duplicate_ids_and_stale_source_pins(self):
        duplicate = copy.deepcopy(self.manifest)
        duplicate["machines"][1]["id"] = duplicate["machines"][0]["id"]
        with self.assertRaisesRegex(self.renderer.MissionControlError, "duplicate stable id"):
            self.renderer.validate_manifest(duplicate)

        stale = copy.deepcopy(self.manifest)
        stale["queue"][0]["source_contains"] = "a phrase the handoff cannot contain"
        with self.assertRaisesRegex(self.renderer.MissionControlError, "source pin is stale"):
            self.renderer.validate_manifest(stale)

    def test_human_sequences_are_derived_from_authoritative_sections(self):
        creator = self.manifest["recovery"]["creator_flow"]
        creator_steps = self.renderer.list_items(
            self.renderer.markdown_section(self.renderer.source_path(creator["source"]), creator["heading"]),
            ordered=True,
        )
        self.assertEqual(6, len(creator_steps))
        self.assertTrue(creator_steps[0].startswith("With Valheim closed"))
        self.assertIn("one bounded operation", creator_steps[3])
        self.assertIn("fails unless the receipt says `MATCH`", creator_steps[5])

        revision = self.manifest["recovery"]["revision_flow"]
        revision_steps = revision["source_sequence"].split(" -> ")
        self.assertEqual(
            [
                "Imagine",
                "Author in the world and Studio",
                "Rehearse",
                "Play",
                "Observe",
                "Revise",
                "Reset",
                "Run again",
                "Release",
            ],
            revision_steps,
        )
        self.assertIn(revision["source_sequence"], (REPO / revision["source"]).read_text(encoding="utf-8"))
        self.assertIn("recovery.revision.9", self.committed)

        phase3 = self.manifest["phase3_lap"]
        source = self.renderer.source_path(phase3["source"])
        sequence = self.renderer.list_items(
            self.renderer.markdown_section(source, phase3["sequence_heading"]), ordered=True
        )
        verdicts = self.renderer.list_items(
            self.renderer.markdown_section(source, phase3["verdicts_heading"]), ordered=False
        )
        self.assertEqual(5, len(sequence))
        self.assertEqual(3, len(verdicts))
        self.assertIn("Press F10", sequence[1])
        plain_cast_step = " ".join(re.sub(r"[*`]", "", sequence[2]).split())
        self.assertIn("Press once more to CAST", plain_cast_step)
        self.assertIn("press <code>`</code> once", self.committed)
        self.assertIn("Press <code>`</code> once more", self.committed)

    def test_expected_receipts_come_from_creator_os_contract(self):
        path = self.renderer.source_path(self.manifest["recovery"]["expectations"])
        receipts = self.renderer.evidence_receipts(path)
        operations = [(item["operation"], item["status"]) for item in receipts]
        self.assertEqual(("creator_session_prepare", "active"), operations[0])
        self.assertIn(("blueprint_capture", "completed"), operations)
        self.assertIn(("blueprint_diff", "completed"), operations)
        self.assertIn(("runtime_build_on", "completed"), operations)
        self.assertIn(("runtime_arm", "completed"), operations)
        self.assertIn(("gallery_identify", "completed"), operations)
        self.assertIn(("runtime_build_off", "completed"), operations)
        self.assertIn(("runtime_disarm", "completed"), operations)
        later = self.renderer.later_revision_expectation(path)
        self.assertTrue(later["capture_source_hash_is_preserved"])
        self.assertEqual(
            {"operation": "blueprint_diff", "status": "completed"}, later["changed_content_receipt"]
        )
        live = json.loads(path.read_text(encoding="utf-8"))["observed_live_godbuild"]
        self.assertTrue(live["build_on"]["creator_build_enabled"])
        self.assertFalse(live["build_off"]["creator_build_enabled"])
        self.assertEqual(12, live["capture"]["piece_count"])
        self.assertEqual(
            "e1e01ff675017bc6ed1d83868b3dcd5f9bb9a2f84089721dfa032fb79c537dfd",
            live["capture"]["source_pieces_sha256"],
        )
        self.assertTrue(live["close"]["prior_install_bytes_restored"])
        self.assertFalse(live["close"]["world_restore_performed"])

    def test_page_is_standalone_and_accessible_by_structure(self):
        parser = SurfaceParser()
        parser.feed(self.committed)
        self.assertEqual("en", parser.lang)
        self.assertEqual(1, parser.main_count)
        self.assertEqual(1, parser.h1_count)
        self.assertFalse(parser.duplicate_ids)
        self.assertFalse(parser.script_sources)
        self.assertFalse(parser.stylesheet_links)
        self.assertEqual(0, parser.buttons_without_type)
        self.assertTrue(parser.checkbox_ids.issubset(parser.labels_for))
        self.assertEqual(len(parser.check_ids), len(set(parser.check_ids)))
        self.assertEqual(19, len(parser.check_ids))
        self.assertEqual(16, len([item for item in parser.check_ids if item.startswith("recovery.")]))
        for href in parser.hrefs:
            if href.startswith("#"):
                self.assertIn(href[1:], parser.ids)
            elif not href.startswith(("http://", "https://")):
                self.assertTrue((OUTPUT.parent / href).resolve().is_file(), href)
        self.assertIn('aria-live="polite"', self.committed)
        self.assertIn('role="progressbar"', self.committed)
        self.assertIn('class="skip-link"', self.committed)

    def test_session_state_is_bounded_local_and_exportable(self):
        for expected in (
            "comfy-quest-mission-control-session/v2",
            "localStorage.setItem",
            "localStorage.getItem",
            "file.size>262144",
            "notes:bounded(input.notes,20000)",
            "Session JSON exported",
            "Session imported and saved locally",
            "Local session cleared",
            'confirm("Clear this browser\'s Creator OS',
            "seat_verdict",
            'id="lane-verdict"',
        ):
            self.assertIn(expected, self.committed)
        for forbidden in ("fetch(", "XMLHttpRequest", "WebSocket", "sendBeacon"):
            self.assertNotIn(forbidden, self.committed)

    def test_readme_and_agent_rule_keep_the_page_in_the_workflow(self):
        readme = (REPO / "README.md").read_text(encoding="utf-8")
        agents = (REPO / "AGENTS.md").read_text(encoding="utf-8")
        self.assertIn("docs/quest-mission-control.html", readme)
        self.assertIn("python tools/render_quest_mission_control.py --check", readme)
        self.assertIn("docs/quest-mission-control.json", agents)
        self.assertRegex(agents, re.compile(r"Before committing, check whether the change alters"))
        renderer = RENDERER.read_text(encoding="utf-8")
        self.assertIn("Assert-RepoIdentity.ps1", renderer)
        self.assertLess(renderer.index("assert_repo_identity()", renderer.index("def main")), renderer.index("output.write_text"))

    def test_manifest_is_canonical_json_with_lf(self):
        raw = (REPO / "docs" / "quest-mission-control.json").read_bytes()
        self.assertNotIn(b"\r\n", raw)
        parsed = json.loads(raw.decode("utf-8"))
        self.assertEqual(self.renderer.SCHEMA, parsed["schema"])


if __name__ == "__main__":
    unittest.main()

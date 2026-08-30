import copy
import importlib.util
import json
import re
import tempfile
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
        self.assertIn("Build it. Drive it. Then call the seat.", self.committed)
        self.assertIn("Results through the whole vertical slice", self.committed)
        self.assertIn("Integration evidence dominates", self.committed)
        self.assertIn("Ready before the seat", self.committed)
        self.assertIn("Prove the standalone Workbench boundary", self.committed)
        self.assertIn("Package authored saved worlds", self.committed)
        self.assertIn("Safe-top overhead bar verified live", self.committed)
        self.assertIn("First human-spaced module captured", self.committed)
        self.assertIn("Guild dogfooding is the adoption path.", self.committed)
        # The page carries the declared authority chain rather than its own hand-kept links.
        self.assertIn("Authority reading order", self.committed)
        for item in self.manifest["reading_order"]:
            self.assertIn(item["role"], self.committed)
            self.assertIn(item["source"], self.committed)
        self.assertIn("complete", self.renderer.ALLOWED_QUEUE_STATES)
        self.assertIn("implemented", self.renderer.ALLOWED_QUEUE_STATES)
        self.assertIn("deferred", self.renderer.ALLOWED_QUEUE_STATES)

    def test_manifest_rejects_duplicate_ids_and_stale_source_pins(self):
        duplicate = copy.deepcopy(self.manifest)
        duplicate["machines"][1]["id"] = duplicate["machines"][0]["id"]
        with self.assertRaisesRegex(self.renderer.MissionControlError, "duplicate stable id"):
            self.renderer.validate_manifest(duplicate)

        duplicate_strategy = copy.deepcopy(self.manifest)
        duplicate_strategy["strategy"]["principles"][1]["id"] = duplicate_strategy["strategy"]["principles"][0]["id"]
        with self.assertRaisesRegex(self.renderer.MissionControlError, "duplicate stable id"):
            self.renderer.validate_manifest(duplicate_strategy)

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
                "Steward configures",
                "Creator instantiates",
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

    def test_a_stale_lap_cannot_be_presented_as_a_seat_sequence(self):
        # Audit B7. The runbook still parses into the exact shape the old gate checked --
        # five ordered steps, three verdicts -- while aiming through an F9 drawer ADR 0008
        # removed. Shape is not freshness, so the page refuses it on recorded state.
        phase3 = self.manifest["phase3_lap"]
        self.assertEqual("stale", phase3["state"])
        runbook = self.renderer.source_path(phase3["source"]).read_text(encoding="utf-8")[:2000]
        self.assertIn("STALE", runbook)
        self.assertIn("Do not run it", runbook)

        self.assertIn("stale, do not run it", self.committed)
        self.assertIn("controls that no longer exist", self.committed)
        self.assertIn("Before it is run:", self.committed)
        for gone in (
            "press <code>`</code> once",
            "Press <code>`</code> once more",
            "Press F10",
            '<ol class="derived-sequence">',
            "Exactly three human verdicts",
        ):
            self.assertNotIn(gone, self.committed)

        # The queue card is the other surface that could read as seat-ready.
        card = next(item for item in self.manifest["queue"] if item["id"] == "queue.phase3-lap")
        self.assertEqual("gated", card["state"])
        self.assertIn("stale", card["detail"])

        # A lap with no recorded freshness is refused outright, and a seat-ready one still
        # renders its sequence -- so this is a real branch, not a permanently dark one.
        unstated = copy.deepcopy(self.manifest)
        del unstated["phase3_lap"]["state"]
        with self.assertRaisesRegex(self.renderer.MissionControlError, "phase3_lap.state"):
            self.renderer.validate_manifest(unstated)
        fresh = copy.deepcopy(self.manifest)
        fresh["phase3_lap"]["state"] = "seat-ready"
        self.assertIn('<ol class="derived-sequence">', self.renderer.render(fresh))

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
        self.assertEqual(20, len(parser.check_ids))
        self.assertEqual(17, len([item for item in parser.check_ids if item.startswith("recovery.")]))
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

class RoadmapSurfaceTests(unittest.TestCase):
    """The mechanical repairs from the 2026-08-24 audit that are not the invariant."""

    @classmethod
    def setUpClass(cls):
        cls.renderer = load_renderer()
        cls.manifest = cls.renderer.load_manifest()
        cls.committed = OUTPUT.read_text(encoding="utf-8")

    def test_the_4a_exit_agrees_with_the_recorded_human_boundary(self):
        # Audit item 1 / ADR 0014. The exit used to require automation that "launches and
        # closes the installed game through the standalone harness"; nothing here can do that.
        requirements = (REPO / "docs" / "creator-portfolio-requirements.md").read_text(
            encoding="utf-8"
        )
        section = requirements[requirements.index("### 4A") : requirements.index("### 4B")]
        self.assertNotIn("standalone harness", section)
        self.assertIn("Exactly one human action is permitted", section)
        self.assertIn("launching the game and entering the", section)
        self.assertIn("0014-one-human-launch-and-entry-is-the-baseline.md", section)
        # Strictly launch and entry, not a generic one-intervention budget.
        self.assertIn("not a generic", section)
        for excluded in ("relaying a console command", "retrying a failed mechanical step"):
            self.assertIn(excluded, section)

        phases = json.loads((REPO / "docs" / "creator-os-phases.json").read_text(encoding="utf-8"))
        self.assertTrue(phases["human_boundary"]["scope_is_strict"])
        self.assertNotIn("standalone harness", phases["lanes"][0]["exit"])

    def test_the_authority_reading_order_cannot_go_stale(self):
        # The reason this exists: handoff-2026-08-20.md went stale while README.md and
        # creator-os.md still pointed a cold reader at it. Pointers are pinned now.
        declared = [item["source"] for item in self.manifest["reading_order"]]
        self.assertEqual(list(range(1, len(declared) + 1)), [item["position"] for item in self.manifest["reading_order"]])
        self.assertEqual(len(declared), len(set(declared)))
        self.assertEqual("docs/PLAN.md", declared[0])
        for source in declared:
            with self.subTest(source=source):
                self.assertTrue((REPO / source).is_file(), source)

        # Both advertisements must match the declaration, in order.
        for relative in self.renderer.POINTER_FILES:
            with self.subTest(pointer=relative):
                advertised = self.renderer.pointer_block(REPO / relative)
                self.assertEqual(declared, [source for _, source in advertised])

        # Adding an authority without updating the pointers fails ...
        added = copy.deepcopy(self.manifest)
        added["reading_order"].append(
            {
                "id": "reading.extra",
                "position": len(declared) + 1,
                "role": "Extra",
                "detail": "An authority nobody advertised.",
                "source": "docs/creator-os-audit-2026-08-24.md",
                "source_contains": "## Punch list",
            }
        )
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"advertises a stale reading order"
        ):
            self.renderer.validate_manifest(added)

        # ... and so does reordering it, because read order is the point.
        swapped = copy.deepcopy(self.manifest)
        swapped["reading_order"][0], swapped["reading_order"][1] = (
            swapped["reading_order"][1],
            swapped["reading_order"][0],
        )
        with self.assertRaisesRegex(self.renderer.MissionControlError, r"position"):
            self.renderer.validate_manifest(swapped)

        # A renamed or moved authority fails on its own source pin.
        moved = copy.deepcopy(self.manifest)
        moved["reading_order"][1]["source_contains"] = "a heading the plan cannot contain"
        with self.assertRaisesRegex(self.renderer.MissionControlError, r"source pin is stale"):
            self.renderer.validate_manifest(moved)

    def test_source_intents_are_immutable_and_every_projection_matches(self):
        authority = self.manifest["source_intents"]
        self.assertEqual("djcdevelopment/baseline", authority["repository"])
        self.assertRegex(authority["revision"], r"^[0-9a-f]{40}$")
        self.assertEqual(["01", "02", "03", "04", "05"], [item["id"] for item in authority["documents"]])
        self.renderer.validate_source_intents(self.manifest)
        self.renderer.validate_projections(self.manifest)

        moving = copy.deepcopy(self.manifest)
        moving["source_intents"]["revision"] = "main"
        with self.assertRaisesRegex(self.renderer.MissionControlError, "40-character SHA"):
            self.renderer.validate_manifest(moving)

        wrong_path = copy.deepcopy(self.manifest)
        wrong_path["source_intents"]["documents"][0]["path"] = "docs/arch/moved.md"
        with self.assertRaisesRegex(self.renderer.MissionControlError, "path must be"):
            self.renderer.validate_manifest(wrong_path)

        wrong_hash = copy.deepcopy(self.manifest)
        wrong_hash["source_intents"]["documents"][0]["sha256"] = "not-a-hash"
        with self.assertRaisesRegex(self.renderer.MissionControlError, "lowercase SHA-256"):
            self.renderer.validate_manifest(wrong_hash)

    def test_architectural_build_journey_is_rendered_and_hash_pinned(self):
        attack = self.manifest["next_attack"]
        self.assertEqual("demo-ready-am4-warm", attack["status"])
        self.assertEqual("tn0304", attack["fixture"])
        self.assertEqual(11, len(attack["journey"]))
        workbook = attack["workbook"]
        self.assertEqual("active-rnd", workbook["status"])
        self.assertEqual("creator-os-composition-workbook/v1", workbook["schema"])
        self.assertEqual("creator-os-composition-review/v1", workbook["review_schema"])
        self.assertTrue((REPO / workbook["source"]).is_file())
        self.assertTrue((REPO / workbook["html"]).is_file())
        receipt_path = REPO / attack["evidence"]["source"]
        self.assertTrue(receipt_path.is_file())
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        self.assertEqual("passed", receipt["result"])
        self.assertEqual("active-warm", receipt["state"])
        self.assertEqual("created", receipt["first_lap"]["build_action"])
        self.assertEqual("reused", receipt["second_lap"]["build_action"])
        self.assertEqual(
            ["status", "blueprint_check", "blueprint_count", "blueprint_diff", "status"],
            receipt["second_lap"]["operations"],
        )
        self.assertTrue(receipt["warm_state"]["valheim_running"])
        self.assertEqual(40, receipt["warm_state"]["marked_pieces_retained"])
        self.assertFalse(receipt["warm_state"]["creator_build_enabled"])
        self.assertEqual("retained-not-applied", receipt["rollback_snapshot"]["state"])
        demo_path = REPO / attack["evidence"]["demo_source"]
        self.assertTrue(demo_path.is_file())
        demo = json.loads(demo_path.read_text(encoding="utf-8"))
        self.assertEqual("operator-ready-warm", demo["state"])
        self.assertTrue(demo["operator_demo"]["canonical_stage_already_present"])
        self.assertTrue(demo["operator_demo"]["remote_studio_reused"])
        self.assertTrue(demo["operator_demo"]["ssh_tunnel_reused"])
        self.assertEqual(
            ["status", "blueprint_check", "blueprint_count", "blueprint_diff", "status"],
            demo["operator_demo"]["operations"],
        )
        for marker in (
            "Architectural capsule",
            "reusable operator demo",
            "Demo-ready on AM4",
            "Active ruthless slice",
            "Open the living composition workbook",
            "community evidence",
            "built once",
            "7.953375",
            "43.907838",
            "0.029171",
            "f509aa2a201fdb3495c0f8aa3656ca156421524b476d12d4f0d45aa3cd9a21e9",
            "5d466cdaa5a213ef958d07636325b9398eee9a74584019da8dc16a5603654251",
            "02201382e57635f4e945229836443d2fdbf75e243973281d1f9806cd770ece5f",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.committed)

        changed = copy.deepcopy(self.manifest)
        changed["next_attack"]["evidence"]["capture_sha256"] = "0" * 64
        with self.assertRaisesRegex(self.renderer.MissionControlError, "capture hash disagrees"):
            self.renderer.validate_manifest(changed)

        drifted = copy.deepcopy(self.manifest)
        drifted["next_attack"]["evidence"]["reuse_diff_receipt_sha256"] = "0" * 64
        with self.assertRaisesRegex(self.renderer.MissionControlError, "reuse lap disagrees"):
            self.renderer.validate_manifest(drifted)

        workbook_drifted = copy.deepcopy(self.manifest)
        workbook_drifted["next_attack"]["workbook"]["source_sha256"] = "0" * 64
        with self.assertRaisesRegex(self.renderer.MissionControlError, "workbook source hash disagrees"):
            self.renderer.validate_manifest(workbook_drifted)

    def test_every_projection_marker_is_registered(self):
        self.renderer.validate_projection_registration()
        original = self.renderer.POINTER_FILES
        try:
            self.renderer.POINTER_FILES = original + ("docs/working-agreements.md",)
            with self.assertRaisesRegex(self.renderer.MissionControlError, "projection marker"):
                self.renderer.validate_projection_registration()
        finally:
            self.renderer.POINTER_FILES = original

    def test_4a_journey_is_the_full_a_b_a_contract(self):
        journey = self.renderer.load_4a_journey()
        self.assertEqual(list(self.renderer.JOURNEY_STEP_IDS), [item["id"] for item in journey["steps"]])
        self.assertEqual(
            list(self.renderer.JOURNEY_EVIDENCE_IDS),
            [item["id"] for item in journey["evidence"]],
        )
        projected = self.renderer.fenced_body(
            REPO / "docs" / "PLAN.md",
            self.renderer.JOURNEY_BEGIN,
            self.renderer.JOURNEY_END,
        )
        self.assertEqual(self.renderer.journey_projection(journey), projected)
        for step_id in self.renderer.JOURNEY_STEP_IDS:
            with self.subTest(step=step_id):
                broken = copy.deepcopy(journey)
                broken["steps"] = [item for item in broken["steps"] if item["id"] != step_id]
                with self.assertRaisesRegex(self.renderer.MissionControlError, "ordered sequence"):
                    self.renderer.validate_acceptance_journey(broken)

    def test_guardrails_are_six_product_plus_one_communication(self):
        self.renderer.validate_guardrail_taxonomy()
        plan = (REPO / "docs" / "five-intent-program-plan.md").read_text(encoding="utf-8")
        self.assertIn("### Six product guardrails", plan)
        self.assertIn("### Communication guardrail", plan)

    def test_the_reading_order_answers_why_before_what(self):
        # The chain shipped with nine authorities and not one of them said why any of it
        # exists: a cold reader following it exactly learned the plan, the lanes, the ledger
        # and the queue, and never the thing they serve. The ethos is position 2 now, and
        # pinned on its own thesis so deleting that sentence fails the gate rather than
        # quietly leaving the program without a stated purpose.
        order = self.manifest["reading_order"]
        plan = order[0]
        self.assertEqual("docs/PLAN.md", plan["source"])
        self.assertEqual(
            "Design for composition, not for impressive primitives", plan["source_contains"])
        ethos = next(item for item in order if item["source"] == "docs/five-intent-program-plan.md")
        self.assertEqual("the machine absorbs the complexity", ethos["source_contains"])
        self.assertIn("Why", ethos["role"])
        # It precedes every what-and-what-next authority in the chain.
        positions = {item["source"]: item["position"] for item in order}
        for later in (
            "docs/creator-os-build-strategy.md",
            "docs/creator-os-phases.json",
            "docs/creator-requirements-ledger.json",
            "docs/quest-mission-control.json",
        ):
            with self.subTest(after=later):
                self.assertLess(ethos["position"], positions[later])

    def test_every_lane_states_the_question_it_answers(self):
        # Both fields were in the vocabulary from the start and rendered nowhere.
        lanes = json.loads((REPO / "docs" / "creator-os-phases.json").read_text(encoding="utf-8"))
        for lane in lanes["lanes"]:
            with self.subTest(lane=lane["id"]):
                self.assertTrue(lane["question"].strip().endswith("?"))
                self.assertTrue(lane["failure_mode"].strip())
                self.assertIn(lane["question"], self.committed)
                self.assertIn(lane["failure_mode"], self.committed)

    def test_a_complete_lane_pins_repository_evidence(self):
        vocabulary = json.loads(
            (REPO / "docs" / "creator-os-phases.json").read_text(encoding="utf-8")
        )
        complete = [lane for lane in vocabulary["lanes"] if lane["state"] == "complete"]
        self.assertTrue(complete)
        for lane in complete:
            with self.subTest(lane=lane["id"]):
                self.assertTrue(lane["completed_on"])
                self.assertTrue((REPO / lane["completion_evidence"]).is_file())

        stale = copy.deepcopy(vocabulary)
        completed = next(lane for lane in stale["lanes"] if lane["state"] == "complete")
        completed["completion_evidence"] = "docs/evidence/there-is-no-such-lap.json"
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"source does not exist"
        ):
            with tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "creator-os-phases.json"
                path.write_text(json.dumps(stale), encoding="utf-8")
                self.renderer.load_lane_vocabulary(path)

    def test_the_handoff_names_what_this_repository_does_not_hold(self):
        # The five source design intents live in the baseline repository. A reader who assumes
        # they are here reconstructs them from the plan, which cites and does not restate them.
        handoff = (REPO / "docs" / "handoff-2026-08-24.md").read_text(encoding="utf-8")
        self.assertIn("What this repository does not hold", handoff)
        self.assertIn(self.manifest["source_intents"]["revision"], handoff)
        for document in self.manifest["source_intents"]["documents"]:
            self.assertIn(document["path"], handoff)
        # And it tells a cold agent to check its checkout before trusting any of it.
        self.assertIn("check your checkout", handoff.lower())

    def test_the_superseded_handoff_points_at_the_current_one(self):
        old = (REPO / "docs" / "handoff-2026-08-20.md").read_text(encoding="utf-8")[:2000]
        self.assertIn("HISTORICAL", old)
        self.assertIn("docs/handoff-2026-08-24.md", old)

    def test_the_handoff_lists_every_open_ruling(self):
        # The handoff names the parked ids so it cannot quietly fall behind the ledger.
        handoff = (REPO / "docs" / "handoff-2026-08-24.md").read_text(encoding="utf-8")
        ledger = self.renderer.load_requirements_ledger()
        parked = sorted(
            item["id"] for item in ledger["requirements"] if item["disposition"] == "parked"
        )
        self.assertTrue(parked)
        for requirement_id in parked:
            with self.subTest(requirement=requirement_id):
                self.assertIn(requirement_id, handoff)
        # And it names everything executable, which is the question a cold start asks first.
        for item in self.manifest["queue"]:
            if item["state"] == "ready":
                with self.subTest(item=item["id"]):
                    self.assertIn(item["id"], handoff)

    def test_the_installed_evidence_caution_matches_the_environment_entry(self):
        # The caution may not send a reader back into machine work after the installed gate passed.
        caution = next(
            value
            for value in self.manifest["cautions"]
            if "installed 4A journey is proven" in value
        )
        environment = self.manifest["environment"][4]
        self.assertEqual("confirmed", environment["state"])
        self.assertIn("queue-full-width-journey-20260827-r9", environment["detail"])
        self.assertIn("installed 4A journey is proven", caution)
        self.assertIn("mechanical defects were fixed and rerun", caution)
        self.assertNotIn("What is missing", caution)
        self.assertNotIn("stay unpromoted", caution)
        for operation in ("deployment", "lifecycle", "binding", "reset", "retention", "recovery"):
            self.assertIn(operation, caution)

    def test_the_command_reference_lists_every_creator_session_verb(self):
        # Audit B9: Arm, Disarm, and GalleryRebuild were missing from the reference while the
        # page's own proof chain asserted runtime_arm and runtime_disarm.
        script = (REPO / "tools" / "creator-session" / "Invoke-CreatorSession.ps1").read_text(
            encoding="utf-8"
        )
        declared = re.search(r"\[ValidateSet\(([^)]*)\)\]", script).group(1)
        verbs = set(re.findall(r"'([A-Za-z]+)'", declared))
        self.assertEqual(12, len(verbs))
        listed = {
            verb
            for verb in verbs
            for command in self.manifest["commands"]
            if f"Invoke-CreatorSession.ps1 {verb}" in command["command"]
        }
        self.assertEqual(verbs, listed)
        for verb in ("Arm", "Disarm", "GalleryRebuild", "Launch", "Stop"):
            self.assertIn(verb, listed)

    def test_the_post_render_replacement_table_must_match(self):
        # Audit A3: the table was live at 1fef480 and one ordinary template edit disconnected
        # four of its keys, because nothing asserted a replacement fired.
        source = RENDERER.read_text(encoding="utf-8")
        for dead in (
            "Now · recovery acceptance",
            "Cold-load first. Create second.",
            "Checkpoint A · immutable world judgment",
            "Keep the canonical build unchanged",
            "Last handoff",
            'href="handoff-2026-08-20.md"',
        ):
            self.assertNotIn(dead, source)
        # Audit A4: the table runs over manifest content, so a generic key can start matching
        # text nobody meant to rewrite. That now fails too.
        colliding = copy.deepcopy(self.manifest)
        colliding["queue"][0]["detail"] += " Project source."
        with self.assertRaisesRegex(self.renderer.MissionControlError, "matched 2 times"):
            self.renderer.render(colliding)

    def test_machine_and_environment_states_are_validated(self):
        # Audit A6: badge() fell back to state.title() and emitted an unstyled badge.
        self.assertEqual({"ready", "online", "on-demand"}, self.renderer.ALLOWED_MACHINE_STATES)
        for group, allowed in (
            ("machines", self.renderer.ALLOWED_MACHINE_STATES),
            ("environment", self.renderer.ALLOWED_ENVIRONMENT_STATES),
        ):
            with self.subTest(group=group):
                for item in self.manifest[group]:
                    self.assertIn(item["state"], allowed)
                broken = copy.deepcopy(self.manifest)
                broken[group][0]["state"] = "provisional"
                with self.assertRaisesRegex(
                    self.renderer.MissionControlError, f"invalid {group} state"
                ):
                    self.renderer.validate_manifest(broken)
        stylesheet = RENDERER.read_text(encoding="utf-8")
        for state in self.renderer.ALLOWED_ENVIRONMENT_STATES:
            self.assertIn(f".status-{state}", stylesheet)


class ProgramInvariantTests(unittest.TestCase):
    """No executable roadmap item without lineage; no active requirement without a disposition.

    Audit C6 is the origin: work items carried no `lane` or `requirements` field, so the
    validator could not detect a work item belonging to no lane (C4) or a requirement no work
    item claimed (C2). Each of the five failures below has a test that proves it fires; a
    check that cannot fail is decoration.
    """

    @classmethod
    def setUpClass(cls):
        cls.renderer = load_renderer()
        cls.manifest = cls.renderer.load_manifest()
        cls.ledger = cls.renderer.load_requirements_ledger()
        cls.lanes = cls.renderer.load_lane_vocabulary()
        cls.document_ids = cls.renderer.requirement_ids()

    def check(self, *, manifest=None, ledger=None, lanes=None, document_ids=None):
        self.renderer.validate_program_invariant(
            copy.deepcopy(self.manifest if manifest is None else manifest),
            ledger=copy.deepcopy(self.ledger if ledger is None else ledger),
            lanes=copy.deepcopy(self.lanes if lanes is None else lanes),
            document_ids=set(self.document_ids if document_ids is None else document_ids),
        )

    def entry(self, ledger, requirement_id):
        return next(item for item in ledger["requirements"] if item["id"] == requirement_id)

    def work_item(self, manifest, item_id):
        return next(item for item in manifest["queue"] if item["id"] == item_id)

    # --- the repository itself ------------------------------------------------------

    def test_the_repository_satisfies_the_invariant(self):
        self.check()
        self.assertEqual(47, len(self.document_ids))
        self.assertEqual(47, len(self.ledger["requirements"]))
        self.assertEqual(
            self.document_ids, {item["id"] for item in self.ledger["requirements"]}
        )
        self.assertEqual(
            {"active", "parked", "deferred", "met"},
            set(self.renderer.REQUIREMENT_DISPOSITIONS),
        )
        for item in self.manifest["queue"]:
            with self.subTest(item=item["id"]):
                self.assertIn(
                    item["lane"], self.lanes["lane_ids"] | self.lanes["assignment_states"]
                )
                self.assertIsInstance(item["requirements"], list)

    def test_the_ledger_records_the_findings_lane_zero_deliberately_did_not_fix(self):
        # The scope fence: D2, D5, C5 and their neighbours get a disposition here, not a repair.
        for requirement_id, finding in (
            ("FR-AUTH-001", "D5"),
            ("FR-LOOP-002", "D4"),
            ("FR-LOOP-003", "C2"),
            ("FR-RUN-002", "C2"),
            ("NFR-INTEGRITY-001", "D6"),
            ("NFR-BOUND-001", "C5"),
            ("NFR-MCP-001", "C4"),
        ):
            with self.subTest(requirement=requirement_id):
                entry = self.entry(self.ledger, requirement_id)
                self.assertEqual("parked", entry["disposition"])
                self.assertIn(finding, entry["reason"] + entry.get("note", ""))
        workbench = self.work_item(self.manifest, "queue.workbench-boundary")
        self.assertEqual("unassigned", workbench["lane"])
        self.assertIn("C4", workbench["lane_note"])

    def test_met_requires_evidence_that_exists(self):
        met = [item for item in self.ledger["requirements"] if item["disposition"] == "met"]
        self.assertTrue(met)
        for item in met:
            with self.subTest(requirement=item["id"]):
                self.assertTrue(item["evidence"])
                for reference in item["evidence"]:
                    self.assertTrue((REPO / reference).is_file(), reference)
        broken = copy.deepcopy(self.ledger)
        self.entry(broken, "NFR-SEC-001")["evidence"] = ["tools/there-is-no-such-gate.py"]
        with self.assertRaisesRegex(self.renderer.MissionControlError, "source does not exist"):
            self.check(ledger=broken)

    # --- check 1 ---------------------------------------------------------------------

    def test_an_active_requirement_claimed_by_no_work_item_fails(self):
        orphaned = copy.deepcopy(self.manifest)
        self.work_item(orphaned, "queue.guild-runtime")["requirements"] = ["FR-AUTH-005"]
        with self.assertRaisesRegex(
            self.renderer.MissionControlError,
            r"active requirement FR-RUN-001 is claimed by no work item in lane 4A",
        ):
            self.check(manifest=orphaned)

    def test_an_active_requirement_claimed_only_outside_its_lane_fails(self):
        # "In a named lane" is the substance of `active`: a claim from another lane is not one.
        misplaced = copy.deepcopy(self.manifest)
        self.work_item(misplaced, "queue.guild-runtime")["requirements"] = ["FR-AUTH-005"]
        self.work_item(misplaced, "queue.guild-campaign")["requirements"].append("FR-RUN-001")
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"FR-RUN-001 is claimed by no work item in lane 4A"
        ):
            self.check(manifest=misplaced)

    # --- check 2 ---------------------------------------------------------------------

    def test_a_work_item_referencing_a_nonexistent_requirement_fails(self):
        dangling = copy.deepcopy(self.manifest)
        self.work_item(dangling, "queue.reset")["requirements"].append("FR-RESET-004")
        with self.assertRaisesRegex(
            self.renderer.MissionControlError,
            r"queue\.reset\) references requirement FR-RESET-004, which is in no ledger entry",
        ):
            self.check(manifest=dangling)

    # --- check 3 ---------------------------------------------------------------------

    def test_a_work_item_with_no_lane_disposition_fails(self):
        laneless = copy.deepcopy(self.manifest)
        del self.work_item(laneless, "queue.portfolio")["lane"]
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"queue\.portfolio\) has no lane disposition"
        ):
            self.check(manifest=laneless)

        invented = copy.deepcopy(self.manifest)
        self.work_item(invented, "queue.portfolio")["lane"] = "4D"
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"claims unknown lane '4D'"
        ):
            self.check(manifest=invented)

        # `unassigned` records a gap; it may not excuse one.
        silent = copy.deepcopy(self.manifest)
        del self.work_item(silent, "queue.workbench-boundary")["lane_note"]
        with self.assertRaisesRegex(self.renderer.MissionControlError, r"lane_note"):
            self.check(manifest=silent)

        # `pre-lane` is for finished or blocked pre-vocabulary work, not schedulable work.
        backdated = copy.deepcopy(self.manifest)
        self.work_item(backdated, "queue.guild-campaign")["lane"] = "pre-lane"
        with self.assertRaisesRegex(self.renderer.MissionControlError, r"is `pre-lane` but"):
            self.check(manifest=backdated)

    def test_an_executable_work_item_must_carry_requirement_lineage(self):
        # The other half of the invariant, and the half C6 named as the structural cause.
        bare = copy.deepcopy(self.manifest)
        self.work_item(bare, "queue.workbench-boundary")["requirements"] = []
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"is executable but claims no requirement"
        ):
            self.check(manifest=bare)

    # --- check 4 ---------------------------------------------------------------------

    def test_a_requirement_with_no_explicit_disposition_fails(self):
        unstated = copy.deepcopy(self.ledger)
        del self.entry(unstated, "FR-LOOP-003")["disposition"]
        with self.assertRaisesRegex(
            self.renderer.MissionControlError,
            r"requirement FR-LOOP-003 lacks an explicit disposition",
        ):
            self.check(ledger=unstated)

        invented = copy.deepcopy(self.ledger)
        self.entry(invented, "FR-LOOP-003")["disposition"] = "in-progress"
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"'in-progress' is not one of"
        ):
            self.check(ledger=invented)

    def test_a_disposition_without_its_companion_field_fails(self):
        # "parked" with no reason is the shape this exists to stop: an unscheduled requirement
        # that looks dispositioned.
        for requirement_id, field in (
            ("FR-LOOP-003", "reason"),
            ("FR-RUN-001", "lane"),
            ("FR-REL-001", "phase"),
            ("NFR-SEC-001", "evidence"),
        ):
            with self.subTest(requirement=requirement_id):
                stripped = copy.deepcopy(self.ledger)
                del self.entry(stripped, requirement_id)[field]
                with self.assertRaises(self.renderer.MissionControlError):
                    self.check(ledger=stripped)

    def test_the_ledger_may_not_move_a_requirement_between_lanes(self):
        # docs/creator-os-phases.json is the sole lane authority (ADR 0013). Re-lanning a
        # requirement here would be a scope change wearing a bookkeeping disguise.
        moved = copy.deepcopy(self.ledger)
        self.entry(moved, "FR-RUN-001")["lane"] = "4B"
        with self.assertRaisesRegex(
            self.renderer.MissionControlError,
            r"FR-RUN-001 claims phase authority for lane 4B, but the lane vocabulary records '4A'",
        ):
            self.check(ledger=moved)

        # Nor may it drop a requirement out of the lane that owns it by parking it. That is
        # the quiet version of the same move, and it is the one worth catching.
        dropped = copy.deepcopy(self.ledger)
        entry = self.entry(dropped, "FR-RUN-001")
        entry.clear()
        entry.update(
            {
                "id": "FR-RUN-001",
                "disposition": "parked",
                "reason": "looks like it can wait",
                "pending_ruling": "whether it can wait",
            }
        )
        with self.assertRaisesRegex(
            self.renderer.MissionControlError,
            r"FR-RUN-001 is owned by lane 4A in the lane vocabulary but the ledger records parked",
        ):
            self.check(ledger=dropped)

    # --- check 5 ---------------------------------------------------------------------

    def test_a_requirements_document_edit_with_no_ledger_change_fails(self):
        # The anti-reintroduction clause. The ledger stores the extracted id set, so editing
        # the requirements document alone fails the gate the way a stale source pin does.
        added = set(self.document_ids) | {"FR-PORT-006"}
        with self.assertRaisesRegex(
            self.renderer.MissionControlError,
            r"in the document with no ledger entry: \['FR-PORT-006'\]",
        ):
            self.check(document_ids=added)

        removed = set(self.document_ids) - {"FR-RESET-002"}
        with self.assertRaisesRegex(
            self.renderer.MissionControlError,
            r"in the ledger but no longer in the document: \['FR-RESET-002'\]",
        ):
            self.check(document_ids=removed)

    def test_the_lane_vocabulary_may_not_cite_a_requirement_that_does_not_exist(self):
        cited = self.lanes["cited"]
        self.assertTrue(cited)
        self.assertLessEqual(cited, self.document_ids)
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"cites requirements that no longer exist"
        ):
            self.check(document_ids=set(self.document_ids) - {next(iter(cited))})

    # --- the ledger is a recorder, not a scheduler ----------------------------------

    def test_the_ledger_cannot_confer_lane_authority(self):
        # Authority runs one way: requirements -> phase authority -> ledger -> queue -> evidence.
        # Every lane the ledger names points at where it came from, and neither source is here.
        for entry in self.ledger["requirements"]:
            if entry["disposition"] in {"active", "deferred"}:
                with self.subTest(requirement=entry["id"]):
                    self.assertIn(entry["lane_authority"], self.renderer.LANE_AUTHORITIES)

        silent = copy.deepcopy(self.ledger)
        del self.entry(silent, "FR-RESET-001")["lane_authority"]
        with self.assertRaisesRegex(self.renderer.MissionControlError, r"lane_authority"):
            self.check(ledger=silent)

        invented = copy.deepcopy(self.ledger)
        self.entry(invented, "FR-RESET-001")["lane_authority"] = "ledger"
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"never that it came from the ledger"
        ):
            self.check(ledger=invented)

        # Claiming phase authority the vocabulary did not grant.
        overreach = copy.deepcopy(self.ledger)
        self.entry(overreach, "FR-RESET-001")["lane_authority"] = "phase-authority"
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"claims phase authority for lane 4A"
        ):
            self.check(ledger=overreach)

        # And the reverse: quietly demoting a vocabulary-owned requirement to a queue reading.
        demoted = copy.deepcopy(self.ledger)
        self.entry(demoted, "FR-RUN-001")["lane_authority"] = "queue-realization"
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"is owned by lane 4A in the lane vocabulary"
        ):
            self.check(ledger=demoted)

    def test_a_future_lane_can_only_come_from_the_phase_authority(self):
        # `deferred` is the disposition most tempting to guess at, so it takes no shortcut.
        for entry in self.ledger["requirements"]:
            if entry["disposition"] == "deferred":
                with self.subTest(requirement=entry["id"]):
                    self.assertEqual("phase-authority", entry["lane_authority"])
                    self.assertEqual(entry["phase"], self.lanes["owned"][entry["id"]])
        guessed = copy.deepcopy(self.ledger)
        self.entry(guessed, "FR-REL-001")["lane_authority"] = "queue-realization"
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"a future lane can only come from the phase authority"
        ):
            self.check(ledger=guessed)

    def test_a_parked_or_met_requirement_carries_no_lane(self):
        # This is the mechanical form of "do not helpfully schedule the obvious ones".
        for entry in self.ledger["requirements"]:
            if entry["disposition"] in {"parked", "met"}:
                with self.subTest(requirement=entry["id"]):
                    self.assertNotIn("lane", entry)
                    self.assertNotIn("phase", entry)
                    self.assertNotIn("lane_authority", entry)
        for requirement_id in ("NFR-MCP-001", "NFR-SEC-001"):
            with self.subTest(requirement=requirement_id):
                drifting = copy.deepcopy(self.ledger)
                self.entry(drifting, requirement_id)["lane"] = "4A"
                with self.assertRaisesRegex(
                    self.renderer.MissionControlError, r"has no lane, which is what keeps it"
                ):
                    self.check(ledger=drifting)

    # --- `parked` has one meaning ----------------------------------------------------

    def test_parked_must_name_the_ruling_it_is_waiting_on(self):
        # "Recognized and relevant, but scheduling requires a ruling that has not been made."
        # A park with no missing decision is one of the excluded states in disguise.
        parked = [item for item in self.ledger["requirements"] if item["disposition"] == "parked"]
        self.assertTrue(parked)
        for entry in parked:
            with self.subTest(requirement=entry["id"]):
                self.assertTrue(entry["pending_ruling"].strip())
                self.assertNotEqual(entry["pending_ruling"], entry["reason"])
        vague = copy.deepcopy(self.ledger)
        del self.entry(vague, "NFR-TEST-002")["pending_ruling"]
        with self.assertRaisesRegex(self.renderer.MissionControlError, r"pending_ruling"):
            self.check(ledger=vague)

    def test_the_park_definition_names_what_it_is_not(self):
        # The exclusions are the load-bearing half: without them `parked` becomes "not now".
        parked = self.ledger["dispositions"]["parked"]
        self.assertIn("requires an explicit ruling that has not yet been made", parked["meaning"])
        excluded = {item["state"]: item["use"] for item in parked["not_this"]}
        for state in (
            "future but already decided work",
            "externally blocked work",
            "intentionally abandoned work",
            "superseded requirements",
            "implementation-complete but awaiting evidence",
            "work merely outside the current lane",
        ):
            with self.subTest(state=state):
                self.assertIn(state, excluded)
                self.assertTrue(excluded[state].strip())
        # Each exclusion points at a representation that already exists; no new vocabulary.
        self.assertIn("deferred", excluded["future but already decided work"])
        self.assertIn("active", excluded["implementation-complete but awaiting evidence"])

    def test_met_stays_conservative(self):
        # Code existing is not evidence, and a contradicted requirement is not met.
        met = self.ledger["dispositions"]["met"]
        self.assertIn("Code existing is not evidence", met["check"])
        self.assertIn("unresolved audit finding", met["check"])
        for contradicted in ("NFR-INTEGRITY-001", "NFR-BOUND-001"):
            with self.subTest(requirement=contradicted):
                self.assertEqual("parked", self.entry(self.ledger, contradicted)["disposition"])
        # Wired end to end is not the same as proven, so these stay active in the lane whose
        # exit collects the evidence.
        for wired in ("FR-RESET-001", "FR-RESET-002"):
            with self.subTest(requirement=wired):
                entry = self.entry(self.ledger, wired)
                self.assertEqual("active", entry["disposition"])
                self.assertEqual("4A", entry["lane"])

    # --- assignment states are not lanes ---------------------------------------------

    def test_a_lane_assignment_state_can_never_read_as_a_lane(self):
        states = self.lanes["assignment_states"]
        self.assertEqual({"pre-lane", "unassigned"}, states)
        self.assertFalse(states & self.lanes["lane_ids"])

        # It cannot be recorded as a lane in the ledger ...
        as_a_lane = copy.deepcopy(self.ledger)
        entry = self.entry(as_a_lane, "FR-PORT-002")
        entry["lane"] = "pre-lane"
        entry["lane_authority"] = "queue-realization"
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"that is a work-item lane assignment state, not a lane"
        ):
            self.check(ledger=as_a_lane)

        # ... and it cannot quietly become one by colliding with a lane id.
        collided = copy.deepcopy(self.lanes)
        collided["lane_ids"] = set(collided["lane_ids"])
        vocabulary = json.loads(
            (REPO / "docs" / "creator-os-phases.json").read_text(encoding="utf-8")
        )
        vocabulary["lanes"][0]["id"] = "unassigned"
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"collides with a lane id"
        ):
            self.write_and_load_vocabulary(vocabulary)

        # Every assignment state declares the condition for carrying it and says it is not a
        # lane, so a reader cannot mistake one and a work item cannot opt out through one.
        for state in vocabulary["work_item_lane_assignment_values"]["assignment_states"]:
            with self.subTest(state=state["value"]):
                self.assertTrue(state["rule"].strip())
                self.assertIn("Not a lane", state["not"])

    def write_and_load_vocabulary(self, vocabulary):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "creator-os-phases.json"
            path.write_text(json.dumps(vocabulary), encoding="utf-8")
            return self.renderer.load_lane_vocabulary(path)

    def test_readiness_and_lane_assignment_are_independent(self):
        # A work item may be technically executable, not selected as next work, and pending a
        # named ruling all at once. That is information, not a queue defect to normalize away.
        workbench = self.work_item(self.manifest, "queue.workbench-boundary")
        self.assertEqual("ready", workbench["state"])
        self.assertEqual("unassigned", workbench["lane"])
        self.assertIn("ruling", workbench["lane_note"])
        # The property, not a head count: an executable item either sits in a real lane, or says
        # explicitly that it does not and names the ruling that would place it.
        for item in self.manifest["queue"]:
            if item["state"] != "ready":
                continue
            with self.subTest(item=item["id"]):
                self.assertTrue(
                    item["lane"] in self.lanes["lane_ids"]
                    or (item["lane"] == "unassigned" and item.get("lane_note")),
                    f"{item['id']} is ready with neither a lane nor a recorded reason",
                )

    def test_the_invariant_runs_inside_the_ordinary_manifest_gate(self):
        # It has to fire in CI, not only when someone calls it directly.
        broken = copy.deepcopy(self.manifest)
        del self.work_item(broken, "queue.portfolio")["lane"]
        with self.assertRaisesRegex(
            self.renderer.MissionControlError, r"has no lane disposition"
        ):
            self.renderer.validate_manifest(broken)


if __name__ == "__main__":
    unittest.main()

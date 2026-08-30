import copy
import hashlib
import importlib.util
import json
import subprocess
import sys
import unittest
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit


REPO = Path(__file__).resolve().parents[1]
MANIFEST_PATH = REPO / "docs" / "creator-os-composition-workbook.json"
WORKBOOK_PATH = REPO / "docs" / "creator-os-composition-workbook.html"
RENDERER_PATH = REPO / "tools" / "render_creator_os_composition_workbook.py"


def load_renderer():
    spec = importlib.util.spec_from_file_location("composition_workbook_renderer", RENDERER_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(module)
    return module


class WorkbookParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids = set()
        self.duplicates = set()
        self.links = []
        self.images = []
        self.scripts = 0
        self.external_scripts = []
        self.stylesheets = []
        self.main = 0
        self.h1 = 0

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        element_id = values.get("id")
        if element_id:
            if element_id in self.ids:
                self.duplicates.add(element_id)
            self.ids.add(element_id)
        if tag == "a" and values.get("href"):
            self.links.append(values["href"])
        elif tag == "img":
            self.images.append(values)
        elif tag == "script":
            self.scripts += 1
            if values.get("src"):
                self.external_scripts.append(values["src"])
        elif tag == "link" and "stylesheet" in values.get("rel", "").split():
            self.stylesheets.append(values.get("href"))
        elif tag == "main":
            self.main += 1
        elif tag == "h1":
            self.h1 += 1


class CreatorOsCompositionWorkbookTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
        cls.html = WORKBOOK_PATH.read_text(encoding="utf-8")
        cls.parser = WorkbookParser()
        cls.parser.feed(cls.html)
        cls.renderer = load_renderer()

    def test_generated_workbook_is_current(self):
        completed = subprocess.run(
            [sys.executable, str(RENDERER_PATH), "--check"],
            cwd=REPO,
            text=True,
            capture_output=True,
            check=False,
        )
        self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)
        self.assertIn("OK: docs", completed.stdout)

    def test_manifest_is_exact_slice_first(self):
        self.assertEqual("creator-os-composition-workbook/v1", self.manifest["schema"])
        self.assertEqual("tn0304-signature-hunt-venue", self.manifest["workbook_id"])
        self.assertEqual("tn0304", self.manifest["fixture"])
        self.assertEqual(["Air Drop", "Cold Shot"], self.manifest["story"]["artifacts"])
        self.assertEqual(5, len(self.manifest["stages"]))
        self.assertEqual(
            ["proven", "open", "implemented", "open", "open"],
            [stage["status"] for stage in self.manifest["stages"]],
        )
        self.assertIn("not yet a Guild palette primitive", " ".join(self.manifest["proof_boundary"]["not_claimed"]))
        self.assertIn("No installed player lap", " ".join(self.manifest["proof_boundary"]["not_claimed"]))

    def test_actions_are_bounded_ordered_and_preconditioned(self):
        self.assertEqual(
            [
                "orient",
                "name-place",
                "steward-locks",
                "creator-freedoms",
                "inhabit-venue",
                "attack-seams",
                "demo-verdict",
                "choose-attack",
            ],
            [action["id"] for action in self.manifest["actions"]],
        )
        for action in self.manifest["actions"]:
            self.assertTrue(action["precondition"].strip(), action["id"])
            self.assertIn(f'data-action-check="{action["id"]}"', self.html)
            self.assertIn(f'data-action-note="{action["id"]}"', self.html)

    def test_screenshots_and_local_links_resolve(self):
        for shot in self.manifest["screenshots"]:
            path = MANIFEST_PATH.parent / shot["path"]
            self.assertTrue(path.is_file(), path)
            self.assertEqual(shot["sha256"], hashlib.sha256(path.read_bytes()).hexdigest())
        for href in self.parser.links:
            parsed = urlsplit(href)
            self.assertFalse(parsed.scheme, href)
            self.assertFalse(parsed.netloc, href)
            if not parsed.path:
                self.assertIn(parsed.fragment, self.parser.ids, href)
                continue
            target = (WORKBOOK_PATH.parent / unquote(parsed.path)).resolve()
            self.assertTrue(target.is_file(), href)

    def test_page_is_self_contained_accessible_and_printable(self):
        self.assertEqual(1, self.parser.main)
        self.assertEqual(1, self.parser.h1)
        self.assertFalse(self.parser.duplicates)
        self.assertEqual(1, self.parser.scripts)
        self.assertFalse(self.parser.external_scripts)
        self.assertFalse(self.parser.stylesheets)
        self.assertIn('class="skip"', self.html)
        self.assertIn("@media print", self.html)
        for image in self.parser.images:
            self.assertTrue(image.get("alt", "").strip(), image)
            self.assertTrue(image.get("width", "").isdigit(), image)
            self.assertTrue(image.get("height", "").isdigit(), image)

    def test_review_is_local_bounded_and_explicitly_exported(self):
        review = self.manifest["review"]
        self.assertEqual("creator-os-composition-review/v1", review["schema"])
        self.assertLessEqual(review["max_observation_chars"], 20000)
        for required in (
            "localStorage.getItem(key)",
            "localStorage.setItem(key",
            "review_identity_mismatch",
            "Export review JSON",
            "Import review JSON",
            "new Blob",
            "manifest_sha256",
        ):
            self.assertIn(required, self.html)
        self.assertNotIn("fetch(", self.html)
        self.assertNotIn("XMLHttpRequest", self.html)

    def test_commands_are_copy_only_and_teardown_is_separate(self):
        commands = {item["id"]: item for item in self.manifest["commands"]}
        self.assertEqual(
            "tools\\quest-studio\\Invoke-ArchitecturalDemo.ps1 -Action Open",
            commands["open"]["command"],
        )
        self.assertEqual(
            "tools\\quest-studio\\Invoke-ArchitecturalWarmLap.ps1 -Close",
            commands["close-warm"]["command"],
        )
        self.assertEqual(len(commands), self.html.count("data-copy="))
        self.assertIn("cannot execute", " ".join(self.manifest["limitations"]))

    def test_validator_observably_rejects_proof_drift(self):
        drifted = copy.deepcopy(self.manifest)
        drifted["screenshots"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(self.renderer.WorkbookError, "hash drift"):
            self.renderer.validate_manifest(drifted)


if __name__ == "__main__":
    unittest.main()

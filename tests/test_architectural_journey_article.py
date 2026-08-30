import hashlib
import json
import re
import struct
import unittest
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit


REPO = Path(__file__).resolve().parents[1]
ARTICLE = REPO / "docs" / "architectural-build-journey-20260829.html"
IMAGE_ROOT = REPO / "docs" / "images" / "architectural-build-journey"
MANIFEST = IMAGE_ROOT / "manifest.json"
SOURCEBOOK = REPO / "docs" / "autopodcast" / "2026-08-29-architectural-build-rnd-sourcebook.md"


class JourneyParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids = set()
        self.duplicate_ids = set()
        self.hrefs = []
        self.images = []
        self.script_count = 0
        self.script_sources = []
        self.stylesheet_links = []
        self.main_count = 0
        self.article_count = 0
        self.h1_count = 0
        self.figure_count = 0
        self.figcaption_count = 0
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
        elif tag == "article":
            self.article_count += 1
        elif tag == "h1":
            self.h1_count += 1
        elif tag == "figure":
            self.figure_count += 1
        elif tag == "figcaption":
            self.figcaption_count += 1
        elif tag == "a" and values.get("href"):
            self.hrefs.append(values["href"])
        elif tag == "img":
            self.images.append(values)
        elif tag == "script":
            self.script_count += 1
            if values.get("src"):
                self.script_sources.append(values["src"])
        elif tag == "link" and "stylesheet" in values.get("rel", "").split():
            self.stylesheet_links.append(values.get("href"))


def png_dimensions(path: Path):
    data = path.read_bytes()[:24]
    if data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        raise AssertionError(f"not a PNG with an IHDR: {path}")
    return struct.unpack(">II", data[16:24])


class ArchitecturalJourneyArticleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.html = ARTICLE.read_text(encoding="utf-8")
        cls.manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        cls.parser = JourneyParser()
        cls.parser.feed(cls.html)

    def test_article_is_semantic_standalone_and_accessible(self):
        self.assertEqual("en", self.parser.lang)
        self.assertEqual(1, self.parser.main_count)
        self.assertEqual(1, self.parser.article_count)
        self.assertEqual(1, self.parser.h1_count)
        self.assertGreaterEqual(self.parser.figure_count, 4)
        self.assertEqual(self.parser.figure_count, self.parser.figcaption_count)
        self.assertFalse(self.parser.duplicate_ids)
        self.assertEqual(0, self.parser.script_count)
        self.assertFalse(self.parser.script_sources)
        self.assertFalse(self.parser.stylesheet_links)
        self.assertIn('class="skip-link"', self.html)
        self.assertIn('@media print', self.html)
        self.assertIn('aria-labelledby="flow-title flow-desc"', self.html)
        for image in self.parser.images:
            self.assertTrue(image.get("src"), image)
            self.assertTrue(image.get("alt", "").strip(), image)
            self.assertTrue(image.get("width", "").isdigit(), image)
            self.assertTrue(image.get("height", "").isdigit(), image)

    def test_article_links_are_local_and_resolve(self):
        self.assertNotIn("http://", self.html)
        self.assertNotIn("https://", self.html)
        for href in self.parser.hrefs:
            parsed = urlsplit(href)
            self.assertFalse(parsed.scheme, href)
            self.assertFalse(parsed.netloc, href)
            if not parsed.path:
                self.assertIn(parsed.fragment, self.parser.ids, href)
                continue
            target = (ARTICLE.parent / unquote(parsed.path)).resolve()
            self.assertTrue(target.is_file(), href)
        for image in self.parser.images:
            target = (ARTICLE.parent / unquote(image["src"])).resolve()
            self.assertTrue(target.is_file(), image["src"])

    def test_exact_architectural_and_rnd_claims_are_present(self):
        for expected in (
            "7.953375 × 7.4676 m",
            "2.2225 m",
            "5.8166 m",
            "43.907838°",
            "−0.029171 m",
            "16 floors",
            "16 walls",
            "8 roofs",
            "status → check → count → diff → status",
            "Staged, not applied",
            "R&amp;D mode helped",
            "one retained build",
            "40 marked pieces",
        ):
            self.assertIn(expected, self.html)
        self.assertIn("built, proved, and cleared", self.html)
        self.assertIn("built once and deliberately left standing", self.html)
        self.assertIn("No Creator Session. No mailbox request. No world write.", self.html)

    def test_article_does_not_publish_ephemeral_machine_details(self):
        for forbidden in (
            "C:\\",
            "/home/",
            "127.0.0.1",
            "browser_token",
            "browser token",
            "PID ",
        ):
            self.assertNotIn(forbidden, self.html)

    def test_image_manifest_pins_exact_tracked_bytes(self):
        self.assertEqual("comfy-quest-architectural-journey-imagery/v1", self.manifest["schema"])
        self.assertEqual("tn0304", self.manifest["fixture"])
        self.assertEqual(3, len(self.manifest["images"]))
        article_sources = {image["src"].split("/")[-1] for image in self.parser.images}
        manifest_names = {image["name"] for image in self.manifest["images"]}
        self.assertEqual(manifest_names, article_sources)
        for image in self.manifest["images"]:
            path = IMAGE_ROOT / image["name"]
            data = path.read_bytes()
            self.assertEqual(image["bytes"], len(data), path)
            self.assertEqual(image["sha256"], hashlib.sha256(data).hexdigest(), path)
            self.assertEqual((image["width"], image["height"]), png_dimensions(path), path)
            self.assertFalse(image["world_mutation_performed"], path)
        accepted = next(item for item in self.manifest["images"] if item["name"] == "valheim-standing-build.png")
        self.assertEqual(
            "5d4665915c71248265e1940d5e03a73f792f574b9c477f91c2acf7abac47a29c",
            accepted["sha256"],
        )

    def test_evidence_links_and_hashes_are_exact(self):
        for relative in self.manifest["evidence"].values():
            if not isinstance(relative, str) or not relative.startswith("docs/"):
                continue
            self.assertTrue((REPO / relative).is_file(), relative)
        for digest in (
            "f509aa2a201fdb3495c0f8aa3656ca156421524b476d12d4f0d45aa3cd9a21e9",
            "0b4a62bcd3d0baa914f264081657b99649dd0f25a5973e1258b4e3920acbb578",
            "5d466cdaa5a213ef958d07636325b9398eee9a74584019da8dc16a5603654251",
            "02201382e57635f4e945229836443d2fdbf75e243973281d1f9806cd770ece5f",
            "5d4665915c71248265e1940d5e03a73f792f574b9c477f91c2acf7abac47a29c",
        ):
            self.assertIn(digest, self.html)


class ArchitecturalPodcastSourcebookTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.markdown = SOURCEBOOK.read_text(encoding="utf-8")

    def test_sourcebook_is_sized_and_shaped_for_an_hour_conversation(self):
        words = re.findall(r"\b[\w’'-]+\b", self.markdown, flags=re.UNICODE)
        self.assertGreaterEqual(len(words), 8_000)
        self.assertIn('target_duration_minutes: "55-65"', self.markdown)
        self.assertIn('recommended_format: "two-host investigative conversation"', self.markdown)
        for heading in (
            "## How to use this sourcebook",
            "## Executive overview",
            "## 1. The deeper product context: protect the creator’s seat",
            "## 8. The disposable cold replay: prove reversibility once",
            "## 9. The warm R&D loop: build once, inspect repeatedly",
            "## 13. Product and engineering questions for the hosts",
            "## 16. Source ledger",
            "## 17. Final generator note",
        ):
            self.assertIn(heading, self.markdown)
        self.assertGreaterEqual(self.markdown.count("```"), 20)
        self.assertEqual(0, self.markdown.count("```") % 2)

    def test_sourcebook_preserves_exact_facts_and_boundaries(self):
        normalized = " ".join(self.markdown.split())
        for expected in (
            "7.953375 by 7.4676 metres",
            "2.2225 metres",
            "5.8166 metres",
            "43.907838 degrees",
            "negative 0.029171 metre",
            "16 `wood_floor` pieces",
            "16 `woodwall` pieces",
            "8 `wood_roof_45` pieces",
            "Staged, not applied",
            "status → blueprint_check → blueprint_count → blueprint_diff → status",
            "The disposable cold replay built and cleared",
            "The first warm lap built once and retained",
            "The final operator lap was read-only",
            "not human-aesthetic acceptance",
        ):
            self.assertIn(expected, normalized)
        for digest in (
            "544c0dc26ed3cbb556f5467ca0169705fe1172f5",
            "4842e42dca34bbe7660143fdd41487d9e63a21a8",
            "f2ce9a09c5070cbd6637cfff3c6002e8bdf22891",
            "0e85b95d67c0124679d44edd4775b191b88be0c5",
            "2510553bddf1fbae6a4ea9c14ff923ca05b153d8",
            "f509aa2a201fdb3495c0f8aa3656ca156421524b476d12d4f0d45aa3cd9a21e9",
            "5d4665915c71248265e1940d5e03a73f792f574b9c477f91c2acf7abac47a29c",
        ):
            self.assertIn(digest, self.markdown)

    def test_source_ledger_is_complete_pinned_and_locally_resolvable(self):
        definitions = dict(re.findall(r"^\[(S\d+)\]:\s+(\S+)\s*$", self.markdown, re.MULTILINE))
        expected = {f"S{index}" for index in range(1, 19)}
        self.assertEqual(expected, set(definitions))
        narrative = self.markdown.split("\n[S1]:", 1)[0]
        self.assertEqual(expected, set(re.findall(r"\[(S\d+)\]", narrative)))
        for source_id, href in definitions.items():
            parsed = urlsplit(href)
            if parsed.scheme:
                self.assertEqual("https", parsed.scheme, source_id)
                self.assertEqual("github.com", parsed.netloc, source_id)
                self.assertNotIn("/blob/main/", parsed.path, source_id)
                if "/blob/" in parsed.path:
                    revision = parsed.path.split("/blob/", 1)[1].split("/", 1)[0]
                    self.assertRegex(revision, r"^[0-9a-f]{40}$", source_id)
                elif "/commit/" in parsed.path:
                    revision = parsed.path.rsplit("/commit/", 1)[1]
                    self.assertRegex(revision, r"^[0-9a-f]{40}$", source_id)
                continue
            target = (SOURCEBOOK.parent / unquote(parsed.path)).resolve()
            self.assertTrue(target.is_file(), f"{source_id}: {href}")

    def test_sourcebook_does_not_publish_ephemeral_operator_details(self):
        for forbidden in (
            "C:\\",
            "/home/",
            "127.0.0.1",
            "browser_token",
            "tunnel_pid",
            "world_uid",
        ):
            self.assertNotIn(forbidden, self.markdown)


if __name__ == "__main__":
    unittest.main()

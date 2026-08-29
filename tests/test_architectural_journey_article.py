import hashlib
import json
import struct
import unittest
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit


REPO = Path(__file__).resolve().parents[1]
ARTICLE = REPO / "docs" / "architectural-build-journey-20260829.html"
IMAGE_ROOT = REPO / "docs" / "images" / "architectural-build-journey"
MANIFEST = IMAGE_ROOT / "manifest.json"


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


if __name__ == "__main__":
    unittest.main()

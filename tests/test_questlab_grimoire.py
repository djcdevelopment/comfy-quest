import json
import importlib.util
import unittest
from pathlib import Path

_SPEC = importlib.util.spec_from_file_location(
    "questlab_grimoire_generator",
    Path("tools/questlab-grimoire/generate_grimoire.py"),
)
generate_grimoire = importlib.util.module_from_spec(_SPEC)
assert _SPEC.loader is not None
_SPEC.loader.exec_module(generate_grimoire)


class QuestLabGrimoireTest(unittest.TestCase):
    def test_catalog_matches_the_generated_creator_event_count(self):
        rows = generate_grimoire.catalog()
        self.assertEqual(34, len(rows))
        self.assertEqual(34, len({row["event"] for row in rows}))
        self.assertTrue(all(row["bindable"] for row in rows))

    def test_generated_artifacts_have_one_source_schema(self):
        payload = json.loads(Path("artifacts/questlab-grimoire.json").read_text(encoding="utf-8"))
        self.assertEqual("comfy-questlab-grimoire/v1", payload["schema"])
        self.assertEqual(34, len(payload["events"]))
        markdown = Path("docs/questlab-grimoire.md").read_text(encoding="utf-8")
        self.assertIn("# The Quest Lab Grimoire", markdown)
        self.assertIn("`kill`", markdown)

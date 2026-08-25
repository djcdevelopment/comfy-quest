import copy
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
VERIFIER = REPO / "tools" / "verify_source_intents.py"
MANIFEST = REPO / "docs" / "quest-mission-control.json"


def load_verifier():
    spec = importlib.util.spec_from_file_location("source_intent_verifier", VERIFIER)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class SourceIntentVerifierTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.verifier = load_verifier()
        cls.authority = cls.verifier.load_authority(MANIFEST)

    def fixture(self):
        authority = copy.deepcopy(self.authority)
        payloads = {}
        for document in authority["documents"]:
            payload = f"published intent {document['id']}\n".encode("utf-8")
            document["bytes"] = len(payload)
            document["sha256"] = hashlib.sha256(payload).hexdigest()
            payloads[
                self.verifier.raw_url(
                    authority["repository"], authority["revision"], document["path"]
                )
            ] = payload
        return authority, payloads

    def test_exact_published_bytes_verify(self):
        authority, payloads = self.fixture()
        verified = self.verifier.verify_authority(authority, payloads.__getitem__)
        self.assertEqual(["01", "02", "03", "04", "05"], [item[0] for item in verified])

    def test_byte_or_hash_drift_fails_closed(self):
        authority, payloads = self.fixture()
        authority["documents"][0]["bytes"] += 1
        with self.assertRaisesRegex(self.verifier.SourceIntentError, "byte count"):
            self.verifier.verify_authority(authority, payloads.__getitem__)

        authority, payloads = self.fixture()
        authority["documents"][1]["sha256"] = "0" * 64
        with self.assertRaisesRegex(self.verifier.SourceIntentError, "SHA-256"):
            self.verifier.verify_authority(authority, payloads.__getitem__)

    def test_unavailable_published_revision_is_blocking(self):
        authority, _ = self.fixture()

        def unavailable(_):
            raise self.verifier.SourceIntentError("cannot fetch immutable source")

        with self.assertRaisesRegex(self.verifier.SourceIntentError, "cannot fetch"):
            self.verifier.verify_authority(authority, unavailable)

    def test_moving_revision_and_incomplete_intent_set_are_rejected(self):
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        manifest["source_intents"]["revision"] = "main"
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(self.verifier.SourceIntentError, "40-character SHA"):
                self.verifier.load_authority(path)

        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        manifest["source_intents"]["documents"].pop()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(self.verifier.SourceIntentError, "ordered ids"):
                self.verifier.load_authority(path)


if __name__ == "__main__":
    unittest.main()

import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools" / "nuget"))

from validate_nupkg import (  # noqa: E402
    CONTRACT_ID,
    MOD_GLUE,
    SPATIAL_SCHEMAS,
    PackageError,
    validate_payload,
)


class NuGetPackageValidationTests(unittest.TestCase):
    @staticmethod
    def contracts_payload() -> set[str]:
        names = {
            "[Content_Types].xml",
            "_rels/.rels",
            f"{CONTRACT_ID}.nuspec",
            "PACKAGE-README.md",
            "lib/netstandard2.0/ComfyQuestContracts.dll",
            "package/services/metadata/core-properties/" + "a" * 32 + ".psmdcp",
        }
        names.update(f"contentFiles/cs/any/ModGlue/{name}" for name in MOD_GLUE)
        names.update(f"contracts/spatial/{name}" for name in SPATIAL_SCHEMAS)
        return names

    def test_contracts_allow_exact_spatial_schema_pair(self) -> None:
        validate_payload(self.contracts_payload(), "contracts", allow_signature=False)

    def test_contracts_reject_an_unreviewed_schema_payload(self) -> None:
        names = self.contracts_payload()
        names.add("contracts/spatial/experimental-v2.schema.json")
        with self.assertRaisesRegex(PackageError, "unexpected entries"):
            validate_payload(names, "contracts", allow_signature=False)


if __name__ == "__main__":
    unittest.main()

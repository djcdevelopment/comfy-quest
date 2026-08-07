"""Invariants for the Fallingwater blueprint generator (tools/blueprints/).

The generator has no snapping and no collision: its grid discipline is the
only thing between a blueprint and a z-fighting mess, so the duplicate-pose
check here is load-bearing, not cosmetic. Palette validation against the
runtime prefab dump runs when the dump artifact exists and self-skips (with a
message) until the first in-game `questlab_prefabs dump` lands it.
"""
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PYTHON = sys.executable
TOOLS = ROOT / "tools" / "blueprints"
GENERATOR = TOOLS / "generate_fallingwater.py"
PREFAB_DUMP = ROOT / "tools" / "component-packets" / "samples" / "prefab-dump.json"

sys.path.insert(0, str(TOOLS))
from blueprint_lib import parse_blueprint  # noqa: E402

# HABS PA-1690 main-tray plan dimensions, snapped to the 2 m grid.
EXPECTED_FOOTPRINT = (30.0, 18.0)
BUDGET = (400, 2500)


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


class FallingwaterBlueprint(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._tmp = tempfile.TemporaryDirectory()
        cls.out = Path(cls._tmp.name) / "fallingwater.blueprint"
        run(GENERATOR, "--out", cls.out)
        cls.bp = parse_blueprint(cls.out)

    @classmethod
    def tearDownClass(cls):
        cls._tmp.cleanup()

    def test_piece_count_within_budget(self):
        low, high = BUDGET
        self.assertTrue(
            low <= len(self.bp.pieces) <= high,
            f"{len(self.bp.pieces)} pieces outside [{low}, {high}]",
        )

    def test_palette_is_closed(self):
        sys.path.insert(0, str(TOOLS))
        from generate_fallingwater import PALETTE

        allowed = set(PALETTE.values())
        used = {p.prefab for p in self.bp.pieces}
        self.assertLessEqual(used, allowed, f"unexpected prefabs: {used - allowed}")

    def test_no_scale_fields(self):
        for line in self.out.read_text(encoding="utf-8").splitlines():
            if line.startswith("#") or not line:
                continue
            self.assertEqual(
                len(line.split(";")), 10,
                f"expected the 10-field no-scale form: {line!r}",
            )

    def test_no_duplicate_poses(self):
        seen = {}
        for p in self.bp.pieces:
            key = (p.prefab, round(p.x, 2), round(p.y, 2), round(p.z, 2),
                   round(p.ry, 3), round(p.rw, 3))
            self.assertNotIn(key, seen, f"duplicate pose: {key}")
            seen[key] = p

    def test_footprint_matches_habs(self):
        x0, x1, _y0, _y1, z0, z1 = self.bp.footprint()
        width, depth = EXPECTED_FOOTPRINT
        # Piece centers sit inside the authored envelope, so measured extent is
        # within one tile of the plan dimension, never over it.
        self.assertAlmostEqual(x1 - x0, width, delta=2.0)
        self.assertAlmostEqual(z1 - z0, depth, delta=2.0)

    def test_nothing_below_origin(self):
        # MinY = 0 keeps the ground-snap in LabBlueprintBuilder trivial: the
        # lowest authored piece IS the ground line.
        self.assertGreaterEqual(min(p.y for p in self.bp.pieces), 0.0)

    def test_rotations_are_unit_yaw_quaternions(self):
        for p in self.bp.pieces:
            self.assertEqual((p.rx, p.rz), (0.0, 0.0), "only yaw rotations expected")
            self.assertAlmostEqual(p.ry**2 + p.rw**2, 1.0, places=4)

    def test_checked_in_blueprint_is_current(self):
        committed = TOOLS / "fallingwater.blueprint"
        self.assertTrue(
            committed.exists(),
            "tools/blueprints/fallingwater.blueprint is generated and checked in",
        )
        self.assertEqual(
            committed.read_text(encoding="utf-8"),
            self.out.read_text(encoding="utf-8"),
            "checked-in blueprint is stale — rerun generate_fallingwater.py",
        )

    def test_palette_against_prefab_dump(self):
        if not PREFAB_DUMP.exists():
            self.skipTest(
                f"{PREFAB_DUMP} not landed yet — run questlab_prefabs dump in game "
                "and commit the artifact"
            )
        run(GENERATOR, "--out", Path(self._tmp.name) / "check.blueprint",
            "--prefab-dump", PREFAB_DUMP)


if __name__ == "__main__":
    unittest.main()

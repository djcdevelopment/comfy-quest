"""Generate a Valheim blueprint of Fallingwater (Frank Lloyd Wright, 1935).

Parametric massing, not a scan: the house is modeled as the things that make
it read as Fallingwater — three stacked horizontal trays cantilevered off a
vertical stone core, parapet bands, continuous glass between parapet top and
the slab above, and dark horizontal trim. Dimensions come from the Library of
Congress HABS survey (HABS PA-1690, public domain), snapped to Valheim's 2 m
build grid: the main tray is ~30 x 18 m, the second ~22 x 12 m, the third
~10 x 8 m, storey height 3 m (1 m parapet + 2 m glazing).

Every mass is a function taking the parameter object, so tuning proportions
after the first in-game build is constant-editing, not surgery. The waterfall
is deliberately absent: the lab does not shape terrain, and the first build
site is flat ground.

Prefab names are guesses until proven: `--prefab-dump <path>` cross-checks the
palette against a `questlab_prefabs dump` JSON and fails loudly on a miss, and
the in-game `questlab_blueprint check` gates the build regardless. Piece
pivots are MEASURED, not assumed: the 2026-08-07 dump's snap points showed the
stone pieces pivot at their centers (stone_wall_2x1 snaps at y -0.5..+0.5)
while crystal_wall_1x1 pivots at its bottom (y 0..1) — the original
bottom-pivot assumption would have sunk every stone course half a metre. The
LIFT table below encodes what the dump said.

Usage:
    python tools/blueprints/generate_fallingwater.py [--out PATH] [--stats]
        [--prefab-dump tools/component-packets/samples/prefab-dump.json]
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from blueprint_lib import Blueprint, Piece, write_blueprint, yaw_quat  # noqa: E402


# ---- palette ---------------------------------------------------------------
# One place to swap materials. Stone reads as Wright's ochre concrete at
# Valheim's palette distance; crystal_wall_1x1 (Mistlands) is the only
# continuous-glazing piece in the vanilla game; darkwood reads Cherokee red.
PALETTE = {
    "slab": "stone_floor_2x2",       # 2x2 m floor/roof plate, 1 m thick
    "wall": "stone_wall_2x1",        # 2 w x 1 h
    "wall_big": "stone_wall_4x2",    # 4 w x 2 h, core shell
    "arch": "stone_arch",            # applied opening detail on the core
    "glass": "crystal_wall_1x1",     # 1 x 1 m glazing
    "trim": "darkwood_beam",         # 2 m horizontal accent
}

# Pivot lift: what to ADD to a piece's intended BASE height to get the pivot
# height the blueprint must carry. Measured from the prefab dump's snap
# points (comfy-prefab-dump/v1, game 0.221.12): stone pieces pivot at their
# center, crystal at its bottom, beams a quarter up. A slab's "base" is one
# metre below the walking level it provides, so its entry nets to level-0.5.
LIFT = {
    "slab": 0.5,        # 1 m thick, center pivot: base + 0.5 = pivot
    "wall": 0.5,        # 1 m tall, center pivot
    "wall_big": 1.0,    # 2 m tall, center pivot
    "arch": 0.5,
    "glass": 0.0,       # bottom pivot — the lone exception
    "trim": 0.25,
}


@dataclass
class Params:
    # Trays: (x0, x1, z0, z1, floor_y). X east, Z north, metres, 2 m grid.
    main_tray = (-15, 15, -9, 9, 0)
    l2_tray = (-7, 15, -3, 9, 3)
    l3_tray = (3, 13, 1, 9, 6)
    # Enclosed volumes on each tray (glass on south/east, stone north/west).
    main_volume = (-5, 15, -1, 9)
    l2_volume = (1, 15, 1, 9)
    l3_volume = (5, 11, 3, 9)
    # The vertical stone core (chimney + stair): footprint and total height.
    core = (7, 15, 5, 9)
    core_top = 12
    storey = 3          # parapet + glazing
    parapet_h = 1
    roof_y = 9          # slab over the third-level volume
    trellis_y = 3       # beams over the south terrace


# ---- emitters --------------------------------------------------------------

class Build:
    """Accumulates pieces, deduplicating exact repeats: masses are allowed to
    overlap in the model (a tray edge under a volume wall), but the same piece
    twice in the same pose is always a mistake."""

    def __init__(self):
        self.pieces = []
        self._seen = set()
        self.by_mass = Counter()

    def add(self, mass, prefab, x, y, z, yaw=0.0):
        key = (prefab, round(x, 2), round(y, 2), round(z, 2), round(yaw, 1) % 360)
        if key in self._seen:
            return
        self._seen.add(key)
        rx, ry, rz, rw = yaw_quat(yaw)
        self.pieces.append(Piece(prefab, x, y, z, rx, ry, rz, rw))
        self.by_mass[mass] += 1


def emit_slab(b, mass, x0, x1, z0, z1, y):
    """Floor/roof plates: 2 m tiles, centers on the half-grid. `y` is the
    walking level the slab provides — its 1 m body hangs below that."""
    pivot = y - 1 + LIFT["slab"]
    for x in range(x0, x1, 2):
        for z in range(z0, z1, 2):
            b.add(mass, PALETTE["slab"], x + 1, pivot, z + 1)


def emit_wall_x(b, mass, kind, x0, x1, z, y0, courses, course_h=1, seg=2):
    """A wall running east-west at fixed z, courses stacked up from base y0."""
    for c in range(courses):
        for x in range(x0, x1, seg):
            b.add(mass, PALETTE[kind], x + seg / 2,
                  y0 + c * course_h + LIFT[kind], z, yaw=0)


def emit_wall_z(b, mass, kind, z0, z1, x, y0, courses, course_h=1, seg=2):
    """A wall running north-south at fixed x."""
    for c in range(courses):
        for z in range(z0, z1, seg):
            b.add(mass, PALETTE[kind], x,
                  y0 + c * course_h + LIFT[kind], z + seg / 2, yaw=90)


def emit_glass_x(b, x0, x1, z, y0):
    for c in range(2):
        for x in range(x0, x1):
            b.add("glass", PALETTE["glass"], x + 0.5, y0 + c + LIFT["glass"], z, yaw=0)


def emit_glass_z(b, z0, z1, x, y0):
    for c in range(2):
        for z in range(z0, z1):
            b.add("glass", PALETTE["glass"], x, y0 + c + LIFT["glass"], z + 0.5, yaw=90)


def emit_volume(b, p, volume, floor_y):
    """One storey: stone on north and west, parapet-plus-glass on south and
    east — the orientation that gives the terraces their open faces."""
    x0, x1, z0, z1 = volume
    emit_wall_x(b, "stone", "wall", x0, x1, z1, floor_y, p.storey)          # north
    emit_wall_z(b, "stone", "wall", z0, z1, x0, floor_y, p.storey)          # west
    emit_wall_x(b, "parapet", "wall", x0, x1, z0, floor_y, p.parapet_h)     # south
    emit_glass_x(b, x0, x1, z0, floor_y + p.parapet_h)
    emit_wall_z(b, "parapet", "wall", z0, z1, x1, floor_y, p.parapet_h)     # east
    emit_glass_z(b, z0, z1, x1, floor_y + p.parapet_h)


def emit_tray_parapet(b, p, tray, volume):
    """1 m band on every tray edge not already claimed by the volume above it."""
    x0, x1, z0, z1, y = tray
    vx0, vx1, vz0, vz1 = volume
    emit_wall_x(b, "parapet", "wall", x0, x1, z0, y, p.parapet_h)               # south
    if x0 < vx0:
        emit_wall_x(b, "parapet", "wall", x0, vx0, z1, y, p.parapet_h)          # north gap
        emit_wall_z(b, "parapet", "wall", z0, z1, x0, y, p.parapet_h)           # west
    if z0 < vz0:
        emit_wall_z(b, "parapet", "wall", z0, vz0, x1, y, p.parapet_h)          # east gap


def emit_core(b, p):
    """The vertical mass everything else hangs off. 4x2 stone shell to keep
    the piece count sane; arches applied at each storey's west face as the
    opening read (cutting real holes in 4 m pieces buys nothing at this
    scale)."""
    x0, x1, z0, z1 = p.core
    emit_wall_x(b, "core", "wall_big", x0, x1, z1, 0, p.core_top // 2, course_h=2, seg=4)
    emit_wall_x(b, "core", "wall_big", x0, x1, z0, 0, p.core_top // 2, course_h=2, seg=4)
    emit_wall_z(b, "core", "wall_big", z0, z1, x0, 0, p.core_top // 2, course_h=2, seg=4)
    emit_wall_z(b, "core", "wall_big", z0, z1, x1, 0, p.core_top // 2, course_h=2, seg=4)
    for storey_y in (0, 3, 6):
        b.add("core", PALETTE["arch"], x0, storey_y + LIFT["arch"],
              (z0 + z1) / 2, yaw=90)
    emit_slab(b, "core", x0, x1, z0, z1, p.core_top)                            # chimney cap


def emit_trellis(b, p):
    """Beams over the south terrace, the horizontal shadow-liner read."""
    x0 = p.main_tray[0] + 1
    for x in range(x0, p.main_volume[0], 2):
        for z in range(p.main_tray[2], -1, 2):
            b.add("trellis", PALETTE["trim"], x, p.trellis_y + LIFT["trim"],
                  z + 1, yaw=90)


def emit_trim(b, p):
    """Darkwood caps along the south parapet tops — the Cherokee-red line."""
    for (x0, x1, z0, _z1, y) in (p.main_tray, p.l2_tray, p.l3_tray):
        for x in range(x0, x1, 2):
            b.add("trim", PALETTE["trim"], x + 1, y + p.parapet_h + LIFT["trim"],
                  z0, yaw=0)


def generate(p=None):
    p = p or Params()
    b = Build()

    for tray, volume in ((p.main_tray, p.main_volume), (p.l2_tray, p.l2_volume),
                         (p.l3_tray, p.l3_volume)):
        x0, x1, z0, z1, y = tray
        emit_slab(b, "tray", x0, x1, z0, z1, y)
        emit_tray_parapet(b, p, tray, volume)
        emit_volume(b, p, volume, y)

    # Roofs: the strip of second-level volume the third tray does not cover,
    # then the top slab with its own parapet.
    l3x0, l3x1 = p.l3_tray[0], p.l3_tray[1]
    vx0, vx1, vz0, vz1 = p.l2_volume
    if vx0 < l3x0:
        emit_slab(b, "roof", vx0, l3x0, vz0, vz1, p.l3_tray[4])
    if l3x1 < vx1:
        emit_slab(b, "roof", l3x1, vx1, vz0, vz1, p.l3_tray[4])
    rx0, rx1, rz0, rz1 = p.l3_volume
    emit_slab(b, "roof", rx0, rx1, rz0, rz1, p.roof_y)
    emit_wall_x(b, "roof", "wall", rx0, rx1, rz0, p.roof_y, 1)
    emit_wall_x(b, "roof", "wall", rx0, rx1, rz1, p.roof_y, 1)
    emit_wall_z(b, "roof", "wall", rz0, rz1, rx0, p.roof_y, 1)
    emit_wall_z(b, "roof", "wall", rz0, rz1, rx1, p.roof_y, 1)

    emit_core(b, p)
    emit_trellis(b, p)
    emit_trim(b, p)
    return b


BUDGET_WARN = 2500


def validate_against_dump(dump_path):
    data = json.loads(Path(dump_path).read_text(encoding="utf-8"))
    known = {entry["name"] for entry in data.get("prefabs", [])}
    missing = sorted(v for v in PALETTE.values() if v not in known)
    if missing:
        raise SystemExit(
            "palette names not in the prefab dump: " + ", ".join(missing)
            + " — fix PALETTE before shipping the blueprint."
        )
    return len(known)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    default_out = Path(__file__).resolve().parent / "fallingwater.blueprint"
    ap.add_argument("--out", default=str(default_out))
    ap.add_argument("--stats", action="store_true")
    ap.add_argument("--prefab-dump", help="questlab_prefabs dump JSON to validate against")
    args = ap.parse_args(argv)

    if args.prefab_dump:
        known = validate_against_dump(args.prefab_dump)
        print(f"palette OK against {known} dumped prefabs")

    b = generate()
    bp = Blueprint(
        name="Fallingwater",
        creator="baseline quest lab",
        description=(
            "Frank Lloyd Wright's Fallingwater as parametric massing, from the "
            "HABS PA-1690 survey. Built by questlab_blueprint; no terrain ops."
        ),
        pieces=b.pieces,
    )
    write_blueprint(args.out, bp)

    total = len(b.pieces)
    print(f"fallingwater: {total} pieces -> {args.out}")
    if args.stats:
        for mass, n in sorted(b.by_mass.items(), key=lambda kv: -kv[1]):
            print(f"  {mass:8} {n}")
        fx0, fx1, fy0, fy1, fz0, fz1 = bp.footprint()
        print(f"  footprint {fx1 - fx0:.0f} x {fz1 - fz0:.0f} m, {fy1 - fy0:.0f} m tall")
    if total > BUDGET_WARN:
        print(f"WARNING: {total} pieces exceeds the {BUDGET_WARN} budget — "
              "trim parapet courses or tray sizes.", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

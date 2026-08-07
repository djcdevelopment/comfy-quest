"""Emit and parse PlanBuild .blueprint files.

The format is the community standard the mod-side parser
(network/mod/ComfyQuestLab/Core/BlueprintFile.cs) reads, confirmed against
PlanBuild's own PieceEntry source and its checked-in test blueprints:

    #Name:<text>
    #Creator:<text>
    #Description:<text>
    #Pieces
    prefab;category;posX;posY;posZ;rotX;rotY;rotZ;rotW;additionalInfo

Floats are invariant-culture. We write the 10-field form with an empty
additionalInfo (a trailing semicolon), exactly like PlanBuild's own test
fixtures — scale fields are omitted because vanilla pieces do not scale and
the mod rejects non-unit scales anyway.

Parsing lives here too so generator tests can round-trip their own output
and future generators get it free. It deliberately mirrors the C# parser's
semantics for the subset generators produce; the C# side remains the
authority on tolerating the wild.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field


@dataclass
class Piece:
    prefab: str
    x: float
    y: float
    z: float
    # Quaternion components; identity by default. yaw_quat() builds the
    # rotation-about-Y case that covers almost every build piece.
    rx: float = 0.0
    ry: float = 0.0
    rz: float = 0.0
    rw: float = 1.0
    category: str = "Building"
    info: str = ""


@dataclass
class Blueprint:
    name: str
    creator: str = ""
    description: str = ""
    pieces: list = field(default_factory=list)

    def footprint(self):
        """(minX, maxX, minY, maxY, minZ, maxZ) over all pieces."""
        xs = [p.x for p in self.pieces]
        ys = [p.y for p in self.pieces]
        zs = [p.z for p in self.pieces]
        return (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))


def yaw_quat(degrees):
    """Quaternion (x, y, z, w) for a rotation of `degrees` about the Y axis."""
    half = math.radians(degrees) / 2.0
    return (0.0, math.sin(half), 0.0, math.cos(half))


def _num(v):
    """Invariant float: no exponent for the magnitudes a building uses, no
    trailing zeros, and -0 normalized so position dedup keys stay stable."""
    if v == 0:
        return "0"
    text = f"{v:.4f}".rstrip("0").rstrip(".")
    return text if text not in ("-0", "") else "0"


def write_blueprint(path, bp):
    lines = [f"#Name:{bp.name}"]
    if bp.creator:
        lines.append(f"#Creator:{bp.creator}")
    if bp.description:
        lines.append(f"#Description:{bp.description}")
    lines.append("#Pieces")
    for p in bp.pieces:
        if ";" in p.info:
            raise ValueError(f"additionalInfo may not contain semicolons: {p.info!r}")
        lines.append(
            f"{p.prefab};{p.category};{_num(p.x)};{_num(p.y)};{_num(p.z)};"
            f"{_num(p.rx)};{_num(p.ry)};{_num(p.rz)};{_num(p.rw)};{p.info}"
        )
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


def parse_blueprint(path):
    headers = {}
    pieces = []
    mode = None
    with open(path, encoding="utf-8-sig") as f:
        for raw in f:
            line = raw.rstrip()
            if not line:
                continue
            if line.startswith("#"):
                if ":" in line:
                    key, _, value = line[1:].partition(":")
                    headers[key.strip().lower()] = value.strip()
                else:
                    mode = line[1:].strip().lower()
                continue
            if mode != "pieces":
                continue
            parts = line.split(";")
            if len(parts) < 9:
                raise ValueError(f"piece line with {len(parts)} fields: {line!r}")
            pieces.append(
                Piece(
                    prefab=parts[0],
                    category=parts[1],
                    x=float(parts[2]),
                    y=float(parts[3]),
                    z=float(parts[4]),
                    rx=float(parts[5]),
                    ry=float(parts[6]),
                    rz=float(parts[7]),
                    rw=float(parts[8]),
                    info=parts[9] if len(parts) > 9 else "",
                )
            )
    return Blueprint(
        name=headers.get("name", ""),
        creator=headers.get("creator", ""),
        description=headers.get("description", ""),
        pieces=pieces,
    )

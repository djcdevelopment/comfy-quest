#!/usr/bin/env python3
"""Generate Quest Lab Gallery v2 runtime profiles and top-down previews.

The gallery is data, not hand-coded coordinates. Rune strokes come from LabRunes.cs,
prefab names and 2 m floor spans are checked against the committed runtime dump, and
the same profile model emits the C# plan, a machine-readable summary, and previews.

  python tools/component-packets/generate_gallery.py
  python tools/component-packets/generate_gallery.py --check

Writes:
  network/mod/ComfyQuestLab/Core/LabGalleryPlan.g.cs
  tools/component-packets/samples/gallery-profiles.json
  tools/component-packets/samples/gallery-plan*.png (when Pillow is installed)
"""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import sys
from pathlib import Path


HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
RUNES = REPO / "network" / "mod" / "ComfyQuestLab" / "Ui" / "LabRunes.cs"
OUT = REPO / "network" / "mod" / "ComfyQuestLab" / "Core" / "LabGalleryPlan.g.cs"
SUMMARY = HERE / "samples" / "gallery-profiles.json"
DEFAULT_PREVIEW = HERE / "samples" / "gallery-plan.png"
DEFAULT_DUMP = HERE / "samples" / "prefab-dump.json"

ORDER = [
    "Combat",
    "Harvest",
    "Inventory",
    "Building",
    "Crafting",
    "Progression",
    "World",
    "Social",
]

STATIONS = {
    "Combat": ("Greyling", "spawner", "a target that fights back, restocked on demand"),
    "Harvest": ("Birch1", "prop", "a tree to strike, with room for pickables"),
    "Inventory": ("piece_chest_wood", "piece", "a chest to empty, equip, and refill"),
    "Building": ("piece_workbench", "piece", "a workbench and room to build or repair"),
    "Crafting": ("smelter", "piece", "a smelter with room for cooking and fermenting"),
    "Progression": ("piece_workbench", "piece", "a bench and room to raise a skill"),
    "World": ("portal_wood", "piece", "a paired portal and world-state practice"),
    "Social": ("sign", "piece", "a sign to write and a place to speak"),
}

COMPACT_STATION_NOTES = {
    "Combat": "Greyling at the rune; bow and arrows at the spoke mouth",
    "Harvest": "ground Birch and bronze axe before the ascent portal",
    "Building": "hammer and wood directly in front of the bench",
    "Crafting": "coal directly in front of the smelter",
    "Progression": "nearby course actions raise skills",
    "Social": "the hub sign says sign here",
}

SCHOOL_COLOURS = {
    "Combat": (1.00, 0.28, 0.22),
    "Harvest": (0.45, 0.95, 0.40),
    "Inventory": (0.95, 0.78, 0.30),
    "Building": (0.98, 0.55, 0.20),
    "Crafting": (0.55, 0.80, 1.00),
    "Progression": (0.80, 0.50, 1.00),
    "World": (0.35, 0.90, 0.90),
    "Social": (1.00, 0.70, 0.85),
}

COMMON_PALETTE = {
    "panel": "blackmarble_floor_large",
    "wall": "blackmarble_2x2x1",
    "column": "blackmarble_column_1",
    "sign": "sign",
    "beam": "wood_beam",
}

# These are intentionally meaningfully different rather than tiny tuning variants. The
# first preserves the already-proven build as a comparison baseline. Both v2 choices use
# black marble for every walking-surface cell; live comparison selected the grand profile.
PROFILE_SPECS = [
    {
        "id": "classic",
        "name": "Classic ring",
        "description": "The proven mixed stone/marble footprint retained for comparison.",
        "ring_radius": 38.0,
        "rune_width": 9.0,
        "rune_height": 11.0,
        "rune_base_y": 0.5,
        "platform_clearance": 0.6,
        "rune_name_headers": False,
        "beam_length": 2.0,
        "station_inset": 6.5,
        "rack_radius": 6.0,
        "tile": 2.0,
        "plaza_radius": 9.0,
        "hall_half_width": 2.0,
        "pad_half_width": 7.0,
        "pad_depth": 9.0,
        "rune_gap": 4.0,
        "stage_depth": 8.0,
        "stage_half_width": 8.0,
        "wall_courses": 2,
        "floor": {
            "plaza": "stone_floor_2x2",
            "hall": "blackmarble_floor",
            "pad": "blackmarble_floor",
            "stage": "blackmarble_floor",
        },
    },
    {
        "id": "marble-wide",
        "name": "Marble wide",
        "description": "All-marble floor, 8 m halls, larger runes, and illuminated horizontal rune headers.",
        "ring_radius": 50.0,
        "rune_width": 11.0,
        "rune_height": 14.0,
        "rune_base_y": 0.75,
        "platform_clearance": 1.5,
        "rune_name_headers": True,
        "beam_length": 2.0,
        "station_inset": 8.0,
        "rack_radius": 8.0,
        "tile": 2.0,
        "plaza_radius": 13.0,
        "hall_half_width": 4.0,
        "pad_half_width": 10.0,
        "pad_depth": 14.0,
        "rune_gap": 5.0,
        "stage_depth": 10.0,
        "stage_half_width": 10.0,
        "wall_courses": 2,
        "floor": {
            "plaza": "blackmarble_floor",
            "hall": "blackmarble_floor",
            "pad": "blackmarble_floor",
            "stage": "blackmarble_floor",
        },
    },
    {
        "id": "marble-grand",
        "name": "Marble grand",
        "description": "Selected compact court: a ground welcome camp, 10 m quarter-length halls, and a high sheltered marble canopy with visible hanging braziers.",
        "ring_radius": 27.0,
        "rune_width": 14.0,
        "rune_height": 17.0,
        "rune_base_y": 1.0,
        # r17 proved the 32 m canopy-clear deck crossed Valheim's altitude-driven snow
        # treatment: every upward face went white while the same shared material stayed
        # non-emissive. Keep the court modestly above terrain instead and recoverably
        # prune TreeBase instances from its generated footprint before placing anything.
        "platform_clearance": 6.0,
        "prune_natural_trees": True,
        # A black-marble floor slab is already a valid Valheim roof: roof checks raycast
        # against any non-leaky piece collider. Copy the hub/hall/pad floor cells at an
        # 16 m ceiling height; leave the rune stages open because their glyphs reach 17 m.
        # The r18 live pass selected the shelter but asked for another 8 m of headroom.
        "roof_clearance": 16.0,
        "roof_material": "blackmarble_floor",
        "roof_kinds": ("plaza", "hall", "pad"),
        "ceiling_braziers": True,
        "rune_name_headers": True,
        "compact_course": True,
        "ground_portal": (8.0, 0.0),
        "welcome_anchor": (-3.0, 0.0),
        "beam_length": 2.0,
        "station_inset": 3.0,
        "tile": 2.0,
        "plaza_radius": 14.0,
        "hall_half_width": 5.0,
        "pad_half_width": 8.0,
        "pad_depth": 8.0,
        "rune_gap": 3.0,
        "stage_depth": 8.0,
        "stage_half_width": 8.0,
        "wall_courses": 3,
        "floor": {
            "plaza": "blackmarble_floor",
            "hall": "blackmarble_floor",
            "pad": "blackmarble_floor",
            "stage": "blackmarble_floor",
        },
    },
]

DEFAULT_PROFILE = "marble-grand"
PANEL_SIZE = 8.0
BACKDROP_COLUMNS = 2
BACKDROP_ROWS = 2
WING_ROWS = 2
WALL_INSET = 0.5
WALL_YAW_OFFSET = 90.0
FIXED_PLACED_OBJECTS = 11  # 3 portals + 8 school stations
PICNIC_TABLE_TOP = 0.84  # piece_table is 0.83332 m tall in the committed prefab dump
CEILING_BRAZIER = "piece_brazierceiling01"
# The committed prefab survey measures this piece at 1.945 m tall. Its pivot location is
# deliberately irrelevant: runtime bounds align its topmost mesh point to the underface.


def read_rune_segments(path: Path) -> dict[str, list[tuple[float, float, float, float]]]:
    source = path.read_text(encoding="utf-8")
    blocks = re.findall(r"\{ LabCategory\.(\w+), new\[\] \{(.*?)\} \},", source, re.S)
    parsed = {}
    for name, body in blocks:
        parsed[name] = [
            tuple(float(value) for value in match)
            for match in re.findall(
                r"new Seg\(([\d.]+)f?,\s*([\d.]+)f?,\s*([\d.]+)f?,\s*([\d.]+)f?\)",
                body.replace("f", ""),
            )
        ]
    return parsed


def hex_of(rgb: tuple[float, float, float]) -> str:
    values = tuple(max(0, min(255, int(round(component * 255)))) for component in rgb)
    return "#%02x%02x%02x" % values


def sign_text(category: str, note: str) -> str:
    heading = hex_of(SCHOOL_COLOURS[category])
    return (
        f"<size=28><b><color={heading}>{category.upper()}</color></b></size>\n"
        f"{note}\n<color=#8fdc8f>safe events can bind here</color>"
    )


def rune_letter_text(category: str, letter: str) -> str:
    heading = hex_of(SCHOOL_COLOURS[category])
    return f"<size=44><b><color={heading}>{letter}</color></b></size>"


def monument_beams(segments, angle_deg: float, spec: dict):
    angle = math.radians(angle_deg)
    rune_radius = spec["ring_radius"] + spec["rune_gap"] + spec["stage_depth"] / 2.0
    cx, cz = rune_radius * math.sin(angle), rune_radius * math.cos(angle)
    rx, rz = math.sin(angle + math.pi / 2.0), math.cos(angle + math.pi / 2.0)
    beams = []
    for x1, y1, x2, y2 in segments:
        ax = (x1 - 0.5) * spec["rune_width"]
        ay = (1.0 - y1) * spec["rune_height"]
        bx = (x2 - 0.5) * spec["rune_width"]
        by = (1.0 - y2) * spec["rune_height"]
        length = math.hypot(bx - ax, by - ay)
        count = max(1, int(round(length / spec["beam_length"])))
        for index in range(count):
            t0, t1 = index / count, (index + 1) / count
            mx = ax + (bx - ax) * (t0 + t1) / 2.0
            my = ay + (by - ay) * (t0 + t1) / 2.0
            beams.append(
                {
                    "x": round(cx + rx * mx, 3),
                    "y": round(spec["rune_base_y"] + my, 3),
                    "z": round(cz + rz * mx, 3),
                    "dx": round(rx * (bx - ax) / (length or 1.0), 4),
                    "dy": round((by - ay) / (length or 1.0), 4),
                    "dz": round(rz * (bx - ax) / (length or 1.0), 4),
                }
            )
    return beams, (cx, cz)


def build_monuments(spec: dict, segments: dict):
    monuments = []
    for index, category in enumerate(ORDER):
        angle = index * (360.0 / len(ORDER))
        beams, (cx, cz) = monument_beams(segments[category], angle, spec)
        radians = math.radians(angle)
        prefab, kind, note = STATIONS[category]
        if spec.get("compact_course"):
            note = COMPACT_STATION_NOTES.get(category, note)
        station_radius = spec["ring_radius"] - spec["station_inset"]
        station_x = station_radius * math.sin(radians)
        station_z = station_radius * math.cos(radians)
        station_yaw = (angle + 180.0) % 360.0
        station_y = 0.0
        station_at_ground = False
        station_text = ""
        station_light = ""
        if spec.get("compact_course") and category == "Harvest":
            station_x, station_z, station_yaw = 5.0, 2.5, 0.0
            station_at_ground = True
        elif spec.get("compact_course") and category == "Social":
            station_x, station_z, station_yaw = 3.5, 6.0, 180.0
            station_y = 1.7
            station_text = "sign here"
            station_light = category.lower()
        monuments.append(
            {
                "category": category,
                "angle": angle,
                "cx": round(cx, 3),
                "cz": round(cz, 3),
                "beams": beams,
                "station": {
                    "prefab": prefab,
                    "kind": kind,
                    "note": note,
                    "x": round(station_x, 3),
                    "y": round(station_y, 3),
                    "z": round(station_z, 3),
                    "yaw": round(station_yaw, 3),
                    "text": station_text,
                    "atGround": station_at_ground,
                    "lightSchool": station_light,
                },
            }
        )
    return monuments


def build_course_drops(spec: dict):
    """Put each consumable where its interaction happens, not in a central gear ring."""

    def spoke(category: str, along: float, across: float = 0.0) -> tuple[float, float]:
        angle = math.radians(ORDER.index(category) * (360.0 / len(ORDER)))
        sx, sz = math.sin(angle), math.cos(angle)
        px, pz = math.cos(angle), -math.sin(angle)
        return sx * along + px * across, sz * along + pz * across

    def item(
        prefab: str,
        x: float,
        z: float,
        stack: int,
        note: str,
        *,
        at_ground: bool = False,
    ) -> dict:
        return {
            "prefab": prefab,
            "note": note,
            "x": round(x, 3),
            "y": 0.4,
            "z": round(z, 3),
            "stack": stack,
            "atGround": at_ground,
        }

    station_radius = spec["ring_radius"] - spec["station_inset"]
    combat_ready = spec["plaza_radius"] + 2.0
    building_ready = station_radius - 3.0
    crafting_ready = station_radius - 3.0
    bow_x, bow_z = spoke("Combat", combat_ready, -1.2)
    arrow_x, arrow_z = spoke("Combat", combat_ready, 1.2)
    hammer_x, hammer_z = spoke("Building", building_ready, -1.2)
    wood_x, wood_z = spoke("Building", building_ready, 1.2)
    coal_x, coal_z = spoke("Crafting", crafting_ready)
    axe_x, axe_z = (5.5, 0.8) if spec.get("compact_course") else spoke(
        "Harvest", station_radius - 3.0
    )

    axe_note = (
        "bronze axe beside the ground welcome Birch"
        if spec.get("compact_course")
        else "bronze axe beside the Harvest Birch"
    )
    drops = [
        item(
            "AxeBronze",
            axe_x,
            axe_z,
            1,
            axe_note,
            at_ground=bool(spec.get("compact_course")),
        ),
        item("Bow", bow_x, bow_z, 1, "bow on the player side of the combat spoke"),
        item("ArrowWood", arrow_x, arrow_z, 100, "arrows beside the combat bow"),
        item("Hammer", hammer_x, hammer_z, 1, "hammer in front of the building bench"),
        item("Wood", wood_x, wood_z, 50, "wood beside the building hammer"),
        item("Coal", coal_x, coal_z, 20, "coal directly in front of the smelter"),
    ]
    if not spec.get("compact_course"):
        drops.extend(
            [
                item("CookedMeat", 1.5, 3.5, 10, "health food at the arrival hub"),
                item("QueensJam", 3.5, 3.5, 10, "stamina food at the arrival hub"),
                item("Honey", 5.5, 3.5, 10, "quick stamina food at the arrival hub"),
            ]
        )
    return drops


def build_welcome_fixtures(spec: dict):
    """A terrain-level arrival vignette before the selected profile's ascent portal."""

    if not spec.get("compact_course"):
        return []

    def welcome(
        prefab: str,
        x: float,
        y: float,
        z: float,
        yaw: float,
        note: str,
        attached_item: str = "",
    ) -> dict:
        return {
            "prefab": prefab,
            "attachedItem": attached_item,
            "note": note,
            "x": round(x, 3),
            "y": round(y, 3),
            "z": round(z, 3),
            "yaw": round(yaw, 3),
        }

    # Coordinates are relative to welcome_anchor. All six share its terrain height, so
    # the displays stay on the table even when the wider build footprint crosses a slope.
    return [
        welcome("piece_table", 0.0, 0.0, 0.0, 0.0, "welcome picnic table"),
        welcome("piece_bench01", 0.0, 0.0, -1.35, 0.0, "south picnic bench"),
        welcome("piece_bench01", 0.0, 0.0, 1.35, 0.0, "north picnic bench"),
        welcome(
            "itemstandh",
            -0.8,
            PICNIC_TABLE_TOP,
            0.0,
            0.0,
            "health food display",
            "CookedMeat",
        ),
        welcome(
            "itemstandh",
            0.0,
            PICNIC_TABLE_TOP,
            0.0,
            0.0,
            "stamina food display",
            "QueensJam",
        ),
        welcome(
            "itemstandh",
            0.8,
            PICNIC_TABLE_TOP,
            0.0,
            0.0,
            "quick stamina food display",
            "Bread|CarrotSoup|Sausages|TurnipStew",
        ),
    ]


def platform_tiles(spec: dict, monuments: list[dict]):
    tile = spec["tile"]
    reach = spec["ring_radius"] + spec["rune_gap"] + spec["stage_depth"] + 2.0
    steps = int(reach / tile) + 2
    rays = [
        (math.sin(math.radians(monument["angle"])), math.cos(math.radians(monument["angle"])))
        for monument in monuments
    ]
    tiles = {}
    for i in range(-steps, steps + 1):
        for j in range(-steps, steps + 1):
            x, z = i * tile, j * tile
            if math.hypot(x, z) <= spec["plaza_radius"]:
                tiles[(x, z)] = "plaza"
                continue
            for sx, sz in rays:
                along = x * sx + z * sz
                across = abs(x * sz - z * sx)
                if along <= 0.0:
                    continue
                if (
                    across <= spec["hall_half_width"]
                    and along <= spec["ring_radius"] - spec["pad_depth"] / 2.0
                ):
                    tiles.setdefault((x, z), "hall")
                    break
                if (
                    across <= spec["pad_half_width"]
                    and abs(along - spec["ring_radius"] + spec["pad_depth"] / 2.0 - 1.0)
                    <= spec["pad_depth"] / 2.0
                ):
                    tiles.setdefault((x, z), "pad")
                    break
                if (
                    across <= spec["stage_half_width"]
                    and spec["ring_radius"] + spec["rune_gap"]
                    <= along
                    <= spec["ring_radius"] + spec["rune_gap"] + spec["stage_depth"]
                ):
                    tiles.setdefault((x, z), "stage")
                    break
    return [
        {"x": x, "z": z, "kind": kind, "prefab": spec["floor"][kind]}
        for (x, z), kind in sorted(tiles.items())
    ]


def roof_tiles(spec: dict, tiles: list[dict]):
    """Clone selected floor cells into a level, collider-backed stone canopy."""

    material = spec.get("roof_material")
    kinds = set(spec.get("roof_kinds", ()))
    if not material or not kinds:
        return []
    return [
        {"x": tile["x"], "z": tile["z"], "kind": tile["kind"], "prefab": material}
        for tile in tiles
        if tile["kind"] in kinds
    ]


def ceiling_fixtures(spec: dict, monuments: list[dict]):
    """One real hanging brazier at the hub and one midway down each roofed hall."""

    if not spec.get("ceiling_braziers"):
        return []
    inner_end = spec["ring_radius"] - spec["pad_depth"] / 2.0
    # Keep the flame beyond the letter banner at 55% of the throat; the first draft put
    # both at the same X/Z and would have hung a brazier through the readable word.
    along = spec["plaza_radius"] + (inner_end - spec["plaza_radius"]) * 0.82
    fixtures = [
        {
            "prefab": CEILING_BRAZIER,
            "x": 0.0,
            # Y is the roof-underface attachment plane, not a guessed prefab pivot.
            # Runtime measurement moves the topmost mesh point onto this plane.
            "y": spec["roof_clearance"],
            "z": 0.0,
            "yaw": 0.0,
            "infiniteFuel": True,
        }
    ]
    for monument in monuments:
        angle = math.radians(monument["angle"])
        fixtures.append(
            {
                "prefab": CEILING_BRAZIER,
                "x": round(math.sin(angle) * along, 3),
                "y": spec["roof_clearance"],
                "z": round(math.cos(angle) * along, 3),
                "yaw": round(monument["angle"], 3),
                "infiniteFuel": True,
            }
        )
    return fixtures


def backdrop_panels(spec: dict, monuments: list[dict]):
    panels = []
    back_along = spec["ring_radius"] + spec["rune_gap"] + spec["stage_depth"]
    for monument in monuments:
        angle = math.radians(monument["angle"])
        sx, sz = math.sin(angle), math.cos(angle)
        px, pz = math.cos(angle), -math.sin(angle)
        for column in range(BACKDROP_COLUMNS):
            offset = (column - (BACKDROP_COLUMNS - 1) / 2.0) * PANEL_SIZE
            for row in range(BACKDROP_ROWS):
                panels.append(
                    fixture(
                        COMMON_PALETTE["panel"],
                        sx * back_along + px * offset,
                        PANEL_SIZE / 2.0 + row * PANEL_SIZE,
                        sz * back_along + pz * offset,
                        monument["angle"],
                        "panel",
                    )
                )
        for side in (-1.0, 1.0):
            offset = side * (BACKDROP_COLUMNS * PANEL_SIZE / 2.0)
            along = back_along - PANEL_SIZE / 2.0
            for row in range(WING_ROWS):
                panels.append(
                    fixture(
                        COMMON_PALETTE["panel"],
                        sx * along + px * offset,
                        PANEL_SIZE / 2.0 + row * PANEL_SIZE,
                        sz * along + pz * offset,
                        monument["angle"] + 90.0,
                        "panel",
                    )
                )
    return panels


def hall_walls(spec: dict, monuments: list[dict]):
    walls = []
    inner_end = spec["ring_radius"] - spec["pad_depth"] / 2.0
    for monument in monuments:
        angle = math.radians(monument["angle"])
        sx, sz = math.sin(angle), math.cos(angle)
        px, pz = math.cos(angle), -math.sin(angle)
        along = spec["plaza_radius"]
        while along <= inner_end + 1e-6:
            for side in (-1.0, 1.0):
                offset = side * (spec["hall_half_width"] + WALL_INSET)
                x, z = sx * along + px * offset, sz * along + pz * offset
                for course in range(spec["wall_courses"]):
                    walls.append(
                        fixture(
                            COMMON_PALETTE["wall"],
                            x,
                            course * 2.0,
                            z,
                            monument["angle"] + WALL_YAW_OFFSET,
                        )
                    )
            along += spec["tile"]
    return walls


def hall_signs(spec: dict, monuments: list[dict]):
    signs = []
    for monument in monuments:
        angle = math.radians(monument["angle"])
        sx, sz = math.sin(angle), math.cos(angle)
        px, pz = math.cos(angle), -math.sin(angle)
        along = spec["plaza_radius"] + 1.0
        offset = spec["hall_half_width"] + WALL_INSET + 0.6
        signs.append(
            fixture(
                COMMON_PALETTE["sign"],
                sx * along + px * offset,
                1.2,
                sz * along + pz * offset,
                monument["angle"] + 180.0,
                text=sign_text(monument["category"], monument["station"]["note"]),
            )
        )
    return signs


def rune_name_signs(spec: dict, monuments: list[dict]):
    if not spec["rune_name_headers"]:
        return []
    signs = []
    inner_end = spec["ring_radius"] - spec["pad_depth"] / 2.0
    # Derek's r10 pass liked the one-letter horizontal treatment but showed it floating
    # above the far rune like a distant sky label. Stage the word as an entrance banner:
    # just past the hub into the spoke throat, and 0.75 m above the wall courses.
    along = spec["plaza_radius"] + (inner_end - spec["plaza_radius"]) * 0.55
    y = spec["wall_courses"] * 2.0 + 0.75
    for monument in monuments:
        angle = math.radians(monument["angle"])
        sx, sz = math.sin(angle), math.cos(angle)
        px, pz = math.cos(angle), -math.sin(angle)
        name = monument["category"].upper()
        # A vanilla sign is only one metre wide. Putting the whole school name on it
        # makes Valheim wrap one character per line at this display size, which the r5
        # live screenshot caught immediately. One letter per sign produces a durable,
        # genuinely horizontal word without relying on unsaved transform scaling.
        spacing = min(1.3, (spec["rune_width"] - 1.0) / max(1, len(name) - 1))
        lit_index = len(name) // 2
        for index, letter in enumerate(name):
            offset = (index - (len(name) - 1) / 2.0) * spacing
            signs.append(
                fixture(
                    COMMON_PALETTE["sign"],
                    sx * along + px * offset,
                    y,
                    sz * along + pz * offset,
                    monument["angle"] + 180.0,
                    orient="rune-name-lit" if index == lit_index else "rune-name",
                    text=rune_letter_text(monument["category"], letter),
                    light_school=monument["category"].lower() if index == lit_index else "",
                    text_glow_school=monument["category"].lower(),
                )
            )
    return signs


def fixture(
    prefab,
    x,
    y,
    z,
    yaw,
    orient="",
    text="",
    light_school="",
    text_glow_school="",
):
    return {
        "prefab": prefab,
        "x": round(x, 3),
        "y": round(y, 3),
        "z": round(z, 3),
        "yaw": round(yaw % 360.0, 3),
        "orient": orient,
        "text": text,
        "lightSchool": light_school,
        "textGlowSchool": text_glow_school,
    }


def build_profile(spec: dict, segments: dict):
    monuments = build_monuments(spec, segments)
    tiles = platform_tiles(spec, monuments)
    canopy = roof_tiles(spec, tiles)
    hanging = ceiling_fixtures(spec, monuments)
    fixtures = (
        hall_walls(spec, monuments)
        + backdrop_panels(spec, monuments)
        + hall_signs(spec, monuments)
        + rune_name_signs(spec, monuments)
    )
    if spec.get("compact_course"):
        fixtures.append(
            fixture(
                "wood_pole2",
                3.5,
                0.0,
                6.0,
                0.0,
                orient="sign-post",
                text="",
            )
        )
    course_drops = build_course_drops(spec)
    welcome_fixtures = build_welcome_fixtures(spec)
    ground_portal_x, ground_portal_z = spec.get("ground_portal", (2.0, 0.0))
    welcome_x, welcome_z = spec.get("welcome_anchor", (0.0, 0.0))
    beam_count = sum(len(monument["beams"]) for monument in monuments)
    footprint = (
        spec["ring_radius"]
        + spec["rune_gap"]
        + spec["stage_depth"]
        + max(spec["stage_half_width"], BACKDROP_COLUMNS * PANEL_SIZE / 2.0)
        + 2.0
    )
    floor_materials = sorted({tile["prefab"] for tile in tiles})
    return {
        "id": spec["id"],
        "name": spec["name"],
        "description": spec["description"],
        "ringRadius": spec["ring_radius"],
        "runeHeight": spec["rune_height"],
        "platformClearance": spec["platform_clearance"],
        "pruneNaturalTrees": bool(spec.get("prune_natural_trees")),
        "roofClearance": spec.get("roof_clearance", 0.0),
        "roofMaterials": sorted({tile["prefab"] for tile in canopy}),
        "ceilingFixtureHeights": sorted({fixture["y"] for fixture in hanging}),
        "groundPortalX": ground_portal_x,
        "groundPortalZ": ground_portal_z,
        "welcomeAnchorX": welcome_x,
        "welcomeAnchorZ": welcome_z,
        "beamLength": spec["beam_length"],
        "hallWidth": spec["hall_half_width"] * 2.0,
        "spokeLength": (
            spec["ring_radius"] - spec["pad_depth"] / 2.0 - spec["plaza_radius"]
        ),
        "floorMaterials": floor_materials,
        "solidMarbleFloor": floor_materials == ["blackmarble_floor"],
        "footprintRadius": footprint,
        "monuments": monuments,
        "tiles": tiles,
        "roofTiles": canopy,
        "ceilingFixtures": hanging,
        "fixtures": fixtures,
        "courseDrops": course_drops,
        "welcomeFixtures": welcome_fixtures,
        "counts": {
            "floorTiles": len(tiles),
            "roofTiles": len(canopy),
            "ceilingFixtures": len(hanging),
            "fixtures": len(fixtures),
            "runeBeams": beam_count,
            "runeNameHeaders": len(ORDER) if spec["rune_name_headers"] else 0,
            "runeNameSigns": sum(
                fixture["orient"].startswith("rune-name") for fixture in fixtures
            ),
            "runeNameLights": sum(bool(fixture["lightSchool"]) for fixture in fixtures),
            "courseDrops": len(course_drops),
            "welcomeFixtures": len(welcome_fixtures),
            "estimatedPlacedObjects": (
                len(tiles)
                + len(canopy)
                + len(hanging)
                + len(fixtures)
                + beam_count
                + len(course_drops)
                + len(welcome_fixtures)
                + FIXED_PLACED_OBJECTS
            ),
        },
    }


def validate_profiles(profiles: list[dict], dump_path: Path) -> int:
    data = json.loads(dump_path.read_text(encoding="utf-8"))
    entries = {entry["name"]: entry for entry in data.get("prefabs", [])}
    wanted = set(COMMON_PALETTE.values()) | {"wood_floor", "wood_pole"}
    wanted.update(prefab for prefab, _, _ in STATIONS.values())
    for profile in profiles:
        wanted.update(profile["floorMaterials"])
        wanted.update(profile["roofMaterials"])
        wanted.update(item["prefab"] for item in profile["fixtures"])
        wanted.update(item["prefab"] for item in profile["ceilingFixtures"])
        wanted.update(item["prefab"] for item in profile["courseDrops"])
        wanted.update(item["prefab"] for item in profile["welcomeFixtures"])
        wanted.update(
            candidate
            for item in profile["welcomeFixtures"]
            for candidate in item["attachedItem"].split("|")
            if candidate
        )
    missing = sorted(wanted - set(entries))
    if missing:
        raise SystemExit("gallery prefab(s) absent from dump: " + ", ".join(missing))

    for profile in profiles:
        for floor in sorted(set(profile["floorMaterials"] + profile["roofMaterials"])):
            snaps = entries[floor].get("snapPoints") or []
            if not snaps:
                raise SystemExit(f"{floor} has no snap points; floor span is unverified")
            span_x = max(point[0] for point in snaps) - min(point[0] for point in snaps)
            span_z = max(point[2] for point in snaps) - min(point[2] for point in snaps)
            if abs(span_x - 2.0) > 0.05 or abs(span_z - 2.0) > 0.05:
                raise SystemExit(f"{floor} spans {span_x:.2f} x {span_z:.2f}, not the 2 m grid")

    ids = [profile["id"] for profile in profiles]
    if len(ids) != len(set(ids)) or DEFAULT_PROFILE not in ids:
        raise SystemExit("gallery profile ids must be unique and include the default")
    for profile in profiles:
        if profile["platformClearance"] <= 0.0:
            raise SystemExit(f"profile {profile['id']} has no platform clearance")
        if profile["id"] != "classic" and not profile["solidMarbleFloor"]:
            raise SystemExit(f"v2 profile {profile['id']} is not an all-marble floor")
        if profile["id"] != "classic" and profile["hallWidth"] <= 4.0:
            raise SystemExit(f"v2 profile {profile['id']} did not widen the classic halls")
        if profile["id"] == DEFAULT_PROFILE:
            if not 5.0 <= profile["platformClearance"] <= 8.0:
                raise SystemExit("selected gallery clearance must stay below the witnessed snow line")
            if not profile["pruneNaturalTrees"]:
                raise SystemExit("selected low gallery does not recoverably prune natural trees")
            if profile["roofClearance"] < profile["runeHeight"] * 0.4:
                raise SystemExit("selected gallery roof is too low for its hall banners")
            if profile["roofMaterials"] != ["blackmarble_floor"]:
                raise SystemExit("selected gallery roof is not cloned black marble")
            if profile["counts"]["roofTiles"] <= 0:
                raise SystemExit("selected gallery has no generated roof cells")
            if profile["counts"]["ceilingFixtures"] != len(ORDER) + 1:
                raise SystemExit("selected gallery needs one hub and eight hall braziers")
            if any(tile["kind"] == "stage" for tile in profile["roofTiles"]):
                raise SystemExit("selected gallery roof blocks an open rune stage")
            for fixture in profile["ceilingFixtures"]:
                if not any(
                    abs(fixture["x"] - tile["x"]) <= 1.01
                    and abs(fixture["z"] - tile["z"]) <= 1.01
                    for tile in profile["roofTiles"]
                ):
                    raise SystemExit("selected gallery has a hanging brazier without roof")
            attached = {
                item["attachedItem"].split("|", 1)[0]
                for item in profile["welcomeFixtures"]
                if item["attachedItem"]
            }
            if attached != {"CookedMeat", "QueensJam", "Bread"}:
                raise SystemExit("selected gallery welcome table is missing mounted food")
        expected_headers = 0 if profile["id"] == "classic" else len(ORDER)
        expected_signs = 0 if profile["id"] == "classic" else sum(map(len, ORDER))
        if profile["counts"]["runeNameHeaders"] != expected_headers:
            raise SystemExit(
                f"profile {profile['id']} has {profile['counts']['runeNameHeaders']} "
                f"rune headers, expected {expected_headers}"
            )
        if profile["counts"]["runeNameSigns"] != expected_signs:
            raise SystemExit(
                f"profile {profile['id']} has {profile['counts']['runeNameSigns']} "
                f"rune-name signs, expected {expected_signs}"
            )
        if profile["counts"]["runeNameLights"] != expected_headers:
            raise SystemExit(
                f"profile {profile['id']} has {profile['counts']['runeNameLights']} "
                f"rune-name lights, expected {expected_headers}"
            )
    return len(entries)


def cs(text: str) -> str:
    return (
        '"'
        + text.replace("\\", "\\\\").replace('"', '\\"').replace("\r", "\\r").replace("\n", "\\n")
        + '"'
    )


def f(value) -> str:
    number = float(value)
    if number.is_integer():
        return f"{int(number)}f"
    return f"{number:g}f"


def render_csharp(profiles: list[dict]) -> str:
    lines = [
        "// <auto-generated>",
        "//   Generated by tools/component-packets/generate_gallery.py.",
        "//   Rune strokes come from Ui/LabRunes.cs and profile prefab names are checked",
        "//   against samples/prefab-dump.json. Do not edit by hand.",
        "// </auto-generated>",
        "",
        "namespace ComfyQuestLab;",
        "",
        "using System;",
        "",
        "/// <summary>Gallery v2 profiles, relative to a player-selected world origin.</summary>",
        "public static class LabGalleryPlan {",
        "  public const int PlanVersion = 8;",
        f"  public const string DefaultProfileId = {cs(DEFAULT_PROFILE)};",
        "",
        "  public struct Beam { public float X, Y, Z, Dx, Dy, Dz; }",
        "  public struct Station { public string Prefab, Kind, Note, Text, LightSchool;",
        "                          public float X, Y, Z, Yaw; public bool AtGround; }",
        "  public struct Monument { public string Category; public float Angle, Cx, Cz;",
        "                           public float R, G, B;",
        "                           public Beam[] Beams; public Station Station; }",
        "  public struct CourseDrop { public string Prefab, Note;",
        "                             public float X, Y, Z; public int Stack; public bool AtGround; }",
        "  public struct WelcomeFixture { public string Prefab, AttachedItem, Note;",
        "                                 public float X, Y, Z, Yaw; }",
        "  public struct Tile { public float X, Z; public string Prefab; }",
        "  public struct CeilingFixture { public string Prefab; public float X, Y, Z, Yaw;",
        "                                 public bool InfiniteFuel; }",
        "  public struct Fixture { public string Prefab; public float X, Y, Z, Yaw;",
        "                          public string Orient, Text, LightSchool, TextGlowSchool; }",
        "",
        "  public sealed class Profile {",
        "    public string Id, Name, Description;",
        "    public float RingRadius, RuneHeight, BeamLength, HallWidth, SpokeLength, FootprintRadius;",
        "    public float PlatformClearance, RoofClearance, GroundPortalX, GroundPortalZ;",
        "    public float WelcomeAnchorX, WelcomeAnchorZ;",
        "    public bool SolidMarbleFloor, PruneNaturalTrees;",
        "    public int EstimatedPlacedObjects, RuneNameHeaders, RuneNameSigns, RuneNameLights;",
        "    public string[] FloorMaterials, RoofMaterials;",
        "    public Monument[] Monuments;",
        "    public Tile[] PlatformTiles;",
        "    public Tile[] RoofTiles;",
        "    public CeilingFixture[] CeilingFixtures;",
        "    public Fixture[] Fixtures;",
        "    public CourseDrop[] CourseDrops;",
        "    public WelcomeFixture[] WelcomeFixtures;",
        "  }",
        "",
        "  public static readonly Profile[] Profiles = {",
    ]
    for profile in profiles:
        lines.extend(
            [
                "    new Profile {",
                f"      Id = {cs(profile['id'])}, Name = {cs(profile['name'])},",
                f"      Description = {cs(profile['description'])},",
                f"      RingRadius = {f(profile['ringRadius'])}, RuneHeight = {f(profile['runeHeight'])},",
                f"      BeamLength = {f(profile['beamLength'])}, HallWidth = {f(profile['hallWidth'])},",
                f"      SpokeLength = {f(profile['spokeLength'])},",
                f"      PlatformClearance = {f(profile['platformClearance'])},",
                f"      RoofClearance = {f(profile['roofClearance'])},",
                f"      GroundPortalX = {f(profile['groundPortalX'])}, GroundPortalZ = {f(profile['groundPortalZ'])},",
                f"      WelcomeAnchorX = {f(profile['welcomeAnchorX'])}, WelcomeAnchorZ = {f(profile['welcomeAnchorZ'])},",
                f"      FootprintRadius = {f(profile['footprintRadius'])},",
                f"      SolidMarbleFloor = {str(profile['solidMarbleFloor']).lower()},",
                f"      PruneNaturalTrees = {str(profile['pruneNaturalTrees']).lower()},",
                f"      EstimatedPlacedObjects = {profile['counts']['estimatedPlacedObjects']},",
                f"      RuneNameHeaders = {profile['counts']['runeNameHeaders']},",
                f"      RuneNameSigns = {profile['counts']['runeNameSigns']},",
                f"      RuneNameLights = {profile['counts']['runeNameLights']},",
                "      FloorMaterials = new[] { "
                + ", ".join(cs(item) for item in profile["floorMaterials"])
                + " },",
                "      RoofMaterials = new string[] { "
                + ", ".join(cs(item) for item in profile["roofMaterials"])
                + " },",
                "      Monuments = new[] {",
            ]
        )
        for monument in profile["monuments"]:
            red, green, blue = SCHOOL_COLOURS[monument["category"]]
            station = monument["station"]
            lines.extend(
                [
                    "        new Monument {",
                    f"          Category = LabCategory.{monument['category']},",
                    f"          Angle = {f(monument['angle'])}, Cx = {f(monument['cx'])}, Cz = {f(monument['cz'])},",
                    f"          R = {f(red)}, G = {f(green)}, B = {f(blue)},",
                    "          Station = new Station { "
                    f"Prefab = {cs(station['prefab'])}, Kind = {cs(station['kind'])}, "
                    f"Note = {cs(station['note'])}, Text = {cs(station['text'])}, "
                    f"LightSchool = {cs(station['lightSchool'])}, "
                    f"X = {f(station['x'])}, Y = {f(station['y'])}, Z = {f(station['z'])}, "
                    f"Yaw = {f(station['yaw'])}, AtGround = {str(station['atGround']).lower()} }},",
                    "          Beams = new[] {",
                ]
            )
            for beam in monument["beams"]:
                lines.append(
                    "            new Beam { "
                    f"X = {f(beam['x'])}, Y = {f(beam['y'])}, Z = {f(beam['z'])}, "
                    f"Dx = {f(beam['dx'])}, Dy = {f(beam['dy'])}, Dz = {f(beam['dz'])} }},"
                )
            lines.extend(["          },", "        },"])
        lines.extend(["      },", "      PlatformTiles = new[] {"])
        for tile in profile["tiles"]:
            lines.append(
                f"        new Tile {{ X = {f(tile['x'])}, Z = {f(tile['z'])}, Prefab = {cs(tile['prefab'])} }},"
            )
        lines.extend(["      },", "      RoofTiles = new Tile[] {"])
        for tile in profile["roofTiles"]:
            lines.append(
                f"        new Tile {{ X = {f(tile['x'])}, Z = {f(tile['z'])}, Prefab = {cs(tile['prefab'])} }},"
            )
        lines.extend(["      },", "      CeilingFixtures = new CeilingFixture[] {"])
        for item in profile["ceilingFixtures"]:
            lines.append(
                "        new CeilingFixture { "
                f"Prefab = {cs(item['prefab'])}, X = {f(item['x'])}, Y = {f(item['y'])}, "
                f"Z = {f(item['z'])}, Yaw = {f(item['yaw'])}, "
                f"InfiniteFuel = {str(item['infiniteFuel']).lower()} }},"
            )
        lines.extend(["      },", "      Fixtures = new[] {"])
        for item in profile["fixtures"]:
            light = (
                f", LightSchool = {cs(item['lightSchool'])}"
                if item["lightSchool"]
                else ""
            )
            text_glow = (
                f", TextGlowSchool = {cs(item['textGlowSchool'])}"
                if item["textGlowSchool"]
                else ""
            )
            lines.append(
                "        new Fixture { "
                f"Prefab = {cs(item['prefab'])}, X = {f(item['x'])}, Y = {f(item['y'])}, "
                f"Z = {f(item['z'])}, Yaw = {f(item['yaw'])}, "
                f"Orient = {cs(item['orient'])}, Text = {cs(item['text'])}{light}{text_glow} }},"
            )
        lines.extend(["      },", "      CourseDrops = new[] {"])
        for item in profile["courseDrops"]:
            lines.append(
                "        new CourseDrop { "
                f"Prefab = {cs(item['prefab'])}, Note = {cs(item['note'])}, "
                f"X = {f(item['x'])}, Y = {f(item['y'])}, Z = {f(item['z'])}, "
                f"Stack = {item['stack']}, AtGround = {str(item['atGround']).lower()} }},"
            )
        lines.extend(["      },", "      WelcomeFixtures = new WelcomeFixture[] {"])
        for item in profile["welcomeFixtures"]:
            lines.append(
                "        new WelcomeFixture { "
                f"Prefab = {cs(item['prefab'])}, AttachedItem = {cs(item['attachedItem'])}, "
                f"Note = {cs(item['note'])}, X = {f(item['x'])}, Y = {f(item['y'])}, "
                f"Z = {f(item['z'])}, Yaw = {f(item['yaw'])} }},"
            )
        lines.extend(["      },", "    },"])
    lines.extend(
        [
            "  };",
            "",
            "  public static Profile Find(string id) {",
            "    string wanted = string.IsNullOrWhiteSpace(id) ? DefaultProfileId : id.Trim();",
            "    foreach (Profile profile in Profiles) {",
            "      if (string.Equals(profile.Id, wanted, StringComparison.OrdinalIgnoreCase)) {",
            "        return profile;",
            "      }",
            "    }",
            "    return null;",
            "  }",
            "",
            "  // Source-compatible default aliases used by seed/tests and older call sites.",
            "  public static Monument[] Monuments { get { return Find(DefaultProfileId).Monuments; } }",
            "  public static Tile[] PlatformTiles { get { return Find(DefaultProfileId).PlatformTiles; } }",
            "  public static Tile[] RoofTiles { get { return Find(DefaultProfileId).RoofTiles; } }",
            "  public static CeilingFixture[] CeilingFixtures { get { return Find(DefaultProfileId).CeilingFixtures; } }",
            "  public static Fixture[] Fixtures { get { return Find(DefaultProfileId).Fixtures; } }",
            "  public static CourseDrop[] CourseDrops { get { return Find(DefaultProfileId).CourseDrops; } }",
            "  public static WelcomeFixture[] WelcomeFixtures { get { return Find(DefaultProfileId).WelcomeFixtures; } }",
            "  public static float RuneHeight { get { return Find(DefaultProfileId).RuneHeight; } }",
            "}",
            "",
        ]
    )
    return "\n".join(lines)


def summary_model(profiles: list[dict]) -> dict:
    return {
        "Schema": "comfy-questlab-gallery-profiles/v2",
        "DefaultProfile": DEFAULT_PROFILE,
        "ProfileCount": len(profiles),
        "Profiles": [
            {
                key: profile[key]
                for key in (
                    "id",
                    "name",
                    "description",
                    "ringRadius",
                    "runeHeight",
                    "platformClearance",
                    "pruneNaturalTrees",
                    "roofClearance",
                    "roofMaterials",
                    "ceilingFixtureHeights",
                    "groundPortalX",
                    "groundPortalZ",
                    "welcomeAnchorX",
                    "welcomeAnchorZ",
                    "hallWidth",
                    "spokeLength",
                    "footprintRadius",
                    "floorMaterials",
                    "solidMarbleFloor",
                    "counts",
                )
            }
            for profile in profiles
        ],
    }


def render_previews(profiles: list[dict]) -> None:
    try:
        from PIL import Image, ImageDraw
    except ImportError:
        print("  (Pillow absent - previews not rendered)")
        return

    colors = {
        "Combat": (237, 115, 102),
        "Harvest": (122, 204, 133),
        "Inventory": (217, 184, 107),
        "Building": (199, 153, 97),
        "Crafting": (235, 158, 77),
        "Progression": (245, 214, 107),
        "World": (117, 194, 235),
        "Social": (194, 168, 240),
    }

    images = []
    for profile in profiles:
        size = 720
        span = profile["footprintRadius"] * 2.0 + 8.0
        image = Image.new("RGB", (size, size), (11, 16, 19))
        draw = ImageDraw.Draw(image)

        def point(x, z):
            return (size / 2.0 + x / span * size, size / 2.0 - z / span * size)

        floor_fill = (25, 25, 31) if profile["solidMarbleFloor"] else (45, 40, 37)
        half = 2.0 / span * size / 2.0
        for tile in profile["tiles"]:
            px, pz = point(tile["x"], tile["z"])
            fill = floor_fill if tile["prefab"] == "blackmarble_floor" else (55, 48, 42)
            draw.rectangle([px - half, pz - half, px + half, pz + half], fill=fill)
        for tile in profile["roofTiles"]:
            px, pz = point(tile["x"], tile["z"])
            draw.rectangle(
                [px - half, pz - half, px + half, pz + half],
                outline=(58, 69, 78),
            )
        for fixture in profile["ceilingFixtures"]:
            px, pz = point(fixture["x"], fixture["z"])
            draw.ellipse([px - 3, pz - 3, px + 3, pz + 3], fill=(240, 140, 55))
        for item in profile["fixtures"]:
            px, pz = point(item["x"], item["z"])
            radius = 2 if item["text"] else 1
            draw.ellipse([px - radius, pz - radius, px + radius, pz + radius], fill=(105, 102, 118))
            if item["orient"].startswith("rune-name"):
                label = re.sub(r"<[^>]+>", "", item["text"])
                draw.text((px - len(label) * 3, pz - 6), label, fill=(210, 215, 220))
        for monument in profile["monuments"]:
            color = colors[monument["category"]]
            station_x, station_z = point(
                monument["station"]["x"], monument["station"]["z"]
            )
            draw.rectangle(
                [station_x - 4, station_z - 4, station_x + 4, station_z + 4],
                fill=color,
                outline=(245, 245, 245),
            )
            for beam in monument["beams"]:
                px, pz = point(beam["x"], beam["z"])
                draw.ellipse([px - 2, pz - 2, px + 2, pz + 2], fill=color)
        for drop in profile["courseDrops"]:
            px, pz = point(drop["x"], drop["z"])
            draw.ellipse([px - 3, pz - 3, px + 3, pz + 3], fill=(255, 220, 90))
        for welcome in profile["welcomeFixtures"]:
            px, pz = point(
                profile["welcomeAnchorX"] + welcome["x"],
                profile["welcomeAnchorZ"] + welcome["z"],
            )
            fill = (255, 220, 90) if welcome["attachedItem"] else (130, 92, 54)
            draw.rectangle([px - 3, pz - 3, px + 3, pz + 3], fill=fill)
        draw.text((12, 10), f"{profile['name']} ({profile['id']})", fill=(235, 240, 238))
        draw.text(
            (12, size - 24),
            f"{profile['hallWidth']:.0f} m halls | {profile['spokeLength']:.0f} m walks | "
            f"{profile['counts']['estimatedPlacedObjects']} objects",
            fill=(155, 170, 167),
        )
        path = HERE / "samples" / f"gallery-plan-{profile['id']}.png"
        image.save(path)
        images.append(image)
        if profile["id"] == DEFAULT_PROFILE:
            image.save(DEFAULT_PREVIEW)
        print(f"  {path}")

    composite = Image.new("RGB", (len(images) * 480, 480), (11, 16, 19))
    for index, image in enumerate(images):
        composite.paste(image.resize((480, 480)), (index * 480, 0))
    comparison = HERE / "samples" / "gallery-plan-comparison.png"
    composite.save(comparison)
    print(f"  {comparison}")


def write_or_check(path: Path, content: str, check: bool) -> None:
    if check:
        actual = path.read_text(encoding="utf-8") if path.exists() else None
        if actual != content:
            raise SystemExit(f"generated gallery artifact is stale: {path}")
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8", newline="\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--prefab-dump", type=Path, default=DEFAULT_DUMP)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    segments = read_rune_segments(RUNES)
    missing = [category for category in ORDER if category not in segments]
    if missing:
        raise SystemExit(f"LabRunes.cs is missing gallery segments for: {missing}")

    profiles = [build_profile(spec, segments) for spec in PROFILE_SPECS]
    known = validate_profiles(profiles, args.prefab_dump)
    csharp = render_csharp(profiles)
    summary = json.dumps(summary_model(profiles), indent=2, ensure_ascii=False) + "\n"
    write_or_check(OUT, csharp, args.check)
    write_or_check(SUMMARY, summary, args.check)

    if args.check:
        print(
            f"verified {len(profiles)} gallery profiles; default {DEFAULT_PROFILE}; "
            f"prefab dump {known} entries"
        )
        return 0

    print(f"  palette checked against {known} prefabs in {args.prefab_dump.name}")
    print(f"  {OUT}")
    print(f"  {SUMMARY}")
    for profile in profiles:
        counts = profile["counts"]
        print(
            f"  {profile['id']}: {profile['hallWidth']:.0f} m halls, "
            f"{counts['floorTiles']} floor, {counts['roofTiles']} roof, "
            f"{counts['ceilingFixtures']} ceiling fixtures, {counts['fixtures']} fixtures, "
            f"{counts['runeBeams']} beams, ~{counts['estimatedPlacedObjects']} objects"
        )
    render_previews(profiles)
    return 0


if __name__ == "__main__":
    sys.exit(main())

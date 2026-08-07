#!/usr/bin/env python3
"""Lay out the Tome's gallery, and render a plan of it.

A student should get value two minutes after downloading, not after an hour of
hunting creatures and crafting a bow. So the lab builds its own ground: eight rune
monuments in a ring, a station under each one for practising that school, and an
armoury at the centre so nothing has to be found or made.

The runes are raised from logs. They are already line segments — that is why they
draw well at fourteen pixels — and a line segment is also a beam. The SAME table in
Ui/LabRunes.cs drives both, so a monument cannot end up a different shape from the
glyph on its page.

Emits the plan as data rather than code-with-numbers-in-it, so this script and the
mod agree by construction, and so the preview below is a picture of what will
actually be built.

  python tools/component-packets/generate_gallery.py

Writes network/mod/ComfyQuestLab/Core/LabGalleryPlan.g.cs
      plus a top-down preview PNG next to it when Pillow is available.
"""
import json
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
RUNES = os.path.join(REPO, "network", "mod", "ComfyQuestLab", "Ui", "LabRunes.cs")
OUT = os.path.join(REPO, "network", "mod", "ComfyQuestLab", "Core", "LabGalleryPlan.g.cs")
PREVIEW = os.path.join(HERE, "samples", "gallery-plan.png")
DUMP = os.path.join(HERE, "samples", "prefab-dump.json")

# --- dimensions, in metres -------------------------------------------------------
RING_RADIUS = 38.0     # 30 m of arc between monuments — room to breathe, short spokes
RUNE_WIDTH = 9.0       # a rune reads from the centre of the ring at this size
RUNE_HEIGHT = 11.0
RUNE_BASE_Y = 0.5      # lifted clear of ground clutter
BEAM_LENGTH = 2.0      # a wood beam is 2 m; segments are cut into this many
STATION_INSET = 6.5    # station stands on the same pad as its monument, in front of it
RACK_RADIUS = 6.0      # armoury ring, close enough to spawn to be unmissable

# --- the platform ---------------------------------------------------------------
# Valheim ground is not flat, and 89 beams on a hillside is how this reads as broken
# rather than impressive. So the gallery brings its own floor.
#
# NOT a disc: a 38 m disc is roughly 1,100 tiles of which most are never walked on.
# A plaza, eight spokes and eight pads is a fraction of that and looks deliberate —
# a ritual floor rather than a car park. The shape also tells you where to go.
TILE = 2.0             # wood_floor is 2x2 m
PLAZA_RADIUS = 9.0
SPOKE_HALF_WIDTH = 2.0
PAD_HALF_WIDTH = 7.0   # wide enough to carry a 9 m rune with a margin
PAD_DEPTH = 9.0        # from just behind the monument to just in front of the station

ORDER = ["Combat", "Harvest", "Inventory", "Building", "Crafting", "Progression",
         "World", "Social"]

# What each school needs on the ground to be practisable in the first two minutes.
# Prefab names are the well-known Valheim ones; the builder logs any it cannot
# resolve rather than assuming, because this list is the one thing here that was
# not read out of the assembly.
STATIONS = {
    "Combat":      ("Greyling",       "spawner", "a target that fights back, respawned on demand"),
    "Harvest":     ("Birch1",         "prop",    "a tree to strike, a bush and a berry to pick"),
    "Inventory":   ("piece_chest_wood", "piece", "a chest to empty, with something in it"),
    "Building":    ("piece_workbench", "piece",  "a workbench, so pieces can be placed and repaired"),
    "Crafting":    ("smelter",        "piece",   "a smelter and a kiln, fuelled"),
    "Progression": ("piece_workbench", "piece",  "a bench, and room to swing until a skill rises"),
    "World":       ("portal_wood",    "piece",   "a portal pair, to travel between"),
    "Social":      ("sign",           "piece",   "a sign to write on"),
}

# The armoury. One weapon per stand, chosen so every school can be practised
# immediately: fists need nothing, but a bow without arrows teaches nothing.
ARMOURY = [
    ("AxeBronze",     "an axe, for trees"),
    ("Club",          "a club, for creatures"),
    ("Bow",           "a bow"),
    ("ArrowWood",     "arrows for it"),
    ("PickaxeBronze", "a pickaxe, for rock"),
    ("Hammer",        "a hammer, for building"),
    ("Cultivator",    "a cultivator"),
    ("Torch",         "a torch"),
]


# --- palette --------------------------------------------------------------------
# One place to swap materials. Every name here is cross-checked against a
# `questlab_prefabs dump` by --prefab-dump, because prefab names are the one thing in
# this project that is NOT read out of the assembly — the atlas knows which components
# exist, not which prefabs carry them.
#
# Stone underfoot in the plaza, black marble down the halls: the plaza reads as ground
# you stand on and the halls as something built. The rune strokes stay wood on purpose.
# A warm lit beam against cold marble is the whole composition — swap "beam" to a marble
# piece and the glyph stops being the brightest thing at the end of the corridor.
PALETTE = {
    "plaza":  "stone_floor_2x2",     # 2x2, verified by snap span
    "hall":   "blackmarble_floor",   # 2x2
    "pad":    "blackmarble_floor",
    "stage":  "blackmarble_floor",   # the rune's own platform, past the hall
    "panel":  "blackmarble_floor_large",  # 8 x 8 x 2, stood on edge as a backdrop
    "wall":   "blackmarble_2x2x1",   # 2 wide, 2 tall, 1 thick
    "column": "blackmarble_column_1",
    "sign":   "sign",                # ZDO "text" carries the copy
    "beam":   "wood_beam",           # the strokes; deliberately not marble
}

# Hall wall height, in courses of PALETTE["wall"] (2 m each). Two courses frames the
# corridor without roofing it — a 11 m rune still stands 7 m clear above the wall head,
# which is the shot: you read the sign, then walk toward a lit glyph against open sky.
WALL_COURSES = 2
WALL_INSET = 0.5        # wall centre sits this far outside the hall floor edge

# Yaw that turns a wall's 2 m width along the corridor rather than across it. This is
# the one number here that is a convention rather than a measurement; a wrong guess
# shows up as every wall rotated a quarter turn, and is fixed by changing this alone.
WALL_YAW_OFFSET = 90.0

# --- the rune stage --------------------------------------------------------------
# The rune gets its own platform past the end of the hall, with a gap of open air
# between it and the pad. Two problems answered at once: a chest or a hearth staged on
# the pad can no longer stand in front of the glyph, and a backdrop behind and to the
# sides gives it something black to burn against instead of open sky.
#
# The backdrop is blackmarble_floor_large stood on edge — 8 x 8 x 2, so four of them
# make a 16 m wall where the 2 m blocks would have taken forty. Its thin axis is its
# local Y, which is what the builder swings onto the hall's ray.
RUNE_GAP = 4.0              # open air between the pad edge and the stage
STAGE_DEPTH = 8.0
STAGE_HALF_WIDTH = 8.0      # 16 m across, a 9 m rune with margin either side
PANEL = 8.0                 # blackmarble_floor_large face, verified from snap points
BACKDROP_COLUMNS = 2        # 16 m wide
BACKDROP_ROWS = 2           # 16 m tall, clears an 11 m rune
WING_ROWS = 2               # side returns, same height

# Where the rune itself now stands: centred on its own stage rather than on the pad.
RUNE_RADIUS = RING_RADIUS + RUNE_GAP + STAGE_DEPTH / 2

# One colour per school, and this is the ONLY place it is written down. The plan carries
# it into the mod, so the rune lamp and the sign heading cannot drift apart.
SCHOOL_COLOURS = {
    "Combat":      (1.00, 0.28, 0.22),
    "Harvest":     (0.45, 0.95, 0.40),
    "Inventory":   (0.95, 0.78, 0.30),
    "Building":    (0.98, 0.55, 0.20),
    "Crafting":    (0.55, 0.80, 1.00),
    "Progression": (0.80, 0.50, 1.00),
    "World":       (0.35, 0.90, 0.90),
    "Social":      (1.00, 0.70, 0.85),
}

# Exactly one seam in the whole atlas can bind a quest today. Saying so on the sign at
# the mouth of every hall is the argument the lab exists to make, made before anyone
# walks in rather than after they have wasted an evening.
QUEST_USABLE = {"Combat"}


def hex_of(rgb):
    return "#%02x%02x%02x" % tuple(max(0, min(255, int(round(c * 255)))) for c in rgb)


def sign_text(category, note):
    """Copy for the sign at a hall mouth. Unity rich text: the Sign widget takes the
    same <b>/<color>/<size> tags the rest of the game's UI does, and the string rides
    to the piece in its own ZDO "text" field, which the component atlas reports as a
    plain read/write — so there is no RPC signature to guess at."""
    heading = hex_of(SCHOOL_COLOURS[category])
    verdict = ("a quest CAN bind here" if category in QUEST_USABLE
               else "nothing binds a quest here yet")
    verdict_colour = "#8fdc8f" if category in QUEST_USABLE else "#d08a72"
    return (f"<size=28><b><color={heading}>{category.upper()}</color></b></size>\n"
            f"{note}\n"
            f"<color={verdict_colour}>{verdict}</color>")


def validate_against_dump(dump_path):
    """Fail loudly on a palette name this game build does not have, and check that the
    pieces we lay on a 2 m grid really are 2 m — snap points are the piece's own
    statement of its footprint, so this catches a swap to a 4 m slab before 600 pieces
    go into somebody's world overlapping each other."""
    data = json.loads(open(dump_path, encoding="utf-8").read())
    entries = {e["name"]: e for e in data.get("prefabs", [])}

    missing = sorted(v for v in PALETTE.values() if v not in entries)
    if missing:
        raise SystemExit(
            "palette names absent from the prefab dump: " + ", ".join(missing)
            + " — fix PALETTE before regenerating the plan.")

    for key in ("plaza", "hall", "pad"):
        snaps = entries[PALETTE[key]].get("snapPoints") or []
        if not snaps:
            print(f"  ! {PALETTE[key]} has no snap points — footprint unverified")
            continue
        span_x = max(s[0] for s in snaps) - min(s[0] for s in snaps)
        span_z = max(s[2] for s in snaps) - min(s[2] for s in snaps)
        if abs(span_x - TILE) > 0.05 or abs(span_z - TILE) > 0.05:
            raise SystemExit(
                f"{PALETTE[key]} spans {span_x:.2f} x {span_z:.2f} m, but the plan lays "
                f"it on a {TILE} m grid — pieces would overlap or leave gaps.")
    return len(entries)


def read_rune_segments(path):
    """Read the same table the UI draws from. Parsing the source rather than
    duplicating the numbers is the whole point: one shape, two scales."""
    src = open(path, encoding="utf-8").read()
    blocks = re.findall(r"\{ LabCategory\.(\w+), new\[\] \{(.*?)\} \},", src, re.S)
    out = {}
    for name, body in blocks:
        out[name] = [tuple(float(v) for v in m) for m in re.findall(
            r"new Seg\(([\d.]+)f?,\s*([\d.]+)f?,\s*([\d.]+)f?,\s*([\d.]+)f?\)",
            body.replace("f", ""))]
    return out


def monument_beams(segments, angle_deg):
    """Cut each rune stroke into beam-length pieces standing in a vertical plane.

    The plane faces the centre of the ring, so a student standing at the armoury
    sees all eight runes face-on rather than edge-on."""
    a = math.radians(angle_deg)
    cx, cz = RUNE_RADIUS * math.sin(a), RUNE_RADIUS * math.cos(a)
    # Tangent to the ring: the rune's "across" direction.
    rx, rz = math.sin(a + math.pi / 2), math.cos(a + math.pi / 2)

    beams = []
    for x1, y1, x2, y2 in segments:
        # Rune space is 0..1 with y downward; world y goes up.
        ax = (x1 - 0.5) * RUNE_WIDTH
        ay = (1.0 - y1) * RUNE_HEIGHT
        bx = (x2 - 0.5) * RUNE_WIDTH
        by = (1.0 - y2) * RUNE_HEIGHT
        length = math.hypot(bx - ax, by - ay)
        count = max(1, int(round(length / BEAM_LENGTH)))
        for i in range(count):
            t0, t1 = i / count, (i + 1) / count
            mx = ax + (bx - ax) * (t0 + t1) / 2
            my = ay + (by - ay) * (t0 + t1) / 2
            beams.append({
                "x": round(cx + rx * mx, 3),
                "y": round(RUNE_BASE_Y + my, 3),
                "z": round(cz + rz * mx, 3),
                # Direction of the stroke in world space, for the builder to aim the
                # beam along. Emitted as a vector rather than an angle so the mod can
                # decide which local axis to align without this script guessing.
                "dx": round(rx * (bx - ax) / (length or 1), 4),
                "dy": round((by - ay) / (length or 1), 4),
                "dz": round(rz * (bx - ax) / (length or 1), 4),
            })
    return beams, (cx, cz), (rx, rz)


segments = read_rune_segments(RUNES)
missing = [c for c in ORDER if c not in segments]
if missing:
    raise SystemExit(f"no rune segments for {missing} — LabRunes.cs and this script disagree")

monuments = []
for i, category in enumerate(ORDER):
    angle = i * (360.0 / len(ORDER))
    beams, (cx, cz), (rx, rz) = monument_beams(segments[category], angle)
    a = math.radians(angle)
    station_prefab, station_kind, station_note = STATIONS[category]
    monuments.append({
        "category": category,
        "angle": angle,
        "cx": round(cx, 3), "cz": round(cz, 3),
        "beams": beams,
        "station": {
            "prefab": station_prefab,
            "kind": station_kind,
            "note": station_note,
            "x": round((RING_RADIUS - STATION_INSET) * math.sin(a), 3),
            "z": round((RING_RADIUS - STATION_INSET) * math.cos(a), 3),
        },
    })

rack = []
for i, (item, note) in enumerate(ARMOURY):
    a = 2 * math.pi * i / len(ARMOURY)
    rack.append({
        "item": item, "note": note,
        "x": round(RACK_RADIUS * math.sin(a), 3),
        "z": round(RACK_RADIUS * math.cos(a), 3),
        "yaw": round((math.degrees(a) + 180.0) % 360.0, 1),   # face the centre
    })


def platform_tiles(monuments):
    """Grid cells that need a floor: plaza, spokes out to each pad, pads under each
    monument. Deduped, because spokes and pads overlap where they meet."""
    tiles = {}
    reach = RING_RADIUS + PAD_DEPTH
    steps = int(reach / TILE) + 2
    rays = [(math.sin(math.radians(m["angle"])), math.cos(math.radians(m["angle"])))
            for m in monuments]

    for i in range(-steps, steps + 1):
        for j in range(-steps, steps + 1):
            x, z = i * TILE, j * TILE
            r = math.hypot(x, z)
            if r <= PLAZA_RADIUS:
                tiles[(x, z)] = "plaza"
                continue
            for sx, sz in rays:
                along = x * sx + z * sz              # distance along the ray
                across = abs(x * sz - z * sx)        # perpendicular offset
                if along <= 0:
                    continue
                # hall
                if across <= SPOKE_HALF_WIDTH and along <= RING_RADIUS - PAD_DEPTH / 2:
                    tiles.setdefault((x, z), "hall"); break
                # pad
                if (across <= PAD_HALF_WIDTH
                        and abs(along - RING_RADIUS + PAD_DEPTH / 2 - 1.0) <= PAD_DEPTH / 2):
                    tiles.setdefault((x, z), "pad"); break
                # the rune's own stage, floating past the pad with a gap of open air
                if (across <= STAGE_HALF_WIDTH
                        and RING_RADIUS + RUNE_GAP <= along
                            <= RING_RADIUS + RUNE_GAP + STAGE_DEPTH):
                    tiles.setdefault((x, z), "stage"); break
    return sorted((x, z, kind) for (x, z), kind in tiles.items())


def backdrop_panels(monuments):
    """A black wall behind each rune, and a return down each side.

    blackmarble_floor_large stood on edge. The slab is 8 x 8 with its 2 m thickness on
    local Y, so the builder turns that thin axis onto the hall's ray and the 8 x 8 face
    ends up spanning across the hall and straight up — which is why these carry an
    orientation the builder has to honour rather than a plain yaw."""
    panels = []
    back_along = RING_RADIUS + RUNE_GAP + STAGE_DEPTH
    for m in monuments:
        th = math.radians(m["angle"])
        sx, sz = math.sin(th), math.cos(th)
        px, pz = math.cos(th), -math.sin(th)

        # the wall behind
        for col in range(BACKDROP_COLUMNS):
            off = (col - (BACKDROP_COLUMNS - 1) / 2.0) * PANEL
            for row in range(BACKDROP_ROWS):
                panels.append({
                    "prefab": PALETTE["panel"],
                    "x": round(sx * back_along + px * off, 3),
                    "y": round(PANEL / 2.0 + row * PANEL, 3),   # centre height
                    "z": round(sz * back_along + pz * off, 3),
                    "yaw": round(m["angle"] % 360.0, 3),
                    "orient": "panel",
                    "text": "",
                })

        # the returns, one either side, turned a quarter so they face inward
        for side in (-1.0, 1.0):
            off = side * (BACKDROP_COLUMNS * PANEL / 2.0)
            along = back_along - PANEL / 2.0
            for row in range(WING_ROWS):
                panels.append({
                    "prefab": PALETTE["panel"],
                    "x": round(sx * along + px * off, 3),
                    "y": round(PANEL / 2.0 + row * PANEL, 3),
                    "z": round(sz * along + pz * off, 3),
                    "yaw": round((m["angle"] + 90.0) % 360.0, 3),
                    "orient": "panel",
                    "text": "",
                })
    return panels


def hall_walls(monuments):
    """Two courses of marble down each side of each hall, and no roof.

    The corridor is the framing device: from the plaza you see a sign, a throat of black
    marble, and a lit glyph at the end of it. Roofing that would trade the rune against
    open sky for a tunnel, which is why WALL_COURSES stops at head height and stays
    there."""
    walls = []
    inner_end = RING_RADIUS - PAD_DEPTH / 2
    for m in monuments:
        th = math.radians(m["angle"])
        sx, sz = math.sin(th), math.cos(th)
        px, pz = math.cos(th), -math.sin(th)          # across the hall
        along = PLAZA_RADIUS
        while along <= inner_end + 1e-6:
            for side in (-1.0, 1.0):
                off = side * (SPOKE_HALF_WIDTH + WALL_INSET)
                x, z = sx * along + px * off, sz * along + pz * off
                for course in range(WALL_COURSES):
                    walls.append({
                        "prefab": PALETTE["wall"],
                        "x": round(x, 3), "y": round(course * 2.0, 3), "z": round(z, 3),
                        "yaw": round((m["angle"] + WALL_YAW_OFFSET) % 360.0, 3),
                        "orient": "",
                        "text": "",
                    })
            along += TILE
    return walls


def hall_signs(monuments):
    """One sign at each hall mouth, facing back into the plaza so it is read on the way
    in rather than discovered on the way out."""
    signs = []
    for m in monuments:
        th = math.radians(m["angle"])
        sx, sz = math.sin(th), math.cos(th)
        px, pz = math.cos(th), -math.sin(th)
        along = PLAZA_RADIUS + 1.0
        off = SPOKE_HALF_WIDTH + WALL_INSET + 0.6      # just outside the wall line
        signs.append({
            "prefab": PALETTE["sign"],
            "x": round(sx * along + px * off, 3),
            "y": 1.2,
            "z": round(sz * along + pz * off, 3),
            "yaw": round((m["angle"] + 180.0) % 360.0, 3),
            "orient": "",
            "text": sign_text(m["category"], m["station"]["note"]),
        })
    return signs


def cs(text):
    """A C# string literal. Newlines are escaped, not embedded: sign copy is multi-line
    and a raw newline inside a quoted literal is a compile error, not a formatting
    quirk."""
    return ('"' + text.replace("\\", "\\\\").replace('"', '\\"')
                      .replace("\r", "\\r").replace("\n", "\\n") + '"')


lines = [
    "// <auto-generated>",
    "//   Generated by tools/component-packets/generate_gallery.py.",
    "//   The rune strokes are read from Ui/LabRunes.cs, so a monument is the same shape",
    "//   as the glyph on its page — one table, two scales. Do not edit by hand.",
    "// </auto-generated>",
    "",
    "namespace ComfyQuestLab;",
    "",
    "/// <summary>Where everything in the gallery goes, relative to its origin.",
    "///",
    "/// Coordinates are metres from the gallery centre, not world coordinates: the builder",
    "/// adds the player's chosen origin, so the gallery can be raised anywhere rather than",
    "/// tying the Tome to one world.</summary>",
    "public static class LabGalleryPlan {",
    "  public struct Beam { public float X, Y, Z, Dx, Dy, Dz; }",
    "  public struct Station { public string Prefab, Kind, Note; public float X, Z; }",
    "  public struct Monument { public string Category; public float Angle, Cx, Cz;",
    "                           public float R, G, B;",
    "                           public Beam[] Beams; public Station Station; }",
    "  public struct RackItem { public string Item, Note; public float X, Z, Yaw; }",
    "",
    "  /// <summary>One floor cell and what to pave it with. Stone in the plaza, black",
    "  /// marble down the halls — the plaza reads as ground, the halls as built.</summary>",
    "  public struct Tile { public float X, Z; public string Prefab; }",
    "",
    "  /// <summary>Anything standing on the floor that is not a monument: hall walls, and",
    "  /// the sign at each hall mouth. Text is empty except on signs, where it carries",
    "  /// Unity rich-text markup into the piece's own ZDO \"text\" field.</summary>",
    "  /// Orient is \"\" for anything that only needs a yaw, and \"panel\" for a slab",
    "  /// stood on edge — for those the builder turns the piece's thin axis onto the",
    "  /// hall's ray, and reads Y as a centre height rather than a base.</summary>",
    "  public struct Fixture { public string Prefab; public float X, Y, Z, Yaw;",
    "                          public string Orient, Text; }",
    "",
    f"  public const float RingRadius = {RING_RADIUS}f;",
    f"  public const float RuneHeight = {RUNE_HEIGHT}f;",
    f"  public const float BeamLength = {BEAM_LENGTH}f;",
    "",
    "  public static readonly Monument[] Monuments = {",
]

total_beams = 0
for m in monuments:
    total_beams += len(m["beams"])
    lines.append("    new Monument {")
    lines.append(f"      Category = LabCategory.{m['category']},")
    lines.append(f"      Angle = {m['angle']}f, Cx = {m['cx']}f, Cz = {m['cz']}f,")
    cr, cg, cb = SCHOOL_COLOURS[m["category"]]
    lines.append(f"      R = {cr}f, G = {cg}f, B = {cb}f,")
    st = m["station"]
    lines.append(f"      Station = new Station {{ Prefab = {cs(st['prefab'])}, "
                 f"Kind = {cs(st['kind'])}, Note = {cs(st['note'])}, "
                 f"X = {st['x']}f, Z = {st['z']}f }},")
    lines.append("      Beams = new[] {")
    for b in m["beams"]:
        lines.append(f"        new Beam {{ X = {b['x']}f, Y = {b['y']}f, Z = {b['z']}f, "
                     f"Dx = {b['dx']}f, Dy = {b['dy']}f, Dz = {b['dz']}f }},")
    lines.append("      },")
    lines.append("    },")

tiles = platform_tiles(monuments)
fixtures = hall_walls(monuments) + backdrop_panels(monuments) + hall_signs(monuments)
lines += ["  };", "",
          "  /// <summary>Floor tiles, 2 m apart, relative to the gallery origin. The",
          "  /// builder picks ONE world height for the whole platform — the highest ground",
          "  /// under the footprint plus a clearance — so the floor is level even where the",
          "  /// terrain is not, and drops supports wherever the gap is worth hiding.</summary>",
          "  public static readonly Tile[] PlatformTiles = {"]
for x, z, kind in tiles:
    lines.append(f"    new Tile {{ X = {x}f, Z = {z}f, Prefab = {cs(PALETTE[kind])} }},")
lines += ["  };", "",
          "  /// <summary>Hall walls and the sign at each hall mouth.</summary>",
          "  public static readonly Fixture[] Fixtures = {"]
for f in fixtures:
    lines.append(f"    new Fixture {{ Prefab = {cs(f['prefab'])}, X = {f['x']}f, "
                 f"Y = {f['y']}f, Z = {f['z']}f, Yaw = {f['yaw']}f, "
                 f"Orient = {cs(f['orient'])}, Text = {cs(f['text'])} }},")
lines += ["  };", "", "  public static readonly RackItem[] Armoury = {"]
for r in rack:
    lines.append(f"    new RackItem {{ Item = {cs(r['item'])}, Note = {cs(r['note'])}, "
                 f"X = {r['x']}f, Z = {r['z']}f, Yaw = {r['yaw']}f }},")
lines += ["  };", "}", ""]

dump_path = None
if "--prefab-dump" in sys.argv:
    dump_path = sys.argv[sys.argv.index("--prefab-dump") + 1]
elif os.path.exists(DUMP):
    dump_path = DUMP
if dump_path:
    known = validate_against_dump(dump_path)
    print(f"  palette checked against {known} prefabs in {os.path.basename(dump_path)}")
else:
    print("  ! no prefab dump — palette names are UNVERIFIED; run questlab_prefabs dump")

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    fh.write("\n".join(lines))
print(f"  {OUT}")
walls = sum(1 for f in fixtures if not f["text"])
print(f"  8 monuments, {total_beams} beams, {len(rack)} armoury stands, "
      f"{len(tiles)} floor tiles, {walls} wall pieces, {len(fixtures) - walls} signs")

# --- preview ---------------------------------------------------------------------
try:
    from PIL import Image, ImageDraw
except ImportError:
    print("  (Pillow absent — no preview rendered)")
    raise SystemExit(0)

SPAN, PX = 130.0, 900
img = Image.new("RGB", (PX, PX), (11, 16, 19))
d = ImageDraw.Draw(img)


def to_px(x, z):
    return (PX / 2 + x / SPAN * PX, PX / 2 - z / SPAN * PX)


COLOURS = {
    "Combat": (237, 115, 102), "Harvest": (122, 204, 133), "Inventory": (217, 184, 107),
    "Building": (199, 153, 97), "Crafting": (235, 158, 77), "Progression": (245, 214, 107),
    "World": (117, 194, 235), "Social": (194, 168, 240),
}

# Plaza stone reads warmer than the marble halls, so the preview can be checked at a
# glance for the material split as well as the footprint.
TILE_FILL = {"plaza": (46, 42, 38), "hall": (22, 20, 26), "pad": (22, 20, 26)}
for x, z, kind in tiles:
    px, pz = to_px(x, z)
    half = TILE / SPAN * PX / 2
    d.rectangle([px - half, pz - half, px + half, pz + half],
                fill=TILE_FILL.get(kind, (31, 26, 20)), outline=(46, 38, 29))

for f in fixtures:
    px, pz = to_px(f["x"], f["z"])
    r = 2 if f["text"] else 1
    d.ellipse([px - r, pz - r, px + r, pz + r],
              fill=(220, 190, 120) if f["text"] else (96, 92, 110))

d.ellipse([*to_px(-RING_RADIUS, RING_RADIUS), *to_px(RING_RADIUS, -RING_RADIUS)],
          outline=(38, 50, 55))
d.ellipse([*to_px(-RACK_RADIUS, RACK_RADIUS), *to_px(RACK_RADIUS, -RACK_RADIUS)],
          outline=(60, 78, 84))

for m in monuments:
    colour = COLOURS[m["category"]]
    for b in m["beams"]:
        px, pz = to_px(b["x"], b["z"])
        d.ellipse([px - 2.2, pz - 2.2, px + 2.2, pz + 2.2], fill=colour)
    sx, sz = to_px(m["station"]["x"], m["station"]["z"])
    d.rectangle([sx - 4, sz - 4, sx + 4, sz + 4], outline=colour)
    lx, lz = to_px(m["cx"] * 1.19, m["cz"] * 1.19)
    d.text((lx - 24, lz - 5), m["category"], fill=colour)

for r in rack:
    px, pz = to_px(r["x"], r["z"])
    d.rectangle([px - 2, pz - 2, px + 2, pz + 2], fill=(200, 200, 190))
d.text((PX / 2 - 26, PX / 2 - 4), "armoury", fill=(150, 168, 165))
d.text((12, PX - 22),
       f"gallery plan · {SPAN:.0f} m across · {total_beams} beams · {len(tiles)} floor tiles",
       fill=(120, 140, 138))

os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
img.save(PREVIEW)
print(f"  {PREVIEW}")

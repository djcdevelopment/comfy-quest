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
import math
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
RUNES = os.path.join(REPO, "network", "mod", "ComfyQuestLab", "Ui", "LabRunes.cs")
OUT = os.path.join(REPO, "network", "mod", "ComfyQuestLab", "Core", "LabGalleryPlan.g.cs")
PREVIEW = os.path.join(HERE, "samples", "gallery-plan.png")

# --- dimensions, in metres -------------------------------------------------------
RING_RADIUS = 46.0     # far enough that eight monuments do not crowd each other
RUNE_WIDTH = 9.0       # a rune reads from the centre of the ring at this size
RUNE_HEIGHT = 11.0
RUNE_BASE_Y = 0.5      # lifted clear of ground clutter
BEAM_LENGTH = 2.0      # a wood beam is 2 m; segments are cut into this many
STATION_INSET = 11.0   # station sits between the monument and the centre
RACK_RADIUS = 6.0      # armoury ring, close enough to spawn to be unmissable

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
    cx, cz = RING_RADIUS * math.sin(a), RING_RADIUS * math.cos(a)
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


def cs(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


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
    "                           public Beam[] Beams; public Station Station; }",
    "  public struct RackItem { public string Item, Note; public float X, Z, Yaw; }",
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

lines += ["  };", "", "  public static readonly RackItem[] Armoury = {"]
for r in rack:
    lines.append(f"    new RackItem {{ Item = {cs(r['item'])}, Note = {cs(r['note'])}, "
                 f"X = {r['x']}f, Z = {r['z']}f, Yaw = {r['yaw']}f }},")
lines += ["  };", "}", ""]

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    fh.write("\n".join(lines))
print(f"  {OUT}")
print(f"  8 monuments, {total_beams} beams, {len(rack)} armoury stands")

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
d.text((12, PX - 22), f"gallery plan · {SPAN:.0f} m across · {total_beams} beams",
       fill=(120, 140, 138))

os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
img.save(PREVIEW)
print(f"  {PREVIEW}")

#!/usr/bin/env python3
"""Verify every seam ComfyQuestLab claims to patch actually exists in the game.

Harmony resolves AccessTools.Method at RUNTIME. A wrong argument list does not fail the
build — it returns null, the patch quietly does not apply, and the builder is left
wondering why bushes are silent. That is the exact failure this file exists to catch,
and it catches it headless: no Valheim, no BepInEx, no game launch.

  python tools/component-packets/check_lab_patches.py

Exit 0 when every TryPatch target is in the atlas with a matching signature.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ATLAS = os.path.join(HERE, "samples", "valheim-event-atlas.json")
PATCHES = os.path.join(REPO, "network", "mod", "ComfyQuestLab", "Patches")

# LabPatching.TryPatch(harmony, typeof(X), "Method", new[] { typeof(A), typeof(B) }, ...)
# and the System.Type.EmptyTypes form.
CALL = re.compile(
    r"LabPatching\.TryPatch\(\s*harmony,\s*typeof\(([A-Za-z0-9_.]+)\)\s*,\s*"
    r'"([A-Za-z0-9_]+)"\s*,\s*(new\[\]\s*\{(?P<args>[^}]*)\}|[A-Za-z.]*Type\.EmptyTypes)',
    re.S)
TYPEOF = re.compile(r"typeof\(([A-Za-z0-9_.]+)\)")

# The atlas records short type names; the mod sometimes needs qualified ones.
SHORTEN = {
    "UnityEngine.Vector3": "Vector3",
    "UnityEngine.GameObject": "GameObject",
    "ItemDrop.ItemData": "ItemData",
    "Skills.SkillType": "SkillType",
    "System.Type.EmptyTypes": "",
}

atlas = json.load(open(ATLAS, encoding="utf-8"))
# id -> list of parameter-type lists, one per overload
by_id = {}
for s in atlas["Seams"]:
    params = s["Signature"].split("(", 1)[1].rstrip(")")
    params = [p.strip() for p in params.split(",") if p.strip()]
    by_id.setdefault(s["Id"], []).append(params)

problems, checked = [], 0
for name in sorted(os.listdir(PATCHES)):
    if not name.endswith(".cs"):
        continue
    text = open(os.path.join(PATCHES, name), encoding="utf-8").read()
    for m in CALL.finditer(text):
        declaring, method = m.group(1), m.group(2)
        raw_args = m.group("args")
        args = [SHORTEN.get(a, a.split(".")[-1]) for a in TYPEOF.findall(raw_args or "")]
        seam_id = f"{declaring}.{method}"
        checked += 1

        # UI plumbing seams are intentionally outside the quest-trigger atlas.
        if seam_id in ("GameCamera.UpdateMouseCapture", "Character.TakeInput"):
            continue

        if seam_id not in by_id:
            problems.append(f"{name}: {seam_id} is not in the atlas")
            continue
        if args not in by_id[seam_id]:
            problems.append(
                f"{name}: {seam_id}({', '.join(args)}) does not match any overload; "
                f"atlas has {by_id[seam_id]}")

print(f"checked {checked} TryPatch call(s) against {len(by_id)} atlas seams")
for p in problems:
    print(f"  ! {p}")
if problems:
    print(f"\n{len(problems)} patch target(s) would resolve to null at runtime.")
    sys.exit(1)
print("all patch targets resolve against the atlas")

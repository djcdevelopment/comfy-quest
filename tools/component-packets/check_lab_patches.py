#!/usr/bin/env python3
"""Verify every seam ComfyQuestLab claims to patch actually exists in the game.

Harmony resolves AccessTools.Method at RUNTIME. A wrong argument list does not fail the
build — it returns null, the patch quietly does not apply, and the builder is left
wondering why bushes are silent. That is the exact failure this file exists to catch,
and it catches it headless: no Valheim, no BepInEx, no game launch.

  python tools/component-packets/check_lab_patches.py

Exit 0 when every TryPatch target is in the exact generated capability manifest and
every creator-safe signature has an explicit runtime patch.
"""
import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
MANIFEST = os.path.join(HERE, "samples", "quest-capability-manifest.json")
DEFAULT_PATCHES = os.path.join(REPO, "network", "mod", "ComfyQuestLab", "Patches")

# LabPatching.TryPatch (or DiagnosticPatches' thin TryAtlasPatch wrapper) with explicit
# declaring and argument types, including the System.Type.EmptyTypes form.
CALL = re.compile(
    r"(?:LabPatching\.TryPatch|TryAtlasPatch)\(\s*harmony,\s*typeof\(([A-Za-z0-9_.]+)\)\s*,\s*"
    r'"([A-Za-z0-9_]+)"\s*,\s*(new\[\]\s*\{(?P<args>[^}]*)\}|[A-Za-z.]*Type\.EmptyTypes)',
    re.S)
TYPEOF = re.compile(r"typeof\(([^)]+)\)")

# The atlas records short type names; the mod sometimes needs qualified ones.
SHORTEN = {
    "UnityEngine.Vector3": "Vector3",
    "UnityEngine.GameObject": "GameObject",
    "ItemDrop.ItemData": "ItemData",
    "Skills.SkillType": "SkillType",
    "Talker.Type": "Type",
    "System.Type.EmptyTypes": "",
}


def short_type(type_name):
    compact = type_name.replace(" ", "")
    if compact.startswith("Dictionary<"):
        return "Dictionary`2"
    return SHORTEN.get(compact, compact.split(".")[-1])

parser = argparse.ArgumentParser()
parser.add_argument(
    "--patches",
    default=DEFAULT_PATCHES,
    help="patch source directory (used by the mutation test)",
)
options = parser.parse_args()
patches = os.path.abspath(options.patches)

with open(MANIFEST, encoding="utf-8") as handle:
    manifest = json.load(handle)
signatures = {row["SignatureId"]: row for row in manifest["Signatures"]}
creator_safe = {
    row["SignatureId"] for row in manifest["Signatures"] if row["CreatorSafe"]
}
practical = {
    row["SignatureId"]
    for row in manifest["Signatures"]
    if row["Profile"] != "disabled"
}
by_id = {}
for row in manifest["Signatures"]:
    by_id.setdefault(row["MethodId"], []).append(row["Parameters"])

problems, checked, atlas_checked, support_checked = [], 0, 0, 0
patched_signatures = set()
support_seams = {"GameCamera.UpdateMouseCapture", "Character.TakeInput"}
for name in sorted(os.listdir(patches)):
    if not name.endswith(".cs"):
        continue
    text = open(os.path.join(patches, name), encoding="utf-8").read()
    for m in CALL.finditer(text):
        declaring, method = m.group(1), m.group(2)
        raw_args = m.group("args")
        args = [short_type(a) for a in TYPEOF.findall(raw_args or "")]
        seam_id = f"{declaring}.{method}"
        checked += 1

        # UI plumbing seams are intentionally outside the quest-trigger atlas.
        if seam_id in support_seams:
            support_checked += 1
            continue

        atlas_checked += 1
        if seam_id not in by_id:
            problems.append(f"{name}: {seam_id} is not in the atlas")
            continue
        signature_id = f"{seam_id}({', '.join(args)})"
        if signature_id not in signatures:
            problems.append(
                f"{name}: {signature_id} does not match any overload; "
                f"atlas has {by_id[seam_id]}")
            continue
        patched_signatures.add(signature_id)

missing_creator_safe = sorted(creator_safe - patched_signatures)
missing_practical = sorted(practical - patched_signatures)
for signature_id in missing_creator_safe:
    problems.append(f"creator-safe signature has no runtime patch: {signature_id}")
for signature_id in missing_practical:
    if signature_id not in creator_safe:
        problems.append(f"practical diagnostic signature has no runtime patch: {signature_id}")

print(
    f"checked {checked} TryPatch call(s): {atlas_checked} atlas integration(s), "
    f"{support_checked} lab support hook(s), against {len(signatures)} exact signatures"
)
print(
    f"creator-safe runtime coverage "
    f"{len(creator_safe) - len(missing_creator_safe)}/{len(creator_safe)} signatures"
)
print(
    f"practical atlas runtime coverage "
    f"{len(practical) - len(missing_practical)}/{len(practical)} signatures; "
    f"{len(signatures) - len(practical)} intentionally disabled"
)
for p in problems:
    print(f"  ! {p}")
if problems:
    print(f"\n{len(problems)} integration coverage problem(s).")
    sys.exit(1)
print("all patch targets resolve against the atlas")

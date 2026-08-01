"""Diff two component-atlas extractions and emit a markdown changelog.

Usage:
    python diff_atlas.py <old-atlas.json> <new-atlas.json> [-o changelog.md]

Run the --all sweep after a game patch, diff against the committed atlas, and
the output is exactly what the guide (dictionaries, lessons, explorer) must
update: components added/removed, fields added/removed/retyped per component,
ZDO keys appearing/vanishing, RPC registrations changed.
"""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("old", type=Path)
parser.add_argument("new", type=Path)
parser.add_argument("-o", "--output", type=Path)
args = parser.parse_args()

old = json.loads(args.old.read_text())
new = json.loads(args.new.read_text())

def by_name(atlas):
    return {c["Component"]: c for c in atlas["Components"]}

def field_map(comp):
    return {(f["DeclaredBy"], f["Name"]): f["Type"] for f in comp["TunableFields"]}

oc, nc = by_name(old), by_name(new)
lines = [
    "# Atlas changelog",
    "",
    f"- old: {old['Source']} — {len(old['Components'])} components",
    f"- new: {new['Source']} — {len(new['Components'])} components",
    "",
]
changes = 0

added = sorted(set(nc) - set(oc))
removed = sorted(set(oc) - set(nc))
if added:
    changes += len(added)
    lines += ["## Components added", ""] + [f"- `{n}` ({len(nc[n]['TunableFields'])} fields)" for n in added] + [""]
if removed:
    changes += len(removed)
    lines += ["## Components removed", ""] + [f"- `{n}`" for n in removed] + [""]

field_lines = []
for name in sorted(set(oc) & set(nc)):
    fo, fn = field_map(oc[name]), field_map(nc[name])
    fadd = sorted(set(fn) - set(fo))
    frem = sorted(set(fo) - set(fn))
    retyped = sorted(k for k in set(fo) & set(fn) if fo[k] != fn[k])
    if not (fadd or frem or retyped):
        continue
    changes += len(fadd) + len(frem) + len(retyped)
    field_lines.append(f"### `{name}`")
    field_lines += [f"- added `{d}.{f}: {fn[(d, f)]}`" for d, f in fadd]
    field_lines += [f"- removed `{d}.{f}`" for d, f in frem]
    field_lines += [f"- retyped `{d}.{f}`: {fo[(d, f)]} -> {fn[(d, f)]}" for d, f in retyped]
    field_lines.append("")
if field_lines:
    lines += ["## Field changes", ""] + field_lines

for label, key in (("ZDO keys", "ZdoKeyIndex"), ("RPC names", "RpcIndex")):
    kadd = sorted(set(new[key]) - set(old[key]))
    krem = sorted(set(old[key]) - set(new[key]))
    if kadd or krem:
        changes += len(kadd) + len(krem)
        lines += [f"## {label}", ""]
        lines += [f"- added `{k}`" for k in kadd]
        lines += [f"- removed `{k}`" for k in krem]
        lines.append("")

if not changes:
    lines += ["**No structural changes.** Same components, fields, ZDO keys, and RPCs.", ""]
else:
    lines += [f"**{changes} structural changes.** Re-assemble affected dictionaries,",
              "re-run build_explorer.py, and check lessons that cite changed members.", ""]

text = "\n".join(lines)
if args.output:
    args.output.write_text(text, encoding="utf-8")
    print(f"{args.output}: {changes} changes")
else:
    print(text)

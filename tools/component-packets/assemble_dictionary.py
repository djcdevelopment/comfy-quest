"""Join an extract packet with drafted annotations into a markdown field dictionary.

Usage:
    python assemble_dictionary.py <packet.json> <annotations.json> [-o out.md] [--tagline "..."]

The packet comes from the extractor (dotnet run -- <dll> <Component>); the
annotations file maps "Class.m_fieldName" -> one-line description, produced by
any LLM from annotation-prompt.md and then human-reviewed. Descriptions ending
in "(?)" are the annotator's own low-confidence flags and survive into the
output so reviewers can find them.
"""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("packet", type=Path)
parser.add_argument("annotations", type=Path)
parser.add_argument("-o", "--output", type=Path)
parser.add_argument("--tagline", default="")
args = parser.parse_args()

packet = json.loads(args.packet.read_text())
ann = json.loads(args.annotations.read_text())
comp = packet["Component"]
out_path = args.output or args.packet.with_name(f"{comp.lower()}-field-dictionary.md")

lines = [f"# `{comp}` field dictionary", ""]
if args.tagline:
    lines += [f"*{args.tagline}*", ""]
lines += [
    f"Inheritance: `{' : '.join(c.split('.')[-1] for c in packet['InheritanceChain'])}`  ",
    f"Source: {packet['Source']}. Field names, types, and declaring classes are",
    "extracted from the assembly; descriptions are AI-drafted for editing, and",
    "entries marked `(?)` are low-confidence guesses — verify before publishing.",
    "",
]

by_class: dict[str, list] = {}
for f in packet["TunableFields"]:
    by_class.setdefault(f["DeclaredBy"], []).append(f)

for cls, fields in by_class.items():
    origin = " *(inherited)*" if cls != comp else ""
    lines += [f"## Declared by `{cls}`{origin} — {len(fields)} fields", "",
              "| Field | Type | What it does (draft) |", "|---|---|---|"]
    for f in fields:
        desc = ann.get(f"{cls}.{f['Name']}", "*(no draft)*")
        lines.append(f"| `{f['Name']}` | {f['Type']} | {desc} |")
    lines.append("")

out_path.write_text("\n".join(lines), encoding="utf-8")
missing = sum(1 for c, fs in by_class.items() for f in fs if f"{c}.{f['Name']}" not in ann)
print(f"{out_path.name}: {len(packet['TunableFields'])} fields, {missing} missing drafts")

"""Generate the Norse Quest Lab Grimoire from the shipping event catalog.

The generated catalog is deliberately read from QuestEventCatalog.g.cs rather than
maintaining a second hand-written event list.  The evaluator remains the source of
truth for names, while this file supplies stable creator-facing vocabulary.
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / "network" / "mod" / "ComfyQuestContracts" / "ModGlue" / "QuestEventCatalog.g.cs"
DEFAULT_JSON = ROOT / "artifacts" / "questlab-grimoire.json"
DEFAULT_MD = ROOT / "docs" / "questlab-grimoire.md"

# Each school's invocation must fit EVERY event in that school: the old combat line
# ("...witnesses a foe fall") read as nonsense beside character_healed and attack_blocked,
# because the prose was written for one event and keyed on the whole category.
RUNE = {
    "combat": ("Tiwaz", "crimson", "When the spear-rune stirs in the clash of arms..."),
    "harvest": ("Jera", "emerald", "When you take the land's bounty by hand or by steel..."),
    "inventory": ("Fehu", "amber", "When goods pass through your hands..."),
    "building": ("Othala", "amber", "When the works of your hearth rise or fall..."),
    "crafting": ("Kenaz", "amber", "When the forge answers your hands..."),
    "progression": ("Ansuz", "sapphire", "When your own strength waxes or wanes..."),
    "world": ("Raidho", "sapphire", "When the world itself marks your passage..."),
    "social": ("Mannaz", "sapphire", "When you speak or set words before the hall..."),
}


def catalog(path: Path = CATALOG) -> list[dict[str, str]]:
    text = path.read_text(encoding="utf-8")
    rows = re.findall(
        r'\{ "(?P<event>[a-z0-9_]+)",\s*new Definition\("[a-z0-9_]+",\s*"(?P<category>[a-z]+)",\s*"(?P<profile>[a-z]+)"',
        text,
    )
    if not rows:
        raise ValueError("QuestEventCatalog.g.cs contained no event definitions")
    result = []
    for event, category, profile in rows:
        rune, color, invocation = RUNE.get(category, ("Rune", "white", "When the world answers..."))
        result.append({
            "event": event,
            "category": category,
            "profile": profile,
            "rune": rune,
            "color": color,
            "invocation": invocation,
            "bindable": True,
        })
    return result


def render_markdown(rows: list[dict[str, str]]) -> str:
    lines = ["# The Quest Lab Grimoire", "", "Generated from `QuestEventCatalog.g.cs`; canonical event names remain evaluator-owned.", ""]
    for row in rows:
        title = row["event"].replace("_", " ").title()
        lines.extend([
            f"## {title} — {row['rune']} ({row['category']})",
            "",
            f"- **Invocation:** *{row['invocation']}*",
            f"- **Canonical event:** `{row['event']}`",
            f"- **Runtime profile:** `{row['profile']}`",
            f"- **School color:** `{row['color']}`",
            "- **Quest use:** `BINDABLE`",
            "",
        ])
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--catalog", type=Path, default=CATALOG)
    parser.add_argument("--json", type=Path, default=DEFAULT_JSON)
    parser.add_argument("--markdown", type=Path, default=DEFAULT_MD)
    args = parser.parse_args()
    rows = catalog(args.catalog)
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps({"schema": "comfy-questlab-grimoire/v1", "events": rows}, indent=2) + "\n", encoding="utf-8")
    args.markdown.write_text(render_markdown(rows), encoding="utf-8")
    print(f"generated {len(rows)} Grimoire events")


if __name__ == "__main__":
    main()

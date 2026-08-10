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
CATALOG = ROOT / "network" / "mod" / "ComfyNetworkSense" / "Core" / "Services" / "QuestEventCatalog.g.cs"
DEFAULT_JSON = ROOT / "artifacts" / "questlab-grimoire.json"
DEFAULT_MD = ROOT / "docs" / "questlab-grimoire.md"

RUNE = {
    "combat": ("Tiwaz", "crimson", "When the spear-rune witnesses a foe fall..."),
    "harvest": ("Jera", "emerald", "When your steel bites the living wood..."),
    "inventory": ("Fehu", "amber", "When you claim a thing from the hall..."),
    "building": ("Othala", "amber", "When you raise a structure upon the hearth..."),
    "crafting": ("Kenaz", "amber", "When the forge answers your hands..."),
    "progression": ("Ansuz", "sapphire", "When your strength crosses a new threshold..."),
    "world": ("Raidho", "sapphire", "When the road carries you between worlds..."),
    "social": ("Mannaz", "sapphire", "When your voice speaks to the hall..."),
}


def catalog(path: Path = CATALOG) -> list[dict[str, str]]:
    text = path.read_text(encoding="utf-8")
    rows = re.findall(
        r'\{ "(?P<event>[a-z0-9_]+)", new Definition\("[a-z0-9_]+", "(?P<category>[a-z]+)", "(?P<profile>[a-z]+)"\) \}',
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

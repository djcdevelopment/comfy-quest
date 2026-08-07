#!/usr/bin/env python3
"""Generate the Quest Lab Web Tome HTML page.

This is the web-facing version of the in-game spellbook, generated from the same
source files: journal-pages.json and valheim-event-atlas.json. It also includes
SVG representations of the runes drawn from LabRunes.cs.

Writes Lumberjacks/src/Game.Gateway/Community/questlab.html
"""
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ATLAS = os.path.join(HERE, "samples", "valheim-event-atlas.json")
PAGES = os.path.join(HERE, "journal-pages.json")
PATCHES = os.path.join(REPO, "network", "mod", "ComfyQuestLab", "Patches")
OUT = os.path.join(REPO, "Lumberjacks", "src", "Game.Gateway", "Community", "questlab.html")

ORDER = ["combat", "harvest", "inventory", "building", "crafting", "progression",
         "world", "social"]

# Extracted from LabRunes.cs
RUNES = {
    "combat": [
        (0.5, 0.10, 0.5, 0.95), (0.5, 0.10, 0.15, 0.42), (0.5, 0.10, 0.85, 0.42)
    ],
    "harvest": [
        (0.12, 0.20, 0.50, 0.42), (0.50, 0.42, 0.12, 0.62), 
        (0.88, 0.80, 0.50, 0.58), (0.50, 0.58, 0.88, 0.38)
    ],
    "inventory": [
        (0.28, 0.05, 0.28, 0.95), (0.28, 0.28, 0.80, 0.08), (0.28, 0.55, 0.80, 0.35)
    ],
    "building": [
        (0.50, 0.08, 0.18, 0.38), (0.50, 0.08, 0.82, 0.38), (0.18, 0.38, 0.50, 0.58), 
        (0.82, 0.38, 0.50, 0.58), (0.32, 0.50, 0.12, 0.95), (0.68, 0.50, 0.88, 0.95)
    ],
    "crafting": [
        (0.78, 0.08, 0.25, 0.50), (0.25, 0.50, 0.78, 0.92)
    ],
    "progression": [
        (0.80, 0.10, 0.32, 0.10), (0.32, 0.10, 0.68, 0.50), 
        (0.68, 0.50, 0.20, 0.50), (0.20, 0.50, 0.56, 0.90)
    ],
    "world": [
        (0.25, 0.05, 0.25, 0.95), (0.25, 0.05, 0.75, 0.22), 
        (0.75, 0.22, 0.25, 0.48), (0.35, 0.48, 0.80, 0.95)
    ],
    "social": [
        (0.15, 0.08, 0.15, 0.92), (0.85, 0.08, 0.85, 0.92), 
        (0.15, 0.12, 0.85, 0.58), (0.85, 0.12, 0.15, 0.58)
    ]
}

COLORS = {
    "combat": "#ed7366", "harvest": "#7ace85", "inventory": "#d9b76b",
    "building": "#c79961", "crafting": "#eb9e4c", "progression": "#f5d66b",
    "world": "#75c2eb", "social": "#c2a8f0"
}

atlas = json.load(open(ATLAS, encoding="utf-8"))
_doc = json.load(open(PAGES, encoding="utf-8"))
pages = _doc["pages"]
incantations = _doc["incantations"]

hooked = set()
call = re.compile(r'LabPatching\.TryPatch\(\s*harmony,\s*typeof\(([A-Za-z0-9_.]+)\)\s*,\s*"([A-Za-z0-9_]+)"')
for name in os.listdir(PATCHES):
    if name.endswith(".cs"):
        for m in call.finditer(open(os.path.join(PATCHES, name), encoding="utf-8").read()):
            hooked.add(f"{m.group(1)}.{m.group(2)}")

seams_by_cat = {}
for s in atlas["Seams"]:
    seams_by_cat.setdefault(s["Category"], {})[s["Id"]] = s["QuestUsable"]

VERDICT_SHORT = {
    "today": "a quest can be bound to this",
    "produces-event-no-trigger": "the world speaks, but no quest is listening",
    "lab-candidate": "no quest can be bound to this yet",
    "not-patchable": "beyond reach",
}

def render_svg(category):
    lines = []
    color = COLORS.get(category, "#fff")
    for (x1, y1, x2, y2) in RUNES.get(category, []):
        lines.append(f'<line x1="{x1*100}" y1="{y1*100}" x2="{x2*100}" y2="{y2*100}" stroke="{color}" stroke-width="12" stroke-linecap="round"/>')
    return f'<svg viewBox="0 0 100 100" class="rune" aria-hidden="true">{"".join(lines)}</svg>'

html = [
    '<!DOCTYPE html><html lang="en"><head>',
    '<meta charset="utf-8">',
    '<meta name="viewport" content="width=device-width, initial-scale=1">',
    '<title>ComfyQuestLab — The Tome</title>',
    '<style>',
    ':root { --bg: #0b1013; --paper: #11191d; --panel: #162126; --panel-2: #1b292e; --line: #2c3a3f; --ink: #edf5f2; --muted: #9eb0ad; --wood: #d7a86e; --green: #68d391; --sans: Inter, ui-sans-serif, system-ui, sans-serif; --mono: "SFMono-Regular", Consolas, monospace; }',
    '* { box-sizing: border-box; }',
    'body { margin: 0; background: radial-gradient(circle at 15% 0%, #18302e 0, transparent 28rem), var(--bg); color: var(--ink); font-family: var(--sans); line-height: 1.55; }',
    '.topbar { position: sticky; top: 0; z-index: 10; border-bottom: 1px solid rgba(255,255,255,.08); background: rgba(11,16,19,.92); backdrop-filter: blur(14px); }',
    '.topbar-inner { max-width: 900px; margin: 0 auto; min-height: 58px; display: flex; align-items: center; justify-content: space-between; padding: 0 16px; }',
    '.brand { display: flex; align-items: center; gap: 10px; font-weight: 800; letter-spacing: .02em; }',
    '.mark { width: 26px; height: 26px; display: grid; place-items: center; border: 1px solid var(--wood); color: var(--wood); border-radius: 7px; font-family: var(--mono); }',
    'nav { display: flex; gap: 14px; font-size: .85rem; }',
    'nav a { color: var(--muted); text-decoration: none; }',
    'nav a:hover, nav a[aria-current="page"] { color: var(--ink); }',
    '.wrap { max-width: 900px; margin: 0 auto; padding: 2rem 16px; }',
    'h1 { font-size: 2.5rem; letter-spacing: -.02em; margin-bottom: 1rem; }',
    '.intro { color: var(--muted); font-size: 1.1rem; max-width: 65ch; margin-bottom: 3rem; }',
    '.school { background: var(--paper); border: 1px solid var(--line); border-radius: 12px; margin-bottom: 2rem; padding: 2rem; }',
    '.school-header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1.5rem; }',
    '.school-header h2 { margin: 0; font-size: 1.8rem; }',
    '.rune { width: 48px; height: 48px; }',
    '.spell-list { list-style: none; padding: 0; margin: 0; }',
    '.spell { display: flex; justify-content: space-between; padding: 0.75rem 0; border-bottom: 1px solid rgba(255,255,255,0.05); }',
    '.spell:last-child { border-bottom: none; }',
    '.spell-name { font-weight: 600; }',
    '.spell-true-name { font-family: var(--mono); font-size: 0.85rem; color: var(--wood); margin-left: 0.5rem; }',
    '.spell-verdict { font-size: 0.85rem; color: var(--muted); text-align: right; }',
    '.verdict-today { color: var(--green); }',
    '.bound-badge { display: inline-block; background: rgba(255,255,255,0.1); padding: 0.1rem 0.4rem; border-radius: 4px; font-size: 0.75rem; text-transform: uppercase; margin-left: 0.5rem; }',
    '.start { background: var(--panel); border: 1px solid var(--line); border-radius: 12px; padding: 2rem; margin-bottom: 2rem; }',
    '.start h2 { margin-top: 0; font-size: 1.5rem; }',
    'ol.steps { margin: 0; padding-left: 1.3rem; }',
    'ol.steps > li { margin-bottom: 1rem; }',
    'ol.steps > li > strong { display: block; }',
    'ol.steps p { margin: .25rem 0 0; color: var(--muted); font-size: .95rem; }',
    'kbd { font-family: var(--mono); font-size: .85rem; background: var(--panel-2); border: 1px solid var(--line); border-bottom-width: 2px; border-radius: 5px; padding: .1rem .4rem; color: var(--ink); }',
    'code { font-family: var(--mono); font-size: .9rem; color: var(--wood); }',
    '.cta { display: inline-block; margin-bottom: 1.25rem; background: var(--wood); color: #1a1206; font-weight: 700; text-decoration: none; padding: .6rem 1.1rem; border-radius: 8px; }',
    '.cta:hover { filter: brightness(1.08); }',
    '.note { border-left: 3px solid var(--wood); background: var(--panel-2); padding: .85rem 1rem; border-radius: 0 8px 8px 0; margin: 1.25rem 0 0; color: var(--muted); font-size: .95rem; }',
    '.note strong { color: var(--ink); }',
    '.watch { color: var(--muted); font-size: .95rem; border-left: 3px solid var(--line); padding-left: .9rem; margin: 1rem 0 0; }',
    '.watch strong { color: var(--ink); }',
    '</style>',
    '</head><body>',
    '<div class="topbar"><div class="topbar-inner">',
    '<div class="brand"><div class="mark">B</div>Baseline</div>',
    '<nav aria-label="Gateway surfaces">',
    '<a href="/roadmap">Roadmap</a>',
    '<a href="/community">Community</a>',
    '<a href="/workbench">Workbench</a>',
    '<a href="/questpicker">Quest Picker</a>',
    '<a href="/steward">Steward</a>',
    '<a href="/questlab" aria-current="page">Quest Lab</a>',
    '<a href="/networksense">NetworkSense</a>',
    '<a href="/events">Events</a>',
    '<a href="/testing">Testing</a>',
    '</nav></div></div>',
    '<div class="wrap">',
    '<h1>The Quest Lab Tome</h1>',
    '<p class="intro">This is the web-facing version of the in-game spellbook. It lists every spell the world genuinely answers to, categorized by rune school. Spells marked with a badge are ones this build can witness in-game today.</p>',

    # Onboarding. The tome taught eight schools and 78 spells and never once said how to
    # get the mod -- a reader who wanted to try it had nowhere to go from here.
    '<div class="start">',
    '<h2>Start here</h2>',
    '<a class="cta" href="/workbench/downloads/quest-lab">Download the Quest Lab</a>',
    '<ol class="steps">',
    '<li><strong>Install BepInEx on your Valheim client.</strong>',
    '<p>The lab is a BepInEx plugin. If you already run mods, you have this.</p></li>',
    '<li><strong>Drop <code>ComfyQuestLab.dll</code> into <code>BepInEx/plugins</code>.</strong>',
    '<p>That is the whole install. The zip also carries a README and a default config.</p></li>',
    '<li><strong>Launch a private, single-player world.</strong>',
    '<p>The lab is client-only and does nothing at all on a dedicated server. Nothing '
    'it does touches anyone else, and uninstalling it changes nothing about your game.</p></li>',
    '<li><strong>Press <kbd>F5</kbd> and type <code>lab_setup</code>.</strong>',
    '<p>This raises the practice gallery &mdash; eight rune monuments, a station under each, '
    'and an armoury &mdash; and writes you a starter quest file.</p></li>',
    '<li><strong>Press <kbd>F6</kbd> to open the lab panel.</strong>',
    '<p><kbd>F5</kbd> is Valheim&rsquo;s console, where you type commands. <kbd>F6</kbd> is the '
    'lab&rsquo;s own window. Two different keys, and mixing them up is the usual first stumble.</p></li>',
    '<li><strong>Edit <code>BepInEx/config/comfy-quest-lab/quests/starter.json</code>, then '
    'run <code>lab_reload</code>.</strong>',
    '<p>No restart. The <em>Quests</em> tab names what changed, what will fire, and what '
    'cannot &mdash; and shows the last kill the matcher was actually handed, which is how you '
    'find out why something did not fire.</p></li>',
    '</ol>',

    '<p class="note"><strong>The starter file holds two quests that disagree on purpose.</strong> '
    '<code>neck_romancer</code> is armed: kill a Neck and it fires. <code>punchwood</code> is not, '
    'and nothing errors to tell you &mdash; a <code>hit</code> trigger parses perfectly and can '
    'never fire, because only a creature kill can complete a quest today. All eight schools below '
    'are <em>hooked</em>, meaning the lab can show you every one of them. Exactly one can have a '
    'quest <em>bound</em> to it. Knowing which is which is the entire reason this tool exists, and '
    'the verdict on every row below is the honest answer.</p>',

    '<p class="note"><strong>Each school&rsquo;s rune is also its filter.</strong> The mark beside '
    'each heading is the same mark you click in the lab&rsquo;s console to show or hide that '
    'school &mdash; so learning the book teaches the console for free. One fight emits more rows '
    'than the window holds, which is why the filters matter.</p>',
    '</div>',
]

for cat in ORDER:
    page = pages[cat]
    seams = seams_by_cat.get(cat, {})
    rows = []
    for seam_id in sorted(seams, key=lambda s: (s not in hooked, incantations.get(s, s))):
        rows.append((incantations.get(seam_id, seam_id), seam_id,
                     VERDICT_SHORT[seams[seam_id]], seam_id in hooked, seams[seam_id] == "today"))

    html.append(f'<div class="school">')
    html.append(f'<div class="school-header">{render_svg(cat)}<h2>{page["title"]}</h2></div>')
    
    # These fields are line-WRAPPED prose, not lists of items. Joining them with ", "
    # inserted a comma at every wrap point ("The first thing, most people try, and the
    # reason this lab exists"), which read as a garbled list. A space rejoins the
    # sentence the way the author wrote it.
    html.append('<div style="margin-bottom: 1.5rem">')
    if "what" in page:
        html.append(f'<p><strong>What it covers:</strong> {" ".join(page["what"])}</p>')
    if "try" in page:
        html.append(f'<p><strong>Go and try:</strong> {" ".join(page["try"])}</p>')
    # "watch" is the trap for this school -- the sentence that saves somebody an hour,
    # e.g. that picking a berry is an interact and not damage at all. It was in
    # journal-pages.json and in the in-game spellbook, and the web tome silently dropped
    # it: the single most valuable paragraph per school, missing from the public page.
    if "watch" in page:
        html.append(f'<p class="watch"><strong>Worth knowing:</strong> {" ".join(page["watch"])}</p>')
    html.append('</div>')
    
    html.append('<ul class="spell-list">')
    for name, true_name, verdict, bound, is_today in rows:
        verdict_class = "verdict-today" if is_today else ""
        bound_html = '<span class="bound-badge">Witnessed</span>' if bound else ''
        html.append(f'<li class="spell">')
        html.append(f'<div><span class="spell-name">{name}</span>{bound_html}<br><span class="spell-true-name">{true_name}</span></div>')
        html.append(f'<div class="spell-verdict {verdict_class}">{verdict}</div>')
        html.append(f'</li>')
    html.append('</ul></div>')

html.append('</div></body></html>')

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    fh.write("\n".join(html))

print(f"Generated {OUT}")

#!/usr/bin/env python3
"""Render the provenance view: one page per source showing a guild leader exactly
what happened to their artifact — which columns became which canonical fields, the
fate of every row (with verbatim cell echoes), the anomalies attached to the rows
they concern, and how the result feeds the quest picker.

Standard library only. Pages are self-contained and work from file://. The design
tokens are the baseline set (docs/absorption-loop.html) so every surface reads as
one product: solid = built, dashed = a human step, amber = flagged.

Usage:
  python render_provenance.py                       # all enabled sources + index
  python render_provenance.py out.html prov.json catalog.json   # one explicit page
"""
import html
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

# ---------------------------------------------------------------- shared CSS

TOKENS_CSS = """
:root{
  --bg:#0b1013; --paper:#11191d; --panel:#162126; --line:#2c3a3f; --line2:#3d5058;
  --ink:#edf5f2; --muted:#9eb0ad; --dim:#7b8f8c;
  --wood:#d7a86e; --amber:#f6c453; --blue:#75c9f1; --green:#68d391; --violet:#c7a7ff;
  --sans:ui-sans-serif,system-ui,"Segoe UI",sans-serif;
  --mono:ui-monospace,Consolas,monospace;
}
@media (prefers-color-scheme:light){
  :root{
    --bg:#eceeed; --paper:#f6f7f6; --panel:#e3e8e7; --line:#c6d0ce; --line2:#a8b6b4;
    --ink:#0e1719; --muted:#4d5f5d; --dim:#687977;
    --wood:#8a5a22; --amber:#8a6410; --blue:#1a6285; --green:#1f6b42; --violet:#5b3fa0;
  }
}
:root[data-theme="dark"]{
  --bg:#0b1013; --paper:#11191d; --panel:#162126; --line:#2c3a3f; --line2:#3d5058;
  --ink:#edf5f2; --muted:#9eb0ad; --dim:#7b8f8c;
  --wood:#d7a86e; --amber:#f6c453; --blue:#75c9f1; --green:#68d391; --violet:#c7a7ff;
}
:root[data-theme="light"]{
  --bg:#eceeed; --paper:#f6f7f6; --panel:#e3e8e7; --line:#c6d0ce; --line2:#a8b6b4;
  --ink:#0e1719; --muted:#4d5f5d; --dim:#687977;
  --wood:#8a5a22; --amber:#8a6410; --blue:#1a6285; --green:#1f6b42; --violet:#5b3fa0;
}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font-family:var(--sans);
  font-size:16px;line-height:1.6;-webkit-font-smoothing:antialiased}
p{margin:0}
h1,h2,h3{margin:0;text-wrap:balance;font-weight:600}
a{color:var(--wood);text-decoration:none;border-bottom:1px solid rgba(215,168,110,.4)}
a:hover{border-bottom-color:var(--wood)}
code{font-family:var(--mono);font-size:.86em;color:var(--wood)}
.wrap{max-width:64rem;margin:0 auto;padding:0 20px}
section+section{margin-top:3.4rem}
header+section{margin-top:2rem}
.eyebrow{font-family:var(--mono);font-size:.66rem;font-weight:700;letter-spacing:.16em;
  text-transform:uppercase;color:var(--wood)}
h2{font-family:var(--mono);font-weight:700;font-size:clamp(1.05rem,2.4vw,1.35rem);
  letter-spacing:-.01em;line-height:1.25}
.head{display:flex;flex-direction:column;gap:.45rem;margin-bottom:1rem}
.muted{color:var(--muted)}
header{padding:3.4rem 0 0;display:flex;flex-direction:column;gap:1rem}
.mark{display:flex;align-items:center;gap:.7rem}
.mark .glyph{width:30px;height:30px;display:grid;place-items:center;border-radius:7px;
  border:1px solid var(--wood);color:var(--wood);font-family:var(--mono);font-weight:700;
  font-size:.9rem}
.mark .name{font-family:var(--mono);font-weight:700;font-size:1rem;letter-spacing:.03em}
h1{font-family:var(--mono);font-weight:700;letter-spacing:-.02em;line-height:1.15;
  font-size:clamp(1.35rem,4.6vw,2.3rem);max-width:30ch}
.rule{height:1px;background:var(--line);margin:.4rem 0}
dl.meta{display:grid;grid-template-columns:auto 1fr;gap:.3rem 1.1rem;margin:0;
  font-family:var(--mono);font-size:.74rem;color:var(--dim);max-width:46rem}
dl.meta dt{color:var(--muted);font-weight:700}
dl.meta dd{margin:0;overflow-wrap:anywhere}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(15rem,1fr));gap:.8rem}
.card{background:var(--paper);border:1px solid var(--line);border-radius:5px;
  padding:1rem 1.1rem;display:flex;flex-direction:column;gap:.45rem;min-width:0}
.card h3{font-family:var(--mono);font-size:.9rem;font-weight:700}
.card p{font-size:.9rem;color:var(--muted);line-height:1.55}
.card.ghost{border-style:dashed;border-color:var(--line2)}
.tag{align-self:flex-start;font-family:var(--mono);font-size:.6rem;font-weight:700;
  letter-spacing:.08em;text-transform:uppercase;padding:.16rem .45rem;border-radius:999px;
  border:1px solid currentColor}
.t-run{color:var(--green)}
.t-ghost{color:var(--dim)}
.warn{border-left:2px solid var(--amber);padding:.2rem 0 .2rem 1rem;color:var(--muted);
  font-size:.94rem;max-width:38rem}
.diagram{background:var(--paper);border:1px solid var(--line);border-radius:5px;
  padding:.9rem;overflow-x:auto}
.diagram svg{display:block;width:100%;height:auto;min-width:560px}
.caption{font-family:var(--mono);font-size:.7rem;color:var(--dim);line-height:1.7;
  max-width:44rem;margin-top:.55rem}
svg text{font-family:var(--mono)}
.n-box{fill:var(--panel);stroke:var(--line)}
.n-wood{fill:var(--panel);stroke:var(--wood)}
.n-amber{fill:var(--panel);stroke:var(--amber)}
.t-ink{fill:var(--ink);font-size:12.5px;font-weight:600}
.t-woodtxt{fill:var(--wood);font-size:12.5px;font-weight:700}
.t-ambertxt{fill:var(--amber);font-size:12px;font-weight:600}
.t-dim{fill:var(--dim);font-size:10.5px}
.edge{stroke:var(--dim);stroke-width:1.3;fill:none}
.edge-soft{stroke:var(--dim);stroke-width:1.3;fill:none;stroke-dasharray:5 4}
/* ---- row ledger ---- */
.ledger{display:flex;flex-direction:column;gap:3px}
.ledger details{background:var(--paper);border:1px solid var(--line);border-radius:5px}
.ledger details[data-outcome="skipped"]{border-color:var(--amber)}
.ledger summary{display:flex;align-items:baseline;gap:.7rem;padding:.4rem .8rem;
  cursor:pointer;font-family:var(--mono);font-size:.78rem;list-style:none;flex-wrap:wrap}
.ledger summary::-webkit-details-marker{display:none}
.ledger summary:hover{background:var(--panel)}
.rowno{color:var(--dim);min-width:7.5ch}
.chip{font-size:.6rem;font-weight:700;letter-spacing:.08em;text-transform:uppercase;
  padding:.1rem .4rem;border-radius:999px;border:1px solid currentColor}
.c-quest{color:var(--green)}
.c-ach{color:var(--violet)}
.c-skipped{color:var(--amber)}
.c-dim{color:var(--dim)}
.ledger details[data-outcome="detail"]{margin-left:1.4rem}
.rname{color:var(--ink);font-weight:600}
.rbecame{color:var(--muted)}
.abadge{color:var(--amber);font-weight:700}
.rbody{padding:.5rem .9rem .9rem;border-top:1px solid var(--line);
  display:flex;flex-direction:column;gap:.7rem}
.cells{display:grid;grid-template-columns:repeat(auto-fill,minmax(230px,1fr));gap:.55rem}
.cell{min-width:0}
.cell b{display:block;font-family:var(--mono);font-size:.62rem;font-weight:700;
  letter-spacing:.06em;color:var(--dim);text-transform:uppercase}
.cell div{font-family:var(--mono);font-size:.78rem;white-space:pre-wrap;
  overflow-wrap:anywhere;color:var(--ink)}
.became{font-size:.85rem;color:var(--muted)}
.became b{color:var(--ink)}
.became code{overflow-wrap:anywhere}
.rowanom{border-left:2px solid var(--amber);padding-left:.8rem;
  font-size:.85rem;color:var(--muted)}
.gap{font-family:var(--mono);font-size:.72rem;color:var(--dim);padding:.25rem .8rem}
.anomlist{display:flex;flex-direction:column;gap:.6rem;max-width:46rem}
.anomlist .item{border-left:2px solid var(--amber);padding:.15rem 0 .15rem 1rem;
  font-size:.92rem;color:var(--muted)}
.anomlist .item b{color:var(--ink)}
.anomlist .item a{font-family:var(--mono);font-size:.72rem}
h3.tabname{font-family:var(--mono);font-size:.95rem;font-weight:700;margin:1.6rem 0 .6rem}
footer{border-top:1px solid var(--line);margin-top:4rem;padding:2rem 0 3rem}
.fine{font-family:var(--mono);font-size:.68rem;color:var(--dim);line-height:1.8;margin:0}
@media(prefers-reduced-motion:reduce){*{animation:none!important;transition:none!important}}
"""

# ---------------------------------------------------------------- page templates

PAGE = """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>__TITLE__</title>
<style>__CSS__</style>
</head>
<body>
<div class="wrap">

<header>
  <div class="mark">
    <span class="glyph">A</span>
    <span class="name">THE ABSORPTION LOOP &middot; PROVENANCE</span>
  </div>
  <h1>__HEADING__</h1>
  <p class="muted" style="max-width:40rem">Your artifact was read as it exists —
    nothing in it was edited. This page is the receipt: every column, every row,
    and every question the harvester routed back to you.</p>
  <div class="rule"></div>
  <dl class="meta">__META__</dl>
</header>

<section>
  <div class="head">
    <div class="eyebrow">Columns</div>
    <h2>What each column became</h2>
  </div>
  __COLUMN_MAPS__
</section>

<section>
  <div class="head">
    <div class="eyebrow">Rows</div>
    <h2>The fate of every row</h2>
    <p class="muted" style="max-width:38rem">Click a row to see the verbatim cells
      next to the quest they became. Row numbers match your own sheet.</p>
  </div>
  <div id="ledger"></div>
</section>

<section>
  <div class="head">
    <div class="eyebrow">Questions for you</div>
    <h2>__ANOM_HEADING__</h2>
    <p class="muted" style="max-width:38rem">Nothing here was &ldquo;fixed&rdquo; —
      the harvester copies your content verbatim and flags what looks off. These are
      yours to rule on.</p>
  </div>
  <div id="anomalies" class="anomlist"></div>
</section>

<section>
  <div class="head">
    <div class="eyebrow">Downstream</div>
    <h2>Where your quests went</h2>
  </div>
  <div class="diagram" role="img" aria-label="__LOOP_ALT__">
  __LOOP_SVG__
  </div>
  <p class="caption">Solid lines are built and running. Dashed lines are where a
    human acts. <a href="quest-picker.html">Open the quest picker</a> &middot;
    <a href="provenance.html">all sources</a></p>
</section>

<footer>
  <p class="fine">Generated by recipes/quest-catalogs/render_provenance.py from the
    provenance sidecar — regenerate with <code>python render_provenance.py</code>
    after any harvest. Contracts: schema.md &middot; quest-view-schema.md.</p>
</footer>

</div>
<script>
var DATA = __DATA__;

function el(tag, cls, text){
  var e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text !== undefined && text !== null) e.textContent = text;
  return e;
}

var OUTCOME_CHIP = {
  quest:       ["c-quest", "quest"],
  rank:        ["c-quest", "rank"],
  achievement: ["c-ach", "achievement"],
  detail:      ["c-dim", "detail"],
  skipped:     ["c-skipped", "skipped"],
  blank:       ["c-dim", "blank"],
  filler:      ["c-dim", "not harvested"],
  banner:      ["c-dim", "banner"],
  section:     ["c-dim", "section"],
  header:      ["c-dim", "header"]
};

// anomalies joined by (tab,row) and by quest_id
var anomsByRow = {}, anomsByQuest = {};
DATA.provenance.anomalies.forEach(function(a){
  if (a.row !== null && a.row !== undefined){
    var k = (a.tab || "") + "\\u0000" + a.row;
    (anomsByRow[k] = anomsByRow[k] || []).push(a);
  } else if (a.quest_id){
    (anomsByQuest[a.quest_id] = anomsByQuest[a.quest_id] || []).push(a);
  }
});

var rowAnchorByQuest = {};  // quest_id -> anchor id, for anomaly links

function headerFor(tab, letter){
  for (var i = 0; i < tab.columns.length; i++)
    if (tab.columns[i].letter === letter) return tab.columns[i].header;
  return "";
}

function renderRow(tab, ti, r){
  var d = document.createElement("details");
  d.dataset.outcome = r.outcome;
  d.id = "row-" + ti + "-" + r.row;
  var s = document.createElement("summary");
  var label = (DATA.provenance.tabs.length > 1 && tab.tab ? tab.tab + " " : "") + "row " + r.row;
  s.appendChild(el("span", "rowno", label));
  var chip = OUTCOME_CHIP[r.outcome] || ["c-dim", r.outcome];
  s.appendChild(el("span", "chip " + chip[0], chip[1]));
  var q = r.quest_id ? DATA.entries[r.quest_id] : null;
  var firstCell = r.cells ? r.cells[Object.keys(r.cells).sort()[0]] : "";
  var name = q ? q.name : (firstCell || "").split("\\n")[0];
  if (name) s.appendChild(el("span", "rname", name));
  if (q && (r.outcome === "quest" || r.outcome === "rank" || r.outcome === "achievement"))
    s.appendChild(el("span", "rbecame", "\\u2192 " + r.quest_id));
  if (r.reason) s.appendChild(el("span", "rbecame", r.reason));
  // row-keyed anomalies may carry the tab name or not (single-tab adapters omit it)
  var anoms = (anomsByRow[(tab.tab || "") + "\\u0000" + r.row] || []).slice();
  if (tab.tab) anoms = anoms.concat(anomsByRow["\\u0000" + r.row] || []);
  if (r.quest_id) anoms = anoms.concat(anomsByQuest[r.quest_id] || []);
  if (anoms.length) s.appendChild(el("span", "abadge", "\\u26a0 " + anoms.length));
  d.appendChild(s);
  if (r.quest_id) rowAnchorByQuest[r.quest_id] = d.id;

  var body = el("div", "rbody");
  if (r.cells){
    var cells = el("div", "cells");
    Object.keys(r.cells).sort(function(a,b){
      return (a.length - b.length) || (a < b ? -1 : 1);
    }).forEach(function(letter){
      var c = el("div", "cell");
      c.appendChild(el("b", null, letter + " \\u00b7 " + headerFor(tab, letter)));
      var v = el("div"); v.textContent = r.cells[letter]; c.appendChild(v);
      cells.appendChild(c);
    });
    body.appendChild(cells);
  }
  if (q){
    var b = el("p", "became");
    b.appendChild(el("b", null, "Became: "));
    if (q.kind === "quest"){
      b.appendChild(document.createTextNode(
        q.name + " (" + r.quest_id + ")" +
        (q.category ? " \\u00b7 " + q.category : "") +
        (q.coopable ? " \\u00b7 coopable" : "") +
        (q.auto_checked ? " \\u00b7 auto-checked" : "")));
      if (q.bot_command){
        b.appendChild(document.createElement("br"));
        var cd = document.createElement("code"); cd.textContent = q.bot_command;
        b.appendChild(cd);
      }
      if (q.reward){
        b.appendChild(document.createElement("br"));
        b.appendChild(document.createTextNode("Reward: " + q.reward));
      }
    } else {
      b.appendChild(document.createTextNode(
        q.name + " (" + r.quest_id + ") \\u00b7 " +
        (q.kind === "rank" ? "rank, tier " + q.tier : q.kind)));
      if (q.requirements && q.requirements.length){
        b.appendChild(document.createElement("br"));
        b.appendChild(document.createTextNode("Requires: " + q.requirements.join(" ")));
      }
      if (q.rewards && q.rewards.length){
        b.appendChild(document.createElement("br"));
        b.appendChild(document.createTextNode("Rewards: " + q.rewards.join("; ")));
      }
    }
    body.appendChild(b);
  }
  anoms.forEach(function(a){
    body.appendChild(el("p", "rowanom", a.message));
  });
  if (body.children.length) d.appendChild(body);
  else d.querySelector("summary").style.cursor = "default";
  return d;
}

var ledger = document.getElementById("ledger");
DATA.provenance.tabs.forEach(function(tab, ti){
  if (DATA.provenance.tabs.length > 1 && tab.tab)
    ledger.appendChild(el("h3", "tabname", tab.tab));
  var box = el("div", "ledger");
  var blanks = [];
  function flushBlanks(){
    if (!blanks.length) return;
    if (blanks.length <= 2){
      blanks.forEach(function(r){ box.appendChild(renderRow(tab, ti, r)); });
    } else {
      box.appendChild(el("div", "gap", "rows " + blanks[0].row + "\\u2013" +
        blanks[blanks.length-1].row + " \\u00b7 " + blanks.length + " blank rows"));
    }
    blanks = [];
  }
  tab.rows.forEach(function(r){
    if (r.outcome === "blank"){ blanks.push(r); return; }
    flushBlanks();
    box.appendChild(renderRow(tab, ti, r));
  });
  flushBlanks();
  ledger.appendChild(box);
});

var anomBox = document.getElementById("anomalies");
if (!DATA.provenance.anomalies.length){
  anomBox.appendChild(el("p", "muted", "No anomalies — nothing looked off."));
}
DATA.provenance.anomalies.forEach(function(a){
  var item = el("div", "item");
  var who = el("b", null, a.quest_name ? a.quest_name :
    (a.row !== null && a.row !== undefined ? ((a.tab ? a.tab + " " : "") + "row " + a.row) : "source"));
  item.appendChild(who);
  item.appendChild(document.createTextNode(" \\u2014 " + a.message + " "));
  var target = null;
  if (a.row !== null && a.row !== undefined){
    for (var ti = 0; ti < DATA.provenance.tabs.length; ti++)
      if ((DATA.provenance.tabs[ti].tab || "") === (a.tab || "") ||
          DATA.provenance.tabs.length === 1)
        { target = "row-" + ti + "-" + a.row; break; }
  } else if (a.quest_id && rowAnchorByQuest[a.quest_id]){
    target = rowAnchorByQuest[a.quest_id];
  }
  if (target && document.getElementById(target)){
    var link = el("a", null, "\\u2192 see the row");
    link.href = "#" + target;
    link.addEventListener("click", function(){
      document.getElementById(target).open = true;
    });
    item.appendChild(link);
  }
  anomBox.appendChild(item);
});
</script>
</body>
</html>
"""

INDEX_PAGE = """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Provenance — every absorbed source</title>
<style>__CSS__</style>
</head>
<body>
<div class="wrap">
<header>
  <div class="mark">
    <span class="glyph">A</span>
    <span class="name">THE ABSORPTION LOOP &middot; PROVENANCE</span>
  </div>
  <h1>Every absorbed artifact, and its receipt</h1>
  <p class="muted" style="max-width:40rem">One page per source: what the harvester
    read, what it became, and the questions it routed back. Leaders' content passes
    through verbatim — these pages are the proof.</p>
  <div class="rule"></div>
</header>
<section>
  <div class="grid">__CARDS__</div>
</section>
<footer>
  <p class="fine">Generated by recipes/quest-catalogs/render_provenance.py.
    <a href="quest-picker.html">Open the quest picker</a></p>
</footer>
</div>
</body>
</html>
"""

# ---------------------------------------------------------------- SVG builders


def esc(s):
    return html.escape(str(s), quote=True)


def truncate(s, n):
    s = str(s)
    return s if len(s) <= n else s[: n - 1] + "…"


def column_map_svg(tab):
    """Left: the source column as the leader named it. Right: the canonical fields
    it became. One row per mapped column; solid edges — this path is built."""
    cols = tab["columns"]
    row_h, top, box_h = 34, 10, 26
    height = top * 2 + row_h * len(cols)
    left_w, right_x, right_w = 330, 430, 340
    parts = [
        f'<svg viewBox="0 0 800 {height}" xmlns="http://www.w3.org/2000/svg">',
        '<defs><marker id="cm" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="6" '
        'markerHeight="6" orient="auto-start-reverse">'
        '<path d="M0 0L8 4L0 8z" fill="var(--dim)"/></marker></defs>',
    ]
    for n, c in enumerate(cols):
        y = top + n * row_h
        mid = y + box_h / 2
        label = f"{c['letter']} · {truncate(c['header'], 40)}"
        fields = truncate(" · ".join(c["fields"]), 46)
        parts.append(f'<rect class="n-box" x="10" y="{y}" width="{left_w}" height="{box_h}" rx="5"/>')
        parts.append(f'<text class="t-ink" x="{10 + left_w / 2}" y="{mid + 4.5}" text-anchor="middle">{esc(label)}</text>')
        parts.append(f'<line class="edge" x1="{10 + left_w}" y1="{mid}" x2="{right_x - 6}" y2="{mid}" marker-end="url(#cm)"/>')
        parts.append(f'<rect class="n-wood" x="{right_x}" y="{y}" width="{right_w}" height="{box_h}" rx="5"/>')
        parts.append(f'<text class="t-woodtxt" x="{right_x + right_w / 2}" y="{mid + 4.5}" text-anchor="middle">{esc(fields)}</text>')
    parts.append("</svg>")
    return "\n".join(parts)


def loop_svg(guild, quest_count, picker_total, anomaly_count):
    """This source's slice of the whole loop, with the anomalies branch back to
    the leader and the player's dashed step into quest-view.json."""
    return f"""<svg viewBox="0 0 960 210" xmlns="http://www.w3.org/2000/svg">
<defs><marker id="lp" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="6.5"
 markerHeight="6.5" orient="auto-start-reverse">
 <path d="M0 0L8 4L0 8z" fill="var(--dim)"/></marker></defs>
<rect class="n-box" x="16" y="36" width="170" height="44" rx="5"/>
<text class="t-ink" x="101" y="62" text-anchor="middle">your artifact</text>
<rect class="n-box" x="256" y="36" width="190" height="44" rx="5"/>
<text class="t-ink" x="351" y="56" text-anchor="middle">{esc(guild)} catalog</text>
<text class="t-dim" x="351" y="71" text-anchor="middle">{quest_count} quests</text>
<rect class="n-box" x="516" y="36" width="190" height="44" rx="5"/>
<text class="t-ink" x="611" y="56" text-anchor="middle">quest picker</text>
<text class="t-dim" x="611" y="71" text-anchor="middle">{quest_count} of {picker_total} quests</text>
<rect class="n-wood" x="776" y="36" width="168" height="44" rx="5"/>
<text class="t-woodtxt" x="860" y="56" text-anchor="middle">quest-view.json</text>
<text class="t-dim" x="860" y="71" text-anchor="middle">one player's picks</text>
<line class="edge" x1="186" y1="58" x2="252" y2="58" marker-end="url(#lp)"/>
<line class="edge" x1="446" y1="58" x2="512" y2="58" marker-end="url(#lp)"/>
<line class="edge-soft" x1="706" y1="58" x2="772" y2="58" marker-end="url(#lp)"/>
<rect class="n-amber" x="256" y="140" width="190" height="44" rx="5"/>
<text class="t-ambertxt" x="351" y="160" text-anchor="middle">anomalies</text>
<text class="t-dim" x="351" y="175" text-anchor="middle">{anomaly_count} question(s) for you</text>
<line class="edge" x1="351" y1="80" x2="351" y2="136" marker-end="url(#lp)"/>
<path class="edge-soft" d="M252 162 C 120 162, 90 120, 96 84" marker-end="url(#lp)"/>
<text class="t-dim" x="60" y="120">you rule on them</text>
</svg>"""


# ---------------------------------------------------------------- page assembly


def embed_json(obj):
    return json.dumps(obj, ensure_ascii=False).replace("</", "<\\/")


def meta_rows(sidecar, kind="quest-catalog"):
    src = sidecar["source"]
    rows = [
        ("Artifact", os.path.basename(src["path"] or "") or "(inline template)"),
        ("Tab(s)", " · ".join(t["tab"] for t in sidecar["tabs"] if t["tab"]) or "—"),
        ("Adapter", src["adapter"]),
        ("Retrieved", src.get("retrieved") or "—"),
        ("Rows seen", str(sidecar["counts"]["rows_seen"])),
        ("Quests" if kind == "quest-catalog" else "Entries", str(sidecar["counts"]["quests"])),
        ("Skipped", str(sidecar["counts"]["skipped"])),
        ("Anomalies", str(sidecar["counts"]["anomalies"])),
    ]
    if src.get("url"):
        rows.insert(1, ("Source URL", src["url"]))
    return "\n".join(f"<dt>{esc(k)}</dt><dd>{esc(v)}</dd>" for k, v in rows)


def entry_summaries(artifact):
    """Per-entry facts for the ledger's 'Became:' panel, keyed by the join id.
    Works for both artifact kinds."""
    out = {}
    if "quests" in artifact:
        for q in artifact["quests"]:
            out[q["quest_id"]] = {
                "kind": "quest",
                "name": q["name"],
                "category": q.get("category") or "",
                "coopable": bool(q.get("coopable")),
                "auto_checked": bool(q.get("auto_checked")),
                "bot_command": q.get("bot_command"),
                "reward": q.get("reward"),
            }
        return out
    for r in artifact.get("ranks", []):
        out[r["entry_id"]] = {
            "kind": "rank", "name": r["name"], "tier": r["tier"],
            "requirements": r["requirements"], "rewards": r.get("rewards", []),
        }
    for key, kind in (("achievements", "achievement"),
                      ("village_achievements", "village achievement")):
        for e in artifact.get(key, []):
            out[e["entry_id"]] = {
                "kind": kind, "name": e["name"],
                "requirements": e["requirements"], "rewards": e.get("rewards", []),
            }
    return out


def ladder_loop_svg(guild, n_ranks, n_ach, anomaly_count):
    """The ladder's slice of the loop: artifact -> ladder JSON -> the rendered
    ladder page and the mod's rank actions, anomalies branching back."""
    return f"""<svg viewBox="0 0 960 210" xmlns="http://www.w3.org/2000/svg">
<defs><marker id="lp" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="6.5"
 markerHeight="6.5" orient="auto-start-reverse">
 <path d="M0 0L8 4L0 8z" fill="var(--dim)"/></marker></defs>
<rect class="n-box" x="16" y="36" width="170" height="44" rx="5"/>
<text class="t-ink" x="101" y="62" text-anchor="middle">your artifact</text>
<rect class="n-box" x="256" y="36" width="200" height="44" rx="5"/>
<text class="t-ink" x="356" y="56" text-anchor="middle">{esc(guild)} rank ladder</text>
<text class="t-dim" x="356" y="71" text-anchor="middle">{n_ranks} ranks &#183; {n_ach} achievements</text>
<rect class="n-box" x="546" y="16" width="200" height="44" rx="5"/>
<text class="t-ink" x="646" y="36" text-anchor="middle">rendered ladder page</text>
<text class="t-dim" x="646" y="51" text-anchor="middle">recipes/rank-ladders/render.py</text>
<rect class="n-wood" x="546" y="86" width="200" height="44" rx="5"/>
<text class="t-woodtxt" x="646" y="106" text-anchor="middle">in-game rank actions</text>
<text class="t-dim" x="646" y="121" text-anchor="middle">one submit action per rank-up</text>
<line class="edge" x1="186" y1="58" x2="252" y2="58" marker-end="url(#lp)"/>
<line class="edge" x1="456" y1="50" x2="542" y2="38" marker-end="url(#lp)"/>
<line class="edge-soft" x1="456" y1="66" x2="542" y2="105" marker-end="url(#lp)"/>
<text class="t-dim" x="470" y="92">once the real</text>
<text class="t-dim" x="470" y="106">command is known</text>
<rect class="n-amber" x="256" y="140" width="200" height="44" rx="5"/>
<text class="t-ambertxt" x="356" y="160" text-anchor="middle">anomalies</text>
<text class="t-dim" x="356" y="175" text-anchor="middle">{anomaly_count} question(s) for you</text>
<line class="edge" x1="356" y1="80" x2="356" y2="136" marker-end="url(#lp)"/>
<path class="edge-soft" d="M252 162 C 120 162, 90 120, 96 84" marker-end="url(#lp)"/>
<text class="t-dim" x="60" y="120">you rule on them</text>
</svg>"""


def render_source_page(sidecar, catalog, picker_total, out_path, kind="quest-catalog"):
    guild = sidecar["source"]["guild"] or "?"
    counts = sidecar["counts"]

    if sidecar.get("mode") == "passthrough":
        column_maps = (
            '<p class="muted" style="max-width:38rem">This source is a filled GM '
            "template — it is already in catalog shape, so fields pass through "
            "verbatim with no column mapping.</p>"
        )
    else:
        blocks = []
        for tab in sidecar["tabs"]:
            if len(sidecar["tabs"]) > 1 and tab["tab"]:
                blocks.append(f'<h3 class="tabname">{esc(tab["tab"])}</h3>')
            alt = f"Column mapping for tab {tab['tab']}: each source column and the canonical fields it became."
            blocks.append(f'<div class="diagram" role="img" aria-label="{esc(alt)}">\n{column_map_svg(tab)}\n</div>')
        header_drift = [a for a in sidecar["anomalies"] if a["kind"] == "header_mismatch"]
        if header_drift:
            blocks.append(
                f'<p class="caption">⚠ {len(header_drift)} header(s) did not match what the '
                f"adapter expected — the rows below were still read positionally. "
                f"Details under “Questions for you”.</p>"
            )
        column_maps = "\n".join(blocks)

    n_anoms = counts["anomalies"]
    anom_heading = (
        "No questions — nothing looked off" if n_anoms == 0
        else f"{n_anoms} question(s) the harvester routed back"
    )
    if kind == "rank-ladder":
        n_ranks = len(catalog.get("ranks", []))
        n_ach = (len(catalog.get("achievements", []))
                 + len(catalog.get("village_achievements", [])))
        heading = f"How your artifact became the {guild} rank ladder"
        loop_alt = (
            f"Your artifact becomes the {guild} rank ladder of {n_ranks} ranks and "
            f"{n_ach} achievements, feeding the rendered ladder page and the mod's "
            f"rank submit actions. Anomalies branch back to you.")
        the_loop_svg = ladder_loop_svg(guild, n_ranks, n_ach, n_anoms)
    else:
        heading = f"How your artifact became the {guild} catalog"
        loop_alt = (
            f"Your artifact becomes the {guild} catalog of {counts['quests']} quests, "
            f"which joins the quest picker of {picker_total} quests; a player saves "
            f"quest-view.json. Anomalies branch back to you.")
        the_loop_svg = loop_svg(guild, counts["quests"], picker_total, n_anoms)
    data = {
        "provenance": sidecar,
        "entries": entry_summaries(catalog),
    }
    page = (
        PAGE
        .replace("__CSS__", TOKENS_CSS)
        .replace("__TITLE__", esc(f"Provenance — {guild} ({sidecar['source']['id']})"))
        .replace("__HEADING__", esc(heading))
        .replace("__META__", meta_rows(sidecar, kind))
        .replace("__COLUMN_MAPS__", column_maps)
        .replace("__ANOM_HEADING__", esc(anom_heading))
        .replace("__LOOP_ALT__", esc(loop_alt))
        .replace("__LOOP_SVG__", the_loop_svg)
        .replace("__DATA__", embed_json(data))
    )
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(page)


def render_index(entries, out_path):
    cards = []
    for e in entries:
        if e.get("page"):
            cards.append(
                f'<a class="card" href="{esc(e["page"])}" style="border-bottom:1px solid var(--line)">'
                f'<span class="tag t-run">harvested</span>'
                f'<h3>{esc(e["guild"])}</h3>'
                f'<p>{esc(e["artifact"])}</p>'
                f'<p>{esc(e["counts_line"])}</p>'
                f'<p class="muted" style="font-family:var(--mono);font-size:.68rem">'
                f'retrieved {esc(e["retrieved"] or "—")}</p></a>'
            )
        else:
            cards.append(
                f'<div class="card ghost"><span class="tag t-ghost">not harvested</span>'
                f'<h3>{esc(e["guild"] or e["id"])}</h3>'
                f'<p>{esc(e["note"] or "disabled in sources.json")}</p></div>'
            )
    page = (
        INDEX_PAGE
        .replace("__CSS__", TOKENS_CSS)
        .replace("__CARDS__", "\n".join(cards))
    )
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(page)


# ---------------------------------------------------------------- driver


def load(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def main():
    if len(sys.argv) > 2:
        out, prov_path, cat_path = sys.argv[1], sys.argv[2], sys.argv[3]
        sidecar, catalog = load(prov_path), load(cat_path)
        kind = "rank-ladder" if "ranks" in catalog else "quest-catalog"
        render_source_page(sidecar, catalog, len(catalog.get("quests", [])), out, kind=kind)
        print(f"provenance page -> {out}")
        return

    with open(os.path.join(HERE, "sources.json"), encoding="utf-8-sig") as f:
        config = json.load(f)

    out_dir = os.path.normpath(os.path.join(HERE, "../../data/processed"))
    harvested, entries = [], []
    for source in config["sources"]:
        enabled = source.get("enabled", True) and source.get("output")
        if not enabled:
            entries.append({"id": source["id"], "guild": source.get("guild"),
                            "note": source.get("note"), "page": None})
            continue
        stem = os.path.normpath(os.path.join(HERE, source["output"]))[: -len(".json")]
        prov_path, cat_path = stem + "-provenance.json", stem + ".json"
        if not os.path.exists(prov_path):
            raise SystemExit(f"[{source['id']}] no provenance sidecar at {prov_path} — run harvest.py first")
        harvested.append((source, load(prov_path), load(cat_path)))

    picker_total = sum(len(c["quests"]) for _, _, c in harvested if "quests" in c)
    for source, sidecar, catalog in harvested:
        kind = source.get("kind", "quest-catalog")
        page_name = f"provenance-{source['id']}.html"
        render_source_page(sidecar, catalog, picker_total,
                           os.path.join(out_dir, page_name), kind=kind)
        c = sidecar["counts"]
        if kind == "rank-ladder":
            n_ach = (len(catalog.get("achievements", []))
                     + len(catalog.get("village_achievements", [])))
            counts_line = (f"{len(catalog.get('ranks', []))} ranks · {n_ach} achievements "
                           f"· {c['anomalies']} question(s)")
        else:
            counts_line = (f"{c['quests']} quests · {c['skipped']} skipped "
                           f"· {c['anomalies']} question(s)")
        entries.insert(
            sum(1 for e in entries if e.get("page")),  # keep harvested cards first
            {
                "id": source["id"], "guild": sidecar["source"]["guild"],
                "artifact": os.path.basename(sidecar["source"]["path"] or "") or "(inline)",
                "counts_line": counts_line, "retrieved": sidecar["source"].get("retrieved"),
                "page": page_name,
            },
        )
        print(f"[{source['id']}] provenance page -> data/processed/{page_name}")

    render_index(entries, os.path.join(out_dir, "provenance.html"))
    print(f"index ({len(harvested)} harvested, {len(entries) - len(harvested)} reserved) -> data/processed/provenance.html")


if __name__ == "__main__":
    main()

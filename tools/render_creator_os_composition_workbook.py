#!/usr/bin/env python3
"""Render the exact-slice Creator OS composition workbook."""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import re
import struct
import sys
from pathlib import Path
from typing import Any


REPO = Path(__file__).resolve().parents[1]
SOURCE = REPO / "docs" / "creator-os-composition-workbook.json"
OUTPUT = REPO / "docs" / "creator-os-composition-workbook.html"
SCHEMA = "creator-os-composition-workbook/v1"
REVIEW_SCHEMA = "creator-os-composition-review/v1"
STATUSES = {"proven", "implemented", "simulated", "open"}
ID = re.compile(r"^[a-z0-9][a-z0-9-]{1,79}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")


class WorkbookError(RuntimeError):
    pass


def require_text(value: Any, where: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise WorkbookError(f"{where} must be non-empty text")
    return value


def require_id(value: Any, where: str) -> str:
    value = require_text(value, where)
    if not ID.fullmatch(value):
        raise WorkbookError(f"{where} is not a bounded id")
    return value


def local_file(relative: Any, where: str) -> Path:
    relative = require_text(relative, where)
    if Path(relative).is_absolute() or ".." in Path(relative).parts:
        raise WorkbookError(f"{where} must be a docs-relative path")
    candidate = (SOURCE.parent / relative).resolve()
    if SOURCE.parent.resolve() not in candidate.parents or not candidate.is_file():
        raise WorkbookError(f"{where} does not resolve to a tracked docs file: {relative}")
    return candidate


def png_dimensions(path: Path) -> tuple[int, int]:
    data = path.read_bytes()[:24]
    if data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        raise WorkbookError(f"screenshot is not a PNG: {path.relative_to(REPO)}")
    return struct.unpack(">II", data[16:24])


def load_manifest(path: Path = SOURCE) -> tuple[dict[str, Any], str]:
    raw = path.read_bytes()
    try:
        value = json.loads(raw)
    except (OSError, json.JSONDecodeError) as exc:
        raise WorkbookError(f"cannot read workbook manifest: {exc}") from exc
    validate_manifest(value)
    return value, hashlib.sha256(raw).hexdigest()


def validate_manifest(value: Any) -> None:
    if not isinstance(value, dict) or value.get("schema") != SCHEMA:
        raise WorkbookError(f"workbook schema must be {SCHEMA}")
    require_id(value.get("workbook_id"), "workbook_id")
    if not isinstance(value.get("revision"), int) or value["revision"] < 1:
        raise WorkbookError("revision must be a positive integer")
    for key in ("title", "subtitle", "fixture"):
        require_text(value.get(key), key)

    proof = value.get("proof_boundary")
    if not isinstance(proof, dict):
        raise WorkbookError("proof_boundary must be an object")
    require_text(proof.get("proof_level"), "proof_boundary.proof_level")
    for key in ("accepted", "not_claimed"):
        rows = proof.get(key)
        if not isinstance(rows, list) or not rows:
            raise WorkbookError(f"proof_boundary.{key} must be a non-empty list")
        for index, row in enumerate(rows):
            require_text(row, f"proof_boundary.{key}[{index}]")

    ids: set[str] = set()
    stages = value.get("stages")
    if not isinstance(stages, list) or len(stages) != 5:
        raise WorkbookError("stages must contain the five composition stages")
    for index, stage in enumerate(stages):
        stage_id = require_id(stage.get("id"), f"stages[{index}].id")
        if stage_id in ids:
            raise WorkbookError(f"duplicate id: {stage_id}")
        ids.add(stage_id)
        if stage.get("status") not in STATUSES:
            raise WorkbookError(f"stages[{index}].status is unsupported")
        require_text(stage.get("title"), f"stages[{index}].title")
        require_text(stage.get("summary"), f"stages[{index}].summary")
        evidence = stage.get("evidence")
        if not isinstance(evidence, list):
            raise WorkbookError(f"stages[{index}].evidence must be a list")
        for item_index, item in enumerate(evidence):
            require_text(item.get("label"), f"stages[{index}].evidence[{item_index}].label")
            local_file(item.get("path"), f"stages[{index}].evidence[{item_index}].path")

    screenshots = value.get("screenshots")
    if not isinstance(screenshots, list) or len(screenshots) != 3:
        raise WorkbookError("screenshots must contain the three accepted views")
    for index, shot in enumerate(screenshots):
        shot_id = require_id(shot.get("id"), f"screenshots[{index}].id")
        if shot_id in ids:
            raise WorkbookError(f"duplicate id: {shot_id}")
        ids.add(shot_id)
        for key in ("title", "caption"):
            require_text(shot.get(key), f"screenshots[{index}].{key}")
        path = local_file(shot.get("path"), f"screenshots[{index}].path")
        digest = shot.get("sha256")
        if not isinstance(digest, str) or not SHA256.fullmatch(digest):
            raise WorkbookError(f"screenshots[{index}].sha256 must be SHA-256")
        if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise WorkbookError(f"screenshots[{index}] hash drift")
        dimensions = (shot.get("width"), shot.get("height"))
        if dimensions != png_dimensions(path):
            raise WorkbookError(f"screenshots[{index}] dimension drift")

    actions = value.get("actions")
    if not isinstance(actions, list) or len(actions) != 8:
        raise WorkbookError("actions must contain exactly eight bounded human actions")
    for index, action in enumerate(actions):
        action_id = require_id(action.get("id"), f"actions[{index}].id")
        if action_id in ids:
            raise WorkbookError(f"duplicate id: {action_id}")
        ids.add(action_id)
        for key in ("title", "prompt", "precondition"):
            require_text(action.get(key), f"actions[{index}].{key}")

    for collection in ("verdicts", "next_attacks", "commands"):
        rows = value.get(collection)
        if not isinstance(rows, list) or not rows:
            raise WorkbookError(f"{collection} must be a non-empty list")
        local_ids: set[str] = set()
        for index, row in enumerate(rows):
            row_id = require_id(row.get("id"), f"{collection}[{index}].id")
            if row_id in local_ids:
                raise WorkbookError(f"duplicate {collection} id: {row_id}")
            local_ids.add(row_id)
            require_text(row.get("label"), f"{collection}[{index}].label")
            if collection == "commands":
                require_text(row.get("command"), f"commands[{index}].command")
                require_text(row.get("detail"), f"commands[{index}].detail")

    review = value.get("review")
    if not isinstance(review, dict) or review.get("schema") != REVIEW_SCHEMA:
        raise WorkbookError(f"review schema must be {REVIEW_SCHEMA}")
    require_text(review.get("storage_key"), "review.storage_key")
    for key in ("max_action_note_chars", "max_panel_note_chars", "max_observation_chars", "max_cant_answer_chars"):
        if not isinstance(review.get(key), int) or not 1 <= review[key] <= 20000:
            raise WorkbookError(f"review.{key} must be in 1..20000")


def link(item: dict[str, Any]) -> str:
    return f'<a href="{html.escape(item["path"], quote=True)}">{html.escape(item["label"])}</a>'


def render(manifest: dict[str, Any], manifest_hash: str) -> str:
    stages = "".join(
        f'''<article class="stage status-{stage["status"]}"><span class="status">{html.escape(stage["status"])}</span><h3>{html.escape(stage["title"])}</h3><p>{html.escape(stage["summary"])}</p><div class="links">{"".join(link(item) for item in stage["evidence"]) or '<span>Evidence arrives with this connection.</span>'}</div></article>'''
        for stage in manifest["stages"]
    )
    metrics = "".join(
        f'<div><strong>{html.escape(item["value"])}</strong><span>{html.escape(item["label"])}</span></div>'
        for item in manifest["accepted_values"]
    )
    accepted = "".join(f"<li>{html.escape(item)}</li>" for item in manifest["proof_boundary"]["accepted"])
    not_claimed = "".join(f"<li>{html.escape(item)}</li>" for item in manifest["proof_boundary"]["not_claimed"])
    composition = "".join(
        f'<li><span>{index}</span><strong>{html.escape(item)}</strong></li>'
        for index, item in enumerate(manifest["story"]["composition"], 1)
    )
    shots = "".join(
        f'''<figure id="panel-{shot["id"]}"><img src="{html.escape(shot["path"], quote=True)}" width="{shot["width"]}" height="{shot["height"]}" alt="{html.escape(shot["title"], quote=True)}"><figcaption><h3>{html.escape(shot["title"])}</h3><p>{html.escape(shot["caption"])}</p><label class="workbench">Notes on this view<textarea data-panel-note="{shot["id"]}" maxlength="{manifest["review"]["max_panel_note_chars"]}" placeholder="What does this image clarify, contradict, or leave unresolved?"></textarea></label></figcaption></figure>'''
        for shot in manifest["screenshots"]
    )
    actions = "".join(
        f'''<article class="action" data-action-card="{action["id"]}"><div class="action-head"><span>{index:02d}</span><label><input type="checkbox" data-action-check="{action["id"]}"> <strong>{html.escape(action["title"])}</strong></label></div><p>{html.escape(action["prompt"])}</p><small>Precondition: {html.escape(action["precondition"])}</small><label>Your exact observation<textarea data-action-note="{action["id"]}" maxlength="{manifest["review"]["max_action_note_chars"]}" placeholder="Record the judgment, not a machine fact."></textarea></label></article>'''
        for index, action in enumerate(manifest["actions"], 1)
    )
    commands = "".join(
        f'''<article class="command"><div><strong>{html.escape(command["label"])}</strong><p>{html.escape(command["detail"])}</p></div><div class="command-line"><code>{html.escape(command["command"])}</code><button type="button" data-copy="{html.escape(command["command"], quote=True)}">Copy</button></div></article>'''
        for command in manifest["commands"]
    )
    verdict_options = "".join(
        f'<option value="{item["id"]}">{html.escape(item["label"])}</option>' for item in manifest["verdicts"]
    )
    attack_options = "".join(
        f'<option value="{item["id"]}">{html.escape(item["label"])}</option>' for item in manifest["next_attacks"]
    )
    limitations = "".join(f"<li>{html.escape(item)}</li>" for item in manifest["limitations"])
    embedded = json.dumps(manifest, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")

    page = r'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>__TITLE__ · Creator OS composition workbook</title>
<style>
:root{--ink:#eef3f8;--muted:#aab4c1;--paper:#0b1119;--panel:#121b26;--line:#293647;--gold:#e7b35a;--green:#76d39a;--blue:#7fb6f0;--red:#ef8b82;--serif:Georgia,serif;font-family:Inter,Segoe UI,sans-serif;color:var(--ink);background:var(--paper)}*{box-sizing:border-box}body{margin:0;background:radial-gradient(circle at 80% 0,#18283a 0,transparent 36rem),var(--paper);line-height:1.55}.skip{position:absolute;left:-9999px}.skip:focus{left:1rem;top:1rem;background:#fff;color:#000;padding:.6rem;z-index:10}.wrap{width:min(1180px,calc(100% - 2rem));margin:auto}header{padding:4.5rem 0 2.5rem;border-bottom:1px solid var(--line)}.eyebrow,.status{text-transform:uppercase;letter-spacing:.14em;font-size:.72rem;font-weight:800;color:var(--gold)}h1,h2,h3{font-family:var(--serif);line-height:1.08}h1{font-size:clamp(2.7rem,7vw,5.8rem);max-width:850px;margin:.5rem 0 1rem}h2{font-size:clamp(1.8rem,4vw,3rem);margin:0 0 1rem}.lede{font-size:1.2rem;color:var(--muted);max-width:800px}.toolbar{display:flex;gap:.7rem;flex-wrap:wrap;margin-top:1.5rem}button,.button{border:1px solid var(--line);background:#182434;color:var(--ink);padding:.65rem .9rem;border-radius:.45rem;font:inherit;cursor:pointer}button:hover,.button:hover{border-color:var(--gold)}main section{padding:3.5rem 0;border-bottom:1px solid var(--line)}.map{display:grid;grid-template-columns:repeat(6,1fr);list-style:none;padding:0;gap:.7rem}.map li{background:var(--panel);border:1px solid var(--line);padding:1rem;min-height:120px}.map span{display:block;color:var(--gold);font-weight:800}.metrics{display:grid;grid-template-columns:repeat(3,1fr);gap:.7rem}.metrics div,.proof-box{padding:1rem;background:var(--panel);border:1px solid var(--line)}.metrics strong,.metrics span{display:block}.metrics span{color:var(--muted)}.proof-grid,.stages,.decision-grid{display:grid;grid-template-columns:1fr 1fr;gap:1rem}.proof-box h3{margin-top:0}.proof-box li{margin:.5rem 0}.stages{grid-template-columns:repeat(5,1fr)}.stage{padding:1rem;border:1px solid var(--line);background:var(--panel)}.stage h3{font-size:1.25rem}.stage p,.stage .links{color:var(--muted);font-size:.9rem}.stage .links{display:flex;flex-direction:column;gap:.3rem}.status-proven{border-top:4px solid var(--green)}.status-implemented{border-top:4px solid var(--blue)}.status-simulated{border-top:4px solid var(--gold)}.status-open{border-top:4px solid var(--red)}a{color:#a9cdf3}figure{margin:0 0 2rem;display:grid;grid-template-columns:minmax(0,1.65fr) minmax(260px,.75fr);background:var(--panel);border:1px solid var(--line)}figure img{width:100%;height:auto;display:block}figcaption{padding:1.2rem}textarea,select{width:100%;margin-top:.45rem;background:#081019;color:var(--ink);border:1px solid var(--line);border-radius:.35rem;padding:.75rem;font:inherit}textarea{min-height:110px;resize:vertical}.actions{display:grid;grid-template-columns:1fr 1fr;gap:1rem}.action{background:var(--panel);border:1px solid var(--line);padding:1rem}.action-head{display:flex;gap:.8rem;align-items:flex-start}.action-head>span{color:var(--gold);font-weight:800}.action p{color:var(--muted)}.action small{display:block;border-left:2px solid var(--blue);padding-left:.65rem;margin-bottom:1rem}.action:has(input:checked){border-color:var(--green)}.command{display:grid;grid-template-columns:1fr 1.3fr;gap:1rem;padding:1rem 0;border-bottom:1px solid var(--line)}.command p{color:var(--muted);margin:.3rem 0}.command-line{display:flex;gap:.5rem;align-items:center}.command-line code{flex:1;overflow-wrap:anywhere;background:#081019;padding:.8rem}.decision-grid>div{background:var(--panel);border:1px solid var(--line);padding:1rem}.danger{border-color:#6d3835}.privacy{color:var(--muted)}footer{padding:2rem 0;color:var(--muted)}.presenter .workbench,.presenter #decision-workbench,.presenter #commands{display:none}.presenter .actions{display:none}.toast{position:fixed;right:1rem;bottom:1rem;background:var(--green);color:#07150c;padding:.7rem 1rem;border-radius:.4rem;opacity:0;transition:.2s}.toast.show{opacity:1}@media(max-width:850px){.map,.stages{grid-template-columns:1fr 1fr}.metrics,.proof-grid,.actions,.decision-grid,figure,.command{grid-template-columns:1fr}}@media print{body{background:#fff;color:#111}.toolbar,.workbench,#decision-workbench,#commands,.toast{display:none!important}main section{break-inside:avoid;border-color:#bbb}.stage,.proof-box,.metrics div,figure{background:#fff;border-color:#bbb}a{color:#111}.map{grid-template-columns:repeat(3,1fr)}}
</style></head><body><a class="skip" href="#main">Skip to workbook</a>
<header><div class="wrap"><span class="eyebrow">Creator OS · exact-slice R&amp;D workbook</span><h1>__TITLE__</h1><p class="lede">__SUBTITLE__</p><div class="toolbar"><button id="presenter" type="button">Presenter mode</button><button id="export" type="button">Export review JSON</button><button id="import" type="button">Import review JSON</button><button id="reset" class="danger" type="button">Reset local notes</button><input id="review-file" type="file" accept="application/json,.json" hidden></div><p class="privacy">Private until export. Notes stay in this browser; checkmarks are observations, never proof.</p></div></header>
<main id="main">
<section><div class="wrap"><span class="eyebrow">The integration target</span><h2>Turn a proved structure into a community creative primitive.</h2><ol class="map">__COMPOSITION__</ol><div class="metrics">__METRICS__</div></div></section>
<section><div class="wrap"><span class="eyebrow">Proof boundary</span><h2>Assume the built values hold. Attack the next connection.</h2><p><strong>__PROOF_LEVEL__</strong></p><div class="proof-grid"><article class="proof-box"><h3>Accepted input</h3><ul>__ACCEPTED__</ul></article><article class="proof-box"><h3>Still open</h3><ul>__NOT_CLAIMED__</ul></article></div></div></section>
<section><div class="wrap"><span class="eyebrow">Living R&amp;D state</span><h2>One page shows what is real and what must connect next.</h2><div class="stages">__STAGES__</div></div></section>
<section><div class="wrap"><span class="eyebrow">Pinned visual references</span><h2>Post feedback against the exact views.</h2>__SHOTS__</div></section>
<section><div class="wrap"><span class="eyebrow">Eight human actions</span><h2>Spend the seat on meaning, authority, composition, and judgment.</h2><div class="actions">__ACTIONS__</div></div></section>
<section id="decision-workbench"><div class="wrap"><span class="eyebrow">Decision packet</span><h2>Preserve the observation. Choose the attack.</h2><div class="decision-grid"><div><label>Composition verdict<select id="verdict">__VERDICTS__</select></label><label>Next machine-owned attack<select id="next-attack">__ATTACKS__</select></label></div><div><label>Overall observations<textarea id="observations" maxlength="__MAX_OBSERVATIONS__" placeholder="What did this composition make newly possible or newly confusing?"></textarea></label><label>Anything that produced ‘can't answer why’<textarea id="cant-answer" maxlength="__MAX_CANT_ANSWER__" placeholder="Record the moment exactly. Do not invent a mechanism story here."></textarea></label></div></div></div></section>
<section id="commands"><div class="wrap"><span class="eyebrow">Copy-only operator surfaces</span><h2>Open and close the already-proven demo deliberately.</h2>__COMMANDS__</div></section>
<section><div class="wrap"><span class="eyebrow">Limitations</span><ul>__LIMITATIONS__</ul><p>Workbook manifest SHA-256 <code>__MANIFEST_HASH__</code></p></div></section>
</main><footer><div class="wrap">tn0304 · Slayers Signature Hunt venue composition · revision __REVISION__</div></footer><div id="toast" class="toast" role="status" aria-live="polite"></div>
<script>const WORKBOOK=__EMBEDDED__;const MANIFEST_HASH='__MANIFEST_HASH__';
const $=s=>document.querySelector(s),$$=s=>[...document.querySelectorAll(s)],limits=WORKBOOK.review,key=limits.storage_key;
function bounded(v,n){return typeof v==='string'?v.slice(0,n):''}function blank(){return{schema:limits.schema,workbook_id:WORKBOOK.workbook_id,workbook_revision:WORKBOOK.revision,manifest_sha256:MANIFEST_HASH,saved_at:null,checks:{},action_notes:{},panel_notes:{},observations:'',cant_answer_why:'',verdict:'unrecorded',next_attack:'unrecorded'}}
function validChoice(rows,v){return rows.some(x=>x.id===v)?v:'unrecorded'}function normalize(raw){let n=blank();if(!raw||raw.schema!==n.schema||raw.workbook_id!==n.workbook_id||raw.workbook_revision!==n.workbook_revision||raw.manifest_sha256!==n.manifest_sha256)throw Error('review_identity_mismatch');for(let a of WORKBOOK.actions){n.checks[a.id]=raw.checks?.[a.id]===true;n.action_notes[a.id]=bounded(raw.action_notes?.[a.id],limits.max_action_note_chars)}for(let p of WORKBOOK.screenshots)n.panel_notes[p.id]=bounded(raw.panel_notes?.[p.id],limits.max_panel_note_chars);n.observations=bounded(raw.observations,limits.max_observation_chars);n.cant_answer_why=bounded(raw.cant_answer_why,limits.max_cant_answer_chars);n.verdict=validChoice(WORKBOOK.verdicts,raw.verdict);n.next_attack=validChoice(WORKBOOK.next_attacks,raw.next_attack);n.saved_at=bounded(raw.saved_at,64)||null;return n}
function read(){try{return normalize(JSON.parse(localStorage.getItem(key)))}catch{return blank()}}let state=read();function apply(){for(let a of WORKBOOK.actions){$(`[data-action-check="${a.id}"]`).checked=!!state.checks[a.id];$(`[data-action-note="${a.id}"]`).value=state.action_notes[a.id]||''}for(let p of WORKBOOK.screenshots)$(`[data-panel-note="${p.id}"]`).value=state.panel_notes[p.id]||'';$('#observations').value=state.observations;$('#cant-answer').value=state.cant_answer_why;$('#verdict').value=state.verdict;$('#next-attack').value=state.next_attack}
function capture(){for(let a of WORKBOOK.actions){state.checks[a.id]=$(`[data-action-check="${a.id}"]`).checked;state.action_notes[a.id]=bounded($(`[data-action-note="${a.id}"]`).value,limits.max_action_note_chars)}for(let p of WORKBOOK.screenshots)state.panel_notes[p.id]=bounded($(`[data-panel-note="${p.id}"]`).value,limits.max_panel_note_chars);state.observations=bounded($('#observations').value,limits.max_observation_chars);state.cant_answer_why=bounded($('#cant-answer').value,limits.max_cant_answer_chars);state.verdict=validChoice(WORKBOOK.verdicts,$('#verdict').value);state.next_attack=validChoice(WORKBOOK.next_attacks,$('#next-attack').value);state.saved_at=new Date().toISOString();localStorage.setItem(key,JSON.stringify(state))}
let saveTimer;document.addEventListener('input',()=>{clearTimeout(saveTimer);saveTimer=setTimeout(capture,120)});document.addEventListener('change',capture);function toast(v){let t=$('#toast');t.textContent=v;t.classList.add('show');setTimeout(()=>t.classList.remove('show'),1800)}
$('#presenter').onclick=()=>{document.body.classList.toggle('presenter');$('#presenter').textContent=document.body.classList.contains('presenter')?'Exit presenter mode':'Presenter mode'};$$('[data-copy]').forEach(b=>b.onclick=async()=>{await navigator.clipboard.writeText(b.dataset.copy);toast('Command copied')});
$('#export').onclick=()=>{capture();let blob=new Blob([JSON.stringify(state,null,2)+'\n'],{type:'application/json'}),a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=`${WORKBOOK.workbook_id}-review-${new Date().toISOString().replace(/[:.]/g,'-')}.json`;a.click();URL.revokeObjectURL(a.href);toast('Review packet exported')};
$('#import').onclick=()=>$('#review-file').click();$('#review-file').onchange=async e=>{try{state=normalize(JSON.parse(await e.target.files[0].text()));localStorage.setItem(key,JSON.stringify(state));apply();toast('Review packet imported')}catch(err){toast(err.message||'Review import refused')}finally{e.target.value=''}};
$('#reset').onclick=()=>{if(!confirm('Reset this browser\'s workbook notes and checks? Export first if they should travel.'))return;localStorage.removeItem(key);state=blank();apply();toast('Local workbook reset')};apply();</script></body></html>'''
    replacements = {
        "__TITLE__": html.escape(manifest["title"]),
        "__SUBTITLE__": html.escape(manifest["subtitle"]),
        "__COMPOSITION__": composition,
        "__METRICS__": metrics,
        "__PROOF_LEVEL__": html.escape(manifest["proof_boundary"]["proof_level"]),
        "__ACCEPTED__": accepted,
        "__NOT_CLAIMED__": not_claimed,
        "__STAGES__": stages,
        "__SHOTS__": shots,
        "__ACTIONS__": actions,
        "__VERDICTS__": verdict_options,
        "__ATTACKS__": attack_options,
        "__MAX_OBSERVATIONS__": str(manifest["review"]["max_observation_chars"]),
        "__MAX_CANT_ANSWER__": str(manifest["review"]["max_cant_answer_chars"]),
        "__COMMANDS__": commands,
        "__LIMITATIONS__": limitations,
        "__MANIFEST_HASH__": manifest_hash,
        "__REVISION__": str(manifest["revision"]),
        "__EMBEDDED__": embedded,
    }
    for marker, value in replacements.items():
        page = page.replace(marker, value)
    if "__" in page:
        unresolved = sorted(set(re.findall(r"__[A-Z_]+__", page)))
        if unresolved:
            raise WorkbookError(f"unresolved render markers: {unresolved}")
    return page + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    try:
        manifest, manifest_hash = load_manifest()
        rendered = render(manifest, manifest_hash)
        if args.check:
            if not OUTPUT.is_file() or OUTPUT.read_text(encoding="utf-8") != rendered:
                raise WorkbookError(f"{OUTPUT.relative_to(REPO)} is stale; rerun this renderer")
            print(f"OK: {OUTPUT.relative_to(REPO)}")
            return 0
        OUTPUT.write_text(rendered, encoding="utf-8", newline="\n")
        print(f"Rendered {OUTPUT.relative_to(REPO)}")
        return 0
    except WorkbookError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

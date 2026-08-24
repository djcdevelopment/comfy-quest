#!/usr/bin/env python3
"""Render the offline Quest Mission Control page from tracked status and proven sources."""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any


REPO = Path(__file__).resolve().parents[1]
SOURCE = REPO / "docs" / "quest-mission-control.json"
OUTPUT = REPO / "docs" / "quest-mission-control.html"
SCHEMA = "comfy-quest-mission-control/v1"
SESSION_SCHEMA = "comfy-quest-mission-control-session/v1"
ALLOWED_PHASE_STATES = {"complete", "active", "planned"}
ALLOWED_QUEUE_STATES = {"ready", "human", "gated"}


class MissionControlError(RuntimeError):
    """A bounded status or source-integrity failure."""


def read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as exc:
        raise MissionControlError(f"cannot read {path.relative_to(REPO)}: {exc}") from exc


def load_manifest(path: Path = SOURCE) -> dict[str, Any]:
    try:
        value = json.loads(read_text(path))
    except json.JSONDecodeError as exc:
        raise MissionControlError(f"invalid mission-control JSON: {exc}") from exc
    validate_manifest(value)
    return value


def require_text(value: Any, where: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise MissionControlError(f"{where} must be non-empty text")
    return value


def require_list(value: Any, where: str) -> list[Any]:
    if not isinstance(value, list) or not value:
        raise MissionControlError(f"{where} must be a non-empty list")
    return value


def source_path(relative: str) -> Path:
    candidate = (REPO / require_text(relative, "source path")).resolve()
    try:
        candidate.relative_to(REPO.resolve())
    except ValueError as exc:
        raise MissionControlError(f"source escapes repository: {relative}") from exc
    if not candidate.is_file():
        raise MissionControlError(f"source does not exist: {relative}")
    return candidate


def validate_source_pin(item: dict[str, Any], where: str) -> None:
    path = source_path(require_text(item.get("source"), f"{where}.source"))
    marker = require_text(item.get("source_contains"), f"{where}.source_contains")
    if marker not in read_text(path):
        raise MissionControlError(f"{where} source pin is stale: {marker!r} not found in {path.relative_to(REPO)}")


def validate_manifest(value: Any) -> None:
    if not isinstance(value, dict) or value.get("schema") != SCHEMA:
        raise MissionControlError(f"mission-control schema must be {SCHEMA}")
    page = value.get("page")
    if not isinstance(page, dict):
        raise MissionControlError("page must be an object")
    for key in ("id", "title", "subtitle", "observed_on", "program_commit", "program_commit_label"):
        require_text(page.get(key), f"page.{key}")
    if not re.fullmatch(r"[a-z0-9][a-z0-9.-]{1,79}", page["id"]):
        raise MissionControlError(f"unsafe page id: {page['id']}")

    ids: set[str] = set()

    def take_id(item: dict[str, Any], where: str) -> None:
        item_id = require_text(item.get("id"), f"{where}.id")
        if not re.fullmatch(r"[a-z0-9][a-z0-9.-]{1,79}", item_id):
            raise MissionControlError(f"unsafe stable id at {where}: {item_id}")
        if item_id in ids:
            raise MissionControlError(f"duplicate stable id: {item_id}")
        ids.add(item_id)

    for group in ("machines", "environment", "phases", "queue", "decisions", "commands"):
        for index, item in enumerate(require_list(value.get(group), group)):
            if not isinstance(item, dict):
                raise MissionControlError(f"{group}[{index}] must be an object")
            take_id(item, f"{group}[{index}]")

    phases = value["phases"]
    if [item.get("number") for item in phases] != [1, 2, 3, 4, 5]:
        raise MissionControlError("phases must contain the ordered five-phase program")
    for index, phase in enumerate(phases):
        if phase.get("state") not in ALLOWED_PHASE_STATES:
            raise MissionControlError(f"invalid phase state at phases[{index}]")
        validate_source_pin(phase, f"phases[{index}]")

    for index, item in enumerate(value["queue"]):
        if item.get("state") not in ALLOWED_QUEUE_STATES:
            raise MissionControlError(f"invalid queue state at queue[{index}]")
        validate_source_pin(item, f"queue[{index}]")
    for index, item in enumerate(value["decisions"]):
        validate_source_pin(item, f"decisions[{index}]")

    recovery = value.get("recovery")
    if not isinstance(recovery, dict):
        raise MissionControlError("recovery must be an object")
    take_id(recovery, "recovery")
    cold = recovery.get("cold_load")
    if not isinstance(cold, dict):
        raise MissionControlError("recovery.cold_load must be an object")
    take_id(cold, "recovery.cold_load")
    validate_source_pin(cold, "recovery.cold_load")
    creator = recovery.get("creator_flow")
    if not isinstance(creator, dict):
        raise MissionControlError("recovery.creator_flow must be an object")
    source_path(require_text(creator.get("source"), "recovery.creator_flow.source"))
    require_text(creator.get("heading"), "recovery.creator_flow.heading")
    revision = recovery.get("revision_flow")
    if not isinstance(revision, dict):
        raise MissionControlError("recovery.revision_flow must be an object")
    take_id(revision, "recovery.revision_flow")
    validate_source_pin(revision, "recovery.revision_flow")
    require_text(revision.get("source_sequence"), "recovery.revision_flow.source_sequence")
    edit_path = source_path(require_text(revision.get("edit_source"), "recovery.revision_flow.edit_source"))
    edit_marker = require_text(revision.get("edit_source_contains"), "recovery.revision_flow.edit_source_contains")
    if edit_marker not in read_text(edit_path):
        raise MissionControlError("recovery.revision_flow edit source pin is stale")
    require_text(revision.get("suggested_edit"), "recovery.revision_flow.suggested_edit")
    source_path(require_text(recovery.get("expectations"), "recovery.expectations"))

    phase3 = value.get("phase3_lap")
    if not isinstance(phase3, dict):
        raise MissionControlError("phase3_lap must be an object")
    source_path(require_text(phase3.get("source"), "phase3_lap.source"))
    require_text(phase3.get("sequence_heading"), "phase3_lap.sequence_heading")
    require_text(phase3.get("verdicts_heading"), "phase3_lap.verdicts_heading")

    for key in ("machines", "environment"):
        for index, item in enumerate(value[key]):
            require_text(item.get("label"), f"{key}[{index}].label")
            require_text(item.get("state"), f"{key}[{index}].state")
            require_text(item.get("detail"), f"{key}[{index}].detail")
            require_text(item.get("fact_kind"), f"{key}[{index}].fact_kind")

    for index, command in enumerate(value["commands"]):
        require_text(command.get("command"), f"commands[{index}].command")
    require_list(value.get("cautions"), "cautions")


def markdown_section(path: Path, heading: str) -> list[str]:
    lines = read_text(path).splitlines()
    wanted = re.compile(rf"^##\s+{re.escape(heading)}\s*$", re.IGNORECASE)
    start = next((index + 1 for index, line in enumerate(lines) if wanted.match(line.strip())), None)
    if start is None:
        raise MissionControlError(f"heading {heading!r} not found in {path.relative_to(REPO)}")
    end = next((index for index in range(start, len(lines)) if lines[index].startswith("## ")), len(lines))
    return lines[start:end]


def list_items(lines: list[str], ordered: bool) -> list[str]:
    marker = re.compile(r"^\s*\d+\.\s+(.*)$" if ordered else r"^\s*-\s+(.*)$")
    items: list[str] = []
    current: list[str] = []
    for line in lines:
        match = marker.match(line)
        if match:
            if current:
                items.append(" ".join(current).strip())
            current = [match.group(1).strip()]
            continue
        if current:
            stripped = line.strip()
            if not stripped:
                items.append(" ".join(current).strip())
                current = []
            elif not line.startswith((" ", "\t")):
                items.append(" ".join(current).strip())
                current = []
            else:
                current.append(stripped)
    if current:
        items.append(" ".join(current).strip())
    if not items:
        kind = "ordered" if ordered else "unordered"
        raise MissionControlError(f"no {kind} items found in selected Markdown section")
    return items


def inline_markdown(value: str) -> str:
    code_tokens: list[str] = []

    def protect_code(match: re.Match[str]) -> str:
        token = f"QMCODETOKEN{len(code_tokens)}Q"
        code_tokens.append(f"<code>{html.escape(match.group(1).strip(), quote=True)}</code>")
        return token

    protected = re.sub(r"``\s*(.*?)\s*``", protect_code, value)
    protected = re.sub(r"`([^`]+)`", protect_code, protected)
    escaped = html.escape(protected, quote=True)
    escaped = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", escaped)
    escaped = re.sub(r"\*([^*]+?)\*", r"<em>\1</em>", escaped)
    for index, token in enumerate(code_tokens):
        escaped = escaped.replace(f"QMCODETOKEN{index}Q", token)
    return escaped


def href_for(relative: str) -> str:
    target = source_path(relative)
    return Path(os.path.relpath(target, OUTPUT.parent)).as_posix()


def source_link(relative: str, label: str = "Source") -> str:
    return f'<a class="source-link" href="{html.escape(href_for(relative), quote=True)}">{html.escape(label)}</a>'


def badge(state: str) -> str:
    labels = {
        "complete": "Complete",
        "active": "Active",
        "planned": "Planned",
        "ready": "Ready",
        "human": "Needs Derek",
        "gated": "Gated",
        "online": "Online",
        "on-demand": "On demand",
        "confirmed": "Confirmed",
        "attention": "Attention",
    }
    return f'<span class="badge badge-{html.escape(state)}">{html.escape(labels.get(state, state.title()))}</span>'


def checklist_item(item_id: str, body: str, *, source: str | None = None, source_label: str = "Source") -> str:
    link = f" {source_link(source, source_label)}" if source else ""
    safe_id = html.escape(item_id, quote=True)
    return (
        f'<li class="check-item" data-check-item="{safe_id}">'
        f'<input type="checkbox" id="check-{safe_id}" data-check-id="{safe_id}">'
        f'<label for="check-{safe_id}"><span>{body}</span>{link}</label></li>'
    )


def evidence_receipts(expectations_path: Path) -> list[dict[str, Any]]:
    try:
        value = json.loads(read_text(expectations_path))
        first = value["imported_fork"]["first_revision_receipts"]
        completion = value["behavior"]["receipt_assertions"]
    except (KeyError, TypeError, json.JSONDecodeError) as exc:
        raise MissionControlError("Demo World expectation contract no longer exposes the pinned proof chain") from exc
    receipts = [*first, *completion]
    if not all(isinstance(item, dict) and item.get("operation") and item.get("status") for item in receipts):
        raise MissionControlError("Demo World proof chain contains an invalid receipt assertion")
    return receipts


def later_revision_expectation(expectations_path: Path) -> dict[str, Any]:
    try:
        value = json.loads(read_text(expectations_path))
        later = value["imported_fork"]["later_same_fork_revision"]
        receipt = later["changed_content_receipt"]
    except (KeyError, TypeError, json.JSONDecodeError) as exc:
        raise MissionControlError("Demo World contract no longer exposes later-revision rebind proof") from exc
    if later.get("pack_and_experience_ids_are_preserved") is not True:
        raise MissionControlError("Demo World later-revision identity preservation is no longer pinned")
    if not isinstance(receipt, dict) or not receipt.get("operation") or not receipt.get("status"):
        raise MissionControlError("Demo World later-revision rebind receipt is invalid")
    return later


def current_git_subject(commit: str) -> str:
    try:
        result = subprocess.run(
            ["git", "show", "-s", "--format=%s", commit],
            cwd=REPO,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
    except (OSError, subprocess.CalledProcessError) as exc:
        raise MissionControlError(f"program commit is not available: {commit}") from exc
    return result.stdout.strip()


def assert_repo_identity() -> None:
    shell = shutil.which("pwsh") or shutil.which("powershell")
    if shell is None:
        raise MissionControlError("PowerShell is required to run tools/Assert-RepoIdentity.ps1 before writing")
    try:
        subprocess.run(
            [shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(REPO / "tools" / "Assert-RepoIdentity.ps1")],
            cwd=REPO,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
    except subprocess.CalledProcessError as exc:
        detail = (exc.stderr or exc.stdout or "repository identity check failed").strip()
        raise MissionControlError(detail) from exc


def render(manifest: dict[str, Any]) -> str:
    page = manifest["page"]
    subject = current_git_subject(page["program_commit"])
    if subject != page["program_commit_label"]:
        raise MissionControlError("program commit label does not match Git history")

    creator = manifest["recovery"]["creator_flow"]
    creator_steps = list_items(markdown_section(source_path(creator["source"]), creator["heading"]), ordered=True)
    revision = manifest["recovery"]["revision_flow"]
    revision_steps = [item.strip() for item in revision["source_sequence"].split(" -> ") if item.strip()]
    phase3 = manifest["phase3_lap"]
    phase3_source = source_path(phase3["source"])
    phase3_steps = list_items(markdown_section(phase3_source, phase3["sequence_heading"]), ordered=True)
    phase3_verdicts = list_items(markdown_section(phase3_source, phase3["verdicts_heading"]), ordered=False)
    if len(creator_steps) != 6:
        raise MissionControlError(f"Demo World public creator loop must remain six derived steps, found {len(creator_steps)}")
    if len(revision_steps) != 6:
        raise MissionControlError("Studio normal lap must remain a six-stage source sequence")
    if len(phase3_steps) != 5 or len(phase3_verdicts) != 3:
        raise MissionControlError("Phase 3 runbook must expose five sequence steps and exactly three seat verdicts")

    expectations_relative = manifest["recovery"]["expectations"]
    expectations_path = source_path(expectations_relative)
    receipts = evidence_receipts(expectations_path)
    later_revision = later_revision_expectation(expectations_path)
    source_hash = hashlib.sha256(SOURCE.read_bytes()).hexdigest()[:12]

    machine_cards = "".join(
        f'''<article class="machine-card">
          <div class="card-heading"><div><span class="eyebrow">{html.escape(item["fact_kind"])}</span><h3>{html.escape(item["label"])}</h3></div>{badge(item["state"])}</div>
          <strong>{html.escape(item["role"])}</strong><p>{html.escape(item["detail"])}</p>
          <label class="session-confirm"><input type="checkbox" data-check-id="machine.{html.escape(item["id"], quote=True)}"> Confirmed this sitting</label>
        </article>'''
        for item in manifest["machines"]
    )

    environment_cards = "".join(
        f'''<li><span class="status-dot status-{html.escape(item["state"])}" aria-hidden="true"></span><div><strong>{html.escape(item["label"])}</strong><p>{html.escape(item["detail"])}</p><span class="fact-kind">{html.escape(item["fact_kind"])}</span></div></li>'''
        for item in manifest["environment"]
    )

    phases = "".join(
        f'''<li class="phase phase-{html.escape(item["state"])}">
          <div class="phase-rail"><span>{item["number"]}</span></div>
          <div class="phase-copy"><div class="card-heading"><h3>{html.escape(item["title"])}</h3>{badge(item["state"])}</div><p>{html.escape(item["summary"])}</p>{source_link(item["source"])}</div>
        </li>'''
        for item in manifest["phases"]
    )

    cold = manifest["recovery"]["cold_load"]
    cold_item = checklist_item(
        cold["id"],
        f'<strong>{html.escape(cold["title"])}</strong><small>{html.escape(cold["instruction"])}</small>',
        source=cold["source"],
        source_label="Handoff evidence",
    )
    creator_items = "".join(
        checklist_item(
            f"recovery.creator.{index}",
            f'<strong>Step {index}</strong><small>{inline_markdown(step)}</small>',
            source=creator["source"] if index == 1 else None,
            source_label="Derived sequence",
        )
        for index, step in enumerate(creator_steps, 1)
    )
    revision_items = "".join(
        checklist_item(
            f"recovery.revision.{index}",
            f'<strong>{html.escape(step)}</strong><small>Stage {index} of the Studio normal lap.</small>',
            source=revision["source"] if index == 1 else None,
            source_label="Derived sequence",
        )
        for index, step in enumerate(revision_steps, 1)
    )
    changed_receipt = later_revision["changed_content_receipt"]
    revision_proof = (
        '<div class="revision-proof"><span><strong>Identity</strong> Pack and experience IDs preserved</span>'
        f'<span><strong>Changed content</strong> <code>{html.escape(changed_receipt["operation"])}</code> · {html.escape(changed_receipt["status"])}</span></div>'
    )

    proof_rows = "".join(
        f'''<li><code>{html.escape(item["operation"])}</code><span>{html.escape(item["status"])}</span>{f'<small>{html.escape(item.get("error", ""))}</small>' if item.get("error") else ""}</li>'''
        for item in receipts
    )

    queue_cards = "".join(
        f'''<article class="queue-card"><div class="card-heading"><h3>{html.escape(item["title"])}</h3>{badge(item["state"])}</div><p>{html.escape(item["detail"])}</p>{source_link(item["source"])}</article>'''
        for item in manifest["queue"]
    )

    decision_cards = "".join(
        f'''<article class="decision-card"><span class="eyebrow">Open decision</span><h3>{html.escape(item["title"])}</h3><p>{html.escape(item["question"])}</p><small>{html.escape(item["recommendation"])}</small>{source_link(item["source"])}</article>'''
        for item in manifest["decisions"]
    )

    command_cards = "".join(
        f'''<article class="command-card"><div><strong>{html.escape(item["label"])}</strong><small>{html.escape(item["detail"])}</small></div><div class="command-row"><code>{html.escape(item["command"])}</code><button type="button" class="copy-button" data-copy="{html.escape(item["command"], quote=True)}">Copy</button></div></article>'''
        for item in manifest["commands"]
    )

    caution_items = "".join(f"<li>{html.escape(item)}</li>" for item in manifest["cautions"])
    phase3_sequence = "".join(
        f'<li><span>{index}</span><p>{inline_markdown(step)}</p></li>' for index, step in enumerate(phase3_steps, 1)
    )
    phase3_judgments = "".join(f"<li>{inline_markdown(item)}</li>" for item in phase3_verdicts)

    css = r'''
:root{color-scheme:dark;--ink:#f5f1e8;--muted:#aaa99f;--dim:#74766f;--panel:#111713;--panel-2:#18201b;--panel-3:#202b24;--line:#344139;--gold:#e9a83f;--gold-soft:#ffd98b;--green:#65d68b;--violet:#c9a7ff;--red:#ff8b7d;--blue:#7fc8ff;--shadow:0 18px 50px rgba(0,0,0,.34);--radius:18px}
*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:radial-gradient(circle at 16% 0,rgba(233,168,63,.11),transparent 30rem),radial-gradient(circle at 90% 18%,rgba(100,214,139,.07),transparent 34rem),#090d0b;color:var(--ink);font-family:Inter,ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif;line-height:1.5}.skip-link{position:fixed;top:8px;left:8px;z-index:20;transform:translateY(-150%);padding:10px 14px;background:var(--gold);color:#151008;border-radius:8px}.skip-link:focus{transform:none}a{color:var(--gold-soft);text-underline-offset:3px}button,input,select,textarea{font:inherit}.shell{width:min(1540px,calc(100% - 40px));margin:auto}.masthead{padding:34px 0 22px;border-bottom:1px solid rgba(255,255,255,.07);background:rgba(9,13,11,.84);backdrop-filter:blur(18px);position:sticky;top:0;z-index:10}.masthead-row{display:flex;align-items:flex-end;justify-content:space-between;gap:30px}.brand{display:flex;align-items:center;gap:16px}.sigil{width:52px;height:52px;display:grid;place-items:center;border:1px solid rgba(233,168,63,.52);border-radius:15px;color:var(--gold);font:700 28px Georgia,serif;box-shadow:inset 0 0 24px rgba(233,168,63,.08)}h1,h2,h3,p{margin-top:0}h1{font:650 clamp(1.55rem,2.3vw,2.25rem) Georgia,serif;margin-bottom:3px;letter-spacing:.01em}.subtitle{margin:0;color:var(--muted);font-size:.93rem}.snapshot{text-align:right}.snapshot strong{display:block;color:var(--green);font-size:.9rem}.snapshot small{color:var(--muted)}.topnav{display:flex;gap:18px;margin-top:18px;overflow:auto}.topnav a{font-size:.78rem;color:var(--muted);text-transform:uppercase;letter-spacing:.12em;text-decoration:none;white-space:nowrap}.topnav a:hover,.topnav a:focus-visible{color:var(--ink)}main{padding:36px 0 76px}.hero-grid{display:grid;grid-template-columns:minmax(0,1.55fr) minmax(330px,.75fr);gap:22px}.panel{background:linear-gradient(145deg,rgba(24,32,27,.97),rgba(14,20,16,.98));border:1px solid var(--line);border-radius:var(--radius);box-shadow:var(--shadow)}.now{padding:30px;position:relative;overflow:hidden}.now:after{content:"";position:absolute;right:-90px;top:-90px;width:250px;height:250px;border-radius:50%;border:1px solid rgba(233,168,63,.15);box-shadow:0 0 0 34px rgba(233,168,63,.035),0 0 0 68px rgba(233,168,63,.025);pointer-events:none}.eyebrow{display:block;color:var(--gold);font-size:.68rem;font-weight:750;letter-spacing:.16em;text-transform:uppercase;margin-bottom:7px}.now h2,.section-head h2{font:600 clamp(1.35rem,2vw,1.8rem) Georgia,serif;margin-bottom:8px}.lede{color:var(--muted);max-width:70ch}.next-callout{border-left:3px solid var(--gold);padding:13px 16px;margin:22px 0;background:rgba(233,168,63,.07);border-radius:0 10px 10px 0}.next-callout strong{display:block}.next-callout span{color:var(--muted);font-size:.9rem}.progress-line{display:flex;align-items:center;gap:13px;margin-top:20px}.progress-track{height:7px;flex:1;background:#080b09;border-radius:99px;overflow:hidden}.progress-fill{height:100%;width:0;background:linear-gradient(90deg,var(--gold),var(--green));transition:width .2s ease}.progress-copy{color:var(--muted);font-size:.78rem;min-width:94px;text-align:right}.session-panel{padding:24px}.session-panel h2{font-size:1rem;margin-bottom:6px}.session-panel p{font-size:.84rem;color:var(--muted)}textarea{width:100%;min-height:145px;resize:vertical;background:#090d0b;border:1px solid var(--line);border-radius:10px;color:var(--ink);padding:12px;margin:9px 0}textarea:focus,select:focus,button:focus-visible,input:focus-visible,a:focus-visible{outline:2px solid var(--gold);outline-offset:3px}.button-row{display:flex;flex-wrap:wrap;gap:8px}.button{border:1px solid var(--line);border-radius:9px;background:var(--panel-3);color:var(--ink);padding:8px 11px;cursor:pointer;font-size:.78rem}.button:hover{border-color:var(--gold)}.button-danger{color:var(--red)}#session-status{display:block;min-height:1.4em;margin-top:9px;color:var(--green);font-size:.76rem}.section{margin-top:42px}.section-head{display:flex;align-items:end;justify-content:space-between;gap:20px;margin-bottom:18px}.section-head p{margin:0;color:var(--muted);max-width:72ch}.recovery-grid{display:grid;grid-template-columns:minmax(0,1.45fr) minmax(300px,.65fr);gap:22px}.flow-panel{padding:24px}.flow-panel h3{margin-bottom:5px}.flow-panel>p{color:var(--muted);font-size:.9rem}.check-list{list-style:none;margin:20px 0 0;padding:0;display:grid;gap:10px}.check-item{display:grid;grid-template-columns:26px 1fr;gap:10px;padding:14px;border:1px solid var(--line);background:rgba(0,0,0,.13);border-radius:12px;transition:border-color .15s,opacity .15s}.check-item:has(input:checked){border-color:rgba(101,214,139,.42);opacity:.66}.check-item input,.session-confirm input{accent-color:var(--green);width:18px;height:18px;margin-top:3px}.check-item label{cursor:pointer}.check-item label>span{display:grid;gap:3px}.check-item small{color:var(--muted);line-height:1.45}.check-item:has(input:checked) label>span{text-decoration:line-through;text-decoration-color:rgba(101,214,139,.65)}.source-link{display:inline-block;margin-top:7px;font-size:.72rem;color:var(--gold-soft)}.checkpoint-c{margin-top:34px}.revision-proof{display:grid;grid-template-columns:repeat(2,1fr);gap:8px;margin-top:14px}.revision-proof span{padding:10px 12px;background:rgba(101,214,139,.05);border:1px solid rgba(101,214,139,.18);border-radius:9px;color:var(--muted);font-size:.78rem}.revision-proof strong{display:block;color:var(--green);font-size:.65rem;text-transform:uppercase;letter-spacing:.09em}.verdict-box{margin:14px 0 22px;padding:16px;border:1px solid rgba(201,167,255,.35);background:rgba(201,167,255,.06);border-radius:12px}.verdict-box label{display:block;font-weight:700;margin-bottom:9px}.verdict-box select{width:100%;background:#0b100d;color:var(--ink);border:1px solid var(--line);border-radius:9px;padding:9px}.proof-panel{padding:24px}.proof-panel h3{margin-bottom:4px}.proof-panel>p{color:var(--muted);font-size:.85rem}.proof-list{list-style:none;padding:0;margin:16px 0;display:grid;gap:7px}.proof-list li{display:grid;grid-template-columns:1fr auto;gap:10px;padding:9px 10px;background:#0b100d;border-radius:8px;border-left:2px solid var(--green)}.proof-list code{color:var(--ink)}.proof-list span{color:var(--green);font-size:.75rem;text-transform:uppercase;letter-spacing:.08em}.proof-list small{grid-column:1/-1;color:var(--red)}.guardrail{padding:13px;border-radius:10px;background:rgba(255,139,125,.07);border:1px solid rgba(255,139,125,.22);font-size:.83rem;color:#d8c7c0}.machine-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}.machine-card,.queue-card,.decision-card,.command-card{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:19px}.machine-card h3,.queue-card h3,.decision-card h3{margin:0;font-size:1rem}.machine-card>strong{font-size:.84rem}.machine-card p,.queue-card p,.decision-card p{color:var(--muted);font-size:.82rem;margin:7px 0}.session-confirm{display:flex;gap:8px;align-items:center;margin-top:15px;color:var(--muted);font-size:.76rem}.card-heading{display:flex;align-items:start;justify-content:space-between;gap:12px}.badge{display:inline-flex;align-items:center;border-radius:99px;padding:4px 8px;border:1px solid var(--line);font-size:.64rem;font-weight:750;text-transform:uppercase;letter-spacing:.08em;white-space:nowrap}.badge-complete,.badge-ready,.badge-confirmed,.badge-online{color:var(--green);border-color:rgba(101,214,139,.35);background:rgba(101,214,139,.07)}.badge-active,.badge-human,.badge-attention{color:var(--gold-soft);border-color:rgba(233,168,63,.4);background:rgba(233,168,63,.08)}.badge-planned,.badge-gated,.badge-on-demand{color:var(--muted)}.environment{margin:16px 0 0;padding:18px 22px}.environment h3{font-size:.83rem;color:var(--muted);text-transform:uppercase;letter-spacing:.11em}.environment ul{list-style:none;padding:0;margin:0;display:grid;grid-template-columns:repeat(2,1fr);gap:14px 24px}.environment li{display:grid;grid-template-columns:11px 1fr;gap:10px}.environment strong{font-size:.82rem}.environment p{font-size:.77rem;color:var(--muted);margin:2px 0}.fact-kind{font-size:.62rem;text-transform:uppercase;color:var(--dim);letter-spacing:.09em}.status-dot{width:9px;height:9px;margin-top:6px;border-radius:50%;background:var(--dim);box-shadow:0 0 0 3px rgba(255,255,255,.03)}.status-confirmed{background:var(--green)}.status-attention{background:var(--gold)}.phase-list{list-style:none;padding:0;margin:0;display:grid;grid-template-columns:repeat(5,1fr);gap:1px;background:var(--line);border:1px solid var(--line);border-radius:var(--radius);overflow:hidden}.phase{display:grid;grid-template-rows:auto 1fr;background:var(--panel);min-width:0}.phase-rail{height:46px;display:flex;align-items:center;padding:0 18px;border-bottom:1px solid var(--line);position:relative}.phase-rail:after{content:"";height:2px;background:var(--line);position:absolute;left:47px;right:0}.phase:last-child .phase-rail:after{display:none}.phase-rail span{width:26px;height:26px;display:grid;place-items:center;border-radius:50%;background:var(--panel-3);border:1px solid var(--line);font-size:.76rem;z-index:1}.phase-complete .phase-rail span{background:var(--green);color:#07120b;border-color:var(--green)}.phase-active .phase-rail span{background:var(--gold);color:#1a1204;border-color:var(--gold);box-shadow:0 0 22px rgba(233,168,63,.24)}.phase-copy{padding:17px}.phase-copy h3{font-size:.9rem;margin:0}.phase-copy p{color:var(--muted);font-size:.77rem;margin:9px 0}.queue-grid{display:grid;grid-template-columns:repeat(2,1fr);gap:14px}.decision-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:14px}.decision-card small{display:block;color:var(--gold-soft);margin:10px 0;font-size:.76rem}.command-list{display:grid;gap:10px}.command-card{display:grid;grid-template-columns:minmax(200px,.55fr) 1fr;gap:18px;align-items:center}.command-card small{display:block;color:var(--muted);margin-top:3px}.command-row{display:flex;min-width:0}.command-row code{flex:1;overflow:auto;padding:10px;background:#080b09;border:1px solid var(--line);border-radius:9px 0 0 9px;color:var(--blue);white-space:nowrap}.copy-button{border:1px solid var(--line);border-left:0;background:var(--panel-3);color:var(--ink);border-radius:0 9px 9px 0;padding:0 13px;cursor:pointer}.copy-button:hover{color:var(--gold)}details.future{border:1px solid var(--line);border-radius:14px;background:var(--panel)}details.future summary{cursor:pointer;padding:19px;font-weight:700}details.future>div{padding:0 20px 22px}.derived-sequence{list-style:none;padding:0;margin:15px 0;display:grid;gap:9px}.derived-sequence li{display:grid;grid-template-columns:28px 1fr;gap:9px}.derived-sequence li>span{width:25px;height:25px;display:grid;place-items:center;border:1px solid var(--line);border-radius:50%;font-size:.7rem;color:var(--gold)}.derived-sequence p{font-size:.82rem;color:var(--muted);margin:1px 0}.judgment-list{color:var(--muted);font-size:.84rem}.cautions{display:grid;grid-template-columns:repeat(2,1fr);gap:10px;margin:0;padding:0;list-style:none}.cautions li{padding:14px 16px;border-left:2px solid var(--red);background:rgba(255,139,125,.05);color:#cfcbc3;font-size:.82rem}.footer{border-top:1px solid var(--line);padding:27px 0 45px;color:var(--dim);font-size:.76rem}.footer-row{display:flex;justify-content:space-between;gap:20px}.local-only{color:var(--violet)}code{font-family:"Cascadia Code","SFMono-Regular",Consolas,monospace}body.compact .section:not(#recovery),body.compact .machine-grid,body.compact .environment{display:none}@media(max-width:1100px){.hero-grid,.recovery-grid{grid-template-columns:1fr}.phase-list{grid-template-columns:1fr}.phase{grid-template-columns:50px 1fr;grid-template-rows:1fr}.phase-rail{height:100%;border:0;border-right:1px solid var(--line);padding:14px 11px}.phase-rail:after{width:2px;height:auto;top:45px;bottom:0;left:24px;right:auto}.machine-grid,.decision-grid{grid-template-columns:1fr}.queue-grid{grid-template-columns:1fr 1fr}}@media(max-width:720px){.shell{width:min(100% - 24px,1540px)}.masthead{position:static;padding-top:20px}.masthead-row,.section-head,.footer-row{align-items:start;flex-direction:column}.snapshot{text-align:left}.machine-grid,.queue-grid,.environment ul,.decision-grid,.cautions,.revision-proof{grid-template-columns:1fr}.command-card{grid-template-columns:1fr}.now,.session-panel,.flow-panel,.proof-panel{padding:19px}.topnav{padding-bottom:4px}}@media(prefers-reduced-motion:reduce){html{scroll-behavior:auto}.progress-fill{transition:none}}@media print{.masthead{position:static}.topnav,.session-panel,.button,.copy-button,.session-confirm{display:none!important}.panel,.machine-card,.queue-card,.decision-card{box-shadow:none;break-inside:avoid}body{background:#fff;color:#111}.subtitle,.lede,p,small,.phase-copy p{color:#444!important}.shell{width:100%}}
'''

    js = f'''
(() => {{
  'use strict';
  const schema={json.dumps(SESSION_SCHEMA)};
  const pageId={json.dumps(page["id"])};
  const storageKey=`quest-mission-control.${{pageId}}.v1`;
  const status=document.querySelector('#session-status');
  const checks=[...document.querySelectorAll('[data-check-id]')];
  const knownIds=new Set(checks.map(item=>item.dataset.checkId));
  const notes=document.querySelector('#session-notes');
  const verdict=document.querySelector('#cold-verdict');
  let state={{schema,page_id:pageId,saved_at:null,checks:{{}},notes:'',visibility_verdict:'unrecorded'}};
  let saveTimer=0;

  function announce(message,bad=false){{status.textContent=message;status.style.color=bad?'var(--red)':'var(--green)'}}
  function bounded(value,limit){{return typeof value==='string'?value.slice(0,limit):''}}
  function normalize(input){{
    if(!input||input.schema!==schema||input.page_id!==pageId)throw new Error('This session file belongs to another page or schema.');
    const next={{schema,page_id:pageId,saved_at:bounded(input.saved_at,64)||null,checks:{{}},notes:bounded(input.notes,20000),visibility_verdict:['unrecorded','obvious','not-obvious','mixed'].includes(input.visibility_verdict)?input.visibility_verdict:'unrecorded'}};
    if(input.checks&&typeof input.checks==='object')for(const [key,value] of Object.entries(input.checks))if(knownIds.has(key)&&value===true)next.checks[key]=true;
    return next;
  }}
  function apply(){{
    checks.forEach(item=>item.checked=state.checks[item.dataset.checkId]===true);
    notes.value=state.notes;
    verdict.value=state.visibility_verdict;
    updateProgress();
  }}
  function updateProgress(){{
    const lane=checks.filter(item=>item.dataset.checkId==='recovery.cold-load'||item.dataset.checkId.startsWith('recovery.creator.')||item.dataset.checkId.startsWith('recovery.revision.'));
    const done=lane.filter(item=>item.checked).length;
    const percent=lane.length?Math.round(done/lane.length*100):0;
    document.querySelector('#lane-progress').style.width=`${{percent}}%`;
    const bar=document.querySelector('#lane-progressbar');
    bar.setAttribute('aria-valuenow',String(percent));
    document.querySelector('#progress-copy').textContent=`${{done}} of ${{lane.length}} checked`;
  }}
  function persist(message='Saved in this browser'){{
    clearTimeout(saveTimer);
    saveTimer=setTimeout(()=>{{
      state.saved_at=new Date().toISOString();
      try{{localStorage.setItem(storageKey,JSON.stringify(state));announce(message)}}catch(error){{announce('Browser storage is unavailable; export the session to retain it.',true)}}
    }},100);
  }}
  function load(){{
    try{{const raw=localStorage.getItem(storageKey);if(raw)state=normalize(JSON.parse(raw))}}catch(error){{announce('Saved browser state could not be read; starting clean.',true)}}
    apply();
  }}
  checks.forEach(item=>item.addEventListener('change',()=>{{if(item.checked)state.checks[item.dataset.checkId]=true;else delete state.checks[item.dataset.checkId];updateProgress();persist()}}));
  notes.addEventListener('input',()=>{{state.notes=bounded(notes.value,20000);persist()}});
  verdict.addEventListener('change',()=>{{state.visibility_verdict=verdict.value;persist('Verdict saved in this browser')}});

  document.querySelector('#export-session').addEventListener('click',()=>{{
    state.saved_at=new Date().toISOString();
    const blob=new Blob([JSON.stringify(state,null,2)+'\\n'],{{type:'application/json'}});
    const link=document.createElement('a');link.href=URL.createObjectURL(blob);link.download=`quest-session-${{new Date().toISOString().slice(0,10)}}.json`;document.body.appendChild(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(link.href),0);announce('Session JSON exported');
  }});
  document.querySelector('#import-session').addEventListener('click',()=>document.querySelector('#session-file').click());
  document.querySelector('#session-file').addEventListener('change',async event=>{{
    const file=event.target.files&&event.target.files[0];event.target.value='';if(!file)return;
    if(file.size>262144){{announce('Session file is too large.',true);return}}
    try{{state=normalize(JSON.parse(await file.text()));apply();persist('Session imported and saved locally')}}catch(error){{announce(error.message||'Session import failed.',true)}}
  }});
  document.querySelector('#reset-session').addEventListener('click',()=>{{
    if(!confirm("Clear this browser's Quest Mission Control checkmarks, verdict, and notes?"))return;
    state={{schema,page_id:pageId,saved_at:null,checks:{{}},notes:'',visibility_verdict:'unrecorded'}};try{{localStorage.removeItem(storageKey)}}catch(error){{}}apply();announce('Local session cleared');
  }});
  document.querySelector('#focus-current').addEventListener('click',event=>{{document.body.classList.toggle('compact');event.currentTarget.textContent=document.body.classList.contains('compact')?'Show full program':'Focus current lane'}});

  async function copyText(value){{
    if(navigator.clipboard&&window.isSecureContext){{await navigator.clipboard.writeText(value);return}}
    const area=document.createElement('textarea');area.value=value;area.style.position='fixed';area.style.opacity='0';document.body.appendChild(area);area.select();const ok=document.execCommand('copy');area.remove();if(!ok)throw new Error('copy unavailable');
  }}
  document.querySelectorAll('[data-copy]').forEach(button=>button.addEventListener('click',async()=>{{try{{await copyText(button.dataset.copy);button.textContent='Copied';announce('Command copied');setTimeout(()=>button.textContent='Copy',1200)}}catch(error){{announce('Copy is unavailable; select the command manually.',true)}}}}));
  load();
}})();
'''

    return f'''<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="dark">
<meta name="quest-mission-control-schema" content="{SCHEMA}">
<meta name="quest-mission-control-source" content="{source_hash}">
<title>{html.escape(page["title"])}</title>
<style>{css}</style>
</head>
<body>
<a class="skip-link" href="#main">Skip to mission control</a>
<header class="masthead"><div class="shell">
  <div class="masthead-row"><div class="brand"><div class="sigil" aria-hidden="true">Q</div><div><h1>{html.escape(page["title"])}</h1><p class="subtitle">{html.escape(page["subtitle"])}</p></div></div>
  <div class="snapshot"><strong>Program reconciled through {html.escape(page["program_commit"])}</strong><small>{html.escape(page["observed_on"])} · {html.escape(page["program_commit_label"])}</small></div></div>
  <nav class="topnav" aria-label="Mission control sections"><a href="#recovery">Resume here</a><a href="#machines">Machines</a><a href="#program">Program</a><a href="#queue">Queue</a><a href="#decisions">Decisions</a><a href="#commands">Commands</a></nav>
</div></header>
<main id="main" class="shell">
  <div class="hero-grid">
    <section class="panel now" aria-labelledby="now-title"><span class="eyebrow">Now · recovery acceptance</span><h2 id="now-title">Cold-load first. Create second.</h2><p class="lede">{html.escape(manifest["recovery"]["summary"])}</p>
      <div class="next-callout"><strong>One unanswered human judgment</strong><span>{html.escape(cold["verdict"])}</span></div>
      <div class="progress-line"><div id="lane-progressbar" class="progress-track" role="progressbar" aria-label="Recovery lane progress" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0"><div id="lane-progress" class="progress-fill"></div></div><span id="progress-copy" class="progress-copy">0 checked</span></div>
    </section>
    <aside class="panel session-panel" aria-labelledby="session-title"><span class="eyebrow local-only">Private to this browser</span><h2 id="session-title">Session notebook</h2><p>Checkmarks, the cold-load verdict, and notes stay in localStorage. Export JSON when the observation should travel.</p><label for="session-notes" class="eyebrow">Notes</label><textarea id="session-notes" maxlength="20000" placeholder="Record exact reactions, blockers, and anything that produces ‘can’t answer why’." spellcheck="true"></textarea><div class="button-row"><button id="export-session" class="button" type="button">Export</button><button id="import-session" class="button" type="button">Import</button><button id="reset-session" class="button button-danger" type="button">Reset</button><button id="focus-current" class="button" type="button">Focus current lane</button><input id="session-file" type="file" accept="application/json,.json" hidden></div><span id="session-status" role="status" aria-live="polite"></span></aside>
  </div>

  <section id="recovery" class="section" aria-labelledby="recovery-title"><div class="section-head"><div><span class="eyebrow">Active lane</span><h2 id="recovery-title">{html.escape(manifest["recovery"]["title"])}</h2></div><p>Canonical facts are Git-owned. These checkmarks only help this sitting keep its place.</p></div>
    <div class="recovery-grid"><article class="panel flow-panel"><span class="eyebrow">Checkpoint A · immutable world judgment</span><h3>Keep the canonical build unchanged</h3><ul class="check-list">{cold_item}</ul>
      <div class="verdict-box"><label for="cold-verdict">{html.escape(cold["verdict"])}</label><select id="cold-verdict"><option value="unrecorded">Not recorded</option><option value="obvious">Yes · immediately obvious</option><option value="not-obvious">No · not immediately obvious</option><option value="mixed">Mixed · explain in notes</option></select></div>
      <span class="eyebrow">Checkpoint B · source-derived creator loop</span><h3>{html.escape(creator["title"])}</h3><p>{html.escape(creator["summary"])}</p><div class="guardrail"><strong>Precondition:</strong> begin the imported-fork path where its source says it begins. If recovery leaves the character elsewhere, record that state; do not invent a reset or silently skip the ascent beat.</div><ol class="check-list">{creator_items}</ol>
      <span class="eyebrow checkpoint-c">Checkpoint C · same fork, changed content</span><h3>{html.escape(revision["title"])}</h3><p>{html.escape(revision["summary"])}</p><div class="next-callout"><strong>Bounded suggested edit</strong><span>{html.escape(revision["suggested_edit"])}</span>{source_link(revision["edit_source"], "Project source")}</div><ol class="check-list">{revision_items}</ol>{revision_proof}
    </article>
    <aside class="panel proof-panel"><span class="eyebrow">Acceptance evidence</span><h3>The product’s expected proof chain</h3><p>These receipt assertions come directly from the checked-in Demo World contract. Run-specific identities are deliberately not pinned.</p><ol class="proof-list">{proof_rows}</ol>{source_link(expectations_relative, "Expectation contract")}<p class="guardrail">A checked box is not proof. Use Runtime receipts and the Studio cockpit for machine facts; preserve Derek’s exact words for the human judgment.</p></aside></div>
  </section>

  <section id="machines" class="section" aria-labelledby="machines-title"><div class="section-head"><div><span class="eyebrow">Lab topology</span><h2 id="machines-title">Machines and readiness</h2></div><p>Reported availability is separated from repository-owned role claims.</p></div><div class="machine-grid">{machine_cards}</div><article class="panel environment"><h3>Observed on {html.escape(page["observed_on"])}</h3><ul>{environment_cards}</ul></article></section>

  <section id="program" class="section" aria-labelledby="program-title"><div class="section-head"><div><span class="eyebrow">Five-intent program</span><h2 id="program-title">Two complete. One at the exit. Two planned.</h2></div><p>Phase state is a cited program snapshot, not a live inference from checkboxes.</p></div><ol class="phase-list">{phases}</ol></section>

  <section id="queue" class="section" aria-labelledby="queue-title"><div class="section-head"><div><span class="eyebrow">After recovery</span><h2 id="queue-title">Work queue</h2></div><p>The top bar and warning expiry are build-ready. Phase 3 remains the program gate.</p></div><div class="queue-grid">{queue_cards}</div>
    <details class="future"><summary>Preview the derived Phase 3 exit lap</summary><div><p>{html.escape(phase3["summary"])}</p><ol class="derived-sequence">{phase3_sequence}</ol><h3>Exactly three human verdicts</h3><ul class="judgment-list">{phase3_judgments}</ul>{source_link(phase3["source"], "Derived runbook")}</div></details>
  </section>

  <section id="decisions" class="section" aria-labelledby="decisions-title"><div class="section-head"><div><span class="eyebrow">Not yet decided</span><h2 id="decisions-title">Phase 4 calls</h2></div><p>The top bar itself is already authorized; these choices shape the phase around it.</p></div><div class="decision-grid">{decision_cards}</div></section>

  <section id="commands" class="section" aria-labelledby="commands-title"><div class="section-head"><div><span class="eyebrow">Safe launch points</span><h2 id="commands-title">Commands</h2></div><p>Copy only. This page cannot arm Runtime, mutate Valheim, or execute repository tools.</p></div><div class="command-list">{command_cards}</div><p><a href="http://127.0.0.1:8085/quest-studio">Open Quest Studio on this machine</a> · <a href="../README.md">Repository README</a> · <a href="handoff-2026-08-20.md">Last handoff</a></p></section>

  <section class="section" aria-labelledby="cautions-title"><div class="section-head"><div><span class="eyebrow">Do not relearn these</span><h2 id="cautions-title">Guardrails</h2></div></div><ul class="cautions">{caution_items}</ul></section>
</main>
<footer class="footer"><div class="shell footer-row"><span>Generated from tracked status and cited repository sources · manifest {source_hash}</span><span>Local session schema: {SESSION_SCHEMA}</span></div></footer>
<script>{js}</script>
</body></html>'''


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="fail if the committed HTML is stale")
    parser.add_argument("--out", type=Path, default=OUTPUT, help="render to another path")
    args = parser.parse_args(argv)
    try:
        rendered = render(load_manifest())
        output = args.out.resolve()
        if args.check:
            if not output.is_file() or output.read_text(encoding="utf-8") != rendered:
                print(f"STALE: {output}; run {Path(__file__).name}", file=sys.stderr)
                return 1
            print(f"OK: {output.relative_to(REPO) if output.is_relative_to(REPO) else output}")
            return 0
        assert_repo_identity()
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(rendered, encoding="utf-8", newline="\n")
        print(f"Wrote {output.relative_to(REPO) if output.is_relative_to(REPO) else output}")
        return 0
    except MissionControlError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())

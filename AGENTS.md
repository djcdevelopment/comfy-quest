# Comfy Quest Repository Working Notes

## Operating rules and identity

This sovereign repository owns the Quest product: Lab, Runtime, Contracts, Studio,
creator tools, generators, and Quest release artifacts.

- No cross-repository reach-in. Integrate through published packages or verified
  release files, never a sibling checkout.
- Scripts derive locations from their own repository context. They do not assume a
  host checkout root.
- State-changing entrypoints run tools/Assert-RepoIdentity.ps1 before acting.
- The hosting Companion and Gateway belong to lumberjacks-platform. Client telemetry
  belongs to networksense. Architecture indexing belongs to baseline.

## Landing work

Go, push, land it, ship it, or merge it in authorizes commit, pull, and direct push
to main in one pass. Main is the R&D trunk. Stop only for force-push, history rewrite,
deleting work you did not create, or work outside this repository.

If main moves, pull with fast-forward only and retry. If push protection finds a
credential-shaped fixture, rewrite the fixture; never bypass protection.

## Verification

Before landing, run the build, xUnit, Python, generator-drift, identity, boundary, and
full-history secret-scan gates documented in README.md and CI.

Those gates cover artifacts. **Choreography is verified separately, and by execution.** A
human-facing sequence — a runbook, a workbook, a seat script, any ordered procedure someone
will follow at a keyboard — is not verified by reading it. Derive it from machine sources:
a Studio rehearsal run (which declares its own `proof_level`, `disclaimer` and per-run
`limitations`), the precondition chain in the code and the diagnostic each stage fails
with, and any existing runbook already proven in a lap. Then check that every step's
precondition is established by an earlier step in the same document. Three seat sessions
were burned on sequences that had never been executed; the code was never at fault.

Before writing prose that asserts a sequence, a precondition, a limitation, or a root
cause, consult the surface that already reports it. This product declares a great deal
about itself — rehearsal limitations, receipt evidence and rejected-branch traces,
`ContractDiagnostic` codes, the Lab's usability classifier, harness verdicts — and prose
that re-derives any of it is both wasted and unreliable.

Before committing, check whether the change alters a user-visible creator or Runtime
flow, program phase/status, open decision, machine role, or acceptance evidence. If it
does, update `docs/quest-mission-control.json` and regenerate the living HTML; unrelated
commits do not require a cosmetic edit. `python tools/render_quest_mission_control.py
--check` is part of the normal drift gate.

Every queue item in that manifest carries a `lane` and a `requirements` list, and every
`FR-`/`NFR-` identifier in `docs/creator-portfolio-requirements.md` carries a disposition in
`docs/creator-requirements-ledger.json`. The same drift gate enforces both, so **adding or
removing a requirement without a matching ledger entry fails CI** — that is deliberate, not
friction to route around. Lane vocabulary itself is defined once, in
`docs/creator-os-phases.json`; other documents reference a lane, they do not define one.

The rules above are the short form. `docs/working-agreements.md` carries the long form with the
incident behind each one — why choreography is verified by execution, why a check nobody has
watched fail is a claim, why agreement with your own workstation is not evidence, and what a
hold means. Read it once before your first substantial change; the rules are cheap and the
incidents that produced them were not.

<!-- hearth-offload:begin -->
## Local-first offload (HEARTH)

HEARTH is an always-on MCP door on loopback at `http://127.0.0.1:8710/mcp`. Before spending
metered frontier tokens on a self-contained sub-task, delegate it with
`mcp__hearth__local_generate`. Keep frontier reasoning for architecture, multi-file logic,
judgment, and anything needing whole-repo or whole-conversation context.

**Reach for `local_generate` — don't reason inline — when the sub-task is:**
- summarizing / condensing a file, log, or diff you have already read
- extracting structured data (fields, lists, JSON) from unstructured text
- generating boilerplate (config, test scaffold, docstring, commit-message draft)
- classifying / labeling / yes-no triage over a chunk of text
- drafting prose you will then edit (retro notes, PR-body first pass)

**The door routes itself — pin a rung only with cause** (`backend="name"`), preferred order:
- `gcp-gemini` — near-free frontier-class flash; the default target for self-contained work.
- `gcp-gemini-pro` — **pin-only**; the large-context reach flash cannot carry. Omit `max_tokens`
  and let the rung apply its own default.
- `omen-arc` — **the door default** and the sunk-cost local rung: resident, no cold-start tax,
  so spend freely on grunt work. Consumers keep using port 8082 unchanged.
- `omen-arc-oss` — banked fire, **pin-only**; it costs a model swap, so pin it with cause.
- `omen-swap` — the rotation rung, **pin-only** and always with `model=`. Port 8081 owns the
  model lifecycle: load and unload only through the door's rotation-window tools, **never** the
  bare llama-swap unload endpoint, which unloads production too.

**Rules:**
- Don't paste file contents — pass `files=[...]` and the door packs them scope-guarded.
  Repo-relative paths resolve against the primary root, and absolute paths anywhere under the
  HEARTH scope root reach other repos; only context from outside that root travels in the body.
- The offloaded model cannot run tools and cannot see your conversation — briefs stand alone.
- Trust the result metadata, not the model's self-report: check `ok` first, then read `text`;
  `backend` and `routed_by` are the proof of where the work actually ran.
- `ok:false` or unusable output → one retry at most, then do the task yourself; never loop on a
  cold worker. If the door itself is down, run `/checkmcp` once.
- For async, minutes-scale work (research briefs, simple builds) use `submit_task` (returns a
  `plan_id`; poll `task_status`). The brief must be self-contained.
- Never paste secrets — tokens, keys, credential file contents — into a prompt or `files=` pack.
- A `task_family=` label is expected after C-05 lands; until then, do not pass it.

<!-- synced by tools/ops/sync-offload-block.mjs from docs/agents/hearth-offload-block.md; edit the source, not this copy -->
<!-- hearth-offload:end -->

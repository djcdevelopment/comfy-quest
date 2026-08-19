# Quest Lab persona audit

An ownership map of every F6 Quest Lab capability against the design intents' persona
ladder, produced from a full source inventory (`Ui/LabPanel.cs`, `ComfyQuestLab.cs`,
`Core/*`). It recommends where each capability should eventually live; **it moves no
code**. The backlog rule stands: "Do not add a new wholesale palette before ownership and
observed need are clear." This document is the input to Phase 4 scoping.

The ladder, from intent 02: `Player → Observer → Apprentice → Remixer → Creator`, plus the
**Maintainer** the turnkey vision names as the binding constraint. Intent 02's framing:
Arcane Sight asks *"What is the runtime doing?"*; Quest Lab asks *"What am I experiencing,
how does the system understand it, and what useful ideas can I carry into my own
creations?"*

## The headline shape

Quest Lab is **~70% diagnostics/inspection, ~20% release-gate machinery, ~10% indirect
authoring, and 0% publishing — it contains no editor at all.** Authoring is exactly four
affordances: copy a `trigger.event` id, copy a generated schema-1 quest JSON, write a
draft scenario file, and open the quest folder for an external editor. The Lab never
reads or writes the shipping mod's directories.

## Persona map

| Persona | Lab surface today | Verdict |
| --- | --- | --- |
| **Player** | — | The Lab is not player-facing at all; nothing to protect here. |
| **Observer** | Console / "What just happened" tab: live event rows, school filter, BINDABLE/DIAGNOSTIC verdicts, pause/find | The strongest fit in the product — exactly intent 02's question, answered live. **Keep in Lab.** |
| **Apprentice** | Spellbook tab: eight-school curriculum ("TRY THIS" / "WORTH KNOWING"), world-actions grid | Genuinely good teaching prose, generated against the real catalog. **Keep in Lab**; rename (below). |
| **Remixer** | Clipboard copies of ids and generated JSON only | Thin: no identity, no attribution, no persistence. This is the gap Phase 4's notebook fills — the ladder's missing rung, not a Lab defect. |
| **Creator** | None — there is no editor | Studio owns authoring. The Lab should stop implying otherwise (empty states point at `lab_setup` and hand-edited JSON). |
| **Maintainer** | Ready? tab, Scenarios batch lifecycle, `questlab_seams`/`questlab_profile`, gallery/blueprint/prefabs/runelight commands, drift checks, request mailbox | Release-gate machinery co-resident with apprenticeship. Candidate for gating or extraction **in Phase 4, on observed need** — not now. |

## Seams to iron out (named, not yet acted on)

1. **Two mods, two vocabularies, one creator.** Lab and Runtime are separate mods with
   separate IMGUI stacks, two identically-named `Report()` methods (now on the same HUD
   channel), different hotkeys, and different state-word conventions
   (`[OK]/[INFO]/[CHECK]/[PROVED]` vs. READY/NOT READY). The creator crosses this boundary
   constantly; it is the mechanical root of "F9 felt like a kernel system-information
   panel" and of the Studio↔in-world switch cost.
2. **The "Spellbook" name is already occupied.** The Lab's Spellbook tab is a static
   school browser; Phase 4's spellbook is the *portable personal notebook* ("the personal
   accumulated craft"). Flagged now so Phase 4 renames one of them before they collide —
   recommendation: the notebook keeps the name **Spellbook** (that is what the intent
   means by it), and the Lab tab becomes the **Grimoire** it already renders.
3. **In-UI instructions point at a different surface.** `"F5 / questlab_batch export"`
   and `"Run lab_setup"` are dead ends inside a panel that cannot run them. Any capability
   the panel names, the panel should be able to do — or not name.
4. **Builder/set-dressing tools are a different product.** `questlab_blueprint`,
   `questlab_prefabs`, `questlab_runelight`, and the gallery machinery serve world
   construction, not quest craft. They are candidates for a separate identity when
   observed need forces the question.
5. **Maintainer jargon at creator altitude.** `fail_double_completion`, `schema-1`,
   `QuestAuthoring` (an internal class name in a tooltip), absolute file paths as UI,
   `questlab-v0.2.0-20260809-r24`, and raw Harmony hook failures all render on surfaces an
   apprentice reads. The Runtime side of this is fixed by the creator-loop baseline; the
   Lab side is a Phase 4 copy pass once ownership is decided.

## Cross-intent findings

The audit turned out to be load-bearing for three of the other four intents.

1. **There are two Arcane Sights.** `LabArcaneSight` (lamp `comfy-quest-lab-arcane-sight`,
   gallery pieces, opens with the F6 panel) and `RuntimeArcaneSight` (lamp
   `comfy-quest-runtime-arcane-sight`, charm bindings, opens with the F9 drawer). Same
   name, same technique — point light plus emission property block — two mods, two
   meanings, zero shared code. Intent 01 defines Arcane Sight as *the* in-world runtime
   debugger, and the backlog asks to "investigate Arcane Sight as part of a spellbook
   surface"; that question is unanswerable until the name refers to one thing.
   **Recommendation:** the Runtime implementation keeps the name — it is the one that
   answers intent 01's question — and the Lab's becomes gallery highlighting.
2. **Lab already owns the missing rung of intent 03's loop.** The chain is
   `…Rehearse → Play Revision → Hot-load → Observe…`. Studio rehearses in the browser;
   Runtime plays; **Lab rehearses in-game** — Scenarios runs
   `Prepare draft → Run evaluator → Report → Export` through the real shared evaluator and
   writes receipts, but the machinery is aimed at release verification, not creators. This
   is the strongest single asset for the F9 cognitive-switch complaint (today a creator
   must leave the world to rehearse). Phase 4 input: repointing existing machinery, not
   building new.
3. **Lab solved intent 04's hardest problem in miniature.** Phase 5 needs
   creator-controlled *partial* disclosure. The Lab ships a working per-item disclosure
   classifier — `BINDABLE / DIAGNOSTIC / NO TRIGGER / NOT IN BUILD` — each verdict carrying
   player-altitude prose ("the world speaks, but no quest is listening yet"). Intent 02's
   permission enum (`hidden|explainable|share_selected|remixable`) is the same shape.
   Phase 4/5 should model the permission vocabulary on this proven pattern rather than
   invent one.
4. **The altitude rule generalizes.** Studio carries an author-stage no-plumbing-words
   pin; the creator-loop baseline now pins the Runtime HUD the same way (no snake_case, no
   paths, no hashes in a `CreatorLoopNotice` headline). The Lab is the remaining surface
   without such a pin.
5. **Lab's generated teaching prose is the model for intent 05's ladder level 1.**
   `LabJournal.g.cs` produces the best beginner text in the repo ("A hit and a kill are
   different creator events") from a generator; the adaptive predicates' prose is
   hand-assembled in the Runtime engine. Phase 4 direction: the grimoire generator pattern
   can carry adaptive-measure teaching prose.
6. **No in-world authoring exists anywhere.** Lab has no editor; Runtime has no editor;
   every in-world surface can observe and activate but never change. "Reduce the cognitive
   cost of switching between Studio and the in-world Creator" is therefore a scope
   decision, not a polish task — the switch is currently mandatory for any edit. Phase 4
   scoping starts from this fact.

## What would have to be true before moving anything

Per the palette-admission guardrail, extraction or gating happens only when a lap
demonstrates the cost: an apprentice confused by the Ready? tab, a creator blocked by
maintainer jargon, or Phase 4's notebook needing the Spellbook name. Until then this map
is a decision record, not a work order.

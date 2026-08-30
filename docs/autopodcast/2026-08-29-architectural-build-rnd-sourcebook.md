---
title: "The Build We Stopped Rebuilding"
subtitle: "An architectural capsule, a 40-piece Valheim structure, and the R&D loop that learned to leave good evidence standing"
document_type: "autopodcast-sourcebook"
target_duration_minutes: "55-65"
recorded_context_date: "2026-08-29"
primary_fixture: "tn0304"
audience: "technical creators, game-tool builders, R&D engineers, and curious product leaders"
recommended_format: "two-host investigative conversation"
evidence_status: "implemented, integrated, autonomously driven, operator-ready warm; not human-aesthetic acceptance"
---

# The Build We Stopped Rebuilding

## How to use this sourcebook

This is a source document for an automatically generated podcast of roughly one hour. It is
not intended to be read word-for-word as a monologue. The generator should turn the material
into a curious two-host conversation: one host keeps the story moving and asks what the system
actually did; the other explains the architecture, challenges loose claims, and returns often
to the distinction between evidence and confidence.

The ideal episode should sound like an engineering field story, not release-note narration.
It has a physical object at the center: a small timber building reconstructed from measured
architectural evidence, translated into 40 Valheim pieces, and deliberately left standing on
an automation machine after the team realized that rebuilding it for every R&D lap was wasting
time and erasing useful context.

The central question is not merely, “Can software put a house in a game?” It is:

> Can architectural intent cross several software and machine boundaries without becoming
> an untraceable pile of game prefabs—and can the team inspect that result repeatedly without
> turning every question into another destructive build-and-clear cycle?

The answer at the end of this sourcebook is a qualified yes.

### Recommended episode shape

| Time | Segment | Purpose |
| --- | --- | --- |
| 0:00-4:00 | Cold open: the standing build | Begin inside Valheim under the retained roof, then reveal that the final lap did not build anything. |
| 4:00-10:00 | Why Creator OS exists | Explain creator-seat scarcity, real vertical slices, Baseline, Quest, and AM4. |
| 10:00-18:00 | Reading a building honestly | Walk through the measured envelope, rejected interpretations, holdout view, and 29 mm reconciliation. |
| 18:00-26:00 | The immutable capsule | Explain deterministic ZIP bytes, cross-contract checks, content identity, and the difference between evidence and adaptation. |
| 26:00-35:00 | Studio Build | Cover bounded APIs, hostile ZIP validation, authoritative importer reuse, two view modes, and placement metadata. |
| 35:00-43:00 | Crossing into Valheim | Separate staging from applying, describe the disposable cold replay, and account for all 40 pieces. |
| 43:00-51:00 | Why warm R&D helped | Contrast the first retained build with the final five-operation read-only lap. |
| 51:00-57:00 | Three integration scars | Cold-client assumptions, the GET body bug, and the receipt sourcing bug. |
| 57:00-62:00 | What was proved—and what was not | Discuss fixture specificity, oversized files, aesthetic judgment, and the next honest experiment. |

The times are guidance, not hard chapter boundaries. Let the hosts return to earlier ideas as
new evidence changes their meaning.

### Pronunciation and vocabulary

- `tn0304`: say “tee-en zero-three-zero-four.” It is a fixture identifier, not a product name.
- `AM4`: say “A-M-four.” It is the unattended machine that ran the visible Valheim automation.
- `Valheim`: “Val-hyme.”
- `BepInEx`: “Bep-in-ex.”
- `SHA-256`: “S-H-A two-fifty-six,” or simply “content hash” after the first explanation.
- `yaw`: rotation around the vertical axis.
- `prefab`: a game-defined reusable piece, such as a floor panel, wall panel, or roof section.
- `capsule`: the immutable ZIP handoff, not a container image and not executable code.
- `receipt`: a structured record of an attempted or completed operation, including identity and outcome.
- `diff MATCH`: the game’s selected marked pieces exactly agree with the canonical capture after normalization.

### Factual guardrails for the generator

Do not claim any of the following:

1. Do not say Studio designed the building. Baseline solved and packaged the accepted
   architectural envelope; Studio inspected, placed, derived, and staged it.
2. Do not say the game pieces are the architecture. They are an explicit game adaptation of
   an architectural graph whose dimensions and provenance remain separately visible.
3. Do not say Studio applied placement to the world. Studio saved placement intent and staged
   canonical artifacts. A later Quest Lab operation applied the placement.
4. Do not say every live lap was read-only. The disposable cold replay built and cleared. The
   first warm lap built once and retained. The final operator lap was read-only.
5. Do not say there was never a Creator Session or mailbox activity anywhere in the journey.
   The **Studio staging increment** created neither. The later live apply used the bounded game
   automation surfaces appropriate to that separate attack.
6. Do not say the structure received final human aesthetic approval. The machine proved
   identity, counts, placement, hashes, and diff. The final camera view is operational evidence,
   not a cinematic or architectural taste verdict.
7. Do not present test counts as the main proof. Tests supported the result; the real browser,
   staged bytes, running Valheim client, standing pieces, and receipts were the decisive chain.
8. Do not imply a general CAD system. This is a deliberately hard-pinned rectangular-gable
   vertical slice with one positive fixture, one regression control, and one abstention control.

Primary source anchors for these boundaries are the Creator OS operating strategy [S1], the
Baseline relational-envelope handoff [S2], the capsule exporter [S3], the Studio service [S6],
and the tracked build/live/warm/demo evidence [S11] [S12] [S13] [S14].

---

## Executive overview

On August 29, 2026, three local repositories and one unattended game machine closed a narrow but
surprisingly rich vertical slice.

Baseline took pinned architectural evidence for fixture `tn0304` and solved one bounded envelope:
a rectangle measuring **7.953375 by 7.4676 metres**, a wall datum at **2.2225 metres**, a true
ridge at **5.8166 metres**, and a centered equal-pitch gable at **43.907838 degrees**. The solver
did not hide disagreement. It recorded a **negative 0.029171 metre** ridge-centering
reconciliation for the South holdout view, rejected three tempting alternative interpretations,
and left architectural floor count unresolved rather than extracting geometry from prose that
did not contain a numeric datum. [S2] [S3]

The game adapter compiled that graph into exactly **40 canonical pieces**:

- 16 `wood_floor` pieces;
- 16 `woodwall` pieces; and
- 8 `wood_roof_45` pieces.

It also said, explicitly, that one stable ground-floor surface was a `GAME_ONLY` adaptation. The
presence of floor prefabs did not magically settle the source building’s architectural floor
count. [S3] [S11]

Baseline then exported a deterministic `creator-os-architectural-build-capsule/v0` ZIP. The ZIP
contained the solved graph, constraints, interpretation and compilation receipts, pieces,
architectural candidate capture, and only the prefab geometry required to render those pieces.
Every member had a byte count and SHA-256 hash. The producer cross-checked fixture identity,
schemas, gates, reconciliation IDs, piece counts, prefab counts, and capture signatures before
writing anything. ZIP member order and metadata were fixed so identical inputs produced
byte-identical output. The accepted capsule’s SHA-256 was:

`f509aa2a201fdb3495c0f8aa3656ca156421524b476d12d4f0d45aa3cd9a21e9`

That hash became more than a checksum. It became the Studio build identity. [S3] [S4] [S11]

Quest Studio gained a separate **Build** workspace, outside the ordinary Author, Rehearse, Play,
and Observe quest flow. Studio accepted the raw ZIP through five bounded APIs, validated the
archive as hostile input, persisted imports immutably by capsule hash, invoked the existing
authoritative capture importer, and derived the canonical Quest Lab capture/blueprint pair at
zero yaw. The creator could toggle between the architectural envelope and the normalized game
pieces. The only editable fields were placement `x`, `y`, `z`, and `yaw`; changing them advanced
a revision but did not mutate the capsule, capture, blueprint, or canonical piece bytes. [S5]
[S6] [S7]

Studio staged the exact canonical pair into the Quest Lab blueprint directory. The staging
operation was transactional and idempotent: identical destination bytes were accepted; differing
bytes caused a collision refusal instead of an overwrite. Its receipt said, in effect,
**Staged, not applied**. It also recorded `placement_applied: false`, `creator_session_started: false`,
`mailbox_request_written: false`, and `world_mutation_performed: false`. [S6] [S11]

A separate live journey then applied the staged pair in Valheim. The disposable cold acceptance
proved the complete lifecycle: check, enable build authority, apply 40 pieces at the saved
placement, count them, diff them to `MATCH`, clear them, disable authority, stop, and restore the
exact pre-run plugin, Runtime, Lab, world, and character bytes. That replay proved reversibility,
but it was expensive as an ordinary R&D loop. [S12]

The team changed tactics. The warm harness built the same structure once, turned build authority
back off, retained the 40 marked pieces, retained a rollback snapshot, and left the running client
available for the next question. On the final operator lap the harness recognized the exact
identity and executed only:

`status → blueprint_check → blueprint_count → blueprint_diff → status`

No game launch. No new session. No build. No clear. No stop. No restore. The structure remained
standing and the diff still reported 40 expected, 40 selected, 0 missing, and 0 extra. [S13] [S14]

That is the origin of the title: **The Build We Stopped Rebuilding**.

---

## 1. The deeper product context: protect the creator’s seat

This project sits inside an operating principle that sounds obvious and is repeatedly violated
in real tool building:

> The scarce resource is the creator’s time in the seat.

Creator OS assigns machine-observable work to machines: build, deployment, process identity,
world selection, browser driving, bounded game operations, logs, receipts, screenshots, hash
checks, retries, shutdown, and restoration. Human attention is reserved for questions the
automation cannot settle: spatial judgment, narrative choice, clarity, pacing, composition,
play feel, and whether something deserves to exist. [S1]

That principle creates an evidence ladder:

1. **Implemented** means code exists.
2. **Integrated** means the real owned boundaries exchange the intended bytes and state.
3. **Autonomously driven** means the installed application and game journey completed without a
   human relay and retained evidence.
4. **Ready for seat** means machine-observable gates are green and remaining questions require
   human perception or authorship.
5. **Human accepted** means the creator supplied those prepared judgments.

The architectural journey reached the fourth state for a demo: operator-ready and warm. It did
not jump to the fifth state merely because a screenshot existed.

### Why three repositories appear in one story

The repository boundary is part of the experiment, not incidental organization.

**Baseline** owns cross-product research and the architectural evidence pipeline. For this lap it
owned the constraint model, solved graph, compiler receipts, deterministic capsule export, and
the honest distinction between measured, constrained, unresolved, repaired, and game-adapted
claims. The pinned Baseline source revision is
`544c0dc26ed3cbb556f5467ca0169705fe1172f5`. [S2] [S3] [S4]

**comfy-quest** owns Quest Studio, Quest Lab, the importer, browser surface, game request/receipt
contracts, live automation probe, and the evidence indexes. The architectural implementation
landed in source commit `4842e42dca34bbe7660143fdd41487d9e63a21a8`; the operator-ready evidence
followed in `f2ce9a09c5070cbd6637cfff3c6002e8bdf22891`. [S5] [S6] [S8] [S9]

**lumberjacks-platform** supplied the surrounding platform/tooling revision pinned by the
evidence: `2510553bddf1fbae6a4ea9c14ff923ca05b153d8`. That particular commit was not an
architectural solver change; it is a provenance pin for the platform state used during the lap.
Saying this out loud prevents a source revision from being misrepresented as feature authorship.
[S15]

**AM4** was the unattended visible machine. It carried the standalone Studio host, the SSH
control path, Steam, Valheim, the selected world and character, the staged Quest Lab pair, and the
standing marked structure. Calling it unattended does not mean unobserved. It means the machine
performed the mechanical loop without turning the creator into a keyboard relay.

### A useful debate for the hosts

Is this separation over-engineering for a 40-piece building? The skeptical answer is reasonable:
one could write a short script that emits floor, wall, and roof coordinates and then call the
result a house.

The counterargument is the heart of the episode. The difficult product question was never
whether code could place 40 objects. It was whether, after several translations, someone could
still answer:

- Which dimensions came from measured evidence?
- Which dimensions were constrained from other measurements?
- Which observation conflicted?
- Which repair was applied, by how much, and why?
- Which surfaces exist only because Valheim needs buildable pieces?
- Which exact bytes reached the game?
- Did placement metadata alter canonical geometry?
- Did the final lap prove the same build or quietly regenerate another one?

A tiny script can place a roof. It does not automatically preserve those answers.

---

## 2. Reading the building without pretending

### The accepted envelope

The positive development fixture, `tn0304`, is intentionally modest: one orthogonal rectangular
primary mass and one centered equal-pitch gable. This is not an arbitrary polygon solver, a room
planner, an opening compiler, or a detailed restoration model.

The accepted measurements are:

| Architectural fact | Value | Classification |
| --- | ---: | --- |
| Envelope width | 7.953375 m | measured |
| Envelope depth | 7.467600 m | measured |
| Floor-to-eave / wall datum | 2.222500 m | measured |
| Eave-to-ridge rise | 3.594100 m | measured |
| True ridge height | 5.816600 m | constrained from wall datum plus rise |
| Roof pitch | 43.907838° | constrained from rise divided by half-depth |
| Footprint closure gap | 0.000000 m | constrained rectangle |
| Architectural floor count | unresolved | rejected narrative inference |

The two key equations are simple enough to say aloud:

```text
ridge height = wall datum + roof rise
             = 2.2225 m + 3.5941 m
             = 5.8166 m

roof pitch = atan(roof rise / half the footprint depth)
           = atan(3.5941 / (7.4676 / 2))
           = 43.907838 degrees
```

Simple equations do not imply simple evidence. The challenge was attaching written dimensions
and view-relative geometry without allowing a visually plausible pixel line to outrank a stronger
explicit datum. [S2] [S3]

### The active view, the holdout view, and why both matter

The North active elevation began with 1,862 broad roof hypotheses. Constraint propagation
retained 88—a 95.2739 percent reduction. The chosen active hypothesis differed from the explicit
rise by 2.503 millimetres. A view-only vertical translation brought its roof relation within
1.669 millimetres without changing source coordinates.

The South elevation was held out while the active interpretation was developed. It began with
2,613 hypotheses and retained 41—a 98.4309 percent reduction. Its relative vertical agreement was
within 0.711 millimetres. It also exposed the visible imperfection that became the most memorable
number in the story: the ridge needed a 29.171 millimetre horizontal centering adjustment to enter
the accepted three-percent evidence tolerance. [S2]

The reconciliation record preserved:

- the source view and whether it was active or holdout;
- the selected candidate;
- the before and after values;
- `delta = -0.029171 m`;
- the allowable bound;
- the reason: “minimum horizontal snap needed to enter centered-ridge evidence tolerance”; and
- `source_observation_mutated = false`.

This is a useful conceptual distinction for the episode. A **repair** is not automatically a
cover-up. A hidden repair is a cover-up. A bounded, named, reasoned repair that preserves the
original observation can be better evidence than a false claim that noisy sources agreed
perfectly.

### Three interpretations the system refused

The solver rejected three tempting alternatives:

1. A shared absolute elevation origin across views. The views supported relative geometry, not
   one unquestioned global pixel frame.
2. Two equal-height levels inferred from historical prose. The sentence was narrative, not a
   numeric geometric datum.
3. A legacy visual plan frame measuring roughly 10.426850 by 9.033344 metres. It looked plausible
   but contradicted the stronger explicit dimensions.

Exactly one envelope hypothesis remained admissible. The goal was not to maximize recovered
detail. It was to maximize defensible detail.

### The two control fixtures

The experiment also carried two important controls.

`sd0401` was an accepted regression control. Its main width and depth stayed aligned with a prior
F2 oracle, its maximum eave/ridge deviation remained under a 100 mm bound, and it compiled within
budget. Crucially, the accepted oracle was evaluation-only; it did not feed the solver’s features.

`tn0305` was a negative abstention control. Width and depth were missing. The status remained
`HELD_INSUFFICIENT_CONSTRAINTS`. No footprint, roof, compilation receipt, or pieces were invented.

The negative fixture matters because a solver that only encounters answerable examples can look
honest without ever having to abstain.

### The first implementation scar

The first envelope run treated detected diagonal segment endpoints as roof eaves. The visible
segments stopped short, but the code still allowed pixel geometry to behave like architectural
authority. The final pass reversed the relationship:

```text
vision proposes slopes
        ↓
explicit constraints predict a narrow eave band
        ↓
the existing vision pass validates horizontal support
        ↓
the eave datum intersects the candidate slopes
```

No new detector and no Hough-tuning campaign were introduced. The scar was a reasoning error,
not a shortage of computer vision. [S2]

### Conversation prompt

Ask whether “honest abstention” is a product feature or an engineering luxury. Then connect it to
the podcast’s later theme: warm mode also abstains from rebuilding when the exact accepted object
already exists.

---

## 3. The game adaptation is not the architecture

Once the graph was solved, a bounded compiler translated it into Valheim’s modular vocabulary.
The result was exactly 40 pieces:

| Prefab | Role | Count | Capsule proxy bounds |
| --- | --- | ---: | --- |
| `wood_floor` | stable floor surface modules | 16 | 2.0 × 0.2 × 2.0 m |
| `woodwall` | wall modules | 16 | 2.0 × 2.0 × 0.2 m |
| `wood_roof_45` | roof modules | 8 | 2.0 × 2.4633 × 2.8284 m |

There are several intentionally visible mismatches.

The architectural pitch is 43.907838 degrees; the target prefab is the game’s 45-degree roof
piece. The architecture view keeps the true ridge and pitch. The game-pieces view shows the exact
prefab adaptation. Neither view is allowed to silently impersonate the other.

The source’s architectural floor count is unresolved; the compiler emits one stable buildable
ground surface made from 16 floor modules. That is recorded as:

```json
{
  "id": "game-floor-surface",
  "kind": "GAME_ONLY",
  "architectural_floor_count": null,
  "compiled_floor_surfaces": 1,
  "reason": "one stable buildable ground surface; not architectural evidence"
}
```

Sixteen floor prefabs do not mean sixteen floors. One compiled floor surface does not settle the
historical storey count. This sounds pedantic until downstream tools begin treating implementation
convenience as source truth.

The compiler recorded no hidden overlap. It stayed within the frozen piece budget. It emitted a
piece list, a compilation receipt, and reconciliation IDs matching the solved graph and
interpretation receipt. [S3]

### Why the podcast should linger here

Many software pipelines collapse “source,” “model,” and “render” into one thing because they are
visually similar. This slice deliberately maintains three layers:

1. **Observation and constraint truth**: what the evidence says or supports.
2. **Solved architectural graph**: the accepted interpretation and its repairs.
3. **Game adaptation**: the finite set of buildable pieces used to approximate that graph.

The same separation applies beyond buildings. It is the difference between a musical score and a
synth patch, a manufacturing drawing and a toolpath, or a quest design and the Runtime actions
that implement it.

---

## 4. The capsule: an immutable handoff that can argue with itself

### What the ZIP contains

The architectural build capsule has exactly eight members:

```text
capsule.json
solved-building.graph.json
constraint-model.json
interpretation-receipt.json
compilation-receipt.json
pieces.json
architectural-candidate.capture.json
prefab-geometry.json
```

It does not include imported HTML, JavaScript, a general prefab catalog, source TIFFs, a solver
runtime, or arbitrary files. Studio owns its renderer and never executes presentation code from
the ZIP.

`capsule.json` records the schema, fixture and source revision, producer and producer hash,
entrypoints, member byte counts and hashes, piece count, prefab counts, compiled-piece hash,
candidate-piece hash, reconciliation IDs, and game adaptations.

### Cross-check before publication

The producer does not merely gather files. It rejects a capsule if:

- graph, constraint model, and interpretation receipt disagree on fixture identity;
- any member uses an unsupported schema;
- the graph is not `SOLVED_RND`;
- the interpretation receipt or any gate is not `PASS`;
- the pieces, compilation receipt, or maximum budget disagree with the 40-piece result;
- prefab counts disagree;
- graph, interpretation, and compilation reconciliation IDs disagree;
- prefab geometry covers more or fewer prefab names than the compiled pieces; or
- the architectural capture’s piece count or normalized piece hash disagrees with the pieces.

The key exporter logic is concise enough to show in the episode notes. This excerpt is lightly
abridged but preserves the checks and deterministic ZIP mechanics: [S3]

```python
def write_capsule(revision, output):
    pieces = read_json(source / "pieces.json")
    graph = read_json(source / "solved-building.graph.json")
    constraints = read_json(source / "constraint-model.json")
    compilation = read_json(source / "compilation-receipt.json")
    interpretation = read_json(source / "interpretation-receipt.json")

    if fixture_ids != {"tn0304"}:
        raise RuntimeError("members disagree on fixture identity")
    if graph["status"] != "SOLVED_RND" or interpretation["status"] != "PASS":
        raise RuntimeError("not a closed architectural compilation")
    if len(pieces) != 40 or compilation["piece_count"] != 40:
        raise RuntimeError("piece-count disagreement")
    if graph_reconciliations != interpretation_reconciliations \
            or graph_reconciliations != compilation_reconciliations:
        raise RuntimeError("reconciliation identities disagree")

    with zipfile.ZipFile(temp, "w", compression=zipfile.ZIP_DEFLATED,
                         compresslevel=9) as archive:
        for name in sorted(members):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o600 << 16
            info.extra = b""
            info.comment = b""
            archive.writestr(info, members[name])
```

The fixed 1980 ZIP timestamp is not nostalgia. ZIP metadata often carries current timestamps,
permissions, platform flags, comments, or member ordering. Any one of those can make two
semantically identical archives hash differently. Sorting names and fixing metadata turns the
archive’s SHA-256 into a meaningful content identity.

The focused Baseline test writes the capsule twice and compares the complete byte arrays before
checking member order, schemas, the 40-piece count, and the 16/16/8 prefab split. [S4]

### Content-addressed identity

Studio uses the complete capsule SHA-256 as `build_id`. Reimporting identical bytes reopens the
same immutable build. Changed bytes create a different identity even if a human would give both
ZIPs the same filename.

This avoids a subtle but common bug: identity by path. A file called `final.zip` can change
without warning. A content hash cannot change while remaining the same identity.

### The accepted artifact pins

| Artifact | SHA-256 |
| --- | --- |
| Capsule / Studio build | `f509aa2a201fdb3495c0f8aa3656ca156421524b476d12d4f0d45aa3cd9a21e9` |
| Solved graph | `29402c6c9d85c66d3b3760d88b6323ec64b1cb938aa29158c7501309ac0bed46` |
| Canonical pieces | `0b4a62bcd3d0baa914f264081657b99649dd0f25a5973e1258b4e3920acbb578` |
| Canonical capture | `5d466cdaa5a213ef958d07636325b9398eee9a74584019da8dc16a5603654251` |
| Canonical blueprint | `02201382e57635f4e945229836443d2fdbf75e243973281d1f9806cd770ece5f` |
| Authoritative importer | `eadd147307c0540706bdfa7633c9cc887f4130ec6a2bbb67bb88a228eee0a5df` |

The hosts do not need to read every hash aloud. Explain one fully, then describe the rest as a
linked identity chain. The exact values are here for the generator’s factual grounding.

---

## 5. Studio Build: a new workspace, not a quest stage

### Why Build sits outside Author, Rehearse, Play, and Observe

Quest Studio already had a creator journey for quest content. Architectural placement is a
different cognitive task and a different authority boundary. The Build workspace therefore
appears as a separate destination. Its own header says:

> Inspect the accepted CAD intent and its exact game-piece adaptation. Placement is prepared
> metadata only; it is not applied to a world.

This avoids smuggling world-building controls into the quest lifecycle and prevents a creator
from assuming that clicking Stage is equivalent to entering or modifying Valheim.

### The five APIs

All five routes use the existing browser token and no-store responses: [S5]

```text
GET  /api/v2/quest-studio/builds
POST /api/v2/quest-studio/builds/import
GET  /api/v2/quest-studio/builds/{buildId}
PUT  /api/v2/quest-studio/builds/{buildId}/placement
POST /api/v2/quest-studio/builds/{buildId}/stage
```

The import route accepts raw `application/zip` bytes. There is no multipart form contract to
silently reinterpret and no filename-based identity.

### Treat the ZIP as hostile input

Studio’s hard limits are:

- 2 MiB compressed;
- 32 archive members;
- 8 MiB expanded;
- 256 pieces; and
- 32 KiB of importer diagnostic output.

It rejects paths containing traversal, directories, backslashes, absolute roots, or `..`;
duplicate names; unlisted names; incomplete member sets; member size overflow; member hash or
byte-count mismatch; unsupported schemas; non-`tn0304` fixtures; failed interpretation gates;
piece-count or prefab-count disagreement; non-architectural capture selection; quaternion or
piece normalization disagreement; reconciliation disagreement; and prefab geometry that does not
exactly cover the used prefabs. [S6]

Representative validation code: [S6]

```csharp
public const long MaxCompressedBytes = 2 * 1024 * 1024;
public const long MaxExpandedBytes = 8 * 1024 * 1024;
public const int MaxMembers = 32;
public const int MaxPieces = 256;

if (name != entry.Name || name.Contains('\\') || name.StartsWith('/') ||
    name.Contains("..", StringComparison.Ordinal) ||
    !RequiredMembers.Contains(name, StringComparer.Ordinal))
    throw new BuildContractException("capsule_member_invalid");

if (!files.TryAdd(name, []))
    throw new BuildContractException("capsule_duplicate_member");
```

These checks happen before immutable persistence. A rejected archive does not leave a half-built
Studio identity behind.

### Reuse the authoritative importer or fail visibly

Quest already had `tools/blueprints/import_capture.py`, the authoritative path from a normalized
capture to the Quest Lab capture/blueprint pair. Studio invokes that script through the
standalone R&D host. It supplies a content-addressed derived name and an explicit zero-degree
derivation yaw. [S7]

```text
import_capture.py <candidate-capture>
  --output-root <temporary-output>
  --derive-architectural-name <content-addressed-name>
  --derive-yaw-degrees 0
```

If the script, Python executable, or standalone bundle manifest is unavailable, Studio returns:

`architectural_importer_unavailable`

It does not quietly implement a second importer in C#.

This “scar” is worth discussing. Duplication might make a demo appear more self-contained, but
two importers would create two normalization authorities. A future mismatch could produce a
Studio preview that looked right while Quest Lab built different bytes. Explicit unavailability
is less convenient and more trustworthy.

### Placement as revision-checked intent

The imported build begins with placement revision 1 and zero values. The accepted demo placement
became revision 2:

```json
{
  "revision": 2,
  "x": 12.5,
  "y": 1.25,
  "z": -3.75,
  "yaw": 22.5
}
```

The client sends `expected_revision`. If another edit has already advanced the build, the server
returns `revision_conflict`. Finite-number validation rejects NaN or infinity. Saving placement
does not regenerate the capture, blueprint, or canonical pieces.

That is why the Studio UI repeatedly says “prepared only” and “not applied.” The metadata is a
future build-request intent.

---

## 6. Two views of truth

The Build workspace has two explicit view modes.

### Architecture mode

Architecture mode renders Studio-owned geometry from the solved graph:

- the measured rectangular footprint;
- the wall datum;
- the true ridge;
- the roof planes;
- measured, constrained, inferred, repaired, and unresolved classifications;
- confidence and source views;
- supporting constraints;
- conflicting or rejected interpretations;
- the negative 0.029171 metre ridge reconciliation and its reason; and
- game-only adaptations listed as adaptations.

This is not imported HTML. The capsule supplies data; Studio supplies presentation.

### Game pieces mode

Game pieces mode renders all 40 normalized pieces using the capsule’s bounded prefab proxy
geometry. It shows the three prefab counts and artifact hashes. The proxy geometry is enough for
inspection but is not claimed to be a complete copy of Valheim’s game meshes.

### Why toggling matters

The useful gesture is not merely switching a tab. It is asking two different questions:

- “What did the architectural evidence and constraint system settle?”
- “What exact objects will the game receive?”

Most downstream surprises live in the gap between those answers.

The browser journey proved both views, the provenance and reconciliation text, the placement
edit, staging, deep linking, reimport idempotence, and all 40 rendered piece records. It also
proved that capture and blueprint hashes remained unchanged across placement revision. [S10]
[S11]

---

## 7. Staging is a transaction, not a euphemism for building

### What Stage does

The Stage endpoint discovers the Quest Lab blueprint directory and considers exactly two files:

```text
<content-addressed-name>.capture.json
<content-addressed-name>.blueprint
```

It verifies importer output and the canonical descriptor, writes temporary files, verifies their
hashes, records a journal, and commits the pair. If a failure occurs between the two moves,
recovery can remove the partial destination or complete the exact pair safely. [S6]

If both destination files already contain identical bytes, the operation is idempotent and
returns `already_present: true`. If either destination contains different bytes, Studio returns
`stage_collision`; it does not overwrite the differing file.

### What the stage receipt says

The receipt schema is `comfy-quest-studio-build-stage/v1`. It pins:

- stage ID and completion time;
- build and capsule identity;
- source revision;
- graph, compiled-piece, canonical-piece, importer, and importer-bundle hashes;
- exact capture and blueprint paths, sizes, and hashes;
- saved placement revision and values; and
- the safety boundary.

The safety fields are intentionally blunt:

```json
{
  "placement_applied": false,
  "creator_session_started": false,
  "mailbox_request_written": false,
  "world_mutation_performed": false
}
```

### Why the distinction matters

“Stage” is one of those words that can hide a multitude of side effects. Here it means moving a
verified canonical pair into the directory where a later bounded live operation can find it. It
does not mean launching Steam, entering a world, enabling free build, applying pieces, starting a
Creator Session, or sending a mailbox command.

The podcast should make this moment suspenseful precisely because nothing visible happens in the
game. The work is preparing a trustworthy edge for the next attack.

---

## 8. The disposable cold replay: prove reversibility once

The first complete live acceptance intentionally used the expensive lifecycle.

It entered the pinned demo world and character, verified the exact staged pair, and performed:

1. `status`;
2. `blueprint_check`;
3. `blueprint_count_before`;
4. `build_on`;
5. `blueprint_build_at` using `x=12.5`, `y=1.25`, `z=-3.75`, `yaw=22.5`;
6. `blueprint_count_applied`;
7. `blueprint_diff_at`;
8. `blueprint_clear`;
9. `blueprint_count_after`;
10. `build_off`; and
11. `status_final`.

The build operation reported 40 placed and 0 failed. The marked piece count reported 16 floors,
16 walls, and 8 roofs. Diff reported:

```text
MATCH
expected 40
selected 40
missing 0
extra 0
```

Clear reported 40 cleared and 0 remaining. Restoration then proved:

- graceful shutdown;
- no forced termination;
- exact plugin restoration;
- exact Runtime and Lab state restoration;
- exact world database before/after SHA-256;
- exact character profile before/after SHA-256; and
- retention of the canonical staged pair.

This run was a **disposable technical replay**. Its own tracked limitation says that the exact
pair was applied, proved, and cleared rather than retained as authored world state. [S12]

### Why this was still necessary

Warm mode should not become an excuse to skip cleanup proof. Before deciding to retain a build,
the team needed one full demonstration that it could build, identify only its own marked pieces,
clear them, shut down, and restore exact bytes.

That cold replay answered, “Can we reverse the transaction?”

It did not answer, “Should we pay the reversal cost after every later observation?”

### Timeline nuance

The cold evidence receipt records its checkout base as `d2187c04cddb3913f47ecf502e954b05f4c7d2a0`.
The source and evidence that formalized the complete architectural/warm slice landed together in
`4842e42dca34bbe7660143fdd41487d9e63a21a8`. This is an example of why receipts and landed
commit history should both be cited rather than collapsed into one date.

---

## 9. The warm R&D loop: build once, inspect repeatedly

### The first warm lap

The warm harness began from a prepared identity and retained rollback snapshot. If Valheim was
not running, it performed the bounded launch and world entry. It checked the canonical pair and
counted marked pieces.

When the count was zero, it enabled creator build authority, built the 40 pieces at the saved
placement, counted again, diffed to `MATCH`, disabled authority, captured evidence, and left the
marked structure standing. The tracked warm receipt calls this `build_action: created`. [S8]
[S13]

The implementation’s central branch is readable: [S8]

```python
count_receipt = driver.request("lab", "blueprint_count", ...)
standing_count = driver.count(count_receipt)

if standing_count == 0:
    driver.request("runtime", "build_on")
    driver.request("lab", "blueprint_build", ...)
    standing_count = driver.count(driver.request("lab", "blueprint_count", ...))
    build_action = "created"
elif standing_count == args.piece_count:
    build_action = "reused"
else:
    raise RuntimeError(f"warm_mark_count_drift:{standing_count}")

driver.request("lab", "blueprint_diff", ...)
driver.disable_authority()
```

This is not “if anything exists, trust it.” Zero means create. Exactly 40 means continue to exact
prefab and diff checks. Any partial marked count fails closed.

### Failure ownership

The most thoughtful warm-mode behavior appears in cleanup:

> A failed lap removes only pieces it created itself. A previously accepted warm build is never
> torn down merely because a later probe failed.

If a new build attempt fails, the harness owns cleanup of those pieces. If a later read-only
probe fails against an already accepted structure, the harness disables authority but preserves
the scene for diagnosis. [S8]

This is the practical meaning of “stop rebuilding.” It is not abandoning cleanup discipline. It
is assigning cleanup ownership to the operation that created the state.

### The final operator lap

By the final demo, Valheim and Steam were already running, the warm session existed, the installed
Lab plugin matched, the canonical stage was already present, Studio was already running, and the
SSH tunnel could be reused.

The demo harness explicitly forbade build, clear, stop, restore, and deploy operations. It
required the observed sequence to equal exactly: [S9] [S14]

```text
status
blueprint_check
blueprint_count
blueprint_diff
status
```

The result:

- `build_action: reused`;
- `standing_piece_count: 40`;
- `diff_result: MATCH`;
- game launched: false;
- session opened: false;
- canonical stage already present: true;
- remote Studio reused: true;
- SSH tunnel reused: true;
- Valheim left running: true;
- pending Runtime mailbox: false; and
- pending Lab mailbox: false.

The exact game-window evidence was captured at 1920 by 1080. Its SHA-256 was
`5d4665915c71248265e1940d5e03a73f792f574b9c477f91c2acf7abac47a29c`.
Unlike earlier desktop-wide images, this capture selected the actual X11 Valheim window and did
not include the neighboring Steam client. [S13] [S14] [S16]

### The R&D verdict

Warm R&D helped in three measurable ways.

First, it reduced the final lap from a lifecycle transaction to five read-only observations.

Second, it preserved spatial context. A standing structure can be inspected from another angle,
compared with Studio, and discussed without asking whether a fresh rebuild changed the thing
under review.

Third, it made seam defects cheaper. Restarting the whole stack after every fix would have mixed
startup, deployment, world entry, building, cleanup, and restoration failures into every probe.
Warm reuse isolated the control-plane and evidence problems that actually remained.

The caution is equally important: warm state creates responsibility. Identity drift must fail
closed. Plugin changes or a different canonical pair require explicit close and restoration.
Ordinary laps cannot silently deploy over a retained session.

---

## 10. Three integration scars found after the building worked

### Scar one: the verifier assumed Valheim was cold

The original stage verifier treated an already-running client as unexpected state. That made
sense for the mutation-free Studio increment, but not for an R&D loop deliberately retaining the
same process and scene.

The correction introduced explicit warm-client verification. It does not broadly relax the
machine guard. It requires the exact same Valheim process identity and command. World, character,
Creator Session, mailbox, Lab control, and staged artifact bytes remain strict. The only mutable
filesystem allowance is append-only event archive growth in the expected JSONL/CSV records.

This is a good discussion point: safe reuse often requires **more specific verification**, not
less verification.

### Scar two: a PowerShell GET acquired an empty body

The operator helper accepted a body parameter. On Windows PowerShell, an empty-but-present body
could still affect a GET request. The fix was small and exact:

```powershell
if (-not [string]::IsNullOrEmpty($Body)) {
    $arguments.ContentType = 'application/json'
    $arguments.Body = $Body
}
$response = Invoke-WebRequest @arguments
```

This bug had nothing to do with roofs, constraints, Valheim physics, or content hashes. It was
still capable of breaking the demo. Vertical slices are valuable because they force mundane
transport behavior to meet the grand architecture. [S9]

### Scar three: the evidence receipt read the wrong source

The first demo evidence index populated its operation list from a summary acceptance object rather
than the detailed warm sequence. The run itself had performed the correct operations, but the
tracked evidence could have told a less exact story.

The final correction sourced the operation list from the warm sequence and explicitly recorded:

- exact read-only sequence: true;
- canonical stage idempotent: true; and
- control plane reused: true.

This scar is philosophically important. Evidence software is production software. A correct
operation with an incorrect receipt is not fully proved.

### A fourth, visual scar: the screenshot included too much truth

Early live screenshots captured the whole desktop. They proved something had happened, but a
large Steam window competed with Valheim and made the artifact harder to read. The final capture
found the exact X11 window by class and captured only that window.

Nothing about the structure changed. The evidence became more legible.

This invites a useful distinction between manipulating evidence and presenting evidence well.
Cropping away unrelated desktop chrome by selecting the authoritative application window is not
the same as editing the scene or generating a prettier building.

---

## 11. The final demoable state

At handoff, the operator could open Studio directly into the Build workspace and begin in
Architecture mode. The accepted build identity, measurements, 40-piece count, placement intent,
capsule hash, and staged-not-applied status were visible in one page.

The creator could:

1. inspect the architecture view;
2. read classifications, provenance, conflicts, and the 29 mm reconciliation;
3. toggle to the game-pieces view;
4. verify 16 floors, 16 walls, and 8 roofs;
5. confirm the exact placement values;
6. stage idempotently; and
7. compare the Studio evidence with the already-standing Valheim build.

The structure remained in the world with creator build authority disabled. No pending Runtime or
Lab mailboxes remained. Explicit close and exact rollback still existed as a separate operation;
ordinary demo laps did not invoke it. [S13] [S14]

The browser and game visuals are collected in the companion HTML journey [S16]. The image
provenance manifest pins the exact Studio documentation captures and accepted Valheim screenshot
bytes. [S17]

### Tests at the architectural handoff

The implementation handoff recorded:

- 411 Python tests passing;
- 133 Studio tests passing; and
- 371 Quest Lab tests passing.

The later HTML article added six focused contracts, bringing the current Python total to 417 at
that article commit. The podcast sourcebook should say “tests supported the chain,” not “1,000
tests proved the product.”

The focused scars included:

- deterministic capsule generation;
- valid import and idempotent reimport;
- tampered, oversized, traversal, duplicate, unsupported, and cross-contract rejection;
- authoritative architectural derivation;
- placement revision conflicts without artifact drift;
- staging collision refusal and recovery;
- absence of Studio mailbox/world writes;
- cold build/diff/clear/restore;
- warm create versus reuse behavior;
- exact five-operation final demo sequence;
- screenshot selection by exact X11 window; and
- mission-control agreement with immutable evidence.

---

## 12. What the review found after the adrenaline

### What is genuinely proved

1. The accepted `tn0304` constraint envelope can be exported as byte-deterministic capsule bytes.
2. The capsule preserves solved intent, provenance, conflicts, repairs, pieces, and only the
   geometry required for its game adaptation.
3. Studio can reject malformed or inconsistent capsules before persistence.
4. Studio reuses Quest’s authoritative importer rather than creating a second normalization path.
5. Architecture and game adaptation can be inspected separately in Studio-owned rendering.
6. Placement is revision-checked intent and does not drift canonical artifact hashes.
7. Staging is an idempotent two-file transaction with collision refusal and recovery.
8. The staged pair can be applied at the saved placement as 40 pieces with zero failures.
9. The standing marked set can diff exactly against the canonical pair.
10. The disposable lifecycle can clear and restore exact prior bytes.
11. A retained warm lifecycle can reuse the exact build and reduce later laps to read-only proof.

### What remains narrow or uncomfortable

1. The Studio import contract hard-pins `tn0304`. That is appropriate for the slice and not a
   general fixture system.
2. The building topology is one rectangle and one centered gable. Openings, appendages, arbitrary
   polygons, interiors, and a general CAD solver remain out of scope.
3. The game roof uses 45-degree prefabs against a 43.907838-degree architectural pitch. The
   mismatch is visible and recorded, not solved away.
4. The accepted game screenshot is evidentiary rather than cinematic. It looks from under and
   around the structure; it does not supply a perfect portfolio elevation.
5. The implementation tranche is large: 6,391 insertions across 33 files in the primary source
   commit. `QuestStudioBuilds.cs` landed at roughly 1,168 lines and the live probe at roughly
   1,259. Those files should be split if the feature generalizes.
6. The harness carries machine-, fixture-, world-, and placement-specific pins. A second fixture
   should earn a manifest-driven abstraction before a framework is invented.
7. Human spatial and aesthetic acceptance is still absent. Machine proof cannot decide whether
   the adaptation feels architecturally convincing in play.

### Why “fixture-specific” is not automatically a defect

Hard pins can be technical debt, but premature generality can be a more expensive form of debt.
The slice was designed to discover which boundaries actually repeat. A second structure might
show that the reusable abstraction is “architectural capsule,” or it might reveal that roof
topology, prefab vocabulary, opening semantics, and placement policy need different contracts.

The correct next abstraction should be extracted from observed repetition, not from the current
team’s ability to imagine a class hierarchy.

### Why “leave it standing” is not automatically state leakage

Retained state is dangerous when it is anonymous, mutable, or lacks rollback. Here it was:

- tied to capsule, stage, world, character, session, plugin, and process identity;
- counted and diffed;
- protected by disabled build authority;
- accompanied by a retained rollback snapshot;
- refused on drift; and
- removable only by an explicit close operation.

The design does not say “never clean up.” It says “do not erase an accepted diagnostic scene just
because the harness traditionally cleans up at the end of every command.”

---

## 13. Product and engineering questions for the hosts

These questions are intentionally open enough to sustain conversation. The source material above
contains the facts needed to argue both sides.

### On truth and adaptation

1. At what point does a game adaptation stop being faithful even if every difference is recorded?
2. Is preserving a 43.907838-degree true pitch meaningful when the game can only build it from
   45-degree modules?
3. Does visible provenance improve creative judgment, or does it burden the creator with
   engineering detail?
4. What other domains need a hard distinction between source truth and implementation truth?

### On deterministic artifacts

5. Is byte-identical ZIP output necessary, or would canonical member hashes be enough?
6. When does a content hash become a product identity rather than an implementation detail?
7. What new failure modes appear when immutable build identity meets mutable placement intent?
8. Does failing with `architectural_importer_unavailable` create a better product or merely a
   more honest prototype?

### On R&D operations

9. When does a warm retained environment improve scientific observation, and when does it create
   hidden coupling?
10. Was the cold build-and-clear replay necessary once, or should the team have gone warm from
    the beginning?
11. What evidence is required before a team is justified in preserving state between laps?
12. Should a failed read-only probe ever tear down previously accepted state?
13. How should ownership work when one operation creates state and another discovers a problem?

### On the creator’s seat

14. Which judgments in this story truly require a human creator?
15. Does an unattended visible game machine protect creative time, or distance the creator from
    useful serendipity?
16. What does “ready for seat” mean when the machine can prove geometry but not taste?
17. How should a demo communicate that distinction without sounding apologetic?

### On evidence

18. Which was more valuable: the exact game screenshot, the diff receipt, or the rollback proof?
19. Is an operation still proved if the receipt summarizes it incorrectly?
20. When does improving evidence presentation become evidence manipulation?
21. Are 417 Python tests worth mentioning if the decisive observation is five live read-only
    operations against a standing structure?

### On scope

22. What would a second fixture need to prove before the Studio contract should stop hard-pinning
    `tn0304`?
23. Should the next push improve visual fidelity, add openings, generalize schemas, or conduct a
    human spatial review?
24. Which oversized implementation seam should be split first, and which should remain intact
    until another fixture exists?

---

## 14. Suggested closing argument

The most interesting artifact from this lap is not the ZIP, the Studio page, the code, or even the
building. It is a change in how the team treated an accepted result.

The early automation loop behaved like many responsible test harnesses: set up exact state,
perform the mutation, verify the result, clear it, stop everything, and restore exact bytes. That
discipline earned trust. It also became wasteful once the remaining questions concerned the same
accepted scene.

Warm R&D did not discard rigor. It moved rigor from automatic teardown to explicit identity:

- Is this the same capsule?
- Is this the same canonical pair?
- Is this the same plugin and process?
- Is this the same world, character, and session?
- Are exactly 40 marked pieces standing?
- Do they still diff to `MATCH`?
- Is creator build authority off?
- Are there no pending mailbox requests?
- Does rollback still exist as a separate, deliberate act?

Once those answers were yes, rebuilding stopped being proof. Reuse became proof.

The next honest step is not to regenerate the structure for a more theatrical demo. It is to
bring human attention to the standing evidence and ask the questions automation cannot answer:
Does the adaptation read as the building it claims to represent? Does the scale feel right from
player altitude? Does the roof communicate the architectural intent despite the prefab pitch?
Which compromises are acceptable, and which ones should force another architectural or game
adapter revision?

The machine loop should arrive with those questions prepared. The creator should not have to wait
for another house to be built.

---

## 15. Compact fact sheet

Use this section to resolve ambiguity during generation.

### Architectural facts

- Fixture: `tn0304`.
- Footprint: 7.953375 × 7.467600 m.
- Wall datum: 2.222500 m.
- Roof rise: 3.594100 m.
- True ridge: 5.816600 m.
- True pitch: 43.907838°.
- Ridge reconciliation: −0.029171 m in the South holdout view.
- Footprint closure: 0 m.
- Architectural floor count: unresolved.
- Accepted hypothesis count: exactly one.

### Game adaptation facts

- Total pieces: 40.
- Floors: 16 `wood_floor`.
- Walls: 16 `woodwall`.
- Roofs: 8 `wood_roof_45`.
- Distinct prefabs: 3.
- Compiled floor surfaces: 1, game-only.
- Hidden overlap: 0 m.

### Placement facts

- Placement revision: 2.
- X: 12.5.
- Y: 1.25.
- Z: −3.75.
- Yaw: 22.5°.
- Studio placement applied: false.
- Live placement later applied: true.

### Final warm-lap facts

- Build action: reused.
- Standing marked pieces: 40.
- Diff: `MATCH`.
- Missing: 0.
- Extra: 0.
- Game launched: false.
- Session opened: false.
- Studio reused: true.
- SSH tunnel reused: true.
- Stage already present: true.
- Valheim left running: true.
- Creator build enabled at end: false.
- Pending mailboxes: none.
- Operations: `status`, `blueprint_check`, `blueprint_count`, `blueprint_diff`, `status`.

### Commit facts

- Baseline architectural pipeline and capsule: `544c0dc26ed3cbb556f5467ca0169705fe1172f5`.
- Quest implementation: `4842e42dca34bbe7660143fdd41487d9e63a21a8`.
- Quest operator-ready evidence: `f2ce9a09c5070cbd6637cfff3c6002e8bdf22891`.
- Quest HTML journey and tracked imagery: `0e85b95d67c0124679d44edd4775b191b88be0c5`.
- Lumberjacks platform state pin: `2510553bddf1fbae6a4ea9c14ff923ca05b153d8`.

---

## 16. Source ledger

The links below are the authority map for this sourcebook. Local evidence links are relative to
this Markdown file. GitHub code links are pinned to immutable commits rather than `main`.

| ID | Source | Why it matters |
| --- | --- | --- |
| S1 | [Creator OS operating strategy][S1] | Defines creator-seat scarcity, evidence priorities, proof levels, repository ownership, and ready-before-seat behavior. |
| S2 | [Relational Envelope v0 handoff at Baseline commit 544c0dc][S2] | Canonical narrative for measurements, hypotheses, controls, reconciliation, hardware proof, and known limits. |
| S3 | [Baseline envelope and capsule producer][S3] | Exact solved graph construction, classifications, game adaptation, capsule cross-checks, member hashes, and deterministic ZIP writing. |
| S4 | [Baseline architectural capsule test][S4] | Byte-identical rebuild, exact member set, schemas, piece count, and prefab split. |
| S5 | [Quest Studio Build API routes at implementation commit][S5] | Exact five route definitions, authorization, no-store handling, and 2 MiB request limit. |
| S6 | [Quest Studio build service at implementation commit][S6] | Archive bounds, validation, immutable persistence, placement revisions, importer invocation, staging transaction, collision refusal, and receipt. |
| S7 | [Authoritative capture importer at implementation commit][S7] | Architectural derivation, content-addressed naming, zero-yaw support, and canonical pair output. |
| S8 | [Architectural live probe at implementation commit][S8] | Cold replay, warm create/reuse branch, failure ownership, exact diff, screenshot capture, shutdown, and restoration. |
| S9 | [Operator demo harness at evidence commit][S9] | Warm preconditions, exact five-operation sequence, forbidden operations, control-plane reuse, GET-body fix, idempotent stage, and final assertions. |
| S10 | [Synthetic Studio browser journey][S10] | Browser proof for upload, both views, provenance, placement edit, immutable hashes, staging, reimport, and deep links. |
| S11 | [Tracked Studio build evidence][S11] | Capsule/build/stage identity, architecture values, placement intent, canonical hashes, and mutation-free Studio boundary. |
| S12 | [Tracked cold live replay evidence][S12] | Full apply/diff/clear lifecycle, exact receipt chain, screenshot, and byte restoration. |
| S13 | [Tracked warm R&D evidence][S13] | First created lap, final reused lap, five operations, retained state, exact screenshot, rollback boundary, and limitations. |
| S14 | [Tracked operator demo evidence][S14] | Operator-ready state, final identities, browser proof, warm reuse, safety assertions, and limitations. |
| S15 | [Pinned Lumberjacks platform revision][S15] | Provenance for the surrounding platform state; explicitly not claimed as the architectural implementation commit. |
| S16 | [Companion HTML journey][S16] | Visual narrative and exact evidence images for human review. |
| S17 | [Journey image provenance manifest][S17] | Exact image byte counts, dimensions, SHA-256 values, capture context, and mutation declarations. |
| S18 | [Architectural journey contract tests][S18] | Guards the article’s facts, local links, source hashes, image bytes, accessibility structure, and absence of ephemeral machine details. |

[S1]: ../creator-os.md
[S2]: https://github.com/djcdevelopment/baseline/blob/544c0dc26ed3cbb556f5467ca0169705fe1172f5/tools/selfie-stick/HANDOFF-RELATIONAL-ENVELOPE-V0-2026-08-29.md
[S3]: https://github.com/djcdevelopment/baseline/blob/544c0dc26ed3cbb556f5467ca0169705fe1172f5/tools/selfie-stick/probe_architectural_constraint_envelope.py
[S4]: https://github.com/djcdevelopment/baseline/blob/544c0dc26ed3cbb556f5467ca0169705fe1172f5/tools/selfie-stick/test_architectural_curriculum.py
[S5]: https://github.com/djcdevelopment/comfy-quest/blob/4842e42dca34bbe7660143fdd41487d9e63a21a8/src/Quest.Studio/QuestStudioEndpoints.cs
[S6]: https://github.com/djcdevelopment/comfy-quest/blob/4842e42dca34bbe7660143fdd41487d9e63a21a8/src/Quest.Studio/QuestStudioBuilds.cs
[S7]: https://github.com/djcdevelopment/comfy-quest/blob/4842e42dca34bbe7660143fdd41487d9e63a21a8/tools/blueprints/import_capture.py
[S8]: https://github.com/djcdevelopment/comfy-quest/blob/4842e42dca34bbe7660143fdd41487d9e63a21a8/tools/quest-studio/architectural_live_probe.py
[S9]: https://github.com/djcdevelopment/comfy-quest/blob/f2ce9a09c5070cbd6637cfff3c6002e8bdf22891/tools/quest-studio/Invoke-ArchitecturalDemo.ps1
[S10]: https://github.com/djcdevelopment/comfy-quest/blob/4842e42dca34bbe7660143fdd41487d9e63a21a8/src/Quest.Studio.E2E.Tests/QuestStudioSyntheticE2ETests.cs
[S11]: ../evidence/architectural-build-tn0304-20260829-r1.json
[S12]: ../evidence/architectural-live-tn0304-20260829-r1.json
[S13]: ../evidence/architectural-warm-tn0304-20260829-r1.json
[S14]: ../evidence/architectural-demo-tn0304-20260829-r1.json
[S15]: https://github.com/djcdevelopment/lumberjacks-platform/commit/2510553bddf1fbae6a4ea9c14ff923ca05b153d8
[S16]: ../architectural-build-journey-20260829.html
[S17]: ../images/architectural-build-journey/manifest.json
[S18]: ../../tests/test_architectural_journey_article.py

---

## 17. Final generator note

Favor concrete contrasts over a linear list of features:

- measured pitch versus game roof prefab;
- immutable geometry versus mutable placement intent;
- staging versus applying;
- cold reversibility versus warm reuse;
- a full desktop screenshot versus an exact game window;
- correct operations versus a receipt sourced from the wrong summary;
- many passing tests versus one standing structure that still diffs exactly;
- creator-seat judgment versus machine-observable work.

Return to the title near the end. The team did not stop rebuilding because the building stopped
mattering. It stopped rebuilding because the exact accepted building mattered enough to preserve.

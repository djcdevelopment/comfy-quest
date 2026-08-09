# Quest Lab event parser and Sheets export

Quest Lab's archive is the durable event ledger; this tool turns one session, a rotated
session, or a directory of sessions into a creator-readable report. It is local-only and
does not need Valheim, Google credentials, Python packages, or network access.

## Fast path

```powershell
python tools\questlab-events\questlab_events.py `
  "C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\config\comfy-quest-lab\event-archive" `
  --strict `
  --sheets captures\questlab-events.xlsx `
  --bundle captures\questlab-events-sheets.zip
```

Upload or open `questlab-events.xlsx` in Google Sheets. It contains frozen headers,
filters, useful column widths, and these tabs:

- **Events** — one row per canonical player action after stable-identity coalescing;
- **Summary** — total, school, and creator-event counts, including raw and coalesced totals;
- **Metadata** — UTC range, release IDs, applied filters, archive health, and privacy policy;
- **Raw Witnesses** — one row per unique archived sequence, without raw runtime identities;
- **Read Me** — an import and interpretation guide.

The zip adds UTF-8 CSV versions and the normalized JSON report for systems that do not
accept workbooks. It never contains macros, scripts, API keys, auth tokens, or an automatic
upload. That boundary is deliberate: the workbook is the safe, portable one-file handoff,
while a UI companion may open it or hand it to an authenticated Google integration later.

## Archive contract

Strict mode accepts schema `comfy-questlab-events/v1`:

- every JSONL segment starts with `recordType: "session"`, including its 1-based
  `segment`, UTC start, release, and privacy flags;
- event rows carry `sessionId`, positive `sequence`, UTC `timestampUtc`, `school`,
  `creatorEvent`, `target`, and `usability`; `detail`, `diagnosticSeam`, and
  `actionIdentity` are optional;
- clean shutdown may append `recordType: "sessionEnd"` to the final segment. Strict mode
  reconciles its release/profile/start identity, segment and event counts, dropped count, and
  final-record position before reporting the shutdown as clean. Missing retained segments are
  explicit partial/data-loss state rather than a false corruption verdict;
- bounded queue loss is explicit as `recordType: "archiveNotice"`, and the parser turns
  its cumulative dropped count into a visible `data_loss_detected` result;
- the paired CSV projection must use this exact RFC 4180 header:

```text
schema,session_id,sequence,timestamp_utc,school,creator_event,target,detail,usability,diagnostic_seam,action_identity
```

JSONL remains authoritative. If both JSONL and CSV are passed, equal `(sessionId,
sequence)` witnesses are recognized as mirrors and counted once. Conflicting mirror rows
fail with a hashed identity instead of silently choosing one.

A crash can leave one partial final JSONL line. The default is to fail and name its file,
line, and JSON column. `--allow-truncated-tail` explicitly skips only that incomplete last
line and marks the export as data-loss-affected. It never skips malformed complete rows.

Without `--strict`, the parser also understands older names such as `category`,
`eventName`, `at`, and `dedupeKey`. An ISO-8601 date and timezone are still required; a
wall-clock value such as `14:03:22` is not enough to merge or filter sessions safely.

## Filtering and output

Filters apply before action coalescing and are inclusive:

```powershell
# Combat and harvest actions involving a grey target in one UTC window.
python tools\questlab-events\questlab_events.py .\event-archive `
  --strict --school combat,harvest --target grey `
  --since 2026-08-09T16:00:00Z --until 2026-08-09T17:00:00Z

# Normalized JSON for a downstream parser.
python tools\questlab-events\questlab_events.py .\event-archive `
  --strict --event kill --format json --output captures\kills.json

# A single table rather than the workbook/bundle.
python tools\questlab-events\questlab_events.py .\event-archive `
  --strict --format csv --csv-view summary --output captures\summary.csv
```

`--school` and `--event` may be repeated or comma-separated. `--target` is a
case-insensitive substring match. CSV views are `actions`, `witnesses`, `summary`,
`metadata`, `event-summary`, and `school-summary`.

## Sheets companion column contract

The first three workbook tabs and their CSV counterparts are the stable companion
surface. Column order is intentional.

`Events` / `tables/events.csv`:

```text
timestamp_utc,last_timestamp_utc,school,creator_event,target,fields_json,usability,raw_witnesses,coalesced_witnesses,source_records,action_id,session_id,release_id
```

`Summary` / `tables/summary.csv`:

```text
level,school,creator_event,canonical_actions,raw_witnesses,coalesced_witnesses,distinct_targets,first_seen_utc,last_seen_utc
```

`Metadata` / `tables/metadata.csv`:

```text
key,value
```

`level` is `total`, `school`, or `event`. All timestamps are normalized UTC ISO-8601.
`fields_json` is canonical compact JSON. `action_id` and `session_id` are one-way SHA-256
aliases, not the raw runtime values. `--include-diagnostics` appends `detail` and
`diagnostic_seams` to Events; consumers should accept and ignore trailing columns they do
not use.

## Dedupe and privacy

This tool does not reimplement `QuestTriggerEvaluator`. The runtime remains responsible
for canonical names and safe action identity. The parser only:

1. removes equal JSONL/CSV mirrors by `(sessionId, sequence)`;
2. groups rows that share `(sessionId, creatorEvent, actionIdentity)`;
3. keeps rows without an action identity as distinct actions;
4. fails if one stable identity carries conflicting canonical payloads.

When an input actually carries multiple witnesses for one action, the report retains
`raw_witnesses`, `coalesced_witnesses`, and `source_records` instead of hiding that evidence.
The stock runtime archive is intentionally downstream of Quest Lab's local/RPC and overload
dedupe and normally records only the first creator-facing witness; it cannot reconstruct seams
that were suppressed before persistence. Use the exact live-suite receipt—not an event archive—
for transport-witness/coalescing proof.

By default, source paths, raw session IDs, raw action identities, detail, diagnostic
seams, and private-looking field names are absent. `--include-diagnostics` and
`--include-private-fields` are explicit opt-ins. CSV cells that could begin a Sheets or
Excel formula (`=`, `+`, `-`, `@`, or control whitespace) are forced to literal text; XLSX
cells are emitted as strings and receive the same defense.

Inputs are bounded to 256 files, 512 MiB total, 128 MiB per file, and 1,000,000 unique
event records. Files are streamed line-by-line before bounded normalization; explicit
local paths are allowed because this is a CLI, not a file-serving endpoint. JSON and
single-table CSV may use that full parser bound. In-memory workbook generation has a
separate 25,000-row-per-tab / 64 MiB expanded estimate ceiling; the multi-format zip uses
a 96 MiB expanded estimate ceiling. If either would be exceeded, the command fails before
building XML and tells the creator to filter or use normalized JSON/CSV instead.

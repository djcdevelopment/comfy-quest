# Quest Lab Sheets companion

Quest Lab keeps its normalized event history local and useful before Google enters the
picture. This companion validates a session's authoritative JSONL, produces a safe CSV,
and—after a one-time opt-in—turns one selected session into a polished Google workbook.

## The two-click setup, then one-click exports

1. Start the companion:

   ```powershell
   tools\questlab-sheets\Start-QuestLabSheets.ps1
   ```

   In the packaged Quest Lab zip, run `questlab-sheets\Start-QuestLabSheets.ps1`
   instead—the contents are identical and the launcher derives its files from its own folder.

   It binds only `127.0.0.1:47631`, opens the system browser, and discovers sessions under
   `BepInEx/config/comfy-quest-lab/event-archive`. The Quest Lab panel's **Exports** button
   opens this same fixed loopback page. CSV downloads work immediately.

2. To opt into Google, install the official client libraries:

   ```powershell
   python -m pip install -r tools\questlab-sheets\requirements-google.txt
   ```

   From the packaged Quest Lab zip, use
   `python -m pip install -r questlab-sheets\requirements-google.txt`.

3. In a Google Cloud project, enable the Google Sheets API, configure the OAuth consent
   screen, and create an OAuth client whose application type is **Desktop app**. Copy the
   downloaded JSON to the setup path shown by the dashboard (normally
   `%LOCALAPPDATA%\ComfyQuestLab\google-sheets\desktop-oauth-client.json`). Nothing is
   bundled in the mod or repository, and the companion refuses non-Google authorization
   and token endpoints.

4. Click **Connect Google** once. Authorization opens in the system browser, returns to a
   random `127.0.0.1` callback port, and uses PKCE plus OAuth state validation through
   Google's installed-app library. The only requested scope is
   `https://www.googleapis.com/auth/drive.file`: Quest Lab can create a workbook and edit
   that app-created file; it cannot browse or alter the rest of Drive.

After that setup, **Create Google Sheet** is one deliberate click. It creates a new
workbook, writes the selected normalized session, applies all formatting in one bounded
batch, saves a local receipt, and takes the browser directly to the new Sheet.

## What the workbook contains

- **Events** — exact normalized archive columns, frozen header, filter, and practical widths.
- **Summary** — total/bindable rows plus school and canonical-event counts.
- **Metadata** — schema, release, UTC boundaries, privacy-field switches, source filenames
  and SHA-256, per-file OAuth scope, and RAW write mode.

The exporter uses the Sheets API's `RAW` input option, so sign text, player-created names,
and other cells cannot become formulas. CSV adds a leading apostrophe to formula-shaped
text because desktop spreadsheet programs may otherwise interpret it on open. Google
requests stay below 1.5 MB, inside Google's current recommendation to keep payloads under
2 MB. There is never one network request per event row.

## Local parser

The parser has no optional dependencies and makes no network request:

```powershell
python tools\questlab-sheets\questlab_sheets.py inspect `
  "C:\...\event-archive\questlab-events-SESSION.jsonl" `
  "C:\...\event-archive\questlab-events-SESSION-part002.jsonl"

python tools\questlab-sheets\questlab_sheets.py to-csv `
  "C:\...\event-archive\questlab-events-SESSION.jsonl" `
  --output ".\questlab-events-SESSION.csv"

python tools\questlab-sheets\questlab_sheets.py doctor
```

JSONL is authoritative. Every part is hashed; headers, segment numbers, privacy fields,
release/profile identity, and session IDs must agree. `archiveNotice` queue-drop records and
the final `sessionEnd` are validated as integrity metadata, never mistaken for gameplay rows.
A clean end, clean end with drops, still-active/unclean tail, and retention-partial session
remain distinct states in both the dashboard and workbook. Missing retained segments may be
exported with a prominent warning; an unexplained sequence gap or malformed row is refused.
One broken session gets its own disabled card rather than hiding healthy sessions. Limits are
128 parts, 64 MiB per part, 250,000 events, and 256 KiB per JSONL row.

These archives contain the stable, post-deduplication creator events that quests actually
consume; they are intentionally not a raw Harmony/RPC/overload trace. Diagnostic seam and
action identity fields remain an explicit privacy opt-in. Use the bounded live-suite receipts
when transport-witness evidence is required. An interrupted writer may leave one incomplete,
unterminated final JSONL line: the parser hashes but does not export that crash tail, labels the
session active/unclean, and still rejects malformed newline-terminated rows or damage in an
earlier segment.

## Credential and network boundary

- The dashboard binds IPv4 loopback only, validates its fixed Host and Origin, uses a
  per-process CSRF token, sets a deny-by-default content security policy, and accepts only
  server-discovered session IDs. There is no URL field, file-path form, remote console,
  CORS access, or public listener.
- OAuth uses the system browser. Google's discontinued out-of-band copy/paste flow is not
  implemented. The callback listener uses a random loopback port.
- On Windows, the refresh token is encrypted with current-user DPAPI under Local App Data.
  Other platforms use an owner-only (`0600`) file. Tokens are never kept under the repo or
  BepInEx config and are never printed. **Disconnect and revoke** calls Google's fixed
  revocation endpoint, then deletes the local token even if revocation cannot be confirmed.
- No Google request occurs on startup, session discovery, `inspect`, `to-csv`, `doctor`, or
  CSV download. If dependencies, OAuth configuration, consent, network access, or a
  Workspace policy blocks Google, the local archive and CSV path remain fully functional.
- A write failure after workbook creation can leave a partial workbook. The error names its
  fixed Google Sheets link so the owner can inspect or delete it; the tool never retries into
  duplicate workbooks by itself.

## Enterprise deployment reality

A community or organization still needs to own its Google Cloud project, OAuth brand, and
Desktop client. Public OAuth applications may require Google verification, and Workspace
administrators can restrict third-party application access. Those are tenant/governance
decisions, not credentials Quest Lab can or should ship around. A denied tenant remains a
first-class local CSV experience rather than a broken exporter.

Official design sources (checked 2026-08-09):

- [OAuth 2.0 for Desktop Apps](https://developers.google.com/identity/protocols/oauth2/native-app)
  — system browser, random loopback IP listener, PKCE/state, and OOB deprecation.
- [OAuth best practices](https://developers.google.com/identity/protocols/oauth2/resources/best-practices)
  — PKCE, state validation, secure token storage, and no embedded user agent.
- [`spreadsheets.create`](https://developers.google.com/workspace/sheets/api/reference/rest/v4/spreadsheets/create)
  and [`spreadsheets.values.update`](https://developers.google.com/workspace/sheets/api/reference/rest/v4/spreadsheets.values/update)
  — `drive.file` is accepted for create/write.
- [Choose Google Drive API scopes](https://developers.google.com/workspace/drive/api/guides/api-specific-auth)
  — `drive.file` is the recommended non-sensitive, per-file scope.
- [Sheets `ValueInputOption`](https://developers.google.com/workspace/sheets/api/reference/rest/v4/ValueInputOption)
  — RAW values are not parsed and are stored as supplied.
- [Sheets API usage limits](https://developers.google.com/workspace/sheets/api/limits)
  — recommended request payload under 2 MB and bounded write quotas.
- [OAuth verification](https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification)
  — production brand/verification responsibilities.
- [Control which third-party and internal apps access Google Workspace data](https://support.google.com/a/answer/7281227)
  — administrators can restrict or block third-party OAuth applications.

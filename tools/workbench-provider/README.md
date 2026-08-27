# Comfy Quest provider for Isolate Workbench

This package contributes four bounded product-owned tools to an independently released
Isolate gateway:

- `quest_runtime_status` reads active-pack, dev-channel, world-entry, and run status, including
  the non-secret Creator Session identity required by later controls, while reducing participant
  identity to a count;
- `quest_runtime_receipts` reads and filters at most the bounded 512-file live receipt
  window without descending into another receipt store;
- `quest_runtime_creator_request` sends only the existing `status`, `arm`, `disarm`,
  `build_on`, and `build_off` Creator Session operations; and
- `quest_runtime_run_control` sends only the existing selection, nearby binding, and
  preview-confirmed reset operations, pinned to the Creator Session that completed world entry.

The provider is not a server and has no lifecycle, command, console, synthetic-input,
process, or arbitrary-path capability. Isolate owns gateway authentication, caller identity,
provider mounting, and teardown. Runtime remains the authority for exact machine/world/session
identity, private-world confirmation, pack membership, nearby object ownership, prerequisites,
reset tokens, recovery, and correlated receipts. The final released profile still owes a
per-lap generated credential rather than the installed operator-specific registry entry.

Build the release file from this repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\workbench-provider\New-QuestWorkbenchProvider.ps1
```

The output is a Python-importable zip plus a SHA-256 manifest under
`artifacts\quest-workbench-provider`. An Isolate release mounts that verified zip read-only,
adds the zip to `PYTHONPATH`, mounts only the required Valheim configuration root at a
release-declared container location, sets `COMFY_QUEST_RUNTIME_ROOT` to the fixed Runtime
subdirectory beneath that mount, passes the verified archive hash as
`COMFY_QUEST_PROVIDER_SHA256`, and includes `comfy_quest_workbench` in its explicit provider
allowlist. There is deliberately no machine-named or operator-named default path: the provider
fails closed when the release profile omits the mount identity. `quest_runtime_status` reports
the provider release hash but not the private mount path. The gateway must still attest its own
Isolate image, caller, profile, and provider identity before Quest relies on it.

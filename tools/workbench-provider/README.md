# Comfy Quest provider for Isolate Workbench

This package contributes five bounded product-owned tools to an independently released
Isolate gateway:

- `quest_runtime_status` reads active-pack, dev-channel, world-entry, and run status, including
  the non-secret Creator Session and durable binding identities required by later controls, while
  reducing participant identity to a count;
- `quest_runtime_receipts` reads and filters at most the bounded 512-file live receipt
  window without descending into another receipt store;
- `quest_runtime_creator_request` sends only the existing `status`, `arm`, `disarm`,
  `build_on`, and `build_off` Creator Session operations;
- `quest_runtime_run_control` sends only the existing selection, nearby binding, and
  preview-confirmed reset operations, pinned to the Creator Session that completed world entry;
  and
- `quest_lab_replay` hash-verifies and stages one release-mounted reviewed Godbuild pair, then
  obtains live Runtime `status` and `build_on` receipts before staging anything. The `build_on`
  operation rechecks the active Creator Session, loaded machine/world, and private-world safety
  setting and must report `creator_build_enabled=true`. Replay then performs a ground-only
  check/count/build/diff choreography through correlated Lab receipts. A loaded mark count plus
  mark-scoped diff makes completed or partial Lab mutation retries safe: an existing exact count
  and `MATCH` is already complete, only the Lab's canonical zero-count diagnostic may build, and
  partial, extra, or location-inconsistent state fails closed. Fresh replay succeeds only when
  the post-build diff reports exact `MATCH` for the same machine/world/session. If replay enabled
  creator build mode, it always sends and verifies `build_off` after the Lab attempt; if mode was
  already enabled, it preserves that prior state. Sky mode is refused because its support pieces
  are outside the reviewed capture and therefore cannot satisfy that zero-diff postcondition.

The provider is not a server and has no lifecycle, command, console, synthetic-input,
process, or arbitrary-path capability. Isolate owns gateway authentication, caller identity,
provider mounting, and teardown. Runtime remains the authority for exact machine/world/session
identity, private-world confirmation, pack membership, nearby object ownership, prerequisites,
reset tokens, recovery, and correlated receipts. Quest Lab remains the authority for blueprint
validation, build completion, durable marks, and zero-diff proof. The final released profile
still owes a per-lap generated credential rather than the installed operator-specific registry
entry.

Build the release file from this repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\workbench-provider\New-QuestWorkbenchProvider.ps1
```

The output is a Python-importable zip plus a SHA-256 manifest under
`artifacts\quest-workbench-provider`. An Isolate release:

- mounts that verified zip read-only and adds it to `PYTHONPATH`;
- mounts the required Valheim configuration root at a release-declared location, then sets
  `COMFY_QUEST_RUNTIME_ROOT` and `COMFY_QUEST_LAB_ROOT` to its fixed Runtime and Lab
  subdirectories;
- mounts a verified Quest release's reviewed Godbuild root read-only and sets
  `COMFY_QUEST_REVIEWED_GODBUILD_ROOT` to it;
- passes the verified provider archive hash as `COMFY_QUEST_PROVIDER_SHA256`; and
- includes `comfy_quest_workbench` in its explicit provider allowlist.

None of those paths is a tool argument. There is deliberately no machine-named or
operator-named default: the provider fails closed when the release profile omits a mount
identity, when the reviewed manifest or any declared artifact drifts, or when Runtime's entered
Creator Session does not match the requested machine/world/session. A stale or closed session and
a disabled private-world confirmation fail at live Runtime authority before Lab staging or
mutation. Replay results project bounded authority, cleanup, and Lab receipt evidence without
exporting private artifact paths. Timed-out requests are already expired before the tool returns;
the provider deliberately leaves the fixed mailbox for Runtime or Lab to claim and reject. That
fail-closed mailbox remains occupied until its authority consumes it, so the provider never races
a successor by comparing and deleting the shared path. `quest_runtime_status` reports the
provider release hash but not any private mount path. The gateway must still attest its own
Isolate image, caller, profile, provider, reviewed-artifact release, and lifecycle before Quest
relies on it.

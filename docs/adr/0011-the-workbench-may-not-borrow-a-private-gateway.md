# 0011 — The development Workbench may not borrow a private operator gateway

Status: accepted 2026-08-24.

## Context

Quest's development MCP surface is an Isolate Workbench. Isolate owns the standalone turnkey
Workbench and the development MCP runtime; Quest contributes only bounded product-owned
commands and receipts.

The tempting shortcut is to point Quest at a private operator gateway that already runs on the
maintainer's machine. It responds on localhost, it is already authenticated, and it would work
today. That is precisely the failure this record forecloses.

Two reasons it must not happen:

**A responsive localhost port is not an identity.** A healthy port proves something is
listening. It does not prove which project, which image, which profile, which provider
allowlist, or which caller. A gateway that answers is not the same as a gateway that has
attested what it is.

**A volunteer cannot reproduce a private fleet.** The whole point of the standalone Workbench
is that it stands up on someone else's machine from released artifacts alone. Any discovery
path, fallback, credential, hostname, path, or retained runtime reference to a private
operator's topology makes the volunteer profile unreproducible — and it will not fail loudly,
it will fail only for people who are not the maintainer.

This mirrors the repository boundary already in force: integration happens through published
packages and verified release files, never a sibling checkout. `tools/assert_no_reach_in.py`
enforces the filesystem half of that in CI. This record covers the network and identity half,
which no scan catches.

## Decision

**The development MCP must be an independently released Isolate Workbench that can stand up on
any volunteer machine. It may not discover, import, fall back to, share credentials with, share
state with, or carry a runtime reference to any private operator gateway or fleet topology.**

Before Quest relies on that surface, a clean-machine acceptance must prove the exact Isolate
project identity, immutable source/image identity, profile, provider allowlist, generated local
caller credentials, bounded state roots, and teardown.

## Consequences

- **Never connect by port alone.** The gateway must attest the exact standalone Isolate
  release. A health check is orientation, not proof.
- The volunteer profile may not depend on a specific operator, a specific machine, private
  hostnames, private paths, sibling checkouts, or historical fleet defaults.
- Quest does not clone the MCP kernel or a Valheim lifecycle harness into this repository in
  order to satisfy this record. Bounded product-owned commands and receipts only.
- The Workbench boundary currently sits in the queue as `ready` with **no lane and no exit
  gate**, while `docs/creator-os.md`'s roadmap item 3 treats it as a precondition for the
  autonomous integration that lane 4A's exit depends on. That gap is a scheduling defect, not a
  disagreement with this record, and it is what the program invariant's disposition checks are
  meant to catch.

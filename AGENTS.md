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

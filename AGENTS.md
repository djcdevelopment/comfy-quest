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

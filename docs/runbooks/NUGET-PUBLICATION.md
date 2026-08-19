# NuGet publication and public repin

This lane publishes the two public packages in dependency order. It is intentionally
inactive until the repository's `nuget-production` environment has a
`NUGET_API_KEY` secret and the publisher controls both package IDs.

The committed development state remains:

- `Comfy.Quest.Contracts` and `Comfy.Quest.Studio` producer version
  `0.3.0-local`;
- every Contracts consumer pinned exactly to `[0.3.0-local]`; and
- `packages-local` first in `nuget.config`.

Verify that state at any time:

```powershell
python tools\nuget\repin_public.py --check-interim
```

## First publication

Before creating a tag, confirm `main` is green, the working tree is clean, the two
NuGet package IDs are available to this publisher, and the environment secret is
configured. The future publication command is:

```powershell
git tag nuget-v0.3.0
git push origin nuget-v0.3.0
```

Do not run those commands as a rehearsal: a NuGet version is immutable. The
`publish-nuget.yml` workflow performs these ordered gates:

1. derive one stable version from the tag and require publication authority;
2. pack and strictly inspect `Comfy.Quest.Contracts`;
3. push Contracts, then poll NuGet.org for the downloadable public bytes;
4. reject a wrong ID, version, repository URL/commit, dependency, or payload entry;
5. restore Studio from a NuGet.org-only configuration with Contracts pinned exactly
   to `[0.3.0]`;
6. only then pack, inspect, and push `Comfy.Quest.Studio`; and
7. poll and inspect the public Studio package, including its exact Contracts
   dependency.

If an existing immutable package causes a duplicate push to be skipped, the
downloaded package still has to pass the same identity and payload checks. A bad
public byte set stops the lane; it is never treated as success.

## Repin after both packages are public

Do not hand-edit the pins or remove `packages-local`. From a clean checkout of the
intended public commit, run:

```powershell
python tools\nuget\repin_public.py --apply --version 0.3.0
python tools\nuget\repin_public.py --check --version 0.3.0
dotnet restore network\mod\ComfyQuestLab.Tests\ComfyQuestLab.Tests.csproj
dotnet restore src\Quest.Studio\Quest.Studio.csproj
```

The apply command first downloads and validates both public packages. Only then does
it atomically replace the producer versions and exact consumer pins, switch
`nuget.config` to NuGet.org only, and remove the three allowlisted interim feed
files. It refuses an unexpected local-feed payload.

Review and commit that repin as a separate change so downstream repositories have
one clear public-package commitment point.

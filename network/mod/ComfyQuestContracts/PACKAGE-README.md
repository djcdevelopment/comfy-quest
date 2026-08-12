# Comfy Quest Contracts

`Comfy.Quest.Contracts` is the versioned boundary shared by Comfy Quest Runtime,
Quest Lab, Quest Studio, and their platform hosts. It contains the compiled
experience/runtime contract plus the six Quest ModGlue sources as NuGet
`contentFiles`.

Mod consumers that need the glue compile those sources into their single BepInEx
assembly and exclude this package's compile/runtime assets. Host applications such
as Quest Studio consume the compiled contract and exclude `contentFiles`.

Use an exact package version. The source, license, release procedure, and package
payload validator live in the
[Comfy Quest repository](https://github.com/djcdevelopment/comfy-quest).

This package is licensed under the Business Source License 1.1; see the repository
license for its parameters and change date.

# Comfy Quest Studio

`Comfy.Quest.Studio` is a .NET 9 class library that adds the loopback Quest
authoring, certification, history, and Runtime inbox publication surface to a host
application. It is not a standalone web server.

The host implements `IQuestStudioHost`, maps `QuestStudioEndpoints`, and remains
responsible for loopback/browser-token authorization, durable state, and locating
the local Valheim installation. Studio communicates with gameplay only by creating
validated, data-only `.questpack` files for Comfy Quest Runtime.

A host may also implement `IQuestStudioStewardHost` to enable the private Creator/DM scene. The
connection contains separate Steward scene and Viewer origins plus a server-only operator token;
Studio never returns that credential to browser code. Measured scenes and the pinned Steward WebGPU
renderer are exchanged as versioned artifacts, with no sibling-repository source dependency.

Every Studio release depends on the exact matching
`Comfy.Quest.Contracts` version. The source, integration boundary, release
procedure, and package payload validator live in the
[Comfy Quest repository](https://github.com/djcdevelopment/comfy-quest).

This package is licensed under the Business Source License 1.1; see the repository
license for its parameters and change date.

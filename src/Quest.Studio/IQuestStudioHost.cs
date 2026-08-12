using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Comfy.Quest.Studio;

/// <summary>
/// The surface Quest Studio needs from its host process. Per
/// docs/quest-studio-runtime-boundary.md, Companion stays the host and .questpack files
/// remain the runtime handoff — this interface is the seam that lets Quest Studio compile
/// and ship as its own package (Comfy.Quest.Studio) without a source dependency on
/// Game.Companion's internals (WorkbenchStore, ValheimLocator, WorkbenchService, Json).
/// </summary>
public interface IQuestStudioHost
{
    /// <summary>
    /// Durable state root Quest Studio creates its own "quest-studio" subdirectory under.
    /// Mirrors WorkbenchStore.RootDirectory.
    /// </summary>
    string StateDirectory { get; }

    /// <summary>Locates the local Valheim install, or null if none is found. Mirrors ValheimLocator.Find().</summary>
    string? FindValheim();

    /// <summary>
    /// Confined-loopback-origin + browser-token authorization check for browser-originated
    /// mutations. Mirrors WorkbenchService.BrowserMutationAllowed(HttpRequest).
    /// </summary>
    bool Authorize(HttpRequest request);

    /// <summary>
    /// The host's canonical JsonSerializerOptions (snake_case, indented, case-insensitive) so
    /// Studio's persisted project/history state and API bodies match Companion's conventions.
    /// Mirrors the Json.Options used everywhere else in Companion.
    /// </summary>
    JsonSerializerOptions Json { get; }
}

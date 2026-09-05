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

/// <summary>Host-owned connection to Steward's private authoring surface. The operator
/// credential stays in the server process and is never returned by a Studio endpoint.</summary>
public sealed record QuestStudioStewardConnection(
    Uri SceneOrigin,
    Uri ViewerOrigin,
    string OperatorToken);

public interface IQuestStudioStewardHost
{
    QuestStudioStewardConnection? StewardConnection { get; }
}

/// <summary>Optional sovereign-repository capability used only by the local R&amp;D campaign lap.
/// Package hosts that do not own the comfy-quest checkout simply omit it; Studio never reaches
/// into a sibling repository or guesses a source root.</summary>
public interface IQuestStudioRAndDHost
{
    string? RepositoryRoot { get; }
}

/// <summary>
/// Exact, host-owned entrypoint for the architectural capture importer. Package hosts omit
/// this capability and the Build workspace fails closed instead of carrying a second importer.
/// A standalone bundle manifest is optional for a normal sovereign checkout and mandatory for
/// a staged source-less R&amp;D bundle.
/// </summary>
public sealed record QuestStudioArchitecturalImporter(
    string ScriptPath,
    string PythonExecutable,
    string? StandaloneBundleManifestPath);

public interface IQuestStudioArchitecturalRAndDHost
{
    QuestStudioArchitecturalImporter? ArchitecturalImporter { get; }
}

using System.Reflection;

namespace Comfy.Quest.Studio;

internal static class QuestStudioCreatorRenderer
{
    public const string ArtifactSha256 = "a31e36330c35325ccd40f4e0e36fa26636692f970a2e6872dda7da37460a0eb4";
    static readonly Lazy<string> Source = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Comfy.Quest.Studio.CreatorSceneRenderer.js")
            ?? throw new InvalidOperationException("Creator renderer artifact is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string Js => Source.Value;
}

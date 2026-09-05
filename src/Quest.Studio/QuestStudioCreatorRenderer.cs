using System.Reflection;

namespace Comfy.Quest.Studio;

internal static class QuestStudioCreatorRenderer
{
    public const string ArtifactSha256 = "362d1259476d902192a72c0368ee771f381a5c02c08ff212c53a2a52cc84682e";
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

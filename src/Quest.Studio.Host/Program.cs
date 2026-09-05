using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Comfy.Quest.Studio;

var port = ReadPort(args);
var stateDirectory = Environment.GetEnvironmentVariable("COMFY_QUEST_STUDIO_STATE");
if (string.IsNullOrWhiteSpace(stateDirectory))
    stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComfyQuest");

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};
var host = new StandaloneQuestStudioHost(
    stateDirectory, json, port,
    Environment.GetEnvironmentVariable("COMFY_QUEST_REPO_ROOT"));
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = json.PropertyNamingPolicy;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.WriteIndented = true;
});
builder.Services.AddSingleton<IQuestStudioHost>(host);
builder.Services.AddSingleton(host);
builder.Services.AddSingleton<QuestPackPublisher>();
builder.Services.AddSingleton<QuestStudioService>();
builder.Services.AddSingleton<QuestStudioBuildService>();

var app = builder.Build();
app.MapGet("/", () => Results.Redirect("/quest-studio"));
app.MapGet("/workbench", () => Results.Redirect("/quest-studio"));
app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    service = "comfy-quest-studio",
    binding = $"127.0.0.1:{port}"
}, json));
app.MapGet("/api/v1/workbench/security", (HttpRequest request, HttpResponse response) =>
{
    response.Headers.CacheControl = "no-store";
    response.Headers.Pragma = "no-cache";
    response.Headers["X-Content-Type-Options"] = "nosniff";
    return host.IsLoopbackRequest(request)
        ? Results.Json(new { browser_token = host.BrowserToken }, json)
        : Results.StatusCode(StatusCodes.Status403Forbidden);
});
QuestStudioEndpoints.Map(app, host);
app.Run();

static int ReadPort(string[] arguments)
{
    var configured = Environment.GetEnvironmentVariable("COMFY_QUEST_STUDIO_PORT");
    for (var index = 0; index < arguments.Length - 1; index++)
        if (arguments[index] == "--port") configured = arguments[index + 1];
    return int.TryParse(configured, out var value) && value is >= 1024 and <= 65535 ? value : 8085;
}

sealed class StandaloneQuestStudioHost : IQuestStudioHost, IQuestStudioRAndDHost,
    IQuestStudioArchitecturalRAndDHost, IQuestStudioStewardHost
{
    readonly int _port;
    readonly byte[] _tokenBytes;

    public StandaloneQuestStudioHost(string stateDirectory, JsonSerializerOptions json, int port, string? repositoryRoot)
    {
        StateDirectory = Path.GetFullPath(stateDirectory);
        Directory.CreateDirectory(StateDirectory);
        Json = json;
        RepositoryRoot = string.IsNullOrWhiteSpace(repositoryRoot) ? null : Path.GetFullPath(repositoryRoot);
        _port = port;
        _tokenBytes = RandomNumberGenerator.GetBytes(32);
        BrowserToken = Convert.ToHexString(_tokenBytes).ToLowerInvariant();
    }

    public string StateDirectory { get; }
    public string BrowserToken { get; }
    public JsonSerializerOptions Json { get; }
    public string? RepositoryRoot { get; }

    public QuestStudioStewardConnection? StewardConnection
    {
        get
        {
            var scene = Environment.GetEnvironmentVariable("COMFY_QUEST_STEWARD_SCENE_URL");
            var viewer = Environment.GetEnvironmentVariable("COMFY_QUEST_STEWARD_VIEWER_URL");
            var token = Environment.GetEnvironmentVariable("COMFY_QUEST_STEWARD_TOKEN");
            return Uri.TryCreate(scene, UriKind.Absolute, out var sceneUri)
                && Uri.TryCreate(viewer, UriKind.Absolute, out var viewerUri)
                && !string.IsNullOrWhiteSpace(token)
                ? new(sceneUri, viewerUri, token) : null;
        }
    }

    public QuestStudioArchitecturalImporter? ArchitecturalImporter
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RepositoryRoot)) return null;
            var script = Path.Combine(RepositoryRoot, "tools", "blueprints", "import_capture.py");
            var python = Environment.GetEnvironmentVariable("COMFY_QUEST_PYTHON");
            if (string.IsNullOrWhiteSpace(python)) python = OperatingSystem.IsWindows() ? "python" : "python3";
            var bundle = Environment.GetEnvironmentVariable("COMFY_QUEST_RND_BUNDLE_MANIFEST");
            return new(script, python, string.IsNullOrWhiteSpace(bundle) ? null : Path.GetFullPath(bundle));
        }
    }

    public string? FindValheim()
    {
        var configured = Environment.GetEnvironmentVariable("COMFY_VALHEIM_DIR");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return Path.GetFullPath(configured);
        var conventional = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Valheim");
        return Directory.Exists(conventional) ? conventional : null;
    }

    public bool Authorize(HttpRequest request)
    {
        if (!IsLoopbackRequest(request)) return false;
        var supplied = request.Headers["X-Workbench-Token"].ToString();
        byte[] decoded;
        try { decoded = Convert.FromHexString(supplied); }
        catch { return false; }
        return decoded.Length == _tokenBytes.Length && CryptographicOperations.FixedTimeEquals(decoded, _tokenBytes);
    }

    public bool IsLoopbackRequest(HttpRequest request)
    {
        var remote = request.HttpContext.Connection.RemoteIpAddress;
        if (remote is not null && !IPAddress.IsLoopback(remote)) return false;
        var host = request.Host.Host;
        if (host is not ("127.0.0.1" or "localhost" or "[::1]" or "::1")) return false;
        if (request.Host.Port is int requestPort && requestPort != _port) return false;
        if (!request.Headers.TryGetValue("Origin", out var origins) || origins.Count == 0) return true;
        return origins.All(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttp && uri.Port == _port &&
            (uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "[::1]" || uri.Host == "::1"));
    }
}

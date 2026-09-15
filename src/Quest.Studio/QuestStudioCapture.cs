using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Comfy.Quest.Studio;

internal static class QuestStudioCapture
{
    static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(120) };
    static readonly Lazy<Dictionary<string, byte[]>> Assets = new(LoadAssets);
    static Dictionary<string, byte[]> LoadAssets()
    {
        byte[] Read(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Comfy.Quest.Studio.CaptureComposer." + name)
                ?? throw new InvalidOperationException("Pinned capture composer asset missing: " + name);
            using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
        }
        using var manifest = JsonDocument.Parse(Read("manifest.json"));
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var row in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var name = row.GetProperty("path").GetString()!;
            var bytes = Read(name);
            if (bytes.Length != row.GetProperty("bytes").GetInt32() ||
                Convert.ToHexStringLower(SHA256.HashData(bytes)) != row.GetProperty("sha256").GetString())
                throw new InvalidOperationException("Capture composer integrity mismatch: " + name);
            assets.Add(name, bytes);
        }
        return assets;
    }
    public static void Map(WebApplication app, IQuestStudioHost host)
    {
        app.MapGet("/quest-studio/capture-mode.js", () => Results.Text(ModeScript, "text/javascript"));
        app.MapGet("/quest-studio/capture/{asset}", (string asset) =>
            Assets.Value.TryGetValue(asset, out var data) ? Results.Bytes(data, asset.EndsWith(".css") ? "text/css" : asset.EndsWith(".json") ? "application/json" : "text/javascript") : Results.NotFound());
        app.MapGet("/api/v2/quest-studio/captures", (Func<HttpContext, Task<IResult>>)(context => Forward(context, host, "", false)));
        app.MapGet("/api/v2/quest-studio/captures/{id}", (HttpContext context, string id) => Forward(context, host, id, false));
        app.MapGet("/api/v2/quest-studio/captures/{id}/scene", (HttpContext context, string id) => Forward(context, host, id + "/scene", false));
        app.MapPost("/api/v2/quest-studio/captures/{id}/export", (HttpContext context, string id) => Forward(context, host, id + "/export", true));
    }
    const string ModeScript = """
import {mountCaptureComposer} from '/quest-studio/capture/capture-composer.js';
const capture=document.getElementById('creator-capture-workspace'),gameplay=document.querySelector('.creator-mode');
const photoButton=document.getElementById('creator-capture-mode'),gameButton=document.getElementById('creator-gameplay-mode');
let component;
let captureToken;
async function captureFetch(url,options={},retry=true){
  if(!captureToken){const security=await fetch('/api/v1/workbench/security',{cache:'no-store'});if(!security.ok)throw Error('Studio authorization is unavailable.');captureToken=(await security.json()).browser_token;}
  const result=await fetch(url,{...options,headers:{...options.headers,'X-Workbench-Token':captureToken}});
  if(result.status===403&&retry){captureToken=null;return captureFetch(url,options,false);}return result;
}
photoButton.addEventListener('click',async()=>{
  capture.hidden=false;gameplay.hidden=true;photoButton.setAttribute('aria-pressed','true');gameButton.setAttribute('aria-pressed','false');
  component??=mountCaptureComposer(capture,{apiBase:'/api/v2/quest-studio/captures',fetcher:captureFetch});
  window.studioCaptureComposer=await component;
});
gameButton.addEventListener('click',()=>{capture.hidden=true;gameplay.hidden=false;photoButton.setAttribute('aria-pressed','false');gameButton.setAttribute('aria-pressed','true');});
""";
    static async Task<IResult> Forward(HttpContext context, IQuestStudioHost host, string suffix, bool export)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!host.Authorize(context.Request)) return Results.StatusCode(403);
        var connection = (host as IQuestStudioStewardHost)?.StewardConnection;
        if (connection is null) return Results.Json(new { error = "Steward photography is not connected." }, statusCode: 503);
        if (suffix.Length > 200 || suffix.Split('/').Any(part => part.Length == 0 || !part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            if (suffix.Length != 0) return Results.BadRequest(new { error = "Invalid photograph identity." });
        }
        var uri = new Uri(connection.SceneOrigin, "api/captures" + (suffix.Length == 0 ? "" : "/" + suffix));
        using var request = new HttpRequestMessage(export ? HttpMethod.Post : HttpMethod.Get, uri);
        if (export)
        {
            var block = new byte[8193]; int count = await context.Request.Body.ReadAtLeastAsync(block, 8193, false, context.RequestAborted);
            if (count > 8192) return Results.BadRequest(new { error = "Capture request is too large." });
            request.Content = new ByteArrayContent(block, 0, count); request.Content.Headers.ContentType = new("application/json");
        }
        try
        {
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            const int limit = 48 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) return Results.Json(new { error = "Capture response is too large." }, statusCode: 502);
            using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
            using var output = new MemoryStream(); var block = new byte[65536]; int read;
            while ((read = await stream.ReadAsync(block, context.RequestAborted)) != 0)
            {
                if (output.Length + read > limit) return Results.Json(new { error = "Capture response is too large." }, statusCode: 502);
                output.Write(block, 0, read);
            }
            if (!response.IsSuccessStatusCode)
                return Results.Content(System.Text.Encoding.UTF8.GetString(output.ToArray()), response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
            // The same Steward endpoint creates both downloads; Studio does not reinterpret the camera.
            return Results.Bytes(output.ToArray(), response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", export ? "selfiestick-capture.zip" : null);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        { return Results.Json(new { error = "Steward photography is unavailable. Try again when the archived scene service is connected." }, statusCode: 503); }
    }
}

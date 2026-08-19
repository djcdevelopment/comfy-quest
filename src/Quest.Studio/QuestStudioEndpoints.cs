using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Comfy.Quest.Studio;

/// <summary>
/// Route-for-route port of the Quest Studio endpoints formerly mapped inline by
/// Lumberjacks.Companion's WorkbenchEndpoints.Map (WorkbenchKernel.cs:771-798 and 832-838).
/// Companion calls this once, right after WorkbenchEndpoints.Map(app), passing its
/// IQuestStudioHost adapter. QuestStudioService/QuestPackPublisher are resolved from the
/// same DI container Companion registers them in — same pattern the original endpoints used
/// for WorkbenchService/QuestStudioService/QuestPackPublisher.
/// </summary>
public static class QuestStudioEndpoints
{
    public static void Map(WebApplication app, IQuestStudioHost host)
    {
        app.MapGet("/quest-studio", () => Results.Text(QuestStudioPage.Html, "text/html", Encoding.UTF8));
        app.MapGet("/quest-studio/studio.css", () => Results.Text(QuestStudioPage.Css, "text/css", Encoding.UTF8));
        app.MapGet("/quest-studio/studio.js", () => Results.Text(QuestStudioPage.Js, "text/javascript", Encoding.UTF8));

        app.MapGet("/api/v2/quest-studio/catalog", (QuestStudioService studio) => Results.Json(studio.WorkspaceCatalog(), host.Json));
        app.MapGet("/api/v2/quest-studio/projects", (HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            return !host.Authorize(request)
                ? Forbidden(host)
                : Results.Json(new { schema_version = 2, projects = studio.ListProjects() }, host.Json);
        });
        app.MapPost("/api/v2/quest-studio/projects", (HttpRequest request, HttpResponse response, StudioCreateRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            return Results.Json(studio.CreateProject(body?.TemplateId), host.Json, statusCode: StatusCodes.Status201Created);
        });
        app.MapGet("/api/v2/quest-studio/projects/{projectId}", (string projectId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var project = studio.ReadProject(projectId);
            return project is null ? Results.NotFound() : Results.Json(project, host.Json);
        });
        app.MapPut("/api/v2/quest-studio/projects/{projectId}", (string projectId, HttpRequest request, HttpResponse response, StudioSaveRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.SaveDraft(projectId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/duplicate", (string projectId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var project = studio.DuplicateProject(projectId);
            return project is null ? Results.NotFound() : Results.Json(project, host.Json, statusCode: StatusCodes.Status201Created);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/bump-patch", (string projectId, HttpRequest request, HttpResponse response, StudioBumpRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.BumpPatch(projectId, body?.ExpectedRevision ?? -1);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/validate", (string projectId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.ValidateGraph(projectId);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/certify", (string projectId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.CertifyGraph(projectId);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/rehearse", (string projectId, HttpRequest request, HttpResponse response, StudioRehearsalRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.Rehearse(projectId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/publish", async (string projectId, HttpRequest request, HttpResponse response, StudioPublishRequest? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.PublishGraphAsync(projectId, body?.ExpectedRevision ?? -1, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/play", async (string projectId, HttpRequest request, HttpResponse response, StudioPublishRequest? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.PlayRevisionAsync(projectId, body?.ExpectedRevision ?? -1, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        });
        app.MapGet("/api/v2/quest-studio/projects/{projectId}/history", (string projectId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            return !host.Authorize(request) ? Forbidden(host) : Results.Json(studio.ProjectHistory(projectId), host.Json);
        });
        app.MapGet("/api/v2/quest-studio/projects/{projectId}/runtime-status", (string projectId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            return !host.Authorize(request) ? Forbidden(host) : Results.Json(studio.RuntimeStatusView(projectId), host.Json);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/export", (string projectId, HttpRequest request, HttpResponse response, StudioExportRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            return Download(response, studio.ExportProject(projectId, body), host);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/questpack", (string projectId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            return Download(response, studio.DownloadQuestpack(projectId), host);
        });

        app.MapGet("/api/v2/quest-studio/usage", (HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            return !host.Authorize(request) ? Forbidden(host) : Results.Json(studio.UsageReport(), host.Json);
        });
        app.MapPut("/api/v2/quest-studio/usage/settings", (HttpRequest request, HttpResponse response, StudioUsageSettingsRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            if (body is null) return Results.Json(new { error = "usage_settings_required" }, host.Json, statusCode: StatusCodes.Status400BadRequest);
            var result = studio.SetUsageEnabled(body.Enabled);
            return Results.Json(result, host.Json, statusCode: result.SettingsAvailable ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });
        app.MapPost("/api/v2/quest-studio/usage/export", (HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            return Download(response, studio.ExportUsage(), host);
        });
        app.MapPost("/api/v2/quest-studio/usage/reset", (HttpRequest request, HttpResponse response, StudioUsageResetRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.ResetUsage(body?.Confirm == true);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Error == "usage_storage_unavailable" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status400BadRequest);
        });

        app.MapGet("/api/v1/workbench/quest-studio/project", (HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            return !host.Authorize(request) ? Forbidden(host) : Results.Json(studio.Read(), host.Json);
        });
        app.MapGet("/api/v1/workbench/quest-studio/events", (QuestStudioService studio) => Results.Json(studio.Events(), host.Json));
        app.MapGet("/api/v1/workbench/quest-studio/receipts", (HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            return !host.Authorize(request) ? Forbidden(host) : Results.Json(studio.Receipts(), host.Json);
        });
        app.MapGet("/api/v1/workbench/quest-studio/history", (HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            return !host.Authorize(request) ? Forbidden(host) : Results.Json(studio.History(), host.Json);
        });
        app.MapGet("/api/v1/workbench/quest-studio/diff", (string? from, string? to, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.Diff(from, to);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v1/workbench/quest-studio/save", (HttpRequest request, HttpResponse response, QuestStudioProject? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.Save(body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v1/workbench/quest-studio/certify", (HttpRequest request, HttpResponse response, QuestStudioProject? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.Certify(body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v1/workbench/quest-studio/publish-project", async (HttpRequest request, HttpResponse response, QuestStudioProject? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.PublishAsync(body, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v1/workbench/quest-studio/publish", async (HttpRequest request, HttpResponse response, QuestPackPublisher publisher, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var filename = request.Headers["X-Questpack-Filename"].ToString();
            var receipt = await publisher.PublishAsync(request.Body, filename, cancellationToken);
            return Results.Json(receipt, host.Json, statusCode: receipt.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
    }

    static IResult Forbidden(IQuestStudioHost host) => Results.Json(
        new { error = "browser_authorization_required", detail = "Refresh the loopback browser token and retry." },
        host.Json,
        statusCode: StatusCodes.Status403Forbidden);

    static IResult Download(HttpResponse response, StudioDownloadResult result, IQuestStudioHost host)
    {
        NoStore(response);
        if (!result.Ok || result.Bytes is null || result.Filename is null || result.ContentType is null)
        {
            var status = result.Error == "project_missing"
                ? StatusCodes.Status404NotFound
                : result.Error is "export_entry_limit" or "export_size_limit" or "package_too_large"
                    ? StatusCodes.Status413PayloadTooLarge
                    : StatusCodes.Status400BadRequest;
            return Results.Json(new { error = result.Error ?? "download_failed" }, host.Json, statusCode: status);
        }
        response.Headers["X-Content-Type-Options"] = "nosniff";
        if (!string.IsNullOrWhiteSpace(result.Sha256)) response.Headers["X-Content-SHA256"] = result.Sha256;
        if (!string.IsNullOrWhiteSpace(result.ContentHash)) response.Headers["X-Comfy-Content-Hash"] = result.ContentHash;
        return Results.File(result.Bytes, result.ContentType, result.Filename, enableRangeProcessing: false);
    }

    static void NoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
    }
}

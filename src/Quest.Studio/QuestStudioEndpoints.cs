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
        app.MapGet("/api/v2/quest-studio/projects", (QuestStudioService studio) => Results.Json(new { schema_version = 2, projects = studio.ListProjects() }, host.Json));
        app.MapPost("/api/v2/quest-studio/projects", (HttpRequest request, StudioCreateRequest? body, QuestStudioService studio) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            return Results.Json(studio.CreateProject(body?.TemplateId), host.Json, statusCode: StatusCodes.Status201Created);
        });
        app.MapGet("/api/v2/quest-studio/projects/{projectId}", (string projectId, QuestStudioService studio) =>
        {
            var project = studio.ReadProject(projectId);
            return project is null ? Results.NotFound() : Results.Json(project, host.Json);
        });
        app.MapPut("/api/v2/quest-studio/projects/{projectId}", (string projectId, HttpRequest request, StudioSaveRequest? body, QuestStudioService studio) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.SaveDraft(projectId, body);
            return result.Ok ? Results.Ok(result) : result.Conflict ? Results.Json(result, host.Json, statusCode: StatusCodes.Status409Conflict) : Results.BadRequest(result);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/duplicate", (string projectId, HttpRequest request, QuestStudioService studio) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            var project = studio.DuplicateProject(projectId);
            return project is null ? Results.NotFound() : Results.Json(project, host.Json, statusCode: StatusCodes.Status201Created);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/bump-patch", (string projectId, HttpRequest request, StudioBumpRequest? body, QuestStudioService studio) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.BumpPatch(projectId, body?.ExpectedRevision ?? -1);
            return result.Ok ? Results.Ok(result) : result.Conflict ? Results.Json(result, host.Json, statusCode: StatusCodes.Status409Conflict) : Results.BadRequest(result);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/validate", (string projectId, HttpRequest request, QuestStudioService studio) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.ValidateGraph(projectId);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/certify", (string projectId, HttpRequest request, QuestStudioService studio) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.CertifyGraph(projectId);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/rehearse", (string projectId, HttpRequest request, StudioRehearsalRequest? body, QuestStudioService studio) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.Rehearse(projectId, body);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/publish", async (string projectId, HttpRequest request, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.PublishGraphAsync(projectId, cancellationToken);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        });
        app.MapGet("/api/v2/quest-studio/projects/{projectId}/history", (string projectId, QuestStudioService studio) => Results.Json(studio.ProjectHistory(projectId), host.Json));
        app.MapGet("/api/v2/quest-studio/projects/{projectId}/runtime-status", (string projectId, QuestStudioService studio) => Results.Json(studio.RuntimeStatus(projectId), host.Json));

        app.MapGet("/api/v1/workbench/quest-studio/project", (QuestStudioService studio) => Results.Json(studio.Read(), host.Json));
        app.MapGet("/api/v1/workbench/quest-studio/events", (QuestStudioService studio) => Results.Json(studio.Events(), host.Json));
        app.MapGet("/api/v1/workbench/quest-studio/receipts", (QuestStudioService studio) => Results.Json(studio.Receipts(), host.Json));
        app.MapGet("/api/v1/workbench/quest-studio/history", (QuestStudioService studio) => Results.Json(studio.History(), host.Json));
        app.MapGet("/api/v1/workbench/quest-studio/diff", (string? from, string? to, QuestStudioService studio) =>
        {
            var result = studio.Diff(from, to);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        });
        app.MapPost("/api/v1/workbench/quest-studio/save", (HttpRequest request, QuestStudioProject? body, QuestStudioService studio) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.Save(body);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        });
        app.MapPost("/api/v1/workbench/quest-studio/certify", (HttpRequest request, QuestStudioProject? body, QuestStudioService studio) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.Certify(body);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        });
        app.MapPost("/api/v1/workbench/quest-studio/publish-project", async (HttpRequest request, QuestStudioProject? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.PublishAsync(body, cancellationToken);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        });
        app.MapPost("/api/v1/workbench/quest-studio/publish", async (HttpRequest request, QuestPackPublisher publisher, CancellationToken cancellationToken) =>
        {
            if (!host.Authorize(request)) return Forbidden(host);
            var filename = request.Headers["X-Questpack-Filename"].ToString();
            var receipt = await publisher.PublishAsync(request.Body, filename, cancellationToken);
            return receipt.Ok ? Results.Ok(receipt) : Results.BadRequest(receipt);
        });
    }

    static IResult Forbidden(IQuestStudioHost host) => Results.Json(
        new { error = "browser_authorization_required", detail = "Refresh the loopback browser token and retry." },
        host.Json,
        statusCode: StatusCodes.Status403Forbidden);
}

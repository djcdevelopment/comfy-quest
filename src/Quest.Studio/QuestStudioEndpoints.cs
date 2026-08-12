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
            if (!host.Authorize(request)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var result = studio.Save(body);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        });
        app.MapPost("/api/v1/workbench/quest-studio/certify", (HttpRequest request, QuestStudioProject? body, QuestStudioService studio) =>
        {
            if (!host.Authorize(request)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var result = studio.Certify(body);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        });
        app.MapPost("/api/v1/workbench/quest-studio/publish-project", async (HttpRequest request, QuestStudioProject? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            if (!host.Authorize(request)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var result = await studio.PublishAsync(body, cancellationToken);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        });
        app.MapPost("/api/v1/workbench/quest-studio/publish", async (HttpRequest request, QuestPackPublisher publisher, CancellationToken cancellationToken) =>
        {
            if (!host.Authorize(request)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var filename = request.Headers["X-Questpack-Filename"].ToString();
            var receipt = await publisher.PublishAsync(request.Body, filename, cancellationToken);
            return receipt.Ok ? Results.Ok(receipt) : Results.BadRequest(receipt);
        });
    }
}

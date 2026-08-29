using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
    const long MaxImportRequestBytes = 1024 * 1024 + 1024;
    const long MaxSourceImportRequestBytes = 10L * 1024 * 1024;

    public static void Map(WebApplication app, IQuestStudioHost host)
    {
        app.MapGet("/quest-studio", () => Results.Text(QuestStudioPage.Html, "text/html", Encoding.UTF8));
        app.MapGet("/quest-studio/studio.css", () => Results.Text(QuestStudioPage.Css, "text/css", Encoding.UTF8));
        app.MapGet("/quest-studio/studio.js", () => Results.Text(QuestStudioPage.Js, "text/javascript", Encoding.UTF8));

        app.MapGet("/api/v2/quest-studio/catalog", (QuestStudioService studio) => Results.Json(studio.WorkspaceCatalog(), host.Json));
        app.MapGet("/api/v2/quest-studio/builds", (HttpRequest request, HttpResponse response, [FromServices] QuestStudioBuildService builds) =>
        { NoStore(response); return !host.Authorize(request) ? Forbidden(host) : Results.Json(new { schema = "comfy-quest-studio-build-list/v1", builds = builds.List() }, host.Json); });
        app.MapPost("/api/v2/quest-studio/builds/import", async (HttpRequest request, HttpResponse response, [FromServices] QuestStudioBuildService builds, CancellationToken token) =>
        { NoStore(response); if (!host.Authorize(request)) return Forbidden(host); return BuildResult(await builds.ImportAsync(request, token), host); }).WithMetadata(new RequestSizeLimitAttribute(2 * 1024 * 1024));
        app.MapGet("/api/v2/quest-studio/builds/{buildId}", (string buildId, HttpRequest request, HttpResponse response, [FromServices] QuestStudioBuildService builds) =>
        { NoStore(response); if (!host.Authorize(request)) return Forbidden(host); var build = builds.Read(buildId); return build is null ? Results.NotFound() : Results.Json(build, host.Json); });
        app.MapPut("/api/v2/quest-studio/builds/{buildId}/placement", (string buildId, HttpRequest request, HttpResponse response, StudioBuildPlacementRequest? body, [FromServices] QuestStudioBuildService builds) =>
        { NoStore(response); if (!host.Authorize(request)) return Forbidden(host); if (body is null) return Results.BadRequest(new { ok = false, error = "placement_required" }); return BuildResult(builds.Placement(buildId, body), host); });
        app.MapPost("/api/v2/quest-studio/builds/{buildId}/stage", (string buildId, HttpRequest request, HttpResponse response, [FromServices] QuestStudioBuildService builds) =>
        { NoStore(response); if (!host.Authorize(request)) return Forbidden(host); return BuildResult(builds.Stage(buildId), host); });
        app.MapGet("/api/v2/quest-studio/portfolio", (HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            return !host.Authorize(request) ? Forbidden(host) : Results.Json(studio.PortfolioView(), host.Json);
        });
        app.MapPut("/api/v2/quest-studio/portfolio", (HttpRequest request, HttpResponse response, StudioPortfolioSaveRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.SavePortfolio(body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        });
        app.MapGet("/api/v2/quest-studio/guilds", (HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            return !host.Authorize(request) ? Forbidden(host) : Results.Json(new { schema_version = 1, guilds = studio.ListGuilds() }, host.Json);
        });
        app.MapPost("/api/v2/quest-studio/guilds", (HttpRequest request, HttpResponse response, StudioGuildCreateRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            try { return Results.Json(studio.CreateGuild(body), host.Json, statusCode: StatusCodes.Status201Created); }
            catch (InvalidOperationException exception) { return Results.Json(new { error = exception.Message }, host.Json, statusCode: StatusCodes.Status400BadRequest); }
        });
        app.MapPost("/api/v2/quest-studio/guilds/import", (HttpRequest request, HttpResponse response, StudioGuildImportRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.ImportGuild(body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status201Created : StatusCodes.Status400BadRequest);
        }).WithMetadata(new RequestSizeLimitAttribute(MaxImportRequestBytes));
        app.MapGet("/api/v2/quest-studio/guilds/{guildId}", (string guildId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var guild = studio.ReadGuild(guildId);
            return guild is null ? Results.NotFound() : Results.Json(guild, host.Json);
        });
        app.MapPut("/api/v2/quest-studio/guilds/{guildId}", (string guildId, HttpRequest request, HttpResponse response, StudioGuildSaveRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.SaveGuild(guildId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict : result.Error == "guild_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/archive", (string guildId, HttpRequest request, HttpResponse response, StudioGuildArchiveRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.ArchiveGuild(guildId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/place", (string guildId, HttpRequest request, HttpResponse response, StudioGuildPlacementRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.PlaceProject(guildId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/duplicate", (string guildId, HttpRequest request, HttpResponse response, StudioGuildDuplicateRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.DuplicateGuild(guildId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status201Created
                : result.Error == "revision_conflict" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/export", (string guildId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            return Download(response, studio.ExportGuild(guildId), host);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/certify", (string guildId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.CertifyGuild(guildId);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Error == "guild_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/publish", async (string guildId, HttpRequest request, HttpResponse response, StudioPublishRequest? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.PublishGuildAsync(guildId, body, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict
                : result.Error == "guild_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/play", async (string guildId, HttpRequest request, HttpResponse response, StudioPublishRequest? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.PlayGuildAsync(guildId, body, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict
                : result.Error == "guild_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/sources/import", (string guildId, HttpRequest request, HttpResponse response, StudioSourceImportRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.ImportSource(guildId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status201Created
                : result.Conflict ? StatusCodes.Status409Conflict
                : result.Error == "guild_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        }).WithMetadata(new RequestSizeLimitAttribute(MaxSourceImportRequestBytes));
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/abstractions/promote", (string guildId, HttpRequest request, HttpResponse response, StudioAbstractionPromoteRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.PromoteAbstraction(guildId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status201Created
                : result.Conflict ? StatusCodes.Status409Conflict
                : result.Error == "guild_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/abstractions/{abstractionId}/revisions", (string guildId, string abstractionId, HttpRequest request, HttpResponse response, StudioAbstractionReviseRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.ReviseAbstraction(guildId, abstractionId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status201Created
                : result.Conflict ? StatusCodes.Status409Conflict
                : result.Error is "guild_missing" or "abstraction_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/abstractions/{abstractionId}/instantiate", (string guildId, string abstractionId, HttpRequest request, HttpResponse response, StudioAbstractionInstantiateRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.InstantiateAbstraction(guildId, abstractionId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status201Created
                : result.Conflict ? StatusCodes.Status409Conflict
                : result.Error is "guild_missing" or "abstraction_revision_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/campaigns", (string guildId, HttpRequest request, HttpResponse response, StudioCampaignCreateRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.CreateCampaign(guildId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status201Created
                : result.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        });
        app.MapGet("/api/v2/quest-studio/guilds/{guildId}/campaigns/{campaignId}", (string guildId, string campaignId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var campaign = studio.ReadCampaign(guildId, campaignId);
            return campaign is null ? Results.NotFound() : Results.Json(campaign, host.Json);
        });
        app.MapPut("/api/v2/quest-studio/guilds/{guildId}/campaigns/{campaignId}", (string guildId, string campaignId, HttpRequest request, HttpResponse response, StudioCampaignSaveRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.SaveCampaign(guildId, campaignId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict
                : result.Error is "guild_missing" or "campaign_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/campaigns/{campaignId}/place", (string guildId, string campaignId, HttpRequest request, HttpResponse response, StudioCampaignPlacementRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.PlaceInCampaign(guildId, campaignId, body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict
                : result.Error is "guild_missing" or "campaign_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/campaigns/{campaignId}/certify", (string guildId, string campaignId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.CertifyCampaign(guildId, campaignId);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Error is "guild_missing" or "campaign_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/campaigns/{campaignId}/publish", async (string guildId, string campaignId, HttpRequest request, HttpResponse response, StudioCampaignPublishRequest? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.PublishCampaignAsync(guildId, campaignId, body, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict
                : result.Error is "guild_missing" or "campaign_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/guilds/{guildId}/campaigns/{campaignId}/play", async (string guildId, string campaignId, HttpRequest request, HttpResponse response, StudioCampaignPublishRequest? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.PlayCampaignAsync(guildId, campaignId, body, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK
                : result.Conflict ? StatusCodes.Status409Conflict
                : result.Error is "guild_missing" or "campaign_missing" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        });
        app.MapGet("/api/v2/quest-studio/guilds/{guildId}/campaigns/{campaignId}/evidence", (string guildId, string campaignId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var evidence = studio.CampaignEvidence(guildId, campaignId);
            return evidence is null ? Results.NotFound() : Results.Json(evidence, host.Json);
        });
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
        app.MapPost("/api/v2/quest-studio/projects/import", (HttpRequest request, HttpResponse response, StudioImportRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.ImportProject(body);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status201Created : StatusCodes.Status400BadRequest);
        }).WithMetadata(new RequestSizeLimitAttribute(MaxImportRequestBytes));
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
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/detach", (string projectId, HttpRequest request, HttpResponse response, StudioProjectDetachRequest? body, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.DetachProject(projectId, body);
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
        app.MapGet("/api/v2/quest-studio/projects/{projectId}/runs", (string projectId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            return !host.Authorize(request) ? Forbidden(host) : Results.Json(studio.RunStatus(projectId), host.Json);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/runs/reset-preview", async (string projectId, HttpRequest request, HttpResponse response, StudioRunResetRequest? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.PreviewResetAsync(projectId, body, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/runs/reset", async (string projectId, HttpRequest request, HttpResponse response, StudioRunResetRequest? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.ApplyResetAsync(projectId, body, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/runs/select-experience", async (string projectId, HttpRequest request, HttpResponse response, StudioSelectExperienceRequest? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.SelectExperienceAsync(projectId, body, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/runs/binding-candidates", async (string projectId, HttpRequest request, HttpResponse response, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.BindingCandidatesAsync(projectId, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/runs/bind", async (string projectId, HttpRequest request, HttpResponse response, StudioBindExperienceRequest? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.BindExperienceAsync(projectId, body, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapPost("/api/v2/quest-studio/projects/{projectId}/runs/restore-binding", async (string projectId, HttpRequest request, HttpResponse response, StudioRestoreBindingRequest? body, QuestStudioService studio, CancellationToken cancellationToken) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = await studio.RestoreBindingAsync(projectId, body, cancellationToken);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
        app.MapGet("/api/v2/quest-studio/projects/{projectId}/runs/control/{requestId}", (string projectId, string requestId, string? runId, HttpRequest request, HttpResponse response, QuestStudioService studio) =>
        {
            NoStore(response);
            if (!host.Authorize(request)) return Forbidden(host);
            var result = studio.RunControlReceipt(projectId, requestId, runId);
            return Results.Json(result, host.Json, statusCode: result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
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

    static IResult BuildResult(StudioBuildOperationResult result, IQuestStudioHost host) => Results.Json(new
    {
        result.Ok,
        result.Error,
        result.Build,
        result.Receipt,
        result.AlreadyPresent,
        result.Revision,
    }, host.Json, statusCode: result.Status);

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

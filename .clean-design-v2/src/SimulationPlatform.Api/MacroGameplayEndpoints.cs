using System.Security.Claims;
using SimulationPlatform.Identity.Authorization;
using SimulationPlatform.Simulations.Economics.Macroeconomics;

namespace SimulationPlatform.Api;

public static class MacroGameplayEndpoints
{
    public static IEndpointRouteBuilder MapMacroGameplay(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/economics/macro/sessions/{sessionId:guid}")
            .RequireAuthorization(PlatformPolicies.Instructor);
        group.MapGet("/console", async (Guid sessionId, ClaimsPrincipal user, IMacroClassroomGameplay gameplay, CancellationToken ct) =>
            Results.Ok(await gameplay.GetInstructorConsoleAsync(UserId(user), sessionId, ct)));
        group.MapGet("/debrief", async (Guid sessionId, ClaimsPrincipal user, IMacroClassroomGameplay gameplay, CancellationToken ct) =>
            Results.Ok(await gameplay.GetDebriefAsync(UserId(user), sessionId, ct)));
        group.MapGet("/analytics", async (Guid sessionId, ClaimsPrincipal user, IMacroLearningAnalytics analytics, CancellationToken ct) =>
            Results.Ok(await analytics.AnalyzeAsync(UserId(user), sessionId, ct)));
        group.MapGet("/comparison", async (Guid sessionId, ClaimsPrincipal user, IMacroLearningAnalytics analytics, CancellationToken ct) =>
            Results.Ok(await analytics.AnalyzeAsync(UserId(user), sessionId, ct)));
        group.MapGet("/cohort-summary", async (Guid sessionId, ClaimsPrincipal user, IMacroLearningAnalytics analytics, CancellationToken ct) =>
            Results.Ok(await analytics.AnalyzeAsync(UserId(user), sessionId, ct)));
        group.MapGet("/report", async (Guid sessionId, string format, ClaimsPrincipal user, IMacroLearningAnalytics analytics, CancellationToken ct) =>
        { var report = await analytics.ReportAsync(UserId(user), sessionId, ct); return format.Equals("csv", StringComparison.OrdinalIgnoreCase)
                ? Results.Text(analytics.ExportCsv(report), "text/csv") : Results.Json(report); });
        group.MapGet("/replay", async (Guid sessionId, ClaimsPrincipal user, IMacroLearningAnalytics analytics, CancellationToken ct) =>
            Results.Ok((await analytics.ReportAsync(UserId(user), sessionId, ct)).Replay));
        group.MapGet("/assessment-comments", async (Guid sessionId, ClaimsPrincipal user, IMacroLearningAnalytics analytics, CancellationToken ct) =>
            Results.Ok(await analytics.CommentsAsync(UserId(user), sessionId, ct)));
        group.MapPost("/assessment-comments", async (Guid sessionId, AssessmentCommentRequest request, ClaimsPrincipal user, HttpRequest http, IMacroLearningAnalytics analytics, CancellationToken ct) =>
            Results.Created($"/api/v1/economics/macro/sessions/{sessionId}/assessment-comments", await analytics.AddCommentAsync(UserId(user), sessionId, request.TargetType, request.TargetId, request.Text, http.Headers["Idempotency-Key"].ToString(), ct)));
        group.MapPut("/assessment-comments/{commentId:guid}", async (Guid commentId, AssessmentCommentUpdateRequest request, ClaimsPrincipal user, IMacroLearningAnalytics analytics, CancellationToken ct) =>
            Results.Ok(await analytics.UpdateCommentAsync(UserId(user), commentId, request.Text, request.ExpectedVersion, ct)));

        var authoring = endpoints.MapGroup("/api/v1/economics/macro/scenario-authoring")
            .RequireAuthorization(PlatformPolicies.Instructor);
        authoring.MapGet("/templates", (IMacroScenarioAuthoring service) => Results.Ok(service.Templates()));
        authoring.MapPost("/drafts", async (CreateMacroDraftRequest request, ClaimsPrincipal user, HttpRequest http,
            IMacroScenarioAuthoring service, CancellationToken ct) => Results.Created("/api/v1/economics/macro/scenario-authoring/drafts",
            await service.CreateAsync(UserId(user), request.SimulationDefinitionId, request.Name, request.Content,
                http.Headers["Idempotency-Key"].ToString(), ct)));
        authoring.MapGet("/drafts/{draftId:guid}", async (Guid draftId, ClaimsPrincipal user, IMacroScenarioAuthoring service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(UserId(user), draftId, ct)));
        authoring.MapPut("/drafts/{draftId:guid}", async (Guid draftId, UpdateMacroDraftRequest request, ClaimsPrincipal user,
            IMacroScenarioAuthoring service, CancellationToken ct) => Results.Ok(await service.UpdateAsync(UserId(user), draftId,
                request.Name, request.Content, request.ExpectedVersion, ct)));
        authoring.MapPost("/drafts/{draftId:guid}/clone", async (Guid draftId, CloneMacroDraftRequest request, ClaimsPrincipal user,
            HttpRequest http, IMacroScenarioAuthoring service, CancellationToken ct) => Results.Created("/api/v1/economics/macro/scenario-authoring/drafts",
                await service.CloneAsync(UserId(user), draftId, request.Name, http.Headers["Idempotency-Key"].ToString(), ct)));
        authoring.MapPost("/drafts/{draftId:guid}/archive", async (Guid draftId, VersionRequest request, ClaimsPrincipal user,
            IMacroScenarioAuthoring service, CancellationToken ct) => { await service.ArchiveAsync(UserId(user), draftId, request.ExpectedVersion, ct); return Results.NoContent(); });
        authoring.MapPost("/drafts/{draftId:guid}/validate", async (Guid draftId, ClaimsPrincipal user, IMacroScenarioAuthoring service, CancellationToken ct) =>
            Results.Ok(service.Validate((await service.GetAsync(UserId(user), draftId, ct)).Content)));
        authoring.MapPost("/drafts/{draftId:guid}/preview", async (Guid draftId, PreviewRequest request, ClaimsPrincipal user,
            IMacroScenarioAuthoring service, CancellationToken ct) => Results.Ok(await service.PreviewAsync(UserId(user), draftId, request.Seed, ct)));
        authoring.MapPost("/drafts/{draftId:guid}/publish", async (Guid draftId, VersionRequest request, ClaimsPrincipal user,
            IMacroScenarioAuthoring service, CancellationToken ct) => Results.Ok(new { scenarioVersionId = await service.PublishAsync(UserId(user), draftId, request.ExpectedVersion, ct) }));
        return endpoints;
    }

    private static Guid UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var id)
            ? id : throw new UnauthorizedAccessException("Authenticated subject is missing.");
}

public sealed record CreateMacroDraftRequest(Guid SimulationDefinitionId, string Name, MacroScenarioContent Content);
public sealed record UpdateMacroDraftRequest(string Name, MacroScenarioContent Content, long ExpectedVersion);
public sealed record CloneMacroDraftRequest(string Name);
public sealed record VersionRequest(long ExpectedVersion);
public sealed record PreviewRequest(int Seed);
public sealed record AssessmentCommentRequest(string TargetType, Guid TargetId, string Text);
public sealed record AssessmentCommentUpdateRequest(string Text, long ExpectedVersion);

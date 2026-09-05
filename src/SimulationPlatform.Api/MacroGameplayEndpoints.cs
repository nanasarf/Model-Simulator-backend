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

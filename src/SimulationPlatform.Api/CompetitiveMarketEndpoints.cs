using System.Security.Claims;
using SimulationPlatform.Identity.Authorization;
using SimulationPlatform.Simulations.Economics.CompetitiveMarket;

namespace SimulationPlatform.Api;
public static class CompetitiveMarketEndpoints
{
    public static IEndpointRouteBuilder MapCompetitiveMarket(this IEndpointRouteBuilder e)
    {
        var gameplay = e.MapGroup("/api/v1/economics/competitive-market/sessions/{sessionId:guid}").RequireAuthorization(PlatformPolicies.Instructor);
        gameplay.MapGet("/console", async (Guid sessionId, ClaimsPrincipal u, ICompetitiveMarketGameplay s, CancellationToken ct) => Results.Ok(await s.ConsoleAsync(Id(u), sessionId, ct)));
        gameplay.MapGet("/replay", async (Guid sessionId, ClaimsPrincipal u, ICompetitiveMarketGameplay s, CancellationToken ct) => Results.Ok(await s.ReplayAsync(Id(u), sessionId, ct)));
        var g = e.MapGroup("/api/v1/economics/competitive-market/scenario-authoring").RequireAuthorization(PlatformPolicies.Instructor);
        g.MapGet("/templates", (ICompetitiveMarketAuthoring s) => Results.Ok(s.Templates()));
        g.MapPost("/drafts", async (CompetitiveMarketCreateRequest r, ClaimsPrincipal u, HttpRequest h, ICompetitiveMarketAuthoring s, CancellationToken ct) => Results.Created("/api/v1/economics/competitive-market/scenario-authoring/drafts", await s.CreateAsync(Id(u), r.SimulationDefinitionId, r.Name, r.Content, h.Headers["Idempotency-Key"].ToString(), ct)));
        g.MapGet("/drafts/{id:guid}", async (Guid id, ClaimsPrincipal u, ICompetitiveMarketAuthoring s, CancellationToken ct) => Results.Ok(await s.GetAsync(Id(u), id, ct)));
        g.MapPut("/drafts/{id:guid}", async (Guid id, CompetitiveMarketUpdateRequest r, ClaimsPrincipal u, ICompetitiveMarketAuthoring s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(Id(u), id, r.Name, r.Content, r.ExpectedVersion, ct)));
        g.MapPost("/drafts/{id:guid}/clone", async (Guid id, CompetitiveMarketCloneRequest r, ClaimsPrincipal u, HttpRequest h, ICompetitiveMarketAuthoring s, CancellationToken ct) => Results.Created("/api/v1/economics/competitive-market/scenario-authoring/drafts", await s.CloneAsync(Id(u), id, r.Name, h.Headers["Idempotency-Key"].ToString(), ct)));
        g.MapPost("/drafts/{id:guid}/archive", async (Guid id, VersionRequest r, ClaimsPrincipal u, ICompetitiveMarketAuthoring s, CancellationToken ct) => { await s.ArchiveAsync(Id(u), id, r.ExpectedVersion, ct); return Results.NoContent(); });
        g.MapPost("/drafts/{id:guid}/validate", async (Guid id, ClaimsPrincipal u, ICompetitiveMarketAuthoring s, CancellationToken ct) => Results.Ok(s.Validate((await s.GetAsync(Id(u), id, ct)).Content)));
        g.MapPost("/drafts/{id:guid}/preview", async (Guid id, PreviewRequest r, ClaimsPrincipal u, ICompetitiveMarketAuthoring s, CancellationToken ct) => Results.Ok(await s.PreviewAsync(Id(u), id, r.Seed, ct)));
        g.MapPost("/drafts/{id:guid}/publish", async (Guid id, VersionRequest r, ClaimsPrincipal u, ICompetitiveMarketAuthoring s, CancellationToken ct) => Results.Ok(new { scenarioVersionId = await s.PublishAsync(Id(u), id, r.ExpectedVersion, ct) }));
        return e;
    }
    private static Guid Id(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")!);
}
public sealed record CompetitiveMarketCreateRequest(Guid SimulationDefinitionId, string Name, CompetitiveMarketScenarioContent Content);
public sealed record CompetitiveMarketUpdateRequest(string Name, CompetitiveMarketScenarioContent Content, long ExpectedVersion);
public sealed record CompetitiveMarketCloneRequest(string Name);

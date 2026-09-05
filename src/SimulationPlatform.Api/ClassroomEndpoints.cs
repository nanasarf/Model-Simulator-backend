using System.Security.Claims;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Identity.Authorization;

namespace SimulationPlatform.Api;

public static class ClassroomEndpoints
{
    public static IEndpointRouteBuilder MapClassroomWorkflow(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");
        api.MapPost("/courses", async (NamedCourseRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
            Results.Created("/api/v1/courses", new { id = await workflow.CreateCourseAsync(UserId(user), request.Code, request.Name, ct) }))
            .RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/courses/{courseId:guid}/classrooms", async (Guid courseId, NameRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
            Results.Created("/api/v1/classrooms", new { id = await workflow.CreateClassroomAsync(UserId(user), courseId, request.Name, ct) }))
            .RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/classrooms/{classroomId:guid}/enrollments", async (Guid classroomId, UserRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
        { await workflow.EnrollAsync(UserId(user), classroomId, request.UserId, ct); return Results.NoContent(); })
            .RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/simulation-definitions", async (NameRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
            Results.Created("/api/v1/simulation-definitions", new { id = await workflow.CreateDefinitionAsync(UserId(user), request.Name, ct) }))
            .RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/simulation-definitions/{definitionId:guid}/scenarios", async (Guid definitionId, PublishScenarioRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
            Results.Created("/api/v1/scenarios", new { id = await workflow.PublishScenarioAsync(UserId(user), definitionId, request.Name, request.Manifest, ct) }))
            .RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/classrooms/{classroomId:guid}/sessions", async (Guid classroomId, CreateSessionRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
            Results.Created("/api/v1/sessions", new { id = await workflow.CreateSessionAsync(UserId(user), classroomId, request.ScenarioVersionId, request.Seed, ct) }))
            .RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/sessions/{sessionId:guid}/teams", async (Guid sessionId, NameRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
            Results.Created("/api/v1/teams", new { id = await workflow.CreateTeamAsync(UserId(user), sessionId, request.Name, ct) }))
            .RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/sessions/{sessionId:guid}/teams/{teamId:guid}/members", async (Guid sessionId, Guid teamId, UserRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
        { await workflow.AddTeamMemberAsync(UserId(user), sessionId, teamId, request.UserId, ct); return Results.NoContent(); })
            .RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/sessions/{sessionId:guid}/role-assignments", async (Guid sessionId, AssignRoleRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
            Results.Created("/api/v1/role-assignments", new { id = await workflow.AssignRoleAsync(UserId(user), sessionId, request.TeamId, request.UserId, request.RoleCode, ct) }))
            .RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPut("/sessions/{sessionId:guid}/participants/me/readiness", async (Guid sessionId, ReadinessRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
        { await workflow.SetReadyAsync(UserId(user), sessionId, request.Ready, ct); return Results.NoContent(); })
            .RequireAuthorization(PlatformPolicies.Student);
        api.MapPut("/sessions/{sessionId:guid}/rounds/current/readiness", async (Guid sessionId, ReadinessRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
        { await workflow.SetRoundReadyAsync(UserId(user), sessionId, request.Ready, ct); return Results.NoContent(); })
            .RequireAuthorization(PlatformPolicies.Student);
        api.MapPost("/sessions/{sessionId:guid}/commands/start", async (Guid sessionId, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
        { await workflow.StartSessionAsync(UserId(user), sessionId, ct); return Results.NoContent(); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/sessions/{sessionId:guid}/commands/pause", async (Guid sessionId, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
        { await workflow.PauseSessionAsync(UserId(user), sessionId, ct); return Results.NoContent(); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/sessions/{sessionId:guid}/commands/resume", async (Guid sessionId, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
        { await workflow.ResumeSessionAsync(UserId(user), sessionId, ct); return Results.NoContent(); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/sessions/{sessionId:guid}/commands/advance-phase", async (Guid sessionId, AdvancePhaseRequest request, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
        { await workflow.AdvancePhaseAsync(UserId(user), sessionId, request.TargetPhase, ct); return Results.NoContent(); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapGet("/sessions/{sessionId:guid}/state", async (Guid sessionId, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
            Results.Ok(await workflow.RecoverAsync(UserId(user), user.IsInRole("Instructor") || user.IsInRole("PlatformAdministrator"), sessionId, ct))).RequireAuthorization();
        api.MapGet("/sessions/{sessionId:guid}/history", async (Guid sessionId, ClaimsPrincipal user, IClassroomWorkflow workflow, CancellationToken ct) =>
            Results.Ok(await workflow.HistoryAsync(UserId(user), user.IsInRole("Instructor") || user.IsInRole("PlatformAdministrator"), sessionId, ct))).RequireAuthorization();
        return endpoints;
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedAccessException("Authenticated subject is missing.");
}

public sealed record NamedCourseRequest(string Code, string Name);
public sealed record NameRequest(string Name);
public sealed record UserRequest(Guid UserId);
public sealed record PublishScenarioRequest(string Name, ScenarioManifest Manifest);
public sealed record CreateSessionRequest(Guid ScenarioVersionId, int Seed);
public sealed record AssignRoleRequest(Guid TeamId, Guid UserId, string RoleCode);
public sealed record ReadinessRequest(bool Ready);
public sealed record AdvancePhaseRequest(string TargetPhase);

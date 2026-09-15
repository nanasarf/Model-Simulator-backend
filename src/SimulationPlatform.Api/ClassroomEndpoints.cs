using System.Security.Claims;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Identity.Authorization;
using SimulationPlatform.Identity;
using SimulationPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace SimulationPlatform.Api;

public static class ClassroomEndpoints
{
    public static IEndpointRouteBuilder MapClassroomWorkflow(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");
        api.MapGet("/classrooms", async (ClaimsPrincipal user, PlatformDbContext db, int? page, int? pageSize, CancellationToken ct) =>
        {
            var owner = UserId(user); var p = Math.Max(1, page ?? 1); var size = Math.Clamp(pageSize ?? 25, 1, 100);
            var query = db.Classrooms.AsNoTracking().Join(db.Courses, r => r.CourseId, c => c.Id, (r,c) => new { r,c }).Where(x => x.c.OwnerUserId == owner).OrderBy(x => x.r.Id);
            var total = await query.CountAsync(ct);
            var items = await query.Skip((p-1)*size).Take(size).Select(x => new { classroomId=x.r.Id, name=x.r.Name, courseId=x.c.Id, courseCode=x.c.Code, courseName=x.c.Name }).ToListAsync(ct);
            return Results.Ok(new { items, page=p, pageSize=size, totalCount=total });
        }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapGet("/classrooms/{classroomId:guid}", async (Guid classroomId, ClaimsPrincipal user, PlatformDbContext db, CancellationToken ct) =>
        {
            var owner = UserId(user); var item = await db.Classrooms.AsNoTracking().Join(db.Courses, r=>r.CourseId,c=>c.Id,(r,c)=>new {r,c}).Where(x=>x.r.Id==classroomId && x.c.OwnerUserId==owner).Select(x=>new { classroomId=x.r.Id, name=x.r.Name, courseId=x.c.Id, courseCode=x.c.Code, courseName=x.c.Name }).SingleOrDefaultAsync(ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapGet("/classrooms/{classroomId:guid}/roster", async (Guid classroomId, ClaimsPrincipal user, PlatformDbContext db, IdentityDataContext identity, CancellationToken ct) =>
        {
            var owner = UserId(user); var allowed = await db.Classrooms.Join(db.Courses,r=>r.CourseId,c=>c.Id,(r,c)=>new {r,c}).AnyAsync(x=>x.r.Id==classroomId && x.c.OwnerUserId==owner,ct); if(!allowed) return Results.NotFound();
            var rows = await db.Enrollments.Where(x=>x.ClassroomId==classroomId).Join(identity.Users,u=>u.Id,e=>e.UserId,(e,u)=>new { participantUserId=e.UserId, email=u.Email, userName=u.UserName, enrolledAt=e.EnrolledAt }).OrderBy(x=>x.participantUserId).ToListAsync(ct); return Results.Ok(rows);
        }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapGet("/sessions", async (ClaimsPrincipal user, PlatformDbContext db, int? page, int? pageSize, Guid? classroomId, string? status, string? modelIdentifier, CancellationToken ct) =>
        {
            var owner=UserId(user); var p=Math.Max(1,page??1); var size=Math.Clamp(pageSize??25,1,100);
            var q=db.Sessions.AsNoTracking().Join(db.Classrooms,s=>s.ClassroomId,r=>r.Id,(s,r)=>new{s,r}).Join(db.Courses,x=>x.r.CourseId,c=>c.Id,(x,c)=>new{x.s,x.r,c}).Join(db.ScenarioVersions,x=>x.s.ScenarioVersionId,v=>v.Id,(x,v)=>new{x.s,x.r,x.c,v}).Where(x=>x.c.OwnerUserId==owner);
            if(classroomId.HasValue) q=q.Where(x=>x.s.ClassroomId==classroomId); if(!string.IsNullOrWhiteSpace(status)) q=q.Where(x=>x.s.Status==status); if(!string.IsNullOrWhiteSpace(modelIdentifier)) q=q.Where(x=>x.s.ModelIdentifier==modelIdentifier);
            var total=await q.CountAsync(ct); var items=await q.OrderBy(x=>x.s.Id).Skip((p-1)*size).Take(size).Select(x=>new{sessionId=x.s.Id,classroomId=x.s.ClassroomId,classroomName=x.r.Name,scenarioId=x.v.SimulationDefinitionId,scenarioTitle=x.v.Name,publishedScenarioVersion=x.v.Version,modelIdentifier=x.s.ModelIdentifier,modelVersion=x.s.ModelVersion,status=x.s.Status,currentRound=x.s.RoundNumber,currentPhase=x.s.Phase,createdAt=x.s.CreatedAt,startedAt=x.s.StartedAt,completedAt=x.s.CompletedAt}).ToListAsync(ct); return Results.Ok(new{items,page=p,pageSize=size,totalCount=total});
        }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapGet("/me/sessions", async (ClaimsPrincipal user, PlatformDbContext db, CancellationToken ct) =>
        { var id=UserId(user); var rows=await db.Participants.AsNoTracking().Where(p=>p.UserId==id).Join(db.Sessions,p=>p.SessionId,s=>s.Id,(p,s)=>new{p,s}).Join(db.Classrooms,x=>x.s.ClassroomId,r=>r.Id,(x,r)=>new{x.p,x.s,r}).Join(db.ScenarioVersions,x=>x.s.ScenarioVersionId,v=>v.Id,(x,v)=>new{x.p,x.s,x.r,v}).OrderBy(x=>x.s.Id).Select(x=>new{sessionId=x.s.Id,classroomId=x.s.ClassroomId,classroomName=x.r.Name,scenarioTitle=x.v.Name,modelIdentifier=x.s.ModelIdentifier,modelVersion=x.s.ModelVersion,status=x.s.Status,currentRound=x.s.RoundNumber,currentPhase=x.s.Phase,teamId=x.p.TeamId}).ToListAsync(ct); return Results.Ok(rows); }).RequireAuthorization(PlatformPolicies.Student);
        api.MapGet("/sessions/{sessionId:guid}/setup", async (Guid sessionId, ClaimsPrincipal user, PlatformDbContext db, IdentityDataContext identity, CancellationToken ct) =>
        { var owner=UserId(user); var baseRow=await db.Sessions.AsNoTracking().Join(db.Classrooms,s=>s.ClassroomId,r=>r.Id,(s,r)=>new{s,r}).Join(db.Courses,x=>x.r.CourseId,c=>c.Id,(x,c)=>new{x.s,x.r,c}).Join(db.ScenarioVersions,x=>x.s.ScenarioVersionId,v=>v.Id,(x,v)=>new{x.s,x.r,x.c,v}).Where(x=>x.s.Id==sessionId&&x.c.OwnerUserId==owner).SingleOrDefaultAsync(ct); if(baseRow is null)return Results.NotFound(); var manifest=JsonSerializer.Deserialize<ScenarioManifest>(baseRow.v.ManifestJson)!; var teams=await db.Teams.Where(t=>t.SessionId==sessionId).Select(t=>new{teamId=t.Id,name=t.Name,members=db.Participants.Where(p=>p.TeamId==t.Id).Select(p=>p.UserId).ToList()}).ToListAsync(ct); var participants=await db.Participants.Where(p=>p.SessionId==sessionId).Select(p=>new{participantId=p.Id,userId=p.UserId,teamId=p.TeamId,isReady=p.IsReady}).ToListAsync(ct); var roles=await db.RoleAssignments.Where(r=>r.SessionId==sessionId&&r.RevokedAt==null).Select(r=>new{assignmentId=r.Id,participantId=r.UserId,teamId=r.TeamId,roleCode=r.RoleCode,effectiveCapabilities=JsonSerializer.Deserialize<HashSet<string>>(r.CapabilitiesJson)}).ToListAsync(ct); return Results.Ok(new{session=new{ id=baseRow.s.Id,status=baseRow.s.Status,currentRound=baseRow.s.RoundNumber,currentPhase=baseRow.s.Phase},classroom=new{id=baseRow.r.Id,name=baseRow.r.Name},scenario=new{id=baseRow.v.SimulationDefinitionId,title=baseRow.v.Name,publishedVersion=baseRow.v.Version},model=new{identifier=baseRow.s.ModelIdentifier,version=baseRow.s.ModelVersion},teams,participants,availableRoles=manifest.Roles,roleAssignments=roles,readiness=new{isReady=teams.Count>0&&participants.All(p=>p.teamId!=null),blockers=participants.Where(p=>p.teamId==null).Select(p=>new{code="session.participant.unassigned",participantId=p.participantId}).ToList()}}); }).RequireAuthorization(PlatformPolicies.Instructor);
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

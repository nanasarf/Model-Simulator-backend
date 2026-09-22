using System.Security.Claims;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Application.Abstractions;
using SimulationPlatform.Identity.Authorization;
using SimulationPlatform.Identity;
using SimulationPlatform.Infrastructure.Persistence;
using SimulationPlatform.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Security.Cryptography;

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
        api.MapPost("/classrooms/{classroomId:guid}/join-code/rotate", async (Guid classroomId, ClaimsPrincipal user, PlatformDbContext db, IClock clock, HttpContext http, CancellationToken ct) =>
        { var owner=UserId(user); var allowed=await db.Classrooms.Join(db.Courses,r=>r.CourseId,c=>c.Id,(r,c)=>new{r,c}).AnyAsync(x=>x.r.Id==classroomId&&x.c.OwnerUserId==owner,ct); if(!allowed)return Results.NotFound(); var active=await db.ClassroomJoinCodes.Where(x=>x.ClassroomId==classroomId&&x.IsActive).ToListAsync(ct); foreach(var old in active){old.IsActive=false;old.RevokedAt=clock.UtcNow;} ClassroomJoinCodeRow row; do { var code=GenerateJoinCode(); row=new ClassroomJoinCodeRow{Id=Guid.NewGuid(),ClassroomId=classroomId,NormalizedCode=code,IsActive=true,CreatedAt=clock.UtcNow,CreatedBy=owner}; } while(await db.ClassroomJoinCodes.AnyAsync(x=>x.NormalizedCode==row.NormalizedCode&&x.IsActive,ct)); db.ClassroomJoinCodes.Add(row); db.AuditRecords.Add(AuditFactory.Create(owner,"ClassroomJoinCodeRotated","Classroom",classroomId.ToString(),http.TraceIdentifier,clock.UtcNow)); await db.SaveChangesAsync(ct); return Results.Ok(new{classroomId,joinCode=row.NormalizedCode,active=true}); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapGet("/classrooms/{classroomId:guid}/roster", async (Guid classroomId, ClaimsPrincipal user, PlatformDbContext db, IdentityDataContext identity, CancellationToken ct) =>
        {
            var owner = UserId(user); var allowed = await db.Classrooms.Join(db.Courses,r=>r.CourseId,c=>c.Id,(r,c)=>new {r,c}).AnyAsync(x=>x.r.Id==classroomId && x.c.OwnerUserId==owner,ct); if(!allowed) return Results.NotFound();
            var enrollments = await db.Enrollments.AsNoTracking().Where(x => x.ClassroomId == classroomId).OrderBy(x => x.UserId).ToListAsync(ct);
            var ids = enrollments.Select(x => x.UserId).ToArray();
            var users = await identity.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var rows = enrollments.Select(e => new { participantUserId = e.UserId, email = users.TryGetValue(e.UserId, out var u) ? u.Email : null, userName = users.TryGetValue(e.UserId, out u) ? u.UserName : null, enrolledAt = e.EnrolledAt });
            return Results.Ok(rows);
        }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapGet("/classrooms/{classroomId:guid}/join-code", async (Guid classroomId, ClaimsPrincipal user, PlatformDbContext db, CancellationToken ct) =>
        { var owner=UserId(user); var code=await db.ClassroomJoinCodes.AsNoTracking().Join(db.Classrooms,j=>j.ClassroomId,c=>c.Id,(j,c)=>new{j,c}).Join(db.Courses,x=>x.c.CourseId,c=>c.Id,(x,c)=>new{x.j,c}).Where(x=>x.j.ClassroomId==classroomId&&x.c.OwnerUserId==owner&&x.j.IsActive).Select(x=>x.j.NormalizedCode).SingleOrDefaultAsync(ct); return code is null?Results.NotFound():Results.Ok(new{classroomId,joinCode=code,active=true}); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/classroom-join-requests", async (JoinClassroomRequest request, ClaimsPrincipal user, PlatformDbContext db, IClock clock, HttpContext http, CancellationToken ct) =>
        { var code=(request.Code??"").Trim().ToUpperInvariant(); var student=UserId(user); var classroom=await db.ClassroomJoinCodes.Where(x=>x.NormalizedCode==code&&x.IsActive).Select(x=>x.ClassroomId).SingleOrDefaultAsync(ct); if(classroom==Guid.Empty)throw new DomainException("classroom.join_code_invalid","The classroom code is invalid or inactive."); if(await db.Enrollments.AnyAsync(x=>x.ClassroomId==classroom&&x.UserId==student,ct))return Results.Ok(new{status="Approved",classroomId=classroom}); var existing=await db.ClassroomJoinRequests.Where(x=>x.ClassroomId==classroom&&x.StudentUserId==student&&x.Status=="Pending").SingleOrDefaultAsync(ct); if(existing is not null)return Results.Ok(new{requestId=existing.Id,status=existing.Status,classroomId=classroom}); var row=new ClassroomJoinRequestRow{Id=Guid.NewGuid(),ClassroomId=classroom,StudentUserId=student,Status="Pending",RequestedAt=clock.UtcNow,Version=1}; db.ClassroomJoinRequests.Add(row); db.AuditRecords.Add(AuditFactory.Create(student,"ClassroomJoinRequested","Classroom",classroom.ToString(),http.TraceIdentifier,clock.UtcNow)); db.Outbox.Add(new OutboxMessage{Id=Guid.NewGuid(),Type="ClassroomJoinRequested",AggregateId=classroom,PayloadJson=JsonSerializer.Serialize(new{classroomId=classroom,requestId=row.Id}),OccurredAt=clock.UtcNow,NextAttemptAt=clock.UtcNow}); await db.SaveChangesAsync(ct); return Results.Accepted($"/api/v1/classroom-join-requests/{row.Id}",new{requestId=row.Id,status=row.Status,classroomId=classroom}); }).RequireAuthorization(PlatformPolicies.Student);
        api.MapGet("/classroom-join-requests/{requestId:guid}", async (Guid requestId, ClaimsPrincipal user, PlatformDbContext db, CancellationToken ct) => { var student=UserId(user); var item=await db.ClassroomJoinRequests.AsNoTracking().Where(x=>x.Id==requestId&&x.StudentUserId==student).SingleOrDefaultAsync(ct); return item is null?Results.NotFound():Results.Ok(item); }).RequireAuthorization(PlatformPolicies.Student);
        api.MapGet("/classrooms/{classroomId:guid}/join-requests", async (Guid classroomId, ClaimsPrincipal user, PlatformDbContext db, CancellationToken ct) => { var owner=UserId(user); var ok=await db.Classrooms.Join(db.Courses,r=>r.CourseId,c=>c.Id,(r,c)=>new{r,c}).AnyAsync(x=>x.r.Id==classroomId&&x.c.OwnerUserId==owner,ct); if(!ok)return Results.NotFound(); return Results.Ok(await db.ClassroomJoinRequests.AsNoTracking().Where(x=>x.ClassroomId==classroomId&&x.Status=="Pending").OrderBy(x=>x.RequestedAt).ToListAsync(ct)); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/classrooms/{classroomId:guid}/join-requests/{requestId:guid}/approve", async (Guid classroomId, Guid requestId, ClaimsPrincipal user, PlatformDbContext db, IClock clock, HttpContext http, CancellationToken ct) => { var owner=UserId(user); var owns=await db.Classrooms.Join(db.Courses,r=>r.CourseId,c=>c.Id,(r,c)=>new{r,c}).AnyAsync(x=>x.r.Id==classroomId&&x.c.OwnerUserId==owner,ct); if(!owns)return Results.NotFound(); await using var tx=await db.Database.BeginTransactionAsync(ct); var req=await db.ClassroomJoinRequests.SingleOrDefaultAsync(x=>x.Id==requestId&&x.ClassroomId==classroomId,ct)??throw new DomainException("classroom.join_request_not_found","Join request was not found."); if(req.Status!="Pending")return Results.Ok(new{requestId=req.Id,status=req.Status}); req.Status="Approved";req.ReviewedBy=owner;req.ReviewedAt=clock.UtcNow; if(!await db.Enrollments.AnyAsync(x=>x.ClassroomId==classroomId&&x.UserId==req.StudentUserId,ct))db.Enrollments.Add(new EnrollmentRow{Id=Guid.NewGuid(),ClassroomId=classroomId,UserId=req.StudentUserId,EnrolledAt=clock.UtcNow}); db.AuditRecords.Add(AuditFactory.Create(owner,"ClassroomJoinApproved","ClassroomJoinRequest",req.Id.ToString(),http.TraceIdentifier,clock.UtcNow)); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(new{requestId=req.Id,status=req.Status}); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/classrooms/{classroomId:guid}/join-requests/{requestId:guid}/reject", async (Guid classroomId, Guid requestId, ClaimsPrincipal user, PlatformDbContext db, IClock clock, HttpContext http, CancellationToken ct) => { var owner=UserId(user); var req=await db.ClassroomJoinRequests.Join(db.Classrooms,r=>r.ClassroomId,c=>c.Id,(r,c)=>new{r,c}).Join(db.Courses,x=>x.c.CourseId,c=>c.Id,(x,c)=>new{x.r,c}).SingleOrDefaultAsync(x=>x.r.Id==requestId&&x.r.ClassroomId==classroomId&&x.c.OwnerUserId==owner,ct); if(req is null)return Results.NotFound(); if(req.r.Status=="Pending"){req.r.Status="Rejected";req.r.ReviewedBy=owner;req.r.ReviewedAt=clock.UtcNow;db.AuditRecords.Add(AuditFactory.Create(owner,"ClassroomJoinRejected","ClassroomJoinRequest",requestId.ToString(),http.TraceIdentifier,clock.UtcNow));await db.SaveChangesAsync(ct);} return Results.Ok(new{requestId,status=req.r.Status}); }).RequireAuthorization(PlatformPolicies.Instructor);
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
        { var owner=UserId(user); var baseRow=await db.Sessions.AsNoTracking().Join(db.Classrooms,s=>s.ClassroomId,r=>r.Id,(s,r)=>new{s,r}).Join(db.Courses,x=>x.r.CourseId,c=>c.Id,(x,c)=>new{x.s,x.r,c}).Join(db.ScenarioVersions,x=>x.s.ScenarioVersionId,v=>v.Id,(x,v)=>new{x.s,x.r,x.c,v}).Where(x=>x.s.Id==sessionId&&x.c.OwnerUserId==owner).SingleOrDefaultAsync(ct); if(baseRow is null)return Results.NotFound(); var manifest=JsonSerializer.Deserialize<ScenarioManifest>(baseRow.v.ManifestJson)!; var teams=await db.Teams.Where(t=>t.SessionId==sessionId).Select(t=>new{teamId=t.Id,name=t.Name,members=db.Participants.Where(p=>p.TeamId==t.Id).Select(p=>p.UserId).ToList()}).ToListAsync(ct); var participants=await db.Participants.Where(p=>p.SessionId==sessionId).Select(p=>new{participantId=p.Id,userId=p.UserId,teamId=p.TeamId,isReady=p.IsReady}).ToListAsync(ct); var roles=await db.RoleAssignments.Where(r=>r.SessionId==sessionId&&r.RevokedAt==null).Select(r=>new{assignmentId=r.Id,participantId=r.UserId,teamId=r.TeamId,roleCode=r.RoleCode,effectiveCapabilities=JsonSerializer.Deserialize<HashSet<string>>(r.CapabilitiesJson)}).ToListAsync(ct); return Results.Ok(new{session=new{ id=baseRow.s.Id,status=baseRow.s.Status,currentRound=baseRow.s.RoundNumber,currentPhase=baseRow.s.Phase,version=baseRow.s.Version},classroom=new{id=baseRow.r.Id,name=baseRow.r.Name},scenario=new{id=baseRow.v.SimulationDefinitionId,title=baseRow.v.Name,publishedVersion=baseRow.v.Version},model=new{identifier=baseRow.s.ModelIdentifier,version=baseRow.s.ModelVersion},teams,participants,availableRoles=manifest.Roles,roleAssignments=roles,readiness=new{isReady=teams.Count>0&&participants.All(p=>p.teamId!=null),blockers=participants.Where(p=>p.teamId==null).Select(p=>new{code="session.participant.unassigned",participantId=p.participantId}).ToList()}}); }).RequireAuthorization(PlatformPolicies.Instructor);
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
        api.MapPost("/sessions/{sessionId:guid}/teams/{teamId:guid}/rename", async (Guid sessionId, Guid teamId, CorrectionNameRequest request, ClaimsPrincipal user, HttpRequest http, IClassroomWorkflow workflow, CancellationToken ct) => { await workflow.RenameTeamAsync(UserId(user),sessionId,teamId,request.Name,request.ExpectedVersion,http.Headers["Idempotency-Key"].ToString(),ct); return Results.NoContent(); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/sessions/{sessionId:guid}/teams/{teamId:guid}/delete", async (Guid sessionId, Guid teamId, SessionVersionRequest request, ClaimsPrincipal user, HttpRequest http, IClassroomWorkflow workflow, CancellationToken ct) => { await workflow.DeleteTeamAsync(UserId(user),sessionId,teamId,request.ExpectedVersion,http.Headers["Idempotency-Key"].ToString(),ct); return Results.NoContent(); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/sessions/{sessionId:guid}/teams/{teamId:guid}/members/{studentId:guid}/remove", async (Guid sessionId, Guid teamId, Guid studentId, SessionVersionRequest request, ClaimsPrincipal user, HttpRequest http, IClassroomWorkflow workflow, CancellationToken ct) => { await workflow.RemoveTeamMemberAsync(UserId(user),sessionId,teamId,studentId,request.ExpectedVersion,http.Headers["Idempotency-Key"].ToString(),ct); return Results.NoContent(); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/sessions/{sessionId:guid}/participants/{studentId:guid}/move", async (Guid sessionId, Guid studentId, MoveMemberRequest request, ClaimsPrincipal user, HttpRequest http, IClassroomWorkflow workflow, CancellationToken ct) => { await workflow.MoveTeamMemberAsync(UserId(user),sessionId,studentId,request.TargetTeamId,request.ExpectedVersion,http.Headers["Idempotency-Key"].ToString(),ct); return Results.NoContent(); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/sessions/{sessionId:guid}/role-assignments/{assignmentId:guid}/unassign", async (Guid sessionId, Guid assignmentId, SessionVersionRequest request, ClaimsPrincipal user, HttpRequest http, IClassroomWorkflow workflow, CancellationToken ct) => { await workflow.UnassignRoleAsync(UserId(user),sessionId,assignmentId,request.ExpectedVersion,http.Headers["Idempotency-Key"].ToString(),ct); return Results.NoContent(); }).RequireAuthorization(PlatformPolicies.Instructor);
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
        api.MapGet("/sessions/{sessionId:guid}/join-code", async (Guid sessionId, ClaimsPrincipal user, PlatformDbContext db, CancellationToken ct) =>
        { var owner=UserId(user); var session=await db.Sessions.Join(db.Classrooms,s=>s.ClassroomId,r=>r.Id,(s,r)=>new{s,r}).Join(db.Courses,x=>x.r.CourseId,c=>c.Id,(x,c)=>new{x.s,c}).Where(x=>x.s.Id==sessionId&&x.c.OwnerUserId==owner).Select(x=>x.s).SingleOrDefaultAsync(ct); if(session is null)return Results.NotFound(); if(session.Status is not ("Draft" or "Running")) throw new DomainException("session.join_not_allowed", "This session is not accepting participants."); return Results.Ok(new{sessionId,joinCode=JoinCode(sessionId),active=true}); }).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapPost("/session-joins", async (JoinSessionRequest request, ClaimsPrincipal user, PlatformDbContext db, IClock clock, HttpContext http, CancellationToken ct) =>
        {
            var code=(request.Code??string.Empty).Trim().ToUpperInvariant(); var student=UserId(user);
            var session=(await db.Sessions.AsNoTracking().ToListAsync(ct)).SingleOrDefault(s=>JoinCode(s.Id)==code);
            if(session is null) throw new DomainException("session.join_code_invalid", "The join code is invalid or expired.");
            if(session.Status is not ("Draft" or "Running")) throw new DomainException("session.join_not_allowed", "This session is not accepting participants.");
            var p=await db.Participants.SingleOrDefaultAsync(x=>x.SessionId==session.Id&&x.UserId==student,ct);
            if(p is null)
            {
                p=new ParticipantRow{Id=Guid.NewGuid(),SessionId=session.Id,UserId=student,JoinedAt=clock.UtcNow}; db.Participants.Add(p);
                db.AuditRecords.Add(AuditFactory.Create(student,"Session.ParticipantJoined","Session",session.Id.ToString(),http.TraceIdentifier,clock.UtcNow));
                db.Outbox.Add(new OutboxMessage{Id=Guid.NewGuid(),Type="ParticipantJoined",AggregateId=session.Id,PayloadJson=JsonSerializer.Serialize(new{sessionId=session.Id,userId=student,participantId=p.Id}),OccurredAt=clock.UtcNow,NextAttemptAt=clock.UtcNow});
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new{sessionId=session.Id,participantId=p.Id,teamId=p.TeamId,joinCode=code});
        }).RequireAuthorization(PlatformPolicies.Student);
        return endpoints;
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedAccessException("Authenticated subject is missing.");
    private static string GenerateJoinCode() { const string alphabet="ABCDEFGHJKMNPQRSTUVWXYZ23456789"; Span<byte> bytes=stackalloc byte[8]; RandomNumberGenerator.Fill(bytes); Span<char> chars=stackalloc char[8]; for(var i=0;i<chars.Length;i++) chars[i]=alphabet[bytes[i]%alphabet.Length]; return new string(chars); }
    private static string JoinCode(Guid id) { const string a="ABCDEFGHJKMNPQRSTUVWXYZ23456789"; var v=BitConverter.ToUInt64(id.ToByteArray(),0); var c=new char[6]; for(var i=0;i<6;i++){c[i]=a[(int)(v%(ulong)a.Length)];v/=(ulong)a.Length;} return new(c); }
}

public sealed record NamedCourseRequest(string Code, string Name);
public sealed record NameRequest(string Name);
public sealed record UserRequest(Guid UserId);
public sealed record PublishScenarioRequest(string Name, ScenarioManifest Manifest);
public sealed record CreateSessionRequest(Guid ScenarioVersionId, int Seed);
public sealed record AssignRoleRequest(Guid TeamId, Guid UserId, string RoleCode);
public sealed record ReadinessRequest(bool Ready);
public sealed record AdvancePhaseRequest(string TargetPhase);
public sealed record JoinSessionRequest(string Code);
public sealed record JoinClassroomRequest(string Code);
public sealed record SessionVersionRequest(long ExpectedVersion);
public sealed record CorrectionNameRequest(string Name, long ExpectedVersion);
public sealed record MoveMemberRequest(Guid TargetTeamId, long ExpectedVersion);

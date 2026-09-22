using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Infrastructure.Persistence;

namespace Tests.Integration;

public sealed class SessionAdmissionAcceptanceTests : IClassFixture<ClassroomDatabase>
{
    private readonly ClassroomDatabase database;
    public SessionAdmissionAcceptanceTests(ClassroomDatabase database) => this.database = database;
    private static HttpClient Client(DiscoveryApiFactory f, Guid id, string role) { var c=f.CreateClient(); c.DefaultRequestHeaders.Add("X-Test-User",id.ToString()); c.DefaultRequestHeaders.Add("X-Test-Role",role); return c; }
    private async Task<(ClassroomScope Scope, WorkflowIds Ids, Guid Code)> SetupAsync()
    {
        var scope=await database.CreateScope(); var ids=await scope.BuildStartedSession();
        using var f=new DiscoveryApiFactory(database.ConnectionString); var instructor=Client(f,ids.Instructor,"Instructor");
        var response=await instructor.GetAsync($"/api/v1/sessions/{ids.Session}/join-code"); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var code=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("joinCode").GetString()!;
        return (scope,ids,Guid.Parse(code.Length==0?Guid.Empty.ToString():ids.Session.ToString()));
    }
    private static async Task<string> CodeAsync(DiscoveryApiFactory f, Guid instructor, Guid session) => (await (await Client(f,instructor,"Instructor").GetAsync($"/api/v1/sessions/{session}/join-code")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("joinCode").GetString()!;
    private static async Task<Guid> NewMemberAsync(ClassroomScope scope, WorkflowIds ids) { var classroom=await scope.Platform.Sessions.Where(x=>x.Id==ids.Session).Select(x=>x.ClassroomId).SingleAsync(); var id=Guid.NewGuid(); scope.Platform.Enrollments.Add(new EnrollmentRow{Id=Guid.NewGuid(),ClassroomId=classroom,UserId=id,EnrolledAt=DateTimeOffset.UtcNow}); await scope.Platform.SaveChangesAsync(); return id; }

    [Fact]
    public async Task Instructor_CanGetSessionJoinCode_AndStudentMemberCanRequest()
    {
        var setup=await SetupAsync(); await using var scope=setup.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString);
        var code=await CodeAsync(f,setup.Ids.Instructor,setup.Ids.Session); Assert.Equal(8,code.Length); var studentId=await NewMemberAsync(scope,setup.Ids);
        var student=Client(f,studentId,"Student"); var response=await student.PostAsJsonAsync("/api/v1/session-join-requests",new{code=code.ToLowerInvariant()});
        Assert.Equal(HttpStatusCode.Accepted,response.StatusCode); scope.Platform.ChangeTracker.Clear(); Assert.Equal(1,await scope.Platform.SessionJoinRequests.CountAsync(x=>x.SessionId==setup.Ids.Session&&x.StudentUserId==studentId&&x.Status=="Pending")); Assert.Equal(0,await scope.Platform.Participants.CountAsync(x=>x.SessionId==setup.Ids.Session&&x.UserId==studentId));
    }

    [Fact]
    public async Task NonClassroomMember_CannotRequestSessionAdmission()
    {
        var setup=await SetupAsync(); await using var scope=setup.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var code=await CodeAsync(f,setup.Ids.Instructor,setup.Ids.Session); var student=Client(f,Guid.NewGuid(),"Student");
        var response=await student.PostAsJsonAsync("/api/v1/session-join-requests",new{code}); Assert.True((int)response.StatusCode>=400); Assert.Equal(0,await scope.Platform.SessionJoinRequests.CountAsync(x=>x.SessionId==setup.Ids.Session));
    }

    [Fact]
    public async Task InstructorApproval_CreatesParticipantWithoutTeamOrRole()
    {
        var setup=await SetupAsync(); await using var scope=setup.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var code=await CodeAsync(f,setup.Ids.Instructor,setup.Ids.Session); var studentId=await NewMemberAsync(scope,setup.Ids); var student=Client(f,studentId,"Student"); var created=await student.PostAsJsonAsync("/api/v1/session-join-requests",new{code}); var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid(); var instructor=Client(f,setup.Ids.Instructor,"Instructor");
        var approved=await instructor.PostAsJsonAsync($"/api/v1/sessions/{setup.Ids.Session}/join-requests/{id}/approve",new{}); Assert.Equal(HttpStatusCode.OK,approved.StatusCode); Assert.Equal(1,await scope.Platform.Participants.CountAsync(x=>x.SessionId==setup.Ids.Session&&x.UserId==studentId)); Assert.Equal(0,await scope.Platform.RoleAssignments.CountAsync(x=>x.SessionId==setup.Ids.Session&&x.UserId==studentId));
    }

    [Fact]
    public async Task LegacySessionJoinEndpoint_CannotAutoCreateParticipant()
    {
        var setup=await SetupAsync(); await using var scope=setup.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var code=await CodeAsync(f,setup.Ids.Instructor,setup.Ids.Session); var studentId=await NewMemberAsync(scope,setup.Ids); var student=Client(f,studentId,"Student"); var response=await student.PostAsJsonAsync("/api/v1/session-joins",new{code}); Assert.True((int)response.StatusCode>=400); Assert.Equal(0,await scope.Platform.Participants.CountAsync(x=>x.SessionId==setup.Ids.Session&&x.UserId==studentId));
    }

    [Fact]
    public async Task SessionJoinCode_RotationRevokesPreviousCode()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var instructor=Client(f,s.Ids.Instructor,"Instructor"); var a=await CodeAsync(f,s.Ids.Instructor,s.Ids.Session); var rotated=await instructor.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-code/rotate",new{}); Assert.Equal(HttpStatusCode.OK,rotated.StatusCode); var b=(await rotated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("joinCode").GetString()!; Assert.NotEqual(a,b); Assert.Equal(1,await scope.Platform.SessionJoinCodes.CountAsync(x=>x.SessionId==s.Ids.Session&&x.IsActive));
    }

    [Fact]
    public async Task InvalidSessionJoinCode_IsRejected()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var response=await Client(f,id,"Student").PostAsJsonAsync("/api/v1/session-join-requests",new{code="XXXXXXXX"}); Assert.True((int)response.StatusCode>=400); Assert.Equal(0,await scope.Platform.SessionJoinRequests.CountAsync(x=>x.SessionId==s.Ids.Session&&x.StudentUserId==id));
    }

    [Fact]
    public async Task Student_CannotListSessionJoinQueue()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var response=await Client(f,s.Ids.Student,"Student").GetAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests"); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task Instructor_CanRejectSessionJoinRequest()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var code=await CodeAsync(f,s.Ids.Instructor,s.Ids.Session); var created=await Client(f,id,"Student").PostAsJsonAsync("/api/v1/session-join-requests",new{code}); var requestId=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid(); var response=await Client(f,s.Ids.Instructor,"Instructor").PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{requestId}/reject",new{}); Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Equal(0,await scope.Platform.Participants.CountAsync(x=>x.SessionId==s.Ids.Session&&x.UserId==id));
    }

    [Fact]
    public async Task RepeatedSessionJoinRequest_DoesNotCreateDuplicatePendingRows()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var code=await CodeAsync(f,s.Ids.Instructor,s.Ids.Session); var c=Client(f,id,"Student"); var a=await c.PostAsJsonAsync("/api/v1/session-join-requests",new{code}); var b=await c.PostAsJsonAsync("/api/v1/session-join-requests",new{code}); Assert.True((int)a.StatusCode<500&&(int)b.StatusCode<500); Assert.Equal(1,await scope.Platform.SessionJoinRequests.CountAsync(x=>x.SessionId==s.Ids.Session&&x.StudentUserId==id&&x.Status=="Pending"));
    }

    [Fact]
    public async Task ExistingSessionParticipant_DoesNotCreatePendingRequest()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var code=await CodeAsync(f,s.Ids.Instructor,s.Ids.Session); var r=await Client(f,s.Ids.Student,"Student").PostAsJsonAsync("/api/v1/session-join-requests",new{code}); Assert.True((int)r.StatusCode<500); Assert.Equal(0,await scope.Platform.SessionJoinRequests.CountAsync(x=>x.SessionId==s.Ids.Session&&x.StudentUserId==s.Ids.Student&&x.Status=="Pending")); Assert.Equal(1,await scope.Platform.Participants.CountAsync(x=>x.SessionId==s.Ids.Session&&x.UserId==s.Ids.Student));
    }

    [Fact]
    public async Task ConcurrentSessionJoinRequests_CreateExactlyOnePendingRequest()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var code=await CodeAsync(f,s.Ids.Instructor,s.Ids.Session); using var a=Client(f,id,"Student"); using var b=Client(f,id,"Student"); var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var t1=Task.Run(async()=>{await gate.Task;return await a.PostAsJsonAsync("/api/v1/session-join-requests",new{code});}); var t2=Task.Run(async()=>{await gate.Task;return await b.PostAsJsonAsync("/api/v1/session-join-requests",new{code});}); gate.SetResult(); var rs=await Task.WhenAll(t1,t2); Assert.All(rs,x=>Assert.True((int)x.StatusCode<500)); Assert.Equal(1,await scope.Platform.SessionJoinRequests.CountAsync(x=>x.SessionId==s.Ids.Session&&x.StudentUserId==id&&x.Status=="Pending"));
    }

    [Fact]
    public async Task Student_CannotReadAnotherStudentsSessionJoinRequest()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var code=await CodeAsync(f,s.Ids.Instructor,s.Ids.Session); var created=await Client(f,id,"Student").PostAsJsonAsync("/api/v1/session-join-requests",new{code}); var rid=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid(); var other=Client(f,Guid.NewGuid(),"Student"); var response=await other.GetAsync($"/api/v1/session-join-requests/{rid}"); Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SessionApproval_Retry_DoesNotDuplicateParticipant()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var code=await CodeAsync(f,s.Ids.Instructor,s.Ids.Session); var created=await Client(f,id,"Student").PostAsJsonAsync("/api/v1/session-join-requests",new{code}); var rid=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid(); var c=Client(f,s.Ids.Instructor,"Instructor"); await c.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/approve",new{}); var retry=await c.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/approve",new{}); Assert.True((int)retry.StatusCode<500); Assert.Equal(1,await scope.Platform.Participants.CountAsync(x=>x.SessionId==s.Ids.Session&&x.UserId==id));
    }

    [Fact]
    public async Task ApproveAfterRejection_DoesNotChangeTerminalState()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var code=await CodeAsync(f,s.Ids.Instructor,s.Ids.Session); var created=await Client(f,id,"Student").PostAsJsonAsync("/api/v1/session-join-requests",new{code}); var rid=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid(); var c=Client(f,s.Ids.Instructor,"Instructor"); await c.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/reject",new{}); await c.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/approve",new{}); Assert.Equal("Rejected",(await scope.Platform.SessionJoinRequests.SingleAsync(x=>x.Id==rid)).Status); Assert.Equal(0,await scope.Platform.Participants.CountAsync(x=>x.SessionId==s.Ids.Session&&x.UserId==id));
    }

    private static async Task<Guid> CreateRequestAsync(ClassroomScope scope, WorkflowIds ids, DiscoveryApiFactory f, Guid student)
    { var code=await CodeAsync(f,ids.Instructor,ids.Session); var r=await Client(f,student,"Student").PostAsJsonAsync("/api/v1/session-join-requests",new{code}); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid(); }

    [Fact]
    public async Task InstructorB_CannotListApproveOrRejectSessionJoinRequest()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var rid=await CreateRequestAsync(scope,s.Ids,f,id); var other=Client(f,s.Ids.OtherInstructor,"Instructor"); Assert.True((int)(await other.GetAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests")).StatusCode>=400); Assert.True((int)(await other.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/approve",new{})).StatusCode>=400); Assert.True((int)(await other.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/reject",new{})).StatusCode>=400); Assert.Equal("Pending",(await scope.Platform.SessionJoinRequests.SingleAsync(x=>x.Id==rid)).Status);
    }

    [Fact]
    public async Task Student_CannotApproveOrRejectSessionJoinRequest()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var rid=await CreateRequestAsync(scope,s.Ids,f,id); var student=Client(f,id,"Student"); Assert.Equal(HttpStatusCode.Forbidden,(await student.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/approve",new{})).StatusCode); Assert.Equal(HttpStatusCode.Forbidden,(await student.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/reject",new{})).StatusCode);
    }

    [Fact]
    public async Task SessionApproval_ConcurrentRequests_CreateOneParticipant()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var rid=await CreateRequestAsync(scope,s.Ids,f,id); using var a=Client(f,s.Ids.Instructor,"Instructor"); using var b=Client(f,s.Ids.Instructor,"Instructor"); var t1=a.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/approve",new{}); var t2=b.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/approve",new{}); var rs=await Task.WhenAll(t1,t2); Assert.All(rs,x=>Assert.True((int)x.StatusCode<500)); Assert.Equal(1,await scope.Platform.Participants.CountAsync(x=>x.SessionId==s.Ids.Session&&x.UserId==id));
    }

    [Fact]
    public async Task ApproveAndRejectSessionRequest_Concurrent_HasSingleTerminalOutcome()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var rid=await CreateRequestAsync(scope,s.Ids,f,id); using var i=Client(f,s.Ids.Instructor,"Instructor"); using var j=Client(f,s.Ids.Instructor,"Instructor"); var a=i.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/approve",new{}); var r=j.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/reject",new{}); var rs=await Task.WhenAll(a,r); Assert.All(rs,x=>Assert.True((int)x.StatusCode<500)); var state=(await scope.Platform.SessionJoinRequests.SingleAsync(x=>x.Id==rid)).Status; Assert.Contains(state,new[]{"Approved","Rejected"}); var participants=await scope.Platform.Participants.CountAsync(x=>x.SessionId==s.Ids.Session&&x.UserId==id); Assert.Equal(state=="Approved"?1:0,participants);
    }

    [Fact]
    public async Task RejectAfterApproval_DoesNotChangeTerminalState()
    {
        var s=await SetupAsync(); await using var scope=s.Scope; using var f=new DiscoveryApiFactory(database.ConnectionString); var id=await NewMemberAsync(scope,s.Ids); var rid=await CreateRequestAsync(scope,s.Ids,f,id); var c=Client(f,s.Ids.Instructor,"Instructor"); await c.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/approve",new{}); await c.PostAsJsonAsync($"/api/v1/sessions/{s.Ids.Session}/join-requests/{rid}/reject",new{}); Assert.Equal("Approved",(await scope.Platform.SessionJoinRequests.SingleAsync(x=>x.Id==rid)).Status); Assert.Equal(1,await scope.Platform.Participants.CountAsync(x=>x.SessionId==s.Ids.Session&&x.UserId==id));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Tests.Integration;

public sealed class ClassroomAdmissionAcceptanceTests : IClassFixture<ClassroomDatabase>
{
    private readonly ClassroomDatabase database;
    public ClassroomAdmissionAcceptanceTests(ClassroomDatabase database) => this.database = database;

    private static HttpClient Client(DiscoveryApiFactory factory, Guid user, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task<(ClassroomScope Scope, WorkflowIds Ids, Guid ClassroomId)> SetupAsync()
    {
        var scope = await database.CreateScope();
        var ids = await scope.BuildStartedSession();
        var classroom = await scope.Platform.Sessions.Where(x => x.Id == ids.Session).Select(x => x.ClassroomId).SingleAsync();
        return (scope, ids, classroom);
    }

    private static async Task<string> RotateAsync(HttpClient instructor, Guid classroomId)
    {
        var response = await instructor.PostAsJsonAsync($"/api/v1/classrooms/{classroomId}/join-code/rotate", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("joinCode").GetString()!;
    }

    [Fact]
    public async Task Instructor_CanRetrieveAndRotateSecureJoinCode()
    {
        var setup = await SetupAsync(); await using var scope = setup.Scope;
        using var factory = new DiscoveryApiFactory(database.ConnectionString);
        var instructor = Client(factory, setup.Ids.Instructor, "Instructor");
        var rotate = await instructor.PostAsJsonAsync($"/api/v1/classrooms/{setup.ClassroomId}/join-code/rotate", new { });
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        var first = await rotate.Content.ReadFromJsonAsync<JsonElement>();
        var codeA = first.GetProperty("joinCode").GetString();
        var get = await instructor.GetAsync($"/api/v1/classrooms/{setup.ClassroomId}/join-code");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(codeA, (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("joinCode").GetString());
        var second = await instructor.PostAsJsonAsync($"/api/v1/classrooms/{setup.ClassroomId}/join-code/rotate", new { });
        var codeB = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("joinCode").GetString();
        Assert.NotEqual(codeA, codeB);
        Assert.Equal(1, await setup.Scope.Platform.ClassroomJoinCodes.CountAsync(x => x.ClassroomId == setup.ClassroomId && x.IsActive));
        Assert.Equal(8, codeB!.Length);
    }

    [Fact]
    public async Task EnrolledStudent_SubmittingCode_DoesNotCreateDuplicateRequest()
    {
        var setup = await SetupAsync(); await using var scope = setup.Scope;
        using var factory = new DiscoveryApiFactory(database.ConnectionString);
        var instructor = Client(factory, setup.Ids.Instructor, "Instructor");
        var rotate = await instructor.PostAsJsonAsync($"/api/v1/classrooms/{setup.ClassroomId}/join-code/rotate", new { });
        var code = (await rotate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("joinCode").GetString()!;
        var student = Client(factory, setup.Ids.Student, "Student");
        var response = await student.PostAsJsonAsync("/api/v1/classroom-join-requests", new { code = code.ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Approved", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(1, await setup.Scope.Platform.Enrollments.CountAsync(x => x.ClassroomId == setup.ClassroomId && x.UserId == setup.Ids.Student));
        Assert.Equal(0, await setup.Scope.Platform.ClassroomJoinRequests.CountAsync(x => x.ClassroomId == setup.ClassroomId && x.StudentUserId == setup.Ids.Student));
    }

    [Fact]
    public async Task Student_CanRequestClassroomAdmission_WithValidCode_AndReadOwnRequest()
    {
        var setup = await SetupAsync(); await using var scope = setup.Scope;
        using var factory = new DiscoveryApiFactory(database.ConnectionString);
        var instructor = Client(factory, setup.Ids.Instructor, "Instructor"); var code = await RotateAsync(instructor, setup.ClassroomId);
        var studentId = Guid.NewGuid(); var student = Client(factory, studentId, "Student");
        var response = await student.PostAsJsonAsync("/api/v1/classroom-join-requests", new { code = code.ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var request = await response.Content.ReadFromJsonAsync<JsonElement>(); var requestId = request.GetProperty("requestId").GetGuid();
        Assert.Equal(1, await scope.Platform.ClassroomJoinRequests.CountAsync(x => x.Id == requestId && x.StudentUserId == studentId && x.ClassroomId == setup.ClassroomId && x.Status == "Pending"));
        var own = await student.GetAsync($"/api/v1/classroom-join-requests/{requestId}"); Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task InvalidAndRevokedCodes_AreRejected_AndDuplicatePendingIsSafe()
    {
        var setup = await SetupAsync(); await using var scope = setup.Scope;
        using var factory = new DiscoveryApiFactory(database.ConnectionString);
        var instructor = Client(factory, setup.Ids.Instructor, "Instructor"); var oldCode = await RotateAsync(instructor, setup.ClassroomId); var newCode = await RotateAsync(instructor, setup.ClassroomId);
        var studentId = Guid.NewGuid(); var student = Client(factory, studentId, "Student");
        Assert.True((int)(await student.PostAsJsonAsync("/api/v1/classroom-join-requests", new { code = oldCode })).StatusCode >= 400);
        var first = await student.PostAsJsonAsync("/api/v1/classroom-join-requests", new { code = newCode });
        var second = await student.PostAsJsonAsync("/api/v1/classroom-join-requests", new { code = newCode });
        Assert.True((int)first.StatusCode is >= 200 and < 300); Assert.True((int)second.StatusCode is >= 200 and < 300);
        Assert.Equal(1, await scope.Platform.ClassroomJoinRequests.CountAsync(x => x.ClassroomId == setup.ClassroomId && x.StudentUserId == studentId && x.Status == "Pending"));
    }

    [Fact]
    public async Task Instructor_CanListAndApprovePendingRequest_CreatingEnrollment()
    {
        var setup = await SetupAsync(); await using var scope = setup.Scope;
        using var factory = new DiscoveryApiFactory(database.ConnectionString);
        var instructor = Client(factory, setup.Ids.Instructor, "Instructor"); var code = await RotateAsync(instructor, setup.ClassroomId);
        var studentId = Guid.NewGuid(); var student = Client(factory, studentId, "Student");
        var created = await student.PostAsJsonAsync("/api/v1/classroom-join-requests", new { code }); var requestId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid();
        var list = await instructor.GetAsync($"/api/v1/classrooms/{setup.ClassroomId}/join-requests"); Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var approved = await instructor.PostAsJsonAsync($"/api/v1/classrooms/{setup.ClassroomId}/join-requests/{requestId}/approve", new { }); Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(1, await scope.Platform.Enrollments.CountAsync(x => x.ClassroomId == setup.ClassroomId && x.UserId == studentId));
        Assert.Equal("Approved", (await scope.Platform.ClassroomJoinRequests.SingleAsync(x => x.Id == requestId)).Status);
    }

    [Fact]
    public async Task StudentAndOtherInstructor_CannotManagePendingQueue()
    {
        var setup = await SetupAsync(); await using var scope = setup.Scope;
        using var factory = new DiscoveryApiFactory(database.ConnectionString);
        var student = Client(factory, Guid.NewGuid(), "Student"); var other = Client(factory, setup.Ids.OtherInstructor, "Instructor");
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/v1/classrooms/{setup.ClassroomId}/join-requests")).StatusCode);
        Assert.True((int)(await other.GetAsync($"/api/v1/classrooms/{setup.ClassroomId}/join-requests")).StatusCode >= 400);
    }
}

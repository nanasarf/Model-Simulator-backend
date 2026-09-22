using System.Net;
using System.Text.Json;
using System.Net.Http.Json;
using SimulationPlatform.Simulations.Core.Authoring;

namespace Tests.Integration;

public sealed class AiMvp1ProposalFixture : IAsyncLifetime
{
    private ClassroomDatabase? _database;
    public Guid InstructorA { get; } = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public Guid InstructorB { get; } = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public Guid StudentA { get; } = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public string ConnectionString => _database?.ConnectionString ?? throw new InvalidOperationException("Fixture not initialized.");
    public Task InitializeAsync() { _database = new ClassroomDatabase(); return _database.InitializeAsync(); }
    public Task DisposeAsync() => _database?.DisposeAsync() ?? Task.CompletedTask;
    public ScenarioProposalTestClient Client(Guid actor, string role = "Instructor") => new(new DiscoveryApiFactory(ConnectionString).CreateClient(), actor, role);
}

public sealed class ScenarioProposalTestClient(HttpClient client, Guid actor, string role)
{
    public async Task<HttpResponseMessage> CreateAsync(ScenarioBlueprint blueprint, CancellationToken ct = default)
    { using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/scenario-proposals") { Content = JsonContent.Create(blueprint) }; return await SendAsync(request, ct); }
    public async Task<HttpResponseMessage> ValidateAsync(Guid id, CancellationToken ct = default) => await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/scenario-proposals/{id}/validate"), ct);
    public async Task<HttpResponseMessage> ApproveAsync(Guid id, long version, string key, CancellationToken ct = default) => await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/scenario-proposals/{id}/approve") { Content = JsonContent.Create(new { expectedVersion = version }) }, key, ct);
    public async Task<HttpResponseMessage> RejectAsync(Guid id, long version, string key, CancellationToken ct = default) => await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/scenario-proposals/{id}/reject") { Content = JsonContent.Create(new { expectedVersion = version }) }, key, ct);
    public async Task<HttpResponseMessage> GetAsync(Guid id, CancellationToken ct = default) => await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/v1/scenario-proposals/{id}"), ct);
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => await SendAsync(request, null, ct);
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string? key, CancellationToken ct)
    { request.Headers.Add("X-Test-Role", role); request.Headers.Add("X-Test-User", actor.ToString()); if (key is not null) request.Headers.Add("Idempotency-Key", key); return await client.SendAsync(request, ct); }
}

public static class ScenarioBlueprintFixtures
{
    public static ScenarioBlueprint ValidShortRunMacro() => Empty("Economics.ShortRunMacro");
    public static ScenarioBlueprint ValidCompetitiveMarket() => Empty("Economics.CompetitiveMarket");
    public static ScenarioBlueprint WithUnsupportedRole(ScenarioBlueprint blueprint) => blueprint with { Roles = blueprint.Roles.Append(new RoleBlueprint("UNSUPPORTED", "Unsupported", "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), 1, 1)).ToArray() };
    public static ScenarioBlueprint WithUnsupportedCapability(ScenarioBlueprint blueprint) => blueprint with { Decisions = blueprint.Decisions.Append(new DecisionBlueprint("UNSUPPORTED", "Unsupported", "", blueprint.Roles.Select(x => x.Code).ToArray(), "UNSUPPORTED", Array.Empty<string>(), Array.Empty<string>())).ToArray() };
    public static ScenarioBlueprint MakeInvalidForApproval(ScenarioBlueprint blueprint) => WithUnsupportedCapability(blueprint);
    private static ScenarioBlueprint Empty(string model)
    {
        var roles = model == "Economics.ShortRunMacro"
            ? new[] { "GOVERNMENT", "CENTRAL_BANK", "BUSINESS", "HOUSEHOLD_LABOR" }
            : new[] { "BUYER", "SELLER", "GOVERNMENT" };
        return new("1.0", "Integration proposal", "Summary", "Briefing", model, model, "1.0.0", 1,
            new[] { new LearningObjectiveBlueprint("objective-1", "Understand model decisions") },
            roles.Select(x => new RoleBlueprint(x, x, x, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), 1, 1)).ToArray(),
            Array.Empty<DecisionBlueprint>(), Array.Empty<RuleBlueprint>(), null, Array.Empty<ShockBlueprint>(), null, null);
    }
}

public sealed class AiMvp1ProposalSmokeTests : IClassFixture<AiMvp1ProposalFixture>
{
    private readonly AiMvp1ProposalFixture fixture;
    public AiMvp1ProposalSmokeTests(AiMvp1ProposalFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task MacroProposal_CanBeCreatedAndValidated()
    {
        var client = fixture.Client(fixture.InstructorA);
        var created = await client.CreateAsync(ScenarioBlueprintFixtures.ValidShortRunMacro());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        var validation = await client.ValidateAsync(id);
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        var report = await validation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(report.GetProperty("blockers").EnumerateArray());
    }

    [Fact]
    public async Task CompetitiveMarketProposal_CanBeCreatedAndValidated()
    {
        var client = fixture.Client(fixture.InstructorA);
        var created = await client.CreateAsync(ScenarioBlueprintFixtures.ValidCompetitiveMarket());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var validation = await client.ValidateAsync(body.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        var report = await validation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(report.GetProperty("blockers").EnumerateArray());
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SimulationPlatform.Application.Actions;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Application.Rules;
using SimulationPlatform.Application.Runtime;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Runtime;
using SimulationPlatform.Identity;
using SimulationPlatform.Infrastructure;
using SimulationPlatform.Infrastructure.Persistence;
using SimulationPlatform.Simulations.Core.Contracts;
using SimulationPlatform.Simulations.Economics.SupplyDemand;
using SimulationPlatform.Simulations.Economics.Macroeconomics;

namespace Tests.Integration;

public sealed class ClassroomWorkflowPostgresTests : IClassFixture<ClassroomDatabase>
{
    private readonly ClassroomDatabase database;
    public ClassroomWorkflowPostgresTests(ClassroomDatabase database) => this.database = database;

    [Fact]
    public async Task Instructor_to_student_round_is_authoritative_recoverable_and_role_projected()
    {
        await using var scope = await database.CreateScope();
        var ids = await scope.BuildStartedSession();
        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Decision, default);

        var payload = JsonSerializer.SerializeToElement(new { direction = "increase" });
        var submission = await scope.Submit.HandleAsync(new(ids.Session, ids.Team, ids.Student, ids.Assignment,
            "CHANGE_OUTPUT", payload, "submit-1"), default);
        var duplicate = await scope.Submit.HandleAsync(new(ids.Session, ids.Team, ids.Student, ids.Assignment,
            "CHANGE_OUTPUT", payload, "submit-1"), default);
        Assert.Equal(submission.Id, duplicate.Id);

        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Locked, default);
        var result = await scope.Execute.HandleAsync(new(ids.Session, ids.Team, ids.Instructor, Guid.NewGuid(), "test"), default);
        Assert.Equal(45m, result.Metrics["marketPrice"]);
        Assert.Equal(55m, result.Metrics["quantityTraded"]);

        var recovered = await scope.Workflow.RecoverAsync(ids.Student, false, ids.Session, default);
        Assert.Equal(SessionPhases.Results, recovered.Phase);
        Assert.Equal(45m, recovered.VisibleState!.Value.GetProperty("Price").GetDecimal());
        Assert.True(recovered.VisibleState.Value.TryGetProperty("DemandIntercept", out _));
        var history = await scope.Workflow.HistoryAsync(ids.Student, false, ids.Session, default);
        Assert.Contains(history, x => x.Type == "SimulationExecuted");
        Assert.True(await scope.Platform.Outbox.CountAsync() > 0);
        Assert.True(await scope.Platform.AuditRecords.CountAsync() > 0);
    }

    [Fact]
    public async Task Ownership_simulation_authority_manifest_freezing_and_concurrency_are_enforced()
    {
        await using var scope = await database.CreateScope();
        var ids = await scope.BuildStartedSession();
        await Assert.ThrowsAsync<DomainException>(() => scope.Workflow.PauseSessionAsync(ids.OtherInstructor, ids.Session, default).AsTask());

        var manifest = await scope.Platform.SessionManifests.SingleAsync(x => x.SessionId == ids.Session);
        var frozen = manifest.ManifestJson;
        var published = await scope.Platform.ScenarioVersions.SingleAsync(x => x.Id == ids.Scenario);
        published.ManifestJson = published.ManifestJson.Replace("VIEW_CURVES", "VIEW_NOTHING", StringComparison.Ordinal);
        foreach (var rule in scope.Platform.Rules.Where(x => x.ScenarioVersionId == ids.Scenario)) rule.Effect = "Deny";
        await scope.Platform.SaveChangesAsync();
        var persistedFrozen = (await scope.Platform.SessionManifests.AsNoTracking().SingleAsync(x => x.SessionId == ids.Session)).ManifestJson;
        using var expectedManifest = JsonDocument.Parse(frozen);
        using var actualManifest = JsonDocument.Parse(persistedFrozen);
        Assert.True(JsonElement.DeepEquals(expectedManifest.RootElement, actualManifest.RootElement));

        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Decision, default);
        var accepted = await scope.Submit.HandleAsync(new(ids.Session, ids.Team, ids.Student, ids.Assignment,
            "CHANGE_OUTPUT", JsonSerializer.SerializeToElement(new { direction = "decrease" }), "frozen-rule"), default);
        Assert.NotEqual(Guid.Empty, accepted.Id);
        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Locked, default);

        await using var contender = await database.CreateScope();
        var attempts = new[]
        {
            scope.Execute.HandleAsync(new(ids.Session, ids.Team, ids.Instructor, Guid.NewGuid(), "race-a"), default).AsTask(),
            contender.Execute.HandleAsync(new(ids.Session, ids.Team, ids.Instructor, Guid.NewGuid(), "race-b"), default).AsTask()
        };
        var succeeded = await Task.WhenAll(attempts.Select(async task => { try { await task; return true; } catch { return false; } }));
        Assert.Equal(1, succeeded.Count(x => x));
        await using var verify = await database.CreateScope();
        Assert.Equal(1, await verify.Platform.Executions.CountAsync(x => x.SessionId == ids.Session && x.TeamId == ids.Team && x.RoundNumber == 1));
        Assert.Equal(1, await verify.Platform.Snapshots.CountAsync(x => x.SessionId == ids.Session && x.TeamId == ids.Team && x.RoundNumber == 1));
    }
}

public sealed class ClassroomDatabase : IAsyncLifetime
{
    private readonly string databaseName = "simulation_m2_" + Guid.NewGuid().ToString("N");
    private const string Admin = "Host=localhost;Port=5432;Database=postgres;Username=simulation_platform;Password=local-development-only";
    public string ConnectionString => $"Host=localhost;Port=5432;Database={databaseName};Username=simulation_platform;Password=local-development-only";

    public async Task InitializeAsync()
    {
        await using (var connection = new NpgsqlConnection(Admin))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(ConnectionString).Options);
        await platform.Database.MigrateAsync();
        await using var identity = new IdentityDataContext(new DbContextOptionsBuilder<IdentityDataContext>().UseNpgsql(ConnectionString).Options);
        await identity.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(Admin);
        await connection.OpenAsync();
        await using var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name", connection);
        terminate.Parameters.AddWithValue("name", databaseName); await terminate.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", connection);
        await drop.ExecuteNonQueryAsync();
    }

    public async Task<ClassroomScope> CreateScope()
    {
        var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(ConnectionString).Options);
        var identity = new IdentityDataContext(new DbContextOptionsBuilder<IdentityDataContext>().UseNpgsql(ConnectionString).Options);
        await Task.CompletedTask;
        return new ClassroomScope(platform, identity);
    }
}

public sealed class ClassroomScope : IAsyncDisposable
{
    public PlatformDbContext Platform { get; }
    private IdentityDataContext Identity { get; }
    public EfClassroomWorkflow Workflow { get; }
    public SubmitActionHandler Submit { get; }
    public ExecuteRoundHandler Execute { get; }
    private static readonly FixedClock Clock = new();

    public ClassroomScope(PlatformDbContext platform, IdentityDataContext identity)
    {
        Platform = platform; Identity = identity;
        var registry = new SimulationModelRegistry(new ISimulationModel[] { new SupplyDemandModel(), new ShortRunMacroModel() });
        Workflow = new(platform, identity, registry, Clock);
        var store = new EfRuntimeStore(platform); var transactions = new EfTransactionRunner(platform);
        Submit = new(store, store, registry, Clock, transactions, new EfActionRuleEvaluator(platform, new RuleEngine()));
        Execute = new(store, store, store, registry, transactions, Clock, new EfAuditWriter(platform));
    }

    public async Task<WorkflowIds> BuildStartedSession()
    {
        var instructor = await AddUser(PlatformRoles.Instructor);
        var other = await AddUser(PlatformRoles.Instructor);
        var student = await AddUser(PlatformRoles.Student);
        var course = await Workflow.CreateCourseAsync(instructor, "ECON-101-" + Guid.NewGuid().ToString("N")[..6], "Markets", default);
        var classroom = await Workflow.CreateClassroomAsync(instructor, course, "Section A", default);
        await Workflow.EnrollAsync(instructor, classroom, student, default);
        var definition = await Workflow.CreateDefinitionAsync(instructor, "Supply and demand", default);
        var scenario = await Workflow.PublishScenarioAsync(instructor, definition, "Market round", Manifest(), default);
        var session = await Workflow.CreateSessionAsync(instructor, classroom, scenario, 12345, default);
        var team = await Workflow.CreateTeamAsync(instructor, session, "Blue", default);
        await Workflow.AddTeamMemberAsync(instructor, session, team, student, default);
        var assignment = await Workflow.AssignRoleAsync(instructor, session, team, student, "PRODUCER", default);
        await Workflow.SetReadyAsync(student, session, true, default);
        await Workflow.StartSessionAsync(instructor, session, default);
        return new(instructor, other, student, scenario, session, team, assignment);
    }

    public async Task<MacroWorkflowIds> BuildStartedMacroSession()
    {
        var instructor = await AddUser(PlatformRoles.Instructor);
        var other = await AddUser(PlatformRoles.Instructor);
        var students = new[] { await AddUser(PlatformRoles.Student), await AddUser(PlatformRoles.Student),
            await AddUser(PlatformRoles.Student), await AddUser(PlatformRoles.Student) };
        var course = await Workflow.CreateCourseAsync(instructor, "MACRO-" + Guid.NewGuid().ToString("N")[..6], "Macro Lab", default);
        var classroom = await Workflow.CreateClassroomAsync(instructor, course, "Country Cabinet", default);
        foreach (var student in students) await Workflow.EnrollAsync(instructor, classroom, student, default);
        var definition = await Workflow.CreateDefinitionAsync(instructor, "Short-run country", default);
        var scenario = await Workflow.PublishScenarioAsync(instructor, definition, "Two-quarter stabilization", MacroManifest(), default);
        var session = await Workflow.CreateSessionAsync(instructor, classroom, scenario, 4242, default);
        var team = await Workflow.CreateTeamAsync(instructor, session, "Novara", default);
        var roles = new[] { "GOVERNMENT", "CENTRAL_BANK", "BUSINESS", "HOUSEHOLD_LABOR" };
        var assignments = new List<Guid>();
        for (var i = 0; i < students.Length; i++)
        {
            await Workflow.AddTeamMemberAsync(instructor, session, team, students[i], default);
            assignments.Add(await Workflow.AssignRoleAsync(instructor, session, team, students[i], roles[i], default));
            await Workflow.SetReadyAsync(students[i], session, true, default);
        }
        await Workflow.StartSessionAsync(instructor, session, default);
        return new(instructor, other, students, scenario, session, team, assignments.ToArray());
    }

    public async Task<(Guid Instructor, Guid Session, Guid[] Teams, Guid[] Students, Guid[] Assignments)> BuildTwoTeamSession()
    {
        var instructor = await AddUser(PlatformRoles.Instructor);
        var students = new[] { await AddUser(PlatformRoles.Student), await AddUser(PlatformRoles.Student) };
        var course = await Workflow.CreateCourseAsync(instructor, "TWO-" + Guid.NewGuid().ToString("N")[..6], "Two Teams", default);
        var classroom = await Workflow.CreateClassroomAsync(instructor, course, "Section", default);
        foreach (var student in students) await Workflow.EnrollAsync(instructor, classroom, student, default);
        var definition = await Workflow.CreateDefinitionAsync(instructor, "Market", default);
        var scenario = await Workflow.PublishScenarioAsync(instructor, definition, "Market", Manifest(), default);
        var session = await Workflow.CreateSessionAsync(instructor, classroom, scenario, 7, default);
        var teams = new[] { await Workflow.CreateTeamAsync(instructor, session, "A", default),
            await Workflow.CreateTeamAsync(instructor, session, "B", default) };
        var assignments = new Guid[2];
        for (var i = 0; i < 2; i++)
        {
            await Workflow.AddTeamMemberAsync(instructor, session, teams[i], students[i], default);
            assignments[i] = await Workflow.AssignRoleAsync(instructor, session, teams[i], students[i], "PRODUCER", default);
            await Workflow.SetReadyAsync(students[i], session, true, default);
        }
        await Workflow.StartSessionAsync(instructor, session, default);
        return (instructor, session, teams, students, assignments);
    }

    private async Task<Guid> AddUser(string roleName)
    {
        var role = await Identity.Roles.SingleOrDefaultAsync(x => x.Name == roleName);
        if (role is null) { role = new ApplicationRole { Id = Guid.NewGuid(), Name = roleName, NormalizedName = roleName.ToUpperInvariant() }; Identity.Roles.Add(role); }
        var id = Guid.NewGuid();
        Identity.Users.Add(new ApplicationUser { Id = id, UserName = id + "@test.local", NormalizedUserName = (id + "@test.local").ToUpperInvariant(), IsActive = true, CreatedAt = Clock.UtcNow, SecurityStamp = Guid.NewGuid().ToString() });
        Identity.UserRoles.Add(new IdentityUserRole<Guid> { UserId = id, RoleId = role.Id });
        await Identity.SaveChangesAsync(); return id;
    }

    private static ScenarioManifest Manifest()
    {
        var condition = JsonSerializer.SerializeToElement(new { kind = "all", children = new object[]
        {
            new { kind = "comparison", fact = "runtime.phase", @operator = "eq", value = SessionPhases.Decision },
            new { kind = "contains", fact = "actor.capabilities", value = "CHANGE_OUTPUT" }
        }});
        return new("Economics.SupplyDemand", "1.0.0", 1, JsonSerializer.SerializeToElement(new { }),
            new List<string> { SessionPhases.Briefing, SessionPhases.Decision, SessionPhases.Locked, SessionPhases.Simulation, SessionPhases.Results, SessionPhases.Reflection, SessionPhases.Completed },
            new Dictionary<string, HashSet<string>>
            {
                [SessionPhases.Briefing] = new HashSet<string> { SessionPhases.Decision },
                [SessionPhases.Decision] = new HashSet<string> { SessionPhases.Locked },
                [SessionPhases.Locked] = new HashSet<string> { SessionPhases.Simulation },
                [SessionPhases.Simulation] = new HashSet<string> { SessionPhases.Results },
                [SessionPhases.Results] = new HashSet<string> { SessionPhases.Reflection },
                [SessionPhases.Reflection] = new HashSet<string> { SessionPhases.Completed }
            },
            new List<RoleManifest> { new("PRODUCER", "Producer", 1, 1, new HashSet<string> { "CHANGE_OUTPUT", "VIEW_CURVES" }) },
            new List<ActionManifest> { new("CHANGE_OUTPUT", "CHANGE_OUTPUT", new HashSet<string> { SessionPhases.Decision }) },
            new List<RuleManifest> { new(Guid.NewGuid(), 1, "Allow", condition) });
    }

    private static ScenarioManifest MacroManifest()
    {
        var phases = new List<string> { SessionPhases.Briefing, "Prediction", SessionPhases.Decision,
            SessionPhases.Locked, SessionPhases.Simulation, SessionPhases.Results, "Discussion", SessionPhases.Completed };
        var transitions = new Dictionary<string, HashSet<string>>
        {
            [SessionPhases.Briefing] = ["Prediction"], ["Prediction"] = [SessionPhases.Decision],
            [SessionPhases.Decision] = [SessionPhases.Locked], [SessionPhases.Locked] = [SessionPhases.Simulation],
            [SessionPhases.Simulation] = [SessionPhases.Results], [SessionPhases.Results] = ["Discussion"],
            ["Discussion"] = [SessionPhases.Briefing, SessionPhases.Completed]
        };
        var roles = new List<RoleManifest>
        {
            new("GOVERNMENT", "Government", 1, 1, [MacroCapabilities.SubmitPrediction, MacroCapabilities.SetFiscalPolicy, MacroCapabilities.ViewFiscal]),
            new("CENTRAL_BANK", "Central Bank", 1, 1, [MacroCapabilities.SubmitPrediction, MacroCapabilities.SetMonetaryPolicy, MacroCapabilities.ViewMonetary]),
            new("BUSINESS", "Business", 1, 1, [MacroCapabilities.SubmitPrediction, MacroCapabilities.SetBusinessStrategy, MacroCapabilities.ViewBusiness]),
            new("HOUSEHOLD_LABOR", "Household/Labor", 1, 1, [MacroCapabilities.SubmitPrediction, MacroCapabilities.SetHouseholdLaborStance, MacroCapabilities.ViewHousehold])
        };
        var actions = new List<ActionManifest>
        {
            new(MacroActions.DirectionalPrediction, MacroCapabilities.SubmitPrediction, ["Prediction"]),
            new(MacroActions.FiscalPolicy, MacroCapabilities.SetFiscalPolicy, [SessionPhases.Decision]),
            new(MacroActions.MonetaryPolicy, MacroCapabilities.SetMonetaryPolicy, [SessionPhases.Decision]),
            new(MacroActions.BusinessStrategy, MacroCapabilities.SetBusinessStrategy, [SessionPhases.Decision]),
            new(MacroActions.HouseholdLaborStance, MacroCapabilities.SetHouseholdLaborStance, [SessionPhases.Decision])
        };
        var config = new MacroConfiguration(ScheduledShocks:
            [new ScheduledMacroShock(1, "supply_disruption", PolicyIntensity.Moderate)]);
        var rules = new List<RuleManifest>
        {
            new(Guid.NewGuid(), 100, "Deny", JsonSerializer.SerializeToElement(new
                { kind = "comparison", fact = "submission.count", @operator = "gte", value = 1 })),
            new(Guid.NewGuid(), 0, "Allow", JsonSerializer.SerializeToElement(new
                { kind = "exists", fact = "action.code" }))
        };
        return new("Economics.ShortRunMacro", "1.0.0", 1, JsonSerializer.SerializeToElement(config), phases,
            transitions, roles, actions, rules, ["Prediction", SessionPhases.Decision], 2);
    }

    public async ValueTask DisposeAsync() { await Platform.DisposeAsync(); await Identity.DisposeAsync(); }
}

public sealed record WorkflowIds(Guid Instructor, Guid OtherInstructor, Guid Student, Guid Scenario, Guid Session, Guid Team, Guid Assignment);
public sealed record MacroWorkflowIds(Guid Instructor, Guid OtherInstructor, Guid[] Students, Guid Scenario, Guid Session, Guid Team, Guid[] Assignments);
public sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

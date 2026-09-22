using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SimulationPlatform.Application.Abstractions;

namespace Tests.Integration;

internal sealed class ProposalCommandInjectedFailureException(ProposalCommandCheckpoint checkpoint)
    : Exception($"Injected proposal command failure at {checkpoint}.");

internal sealed class ThrowingProposalCommandFaultInjector(ProposalCommandCheckpoint checkpoint) : IProposalCommandFaultInjector
{
    public List<ProposalCommandCheckpoint> Reached { get; } = [];
    public Task CheckpointAsync(ProposalCommandCheckpoint value, CancellationToken cancellationToken)
    {
        Reached.Add(value);
        if (value == checkpoint) throw new ProposalCommandInjectedFailureException(value);
        return Task.CompletedTask;
    }
}

internal sealed class FaultInjectingApiFactory(string connectionString, ProposalCommandCheckpoint checkpoint) : SecureApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ConnectionStrings:Platform", connectionString);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProposalCommandFaultInjector>();
            services.AddScoped<IProposalCommandFaultInjector>(_ => new ThrowingProposalCommandFaultInjector(checkpoint));
        });
    }
}

public sealed class AiMvp1RollbackAcceptanceTests : IClassFixture<AiMvp1ProposalFixture>
{
    private readonly AiMvp1ProposalFixture fixture;
    public AiMvp1RollbackAcceptanceTests(AiMvp1ProposalFixture fixture) => this.fixture = fixture;

    public static IEnumerable<object[]> ApprovalCheckpoints() => Enum.GetValues<ProposalCommandCheckpoint>()
        .Select(x => new object[] { x });
    public static IEnumerable<object[]> RejectionCheckpoints() => new[]
    {
        ProposalCommandCheckpoint.AfterProposalMutated, ProposalCommandCheckpoint.AfterAuditAdded,
        ProposalCommandCheckpoint.AfterOutboxAdded, ProposalCommandCheckpoint.BeforeIdempotencyCompleted
    }.Select(x => new object[] { x });

    private async Task<(Guid id, long version)> SetupAsync()
    {
        var client = fixture.Client(fixture.InstructorA);
        var created = await client.CreateAsync(ScenarioBlueprintFixtures.ValidShortRunMacro());
        var id = (await client.ReadJsonAsync(created)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.ValidateAsync(id)).StatusCode);
        var row = await fixture.Persistence.GetProposalAsync(id);
        return (id, row!.Version);
    }

    [Theory]
    [MemberData(nameof(ApprovalCheckpoints))]
    public async Task Approve_WhenFaultInjectedAtCheckpoint_RollsBackEntireTransaction(ProposalCommandCheckpoint checkpoint)
    {
        var (id, version) = await SetupAsync();
        var definitionsBefore = await fixture.Persistence.CountSimulationDefinitionsAsync(fixture.InstructorA, "Economics.ShortRunMacro", "1.0.0");
        var factory = new FaultInjectingApiFactory(fixture.ConnectionString, checkpoint);
        var client = new ScenarioProposalTestClient(factory.CreateClient(), fixture.InstructorA, "Instructor");
        var response = await client.ApproveAsync(id, version, "rollback-approval-" + checkpoint);
        Assert.True((int)response.StatusCode >= 500);
        var row = await fixture.Persistence.GetProposalAsync(id);
        Assert.Equal("Validated", row!.Status); Assert.Null(row.LinkedDraftId); Assert.Equal(version, row.Version);
        Assert.Equal(0, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
        Assert.Empty(await fixture.Persistence.GetProposalConversionAuditsAsync(id));
        Assert.Empty(await fixture.Persistence.GetProposalConversionOutboxAsync(id));
        Assert.Equal(0, await fixture.Persistence.CountCompletedIdempotencyEntriesAsync(fixture.InstructorA, "rollback-approval-" + checkpoint));
        Assert.Equal(definitionsBefore, await fixture.Persistence.CountSimulationDefinitionsAsync(fixture.InstructorA, "Economics.ShortRunMacro", "1.0.0"));
        await factory.DisposeAsync();
    }

    [Theory]
    [MemberData(nameof(RejectionCheckpoints))]
    public async Task Reject_WhenFaultInjectedAtCheckpoint_RollsBackEntireTransaction(ProposalCommandCheckpoint checkpoint)
    {
        var (id, version) = await SetupAsync();
        var factory = new FaultInjectingApiFactory(fixture.ConnectionString, checkpoint);
        var client = new ScenarioProposalTestClient(factory.CreateClient(), fixture.InstructorA, "Instructor");
        var response = await client.RejectAsync(id, version, "rollback-rejection-" + checkpoint);
        Assert.True((int)response.StatusCode >= 500);
        var row = await fixture.Persistence.GetProposalAsync(id);
        Assert.Equal("Validated", row!.Status); Assert.Equal(version, row.Version);
        Assert.Empty(await fixture.Persistence.GetProposalRejectionAuditsAsync(id));
        Assert.Empty(await fixture.Persistence.GetProposalRejectionOutboxAsync(id));
        Assert.Equal(0, await fixture.Persistence.CountCompletedIdempotencyEntriesAsync(fixture.InstructorA, "rollback-rejection-" + checkpoint));
        await factory.DisposeAsync();
    }
}

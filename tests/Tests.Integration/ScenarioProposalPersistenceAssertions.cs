using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Infrastructure.Persistence;

namespace Tests.Integration;

/// <summary>Read-only persistence assertions for proposal acceptance tests.</summary>
public sealed class ScenarioProposalPersistenceAssertions(string connectionString)
{
    private PlatformDbContext CreateContext() => new(new DbContextOptionsBuilder<PlatformDbContext>()
        .UseNpgsql(connectionString).Options);

    public async Task<ScenarioProposalRow?> GetProposalAsync(Guid proposalId, CancellationToken ct = default)
    {
        await using var db = CreateContext();
        return await db.ScenarioProposals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proposalId, ct);
    }

    public async Task<IReadOnlyList<ScenarioProposalRevisionRow>> GetProposalRevisionsAsync(Guid proposalId, CancellationToken ct = default)
    {
        await using var db = CreateContext();
        return await db.ScenarioProposalRevisions.AsNoTracking().Where(x => x.ProposalId == proposalId)
            .OrderBy(x => x.RevisionNumber).ToListAsync(ct);
    }

    public async Task<int> CountProposalRevisionsAsync(Guid proposalId, CancellationToken ct = default)
    {
        await using var db = CreateContext();
        return await db.ScenarioProposalRevisions.CountAsync(x => x.ProposalId == proposalId, ct);
    }

    public async Task<IReadOnlyList<ScenarioProposalValidationRow>> GetProposalValidationsAsync(Guid proposalId, CancellationToken ct = default)
    {
        await using var db = CreateContext();
        return await db.ScenarioProposalValidations.AsNoTracking().Where(x => x.ProposalId == proposalId)
            .OrderBy(x => x.ValidatedAt).ToListAsync(ct);
    }

    public async Task<int> CountProposalValidationsAsync(Guid proposalId, CancellationToken ct = default)
    {
        await using var db = CreateContext();
        return await db.ScenarioProposalValidations.CountAsync(x => x.ProposalId == proposalId, ct);
    }

    public async Task<ScenarioDraftRow?> GetScenarioDraftAsync(Guid draftId, CancellationToken ct = default)
    {
        await using var db = CreateContext();
        return await db.ScenarioDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == draftId, ct);
    }

    public async Task<ScenarioDraftRow?> GetLinkedDraftAsync(Guid proposalId, CancellationToken ct = default)
    {
        await using var db = CreateContext();
        var draftId = await db.ScenarioProposals.AsNoTracking().Where(x => x.Id == proposalId)
            .Select(x => x.LinkedDraftId).SingleOrDefaultAsync(ct);
        return draftId is null ? null : await db.ScenarioDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == draftId, ct);
    }

    public async Task<int> CountDraftsLinkedToProposalAsync(Guid proposalId, CancellationToken ct = default)
        => await GetLinkedDraftAsync(proposalId, ct) is null ? 0 : 1;

    // SimulationDefinitionRow intentionally stores ownership/name only. Approval uses
    // the model identifier as its name; model version is carried by the draft content.
    public async Task<IReadOnlyList<SimulationDefinitionRow>> GetSimulationDefinitionsAsync(
        Guid ownerUserId, string modelIdentifier, string modelVersion, CancellationToken ct = default)
    {
        await using var db = CreateContext();
        return await db.SimulationDefinitions.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.Name == modelIdentifier).ToListAsync(ct);
    }

    public async Task<int> CountSimulationDefinitionsAsync(Guid ownerUserId, string modelIdentifier, string modelVersion, CancellationToken ct = default)
        => (await GetSimulationDefinitionsAsync(ownerUserId, modelIdentifier, modelVersion, ct)).Count;

    public async Task<IdempotencyRow?> GetIdempotencyAsync(Guid actorUserId, string key, CancellationToken ct = default)
    {
        await using var db = CreateContext();
        return await db.IdempotencyRecords.AsNoTracking().Where(x => x.UserId == actorUserId && x.Key == key)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
    }

    public async Task<int> CountIdempotencyEntriesAsync(Guid actorUserId, string key, CancellationToken ct = default)
    {
        await using var db = CreateContext();
        return await db.IdempotencyRecords.CountAsync(x => x.UserId == actorUserId && x.Key == key, ct);
    }

    public async Task<int> CountCompletedIdempotencyEntriesAsync(Guid actorUserId, string key, CancellationToken ct = default)
    {
        await using var db = CreateContext();
        return await db.IdempotencyRecords.CountAsync(x => x.UserId == actorUserId && x.Key == key && x.CompletedAt != null, ct);
    }

    public Task<IReadOnlyList<AuditRow>> GetProposalConversionAuditsAsync(Guid proposalId, CancellationToken ct = default)
        => GetProposalAuditsAsync(proposalId, "ScenarioProposalConvertedToDraft", ct);

    public Task<IReadOnlyList<AuditRow>> GetProposalRejectionAuditsAsync(Guid proposalId, CancellationToken ct = default)
        => GetProposalAuditsAsync(proposalId, "ScenarioProposalRejected", ct);

    private async Task<IReadOnlyList<AuditRow>> GetProposalAuditsAsync(Guid proposalId, string action, CancellationToken ct)
    {
        await using var db = CreateContext();
        return await db.AuditRecords.AsNoTracking().Where(x => x.ResourceType == "ScenarioProposal" &&
            x.ResourceId == proposalId.ToString() && x.Action == action).OrderBy(x => x.OccurredAt).ToListAsync(ct);
    }

    public Task<IReadOnlyList<OutboxMessage>> GetProposalConversionOutboxAsync(Guid proposalId, CancellationToken ct = default)
        => GetProposalOutboxAsync(proposalId, "ScenarioProposalConvertedToDraft", ct);

    public Task<IReadOnlyList<OutboxMessage>> GetProposalRejectionOutboxAsync(Guid proposalId, CancellationToken ct = default)
        => GetProposalOutboxAsync(proposalId, "ScenarioProposalRejected", ct);

    private async Task<IReadOnlyList<OutboxMessage>> GetProposalOutboxAsync(Guid proposalId, string type, CancellationToken ct)
    {
        await using var db = CreateContext();
        return await db.Outbox.AsNoTracking().Where(x => x.AggregateId == proposalId && x.Type == type)
            .OrderBy(x => x.OccurredAt).ToListAsync(ct);
    }
}

public sealed class AiMvp1ProposalPersistenceHelperTests : IClassFixture<AiMvp1ProposalFixture>
{
    private readonly AiMvp1ProposalFixture fixture;
    public AiMvp1ProposalPersistenceHelperTests(AiMvp1ProposalFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task ProposalPersistenceHelpers_CanReadValidatedProposal()
    {
        var client = fixture.Client(fixture.InstructorA);
        var created = await client.CreateAsync(ScenarioBlueprintFixtures.ValidShortRunMacro());
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var createdBody = await client.ReadJsonAsync(created);
        var proposalId = createdBody.GetProperty("id").GetGuid();

        var validation = await client.ValidateAsync(proposalId);
        Assert.Equal(System.Net.HttpStatusCode.OK, validation.StatusCode);

        var proposal = await fixture.Persistence.GetProposalAsync(proposalId);
        Assert.NotNull(proposal);
        Assert.Equal("Validated", proposal!.Status);
        Assert.Null(proposal.LinkedDraftId);
        Assert.True(await fixture.Persistence.CountProposalValidationsAsync(proposalId) >= 1);
        Assert.Equal(0, await fixture.Persistence.CountDraftsLinkedToProposalAsync(proposalId));
    }
}

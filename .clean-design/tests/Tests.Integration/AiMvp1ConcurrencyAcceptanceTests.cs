using System.Net;

namespace Tests.Integration;

public sealed class AiMvp1ConcurrencyAcceptanceTests : IClassFixture<AiMvp1ProposalFixture>
{
    private readonly AiMvp1ProposalFixture fixture;
    public AiMvp1ConcurrencyAcceptanceTests(AiMvp1ProposalFixture fixture) => this.fixture = fixture;

    private async Task<(Guid Id, long Version)> ProposalAsync()
    {
        var client = fixture.Client(fixture.InstructorA);
        var created = await client.CreateAsync(ScenarioBlueprintFixtures.ValidShortRunMacro());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await client.ReadJsonAsync(created)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.ValidateAsync(id)).StatusCode);
        var row = await fixture.Persistence.GetProposalAsync(id);
        return (id, row!.Version);
    }

    private static async Task<HttpResponseMessage[]> PairAsync(Func<string, Task<HttpResponseMessage>> send, string a, string b)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Run(string key) { await gate.Task; return await send(key); }
        var first = Run(a); var second = Run(b); gate.SetResult();
        return await Task.WhenAll(first, second);
    }

    private static void NoServerErrors(HttpResponseMessage[] responses)
        => Assert.DoesNotContain(responses, x => x.StatusCode == HttpStatusCode.InternalServerError);

    [Fact]
    public async Task Approve_ConcurrentSameKey_ProducesOneConversionAndOneLogicalResponse()
    {
        var (id, version) = await ProposalAsync();
        var responses = await PairAsync(key => fixture.Client(fixture.InstructorA).ApproveAsync(id, version, key), "same-approve", "same-approve");
        NoServerErrors(responses);
        Assert.Contains(responses, x => (int)x.StatusCode is >= 200 and < 300);
        var row = await fixture.Persistence.GetProposalAsync(id);
        Assert.Equal("ConvertedToScenario", row!.Status);
        Assert.Equal(1, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalConversionAuditsAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalConversionOutboxAsync(id));
        Assert.Equal(1, await fixture.Persistence.CountIdempotencyEntriesAsync(fixture.InstructorA, "same-approve"));
        Assert.Equal(1, await fixture.Persistence.CountCompletedIdempotencyEntriesAsync(fixture.InstructorA, "same-approve"));
    }

    [Fact]
    public async Task Reject_ConcurrentSameKey_ProducesOneRejection()
    {
        var (id, version) = await ProposalAsync();
        var responses = await PairAsync(key => fixture.Client(fixture.InstructorA).RejectAsync(id, version, key), "same-reject", "same-reject");
        NoServerErrors(responses);
        Assert.Contains(responses, x => (int)x.StatusCode is >= 200 and < 300);
        Assert.Equal("Rejected", (await fixture.Persistence.GetProposalAsync(id))!.Status);
        Assert.Equal(0, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalRejectionAuditsAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalRejectionOutboxAsync(id));
        Assert.Equal(1, await fixture.Persistence.CountCompletedIdempotencyEntriesAsync(fixture.InstructorA, "same-reject"));
    }

    [Fact]
    public async Task Approve_ConcurrentDifferentKeys_CreatesOnlyOneDraft()
    {
        var (id, version) = await ProposalAsync();
        var responses = await PairAsync(key => fixture.Client(fixture.InstructorA).ApproveAsync(id, version, key), "different-approve-a", "different-approve-b");
        NoServerErrors(responses);
        Assert.Equal("ConvertedToScenario", (await fixture.Persistence.GetProposalAsync(id))!.Status);
        Assert.Equal(1, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalConversionAuditsAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalConversionOutboxAsync(id));
        Assert.Equal(1, await fixture.Persistence.CountSimulationDefinitionsAsync(fixture.InstructorA, "Economics.ShortRunMacro", "1.0.0"));
    }

    [Fact]
    public async Task Reject_ConcurrentDifferentKeys_ProducesOneRejection()
    {
        var (id, version) = await ProposalAsync();
        var responses = await PairAsync(key => fixture.Client(fixture.InstructorA).RejectAsync(id, version, key), "different-reject-a", "different-reject-b");
        NoServerErrors(responses);
        Assert.Equal("Rejected", (await fixture.Persistence.GetProposalAsync(id))!.Status);
        Assert.Equal(0, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalRejectionAuditsAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalRejectionOutboxAsync(id));
    }

    [Fact]
    public async Task ApproveAndReject_Concurrent_ProducesSingleTerminalOutcome()
    {
        var (id, version) = await ProposalAsync();
        var approval = fixture.Client(fixture.InstructorA).ApproveAsync(id, version, "race-approve");
        var rejection = fixture.Client(fixture.InstructorA).RejectAsync(id, version, "race-reject");
        var responses = await Task.WhenAll(approval, rejection);
        NoServerErrors(responses);
        var row = await fixture.Persistence.GetProposalAsync(id);
        Assert.True(row!.Status is "ConvertedToScenario" or "Rejected");
        if (row.Status == "ConvertedToScenario")
        {
            Assert.NotNull(row.LinkedDraftId);
            Assert.Equal(1, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
            Assert.Single(await fixture.Persistence.GetProposalConversionAuditsAsync(id));
            Assert.Single(await fixture.Persistence.GetProposalConversionOutboxAsync(id));
            Assert.Empty(await fixture.Persistence.GetProposalRejectionAuditsAsync(id));
            Assert.Empty(await fixture.Persistence.GetProposalRejectionOutboxAsync(id));
        }
        else
        {
            Assert.Null(row.LinkedDraftId);
            Assert.Equal(0, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
            Assert.Empty(await fixture.Persistence.GetProposalConversionAuditsAsync(id));
            Assert.Empty(await fixture.Persistence.GetProposalConversionOutboxAsync(id));
            Assert.Single(await fixture.Persistence.GetProposalRejectionAuditsAsync(id));
            Assert.Single(await fixture.Persistence.GetProposalRejectionOutboxAsync(id));
        }
    }
}

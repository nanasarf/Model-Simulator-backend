using System.Net;
using System.Text.Json;

namespace Tests.Integration;

public sealed class AiMvp1CommandAcceptanceTests : IClassFixture<AiMvp1ProposalFixture>
{
    private readonly AiMvp1ProposalFixture fixture;
    public AiMvp1CommandAcceptanceTests(AiMvp1ProposalFixture fixture) => this.fixture = fixture;

    private async Task<(Guid Id, long Version)> CreateValidatedAsync(Guid actor)
    {
        var client = fixture.Client(actor);
        var create = await client.CreateAsync(ScenarioBlueprintFixtures.ValidShortRunMacro());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var body = await client.ReadJsonAsync(create);
        var id = body.GetProperty("id").GetGuid();
        var validate = await client.ValidateAsync(id);
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        var proposal = await fixture.Persistence.GetProposalAsync(id);
        Assert.NotNull(proposal);
        return (id, proposal!.Version);
    }

    private static async Task AssertProblem(HttpResponseMessage response, string code)
    {
        Assert.True((int)response.StatusCode >= 400, await response.Content.ReadAsStringAsync());
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(code, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Approve_ExactRetry_ReplaysStoredResponse_WithoutDuplicateSideEffects()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var client = fixture.Client(fixture.InstructorA);
        var first = await client.ApproveAsync(id, version, "approval-replay");
        var firstBody = await first.Content.ReadAsStringAsync();
        var second = await client.ApproveAsync(id, version, "approval-replay");
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(firstBody, await second.Content.ReadAsStringAsync());
        var row = await fixture.Persistence.GetProposalAsync(id);
        Assert.Equal("ConvertedToScenario", row!.Status);
        Assert.NotNull(row.LinkedDraftId);
        Assert.Equal(1, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalConversionAuditsAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalConversionOutboxAsync(id));
        var idem = await fixture.Persistence.GetIdempotencyAsync(fixture.InstructorA, "approval-replay");
        Assert.NotNull(idem?.ResponseBody);
        Assert.NotNull(idem.CompletedAt);
        Assert.Equal(firstBody, idem.ResponseBody);
        Assert.Equal(1, await fixture.Persistence.CountCompletedIdempotencyEntriesAsync(fixture.InstructorA, "approval-replay"));
    }

    [Fact]
    public async Task Reject_ExactRetry_ReplaysStoredResponse_WithoutDuplicateSideEffects()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var client = fixture.Client(fixture.InstructorA);
        var first = await client.RejectAsync(id, version, "rejection-replay");
        var firstBody = await first.Content.ReadAsStringAsync();
        var second = await client.RejectAsync(id, version, "rejection-replay");
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(firstBody, await second.Content.ReadAsStringAsync());
        Assert.Equal("Rejected", (await fixture.Persistence.GetProposalAsync(id))!.Status);
        Assert.Single(await fixture.Persistence.GetProposalRejectionAuditsAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalRejectionOutboxAsync(id));
        var idem = await fixture.Persistence.GetIdempotencyAsync(fixture.InstructorA, "rejection-replay");
        Assert.NotNull(idem?.ResponseBody);
        Assert.NotNull(idem.CompletedAt);
        Assert.Equal(firstBody, idem.ResponseBody);
        Assert.Equal(0, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
    }

    [Fact]
    public async Task Approve_SameKeyDifferentExpectedVersion_ReturnsIdempotencyConflict()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var client = fixture.Client(fixture.InstructorA);
        var first = await client.ApproveAsync(id, version, "approval-conflict");
        var idem = await fixture.Persistence.GetIdempotencyAsync(fixture.InstructorA, "approval-conflict");
        var hash = idem!.RequestHash; var body = idem.ResponseBody; var completed = idem.CompletedAt;
        var conflict = await client.ApproveAsync(id, version + 1, "approval-conflict");
        await AssertProblem(conflict, "idempotency.conflict");
        var after = await fixture.Persistence.GetIdempotencyAsync(fixture.InstructorA, "approval-conflict");
        Assert.Equal(hash, after!.RequestHash); Assert.Equal(body, after.ResponseBody); Assert.Equal(completed, after.CompletedAt);
        Assert.Single(await fixture.Persistence.GetProposalConversionAuditsAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalConversionOutboxAsync(id));
    }

    [Fact]
    public async Task Reject_SameKeyDifferentExpectedVersion_ReturnsIdempotencyConflict()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var client = fixture.Client(fixture.InstructorA);
        await client.RejectAsync(id, version, "rejection-conflict");
        var conflict = await client.RejectAsync(id, version + 1, "rejection-conflict");
        await AssertProblem(conflict, "idempotency.conflict");
        Assert.Single(await fixture.Persistence.GetProposalRejectionAuditsAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalRejectionOutboxAsync(id));
    }

    [Fact]
    public async Task SameIdempotencyKey_CannotRepresentApprovalAndRejection()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var client = fixture.Client(fixture.InstructorA);
        await client.ApproveAsync(id, version, "cross-operation");
        var conflict = await client.RejectAsync(id, version, "cross-operation");
        await AssertProblem(conflict, "idempotency.conflict");
    }

    [Fact]
    public async Task Approve_NewKeyAfterConversion_ReturnsTerminalError_NotReplay()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var client = fixture.Client(fixture.InstructorA);
        await client.ApproveAsync(id, version, "conversion-a");
        var response = await client.ApproveAsync(id, version, "conversion-b");
        await AssertProblem(response, "scenario_proposal.already_converted");
        Assert.Single(await fixture.Persistence.GetProposalConversionAuditsAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalConversionOutboxAsync(id));
    }

    [Fact]
    public async Task Reject_NewKeyAfterRejection_ReturnsTerminalError_NotReplay()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var client = fixture.Client(fixture.InstructorA);
        await client.RejectAsync(id, version, "rejection-a");
        var response = await client.RejectAsync(id, version + 1, "rejection-b");
        await AssertProblem(response, "scenario_proposal.immutable");
        Assert.Single(await fixture.Persistence.GetProposalRejectionAuditsAsync(id));
        Assert.Single(await fixture.Persistence.GetProposalRejectionOutboxAsync(id));
    }

    [Fact]
    public async Task Reject_AfterConversion_WithFreshKey_ReturnsConvertedTerminalError()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var client = fixture.Client(fixture.InstructorA);
        await client.ApproveAsync(id, version, "convert-first");
        var response = await client.RejectAsync(id, version + 1, "reject-after-convert");
        await AssertProblem(response, "scenario_proposal.immutable");
        Assert.Equal("ConvertedToScenario", (await fixture.Persistence.GetProposalAsync(id))!.Status);
        Assert.Empty(await fixture.Persistence.GetProposalRejectionAuditsAsync(id));
        Assert.Empty(await fixture.Persistence.GetProposalRejectionOutboxAsync(id));
    }

    [Fact]
    public async Task Approve_AfterRejection_WithFreshKey_ReturnsRejectedTerminalError()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var client = fixture.Client(fixture.InstructorA);
        await client.RejectAsync(id, version, "reject-first");
        var response = await client.ApproveAsync(id, version + 1, "approve-after-reject");
        await AssertProblem(response, "scenario_proposal.immutable");
        Assert.Equal("Rejected", (await fixture.Persistence.GetProposalAsync(id))!.Status);
        Assert.Equal(0, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
        Assert.Empty(await fixture.Persistence.GetProposalConversionAuditsAsync(id));
    }

    [Fact]
    public async Task InstructorB_CannotApprove_InstructorAProposal()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var response = await fixture.Client(fixture.InstructorB).ApproveAsync(id, version, "other-owner-approve");
        Assert.True((int)response.StatusCode is 403 or 404);
        Assert.Equal("Validated", (await fixture.Persistence.GetProposalAsync(id))!.Status);
        Assert.Equal(0, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
        Assert.Empty(await fixture.Persistence.GetProposalConversionAuditsAsync(id));
        Assert.Equal(0, await fixture.Persistence.CountIdempotencyEntriesAsync(fixture.InstructorB, "other-owner-approve"));
    }

    [Fact]
    public async Task InstructorB_CannotReject_InstructorAProposal()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var response = await fixture.Client(fixture.InstructorB).RejectAsync(id, version, "other-owner-reject");
        Assert.True((int)response.StatusCode is 403 or 404);
        Assert.Equal("Validated", (await fixture.Persistence.GetProposalAsync(id))!.Status);
        Assert.Empty(await fixture.Persistence.GetProposalRejectionAuditsAsync(id));
        Assert.Equal(0, await fixture.Persistence.CountIdempotencyEntriesAsync(fixture.InstructorB, "other-owner-reject"));
    }

    [Fact]
    public async Task Student_CannotApproveProposal()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var response = await fixture.Client(fixture.StudentA, "Student").ApproveAsync(id, version, "student-approve");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Validated", (await fixture.Persistence.GetProposalAsync(id))!.Status);
        Assert.Equal(0, await fixture.Persistence.CountDraftsLinkedToProposalAsync(id));
        Assert.Equal(0, await fixture.Persistence.CountIdempotencyEntriesAsync(fixture.StudentA, "student-approve"));
    }

    [Fact]
    public async Task Student_CannotRejectProposal()
    {
        var (id, version) = await CreateValidatedAsync(fixture.InstructorA);
        var response = await fixture.Client(fixture.StudentA, "Student").RejectAsync(id, version, "student-reject");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Validated", (await fixture.Persistence.GetProposalAsync(id))!.Status);
        Assert.Empty(await fixture.Persistence.GetProposalRejectionAuditsAsync(id));
        Assert.Equal(0, await fixture.Persistence.CountIdempotencyEntriesAsync(fixture.StudentA, "student-reject"));
    }
}

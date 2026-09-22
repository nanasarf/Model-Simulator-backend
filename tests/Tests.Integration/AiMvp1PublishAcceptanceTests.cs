using System.Net;
using SimulationPlatform.Simulations.Core.Authoring;

namespace Tests.Integration;

public sealed class AiMvp1PublishAcceptanceTests : IClassFixture<AiMvp1ProposalFixture>
{
    private readonly AiMvp1ProposalFixture fixture;
    public AiMvp1PublishAcceptanceTests(AiMvp1ProposalFixture fixture) => this.fixture = fixture;

    private async Task<(Guid ProposalId, Guid DraftId)> ApproveAsync(ScenarioProposalTestClient client, ScenarioBlueprint blueprint)
    {
        var created = await client.CreateAsync(blueprint); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await client.ReadJsonAsync(created)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.ValidateAsync(id)).StatusCode);
        var proposal = await fixture.Persistence.GetProposalAsync(id); Assert.NotNull(proposal);
        var approved = await client.ApproveAsync(id, proposal!.Version, "publish-" + id);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var body = await client.ReadJsonAsync(approved);
        return (id, body.GetProperty("scenarioDraftId").GetGuid());
    }

    [Fact]
    public async Task ShortRunMacroProposal_ApproveThenValidateAndPublish_UsesExistingScenarioLifecycle()
    {
        var client = fixture.Client(fixture.InstructorA);
        var (proposalId, draftId) = await ApproveAsync(client, ScenarioBlueprintFixtures.ValidShortRunMacro());
        var draft = await client.GetMacroDraftAsync(draftId); Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        var row = await fixture.Persistence.GetScenarioDraftAsync(draftId); Assert.NotNull(row);
        Assert.Equal(fixture.InstructorA, row!.OwnerUserId);
        Assert.Contains("GOVERNMENT", row.ContentJson, StringComparison.Ordinal);
        var macroValidation = await client.ValidateMacroDraftAsync(draftId);
        Assert.True(macroValidation.StatusCode == HttpStatusCode.OK, await macroValidation.Content.ReadAsStringAsync());
        var preview = await client.PreviewMacroDraftAsync(draftId); Assert.True((int)preview.StatusCode is >= 200 and < 300);
        row = await fixture.Persistence.GetScenarioDraftAsync(draftId);
        var published = await client.PublishMacroDraftAsync(draftId, row!.Version); Assert.True(published.StatusCode == HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        var publishedBody = await client.ReadJsonAsync(published);
        Assert.True(publishedBody.TryGetProperty("scenarioVersionId", out var versionId));
        Assert.NotNull(versionId.GetGuid());
        Assert.Equal("Published", (await fixture.Persistence.GetScenarioDraftAsync(draftId))!.Status);
        var proposal = await fixture.Persistence.GetProposalAsync(proposalId);
        Assert.Equal("ConvertedToScenario", proposal!.Status);
        Assert.Equal(draftId, proposal.LinkedDraftId);
        Assert.Equal(1, await fixture.Persistence.CountDraftsLinkedToProposalAsync(proposalId));
    }

    [Fact]
    public async Task CompetitiveMarketProposal_ApproveThenValidateAndPublish_UsesExistingScenarioLifecycle()
    {
        var client = fixture.Client(fixture.InstructorA);
        var (proposalId, draftId) = await ApproveAsync(client, ScenarioBlueprintFixtures.ValidCompetitiveMarket());
        var draft = await client.GetMarketDraftAsync(draftId); Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        var draftRow = await fixture.Persistence.GetScenarioDraftAsync(draftId); Assert.NotNull(draftRow);
        Assert.Contains("BUYER", draftRow!.ContentJson, StringComparison.Ordinal);
        var marketValidation = await client.ValidateMarketDraftAsync(draftId);
        Assert.True(marketValidation.StatusCode == HttpStatusCode.OK, await marketValidation.Content.ReadAsStringAsync());
        var row = await fixture.Persistence.GetScenarioDraftAsync(draftId);
        var preview = await client.PreviewMarketDraftAsync(draftId); Assert.True((int)preview.StatusCode is >= 200 and < 300);
        row = await fixture.Persistence.GetScenarioDraftAsync(draftId);
        var published = await client.PublishMarketDraftAsync(draftId, row!.Version); Assert.True(published.StatusCode == HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        Assert.True((await client.ReadJsonAsync(published)).TryGetProperty("scenarioVersionId", out _));
        var proposal = await fixture.Persistence.GetProposalAsync(proposalId);
        Assert.Equal("ConvertedToScenario", proposal!.Status);
        Assert.Equal(draftId, proposal.LinkedDraftId);
    }
}

using System.Text.Json;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Domain.Common;

namespace SimulationPlatform.Simulations.Economics.CompetitiveMarket;

public sealed record CompetitiveMarketRole(string Code, bool Enabled, bool SeePrivateInformation, List<string> Objectives);
public sealed record CompetitiveMarketScenarioContent(string Briefing, List<string> LearningObjectives, List<string> DiscussionPrompts, List<string> DebriefPrompts, MarketConfiguration Configuration, int MaximumRounds, List<CompetitiveMarketRole> Roles, HashSet<string>? EnabledActions = null, HashSet<MarketAssessmentDimension>? AssessmentDimensions = null);
public sealed record MarketAuthoringIssue(string Code, string Message);
public sealed record MarketValidationReport(bool CanPublish, IReadOnlyList<MarketAuthoringIssue> Blockers, IReadOnlyList<MarketAuthoringIssue> Warnings);
public sealed record MarketPreviewResult(int Seed, IReadOnlyList<MarketRoundResult> Rounds, IReadOnlyList<MarketAuthoringIssue> Diagnostics);
public sealed record CompetitiveMarketTemplate(string Code, string Name, CompetitiveMarketScenarioContent Content);
public sealed record CompetitiveMarketDraft(ScenarioDraftDocument Document, CompetitiveMarketScenarioContent Content);
public interface ICompetitiveMarketAuthoring
{
    IReadOnlyList<CompetitiveMarketTemplate> Templates();
    ValueTask<CompetitiveMarketDraft> CreateAsync(Guid owner, Guid definitionId, string name, CompetitiveMarketScenarioContent content, string key, CancellationToken ct);
    ValueTask<CompetitiveMarketDraft> GetAsync(Guid owner, Guid id, CancellationToken ct);
    ValueTask<CompetitiveMarketDraft> UpdateAsync(Guid owner, Guid id, string name, CompetitiveMarketScenarioContent content, long version, CancellationToken ct);
    ValueTask<CompetitiveMarketDraft> CloneAsync(Guid owner, Guid id, string name, string key, CancellationToken ct);
    ValueTask ArchiveAsync(Guid owner, Guid id, long version, CancellationToken ct);
    MarketValidationReport Validate(CompetitiveMarketScenarioContent content);
    ValueTask<MarketPreviewResult> PreviewAsync(Guid owner, Guid id, int seed, CancellationToken ct);
    ValueTask<Guid> PublishAsync(Guid owner, Guid id, long version, CancellationToken ct);
}
public sealed class CompetitiveMarketAuthoring(IScenarioDraftStore drafts, IClassroomWorkflow workflow, CompetitiveMarketModel model) : ICompetitiveMarketAuthoring
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, (string Action, string Capability, string View)> Roles = new() { ["BUYER"] = (MarketActions.SubmitBid, MarketCapabilities.Bid, MarketCapabilities.ViewBuyerInfo), ["SELLER"] = (MarketActions.SubmitAsk, MarketCapabilities.Ask, MarketCapabilities.ViewSellerInfo), ["GOVERNMENT"] = (MarketActions.Predict, MarketCapabilities.Predict, MarketCapabilities.ViewGovernment) };
    public IReadOnlyList<CompetitiveMarketTemplate> Templates() => CompetitiveMarketTemplates.All;
    public async ValueTask<CompetitiveMarketDraft> CreateAsync(Guid o, Guid d, string n, CompetitiveMarketScenarioContent c, string k, CancellationToken ct) => Wrap(await drafts.CreateAsync(o, d, n, JsonSerializer.SerializeToElement(c, JsonOptions), k, ct));
    public async ValueTask<CompetitiveMarketDraft> GetAsync(Guid o, Guid id, CancellationToken ct) => Wrap(await drafts.GetAsync(o, id, ct));
    public async ValueTask<CompetitiveMarketDraft> UpdateAsync(Guid o, Guid id, string n, CompetitiveMarketScenarioContent c, long v, CancellationToken ct) => Wrap(await drafts.UpdateAsync(o, id, n, JsonSerializer.SerializeToElement(c, JsonOptions), v, ct));
    public async ValueTask<CompetitiveMarketDraft> CloneAsync(Guid o, Guid id, string n, string k, CancellationToken ct) => Wrap(await drafts.CloneAsync(o, id, n, k, ct));
    public ValueTask ArchiveAsync(Guid o, Guid id, long v, CancellationToken ct) => drafts.ArchiveAsync(o, id, v, ct);
    public MarketValidationReport Validate(CompetitiveMarketScenarioContent c)
    {
        var b = new List<MarketAuthoringIssue>(); var w = new List<MarketAuthoringIssue>();
        if (string.IsNullOrWhiteSpace(c.Briefing)) b.Add(new("BRIEFING_REQUIRED", "A briefing is required.")); if (c.LearningObjectives.Count == 0) b.Add(new("OBJECTIVES_REQUIRED", "At least one learning objective is required.")); if (c.MaximumRounds is < 1 or > 20) b.Add(new("ROUND_RANGE", "Maximum rounds must be between 1 and 20."));
        foreach (var role in new[] { "BUYER", "SELLER" }) { var r = c.Roles.SingleOrDefault(x => x.Code == role && x.Enabled); if (r is null) b.Add(new("ROLE_REQUIRED", $"{role} role is required.")); else if (r.Objectives.Count == 0) w.Add(new("ROLE_OBJECTIVE_MISSING", $"{role} has no objective.")); if (c.EnabledActions is null || !c.EnabledActions.Contains(Roles[role].Action)) b.Add(new("ACTION_REQUIRED", $"{role} action is required.")); }
        var m = c.Configuration; if (m.DemandSlope <= 0 || m.SupplySlope <= 0 || m.BuyerCount < 1 || m.SellerCount < 1 || m.UnitsPerBuyer < 1 || m.UnitsPerSeller < 1) b.Add(new("MARKET_CONFIGURATION_INVALID", "Demand/supply slopes and participant quantities must be positive."));
        foreach (var s in m.ScheduledShocks ?? []) if (s.Round < 1 || s.Round > c.MaximumRounds || s.Type is not ("demand_increase" or "demand_decrease" or "supply_increase" or "supply_decrease")) b.Add(new("SHOCK_INVALID", $"Shock {s.Type} has invalid timing or type."));
        if (c.DiscussionPrompts.Count == 0) w.Add(new("DISCUSSION_MISSING", "Add discussion prompts.")); if (c.DebriefPrompts.Count == 0) w.Add(new("DEBRIEF_MISSING", "Add debrief prompts.")); return new(b.Count == 0, b, w);
    }
    public async ValueTask<MarketPreviewResult> PreviewAsync(Guid o, Guid id, int seed, CancellationToken ct) { var d = await GetAsync(o, id, ct); var v = Validate(d.Content); if (!v.CanPublish) return new(seed, [], v.Blockers.Concat(v.Warnings).ToArray()); var state = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(d.Content.Configuration), seed), ct); var rounds = new List<MarketRoundResult>(); for (var r = 1; r <= d.Content.MaximumRounds; r++) { var x = await model.ExecuteRoundAsync(new(state, [], seed, r), ct); state = x.State; rounds.Add(state.Deserialize<MarketState>(JsonOptions)!.LastResult!); } return new(seed, rounds, v.Warnings); }
    public async ValueTask<Guid> PublishAsync(Guid o, Guid id, long v, CancellationToken ct) { var d = await GetAsync(o, id, ct); if (d.Document.Version != v) throw new DomainException("concurrency.conflict", "Draft changed before publication."); var report = Validate(d.Content); if (!report.CanPublish) throw new DomainException("scenario.validation_failed", string.Join(" ", report.Blockers.Select(x => x.Message))); return await workflow.PublishScenarioDraftAsync(o, id, d.Document.Name, Manifest(d.Content), v, ct); }
    private static CompetitiveMarketDraft Wrap(ScenarioDraftDocument d) => new(d, d.Content.Deserialize<CompetitiveMarketScenarioContent>(JsonOptions) ?? throw new DomainException("scenario.invalid", "Invalid market scenario."));
    private static ScenarioManifest Manifest(CompetitiveMarketScenarioContent c) { var roles = c.Roles.Where(x => x.Enabled && Roles.ContainsKey(x.Code)).Select(x => new RoleManifest(x.Code, x.Code, 1, 1, new HashSet<string> { Roles[x.Code].Capability, Roles[x.Code].View })).ToList(); var actions = (c.EnabledActions ?? []).Where(MarketActions.All.Contains).Select(a => new ActionManifest(a, Roles.Values.First(x => x.Action == a).Capability, new HashSet<string> { "Decision" })).ToList(); var phases = new List<string> { "Briefing", "Decision", "Locked", "Simulation", "Results", "Reflection", "Completed" }; var transitions = new Dictionary<string, HashSet<string>> { ["Briefing"] = ["Decision"], ["Decision"] = ["Locked"], ["Locked"] = ["Simulation"], ["Simulation"] = ["Results"], ["Results"] = ["Reflection"], ["Reflection"] = ["Briefing", "Completed"] }; var p = JsonSerializer.SerializeToElement(new { c.Briefing, c.LearningObjectives, c.DiscussionPrompts, c.DebriefPrompts, c.AssessmentDimensions }); return new("Economics.CompetitiveMarket", "1.0.0", 1, JsonSerializer.SerializeToElement(c.Configuration), phases, transitions, roles, actions, [], ["Decision"], c.MaximumRounds, p); }
}
public static class CompetitiveMarketTemplates
{
    public static readonly IReadOnlyList<CompetitiveMarketTemplate> All = [new("BASIC_MARKET", "Basic competitive market", new("Clear a market and compare welfare outcomes.", ["Explain equilibrium and gains from trade."], ["Who traded and who remained unmatched?"], ["How did the clearing rule affect surplus?"], new(), 4, [new("BUYER", true, true, ["Acquire high-value units."]), new("SELLER", true, true, ["Sell above cost."]), new("GOVERNMENT", true, false, ["Monitor market welfare."])], [MarketActions.SubmitBid, MarketActions.SubmitAsk, MarketActions.Predict]))];
}

using System.Text.Json;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Runtime;
using SimulationPlatform.Simulations.Core.Contracts;

namespace SimulationPlatform.Simulations.Economics.Macroeconomics;

public sealed record MacroStartingConditions(decimal OutputIndex = 100, decimal PotentialOutputIndex = 100,
    decimal Inflation = 2, decimal Unemployment = 5, decimal PolicyRate = 3, decimal DebtToOutput = 55);
public sealed record MacroRoleAuthoring(string Code, bool Enabled, bool CanSeeInstitutionalIndicators,
    List<string> Objectives);
public sealed record MacroScenarioContent(string Briefing, List<string> LearningObjectives,
    List<string> DiscussionPrompts, List<string> DebriefPrompts, MacroStartingConditions StartingConditions,
    int MaximumQuarters, List<MacroRoleAuthoring> Roles, HashSet<string> EnabledActions,
    HashSet<PolicyIntensity> AllowedIntensities, List<ScheduledMacroShock> ScheduledShocks,
    MacroObjectiveConfiguration TeamObjectives, HashSet<MacroAssessmentDimension>? AssessmentDimensions = null);
public sealed record AuthoringIssue(string Code, string Message);
public sealed record MacroValidationReport(bool CanPublish, IReadOnlyList<AuthoringIssue> Blockers, IReadOnlyList<AuthoringIssue> Warnings);
public sealed record MacroPreviewQuarter(int Quarter, MacroState State);
public sealed record MacroPreviewResult(int Seed, IReadOnlyList<MacroPreviewQuarter> Quarters,
    IReadOnlyList<AuthoringIssue> Diagnostics);
public sealed record MacroTemplate(string Code, string Name, string Description, MacroScenarioContent Content);
public sealed record MacroDraft(ScenarioDraftDocument Document, MacroScenarioContent Content);

public interface IMacroScenarioAuthoring
{
    IReadOnlyList<MacroTemplate> Templates();
    ValueTask<MacroDraft> CreateAsync(Guid instructorId, Guid definitionId, string name, MacroScenarioContent content, string key, CancellationToken ct);
    ValueTask<MacroDraft> GetAsync(Guid instructorId, Guid draftId, CancellationToken ct);
    ValueTask<MacroDraft> UpdateAsync(Guid instructorId, Guid draftId, string name, MacroScenarioContent content, long version, CancellationToken ct);
    ValueTask<MacroDraft> CloneAsync(Guid instructorId, Guid draftId, string name, string key, CancellationToken ct);
    ValueTask ArchiveAsync(Guid instructorId, Guid draftId, long version, CancellationToken ct);
    MacroValidationReport Validate(MacroScenarioContent content);
    ValueTask<MacroPreviewResult> PreviewAsync(Guid instructorId, Guid draftId, int seed, CancellationToken ct);
    ValueTask<Guid> PublishAsync(Guid instructorId, Guid draftId, long version, CancellationToken ct);
}

public sealed class MacroScenarioAuthoring(IScenarioDraftStore drafts, IClassroomWorkflow workflow,
    ShortRunMacroModel model) : IMacroScenarioAuthoring
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private static readonly string[] RequiredRoles = ["GOVERNMENT", "CENTRAL_BANK", "BUSINESS", "HOUSEHOLD_LABOR"];
    private static readonly HashSet<string> SupportedActions =
        [MacroActions.FiscalPolicy, MacroActions.MonetaryPolicy, MacroActions.BusinessStrategy, MacroActions.HouseholdLaborStance];
    private static readonly HashSet<string> SupportedShocks =
        ["demand_slump", "demand_boom", "supply_disruption", "productivity_boost", "confidence_crisis"];
    private static readonly Dictionary<string, (string Action, string Set, string View)> RoleMap = new()
    {
        ["GOVERNMENT"] = (MacroActions.FiscalPolicy, MacroCapabilities.SetFiscalPolicy, MacroCapabilities.ViewFiscal),
        ["CENTRAL_BANK"] = (MacroActions.MonetaryPolicy, MacroCapabilities.SetMonetaryPolicy, MacroCapabilities.ViewMonetary),
        ["BUSINESS"] = (MacroActions.BusinessStrategy, MacroCapabilities.SetBusinessStrategy, MacroCapabilities.ViewBusiness),
        ["HOUSEHOLD_LABOR"] = (MacroActions.HouseholdLaborStance, MacroCapabilities.SetHouseholdLaborStance, MacroCapabilities.ViewHousehold)
    };

    public IReadOnlyList<MacroTemplate> Templates() => MacroTemplates.All;
    public async ValueTask<MacroDraft> CreateAsync(Guid owner, Guid definition, string name, MacroScenarioContent content, string key, CancellationToken ct) =>
        Wrap(await drafts.CreateAsync(owner, definition, name, JsonSerializer.SerializeToElement(content, JsonOptions), key, ct));
    public async ValueTask<MacroDraft> GetAsync(Guid owner, Guid id, CancellationToken ct) => Wrap(await drafts.GetAsync(owner, id, ct));
    public async ValueTask<MacroDraft> UpdateAsync(Guid owner, Guid id, string name, MacroScenarioContent content, long version, CancellationToken ct) =>
        Wrap(await drafts.UpdateAsync(owner, id, name, JsonSerializer.SerializeToElement(content, JsonOptions), version, ct));
    public async ValueTask<MacroDraft> CloneAsync(Guid owner, Guid id, string name, string key, CancellationToken ct) => Wrap(await drafts.CloneAsync(owner, id, name, key, ct));
    public ValueTask ArchiveAsync(Guid owner, Guid id, long version, CancellationToken ct) => drafts.ArchiveAsync(owner, id, version, ct);

    public MacroValidationReport Validate(MacroScenarioContent c)
    {
        var blockers = new List<AuthoringIssue>(); var warnings = new List<AuthoringIssue>();
        if (string.IsNullOrWhiteSpace(c.Briefing)) blockers.Add(new("BRIEFING_REQUIRED", "A student briefing is required."));
        if (c.LearningObjectives.Count == 0) blockers.Add(new("LEARNING_OBJECTIVE_REQUIRED", "At least one learning objective is required."));
        if (c.MaximumQuarters is < 1 or > 20) blockers.Add(new("QUARTER_RANGE", "Maximum quarters must be between 1 and 20."));
        if (c.Roles.GroupBy(x => x.Code, StringComparer.Ordinal).Any(x => x.Count() > 1))
            blockers.Add(new("DUPLICATE_ROLE", "A role code may only be configured once."));
        foreach (var unknownRole in c.Roles.Where(x => !RoleMap.ContainsKey(x.Code)))
            blockers.Add(new("UNSUPPORTED_ROLE", $"Role {unknownRole.Code} is not supported by this model version."));
        foreach (var unknownAction in c.EnabledActions.Where(x => !SupportedActions.Contains(x)))
            blockers.Add(new("UNSUPPORTED_ACTION", $"Action {unknownAction} is not supported by this model version."));
        foreach (var role in RequiredRoles)
        {
            var configured = c.Roles.SingleOrDefault(x => x.Code == role && x.Enabled);
            if (configured is null) blockers.Add(new("ROLE_REQUIRED", $"Required role {role} is not enabled."));
            else if (configured.Objectives.Count == 0) warnings.Add(new("ROLE_OBJECTIVE_MISSING", $"Role {role} has no stated objective."));
            if (!c.EnabledActions.Contains(RoleMap[role].Action)) blockers.Add(new("ROLE_ACTION_REQUIRED", $"Role {role} has no enabled decision action."));
        }
        if (c.AllowedIntensities.Count == 0) blockers.Add(new("INTENSITY_REQUIRED", "At least one qualitative intensity is required."));
        foreach (var intensity in c.AllowedIntensities.Where(x => !Enum.IsDefined(x)))
            blockers.Add(new("UNSUPPORTED_INTENSITY", $"Policy intensity {(int)intensity} is not supported by this model version."));
        foreach (var shock in c.ScheduledShocks)
        {
            if (shock.Round < 1 || shock.Round > c.MaximumQuarters) blockers.Add(new("SHOCK_TIMING", $"Shock {shock.Type} is outside the scenario quarter range."));
            if (!SupportedShocks.Contains(shock.Type)) blockers.Add(new("UNSUPPORTED_SHOCK", $"Shock {shock.Type} is not supported by this model version."));
            if (!Enum.IsDefined(shock.Intensity)) blockers.Add(new("UNSUPPORTED_SHOCK_INTENSITY", $"Shock {shock.Type} has an unsupported intensity."));
        }
        if (c.ScheduledShocks.GroupBy(x => (x.Round, x.Type)).Any(x => x.Count() > 1))
            warnings.Add(new("DUPLICATE_SHOCK", "The same shock is scheduled more than once in a quarter; effects will accumulate."));
        var s = c.StartingConditions;
        if (s.OutputIndex is < 50 or > 200 || s.PotentialOutputIndex is < 50 or > 200 || s.Inflation is < -3 or > 20 ||
            s.Unemployment is < 1.5m or > 25 || s.PolicyRate is < 0 or > 20 || s.DebtToOutput is < 0 or > 200)
            blockers.Add(new("STARTING_STATE_BOUNDS", "Starting conditions must fall within the model's documented state bounds."));
        if (c.TeamObjectives.InflationMinimum > c.TeamObjectives.InflationMaximum || c.TeamObjectives.InflationMinimum < -3 ||
            c.TeamObjectives.InflationMaximum > 20 || c.TeamObjectives.UnemploymentMaximum is < 1.5m or > 25 ||
            c.TeamObjectives.OutputGapAbsoluteMaximum is < 0 or > 12 || c.TeamObjectives.DebtToOutputMaximum is < 0 or > 200)
            blockers.Add(new("OBJECTIVE_IMPOSSIBLE", "One or more team objective ranges are impossible within model bounds."));
        if (c.ScheduledShocks.Count == 0) warnings.Add(new("NO_SHOCKS", "The scenario contains no scheduled external shocks."));
        if (c.DiscussionPrompts.Count == 0) warnings.Add(new("DISCUSSION_PROMPTS_MISSING", "Add prompts for facilitated discussion."));
        if (c.DebriefPrompts.Count == 0) warnings.Add(new("DEBRIEF_PROMPTS_MISSING", "Add prompts for the final debrief."));
        return new(blockers.Count == 0, blockers, warnings);
    }

    public async ValueTask<MacroPreviewResult> PreviewAsync(Guid owner, Guid id, int seed, CancellationToken ct)
    {
        var draft = await GetAsync(owner, id, ct); var validation = Validate(draft.Content);
        if (!validation.CanPublish) return new(seed, [], validation.Blockers.Concat(validation.Warnings).ToArray());
        var config = Configuration(draft.Content);
        var state = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(config, JsonOptions), seed), ct);
        var quarters = new List<MacroPreviewQuarter>(); var diagnostics = validation.Warnings.ToList(); var saturation = 0;
        for (var round = 1; round <= draft.Content.MaximumQuarters; round++)
        {
            var actions = PreviewActions(draft.Content, round);
            var result = await model.ExecuteRoundAsync(new(state, actions, seed, round), ct);
            var parsed = result.State.Deserialize<MacroState>(JsonOptions)!; quarters.Add(new(round, parsed)); state = result.State;
            if (Math.Abs(parsed.OutputGap) >= 11.9m || parsed.Inflation is >= 19.9m or <= -2.9m || parsed.Unemployment is >= 24.9m or <= 1.6m) saturation++;
            if (round > 1 && (Math.Abs(parsed.Inflation - quarters[^2].State.Inflation) > 5 || Math.Abs(parsed.OutputGap - quarters[^2].State.OutputGap) > 7))
                diagnostics.Add(new("UNSTABLE_TRAJECTORY", $"Quarter {round} changes abruptly; review shock intensity and duration."));
        }
        if (saturation >= 2) diagnostics.Add(new("REPEATED_SATURATION", "State bounds are reached repeatedly, reducing interpretability."));
        var initial = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(config, JsonOptions), seed), ct);
        var neutral = await model.ExecuteRoundAsync(new(initial, [], seed, 1), ct);
        if (quarters.Count > 0 && Math.Abs(quarters[0].State.OutputIndex - neutral.State.Deserialize<MacroState>(JsonOptions)!.OutputIndex) < 0.05m)
            diagnostics.Add(new("INEFFECTIVE_PLAYER_AGENCY", "Enabled role decisions have negligible first-quarter effect."));
        return new(seed, quarters, diagnostics.DistinctBy(x => x.Code).ToArray());
    }

    public async ValueTask<Guid> PublishAsync(Guid owner, Guid id, long version, CancellationToken ct)
    {
        var draft = await GetAsync(owner, id, ct); if (draft.Document.Version != version) throw new DomainException("concurrency.conflict", "The draft changed before publication.");
        var report = Validate(draft.Content); if (!report.CanPublish) throw new DomainException("scenario.validation_failed", string.Join(" ", report.Blockers.Select(x => x.Message)));
        return await workflow.PublishScenarioDraftAsync(owner, id, draft.Document.Name, Manifest(draft.Content), version, ct);
    }

    private static MacroDraft Wrap(ScenarioDraftDocument doc) => new(doc, doc.Content.Deserialize<MacroScenarioContent>(JsonOptions)
        ?? throw new DomainException("scenario_draft.invalid", "Macro scenario draft content is invalid."));
    private static MacroConfiguration Configuration(MacroScenarioContent c) => new(c.StartingConditions.OutputIndex,
        c.StartingConditions.PotentialOutputIndex, c.StartingConditions.Inflation, c.StartingConditions.Unemployment,
        c.StartingConditions.PolicyRate, c.StartingConditions.DebtToOutput, Objectives: c.TeamObjectives,
        ScheduledShocks: c.ScheduledShocks, EnabledActions: c.EnabledActions.Append(MacroActions.DirectionalPrediction).ToHashSet(),
        AllowedIntensities: c.AllowedIntensities);

    private static ScenarioManifest Manifest(MacroScenarioContent c)
    {
        var phases = new List<string> { SessionPhases.Briefing, "Prediction", SessionPhases.Decision, SessionPhases.Locked,
            SessionPhases.Simulation, SessionPhases.Results, "Discussion", SessionPhases.Completed };
        var transitions = new Dictionary<string, HashSet<string>> { [SessionPhases.Briefing] = ["Prediction"], ["Prediction"] = [SessionPhases.Decision],
            [SessionPhases.Decision] = [SessionPhases.Locked], [SessionPhases.Locked] = [SessionPhases.Simulation], [SessionPhases.Simulation] = [SessionPhases.Results],
            [SessionPhases.Results] = ["Discussion"], ["Discussion"] = [SessionPhases.Briefing, SessionPhases.Completed] };
        var roles = c.Roles.Where(x => x.Enabled).Select(x => { var map = RoleMap[x.Code]; var caps = new HashSet<string> { MacroCapabilities.SubmitPrediction, map.Set }; if (x.CanSeeInstitutionalIndicators) caps.Add(map.View); return new RoleManifest(x.Code, x.Code.Replace('_', ' '), 1, 1, caps); }).ToList();
        var actions = new List<ActionManifest> { new(MacroActions.DirectionalPrediction, MacroCapabilities.SubmitPrediction, ["Prediction"]) };
        actions.AddRange(roles.Select(x => RoleMap[x.Code]).Where(x => c.EnabledActions.Contains(x.Action)).Select(x => new ActionManifest(x.Action, x.Set, [SessionPhases.Decision])));
        var rules = new List<RuleManifest> { new(Guid.NewGuid(), 100, "Deny", JsonSerializer.SerializeToElement(new { kind = "comparison", fact = "submission.count", @operator = "gte", value = 1 })), new(Guid.NewGuid(), 0, "Allow", JsonSerializer.SerializeToElement(new { kind = "exists", fact = "action.code" })) };
        var presentation = JsonSerializer.SerializeToElement(new { c.Briefing, c.LearningObjectives, c.DiscussionPrompts, c.DebriefPrompts,
            roleObjectives = c.Roles.ToDictionary(x => x.Code, x => x.Objectives), c.TeamObjectives,
            assessmentDimensions = c.AssessmentDimensions?.AsEnumerable() ?? Enum.GetValues<MacroAssessmentDimension>().AsEnumerable() }, JsonOptions);
        return new("Economics.ShortRunMacro", "1.0.0", 1, JsonSerializer.SerializeToElement(Configuration(c), JsonOptions), phases,
            transitions, roles, actions, rules, ["Prediction", SessionPhases.Decision], c.MaximumQuarters, presentation);
    }

    private static IReadOnlyList<RoundAction> PreviewActions(MacroScenarioContent c, int round)
    {
        var intensity = c.AllowedIntensities.OrderBy(x => x).ElementAt(c.AllowedIntensities.Count / 2).ToString();
        var directions = new Dictionary<string, string> { [MacroActions.FiscalPolicy] = round % 2 == 0 ? "contract" : "expand",
            [MacroActions.MonetaryPolicy] = round % 2 == 0 ? "ease" : "tighten", [MacroActions.BusinessStrategy] = "expand", [MacroActions.HouseholdLaborStance] = "support" };
        return c.EnabledActions.Where(directions.ContainsKey).OrderBy(x => x).Select(x => new RoundAction(x,
            JsonSerializer.SerializeToElement(new { direction = directions[x], intensity }))).ToArray();
    }
}

public static class MacroTemplates
{
    private static MacroScenarioContent Base(string briefing, List<ScheduledMacroShock> shocks) => new(briefing,
        ["Explain short-run aggregate demand and supply transmission.", "Distinguish economic performance from prediction quality."],
        ["Which institutions worked at cross-purposes?"], ["Which causal prediction changed after observing results?"], new(), 4,
        [new("GOVERNMENT", true, true, ["Support output while monitoring debt."]), new("CENTRAL_BANK", true, true, ["Maintain price stability."]),
         new("BUSINESS", true, true, ["Maintain confidence and productive capacity."]), new("HOUSEHOLD_LABOR", true, true, ["Support employment and real income."])],
        [MacroActions.FiscalPolicy, MacroActions.MonetaryPolicy, MacroActions.BusinessStrategy, MacroActions.HouseholdLaborStance],
        [PolicyIntensity.Mild, PolicyIntensity.Moderate, PolicyIntensity.Strong], shocks, new());
    public static readonly IReadOnlyList<MacroTemplate> All =
    [
        new("BASELINE_STABILIZATION", "Baseline stabilization", "Balanced starting conditions with no forced shock.", Base("Manage a stable economy through four quarters.", [])),
        new("SUPPLY_DISRUPTION", "Supply disruption", "A second-quarter adverse supply shock creates a stabilization trade-off.", Base("Prepare the country for an emerging production-cost disruption.", [new(2, "supply_disruption", PolicyIntensity.Moderate)]))
    ];
}

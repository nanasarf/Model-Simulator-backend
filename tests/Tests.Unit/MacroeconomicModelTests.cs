using System.Text.Json;
using SimulationPlatform.Simulations.Core.Contracts;
using SimulationPlatform.Simulations.Economics.Macroeconomics;

namespace Tests.Unit;

public sealed class MacroeconomicModelTests
{
    private readonly ShortRunMacroModel model = new();

    [Fact]
    public void Fiscal_mechanism_is_independent_and_respects_multiplier()
    {
        var state = State(new MacroConfiguration(FiscalMultiplier: 1.2m));
        var decision = new MacroDecision(MacroActions.FiscalPolicy, "expand", PolicyIntensity.Moderate, null);
        var result = new FiscalTransmissionMechanism().Evaluate(new(state, [decision], [], 1));
        Assert.Equal(2.4m, result.DemandPressure);
        Assert.True(result.FiscalBalanceChange < 0);
        Assert.Equal(0, result.PolicyRateChange);
    }

    [Fact]
    public void Supply_shock_mechanism_raises_cost_pressure_and_reduces_capacity()
    {
        var result = new ExternalShockMechanism().Evaluate(new(State(), [],
            [new ScheduledMacroShock(1, "supply_disruption", PolicyIntensity.Strong)], 1));
        Assert.True(result.SupplyPressure > 0);
        Assert.True(result.PotentialOutputChange < 0);
        Assert.Equal(0, result.DemandPressure);
    }

    [Theory]
    [InlineData("Strong", true)]
    [InlineData("7", false)]
    [InlineData("Extreme", false)]
    public async Task Actions_accept_only_qualitative_bounded_intensities(string intensity, bool valid)
    {
        var state = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(new { }), 1), default);
        var result = await model.ValidateActionAsync(new(state, MacroActions.FiscalPolicy,
            JsonSerializer.SerializeToElement(new { direction = "expand", intensity })), default);
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public async Task Same_inputs_are_deterministic_and_policy_interactions_are_reported()
    {
        var initial = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(new { }), 77), default);
        RoundAction[] actions =
        [
            Action(MacroActions.FiscalPolicy, "expand", "Strong"),
            Action(MacroActions.MonetaryPolicy, "tighten", "Moderate"),
            Action(MacroActions.BusinessStrategy, "expand", "Mild"),
            Action(MacroActions.TriggerShock, null, "Moderate", "supply_disruption")
        ];
        var first = await model.ExecuteRoundAsync(new(initial, actions, 77, 1), default);
        var second = await model.ExecuteRoundAsync(new(initial, actions, 77, 1), default);
        Assert.True(JsonElement.DeepEquals(first.State, second.State));
        Assert.Equal(first.Metrics, second.Metrics);
        var state = first.State.Deserialize<MacroState>(Options)!;
        Assert.Contains(state.LastReport!.Conflicts, x => x.Code == "FISCAL_MONETARY_OPPOSITION");
        Assert.Contains(state.LastReport.Conflicts, x => x.Code == "STAGFLATION_TRADEOFF");
        Assert.Contains(state.LastReport.CausalExplanation, x => x.Contains("supply disruption", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Adverse_supply_shock_creates_stagflation_relative_to_baseline()
    {
        var initial = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(new { }), 1), default);
        var baseline = await model.ExecuteRoundAsync(new(initial, [], 1, 1), default);
        var shocked = await model.ExecuteRoundAsync(new(initial,
            [Action(MacroActions.TriggerShock, null, "Strong", "supply_disruption")], 1, 1), default);
        var b = baseline.State.Deserialize<MacroState>(Options)!;
        var s = shocked.State.Deserialize<MacroState>(Options)!;
        Assert.True(s.Inflation > b.Inflation);
        Assert.True(s.OutputIndex < b.OutputIndex);
        Assert.True(s.Unemployment > b.Unemployment);
    }

    [Fact]
    public async Task Conceptual_assessment_is_separate_from_objective_outcomes()
    {
        var initial = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(new { }), 1), default);
        var prediction = new { output = "increase", inflation = "increase", unemployment = "decrease",
            explanation = "Government spending raises aggregate demand through the multiplier." };
        var payload = JsonSerializer.SerializeToElement(new { direction = "expand", intensity = "Moderate", prediction });
        var result = await model.ExecuteRoundAsync(new(initial, [new(MacroActions.FiscalPolicy, payload)], 1, 1), default);
        var state = result.State.Deserialize<MacroState>(Options)!;
        var assessment = Assert.Single(state.LastReport!.Assessments);
        Assert.Equal(100m, assessment.ConceptualScore);
        Assert.Equal(4, state.LastReport.Objectives.Count);
        Assert.Equal(100m, result.Metrics["conceptualScore"]);
    }

    [Fact]
    public async Task Projections_remove_indicators_not_granted_to_the_role()
    {
        var initial = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(new { }), 1), default);
        var household = await model.GenerateVisibleStateAsync(new(initial,
            new HashSet<string> { MacroCapabilities.ViewHousehold }), default);
        Assert.True(household.TryGetProperty("consumerConfidence", out _));
        Assert.True(household.TryGetProperty("realIncomePressure", out _));
        Assert.False(household.TryGetProperty("policyRate", out _));
        Assert.False(household.TryGetProperty("debtToOutput", out _));

        var full = await model.GenerateVisibleStateAsync(new(initial,
            new HashSet<string> { MacroCapabilities.ViewAll }), default);
        Assert.True(full.TryGetProperty("policyRate", out _));
        Assert.True(full.TryGetProperty("configuration", out _));
    }

    [Fact]
    public async Task Scheduled_shocks_execute_only_in_the_configured_quarter()
    {
        var config = new MacroConfiguration(ScheduledShocks:
            [new ScheduledMacroShock(2, "demand_slump", PolicyIntensity.Strong)]);
        var initial = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(config, Options), 42), default);
        var q1 = await model.ExecuteRoundAsync(new(initial, [], 42, 1), default);
        var q2 = await model.ExecuteRoundAsync(new(q1.State, [], 42, 2), default);
        Assert.True(q2.Metrics["outputGap"] < q1.Metrics["outputGap"]);
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private static RoundAction Action(string code, string? direction, string intensity, string? type = null) =>
        new(code, type is null
            ? JsonSerializer.SerializeToElement(new { direction, intensity })
            : JsonSerializer.SerializeToElement(new { type, intensity }));
    private static MacroState State(MacroConfiguration? configuration = null) => new(0, 100, 100, 0, 2, 2,
        5, 3, 0, 55, 50, 50, 0, 100, 0, configuration ?? new(), null);
}

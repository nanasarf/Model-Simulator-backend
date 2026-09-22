using System.Text.Json;
using SimulationPlatform.Simulations.Core.Contracts;

namespace SimulationPlatform.Simulations.Economics.Macroeconomics;

public sealed class ShortRunMacroModel : ISimulationModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { PropertyNameCaseInsensitive = true };
    private readonly IReadOnlyList<IMacroMechanism> mechanisms;

    public ShortRunMacroModel() : this([
        new FiscalTransmissionMechanism(), new MonetaryTransmissionMechanism(),
        new BusinessBehaviorMechanism(), new HouseholdLaborMechanism(), new ExternalShockMechanism()]) { }

    public ShortRunMacroModel(IReadOnlyList<IMacroMechanism> mechanisms) => this.mechanisms = mechanisms;

    public SimulationModelDescriptor Descriptor { get; } =
        new("Economics.ShortRunMacro", "1.0.0", "Short-Run Macroeconomic Country");

    public ValueTask<JsonElement> InitializeAsync(InitializationContext context, CancellationToken ct)
    {
        var configuration = context.Configuration.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? new MacroConfiguration() : context.Configuration.Deserialize<MacroConfiguration>(JsonOptions) ?? new();
        configuration = Sanitize(configuration);
        ValidateConfiguration(configuration);
        var gap = 100m * (configuration.InitialOutputIndex - configuration.InitialPotentialOutputIndex) /
                  configuration.InitialPotentialOutputIndex;
        var state = new MacroState(0, configuration.InitialOutputIndex, configuration.InitialPotentialOutputIndex,
            gap, configuration.InitialInflation, configuration.InitialInflation, configuration.InitialUnemployment,
            configuration.InitialPolicyRate, 0, configuration.InitialDebtToOutput, 50, 50, 0,
            100, 0, configuration, null);
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(state, JsonOptions));
    }

    public ValueTask<ActionValidationResult> ValidateActionAsync(ActionValidationContext context, CancellationToken ct)
    {
        if (!MacroActions.All.Contains(context.ActionCode)) return Invalid("action.unsupported", "Unsupported macroeconomic action.");
        var state = context.State.Deserialize<MacroState>(JsonOptions);
        if (state?.Configuration.EnabledActions is { Count: > 0 } enabled && !enabled.Contains(context.ActionCode))
            return Invalid("action.disabled", "This action is disabled by the published scenario.");
        if (context.Payload.ValueKind != JsonValueKind.Object) return Invalid("action.payload_invalid", "Action payload must be an object.");
        if (context.ActionCode == MacroActions.DirectionalPrediction)
        {
            var target = context.Payload.TryGetProperty("targetActionCode", out var value) ? value.GetString() : null;
            return target is not null && MacroActions.All.Contains(target) &&
                target is not (MacroActions.TriggerShock or MacroActions.DirectionalPrediction) && ValidPrediction(context.Payload)
                ? Valid() : Invalid("prediction.invalid", "Prediction target, directions, or explanation are invalid.");
        }
        if (!TryIntensity(context.Payload, out var intensity)) return Invalid("action.intensity_invalid", "Intensity must be Mild, Moderate, or Strong.");
        if (state?.Configuration.AllowedIntensities is { Count: > 0 } intensities && !intensities.Contains(intensity))
            return Invalid("action.intensity_disabled", "This intensity is disabled by the published scenario.");
        if (context.ActionCode == MacroActions.TriggerShock)
        {
            var type = Text(context.Payload, "type");
            return AllowedShock(type) ? Valid() : Invalid("shock.type_invalid", "The shock type is not supported.");
        }
        var direction = Text(context.Payload, "direction");
        var allowed = context.ActionCode switch
        {
            MacroActions.FiscalPolicy => direction is "expand" or "contract" or "neutral",
            MacroActions.MonetaryPolicy => direction is "tighten" or "ease" or "neutral",
            MacroActions.BusinessStrategy => direction is "expand" or "contract" or "neutral",
            MacroActions.HouseholdLaborStance => direction is "support" or "restrain" or "neutral",
            _ => false
        };
        if (!allowed) return Invalid("action.direction_invalid", "Direction is outside the action's closed vocabulary.");
        if (context.Payload.TryGetProperty("prediction", out var prediction) && !ValidPrediction(prediction))
            return Invalid("prediction.invalid", "Predictions require increase, decrease, or stable directions and a bounded explanation.");
        return Valid();
    }

    public ValueTask<RoundExecutionResult> ExecuteRoundAsync(RoundExecutionContext context, CancellationToken ct)
    {
        var prior = context.State.Deserialize<MacroState>(JsonOptions) ?? throw new JsonException("Invalid macro state.");
        var predictions = context.Actions.Where(x => x.Code == MacroActions.DirectionalPrediction)
            .Select(ParseStandalonePrediction).GroupBy(x => x.Target).ToDictionary(x => x.Key, x => x.Last().Prediction);
        var decisions = context.Actions.Where(x => x.Code is not (MacroActions.TriggerShock or MacroActions.DirectionalPrediction))
            .Select(ParseDecision).Select(x => x.Prediction is null && predictions.TryGetValue(x.Code, out var prediction)
                ? x with { Prediction = prediction } : x).ToArray();
        var shocks = (prior.Configuration.ScheduledShocks ?? []).Where(x => x.Round == context.RoundNumber)
            .Concat(context.Actions.Where(x => x.Code == MacroActions.TriggerShock).Select(ParseShock)).ToArray();
        var mechanismContext = new MacroMechanismContext(prior, decisions, shocks, context.RoundNumber);
        var contributions = mechanisms.Select(x => x.Evaluate(mechanismContext)).ToArray();
        var demand = contributions.Sum(x => x.DemandPressure);
        var supply = contributions.Sum(x => x.SupplyPressure);
        var potential = MacroMath.Clamp(prior.PotentialOutputIndex + contributions.Sum(x => x.PotentialOutputChange), 70, 160);
        var gap = MacroMath.Clamp(prior.OutputGap * 0.55m + demand - supply * 0.55m, -12, 12);
        var output = potential * (1 + gap / 100m);
        var inflation = MacroMath.Clamp(prior.Configuration.InflationPersistence * prior.Inflation +
            (1 - prior.Configuration.InflationPersistence) * prior.ExpectedInflation + gap * 0.22m + supply * 0.48m, -3, 20);
        var unemployment = MacroMath.Clamp(prior.Unemployment - prior.Configuration.OkunCoefficient * (gap - prior.OutputGap) +
            0.12m * (5m - prior.Unemployment), 1.5m, 25m);
        var policyRate = MacroMath.Clamp(prior.PolicyRate + contributions.Sum(x => x.PolicyRateChange), 0, 20);
        var fiscalBalance = MacroMath.Clamp(prior.FiscalBalance * 0.4m + contributions.Sum(x => x.FiscalBalanceChange), -12, 12);
        var debt = MacroMath.Clamp(prior.DebtToOutput - fiscalBalance * 0.22m - gap * 0.03m, 0, 200);
        var expected = MacroMath.Clamp(prior.ExpectedInflation * 0.7m + prior.Inflation * 0.3m, -2, 15);
        var business = MacroMath.Clamp(prior.BusinessConfidence + gap * 0.35m - Math.Abs(inflation - 2m) * 0.12m, 0, 100);
        var consumer = MacroMath.Clamp(prior.ConsumerConfidence - (unemployment - prior.Unemployment) * 1.8m - Math.Max(0, inflation - 3m) * 0.1m, 0, 100);
        var householdStance = MacroMath.NetStance(decisions, MacroActions.HouseholdLaborStance, "support", "restrain");
        var wagePressure = MacroMath.Clamp(prior.WagePressure * 0.5m + householdStance * 0.35m + gap * 0.12m, -5, 8);
        var monetaryRateChange = contributions.Where(x => x.Mechanism == "Monetary transmission").Sum(x => x.PolicyRateChange);
        var nextLaggedMonetaryDemand = -monetaryRateChange * prior.Configuration.MonetarySensitivity * 1.1m;
        var provisional = prior with { Quarter = context.RoundNumber, OutputIndex = output, PotentialOutputIndex = potential,
            OutputGap = gap, Inflation = inflation, ExpectedInflation = expected, Unemployment = unemployment,
            PolicyRate = policyRate, FiscalBalance = fiscalBalance, DebtToOutput = debt,
            BusinessConfidence = business, ConsumerConfidence = consumer, WagePressure = wagePressure,
            Productivity = MacroMath.Clamp(prior.Productivity + contributions.Sum(x => x.PotentialOutputChange) * 0.4m, 70, 160),
            LaggedMonetaryDemand = nextLaggedMonetaryDemand };
        var conflicts = DetectConflicts(decisions, shocks).ToList();
        if (inflation > prior.Inflation && output < prior.OutputIndex)
            conflicts.Add(new("STAGFLATION_CONDITIONS", "Inflation rose while output fell, creating a short-run stabilization trade-off."));
        var assessments = decisions.Where(x => x.Prediction is not null).Select(x => Assess(x, prior, provisional)).ToArray();
        var objectives = EvaluateObjectives(provisional);
        var causal = contributions.Where(x => x.DemandPressure != 0 || x.SupplyPressure != 0 || x.PotentialOutputChange != 0 || x.PolicyRateChange != 0)
            .Select(x => x.Explanation).ToArray();
        var report = new MacroRoundReport(context.RoundNumber, contributions, conflicts, assessments, objectives, causal);
        var state = provisional with { LastReport = report };
        var metrics = new Dictionary<string, decimal>
        {
            ["outputIndex"] = output, ["outputGap"] = gap, ["inflation"] = inflation,
            ["unemployment"] = unemployment, ["policyRate"] = policyRate,
            ["debtToOutput"] = debt, ["conceptualScore"] = assessments.Length == 0 ? 0 : assessments.Average(x => x.ConceptualScore),
            ["objectivesAchieved"] = objectives.Count(x => x.Achieved)
        };
        return ValueTask.FromResult(new RoundExecutionResult(JsonSerializer.SerializeToElement(state, JsonOptions), metrics));
    }

    public ValueTask<JsonElement> GenerateVisibleStateAsync(StateProjectionContext context, CancellationToken ct)
    {
        var state = context.State.Deserialize<MacroState>(JsonOptions) ?? throw new JsonException("Invalid macro state.");
        if (context.Capabilities.Contains(MacroCapabilities.ViewAll))
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(state, JsonOptions));
        var view = new Dictionary<string, object?>
        {
            ["quarter"] = state.Quarter, ["outputIndex"] = state.OutputIndex,
            ["inflation"] = state.Inflation, ["unemployment"] = state.Unemployment
        };
        if (context.Capabilities.Contains(MacroCapabilities.ViewFiscal))
        { view["fiscalBalance"] = state.FiscalBalance; view["debtToOutput"] = state.DebtToOutput; view["outputGap"] = state.OutputGap; }
        if (context.Capabilities.Contains(MacroCapabilities.ViewMonetary))
        { view["policyRate"] = state.PolicyRate; view["expectedInflation"] = state.ExpectedInflation; view["outputGap"] = state.OutputGap; }
        if (context.Capabilities.Contains(MacroCapabilities.ViewBusiness))
        { view["businessConfidence"] = state.BusinessConfidence; view["wagePressure"] = state.WagePressure; view["productivity"] = state.Productivity; }
        if (context.Capabilities.Contains(MacroCapabilities.ViewHousehold))
        { view["consumerConfidence"] = state.ConsumerConfidence; view["wagePressure"] = state.WagePressure; view["realIncomePressure"] = state.Inflation - state.WagePressure; }
        if (state.LastReport is not null)
        {
            var actionCodes = new List<string>();
            if (context.Capabilities.Contains(MacroCapabilities.SetFiscalPolicy)) actionCodes.Add(MacroActions.FiscalPolicy);
            if (context.Capabilities.Contains(MacroCapabilities.SetMonetaryPolicy)) actionCodes.Add(MacroActions.MonetaryPolicy);
            if (context.Capabilities.Contains(MacroCapabilities.SetBusinessStrategy)) actionCodes.Add(MacroActions.BusinessStrategy);
            if (context.Capabilities.Contains(MacroCapabilities.SetHouseholdLaborStance)) actionCodes.Add(MacroActions.HouseholdLaborStance);
            view["assessments"] = state.LastReport.Assessments.Where(x => actionCodes.Contains(x.ActionCode)).ToArray();
            view["policyConflicts"] = state.LastReport.Conflicts;
            var mechanisms = new List<string>();
            if (context.Capabilities.Contains(MacroCapabilities.ViewFiscal)) mechanisms.Add("Fiscal transmission");
            if (context.Capabilities.Contains(MacroCapabilities.ViewMonetary)) mechanisms.Add("Monetary transmission");
            if (context.Capabilities.Contains(MacroCapabilities.ViewBusiness)) mechanisms.Add("Business investment and production");
            if (context.Capabilities.Contains(MacroCapabilities.ViewHousehold)) mechanisms.Add("Household demand and wage bargaining");
            view["causalExplanations"] = state.LastReport.Contributions.Where(x => mechanisms.Contains(x.Mechanism))
                .Select(x => x.Explanation).ToArray();
            if (context.Capabilities.Contains(MacroCapabilities.ViewMonetary))
                view["laggedEffect"] = state.LaggedMonetaryDemand == 0 ? "No inherited monetary-demand effect." :
                    state.LaggedMonetaryDemand < 0 ? "Earlier monetary tightening continues to restrain demand." :
                    "Earlier monetary easing continues to support demand.";
        }
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(view, JsonOptions));
    }

    private static MacroConfiguration Sanitize(MacroConfiguration x) => x with
    {
        InitialPotentialOutputIndex = MacroMath.Clamp(x.InitialPotentialOutputIndex, 50, 200),
        InitialOutputIndex = MacroMath.Clamp(x.InitialOutputIndex, 50, 200),
        InitialInflation = MacroMath.Clamp(x.InitialInflation, -2, 15), InitialUnemployment = MacroMath.Clamp(x.InitialUnemployment, 1.5m, 25),
        InitialPolicyRate = MacroMath.Clamp(x.InitialPolicyRate, 0, 20), InitialDebtToOutput = MacroMath.Clamp(x.InitialDebtToOutput, 0, 180),
        FiscalMultiplier = MacroMath.Clamp(x.FiscalMultiplier, 0.1m, 2), MonetarySensitivity = MacroMath.Clamp(x.MonetarySensitivity, 0.1m, 2),
        InflationPersistence = MacroMath.Clamp(x.InflationPersistence, 0, 0.95m), OkunCoefficient = MacroMath.Clamp(x.OkunCoefficient, 0.1m, 1)
    };

    private static void ValidateConfiguration(MacroConfiguration configuration)
    {
        if (configuration.Objectives is { } o && o.InflationMinimum > o.InflationMaximum)
            throw new JsonException("Objective inflation range is invalid.");
        if ((configuration.ScheduledShocks ?? []).Any(x => x.Round < 1 || !AllowedShock(x.Type) || !Enum.IsDefined(x.Intensity)))
            throw new JsonException("Scheduled shock configuration is invalid.");
    }

    private static MacroDecision ParseDecision(RoundAction action) => new(action.Code, Text(action.Payload, "direction"),
        ParseIntensity(action.Payload), action.Payload.TryGetProperty("prediction", out var p) ? p.Deserialize<DirectionPrediction>(JsonOptions) : null);
    private static (string Target, DirectionPrediction Prediction) ParseStandalonePrediction(RoundAction action) =>
        (action.Payload.GetProperty("targetActionCode").GetString()!, action.Payload.Deserialize<DirectionPrediction>(JsonOptions)!);
    private static ScheduledMacroShock ParseShock(RoundAction action) => new(0, Text(action.Payload, "type"), ParseIntensity(action.Payload));
    private static PolicyIntensity ParseIntensity(JsonElement payload) => Enum.Parse<PolicyIntensity>(Text(payload, "intensity"), true);
    private static bool TryIntensity(JsonElement payload, out PolicyIntensity intensity) =>
        Enum.TryParse(Text(payload, "intensity"), true, out intensity) && Enum.IsDefined(intensity);
    private static string Text(JsonElement payload, string name) => payload.TryGetProperty(name, out var value) ? value.GetString()?.Trim().ToLowerInvariant() ?? "" : "";
    private static bool AllowedShock(string type) => type is "demand_slump" or "demand_boom" or "supply_disruption" or "productivity_boost" or "confidence_crisis";
    private static bool ValidPrediction(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object) return false;
        var directions = new[] { Text(p, "output"), Text(p, "inflation"), Text(p, "unemployment") };
        var explanation = p.TryGetProperty("explanation", out var e) ? e.GetString() : null;
        return directions.All(x => x is "increase" or "decrease" or "stable") && explanation is { Length: > 0 and <= 1000 };
    }
    private static ValueTask<ActionValidationResult> Valid() => ValueTask.FromResult(new ActionValidationResult(true));
    private static ValueTask<ActionValidationResult> Invalid(string code, string message) => ValueTask.FromResult(new ActionValidationResult(false, code, message));

    private static IReadOnlyList<PolicyConflict> DetectConflicts(IReadOnlyList<MacroDecision> decisions, IReadOnlyList<ScheduledMacroShock> shocks)
    {
        var result = new List<PolicyConflict>();
        var fiscal = MacroMath.NetStance(decisions, MacroActions.FiscalPolicy, "expand", "contract");
        var monetary = MacroMath.NetStance(decisions, MacroActions.MonetaryPolicy, "tighten", "ease");
        var business = MacroMath.NetStance(decisions, MacroActions.BusinessStrategy, "expand", "contract");
        var household = MacroMath.NetStance(decisions, MacroActions.HouseholdLaborStance, "support", "restrain");
        if (fiscal * monetary > 0) result.Add(new("FISCAL_MONETARY_OPPOSITION", "Fiscal and monetary authorities are pushing aggregate demand in opposing directions."));
        if (business * household < 0) result.Add(new("PRIVATE_SECTOR_DIVERGENCE", "Business and household stances point in opposite demand directions."));
        if (fiscal > 0 && shocks.Any(x => x.Type == "supply_disruption")) result.Add(new("STAGFLATION_TRADEOFF", "Fiscal expansion supports output during a supply disruption but can intensify inflation pressure."));
        if (household >= 3 && business >= 3) result.Add(new("WAGE_PRICE_PRESSURE", "Strong household wage pressure and business expansion may reinforce near-term inflation."));
        return result;
    }

    private static PredictionAssessment Assess(MacroDecision decision, MacroState before, MacroState after)
    {
        var p = decision.Prediction!; var feedback = new List<string>();
        var pairs = new[] { ("output", p.Output, after.OutputIndex - before.OutputIndex), ("inflation", p.Inflation, after.Inflation - before.Inflation), ("unemployment", p.Unemployment, after.Unemployment - before.Unemployment) };
        var correct = pairs.Count(x => Direction(x.Item3) == x.Item2.ToLowerInvariant());
        foreach (var item in pairs.Where(x => Direction(x.Item3) != x.Item2.ToLowerInvariant())) feedback.Add($"Reconsider the predicted direction of {item.Item1}.");
        var terms = decision.Code switch
        {
            MacroActions.FiscalPolicy => new[] { "demand", "spending", "tax", "multiplier" },
            MacroActions.MonetaryPolicy => new[] { "interest", "borrowing", "investment", "demand" },
            MacroActions.BusinessStrategy => new[] { "investment", "production", "capacity", "demand" },
            _ => new[] { "consumption", "wage", "demand", "cost" }
        };
        var mechanism = terms.Any(x => p.Explanation.Contains(x, StringComparison.OrdinalIgnoreCase));
        if (!mechanism) feedback.Add("Name the causal mechanism linking the action to the predicted outcomes.");
        return new(decision.Code, correct, 3, mechanism, Math.Round((correct / 3m) * 80m + (mechanism ? 20m : 0m), 2), feedback);
    }
    private static string Direction(decimal delta) => Math.Abs(delta) <= 0.05m ? "stable" : delta > 0 ? "increase" : "decrease";

    private static IReadOnlyList<ObjectiveResult> EvaluateObjectives(MacroState state)
    {
        var o = state.Configuration.Objectives ?? new();
        return
        [
            new("PRICE_STABILITY", state.Inflation >= o.InflationMinimum && state.Inflation <= o.InflationMaximum, state.Inflation, $"{o.InflationMinimum} to {o.InflationMaximum}"),
            new("EMPLOYMENT", state.Unemployment <= o.UnemploymentMaximum, state.Unemployment, $"<= {o.UnemploymentMaximum}"),
            new("OUTPUT_STABILITY", Math.Abs(state.OutputGap) <= o.OutputGapAbsoluteMaximum, state.OutputGap, $"absolute value <= {o.OutputGapAbsoluteMaximum}"),
            new("DEBT_SUSTAINABILITY", state.DebtToOutput <= o.DebtToOutputMaximum, state.DebtToOutput, $"<= {o.DebtToOutputMaximum}")
        ];
    }
}

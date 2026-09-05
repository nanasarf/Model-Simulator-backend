using System.Text.Json.Serialization;

namespace SimulationPlatform.Simulations.Economics.Macroeconomics;

public static class MacroActions
{
    public const string FiscalPolicy = "SET_FISCAL_POLICY";
    public const string MonetaryPolicy = "SET_MONETARY_POLICY";
    public const string BusinessStrategy = "SET_BUSINESS_STRATEGY";
    public const string HouseholdLaborStance = "SET_HOUSEHOLD_LABOR_STANCE";
    public const string TriggerShock = "TRIGGER_EXTERNAL_SHOCK";
    public const string DirectionalPrediction = "SUBMIT_DIRECTIONAL_PREDICTION";
    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { FiscalPolicy, MonetaryPolicy, BusinessStrategy, HouseholdLaborStance, TriggerShock, DirectionalPrediction };
}

public static class MacroCapabilities
{
    public const string SetFiscalPolicy = "MACRO_SET_FISCAL_POLICY";
    public const string SetMonetaryPolicy = "MACRO_SET_MONETARY_POLICY";
    public const string SetBusinessStrategy = "MACRO_SET_BUSINESS_STRATEGY";
    public const string SetHouseholdLaborStance = "MACRO_SET_HOUSEHOLD_LABOR_STANCE";
    public const string TriggerShock = "MACRO_TRIGGER_SHOCK";
    public const string SubmitPrediction = "MACRO_SUBMIT_PREDICTION";
    public const string ViewFiscal = "MACRO_VIEW_FISCAL";
    public const string ViewMonetary = "MACRO_VIEW_MONETARY";
    public const string ViewBusiness = "MACRO_VIEW_BUSINESS";
    public const string ViewHousehold = "MACRO_VIEW_HOUSEHOLD";
    public const string ViewAll = "MACRO_VIEW_ALL";
}

[JsonConverter(typeof(JsonStringEnumConverter<PolicyIntensity>))]
public enum PolicyIntensity { Mild = 1, Moderate = 2, Strong = 3 }

public sealed record DirectionPrediction(string Output, string Inflation, string Unemployment, string Explanation);
public sealed record MacroDecision(string Code, string Direction, PolicyIntensity Intensity, DirectionPrediction? Prediction);
public sealed record ScheduledMacroShock(int Round, string Type, PolicyIntensity Intensity);

public sealed record MacroObjectiveConfiguration(
    decimal InflationMinimum = 1m, decimal InflationMaximum = 3m,
    decimal UnemploymentMaximum = 6m, decimal OutputGapAbsoluteMaximum = 2m,
    decimal DebtToOutputMaximum = 80m);

public sealed record MacroConfiguration(
    decimal InitialOutputIndex = 100m,
    decimal InitialPotentialOutputIndex = 100m,
    decimal InitialInflation = 2m,
    decimal InitialUnemployment = 5m,
    decimal InitialPolicyRate = 3m,
    decimal InitialDebtToOutput = 55m,
    decimal FiscalMultiplier = 0.8m,
    decimal MonetarySensitivity = 0.65m,
    decimal InflationPersistence = 0.65m,
    decimal OkunCoefficient = 0.4m,
    MacroObjectiveConfiguration? Objectives = null,
    List<ScheduledMacroShock>? ScheduledShocks = null,
    HashSet<string>? EnabledActions = null,
    HashSet<PolicyIntensity>? AllowedIntensities = null);

public sealed record CausalContribution(string Mechanism, decimal DemandPressure, decimal SupplyPressure,
    decimal PotentialOutputChange, decimal PolicyRateChange, decimal FiscalBalanceChange, string Explanation);
public sealed record PolicyConflict(string Code, string Explanation);
public sealed record PredictionAssessment(string ActionCode, int CorrectDirections, int DirectionCount,
    bool MechanismRecognized, decimal ConceptualScore, IReadOnlyList<string> Feedback);
public sealed record ObjectiveResult(string Code, bool Achieved, decimal Actual, string Target);
public sealed record MacroRoundReport(int Round, IReadOnlyList<CausalContribution> Contributions,
    IReadOnlyList<PolicyConflict> Conflicts, IReadOnlyList<PredictionAssessment> Assessments,
    IReadOnlyList<ObjectiveResult> Objectives, IReadOnlyList<string> CausalExplanation);

public sealed record MacroState(
    int Quarter,
    decimal OutputIndex,
    decimal PotentialOutputIndex,
    decimal OutputGap,
    decimal Inflation,
    decimal ExpectedInflation,
    decimal Unemployment,
    decimal PolicyRate,
    decimal FiscalBalance,
    decimal DebtToOutput,
    decimal BusinessConfidence,
    decimal ConsumerConfidence,
    decimal WagePressure,
    decimal Productivity,
    decimal LaggedMonetaryDemand,
    MacroConfiguration Configuration,
    MacroRoundReport? LastReport);

public sealed record MacroMechanismContext(MacroState State, IReadOnlyList<MacroDecision> Decisions,
    IReadOnlyList<ScheduledMacroShock> Shocks, int Round);

public interface IMacroMechanism
{
    CausalContribution Evaluate(MacroMechanismContext context);
}

public static class MacroModelMetadata
{
    public static readonly string[] Assumptions =
    [
        "Prices and wages adjust gradually within the short run.",
        "Aggregate demand affects output, inflation pressure, and cyclical unemployment.",
        "Inflation expectations are adaptive and bounded.",
        "Monetary transmission is partially lagged.",
        "Potential output changes slowly except under productivity shocks."
    ];

    public static readonly string[] Limitations =
    [
        "Pedagogical model, not a forecast or empirical country calibration.",
        "No international trade, exchange rates, detailed finance, heterogeneous agents, or long-run growth model.",
        "Qualitative policy packages abstract from legislative and implementation detail."
    ];
}

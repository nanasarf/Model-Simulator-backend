using System.Security.Claims;
using SimulationPlatform.Identity.Authorization;
using SimulationPlatform.Simulations.Core.Authoring;
using SimulationPlatform.Simulations.Core.Contracts;
using SimulationPlatform.Simulations.Economics.Macroeconomics;
using SimulationPlatform.Simulations.Economics.CompetitiveMarket;

namespace SimulationPlatform.Api;

public static class AuthoringEndpoints
{
    public static void MapAuthoring(this WebApplication app)
    {
        app.MapGet("/api/v1/simulation-models/{modelIdentifier}/{modelVersion}/authoring-catalog", (string modelIdentifier, string modelVersion, IAuthoringCapabilityCatalog catalogs) =>
            Results.Ok(catalogs.GetCatalog(modelIdentifier, modelVersion))).RequireAuthorization(PlatformPolicies.Instructor);
        app.MapPost("/api/v1/scenario-blueprints/validate", (ScenarioBlueprint blueprint, ScenarioBlueprintValidator validator) =>
            Results.Ok(validator.Validate(blueprint))).RequireAuthorization(PlatformPolicies.Instructor);
    }
}

public sealed class DefaultAuthoringCapabilityCatalog(ISimulationModelRegistry models) : IAuthoringCapabilityCatalog
{
    public AuthoringCatalog GetCatalog(string identifier, string version)
    {
        var descriptor = models.Resolve(identifier, version).Descriptor;
        var phases = new HashSet<string>(StringComparer.Ordinal) { "Briefing", "Prediction", "Decision", "Locked", "Simulation", "Results", "Discussion" };
        if (identifier == "Economics.ShortRunMacro") return new(identifier, version,
            S("GOVERNMENT","CENTRAL_BANK","BUSINESS","HOUSEHOLD_LABOR"), S(MacroCapabilities.SetFiscalPolicy,MacroCapabilities.SetMonetaryPolicy,MacroCapabilities.SetBusinessStrategy,MacroCapabilities.SetHouseholdLaborStance,MacroCapabilities.SubmitPrediction),
            S(MacroActions.FiscalPolicy,MacroActions.MonetaryPolicy,MacroActions.BusinessStrategy,MacroActions.HouseholdLaborStance,MacroActions.DirectionalPrediction), phases,
            S("inflation","unemployment","outputGap","policyRate","debtToOutput","businessConfidence","consumerConfidence","productivity"), S("demand_slump","demand_boom","supply_disruption","productivity_boost","confidence_crisis"),
            S("inflation","unemployment","outputGap","policyRate"), S("equals","gte","lte","exists"), S("increase","decrease","neutral"), S("Mild","Moderate","Strong"), S("PredictionAccuracy","ConceptualUnderstanding","ObjectiveAchievement"));
        if (identifier == "Economics.CompetitiveMarket") return new(identifier, version,
            S("BUYER","SELLER","GOVERNMENT"), S(MarketCapabilities.Bid,MarketCapabilities.Ask,MarketCapabilities.Predict),
            S(MarketActions.SubmitBid,MarketActions.SubmitAsk,MarketActions.Predict), phases,
            S("price","quantity","shortage","surplus","consumerSurplus","producerSurplus","totalSurplus","deadweightLoss"), S("demand_shock","supply_shock"),
            S("price","quantity","consumerSurplus","producerSurplus"), S("equals","gte","lte","exists"), S("increase","decrease","neutral"), S("Mild","Moderate","Strong"), S("EquilibriumReasoning","DemandSupplyReasoning","SurplusReasoning"));
        return new AuthoringCatalog(descriptor.Identifier, descriptor.Version,
            new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal), phases, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
    }
    private static HashSet<string> S(params string[] values) => new(values, StringComparer.Ordinal);
}

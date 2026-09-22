namespace SimulationPlatform.Simulations.Economics.Macroeconomics;

public sealed class FiscalTransmissionMechanism : IMacroMechanism
{
    public CausalContribution Evaluate(MacroMechanismContext context)
    {
        var stance = MacroMath.NetStance(context.Decisions, MacroActions.FiscalPolicy, "expand", "contract");
        var demand = stance * context.State.Configuration.FiscalMultiplier;
        return new("Fiscal transmission", demand, 0, 0, 0, -stance * 0.55m,
            stance switch { > 0 => "Fiscal expansion raises aggregate demand and weakens the public balance.", < 0 => "Fiscal consolidation lowers aggregate demand and improves the public balance.", _ => "Fiscal policy is neutral this quarter." });
    }
}

public sealed class MonetaryTransmissionMechanism : IMacroMechanism
{
    public CausalContribution Evaluate(MacroMechanismContext context)
    {
        var tightening = MacroMath.NetStance(context.Decisions, MacroActions.MonetaryPolicy, "tighten", "ease");
        var rate = tightening * 0.5m;
        var demand = context.State.LaggedMonetaryDemand - tightening * context.State.Configuration.MonetarySensitivity * 0.45m;
        return new("Monetary transmission", demand, 0, 0, rate, 0,
            tightening switch { > 0 => "Tighter policy raises the policy rate and restrains interest-sensitive demand with a lag.", < 0 => "Easier policy lowers the policy rate and supports interest-sensitive demand with a lag.", _ => "The current monetary stance adds only inherited lagged pressure." });
    }
}

public sealed class BusinessBehaviorMechanism : IMacroMechanism
{
    public CausalContribution Evaluate(MacroMechanismContext context)
    {
        var expansion = MacroMath.NetStance(context.Decisions, MacroActions.BusinessStrategy, "expand", "contract");
        var confidence = (context.State.BusinessConfidence - 50m) / 50m;
        return new("Business investment and production", expansion * 0.55m + confidence * 0.2m,
            -expansion * 0.12m, expansion * 0.05m, 0, 0,
            expansion switch { > 0 => "Businesses expand investment and production, supporting demand and near-term capacity.", < 0 => "Businesses retrench, reducing investment demand and near-term capacity utilization.", _ => "Business plans make no discretionary contribution." });
    }
}

public sealed class HouseholdLaborMechanism : IMacroMechanism
{
    public CausalContribution Evaluate(MacroMechanismContext context)
    {
        var support = MacroMath.NetStance(context.Decisions, MacroActions.HouseholdLaborStance, "support", "restrain");
        var confidence = (context.State.ConsumerConfidence - 50m) / 50m;
        return new("Household demand and wage bargaining", support * 0.45m + confidence * 0.15m,
            support * 0.18m, 0, 0, 0,
            support switch { > 0 => "Consumption and wage demands support demand while adding wage-cost pressure.", < 0 => "Household restraint lowers demand and wage-cost pressure.", _ => "Household and labor stance is neutral." });
    }
}

public sealed class ExternalShockMechanism : IMacroMechanism
{
    public CausalContribution Evaluate(MacroMechanismContext context)
    {
        decimal demand = 0, supply = 0, potential = 0;
        var explanations = new List<string>();
        foreach (var shock in context.Shocks)
        {
            var size = (decimal)shock.Intensity;
            switch (shock.Type)
            {
                case "demand_slump": demand -= size * 0.65m; explanations.Add("A demand shock reduces spending."); break;
                case "demand_boom": demand += size * 0.65m; explanations.Add("A demand shock raises spending."); break;
                case "supply_disruption": supply += size * 0.7m; potential -= size * 0.18m; explanations.Add("A supply disruption raises costs and temporarily reduces capacity."); break;
                case "productivity_boost": supply -= size * 0.45m; potential += size * 0.25m; explanations.Add("Higher productivity expands capacity and reduces cost pressure."); break;
                case "confidence_crisis": demand -= size * 0.5m; explanations.Add("A confidence crisis restrains private demand."); break;
            }
        }
        return new("External shocks", demand, supply, potential, 0, 0,
            explanations.Count == 0 ? "No external shock occurs." : string.Join(" ", explanations));
    }
}

internal static class MacroMath
{
    public static decimal NetStance(IEnumerable<MacroDecision> decisions, string code, string positive, string negative) =>
        Clamp(decisions.Where(x => x.Code == code).Sum(x => x.Direction == positive ? (decimal)x.Intensity : x.Direction == negative ? -(decimal)x.Intensity : 0m), -3m, 3m);
    public static decimal Clamp(decimal value, decimal min, decimal max) => Math.Min(max, Math.Max(min, value));
}

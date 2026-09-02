using System.Text.Json;
using SimulationPlatform.Simulations.Core.Contracts;

namespace SimulationPlatform.Simulations.Economics.SupplyDemand;

public sealed record SupplyDemandState(decimal DemandIntercept, decimal SupplyIntercept, decimal Price, decimal Quantity);

public sealed class SupplyDemandModel : ISimulationModel
{
    public SimulationModelDescriptor Descriptor { get; } = new("Economics.SupplyDemand", "1.0.0", "Supply and Demand Market");

    public ValueTask<JsonElement> InitializeAsync(InitializationContext context, CancellationToken ct)
    {
        var state = new SupplyDemandState(100, 20, 60, 40);
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(state));
    }

    public ValueTask<ActionValidationResult> ValidateActionAsync(ActionValidationContext context, CancellationToken ct)
    {
        if (context.ActionCode != "CHANGE_OUTPUT") return ValueTask.FromResult(new ActionValidationResult(false, "action.unsupported", "Unsupported market action."));
        if (!context.Payload.TryGetProperty("direction", out var direction) || direction.GetString() is not ("increase" or "decrease"))
            return ValueTask.FromResult(new ActionValidationResult(false, "action.direction_invalid", "Direction must be increase or decrease."));
        return ValueTask.FromResult(new ActionValidationResult(true));
    }

    public ValueTask<RoundExecutionResult> ExecuteRoundAsync(RoundExecutionContext context, CancellationToken ct)
    {
        var state = context.State.Deserialize<SupplyDemandState>() ?? throw new JsonException("Invalid supply-demand state.");
        var supply = state.SupplyIntercept;
        foreach (var action in context.Actions.Where(x => x.Code == "CHANGE_OUTPUT"))
            supply += action.Payload.GetProperty("direction").GetString() == "increase" ? -10 : 10;
        var price = (state.DemandIntercept - supply) / 2;
        var quantity = state.DemandIntercept - price;
        var result = new SupplyDemandState(state.DemandIntercept, supply, price, quantity);
        return ValueTask.FromResult(new RoundExecutionResult(JsonSerializer.SerializeToElement(result),
            new Dictionary<string, decimal> { ["marketPrice"] = price, ["quantityTraded"] = quantity }));
    }

    public ValueTask<JsonElement> GenerateVisibleStateAsync(StateProjectionContext context, CancellationToken ct)
    {
        var state = context.State.Deserialize<SupplyDemandState>() ?? throw new JsonException("Invalid supply-demand state.");
        var visible = context.Capabilities.Contains("VIEW_CURVES")
            ? JsonSerializer.SerializeToElement(state)
            : JsonSerializer.SerializeToElement(new { state.Price, state.Quantity });
        return ValueTask.FromResult(visible);
    }
}

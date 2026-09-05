using System.Text.Json;
using SimulationPlatform.Simulations.Core.Contracts;

namespace SimulationPlatform.Simulations.Economics.CompetitiveMarket;

public sealed class CompetitiveMarketModel : ISimulationModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public SimulationModelDescriptor Descriptor => new("Economics.CompetitiveMarket", "1.0.0", "Competitive Market");
    public ValueTask<JsonElement> InitializeAsync(InitializationContext context, CancellationToken cancellationToken)
    {
        var c = context.Configuration.Deserialize<MarketConfiguration>(JsonOptions) ?? new();
        var buyers = Enumerable.Range(0, Math.Max(1, c.BuyerCount)).Select(i => new BuyerPrivateInfo(Math.Max(1, c.DemandIntercept / c.DemandSlope - i * 2), Math.Max(1, c.UnitsPerBuyer))).ToArray();
        var sellers = Enumerable.Range(0, Math.Max(1, c.SellerCount)).Select(i => new SellerPrivateInfo(Math.Max(0, c.SupplyIntercept / c.SupplySlope + i * 2), Math.Max(1, c.UnitsPerSeller))).ToArray();
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new MarketState(0, c, buyers, sellers, null, []), JsonOptions));
    }
    public ValueTask<ActionValidationResult> ValidateActionAsync(ActionValidationContext context, CancellationToken cancellationToken)
    {
        if (!MarketActions.All.Contains(context.ActionCode)) return ValueTask.FromResult(new ActionValidationResult(false, "market.action_unsupported", "Unsupported market action."));
        if (context.ActionCode == MarketActions.Predict) return ValueTask.FromResult(context.Payload.Deserialize<MarketPrediction>(JsonOptions) is not null ? new ActionValidationResult(true) : new ActionValidationResult(false, "market.prediction_invalid", "Prediction payload is invalid."));
        if (!context.Payload.TryGetProperty("price", out var price) || !price.TryGetDecimal(out var p) || p < 0 || p > 10000 ||
            !context.Payload.TryGetProperty("quantity", out var quantity) || !quantity.TryGetInt32(out var q) || q < 1 || q > 1000)
            return ValueTask.FromResult(new ActionValidationResult(false, "market.order_invalid", "Orders require bounded non-negative price and quantity."));
        return ValueTask.FromResult(new ActionValidationResult(true));
    }
    public ValueTask<RoundExecutionResult> ExecuteRoundAsync(RoundExecutionContext context, CancellationToken cancellationToken)
    {
        var state = context.State.Deserialize<MarketState>(JsonOptions) ?? throw new InvalidOperationException("Invalid market state.");
        var c = state.Configuration; var shock = c.ScheduledShocks?.Where(x => x.Round == context.RoundNumber).ToArray() ?? [];
        var demandShift = shock.Sum(x => x.Type == "demand_increase" ? (int)x.Intensity * 5 : x.Type == "demand_decrease" ? -(int)x.Intensity * 5 : 0);
        var supplyShift = shock.Sum(x => x.Type == "supply_increase" ? (int)x.Intensity * 5 : x.Type == "supply_decrease" ? -(int)x.Intensity * 5 : 0);
        var buyers = context.Actions.Where(x => x.Code == MarketActions.SubmitBid).Select(ParseOrder).OrderByDescending(x => x.Price).ToList();
        var sellers = context.Actions.Where(x => x.Code == MarketActions.SubmitAsk).Select(ParseOrder).OrderBy(x => x.Price).ToList();
        if (buyers.Count == 0) buyers = state.Buyers.Select(x => new Order(x.Valuation + demandShift, x.QuantityAvailable)).ToList();
        if (sellers.Count == 0) sellers = state.Sellers.Select(x => new Order(x.Cost - supplyShift, x.QuantityAvailable)).ToList();
        var transactions = new List<MarketTransaction>(); decimal cs = 0, ps = 0; var bi = 0; var si = 0; var unmatchedDemand = buyers.Sum(x => x.Quantity); var unmatchedSupply = sellers.Sum(x => x.Quantity);
        while (bi < buyers.Count && si < sellers.Count)
        {
            var b = buyers[bi]; var s = sellers[si]; var effectiveAsk = s.Price + c.UnitTax - c.UnitSubsidy;
            if (b.Price < effectiveAsk) break;
            var q = Math.Min(b.Quantity, s.Quantity); var price = (b.Price + effectiveAsk) / 2;
            if (c.Policy == MarketPolicyType.PriceCeiling && c.PriceCeiling > 0) price = Math.Min(price, c.PriceCeiling);
            if (c.Policy == MarketPolicyType.PriceFloor && c.PriceFloor > 0) price = Math.Max(price, c.PriceFloor);
            transactions.Add(new(b.Price, s.Price, price, q)); cs += (b.Price - price) * q; ps += (price - s.Price) * q; unmatchedDemand -= q; unmatchedSupply -= q;
            buyers[bi] = b with { Quantity = b.Quantity - q }; sellers[si] = s with { Quantity = s.Quantity - q }; if (buyers[bi].Quantity == 0) bi++; if (sellers[si].Quantity == 0) si++;
        }
        var qty = transactions.Sum(x => x.Quantity); var marketPrice = transactions.Count == 0 ? 0 : transactions.Average(x => x.Price); var total = cs + ps; var potential = buyers.Sum(x => Math.Max(0, x.Price) * x.Quantity) - sellers.Sum(x => Math.Max(0, x.Price) * x.Quantity); var dwl = Math.Max(0, potential - total);
        var result = new MarketRoundResult(context.RoundNumber, marketPrice, qty, unmatchedDemand, unmatchedSupply, cs, ps, total, Math.Max(0, dwl), c.UnitTax * qty, Math.Max(0, dwl), c.UnitTax + c.UnitSubsidy, transactions, ["Orders with willingness to pay above effective cost clear first.", "The clearing price is the midpoint of the marginal matched bid and ask.", c.UnitTax > 0 ? "The per-unit tax creates a wedge and transfers part of surplus to government." : "Unmatched orders represent gains from trade that were not realized."]);
        var next = state with { Round = context.RoundNumber, LastResult = result, History = state.History.Append(result).ToArray() };
        return ValueTask.FromResult(new RoundExecutionResult(JsonSerializer.SerializeToElement(next, JsonOptions), new Dictionary<string, decimal> { ["price"] = marketPrice, ["quantity"] = qty, ["consumerSurplus"] = cs, ["producerSurplus"] = ps, ["totalSurplus"] = total, ["deadweightLoss"] = dwl }));
    }
    public ValueTask<JsonElement> GenerateVisibleStateAsync(StateProjectionContext context, CancellationToken cancellationToken)
    {
        var state = context.State.Deserialize<MarketState>(JsonOptions) ?? throw new InvalidOperationException("Invalid market state."); var view = new Dictionary<string, object?> { ["round"] = state.Round, ["lastResult"] = state.LastResult };
        if (context.Capabilities.Contains(MarketCapabilities.ViewBuyerInfo)) view["buyerInformation"] = state.Buyers;
        if (context.Capabilities.Contains(MarketCapabilities.ViewSellerInfo)) view["sellerInformation"] = state.Sellers;
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(view, JsonOptions));
    }
    private static Order ParseOrder(RoundAction x) { var p = x.Payload.GetProperty("price").GetDecimal(); var q = x.Payload.GetProperty("quantity").GetInt32(); return new(p, q); }
    private sealed record Order(decimal Price, int Quantity);
}

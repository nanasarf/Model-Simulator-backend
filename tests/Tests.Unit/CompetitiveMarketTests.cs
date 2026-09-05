using System.Text.Json;
using SimulationPlatform.Simulations.Core.Contracts;
using SimulationPlatform.Simulations.Economics.CompetitiveMarket;

namespace Tests.Unit;
public sealed class CompetitiveMarketTests
{
    [Fact]
    public async Task Clearing_is_deterministic_and_tracks_surplus()
    {
        var model = new CompetitiveMarketModel();
        var state = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(new MarketConfiguration(BuyerCount: 1, SellerCount: 1, UnitsPerBuyer: 1, UnitsPerSeller: 1)), 4), default);
        var actions = new[] { new RoundAction(MarketActions.SubmitBid, JsonSerializer.SerializeToElement(new { price = 100m, quantity = 1 })), new RoundAction(MarketActions.SubmitAsk, JsonSerializer.SerializeToElement(new { price = 20m, quantity = 1 })) };
        var first = await model.ExecuteRoundAsync(new(state, actions, 4, 1), default); var second = await model.ExecuteRoundAsync(new(state, actions, 4, 1), default);
        Assert.True(JsonElement.DeepEquals(first.State, second.State)); var result = first.State.GetProperty("lastResult"); Assert.Equal(1, result.GetProperty("quantityExchanged").GetInt32()); Assert.True(result.GetProperty("totalSurplus").GetDecimal() > 0);
    }
    [Fact]
    public async Task Private_information_requires_capability()
    {
        var model = new CompetitiveMarketModel(); var state = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(new MarketConfiguration()), 1), default);
        var hidden = await model.GenerateVisibleStateAsync(new(state, new HashSet<string>()), default); var visible = await model.GenerateVisibleStateAsync(new(state, new HashSet<string> { MarketCapabilities.ViewBuyerInfo }), default);
        Assert.False(hidden.TryGetProperty("buyerInformation", out _)); Assert.True(visible.TryGetProperty("buyerInformation", out _));
    }
}

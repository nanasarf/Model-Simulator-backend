using System.Text.Json.Serialization;

namespace SimulationPlatform.Simulations.Economics.CompetitiveMarket;

public static class MarketActions
{
    public const string SubmitBid = "SUBMIT_BUYER_BID";
    public const string SubmitAsk = "SUBMIT_SELLER_ASK";
    public const string Predict = "SUBMIT_MARKET_PREDICTION";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { SubmitBid, SubmitAsk, Predict };
}
public static class MarketCapabilities
{
    public const string Bid = "MARKET_SUBMIT_BID";
    public const string Ask = "MARKET_SUBMIT_ASK";
    public const string Predict = "MARKET_SUBMIT_PREDICTION";
    public const string ViewBuyerInfo = "MARKET_VIEW_BUYER_INFO";
    public const string ViewSellerInfo = "MARKET_VIEW_SELLER_INFO";
    public const string ViewGovernment = "MARKET_VIEW_GOVERNMENT_INFO";
}
[JsonConverter(typeof(JsonStringEnumConverter<MarketPolicyType>))]
public enum MarketPolicyType { None, PriceCeiling, PriceFloor, PerUnitTax, PerUnitSubsidy }
[JsonConverter(typeof(JsonStringEnumConverter<MarketIntensity>))]
public enum MarketIntensity { Mild = 1, Moderate = 2, Strong = 3 }
public sealed record MarketConfiguration(decimal DemandIntercept = 120, decimal SupplyIntercept = 20,
    decimal DemandSlope = 1, decimal SupplySlope = 1, decimal PriceCeiling = 0, decimal PriceFloor = 0,
    decimal UnitTax = 0, decimal UnitSubsidy = 0, MarketPolicyType Policy = MarketPolicyType.None,
    int BuyerCount = 10, int SellerCount = 10, int UnitsPerBuyer = 1, int UnitsPerSeller = 2,
    int MaximumRounds = 4, List<ScheduledMarketShock>? ScheduledShocks = null);
public sealed record BuyerPrivateInfo(decimal Valuation, int QuantityAvailable);
public sealed record SellerPrivateInfo(decimal Cost, int QuantityAvailable);
public sealed record MarketTransaction(decimal BuyerValuation, decimal SellerCost, decimal Price, int Quantity);
public sealed record MarketRoundResult(int Round, decimal Price, int QuantityExchanged, int UnmatchedDemand, int UnmatchedSupply,
    decimal ConsumerSurplus, decimal ProducerSurplus, decimal TotalSurplus, decimal UnrealizedGainsFromTrade,
    decimal GovernmentRevenue, decimal DeadweightLoss, decimal TaxWedge, IReadOnlyList<MarketTransaction> Transactions,
    IReadOnlyList<string> CausalExplanation);
public sealed record ScheduledMarketShock(int Round, string Type, MarketIntensity Intensity);
public sealed record MarketState(int Round, MarketConfiguration Configuration, IReadOnlyList<BuyerPrivateInfo> Buyers,
    IReadOnlyList<SellerPrivateInfo> Sellers, MarketRoundResult? LastResult, IReadOnlyList<MarketRoundResult> History);
public sealed record MarketPrediction(string Price, string Quantity, string ShortageSurplus, string Welfare, string Explanation);
public enum MarketAssessmentDimension { EquilibriumReasoning, DemandSupplyReasoning, Elasticity, ConsumerProducerSurplus, TaxIncidence, PriceControls, CausalMarketReasoning }

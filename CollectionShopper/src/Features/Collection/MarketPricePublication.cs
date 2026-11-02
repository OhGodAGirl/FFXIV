namespace CollectionShopper;

/// <summary>Prevents prices from one world being displayed as prices for a different world.</summary>
internal static class MarketPricePublication
{
    internal static Publication Validate(string? requestedMarket, string? currentWorld,
        IReadOnlyDictionary<uint, MarketPriceQuote> prices)
        => MarketWorldPolicy.Matches(requestedMarket, currentWorld)
            ? new(prices, requestedMarket, false)
            : new(new Dictionary<uint, MarketPriceQuote>(), null, true);

    internal static IReadOnlyDictionary<uint, MarketPriceQuote> Merge(MarketPriceRefreshResult result,
        string requestedMarket, string? previousMarket, IReadOnlyDictionary<uint, MarketPriceQuote> previous)
        => result.MergePrevious(previousMarket == requestedMarket ? previous : new Dictionary<uint, MarketPriceQuote>());

    internal sealed record Publication(IReadOnlyDictionary<uint, MarketPriceQuote> Prices,
        string? Market, bool RequestRefresh);
}

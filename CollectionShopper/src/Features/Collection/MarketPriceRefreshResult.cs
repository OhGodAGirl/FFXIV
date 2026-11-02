namespace CollectionShopper;

internal sealed record MarketPriceRefreshResult(
    IReadOnlyDictionary<uint, MarketPriceQuote> Prices,
    IReadOnlyCollection<uint> FailedItemIds)
{
    internal IReadOnlyDictionary<uint, MarketPriceQuote> MergePrevious(
        IReadOnlyDictionary<uint, MarketPriceQuote> previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var merged = previous.Where(pair => FailedItemIds.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var pair in Prices)
            merged[pair.Key] = pair.Value;
        return merged;
    }
}

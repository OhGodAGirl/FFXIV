namespace CollectionShopper;

/// <summary>Display and ordering rules shared by the list, individual Buy, and Buy All.</summary>
internal static class ShoppingListPolicy
{
    internal const string MissingPriceTooltip = "No Universalis Price - Check Marketboard";

    internal static string MissingPriceLabel(bool checking) => checking ? "Checking" : "Not Found";

    internal static bool IsMissing(ShoppingItem item, IReadOnlySet<uint> pending)
        => item.OwnershipKnown && !item.IsLocallyOwned && !pending.Contains(item.ItemId);

    internal static bool CanBuy(ShoppingItem item, IReadOnlySet<uint> pending, bool running)
        => !running && IsMissing(item, pending);

    internal static Summary Summarize(IEnumerable<ShoppingItem> items, ShopperPage? page,
        Func<ShoppingCategory, bool> enabled, IReadOnlySet<uint> pending, bool allowHighQuality)
    {
        var count = 0;
        var priced = 0;
        var total = 0UL;
        foreach (var item in items)
        {
            if (page is not null && (!ShopperPagePolicy.IncludesPurchaseCategory(page.Value, item.Category, enabled)
                || !IsMissing(item, pending)))
                continue;
            count++;
            if (ShoppingProjection.PreferredPrice(item, allowHighQuality) is { } price)
            {
                total += price.PricePerUnit;
                priced++;
            }
        }
        return new(count, priced, total);
    }

    internal sealed record Summary(int Count, int PricedCount, ulong Total)
    {
        internal bool ShowEstimate(bool ownershipReady) => ownershipReady && (Count == 0 || PricedCount > 0);
    }

    internal static IEnumerable<ShoppingItem> PurchaseItems(IEnumerable<ShoppingItem> items,
        ShopperPage page, Func<ShoppingCategory, bool> enabled, IReadOnlySet<uint> pending, bool allowHighQuality)
        => items.Where(item => ShopperPagePolicy.IncludesPurchaseCategory(page, item.Category, enabled))
            .Where(item => IsMissing(item, pending))
            .OrderBy(item => item.CollectionPriority)
            .ThenBy(item => ShoppingProjection.PreferredPrice(item, allowHighQuality) is null)
            .ThenBy(item => ShoppingProjection.PreferredPrice(item, allowHighQuality)?.PricePerUnit ?? uint.MaxValue)
            .ThenBy(item => item.ItemName);

    internal static bool ShowStatus(bool refreshing, bool running, int itemCount, string status)
        => refreshing || running || itemCount == 0
            || status.StartsWith("Purchased ", StringComparison.Ordinal)
            || status.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || status.Contains("cancelled", StringComparison.OrdinalIgnoreCase);
}


namespace CollectionShopper;

/// <summary>
/// Builds immutable, deterministically ordered shopping projections for the UI.
/// </summary>
internal static class ShoppingProjection
{
    internal static string EstimateLabel(bool ownershipReady, int itemCount, int pricedCount)
        => !ownershipReady || itemCount > 0 && pricedCount == 0
            ? "Estimate unavailable"
            : pricedCount < itemCount ? $"Partial ({itemCount - pricedCount:N0} unpriced)" : "Estimated";

    // Refresh estimates only for verified missing items; prices never authorize a purchase.
    internal static uint[] PriceItemIds(IEnumerable<ShoppingItem> items, IReadOnlySet<uint> completedItemIds)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(completedItemIds);
        return items.Where(item => item.ItemId != 0 && item.OwnershipKnown
                && !item.IsLocallyOwned && item.MappingValid && !completedItemIds.Contains(item.ItemId))
            .Select(item => item.ItemId).Distinct().ToArray();
    }

    internal static ShoppingOwnershipCounts CountOwnershipStates(
        IEnumerable<ShoppingItem> items,
        ShopperPage page,
        IReadOnlySet<uint> completedItemIds)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(completedItemIds);

        var missing = 0;
        var unverified = 0;
        foreach (var item in items)
        {
            if (!IncludesPage(page, item.Category)
                || item.IsLocallyOwned
                || completedItemIds.Contains(item.ItemId))
            {
                continue;
            }

            if (item.OwnershipKnown)
                missing++;
            else
                unverified++;
        }

        return new ShoppingOwnershipCounts(missing, unverified);
    }

    internal static ShoppingEmptyState ResolveEmptyState(
        bool collectionLoaded,
        bool canInferMissingItemIds,
        bool hasActiveFilter)
    {
        if (!collectionLoaded)
            return ShoppingEmptyState.NotLoaded;
        if (hasActiveFilter)
            return ShoppingEmptyState.Filtered;
        return canInferMissingItemIds
            ? ShoppingEmptyState.Complete
            : ShoppingEmptyState.OwnershipUnavailable;
    }

    internal static ShoppingItem[] BuildVisible(
        IEnumerable<ShoppingItem> items,
        ShopperPage page,
        string filter,
        bool hideLocallyOwned,
        bool hideCompleted,
        IReadOnlySet<uint> completedItemIds,
        uint currentItemId,
        bool allowHighQuality)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(completedItemIds);

        return items
            .Where(item => IncludesPage(page, item.Category))
            .Where(item => !hideLocallyOwned || !item.IsLocallyOwned)
            .Where(item => !hideCompleted || !completedItemIds.Contains(item.ItemId))
            .Where(item => item.ItemId == currentItemId
                || string.IsNullOrWhiteSpace(filter)
                || item.ItemName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || item.CollectionName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CollectionPriority)
            .ThenBy(item => PreferredPrice(item, allowHighQuality) is null)
            .ThenBy(item => PreferredPrice(item, allowHighQuality)?.PricePerUnit ?? uint.MaxValue)
            .ThenBy(item => item.ItemName, StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool IncludesPage(ShopperPage page, ShoppingCategory category)
        => page == ShopperPage.AllMissing
            || (page, category) switch
            {
                (ShopperPage.Outfits, ShoppingCategory.Outfit) => true,
                (ShopperPage.Weapons, ShoppingCategory.Weapon) => true,
                (ShopperPage.Minions, ShoppingCategory.Minion) => true,
                (ShopperPage.Mounts, ShoppingCategory.Mount) => true,
                (ShopperPage.Orchestrions, ShoppingCategory.Orchestrion) => true,
                (ShopperPage.Hairstyles, ShoppingCategory.Hairstyle) => true,
                (ShopperPage.Facewear, ShoppingCategory.Facewear) => true,
                (ShopperPage.Fashion, ShoppingCategory.Fashion) => true,
                _ => false,
            };

    internal static MarketPrice? PreferredPrice(ShoppingItem item, bool allowHighQuality)
        => item.Prices?.NormalQuality ?? (allowHighQuality ? item.Prices?.HighQuality : null);

}



namespace CollectionShopper;

internal static class PurchasePolicy
{
    public static PurchaseListingCandidate? SelectBestListing(
        IEnumerable<PurchaseListingCandidate> listings,
        uint expectedItemId,
        bool allowHighQuality)
        => listings
            .Where(listing => listing.ItemId == expectedItemId
                && listing.ListingId != 0
                && listing.UnitPrice != 0
                && listing.Quantity == 1
                && !listing.IsMannequin
                && (allowHighQuality || !listing.IsHighQuality))
            .OrderBy(listing => listing.IsHighQuality ? 1 : 0)
            .ThenBy(listing => listing.TotalCost)
            .FirstOrDefault() is { ListingId: not 0 } selected
                ? selected
                : null;

    public static bool IsWithinPerItemCap(
        PurchaseListingCandidate listing,
        int maximumPricePerItem,
        bool unlimitedPricePerItem)
        => unlimitedPricePerItem
            || maximumPricePerItem > 0 && listing.TotalCost <= (ulong)maximumPricePerItem;

    public static bool IsWithinTotalCap(
        ulong alreadySpent,
        PurchaseListingCandidate listing,
        int maximumTotalSpend,
        bool unlimitedTotalSpend)
        => unlimitedTotalSpend
            || maximumTotalSpend > 0
                && listing.TotalCost <= (ulong)maximumTotalSpend
                && alreadySpent <= (ulong)maximumTotalSpend - listing.TotalCost;

    public static IEnumerable<ShoppingItem> PrepareRun(
        IEnumerable<ShoppingItem> candidates,
        IReadOnlySet<uint> completedItemIds)
        => candidates
            .Where(item => item.OwnershipKnown)
            .Where(item => !item.IsLocallyOwned)
            .Where(item => !completedItemIds.Contains(item.ItemId))
            .DistinctBy(item => item.ItemId);

}


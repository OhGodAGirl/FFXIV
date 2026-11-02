
namespace CollectionShopper;

internal readonly record struct PurchaseListingCandidate(
    int SourceIndex,
    uint ItemId,
    ulong ListingId,
    uint UnitPrice,
    uint TotalTax,
    uint Quantity,
    bool IsHighQuality,
    bool IsMannequin)
{
    public ulong TotalCost => (ulong)UnitPrice + TotalTax;
}


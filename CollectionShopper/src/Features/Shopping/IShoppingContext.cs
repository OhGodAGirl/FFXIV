namespace CollectionShopper;

internal interface IShoppingContext
{
    ulong CurrentContentId { get; }
    uint CurrentWorldId { get; }
    ShoppingRunSettings Settings { get; }
    IReadOnlySet<uint> CompletedItemIds { get; }
    ItemOwnership ReadPurchaseReadiness(ShoppingItem item, ulong runContentId);
    bool IsStillMissing(ShoppingItem item, ulong runContentId)
        => ReadPurchaseReadiness(item, runContentId) is { Known: true, SuppressPurchase: false };
    void LogStopped(ShoppingItem item, string reason);
    void LogSearchRetry(ShoppingItem item, string reason);
    void MarkPurchased(uint itemId, ulong purchaseContentId);
    void LogRequested(ShoppingItem item, PurchaseListingCandidate listing);
    void LogConfirmed(ShoppingItem item, ulong listingId, uint quantity);
    void LogSkipped(ShoppingItem item, string reason);
    IReadOnlyList<CollectionDataOwnershipSourceState> DiagnosticSources => Array.Empty<CollectionDataOwnershipSourceState>();
}

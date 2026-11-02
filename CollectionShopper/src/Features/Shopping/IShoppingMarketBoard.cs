namespace CollectionShopper;

internal interface IShoppingMarketBoard : IDisposable
{
    event Action? Updated;
    event Action<ShoppingOfferings>? OfferingsReceived;
    event Action<ShoppingPurchase>? ItemPurchased;
    event Action? EventStreamFaulted;
    bool IsOpen { get; }
    bool IsAvailable { get; }
    bool RequestListings(uint itemId, out int requestId);
    bool TryReadListings(out uint itemId, out IReadOnlyList<PurchaseListingCandidate> listings);
    PurchaseListingCandidate? ReadListing(int index);
    bool SubmitPurchase(PurchaseListingCandidate expected);
    IReadOnlyList<DiagnosticPredicate> LastListingRequestChecks => Array.Empty<DiagnosticPredicate>();
    IReadOnlyList<DiagnosticPredicate> LastSubmissionChecks => Array.Empty<DiagnosticPredicate>();
}

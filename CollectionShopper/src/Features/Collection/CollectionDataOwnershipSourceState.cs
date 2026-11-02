namespace CollectionShopper;

internal sealed record CollectionDataOwnershipSourceState(
    string Source,
    string? Status,
    bool AssertsOwnership,
    int ItemCount,
    string? ObservedAtUtc,
    string? Reason);

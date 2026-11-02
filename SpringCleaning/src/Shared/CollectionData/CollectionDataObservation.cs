namespace SpringCleaning.Shared.CollectionData;

internal readonly record struct CollectionDataObservation(
    bool ClearTransport,
    CollectionDataRefreshRequest? RefreshRequest);

namespace SpringCleaning.Shared.CollectionData;

internal static class CollectionDataStatusPolicy
{
    internal static CollectionDataStatusSnapshot Create(
        CollectionDataSnapshot? latest,
        CollectionDataFailure lastFailure,
        bool refreshRequested,
        bool refreshInProgress)
    {
        return latest is null
            ? new CollectionDataStatusSnapshot(
                lastFailure,
                refreshRequested,
                refreshInProgress,
                null,
                null,
                null,
                null,
                CollectionDataSourceDiagnostic.Unavailable,
                CollectionDataSourceDiagnostic.Unavailable,
                CollectionDataSourceDiagnostic.Unavailable,
                CollectionDataSourceDiagnostic.Unavailable,
                CollectionDataSourceDiagnostic.Unavailable,
                CollectionDataSourceDiagnostic.Unavailable,
                CollectionDataSourceDiagnostic.Unavailable)
            : new CollectionDataStatusSnapshot(
                lastFailure,
                refreshRequested,
                refreshInProgress,
                latest.ProviderInternalName,
                latest.ProviderVersion,
                latest.Revision,
                latest.CapturedAtUtc,
                Create(latest.Inventory),
                Create(latest.ArmoryChest),
                Create(latest.ChocoboSaddlebag),
                Create(latest.PremiumSaddlebag),
                Create(latest.Retainers),
                Create(latest.Armoire),
                Create(latest.GlamourDresser));
    }

    private static CollectionDataSourceDiagnostic Create(CollectionSourceSnapshot source) => new(
        source.Status,
        source.ItemIds.Length,
        source.ObservedAtUtc,
        source.Reason);
}

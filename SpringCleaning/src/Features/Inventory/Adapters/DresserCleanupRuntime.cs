using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed class DresserCleanupRuntime(DresserScanner scanner, NativeUi nativeUi,
    CollectionDataRefreshController collectionData, Configuration configuration) : IDresserCleanupRuntime
{
    private DresserAlertEntry? pending;
    private CollectionDataSnapshot? scope;
    private IReadOnlyList<InventoryDuplicateItem> before = [];
    private uint[] expected = [];

    private bool Retained(CollectionDataSnapshot snapshot, IEnumerable<uint> ids) =>
        scanner.IsCabinetLoaded && ids.All(id =>
            CollectionOwnershipPolicy.GetFact(snapshot, CollectionSourceName.Armoire, id) == CollectionOwnershipFact.Owned
            && scanner.GetCabinetRowId(id) != 0 && scanner.IsInCabinet(scanner.GetCabinetRowId(id)));

    public bool CanRestore(DresserAlertEntry entry)
    {
        var snapshot = collectionData.LatestSnapshot;
        var ids = DresserRestorationPolicy.Components(entry);
        return !configuration.IgnoredDuplicateItemIds.Contains(entry.ItemId)
            && !ids.Any(configuration.IgnoredDuplicateItemIds.Contains)
            && nativeUi.IsDresserOpen && scanner.AreRestorationBagsLoaded()
            && !nativeUi.IsExistingOutfitMergePromptOpen && !nativeUi.IsInventoryContextOpen
            && !nativeUi.IsOutfitCreationOpen && !nativeUi.IsOutfitConfirmationOpen
            && !nativeUi.IsDresserItemConfirmationOpen
            && snapshot is not null && DresserRestorationPolicy.CanPlan(snapshot, entry)
            && (scope is null || (scope.ContentId == snapshot.ContentId
                && scope.ProviderSessionId == snapshot.ProviderSessionId))
            && ids.Length > 0 && Retained(snapshot, ids)
            && scanner.GetEmptyInventorySlots() >= ids.Length;
    }

    public bool BeginRestore(DresserAlertEntry entry)
    {
        var snapshot = collectionData.LatestSnapshot;
        if (snapshot is null || !CanRestore(entry)
            || !ReferenceEquals(snapshot, collectionData.LatestSnapshot))
            return false;
        scope ??= snapshot;
        expected = DresserRestorationPolicy.Components(entry);
        before = Capture(scope);
        if (!scanner.TryRestore(entry))
        {
            Reset();
            return false;
        }
        pending = entry;
        return true;
    }

    public RestoreObservation ObserveRestore()
    {
        var snapshot = collectionData.LatestSnapshot;
        if (pending is null || scope is null)
            return new(false, [], "Restoration is no longer active, kupo!");
        if (snapshot is null)
            return RestoreObservation.Pending;
        if (snapshot.ContentId != scope.ContentId || snapshot.ProviderSessionId != scope.ProviderSessionId)
            return new(false, [], "Character or provider changed. Cleanup stopped, kupo!");
        if (!Retained(snapshot, expected))
            return new(false, [], "Retained appearances could not be verified. Cleanup stopped, kupo!");
        if (!scanner.AreRestorationBagsLoaded() || !scanner.IsRestorationComplete(pending))
            return RestoreObservation.Pending;
        var result = RestoredCopyPolicy.Observe(before, Capture(snapshot), expected);
        if (!result.Waiting && result.Failure is null && result.Copies.Any(copy =>
                !CollectionOwnershipPolicy.CanContinueDuplicateRemoval(snapshot, copy)))
            return RestoreObservation.Pending;
        return result;
    }

    private IReadOnlyList<InventoryDuplicateItem> Capture(CollectionDataSnapshot snapshot) =>
        scanner.CaptureRestorationCopies(snapshot, expected.ToHashSet(),
            configuration.ProtectItemsWithMateria, configuration.ProtectItemsWithSpiritbond);

    public void Reset()
    {
        pending = null;
        scope = null;
        before = [];
        expected = [];
    }
}

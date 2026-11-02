namespace SpringCleaning.Shared.CollectionData;

/// <summary>
/// Converts Exportaramus source facts into Spring Cleaning planning decisions.
/// Missing is trustworthy only for a fresh, complete source. A stale positive remains
/// protective evidence, while stale absence and unavailable data remain unknown.
/// </summary>
internal static class CollectionOwnershipPolicy
{
    internal static string ReadinessMessage(CollectionDataSnapshot? snapshot, params CollectionSourceName[] sources)
    {
        if (snapshot is null)
            return "Waiting for an Exportaramus snapshot for this character.";
        foreach (var name in sources)
        {
            var source = snapshot.GetSource(name);
            var label = name switch
            {
                CollectionSourceName.GlamourDresser => "Glamour Dresser",
                CollectionSourceName.ArmoryChest => "Armoury Chest",
                _ => name.ToString(),
            };
            if (source.Status != CollectionSourceStatus.Fresh)
                return $"Exportaramus: {label} is {source.Status}."
                    + (string.IsNullOrWhiteSpace(source.Reason) ? string.Empty : $" {source.Reason}");
            if (name == CollectionSourceName.GlamourDresser && !snapshot.DresserEntries.HasValue)
                return "Exportaramus has no Glamour Dresser entry details for this snapshot.";
        }
        return "Exportaramus ownership data is ready.";
    }

    internal static bool AreSourcesFresh(
        CollectionDataSnapshot? snapshot,
        params CollectionSourceName[] sources) =>
        snapshot is not null
        && sources.Length > 0
        && sources.All(source => snapshot.GetSource(source).Status == CollectionSourceStatus.Fresh);

    internal static CollectionOwnershipFact GetFact(
        CollectionDataSnapshot? snapshot,
        CollectionSourceName sourceName,
        uint itemId)
    {
        if (snapshot is null || itemId == 0)
            return CollectionOwnershipFact.Unknown;

        var source = snapshot.GetSource(sourceName);
        if (source.Status is CollectionSourceStatus.Unavailable or CollectionSourceStatus.NeverObserved)
            return CollectionOwnershipFact.Unknown;

        if (source.ItemIds.AsSpan().BinarySearch(itemId) >= 0)
            return CollectionOwnershipFact.Owned;

        return source.Status == CollectionSourceStatus.Fresh
            ? CollectionOwnershipFact.Missing
            : CollectionOwnershipFact.Unknown;
    }

    internal static bool CanPlanTransfer(
        CollectionDataSnapshot? snapshot,
        CollectionSourceName source,
        CollectionSourceName destination,
        uint itemId) =>
        GetFact(snapshot, source, itemId) == CollectionOwnershipFact.Owned
        && snapshot!.GetSource(source).Status == CollectionSourceStatus.Fresh
        && GetFact(snapshot, destination, itemId) == CollectionOwnershipFact.Missing;

    internal static bool CanPlanDresserStorage(
        CollectionDataSnapshot? snapshot,
        CollectionSourceName source,
        uint itemId,
        bool isArmoireEligible) =>
        CanPlanTransfer(snapshot, source, CollectionSourceName.GlamourDresser, itemId)
        && (!isArmoireEligible
            || GetFact(snapshot, CollectionSourceName.Armoire, itemId) == CollectionOwnershipFact.Missing);

    internal static bool CanPlanInventoryReview(
        CollectionDataSnapshot? snapshot,
        uint itemId,
        CollectionSourceName source = CollectionSourceName.Inventory) =>
        source is CollectionSourceName.Inventory or CollectionSourceName.ArmoryChest
        && GetFact(snapshot, source, itemId) == CollectionOwnershipFact.Owned
        && snapshot!.GetSource(source).Status == CollectionSourceStatus.Fresh
        && (GetFact(snapshot, CollectionSourceName.GlamourDresser, itemId) == CollectionOwnershipFact.Owned
            || GetFact(snapshot, CollectionSourceName.Armoire, itemId) == CollectionOwnershipFact.Owned);

    // A deliberately closed Dresser becomes retained. An already authorized copy
    // may finish only in the same provider session, with positive provider evidence
    // and immediate local slot, protection, and stored-appearance revalidation.
    internal static bool CanContinueDuplicateRemoval(CollectionDataSnapshot? snapshot,
        InventoryDuplicateItem candidate) =>
        HasCandidateScope(snapshot, candidate)
        && CanPlanInventoryReview(snapshot, candidate.ItemId, candidate.Source);

    internal static bool HasCandidateScope(CollectionDataSnapshot? snapshot, InventoryDuplicateItem candidate) =>
        snapshot is not null
        && candidate.ProviderContentId == snapshot.ContentId
        && candidate.ProviderSessionId == snapshot.ProviderSessionId;

    internal static bool CanOpenDresserFallback(CollectionDataSnapshot? snapshot, InventoryDuplicateItem candidate) =>
        HasCandidateScope(snapshot, candidate)
        && candidate.Disposition == InventoryDuplicateDisposition.DresserFallback
        && CanPlanTransfer(snapshot, CollectionSourceName.Inventory, CollectionSourceName.GlamourDresser, candidate.ItemId);

}

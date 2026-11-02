namespace CollectionShopper;

internal static class PurchaseReadinessPolicy
{
    internal static bool CanContinue(ShoppingItem item, ulong activeContentId, ulong runContentId,
        CollectionDataOwnershipState ownership, IReadOnlySet<uint> completedItemIds)
        => Evaluate(item, activeContentId, runContentId, ownership, completedItemIds)
            is { Known: true, SuppressPurchase: false };

    internal static ItemOwnership Evaluate(ShoppingItem item, ulong activeContentId, ulong runContentId,
        CollectionDataOwnershipState ownership, IReadOnlySet<uint> completedItemIds)
    {
        if (activeContentId == 0 || activeContentId != runContentId)
            return new(false, false, false, "Character identity changed or is unavailable.");
        if (ownership.Snapshot is null)
            return new(false, false, false, ownership.StatusDetail);
        if (ownership.Snapshot.ContentId != activeContentId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            return new(false, false, false, "Ownership snapshot belongs to another character.");
        if (completedItemIds.Contains(item.ItemId))
            return new(true, true, false, "Purchase confirmed; waiting for Exportaramus to observe the item.");
        return ownership.Classify(item.Category, item.ItemId, item.CollectionId, item.MappingValid);
    }
}

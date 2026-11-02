namespace SpringCleaning;

internal static class RestoredCopyPolicy
{
    internal static RestoreObservation Observe(IReadOnlyList<InventoryDuplicateItem> before,
        IReadOnlyList<InventoryDuplicateItem> after, IReadOnlyList<uint> expected)
    {
        static bool Same(InventoryDuplicateItem a, InventoryDuplicateItem b) =>
            a.InventoryTypeId == b.InventoryTypeId && a.SlotIndex == b.SlotIndex
            && a.ItemId == b.ItemId && a.IsHighQuality == b.IsHighQuality;
        if (before.Any(a => !after.Any(b => Same(a, b))))
            return new(false, [], "Inventory changed during restoration. Cleanup stopped, kupo!");
        var added = after.Where(b => !before.Any(a => Same(a, b))).ToArray();
        if (added.Length < expected.Count)
            return RestoreObservation.Pending;
        if (!added.Select(a => a.ItemId).Order().SequenceEqual(expected.Order())
            || added.Any(a => !InventoryDuplicatePlanner.CanBulkDiscard(a)))
            return new(false, [], "Restored copies need review and remain in Inventory, kupo!");
        return new(false, added, null);
    }
}

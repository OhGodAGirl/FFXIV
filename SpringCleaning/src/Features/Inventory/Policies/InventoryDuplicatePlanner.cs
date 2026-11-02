namespace SpringCleaning;

internal static class InventoryDuplicatePlanner
{
    public static InventoryDuplicateDisposition GetDisposition(
        bool isInArmoire,
        bool isInDresser)
    {
        if (isInArmoire || isInDresser)
            return InventoryDuplicateDisposition.RemovalReview;

        return InventoryDuplicateDisposition.None;
    }

    public static bool CanBulkDiscard(InventoryDuplicateItem candidate) =>
        candidate.Disposition == InventoryDuplicateDisposition.RemovalReview
        && candidate.Safety.IsPresent
        && candidate.Safety.IsSafe;

    public static InventoryDuplicateItem[] SelectBulkDiscardCandidates(
        IEnumerable<InventoryDuplicateItem> candidates) =>
        candidates.Where(CanBulkDiscard).ToArray();
}

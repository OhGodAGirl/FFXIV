namespace SpringCleaning;

internal static class ReviewPlanner
{
    public static ReviewPlan Plan(
        IEnumerable<InventoryDuplicateItem> inventoryDuplicates,
        IEnumerable<DresserAlertEntry> dresserEntries,
        bool onlyItemsOutsideOutfits)
    {
        ArgumentNullException.ThrowIfNull(inventoryDuplicates);
        ArgumentNullException.ThrowIfNull(dresserEntries);

        if (onlyItemsOutsideOutfits)
        {
            return new ReviewPlan(
                Array.Empty<InventoryDuplicateItem>(),
                dresserEntries.Where(entry => !entry.BelongsToOutfit).ToArray());
        }

        return new ReviewPlan(
            inventoryDuplicates.ToArray(),
            dresserEntries.Where(IsDefaultReviewEntry).ToArray());
    }

    private static bool IsDefaultReviewEntry(DresserAlertEntry entry) =>
        entry.Kind is DresserAlertKind.Duplicate or DresserAlertKind.DuplicateInExistingOutfit
        || entry.Kind == DresserAlertKind.ArmoireEligible && entry.IsAlreadyInArmoire;
}

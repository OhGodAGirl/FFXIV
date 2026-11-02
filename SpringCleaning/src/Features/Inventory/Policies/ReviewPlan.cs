namespace SpringCleaning;

internal sealed record ReviewPlan(
    IReadOnlyList<InventoryDuplicateItem> InventoryDuplicates,
    IReadOnlyList<DresserAlertEntry> DresserEntries)
{
    public int Count => InventoryDuplicates.Count + DresserEntries.Count;
}

namespace SpringCleaning;

internal sealed class DuplicateReviewCache
{
    private IReadOnlyList<InventoryDuplicateItem>? inventory;
    private IReadOnlyList<DresserAlertEntry>? dresser;
    private HashSet<uint> ignored = [];
    private ReviewPlan? all;
    private ReviewPlan? visible;

    // Preview publishers replace their snapshots; ignored IDs can change in place.
    // Retain one presentation only. Callers must still revalidate every command.
    internal (ReviewPlan All, ReviewPlan Visible) Get(
        IReadOnlyList<InventoryDuplicateItem> inventoryDuplicates,
        IReadOnlyList<DresserAlertEntry> dresserEntries,
        HashSet<uint> ignoredItemIds)
    {
        ArgumentNullException.ThrowIfNull(inventoryDuplicates);
        ArgumentNullException.ThrowIfNull(dresserEntries);
        ArgumentNullException.ThrowIfNull(ignoredItemIds);
        if (!ReferenceEquals(inventory, inventoryDuplicates)
            || !ReferenceEquals(dresser, dresserEntries)
            || !ignored.SetEquals(ignoredItemIds))
        {
            all = ReviewPlanner.Plan(inventoryDuplicates, dresserEntries, false);
            visible = Filter(all, ignoredItemIds);
            inventory = inventoryDuplicates;
            dresser = dresserEntries;
            ignored = new HashSet<uint>(ignoredItemIds);
        }
        return (all!, visible!);
    }

    private static ReviewPlan Filter(ReviewPlan plan, HashSet<uint> ignoredItemIds) => new(
        plan.InventoryDuplicates.Where(copy => !ignoredItemIds.Contains(copy.ItemId)).ToArray(),
        plan.DresserEntries.Where(entry => !IsIgnored(entry, ignoredItemIds)).ToArray());

    internal static bool IsIgnored(DresserAlertEntry entry, HashSet<uint> ignoredItemIds)
    {
        if (ignoredItemIds.Contains(entry.ItemId))
            return true;
        if (entry.IsOutfit)
            foreach (var component in entry.Components)
                if (ignoredItemIds.Contains(component.ItemId))
                    return true;
        return false;
    }

    internal void Clear()
    {
        inventory = null;
        dresser = null;
        ignored = [];
        all = null;
        visible = null;
    }
}

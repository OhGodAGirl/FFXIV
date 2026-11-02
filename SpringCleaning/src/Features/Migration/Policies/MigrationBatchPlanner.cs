namespace SpringCleaning;

internal static class MigrationBatchPlanner
{
    public static int GetBatchPieceCount(
        IEnumerable<EligibleDresserItem> eligible,
        int availableSlots)
    {
        var selectedPieces = 0;
        foreach (var item in eligible)
        {
            if (selectedPieces + item.InventorySlotsRequired > availableSlots)
            {
                continue;
            }

            selectedPieces += item.InventorySlotsRequired;
        }

        return selectedPieces;
    }

    public static EligibleDresserItem? FindNextItem(
        IEnumerable<EligibleDresserItem> eligible,
        int availableSlots)
    {
        return eligible.FirstOrDefault(item =>
            item.InventorySlotsRequired > 0
            && item.InventorySlotsRequired <= availableSlots);
    }
}

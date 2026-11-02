namespace SpringCleaning;

internal static class DresserPrismBudgetPlanner
{
    public static DresserPrismBudgetPlan Plan(
        IReadOnlyList<InventoryDresserOutfit> outfits,
        IReadOnlyList<InventoryDresserItem> items,
        uint availablePrisms)
    {
        var remaining = (long)availablePrisms;
        var selectedOutfits = new List<InventoryDresserOutfit>();
        var selectedItems = new List<InventoryDresserItem>();
        var selectedPieces = 0;
        var totalPieces = outfits.Sum(outfit => outfit.Components.Count) + items.Count;

        foreach (var outfit in outfits)
        {
            var cost = outfit.Components.Count;
            if (cost <= 0 || cost > remaining)
                continue;

            selectedOutfits.Add(outfit);
            selectedPieces += cost;
            remaining -= cost;
        }

        foreach (var item in items)
        {
            if (remaining <= 0)
                break;

            selectedItems.Add(item);
            selectedPieces++;
            remaining--;
        }

        return new DresserPrismBudgetPlan(
            selectedOutfits,
            selectedItems,
            selectedPieces,
            totalPieces - selectedPieces);
    }
}

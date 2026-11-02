namespace SpringCleaning;

internal sealed record DresserPrismBudgetPlan(
    IReadOnlyList<InventoryDresserOutfit> Outfits,
    IReadOnlyList<InventoryDresserItem> Items,
    int SelectedPieces,
    int SkippedPieces);

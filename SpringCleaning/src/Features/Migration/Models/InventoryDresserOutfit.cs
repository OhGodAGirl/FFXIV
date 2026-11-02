namespace SpringCleaning;

internal sealed record InventoryDresserOutfit(
    uint SetItemId,
    int? ExistingPrismBoxIndex,
    string Name,
    IReadOnlyList<InventoryDresserComponent> Components);

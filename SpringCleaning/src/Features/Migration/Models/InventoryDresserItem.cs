namespace SpringCleaning;

internal sealed record InventoryDresserItem(
    int InventoryTypeId,
    int SlotIndex,
    uint ItemId,
    bool IsHighQuality,
    string Name,
    uint IconId,
    int SourceCategoryIndex = 0);

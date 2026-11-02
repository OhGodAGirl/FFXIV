namespace SpringCleaning;

internal sealed record InventoryDresserComponent(
    int InventoryTypeId,
    int SlotIndex,
    int OutfitSlot,
    uint ItemId,
    bool IsHighQuality,
    string Name,
    uint IconId,
    int SourceCategoryIndex = 0);

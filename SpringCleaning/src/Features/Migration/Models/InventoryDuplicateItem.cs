namespace SpringCleaning;

internal sealed record InventoryDuplicateItem(
    int InventoryTypeId,
    int SlotIndex,
    uint ItemId,
    bool IsHighQuality,
    string Name,
    uint IconId,
    InventoryItemSafety Safety,
    bool IsInArmoire,
    bool IsInDresser,
    InventoryDuplicateDisposition Disposition)
{
    internal Shared.CollectionData.CollectionSourceName Source { get; init; } = Shared.CollectionData.CollectionSourceName.Inventory;

    internal string ProviderSessionId { get; init; } = string.Empty;

    internal ulong ProviderContentId { get; init; }
}

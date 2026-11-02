namespace SpringCleaning;

internal sealed record CabinetInventoryItem(
    uint CabinetRowId,
    uint ItemId,
    bool IsHighQuality,
    string Name,
    uint IconId);

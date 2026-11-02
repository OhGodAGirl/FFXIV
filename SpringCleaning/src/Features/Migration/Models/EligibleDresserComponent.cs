namespace SpringCleaning;

internal sealed record EligibleDresserComponent(
    int OutfitSlot,
    uint ItemId,
    bool IsHighQuality,
    uint CabinetRowId,
    string Name);

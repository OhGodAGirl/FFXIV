namespace SpringCleaning;

internal sealed record InventoryDresserCandidate(
    InventoryDresserComponent Component,
    IReadOnlyList<uint> OutfitSetIds);

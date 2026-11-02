namespace SpringCleaning;

internal sealed record PendingDresserOutfit(
    InventoryDresserOutfit Outfit,
    IReadOnlyDictionary<uint, uint> InventoryCountsBefore,
    DateTime Deadline);

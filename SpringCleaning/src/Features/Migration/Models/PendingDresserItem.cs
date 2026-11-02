namespace SpringCleaning;

internal sealed record PendingDresserItem(
    InventoryDresserItem Item,
    uint InventoryCountBefore,
    DateTime Deadline);

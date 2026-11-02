namespace SpringCleaning;

internal sealed record PendingDeposit(
    CabinetInventoryItem Item,
    uint InventoryCountBefore,
    DateTime Deadline);

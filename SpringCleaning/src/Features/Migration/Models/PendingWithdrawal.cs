namespace SpringCleaning;

internal sealed record PendingWithdrawal(
    EligibleDresserItem Item,
    IReadOnlyDictionary<uint, uint> InventoryCountsBefore,
    uint EmptySlotsBefore,
    DateTime Deadline);

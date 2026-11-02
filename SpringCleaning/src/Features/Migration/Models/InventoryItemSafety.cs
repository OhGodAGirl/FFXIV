namespace SpringCleaning;

internal sealed record InventoryItemSafety(
    bool IsPresent,
    bool IsSafe,
    string Status);

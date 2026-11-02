namespace SpringCleaning.Shared.Inventory;

internal sealed record InventoryLocation(string Kind, string RetainerId, string State, string? ObservedAt, bool SupportsQuantity, InventoryQuantity[] Items);

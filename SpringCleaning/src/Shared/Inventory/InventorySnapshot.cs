namespace SpringCleaning.Shared.Inventory;

internal sealed record InventorySnapshot(string Character, ulong Revision, bool RosterKnown, InventoryLocation[] Locations, string Hash);

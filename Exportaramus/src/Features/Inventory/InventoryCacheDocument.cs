namespace Exportaramus.Features.Inventory;

public sealed record InventoryCacheDocument(string SchemaVersion, InventorySnapshot Snapshot);

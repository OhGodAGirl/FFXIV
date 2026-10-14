namespace Exportaramus.Features.Inventory;

public interface IInventoryCache
{
    Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken);
    Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken);
}

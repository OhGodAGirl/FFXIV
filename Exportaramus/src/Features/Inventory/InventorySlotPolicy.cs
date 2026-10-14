namespace Exportaramus.Features.Inventory;

/// <summary>Converts a validated non-empty native slot without treating invalid zero stacks as empty.</summary>
public static class InventorySlotPolicy
{
    public static bool TryCreate(uint itemId, uint quantity, bool highQuality, bool crystalSlot, out InventoryItemQuantity? item)
    {
        item = null;
        if (quantity == 0)
        {
            return crystalSlot;
        }

        item = new(itemId, highQuality ? 0 : quantity, highQuality ? quantity : 0);
        return true;
    }
}

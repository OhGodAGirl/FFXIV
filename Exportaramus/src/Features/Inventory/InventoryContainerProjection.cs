using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.Inventory;

public static class InventoryContainerProjection
{
    public static List<InventoryItemQuantity>? Quantities(IEnumerable<InventorySlotObservation> slots, int crystals, int retainerCrystals)
    {
        var items = new List<InventoryItemQuantity>();
        foreach (var slot in slots)
        {
            if (!InventorySlotPolicy.TryCreate(slot.ItemId, slot.Quantity, slot.HighQuality,
                slot.Container == crystals || slot.Container == retainerCrystals, out var item)) return null;
            if (item is not null) items.Add(item);
        }
        return InventoryPolicy.TryNormalize(items, out var normalized) ? normalized.ToList() : null;
    }
    public static IEnumerable<PresenceItem> Presence(IEnumerable<InventorySlotObservation> slots, bool instances) =>
        slots.Select(slot => new PresenceItem(slot.ItemId, instances ? 1 : slot.Quantity));
}

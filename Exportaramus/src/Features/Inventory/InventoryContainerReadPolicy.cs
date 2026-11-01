namespace Exportaramus.Features.Inventory;

public readonly record struct InventoryContainerObservation(nint Address, int Size, bool Loaded);
public readonly record struct InventorySlotObservation(int Container, int Slot, uint ItemId, uint Quantity, bool HighQuality);

/// <summary>Return copied values, not native pointers.</summary>
public interface IInventoryContainerSource
{
    bool IsCurrent
    {
        get;
    }
    InventoryContainerObservation Container(int type);
    InventorySlotObservation? Slot(int type, int slot);
    bool IsKnownItem(uint itemId);
}

public static class InventoryContainerReadPolicy
{
    public static List<InventorySlotObservation>? Read(IInventoryContainerSource source, IReadOnlyList<int> types)
    {
        if (!source.IsCurrent) return null;
        var containers = new InventoryContainerObservation[types.Count];
        var items = new List<InventorySlotObservation>();
        for (var i = 0; i < types.Count; i++)
        {
            var container = source.Container(types[i]);
            if (container.Address == 0 || !container.Loaded || container.Size is <= 0 or > 1000) return null;
            containers[i] = container;
            for (var slotIndex = 0; slotIndex < container.Size; slotIndex++)
            {
                var slot = source.Slot(types[i], slotIndex);
                if (slot is not { } value || value.Container != types[i] || value.Slot != slotIndex) return null;
                if (value.ItemId == 0) continue;
                if (!source.IsKnownItem(value.ItemId)) return null;
                items.Add(value);
            }
        }
        if (!source.IsCurrent) return null;
        // Recheck every page after the read; a later page may invalidate an earlier one.
        for (var i = 0; i < types.Count; i++)
            if (source.Container(types[i]) != containers[i]) return null;
        return items;
    }
}

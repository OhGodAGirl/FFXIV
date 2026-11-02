namespace EZDelivery.Features.Shopping;

/// <summary>Value-only checks used before native ring indexing and inventory acceptance.</summary>
internal static class NativeShopPolicy
{
    internal static bool TryConditionIndex(ulong capacity, ulong index, out ulong slot)
    {
        slot = 0;
        if (capacity == 0 || capacity > 4096 || (capacity & (capacity - 1)) != 0) return false;
        slot = (capacity - 1) & index;
        return true;
    }

    internal static bool SlotAccepts(uint slotItem, long quantity, uint requestedItem, uint stackSize)
        => slotItem == 0 || (slotItem == requestedItem && quantity < stackSize);

    internal static bool ContainerSizeValid(int size) => size is >= 0 and <= 500;
    internal static int BoundedCount(long count) => count <= int.MaxValue ? (int)count : -1;
}

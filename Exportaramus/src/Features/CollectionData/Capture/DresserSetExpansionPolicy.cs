using System.Collections.Immutable;

namespace Exportaramus.Features.CollectionData.Capture;

public static class DresserSetExpansionPolicy
{
    // Prism-box IDs are encoded integers, not game-owned InventoryItem objects.
    public static uint NormalizeStoredId(uint id) => id % 1_000_000;

    public static ImmutableArray<uint> Expand(ImmutableArray<uint> slotItemIds, ImmutableArray<bool> unlockedSlots)
    {
        if (slotItemIds.IsDefault || unlockedSlots.IsDefault || slotItemIds.Length != 11 || unlockedSlots.Length != 11)
            throw new ArgumentException("A Dresser set requires the exact eleven-slot mapping.");
        return slotItemIds.Where((id, index) => id != 0 && unlockedSlots[index]).Distinct().Order().ToImmutableArray();
    }
}

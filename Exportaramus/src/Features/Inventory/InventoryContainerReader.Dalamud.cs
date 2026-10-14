using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Exportaramus.Features.Inventory;

// Copies a complete location in one framework callback. No native pointer escapes the read.
internal sealed unsafe class InventoryContainerReader(IDataManager data)
{
    public List<InventoryItemQuantity>? Read(InventoryType[] types)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return null;
        var pointers = new nint[types.Length];
        var sizes = new int[types.Length];
        var items = new List<InventoryItemQuantity>();
        var sheet = data.GetExcelSheet<Item>();
        for (var containerIndex = 0; containerIndex < types.Length; containerIndex++)
        {
            var container = manager->GetInventoryContainer(types[containerIndex]);
            if (container == null || !container->IsLoaded || container->Size is <= 0 or > 1000) return null;
            pointers[containerIndex] = (nint)container;
            sizes[containerIndex] = container->Size;
            for (var slotIndex = 0; slotIndex < sizes[containerIndex]; slotIndex++)
            {
                var slot = container->GetInventorySlot(slotIndex);
                if (slot == null || *(nint*)slot == 0 || slot->Container != types[containerIndex] || slot->Slot != slotIndex) return null;
                var itemId = slot->GetBaseItemId();
                if (itemId == 0) continue;
                if (!sheet.HasRow(itemId)) return null;
                var quantity = slot->GetQuantity();
                var highQuality = quantity != 0 && (slot->GetFlags() & InventoryItem.ItemFlags.HighQuality) != 0;
                if (!InventorySlotPolicy.TryCreate(itemId, quantity, highQuality,
                    types[containerIndex] is InventoryType.Crystals or InventoryType.RetainerCrystals, out var item)) return null;
                if (item is not null) items.Add(item);
            }
        }
        if (manager != InventoryManager.Instance()) return null;
        // Recheck every container after the entire group; later pages must not invalidate earlier pages.
        for (var i = 0; i < types.Length; i++)
        {
            var container = manager->GetInventoryContainer(types[i]);
            if ((nint)container != pointers[i] || !container->IsLoaded || container->Size != sizes[i]) return null;
        }
        return InventoryPolicy.TryNormalize(items, out var normalized) ? normalized.ToList() : null;
    }
}

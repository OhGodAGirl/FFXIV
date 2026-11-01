using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Exportaramus.Features.Inventory;

// Use only within one framework callback. Do not retain native slots.
internal sealed unsafe class NativeInventoryContainerSource(IDataManager data) : IInventoryContainerSource
{
    private readonly InventoryManager* manager = InventoryManager.Instance();
    public bool IsCurrent => manager != null && manager == InventoryManager.Instance();
    public InventoryContainerObservation Container(int type)
    {
        if (!IsCurrent) return default;
        var container = manager->GetInventoryContainer((InventoryType)type);
        return container == null ? default : new((nint)container, container->Size, container->IsLoaded);
    }
    public InventorySlotObservation? Slot(int type, int index)
    {
        if (!IsCurrent) return null;
        var container = manager->GetInventoryContainer((InventoryType)type);
        if (container == null || !container->IsLoaded || index >= container->Size) return null;
        var slot = container->GetInventorySlot(index);
        if (slot == null || *(nint*)slot == 0 || (int)slot->Container != type || slot->Slot != index) return null;
        return new((int)slot->Container, slot->Slot, slot->GetBaseItemId(), slot->GetQuantity(),
            (slot->GetFlags() & InventoryItem.ItemFlags.HighQuality) != 0);
    }
    public bool IsKnownItem(uint itemId) => data.GetExcelSheet<Item>().HasRow(itemId);
}

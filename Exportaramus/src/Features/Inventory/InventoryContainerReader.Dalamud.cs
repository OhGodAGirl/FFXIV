using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Exportaramus.Features.Inventory;

internal sealed class InventoryContainerReader(IDataManager data)
{
    public List<InventoryItemQuantity>? Read(InventoryType[] types)
    {
        var slots = InventoryContainerReadPolicy.Read(new NativeInventoryContainerSource(data), types.Select(type => (int)type).ToArray());
        return slots is null ? null : InventoryContainerProjection.Quantities(slots,
            (int)InventoryType.Crystals, (int)InventoryType.RetainerCrystals);
    }
}

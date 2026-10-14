namespace Exportaramus.Features.Inventory;

public readonly record struct InventoryLocationKey(InventoryLocationKind Kind, ulong RetainerId = 0)
{
    public bool IsValid => Enum.IsDefined(Kind) && (Kind == InventoryLocationKind.Retainer ? RetainerId != 0 : RetainerId == 0);
}

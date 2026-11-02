using BetterGlamorPlates.Features.Diagnostics;

namespace BetterGlamorPlates.Features.GlamourPlates;

internal sealed record NativeDyeSupplyState(bool Loaded, int Size, uint ItemId, long Quantity);
internal sealed record NativeDyeReadback(byte PendingDye, bool FromInventory, bool FromItemId,
    uint ItemId, uint Container, ushort InventorySlot);

internal interface IPlateDyePort
{
    bool ContextMatches();
    uint ReadItemId(PlateSlot slot);
    NativeDyeSupplyState? ReadSupply(DyeSupply supply);
    void Apply(PlateDyeChange change);
    NativeDyeReadback ReadDye(PlateDyeChange change);
}

internal static class PlateDyeExecution
{
    internal static bool Apply(PlateLoadStep step, PlateDyePlan plan, IPlateDyePort port, DiagnosticJournal diagnostics)
    {
        foreach (var change in plan.Changes)
        {
            if (change.Slot != step.Slot) continue;
            if (!port.ContextMatches()) return false;
            if (port.ReadItemId(step.Slot) != step.SavedItem!.ItemId) return false;
            var supply = change.Supply;
            var observedSupply = port.ReadSupply(supply);
            if (observedSupply is null || !observedSupply.Loaded || supply.InventorySlot >= observedSupply.Size
                || observedSupply.ItemId != supply.ItemId || observedSupply.Quantity < supply.Quantity) return false;
            port.Apply(change);
            if (!port.ContextMatches()) return false;
            var observed = port.ReadDye(change);
            var matches = PlateDyePlanner.Matches(change, observed.PendingDye, observed.FromInventory,
                observed.FromItemId, observed.ItemId, observed.Container, observed.InventorySlot);
            diagnostics.Check(change.Channel == 0 ? DiagnosticKey.PendingDye0 : DiagnosticKey.PendingDye1,
                change.DyeId, observed.PendingDye, (int)step.Slot);
            if (!diagnostics.Check(DiagnosticKey.DyeAllocation, 1, matches ? 1 : 0, (int)step.Slot)) return false;
        }
        return true;
    }
}

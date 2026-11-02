using BetterGlamorPlates.Features.Diagnostics;

namespace BetterGlamorPlates.Features.GlamourPlates;

internal sealed record NativeCaptureItem(uint ItemId, byte Dye0, byte Dye1, byte Pending0, byte Pending1,
    bool Inventory0, bool Inventory1, bool Item0, bool Item1)
{
    internal bool HasPendingDye => Inventory0 || Inventory1 || Item0 || Item1 || Pending0 != 0 || Pending1 != 0;
}

internal static class PlateCaptureExecution
{
    internal static PlateCaptureResult Capture(string name, DateTime capturedAtUtc, Guid plateId,
        Func<bool> observeReady, Func<int, NativeCaptureItem> read, DiagnosticJournal diagnostics, Func<DateTimeOffset> now)
    {
        diagnostics.Begin(DiagnosticPhase.Capture, now());
        if (!observeReady())
        {
            diagnostics.Finish(DiagnosticStop.Rejected, now());
            return new(PlateCaptureOutcome.EditorNotReady, null);
        }
        var captured = PlateLibrary.Create(name, capturedAtUtc, plateId);
        for (var index = 0; index < captured.Items.Length; index++)
        {
            var item = read(index);
            if (item.ItemId == 0) continue;
            // A zero pending stain may mean removal; its allocation flags are independently significant.
            if (item.HasPendingDye)
            {
                diagnostics.Check(DiagnosticKey.PendingDye0, 0, item.Pending0, index);
                diagnostics.Check(DiagnosticKey.PendingDye1, 0, item.Pending1, index);
                diagnostics.Check(DiagnosticKey.InventoryDye0, 0, item.Inventory0 ? 1 : 0, index);
                diagnostics.Check(DiagnosticKey.InventoryDye1, 0, item.Inventory1 ? 1 : 0, index);
                diagnostics.Check(DiagnosticKey.ItemDye0, 0, item.Item0 ? 1 : 0, index);
                diagnostics.Check(DiagnosticKey.ItemDye1, 0, item.Item1 ? 1 : 0, index);
                diagnostics.Finish(DiagnosticStop.Rejected, now());
                return new(PlateCaptureOutcome.PendingDyes, null);
            }
            captured.Items[index] = new() { ItemId = item.ItemId, Stain0Id = item.Dye0, Stain1Id = item.Dye1 };
        }
        return new(PlateCaptureOutcome.Captured, captured);
    }
}

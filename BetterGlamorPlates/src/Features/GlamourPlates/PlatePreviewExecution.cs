using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Features.GlamourPlates;

internal interface IPlatePreviewPort
{
    bool ObserveReady();
    bool? Initialized { get; }
    uint? State { get; }
    bool DyeWindowOpen { get; }
    bool LoggedIn { get; }
    bool EditorOpen { get; }
    bool MatchesSlot(uint itemId, PlateSlot slot);
    void Begin();
    void SetItem(SavedSlotView slot, PlateDyeMode mode);
    void MarkActive();
    bool ContextMatches();
    bool PlateUnchanged();
    void Release();
}

internal static class PlatePreviewExecution
{
    internal static OutfitPreviewAttempt Preview(IReadOnlyList<SavedSlotView> slots, PlateDyeMode dyeMode,
        IPlatePreviewPort port, DiagnosticJournal diagnostics, Func<DateTimeOffset> now)
    {
        diagnostics.Begin(DiagnosticPhase.Preview, now());
        var ready = port.ObserveReady();
        diagnostics.Check(DiagnosticKey.PreviewInitialized, 1, port.Initialized.HasValue ? port.Initialized.Value ? 1 : 0 : null);
        diagnostics.Check(DiagnosticKey.PreviewState, 6, port.State);
        diagnostics.Check(DiagnosticKey.SlotCount, PlateRules.SlotCount, slots.Count);
        diagnostics.Check(DiagnosticKey.UniqueSlots, PlateRules.SlotCount, slots.Select(slot => slot.Slot).Distinct().Count());
        if (slots.Count != PlateRules.SlotCount || slots.Select(slot => slot.Slot).Distinct().Count() != PlateRules.SlotCount
            || !Enum.IsDefined(dyeMode) || port.DyeWindowOpen)
        {
            diagnostics.Finish(DiagnosticStop.Rejected, now());
            return OutfitPreviewAttempt.Rejected;
        }
        if (!ready || port.Initialized != true || port.State != 6)
        {
            if (port.LoggedIn && port.EditorOpen) return OutfitPreviewAttempt.NotReady;
            diagnostics.Finish(DiagnosticStop.Rejected, now());
            return OutfitPreviewAttempt.Rejected;
        }
        foreach (var slot in slots)
        {
            if ((int)slot.Slot is < 0 or >= PlateRules.SlotCount || (slot.ItemId != 0 && !port.MatchesSlot(slot.ItemId, slot.Slot)))
            {
                diagnostics.Check(DiagnosticKey.SlotCompatible, 1, 0, (int)slot.Slot);
                diagnostics.Finish(DiagnosticStop.Rejected, now());
                return OutfitPreviewAttempt.Rejected;
            }
        }
        port.Begin();
        foreach (var slot in slots)
            if (slot.ItemId != 0) port.SetItem(slot, dyeMode);
        port.MarkActive();
        if (!port.ContextMatches())
        {
            diagnostics.Finish(DiagnosticStop.Rejected, now());
            return OutfitPreviewAttempt.Rejected;
        }
        var unchanged = port.PlateUnchanged();
        diagnostics.Check(DiagnosticKey.NativePlateUnchanged, 1, unchanged ? 1 : 0);
        if (!unchanged)
        {
            diagnostics.Finish(DiagnosticStop.Rejected, now());
            port.Release();
            return OutfitPreviewAttempt.Rejected;
        }
        diagnostics.Confirm();
        diagnostics.Finish(DiagnosticStop.Completed, now());
        return OutfitPreviewAttempt.Applied;
    }
}


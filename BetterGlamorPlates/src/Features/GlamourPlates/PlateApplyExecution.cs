using BetterGlamorPlates.Features.Diagnostics;

namespace BetterGlamorPlates.Features.GlamourPlates;

/// <summary>The native boundary supplies observations and single-attempt mechanisms, never retries.</summary>
internal interface IPlateApplyPort
{
    bool ObserveReady();
    uint SelectedSlot { get; set; }
    PlateDyePlan PrepareDyes(PlateLoadPlan plan);
    void CaptureContext();
    bool ContextMatches(bool observe);
    bool HasChanges { get; set; }
    uint ReadItemId(PlateSlot slot);
    void Clear(PlateSlot slot);
    void Load(PlateLoadStep step);
    bool ObserveLoadedItem(PlateLoadStep step);
    bool ApplyDyes(PlateLoadStep step, PlateDyePlan plan);
}

/// <summary>Executes one validated plan, stopping after any ambiguous native result.</summary>
internal static class PlateApplyExecution
{
    internal static PlateApplyResult Apply(PlateLoadPlan plan, IPlateApplyPort port, DiagnosticJournal diagnostics, Func<DateTimeOffset> now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        diagnostics.Begin(DiagnosticPhase.LoadApply, now());
        diagnostics.Check(DiagnosticKey.PlanExecutable, 1, PlateResultPolicy.IsExecutable(plan) ? 1 : 0);
        var failures = new List<PlateApplyFailure>();
        if (!port.ObserveReady() || !PlateResultPolicy.IsExecutable(plan))
        {
            failures.Add(new(PlateApplyFailureKind.EditorNotReady));
            diagnostics.Finish(DiagnosticStop.Rejected, now());
            return new(0, 0, failures);
        }
        var initialSelectedSlot = port.SelectedSlot;
        var dyePlan = port.PrepareDyes(plan);
        if (dyePlan.Failures.Count > 0)
        {
            diagnostics.Finish(DiagnosticStop.Rejected, now());
            return new(0, 0, dyePlan.Failures);
        }
        port.CaptureContext();
        var hadChanges = port.HasChanges;
        var loaded = 0;
        var cleared = 0;
        try
        {
            foreach (var step in plan.Steps)
            {
                if (!port.ContextMatches(true))
                {
                    failures.Add(new(PlateApplyFailureKind.EditorClosed));
                    break;
                }
                if (step.IsSkipped) continue;
                if (step.SavedItem is null)
                {
                    if (port.ReadItemId(step.Slot) != 0)
                    {
                        port.Clear(step.Slot);
                        if (!port.ContextMatches(true))
                        {
                            failures.Add(new(PlateApplyFailureKind.EditorClosed));
                            break;
                        }
                        if (!diagnostics.Check(DiagnosticKey.ItemId, 0, port.ReadItemId(step.Slot), (int)step.Slot))
                        {
                            failures.Add(new(PlateApplyFailureKind.ClearRejected, step.Slot));
                            break;
                        }
                        cleared++;
                        diagnostics.Confirm();
                    }
                    continue;
                }
                port.SelectedSlot = (uint)step.Slot;
                port.Load(step);
                if (!port.ContextMatches(true))
                {
                    failures.Add(new(PlateApplyFailureKind.EditorClosed));
                    break;
                }
                if (!port.ObserveLoadedItem(step))
                {
                    failures.Add(new(PlateApplyFailureKind.ItemRejected, step.Slot, step.SavedItem.ItemId));
                    break;
                }
                if (!port.ApplyDyes(step, dyePlan))
                {
                    failures.Add(new(PlateApplyFailureKind.DyeRejected, step.Slot, step.SavedItem.ItemId));
                    break;
                }
                loaded++;
                diagnostics.Confirm();
            }
        }
        catch
        {
            diagnostics.Finish(DiagnosticStop.Exception, now());
            throw;
        }
        finally
        {
            if (failures.Count > 0) diagnostics.Finish(DiagnosticStop.Rejected, now());
            if (port.ContextMatches(false)) port.SelectedSlot = initialSelectedSlot;
        }
        if (port.ContextMatches(false)) port.HasChanges |= hadChanges || loaded > 0 || cleared > 0;
        else if (loaded > 0 || cleared > 0) failures.Add(new(PlateApplyFailureKind.EditorClosedAfterPartial));
        if (failures.Count == 0) diagnostics.Finish(DiagnosticStop.Completed, now());
        else if (diagnostics.Current.Stop == DiagnosticStop.None) diagnostics.Finish(DiagnosticStop.Rejected, now());
        return new(loaded, cleared, failures);
    }
}

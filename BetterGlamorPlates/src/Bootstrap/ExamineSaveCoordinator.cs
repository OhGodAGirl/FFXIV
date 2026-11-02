using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.Operations;

namespace BetterGlamorPlates.Bootstrap;

/// <summary>Schedules one character-bound Examine import and reports persistence outcomes.</summary>
internal sealed class ExamineSaveCoordinator(
    OwnedOperationRegistry operations,
    Func<Action, CancellationToken, Task> schedule,
    Func<ulong> currentCharacter,
    Func<uint, string, SavedPlate?> capture,
    Action<SavedPlate> import,
    Action<bool> setSaving,
    Action<string> setStatus,
    DiagnosticJournal diagnostics,
    Func<DateTimeOffset> clock,
    Action<Exception> reportError)
{
    private bool saving;
    internal void Save(uint entityId, string name)
    {
        if (saving) return;
        var owner = currentCharacter();
        saving = true;
        setSaving(true);
        if (!operations.TryStart(async token =>
        {
            try
            {
                await schedule(() =>
                {
                    diagnostics.Begin(DiagnosticPhase.ExamineSave, clock());
                    if (token.IsCancellationRequested)
                    {
                        diagnostics.Finish(DiagnosticStop.Cancelled, clock());
                        return;
                    }
                    diagnostics.Check(DiagnosticKey.LocalOwner, 1, owner != 0 && owner == currentCharacter() ? 1 : 0);
                    if (owner == 0 || owner != currentCharacter())
                    {
                        diagnostics.Finish(DiagnosticStop.CharacterChanged, clock());
                        setStatus("Your character changed. Nothing was saved.");
                        return;
                    }
                    try
                    {
                        var plate = capture(entityId, name);
                        diagnostics.Check(DiagnosticKey.OutfitCreated, 1, plate is null ? 0 : 1);
                        if (plate is null)
                        {
                            diagnostics.Finish(DiagnosticStop.Rejected, clock());
                            setStatus("Examine changed or is still loading. Nothing was saved.");
                            return;
                        }
                        import(plate);
                        diagnostics.Confirm();
                        diagnostics.Finish(DiagnosticStop.Completed, clock());
                        setStatus($"Saved {plate.Name}.");
                    }
                    catch
                    {
                        diagnostics.Finish(DiagnosticStop.Exception, clock());
                        throw;
                    }
                }, token);
            }
            catch (Exception exception)
            {
                setStatus("The outfit could not be saved. Check diagnostics before retrying.");
                reportError(exception);
            }
            finally { Complete(); }
        }))
            Complete();
    }
    private void Complete()
    {
        saving = false;
        setSaving(false);
    }
}

using System.Collections.Immutable;

namespace Exportaramus.Features.Inventory;

public sealed class RetainerGuideController(IRetainerGuidePort port)
{
    private ImmutableArray<InventoryRetainer> roster = [];
    private ulong contentId;
    private int index;
    private TimeSpan deadline;
    private bool bellRequested;

    public RetainerGuideStep Step { get; private set; }
    public bool Active => Step is not (RetainerGuideStep.Idle or RetainerGuideStep.Complete or RetainerGuideStep.Stopped);
    public string Message { get; private set; } = string.Empty;

    public void Start(ulong character, TimeSpan now)
    {
        if (Active) return;
        if (character == 0 || !port.IsSafe(character)) { Stop("Retainer capture is unavailable right now."); return; }
        contentId = character; roster = []; index = 0; bellRequested = false;
        Move(RetainerGuideStep.OpenBell, now, "Opening Summoning Bell");
    }

    public void Cancel() { if (Active) Stop("Retainer capture cancelled. Captured inventories are saved."); }

    public void Update(TimeSpan now)
    {
        if (!Active) return;
        if (!port.IsSafe(contentId)) { Stop("Retainer capture stopped because the character or conditions changed."); return; }
        if (now >= deadline) { Stop("Retainer capture stopped at " + Step + ". Captured inventories are kept; retry when the requested window is ready."); return; }
        if (Step == RetainerGuideStep.OpenBell)
        {
            if (port.ListReady) Move(RetainerGuideStep.ReadRoster, now, "Reading Retainers");
            else if (!bellRequested)
            {
                bellRequested = port.OpenBell();
                Message = bellRequested ? "Opening Summoning Bell" : "Move near a Summoning Bell";
            }
            return;
        }
        else if (Step == RetainerGuideStep.ReadRoster)
        {
            if (!port.TryReadRoster(out var values)) return;
            if (values.IsDefault || values.Length > 10 || values.Any(row => row.RetainerId == 0)
                || values.Select(row => row.RetainerId).Distinct().Count() != values.Length)
            { Stop("The Retainer list could not be read completely."); return; }
            roster = values;
            if (roster.IsEmpty) Move(RetainerGuideStep.CloseOverview, now, "Closing Retainer windows");
            else Move(RetainerGuideStep.SelectRetainer, now, Progress("Opening Retainer"));
            return;
        }
        else if (Step == RetainerGuideStep.SelectRetainer)
        {
            if (!port.TryReadRoster(out var current)) return;
            if (!current.SequenceEqual(roster)) { Stop("The Retainer list changed. Captured inventories are saved."); return; }
            if (port.SelectRetainer(roster[index].RetainerId)) Move(RetainerGuideStep.OpenInventory, now, Progress("Opening inventory"));
            return;
        }
        else if (Step == RetainerGuideStep.OpenInventory)
        {
            if (port.OpenInventory(roster[index].RetainerId)) Move(RetainerGuideStep.CaptureInventory, now, Progress("Capturing inventory"));
            return;
        }
        else if (Step == RetainerGuideStep.CaptureInventory)
        {
            if (port.CaptureInventory(contentId, roster[index].RetainerId)) Move(RetainerGuideStep.CloseInventory, now, Progress("Closing inventory"));
            return;
        }
        else if (Step == RetainerGuideStep.CloseInventory)
        {
            if (port.CloseInventory(roster[index].RetainerId)) Move(RetainerGuideStep.QuitRetainer, now, Progress("Returning to Retainers"));
            return;
        }
        else if (Step == RetainerGuideStep.QuitRetainer)
        {
            if (!port.InventoryVisible && port.QuitRetainer(roster[index].RetainerId)) Move(RetainerGuideStep.WaitForList, now, Progress("Returning to Retainers"));
            return;
        }
        else if (Step == RetainerGuideStep.WaitForList)
        {
            if (!port.ListReady) return;
            index++;
            if (index == roster.Length) Move(RetainerGuideStep.CloseOverview, now, "Closing Retainer windows");
            else Move(RetainerGuideStep.SelectRetainer, now, Progress("Opening Retainer"));
            return;
        }
        else // Every other active state was handled above; only CloseOverview remains.
        {
            if (port.CloseOverviewAndInventory()) Complete();
            return;
        }
    }

    private string Progress(string action) => $"{action} ({index + 1}/{roster.Length})";
    private void Move(RetainerGuideStep step, TimeSpan now, string message)
    { Step = step; deadline = now + TimeSpan.FromSeconds(step == RetainerGuideStep.OpenBell ? 60 : 15); Message = message; }
    private void Stop(string message) { Step = RetainerGuideStep.Stopped; Message = message; }
    private void Complete() { Step = RetainerGuideStep.Complete; Message = $"Retainer capture complete ({roster.Length}/{roster.Length})."; }
}

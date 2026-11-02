namespace SpringCleaning;

internal interface IDresserCleanupRuntime
{
    bool CanRestore(DresserAlertEntry entry);
    bool BeginRestore(DresserAlertEntry entry);
    RestoreObservation ObserveRestore();
    void Reset();
}

internal sealed record RestoreObservation(bool Waiting, IReadOnlyList<InventoryDuplicateItem> Copies, string? Failure)
{
    internal static RestoreObservation Pending => new(true, [], null);
}

// Restore while the Dresser is still open, then submit one combined physical-copy queue.
// The existing discard controller retains ownership of prompts and native completion.
internal sealed class DresserCleanupController(
    IDresserCleanupRuntime runtime, DuplicateDiscardController discard, Func<DateTime> now)
{
    internal Action<string, int>? Stopped { get; set; }

    private readonly Queue<DresserAlertEntry> entries = new();
    private readonly List<InventoryDuplicateItem> copies = [];
    private bool waiting;
    private DateTime deadline;
    private DateTime nextObservation;
    private bool cancelled;
    public bool IsRestoring => waiting || entries.Count != 0;
    public bool IsRunning => IsRestoring || discard.IsAnyRunning;
    public string? Status { get; private set; }
    public int RestoreTotal { get; private set; }
    public int RestoreCompleted { get; private set; }

    public bool CanRestore(DresserAlertEntry entry) => !IsRunning && runtime.CanRestore(entry);

    public void Start(IReadOnlyList<InventoryDuplicateItem> carried, IReadOnlyList<DresserAlertEntry> stored)
    {
        if (IsRunning)
            return;
        cancelled = false;
        copies.AddRange(InventoryDuplicatePlanner.SelectBulkDiscardCandidates(carried));
        foreach (var entry in stored.Where(runtime.CanRestore))
            entries.Enqueue(entry);
        RestoreTotal = entries.Count;
        RestoreCompleted = 0;
        Advance();
    }

    public void Update()
    {
        if (!waiting)
            return;
        if (now() >= deadline)
        {
            Stop("Restoration could not be verified. Restored items remain in Inventory, kupo!");
            return;
        }
        if (now() < nextObservation)
            return;
        nextObservation = now().AddMilliseconds(250);
        var observation = runtime.ObserveRestore();
        if (observation.Failure is { } failure)
        {
            Stop(failure);
            return;
        }
        if (observation.Waiting)
            return;
        waiting = false;
        RestoreCompleted++;
        copies.AddRange(observation.Copies);
        if (cancelled)
            Stop("Cleanup cancelled. Restored items remain in Inventory, kupo!");
        else
            Advance();
    }

    public void Cancel()
    {
        cancelled = true;
        discard.CancelAll();
        // A submitted restore is not undone. Keep its bounded observer until it settles.
        if (!waiting)
            Stop("Cleanup cancelled, kupo!");
    }

    private void Advance()
    {
        if (entries.TryDequeue(out var entry))
        {
            if (!runtime.BeginRestore(entry))
            {
                Stop("That Dresser entry cannot be restored. Any restored items remain in Inventory, kupo!");
                return;
            }
            waiting = true;
            deadline = now().AddSeconds(15);
            nextObservation = now();
            Status = $"Restoring {entry.Name}, kupo!";
            return;
        }
        runtime.Reset();
        if (copies.Count != 0)
            discard.StartBulk(copies.ToArray());
        Status = copies.Count == 0 ? "No duplicates can be discarded, kupo!" : null;
        copies.Clear();
    }

    private void Stop(string reason)
    {
        Stopped?.Invoke(reason, RestoreCompleted);
        entries.Clear();
        copies.Clear();
        waiting = false;
        runtime.Reset();
        Status = reason;
    }
}

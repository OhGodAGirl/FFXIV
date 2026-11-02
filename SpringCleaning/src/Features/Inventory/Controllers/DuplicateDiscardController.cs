namespace SpringCleaning;

internal sealed class DuplicateDiscardController
{
    internal Action<string, int>? Stopped { get; set; }

    // Both carried sources are now included. Reject an impossible input instead of
    // silently truncating the user's Discard All command at the four-bag capacity.
    private const int MaximumBulkCandidates = 1024;
    private static readonly TimeSpan InitialSingleDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan SingleDeadline = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan BulkDeadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan NextBulkItemDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PreviewRefreshDelay = TimeSpan.FromMilliseconds(750);
    private readonly IDuplicateDiscardRuntime runtime;
    private readonly Func<DateTime> utcNow;
    private readonly Action<TimeSpan> requestPreviewRefresh;
    private readonly Queue<InventoryDuplicateItem> bulkQueue = new();
    private InventoryDuplicateItem? pendingSingle;
    private DateTime pendingSingleAfter;
    private DateTime pendingSingleDeadline;
    private bool pendingSingleRequestIssued;
    private bool pendingSingleConfirmationIssued;
    private InventoryDuplicateItem? bulkCurrent;
    private DateTime bulkNextActionAt;
    private DateTime bulkDeadline;
    private bool bulkRequestIssued;
    private bool bulkConfirmationIssued;

    public DuplicateDiscardController(
                IDuplicateDiscardRuntime runtime,
                Func<DateTime> utcNow,
                Action<TimeSpan> requestPreviewRefresh)
    {
        this.runtime = runtime;
        this.utcNow = utcNow;
        this.requestPreviewRefresh = requestPreviewRefresh;
    }

    public string? Status { get; private set; }

    public int BulkTotal { get; private set; }

    public int BulkCompleted { get; private set; }

    public bool IsBulkRunning => bulkCurrent is not null || bulkQueue.Count > 0;

    public bool IsAnyRunning => pendingSingle is not null || IsBulkRunning;

    public void StartBulk(IReadOnlyList<InventoryDuplicateItem> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count > MaximumBulkCandidates)
        {
            Status = "Too many duplicate locations were reported. Cleanup stopped, kupo!";
            return;
        }
        bulkQueue.Clear();
        foreach (var candidate in candidates)
            bulkQueue.Enqueue(candidate);

        BulkTotal = bulkQueue.Count;
        BulkCompleted = 0;
        bulkCurrent = null;
        bulkRequestIssued = false;
        bulkNextActionAt = utcNow();
        bulkDeadline = DateTime.MinValue;
        Status = $"Preparing to discard {BulkTotal:N0} duplicates…";
    }

    public void StartSingle(InventoryDuplicateItem candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var now = utcNow();
        pendingSingle = candidate;
        pendingSingleRequestIssued = false;
        pendingSingleConfirmationIssued = false;
        pendingSingleAfter = now.Add(InitialSingleDelay);
        pendingSingleDeadline = now.Add(SingleDeadline);
        Status = $"Closing the Glamour Dresser before discarding {candidate.Name}…";
        runtime.CloseDresser();
    }

    public void OpenStoreMenu(InventoryDuplicateItem candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Status = runtime.OpenInventoryContext(candidate)
            ? "Choose Store in Glamour Dresser from the game menu and review its confirmation."
            : "That inventory slot changed before its game menu could be opened.";
    }

    public bool IsActive(InventoryDuplicateItem candidate) =>
        IsSame(pendingSingle, candidate) || IsSame(bulkCurrent, candidate);

    public bool IsQueued(InventoryDuplicateItem candidate) =>
        bulkQueue.Any(queued => IsSame(queued, candidate));

    public void Update()
    {
        ProcessBulk();
        if (bulkCurrent is null && bulkQueue.Count == 0)
            ProcessSingle();
    }

    public void CancelBulk()
    {
        if (bulkRequestIssued)
            runtime.CancelDiscardConfirmation(bulkCurrent!, requestIssued: true);
        StopBulk("Discarding duplicates cancelled.");
    }

    public void CancelAll()
    {
        if (bulkRequestIssued)
            runtime.CancelDiscardConfirmation(bulkCurrent!, requestIssued: true);
        else if (pendingSingleRequestIssued)
            runtime.CancelDiscardConfirmation(pendingSingle!, requestIssued: true);

        bulkQueue.Clear();
        bulkCurrent = null;
        bulkRequestIssued = false;
        ClearSingle();
        Status = "Discarding duplicates cancelled.";
    }

    private void ProcessBulk()
    {
        var now = utcNow();
        if (bulkCurrent is null)
        {
            if (bulkQueue.Count == 0)
                return;

            bulkCurrent = bulkQueue.Dequeue();
            bulkRequestIssued = false;
            bulkConfirmationIssued = false;
            bulkDeadline = now.Add(BulkDeadline);
            bulkNextActionAt = now.Add(PollInterval);
        }

        var candidate = bulkCurrent!;
        if (now < bulkNextActionAt)
            return;

        var present = runtime.IsInventoryItemPresent(candidate);
        if (present is null)
        {
            Status = "Waiting for the duplicate's Inventory or Armoury Chest location, kupo!";
            if (now >= bulkDeadline)
                StopBulk("The duplicate's location could not be verified. Cleanup stopped, kupo!");
            bulkNextActionAt = now.Add(PollInterval);
            return;
        }
        if (!present.Value)
        {
            if (!bulkRequestIssued)
            {
                StopBulk("A copy moved before discard was requested. Cleanup stopped, kupo!");
                return;
            }
            BulkCompleted++;
            Status = bulkQueue.Count == 0
                ? $"Discarded {BulkCompleted:N0} safe carried copies."
                : $"Discarded {BulkCompleted:N0} of {BulkTotal:N0} safe carried copies.";
            bulkCurrent = null;
            bulkRequestIssued = false;
            requestPreviewRefresh(PreviewRefreshDelay);
            bulkNextActionAt = now.Add(NextBulkItemDelay);
            return;
        }

        if (runtime.IsDresserOpen)
        {
            if (now >= bulkDeadline)
            {
                StopBulk("The Glamour Dresser did not close. Batch discard stopped.");
                return;
            }
            Status = $"Closing the Glamour Dresser before discarding {candidate.Name}…";
            runtime.CloseDresser();
            bulkNextActionAt = now.Add(PollInterval);
            return;
        }

        if (runtime.IsDiscardConfirmationOpen)
        {
            if (now >= bulkDeadline)
            {
                StopBulk("The FFXIV discard confirmation timed out. Batch discard stopped.");
                return;
            }
            if (bulkRequestIssued && !bulkConfirmationIssued
                && runtime.ConfirmDiscardConfirmation(candidate, requestIssued: true))
            {
                bulkConfirmationIssued = true;
                Status = $"Discarding {candidate.Name} · {BulkCompleted + 1:N0} of {BulkTotal:N0}…";
                bulkNextActionAt = now.Add(PollInterval);
                return;
            }

            Status = $"Waiting for FFXIV to confirm discarding {candidate.Name}…";
            return;
        }

        if (runtime.IsOccupied)
        {
            if (now >= bulkDeadline)
            {
                StopBulk(
                    "The game remained occupied. Batch discard stopped without touching the remaining copies.");
            }
            return;
        }

        if (bulkRequestIssued)
        {
            if (now >= bulkDeadline)
            {
                StopBulk(
                    $"The game did not open a discard confirmation for {candidate.Name}. Batch discard stopped.");
            }
            return;
        }

        if (now >= bulkDeadline)
        {
            StopBulk("An inventory slot changed before it could be discarded. Batch discard stopped.");
            return;
        }
        if (!runtime.CanSafelyOpenDiscardPrompt(candidate))
        {
            StopBulk(
                $"{candidate.Name} changed or now needs manual review. Batch discard stopped without opening a prompt.");
            return;
        }

        if (runtime.RequestDiscard(candidate))
        {
            bulkRequestIssued = true;
            Status = $"Requesting discard confirmation for {candidate.Name}…";
            bulkNextActionAt = now.Add(PollInterval);
        }
        else
        {
            bulkNextActionAt = now.Add(PollInterval);
        }
    }

    private void StopBulk(string status)
    {
        Stopped?.Invoke(status, BulkCompleted);
        bulkQueue.Clear();
        bulkCurrent = null;
        bulkRequestIssued = false;
        Status = status;
        requestPreviewRefresh(PreviewRefreshDelay);
    }

    private void ProcessSingle()
    {
        var candidate = pendingSingle;
        if (candidate is null)
            return;

        var now = utcNow();
        var present = runtime.IsInventoryItemPresent(candidate);
        if (present is null)
        {
            Status = "Waiting for the duplicate's Inventory or Armoury Chest location, kupo!";
            if (now >= pendingSingleDeadline)
            {
                Status = "The duplicate's location could not be verified. Cleanup stopped, kupo!";
                ClearSingle();
            }
            return;
        }
        if (!present.Value)
        {
            Status = pendingSingleRequestIssued ? $"Discarded {candidate.Name}."
                : "A copy moved before discard was requested. Cleanup stopped, kupo!";
            ClearSingle();
            requestPreviewRefresh(NextBulkItemDelay);
            return;
        }

        if (runtime.IsDresserOpen)
        {
            if (now >= pendingSingleDeadline)
            {
                Status = "The Glamour Dresser did not close. Spring Cleaning stopped.";
                ClearSingle();
                return;
            }
            runtime.CloseDresser();
            return;
        }

        if (runtime.IsDiscardConfirmationOpen)
        {
            if (now >= pendingSingleDeadline)
            {
                Status = "The FFXIV discard confirmation timed out. Spring Cleaning stopped.";
                ClearSingle();
                return;
            }
            if (pendingSingleRequestIssued && !pendingSingleConfirmationIssued
                && runtime.ConfirmDiscardConfirmation(candidate, requestIssued: true))
            {
                pendingSingleConfirmationIssued = true;
                Status = $"Discarding {candidate.Name}…";
                return;
            }

            Status = $"Waiting for FFXIV to confirm discarding {candidate.Name}…";
            return;
        }

        if (now < pendingSingleAfter)
            return;

        if (runtime.IsOccupied)
        {
            if (now >= pendingSingleDeadline)
            {
                Status = $"The game remained occupied, so {candidate.Name} was not discarded.";
                ClearSingle();
            }
            return;
        }

        if (pendingSingleRequestIssued)
        {
            if (now >= pendingSingleDeadline)
            {
                Status = $"The game did not open a discard confirmation for {candidate.Name}.";
                ClearSingle();
            }
            return;
        }

        if (now >= pendingSingleDeadline)
        {
            Status = "That inventory slot changed before the discard confirmation could be opened.";
            ClearSingle();
            return;
        }
        if (!runtime.CanSafelyOpenDiscardPrompt(candidate))
        {
            Status = $"{candidate.Name} changed or now needs manual review. No discard prompt was opened.";
            ClearSingle();
            return;
        }

        if (runtime.RequestDiscard(candidate))
        {
            pendingSingleRequestIssued = true;
            Status = $"Waiting for the discard confirmation for {candidate.Name}…";
            pendingSingleAfter = now.Add(PollInterval);
        }
        else
        {
            pendingSingleAfter = now.Add(PollInterval);
        }
    }

    private void ClearSingle()
    {
        pendingSingle = null;
        pendingSingleRequestIssued = false;
    }

    private static bool IsSame(
        InventoryDuplicateItem? left,
        InventoryDuplicateItem right) =>
        left is not null
        && left.InventoryTypeId == right.InventoryTypeId
        && left.SlotIndex == right.SlotIndex
        && left.ItemId == right.ItemId;
}

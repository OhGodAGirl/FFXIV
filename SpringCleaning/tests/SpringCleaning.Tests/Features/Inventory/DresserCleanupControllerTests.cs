namespace SpringCleaning.Tests.Features.Inventory;

public sealed class DresserCleanupControllerTests
{
    private static DresserAlertEntry Entry(int index) => new(index, (uint)index, (uint)index,
        false, false, 1, "", "Item", 1, DresserAlertKind.ArmoireEligible, true, false, false);
    private static InventoryDuplicateItem Copy(int slot) => new(0, slot, (uint)slot, false,
        "Item", 1, new(true, true, ""), true, false, InventoryDuplicateDisposition.RemovalReview);

    [Fact]
    public void StoredEntriesAreObservedBeforeCombinedQueueStarts()
    {
        var f = new Fixture();
        f.Controller.Start([Copy(1)], [Entry(2), Entry(3)]);
        Assert.Equal(2, f.Controller.RestoreTotal);
        Assert.Equal(0, f.Controller.RestoreCompleted);
        Assert.Equal(1, f.Runtime.Requests);
        Assert.Equal("Restoring Item, kupo!", f.Controller.Status);
        Assert.False(f.Discard.IsAnyRunning);
        f.Runtime.Observation = new(false, [Copy(2)], null);
        f.Controller.Update();
        Assert.Equal(1, f.Controller.RestoreCompleted);
        Assert.Equal(2, f.Runtime.Requests);
        Assert.False(f.Discard.IsAnyRunning);
        f.Runtime.Observation = new(false, [Copy(3)], null);
        f.Controller.Update();
        Assert.Equal(3, f.Discard.BulkTotal);
        Assert.True(f.Controller.IsRunning);
        Assert.False(f.Controller.IsRestoring);
        Assert.Null(f.Controller.Status);
        Assert.Equal(1, f.Runtime.Resets);
    }

    [Fact]
    public void CancelAfterRequestObservesItButNeverDiscards()
    {
        var f = new Fixture();
        f.Controller.Start([], [Entry(2), Entry(3)]);
        f.Controller.Cancel();
        Assert.True(f.Controller.IsRestoring);
        f.Runtime.Observation = new(false, [Copy(2)], null);
        f.Controller.Update();
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(1, f.Runtime.Requests);
        Assert.Equal(0, f.Discard.BulkTotal);
        Assert.Contains("remain", f.Controller.Status);
    }

    [Fact]
    public void PendingRestoreHasAnExactBoundedDeadline()
    {
        var f = new Fixture();
        f.Controller.Start([], [Entry(2)]);
        f.Now = f.Now.AddSeconds(14);
        f.Controller.Update();
        Assert.True(f.Controller.IsRestoring);
        f.Now = f.Now.AddSeconds(1);
        f.Controller.Update();
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(0, f.Discard.BulkTotal);
        Assert.Contains("could not be verified", f.Controller.Status);
        Assert.Equal(1, f.Runtime.Observations);
    }

    [Fact]
    public void NativeObservationIsPolledAtMostFourTimesPerSecond()
    {
        var f = new Fixture();
        f.Controller.Start([], [Entry(2)]);
        f.Controller.Update();
        f.Now = f.Now.AddMilliseconds(249);
        f.Controller.Update();
        Assert.Equal(1, f.Runtime.Observations);
        f.Now = f.Now.AddMilliseconds(1);
        f.Controller.Update();
        Assert.Equal(2, f.Runtime.Observations);
    }

    [Fact]
    public void AmbiguousRestoreNeverSubmitsEarlierCarriedCandidates()
    {
        var f = new Fixture();
        f.Controller.Start([Copy(1)], [Entry(2)]);
        f.Runtime.Observation = new(false, [], "Identity changed");
        f.Controller.Update();
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(0, f.Discard.BulkTotal);
        Assert.Equal("Identity changed", f.Controller.Status);
    }

    [Fact]
    public void BusyRunRejectsAnotherCommandAndEligibility()
    {
        var f = new Fixture();
        Assert.True(f.Controller.CanRestore(Entry(2)));
        f.Controller.Start([], [Entry(2)]);
        Assert.False(f.Controller.CanRestore(Entry(3)));
        f.Controller.Start([], [Entry(3)]);
        Assert.Equal(1, f.Runtime.Requests);
    }

    [Fact]
    public void EmptyAndIdleCancellationAreStable()
    {
        var f = new Fixture();
        f.Runtime.Eligible = false;
        Assert.False(f.Controller.CanRestore(Entry(2)));
        f.Controller.Start([], [Entry(2)]);
        Assert.False(f.Controller.IsRunning);
        Assert.Contains("No duplicates", f.Controller.Status);
        var idleStatus = f.Controller.Status;
        f.Controller.Update();
        Assert.Equal(idleStatus, f.Controller.Status);
        Assert.Equal(0, f.Runtime.Observations);
        f.Controller.Cancel();
        Assert.Equal("Cleanup cancelled, kupo!", f.Controller.Status);
    }

    [Fact]
    public void FailedSubmissionStopsWithoutDiscarding()
    {
        var f = new Fixture();
        f.Runtime.Accept = false;
        f.Controller.Start([Copy(1)], [Entry(2), Entry(3)]);
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(0, f.Discard.BulkTotal);
        Assert.Contains("cannot be restored", f.Controller.Status);
        Assert.Equal(1, f.Runtime.Resets);
        f.Controller.Start([], []);
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(2, f.Runtime.Resets);
    }

    [Fact]
    public void CompletedAndCancelledOperationsDoNotLeakCopiesIntoTheNextRun()
    {
        var f = new Fixture();
        f.Controller.Start([Copy(1)], []);
        Assert.True(f.Discard.IsAnyRunning);
        f.Discard.CancelAll();
        f.Controller.Start([], []);
        Assert.False(f.Controller.IsRunning);
        f.Controller.Start([Copy(1)], []);
        f.Controller.Cancel();
        Assert.False(f.Discard.IsAnyRunning);
        f.Controller.Start([], []);
        Assert.False(f.Controller.IsRunning);
        f.Controller.Start([], [Entry(2)]);
        f.Runtime.Observation = new(false, [Copy(2)], null);
        f.Controller.Update();
        Assert.Equal(1, f.Discard.BulkTotal);
        Assert.True(f.Discard.IsAnyRunning);
    }

    [Fact]
    public void FailureObserverSeesCompletedRestoresBeforeCleanupAndCannotSubmitMoreCopies()
    {
        var f = new Fixture();
        var observed = new List<(string Reason, int Count, bool Restoring, int Resets)>();
        f.Controller.Stopped = (reason, count) =>
            observed.Add((reason, count, f.Controller.IsRestoring, f.Runtime.Resets));
        f.Controller.Start([Copy(1)], [Entry(2), Entry(3)]);
        f.Runtime.Observation = new(false, [Copy(2)], null);
        f.Controller.Update();
        f.Runtime.Observation = new(false, [], "Provider session changed");

        f.Controller.Update();

        Assert.Equal(("Provider session changed", 1, true, 0), Assert.Single(observed));
        Assert.Equal(1, f.Runtime.Resets);
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(0, f.Discard.BulkTotal);
        f.Controller.Update();
        Assert.Equal(2, f.Runtime.Requests);
        Assert.Single(observed);
    }

    private sealed class Fixture
    {
        public DateTime Now = new(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        public readonly Runtime Runtime = new();
        public readonly DuplicateDiscardController Discard;
        public readonly DresserCleanupController Controller;
        public Fixture()
        {
            Discard = new(new DiscardRuntime(), () => Now, _ => { });
            Controller = new(Runtime, Discard, () => Now);
        }
    }
    private sealed class Runtime : IDresserCleanupRuntime
    {
        public int Requests;
        public int Observations;
        public int Resets;
        public bool Eligible = true;
        public bool Accept = true;
        public RestoreObservation Observation = RestoreObservation.Pending;
        public bool CanRestore(DresserAlertEntry entry) => Eligible;
        public bool BeginRestore(DresserAlertEntry entry) { Requests++; return Accept; }
        public RestoreObservation ObserveRestore() { Observations++; return Observation; }
        public void Reset() { Resets++; }
    }
    private sealed class DiscardRuntime : IDuplicateDiscardRuntime
    {
        public bool IsDresserOpen => true;
        public bool IsDiscardConfirmationOpen => false;
        public bool IsOccupied => false;
        public bool? IsInventoryItemPresent(InventoryDuplicateItem candidate) => true;
        public void CloseDresser() { }
        public bool CanSafelyOpenDiscardPrompt(InventoryDuplicateItem candidate) => true;
        public bool RequestDiscard(InventoryDuplicateItem candidate) => true;
        public bool ConfirmDiscardConfirmation(InventoryDuplicateItem candidate, bool requestIssued) => true;
        public void CancelDiscardConfirmation(InventoryDuplicateItem candidate, bool requestIssued) { }
        public bool OpenInventoryContext(InventoryDuplicateItem candidate) => true;
    }
}

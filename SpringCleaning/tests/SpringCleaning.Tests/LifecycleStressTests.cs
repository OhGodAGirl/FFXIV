namespace SpringCleaning.Tests;

using SpringCleaning.Shared.CollectionData;

public sealed class LifecycleStressTests
{
    [Fact]
    public void OwnedRegistrationsSurviveRepeatedConstructionAndDisposal()
    {
        for (var iteration = 0; iteration < 250; iteration++)
        {
            var order = new List<int>();
            var registrations = new RuntimeRegistrationSet();
            registrations.Add(() => order.Add(1), () => order.Add(-1));
            registrations.Add(() => order.Add(2), () => order.Add(-2));

            Assert.Empty(registrations.Dispose());
            Assert.Empty(registrations.Dispose());
            Assert.Equal(new[] { 1, 2, -2, -1 }, order);
        }
    }

    [Fact]
    public void PartialRegistrationFailureStillReleasesEarlierOwnership()
    {
        var released = 0;
        var registrations = new RuntimeRegistrationSet();
        registrations.Add(() => { }, () => released++);

        Assert.Throws<InvalidOperationException>(() => registrations.Add(
            () => throw new InvalidOperationException("partial initialization"),
            () => released += 100));

        Assert.Empty(registrations.Dispose());
        Assert.Equal(1, released);
    }

    [Fact]
    public void TeardownContinuesAfterOneReleaseFails()
    {
        var released = 0;
        var registrations = new RuntimeRegistrationSet();
        registrations.Add(() => { }, () => released++);
        registrations.Add(() => { }, () => throw new InvalidOperationException("release"));
        registrations.Add(() => { }, () => released++);

        var errors = registrations.Dispose();

        Assert.Single(errors);
        Assert.Equal(2, released);
    }

    [Fact]
    public void TeardownPreservesEveryFailureAndClearsOwnedRegistrations()
    {
        var registrations = new RuntimeRegistrationSet();
        registrations.Add(() => { }, () => throw new InvalidOperationException("first"));
        registrations.Add(() => { }, () => throw new ArgumentException("second"));

        var errors = registrations.Dispose();

        Assert.Collection(
            errors,
            error => Assert.Equal("second", error.Message),
            error => Assert.Equal("first", error.Message));
        Assert.Equal(0, registrations.Count);
    }

    [Fact]
    public void RegistrationRequiresBothCallbacks()
    {
        var registrations = new RuntimeRegistrationSet();

        var registerError = Assert.Throws<ArgumentNullException>(() =>
            registrations.Add(null!, () => { }));
        var teardownError = Assert.Throws<ArgumentNullException>(() =>
            registrations.Add(() => { }, null!));

        Assert.Equal("register", registerError.ParamName);
        Assert.Equal("teardown", teardownError.ParamName);
    }

    [Fact]
    public void RegistrationAfterDisposalIsRejected()
    {
        var registrations = new RuntimeRegistrationSet();
        registrations.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            registrations.Add(() => { }, () => { }));
    }

    [Fact]
    public void DisposalDuringDuplicateWorkCancelsOwnedPromptAndRejectsStaleCompletion()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var now = new TestClock();
            var runtime = new FakeDuplicateRuntime();
            var controller = new DuplicateDiscardController(runtime, now.GetUtcNow, _ => { });
            var candidate = Duplicate(iteration);
            controller.StartSingle(candidate);
            runtime.DresserOpen = false;
            now.Advance(TimeSpan.FromSeconds(1));
            controller.Update();
            Assert.Equal(1, runtime.DiscardRequests);

            controller.CancelAll();
            controller.CancelAll();
            runtime.Present = false;
            controller.Update();

            Assert.False(controller.IsAnyRunning);
            Assert.Equal(1, runtime.CancelRequests);
            Assert.Equal(1, runtime.DiscardRequests);
        }
    }

    [Fact]
    public void CollectionDataRefreshLifecycleSurvivesRepeatedLogoutAndDisposal()
    {
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var controller = new CollectionDataRefreshController();
            var initial = controller.Observe((ulong)(100 + iteration), providerRequested: false);
            Assert.NotNull(initial.RefreshRequest);
            _ = controller.Complete(
                initial.RefreshRequest!.Value.ContentId,
                CollectionDataParseResult.Accept(CollectionSnapshot(initial.RefreshRequest.Value.ContentId, 7)),
                CollectionSnapshot(initial.RefreshRequest.Value.ContentId, 7));

            var logout = controller.Observe(0, providerRequested: true);
            Assert.True(logout.ClearTransport);
            Assert.Null(logout.RefreshRequest);
            Assert.Equal(CollectionDataFailure.NoCharacter, controller.Snapshot.LastFailure);

            controller.Dispose();
            controller.Dispose();
            var disposed = controller.Observe(999, providerRequested: true);
            Assert.Equal(CollectionDataFailure.Disposed, controller.Snapshot.LastFailure);
            Assert.Equal(default, disposed);
        }
    }

    private static InventoryDuplicateItem Duplicate(int slot) => new(
        0,
        slot,
        (uint)(1000 + slot),
        false,
        $"Item {slot}",
        0,
        new InventoryItemSafety(true, true, "Ready"),
        false,
        true,
        InventoryDuplicateDisposition.RemovalReview);

    private sealed class TestClock
    {
        private DateTime now = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        public DateTime GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }

    private sealed class FakeDuplicateRuntime : IDuplicateDiscardRuntime
    {
        public bool Present { get; set; } = true;
        public bool DresserOpen { get; set; } = true;
        public int DiscardRequests { get; private set; }
        public int CancelRequests { get; private set; }
        public bool IsDresserOpen => DresserOpen;
        public bool IsDiscardConfirmationOpen => false;
        public bool IsOccupied => false;
        public bool? IsInventoryItemPresent(InventoryDuplicateItem candidate) => Present;
        public void CloseDresser() => DresserOpen = false;
        public bool CanSafelyOpenDiscardPrompt(InventoryDuplicateItem candidate) => true;
        public bool RequestDiscard(InventoryDuplicateItem candidate)
        {
            DiscardRequests++;
            return true;
        }
        public bool ConfirmDiscardConfirmation(InventoryDuplicateItem candidate, bool requestIssued) => false;
        public void CancelDiscardConfirmation(InventoryDuplicateItem candidate, bool requestIssued) => CancelRequests++;
        public bool OpenInventoryContext(InventoryDuplicateItem candidate) => true;
    }

    private static CollectionDataSnapshot CollectionSnapshot(ulong contentId, ulong revision) => new(
        "CollectorsCompendium",
        "0.3.0.0",
        contentId,
        revision,
        new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
        CollectionSource(CollectionSourceStatus.Fresh),
        CollectionSource(CollectionSourceStatus.Fresh),
        CollectionSource(CollectionSourceStatus.Fresh),
        CollectionSource(CollectionSourceStatus.Fresh),
        CollectionSource(CollectionSourceStatus.Fresh),
        CollectionSource(CollectionSourceStatus.Stale),
        CollectionSource(CollectionSourceStatus.NeverObserved));

    private static CollectionSourceSnapshot CollectionSource(CollectionSourceStatus status) => status switch
    {
        CollectionSourceStatus.NeverObserved => new(status, null, [], null),
        CollectionSourceStatus.Stale => new(
            status,
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            [4],
            "Refresh required"),
        _ => new(
            status,
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
            [1],
            null),
    };
}

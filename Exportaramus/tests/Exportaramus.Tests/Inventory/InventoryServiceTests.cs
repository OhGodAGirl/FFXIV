using System.Collections.Immutable;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T20:00:00Z");
    private static readonly InventoryLocationKey Bag = new(InventoryLocationKind.Inventory);
    private static readonly InventoryLocationKey First = new(InventoryLocationKind.Retainer, 11);
    private static readonly InventoryLocationKey Second = new(InventoryLocationKind.Retainer, 22);

    [Fact]
    public async Task InvalidRosterEntriesNeverReplaceTheCurrentRoster()
    {
        await using var service = new InventoryService();
        service.SetCharacter(1);
        Assert.False(service.ObserveRoster(1, [null!]));
        Assert.False(service.ObserveRoster(1, [new(0, "Name")]));
        Assert.False(service.ObserveRoster(1, [new(1, "")]));
        Assert.False(service.ObserveRoster(1, [new(1, "Name"), new(1, "Duplicate")]));
        Assert.False(service.GetSnapshot()!.RetainerRosterKnown);
    }

    [Fact]
    public async Task BoundedQueueRetainsPreviouslyQueuedCharacterWhenNewCharacterCannotQueue()
    {
        var cache = new RecordingCache();
        await using var service = new InventoryService(cache);
        for (ulong id = 1; id <= 17; id++)
        {
            service.SetCharacter(id);
            Assert.True(service.AcceptLocation(id, Bag, "Inventory", [], Now));
        }
        Assert.Equal("InventoryCacheQueueFull", service.LastCacheFailure);
        await service.DisposeAsync();
        Assert.Equal(16, cache.Writes.Count);
        Assert.DoesNotContain(cache.Writes, value => value.ContentId == 17);
    }

    [Fact]
    public async Task PumpAfterLogoutCannotRepublishCompletedRestore()
    {
        var cache = new ControlledCache();
        await using var service = new InventoryService(cache);
        service.SetCharacter(1);
        service.Pump();
        await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.SetCharacter(0);
        cache.Loaded.SetResult(Saved(1));
        await Task.Delay(10);
        service.Pump();
        Assert.Null(service.GetSnapshot());
    }

    private sealed class RecordingCache : IInventoryCache
    {
        public List<InventorySnapshot> Writes { get; } = [];
        public Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken) => Task.FromResult<InventorySnapshot?>(null);
        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken) { Writes.Add(snapshot); return Task.CompletedTask; }
    }

    [Fact]
    public async Task RetainersReplaceIndependentlyAndFailuresRetainPriorContents()
    {
        await using var service = new InventoryService();
        service.SetCharacter(1);
        Assert.True(service.ObserveRoster(1, [new(11, "First"), new(22, "Second")]));
        Assert.True(service.AcceptLocation(1, First, "First", [new(100, 2, 1), new(100, 3, 4)], Now));
        Assert.True(service.AcceptLocation(1, Second, "Second", [new(200, 1, 0)], Now));
        var immutable = service.GetSnapshot()!;
        Assert.Equal(new(100, 5, 5), At(immutable, First).Items.Single());
        service.AcceptLocation(1, First, "First", [], Now.AddSeconds(1));
        Assert.Empty(At(service.GetSnapshot()!, First).Items);
        Assert.Single(At(service.GetSnapshot()!, Second).Items);
        Assert.Single(At(immutable, First).Items);
        service.MarkUnavailable(1, Second, "Second", Now.AddSeconds(2), true);
        var failed = At(service.GetSnapshot()!, Second);
        Assert.Equal(InventoryObservationState.ReadFailed, failed.State);
        Assert.Equal(Now, failed.ObservedAtUtc);
        Assert.Single(failed.Items);
        service.MarkUnavailable(1, Second, "Second", Now.AddSeconds(3), false);
        Assert.Equal(InventoryObservationState.Retained, At(service.GetSnapshot()!, Second).State);
    }

    [Fact]
    public async Task UnknownRosterAndUnreadRetainersNeverBecomeKnownEmpty()
    {
        await using var service = new InventoryService();
        service.SetCharacter(1);
        Assert.False(service.GetSnapshot()!.RetainerRosterKnown);
        Assert.False(service.AcceptLocation(1, First, "First", [], Now));
        service.ObserveRoster(1, [new(11, "First"), new(22, "Second")]);
        service.AcceptLocation(1, First, "First", [], Now);
        Assert.True(At(service.GetSnapshot()!, First).HasObservation);
        Assert.False(At(service.GetSnapshot()!, Second).HasObservation);
        Assert.Equal(InventoryObservationState.Unknown, At(service.GetSnapshot()!, Second).State);
        var beforeInvalidRoster = service.GetSnapshot();
        Assert.False(service.ObserveRoster(1, [new(11, "First"), new(11, "Duplicate")]));
        Assert.Same(beforeInvalidRoster, service.GetSnapshot());
        service.ObserveRoster(1, [new(22, "Renamed")]);
        Assert.DoesNotContain(service.GetSnapshot()!.Locations, location => location.Key == First);
        Assert.Equal("Renamed", At(service.GetSnapshot()!, Second).Name);
        service.ObserveRoster(1, []);
        Assert.True(service.GetSnapshot()!.RetainerRosterKnown);
        Assert.DoesNotContain(service.GetSnapshot()!.Locations, location => location.Key.Kind == InventoryLocationKind.Retainer);
    }

    [Fact]
    public async Task QuantityOverflowRejectsWholeLocationWithoutLosingItsOldSnapshot()
    {
        await using var service = new InventoryService();
        service.SetCharacter(1);
        service.AcceptLocation(1, Bag, "Inventory", [new(100, 2, 0)], Now);
        service.AcceptLocation(1, Bag, "Inventory", [new(100, uint.MaxValue, 0), new(100, 0, 1)], Now.AddSeconds(1));
        var location = At(service.GetSnapshot()!, Bag);
        Assert.Equal(InventoryObservationState.ReadFailed, location.State);
        Assert.Equal((uint)2, location.Items.Single().NormalQuantity);
        Assert.Equal(Now, location.ObservedAtUtc);
    }

    [Fact]
    public async Task MembershipDoesNotInventCopiesAndDuplicateStacksRemainChecked()
    {
        Assert.False(InventoryPolicy.TryNormalize([new(100, 0, 0)], out _));
        Assert.False(InventoryPolicy.TryNormalize([new(0, 1, 0)], out _));
        Assert.False(InventoryPolicy.TryNormalize([new(1000100, 1, 0)], out _));
        Assert.True(InventoryPolicy.TryNormalize([new(100, uint.MaxValue, 0)], out var maximum));
        Assert.Equal(uint.MaxValue, maximum.Single().NormalQuantity);
        await using var service = new InventoryService();
        service.SetCharacter(1);
        var armoire = new InventoryLocationKey(InventoryLocationKind.Armoire);
        service.AcceptLocation(1, armoire, "Armoire", [new(100, 0, 0), new(100, 0, 0)], Now);
        var location = At(service.GetSnapshot()!, armoire);
        Assert.False(location.SupportsQuantity);
        Assert.True(location.HasObservation);
        Assert.Single(location.Items);
    }

    [Fact]
    public async Task IdentityChangesWithdrawImmediatelyAndRejectLateObservations()
    {
        await using var service = new InventoryService();
        service.SetCharacter(1);
        service.AcceptLocation(1, Bag, "Inventory", [new(100, 2, 0)], Now);
        service.SetCharacter(2);
        Assert.False(service.AcceptLocation(1, Bag, "Inventory", [], Now));
        Assert.All(service.GetSnapshot()!.Locations, location => Assert.False(location.HasObservation));
        service.SetCharacter(0);
        Assert.Null(service.GetSnapshot());
        Assert.False(service.ObserveRoster(1, []));
        Assert.False(service.AcceptLocation(0, Bag, "Inventory", [], Now));
    }

    [Fact]
    public async Task RepeatedUnchangedReadsAreThrottledButTransitionsAndMinuteRefreshPublish()
    {
        await using var service = new InventoryService();
        service.SetCharacter(1);
        service.AcceptLocation(1, Bag, "Inventory", [], Now);
        var before = service.GetSnapshot();
        Assert.False(service.AcceptLocation(1, Bag, "Inventory", [], Now.AddSeconds(59)));
        Assert.Same(before, service.GetSnapshot());
        Assert.True(service.AcceptLocation(1, Bag, "Inventory", [], Now.AddMinutes(1)));
        Assert.True(service.MarkUnavailable(1, Bag, "Inventory", Now.AddMinutes(2), false));
        Assert.False(service.MarkUnavailable(1, Bag, "Inventory", Now.AddMinutes(3), false));
        Assert.True(service.AcceptLocation(1, Bag, "Inventory", [], Now.AddMinutes(2)));
        Assert.False(service.AcceptLocation(1, Bag, "Inventory", [new(100, 1, 0)], Now));
    }

    [Fact]
    public async Task LateRestorePreservesNewCaptureAndMarksSavedRetainersRetained()
    {
        var cache = new ControlledCache();
        await using var service = new InventoryService(cache);
        service.SetCharacter(1);
        service.Pump();
        await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.AcceptLocation(1, Bag, "Inventory", [], Now.AddSeconds(1));
        cache.Loaded.SetResult(Saved(1));
        await PumpUntil(service, () => service.GetSnapshot()!.Locations.Any(location => location.Key == First));
        Assert.Empty(At(service.GetSnapshot()!, Bag).Items);
        Assert.Equal(InventoryObservationState.Observed, At(service.GetSnapshot()!, Bag).State);
        Assert.Equal(InventoryObservationState.Retained, At(service.GetSnapshot()!, First).State);
        Assert.False(service.GetSnapshot()!.RetainerRosterKnown);
        Assert.Equal(Now, At(service.GetSnapshot()!, First).ObservedAtUtc);
    }

    [Fact]
    public async Task LateRestoreCannotPublishAfterCharacterChangeOrDisposal()
    {
        var cache = new ControlledCache();
        var service = new InventoryService(cache);
        service.SetCharacter(1);
        service.Pump();
        await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.SetCharacter(2);
        cache.Loaded.SetResult(Saved(1));
        await PumpUntil(service, () => cache.LoadCount >= 2);
        Assert.Equal((ulong)2, service.GetSnapshot()!.ContentId);
        Assert.All(service.GetSnapshot()!.Locations, location => Assert.False(location.HasObservation));
        await service.DisposeAsync();
        service.Pump();
        service.SetCharacter(1);
        Assert.Null(service.GetSnapshot());
        await service.DisposeAsync();
    }

    [Fact]
    public async Task ShutdownOwnsUncooperativeWorkerUntilCompletionAndFlushesNewestSnapshot()
    {
        var cache = new BlockingWriteCache();
        var service = new InventoryService(cache);
        service.SetCharacter(1);
        service.Pump();
        service.AcceptLocation(1, Bag, "Inventory", [new(100, 1, 0)], Now);
        await PumpUntil(service, () => cache.Started.Task.IsCompleted);
        service.AcceptLocation(1, Bag, "Inventory", [], Now.AddSeconds(1));
        var shutdown = service.DisposeAsync().AsTask();
        Assert.Null(service.GetSnapshot());
        Assert.False(shutdown.IsCompleted);
        Assert.False(service.AcceptLocation(1, Bag, "Inventory", [new(200, 1, 0)], Now.AddSeconds(2)));
        cache.Release.SetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, cache.Writes.Count);
        Assert.Empty(At(cache.Writes.Last(), Bag).Items);
        Assert.Equal(1, cache.MaximumActive);
    }

    [Fact]
    public async Task ReadFailureIsVisibleWithoutDiscardingFreshLiveObservation()
    {
        var cache = new FailingCache();
        await using var service = new InventoryService(cache);
        service.SetCharacter(1);
        service.AcceptLocation(1, Bag, "Inventory", [new(100, 1, 0)], Now);
        service.Pump();
        await PumpUntil(service, () => service.LastCacheFailure is not null);
        Assert.Equal("InventoryCacheUnavailable", service.LastCacheFailure);
        Assert.Single(At(service.GetSnapshot()!, Bag).Items);
    }

    [Fact]
    public async Task FailedWriteRetriesLatestSnapshotAfterBackoffWithoutMoreCaptures()
    {
        var cache = new RetryCache();
        var clock = new ManualClock();
        await using var service = new InventoryService(cache, clock);
        service.SetCharacter(1);
        service.AcceptLocation(1, Bag, "Inventory", [new(100, 1, 0)], Now);
        service.Pump();
        await PumpUntil(service, () => service.LastCacheFailure is not null);
        Assert.Equal(1, cache.Attempts);
        service.AcceptLocation(1, Bag, "Inventory", [], Now.AddSeconds(1));
        for (var index = 0; index < 100; index++) service.Pump();
        Assert.Equal(1, cache.Attempts);
        clock.Now = Now.AddSeconds(29);
        service.Pump();
        Assert.Equal(1, cache.Attempts);
        clock.Now = Now.AddSeconds(30);
        await PumpUntil(service, () => cache.Saved is not null && service.LastCacheFailure is null);
        Assert.Equal(2, cache.Attempts);
        Assert.Empty(At(cache.Saved!, Bag).Items);
    }

    [Fact]
    public async Task PumpReturnsWhileTheOwnedCacheReadIsStillPending()
    {
        var cache = new ControlledCache();
        var service = new InventoryService(cache);
        Task? pump = null;
        try
        {
            service.SetCharacter(1);
            service.Pump();
            await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            pump = Task.Factory.StartNew(service.Pump, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await pump.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(cache.Loaded.Task.IsCompleted);
            Assert.Equal(1, cache.LoadCount);
            Assert.Null(service.LastCacheFailure);
        }
        finally
        {
            cache.Loaded.TrySetResult(null);
            if (pump is not null) await pump.WaitAsync(TimeSpan.FromSeconds(5));
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task PumpAfterDisposalDoesNotStartThePendingInitialRestore()
    {
        var cache = new ControlledCache();
        var service = new InventoryService(cache);
        service.SetCharacter(1);
        await service.DisposeAsync();
        service.Pump();
        // Check ownership synchronously: a queued task can fault before reaching the cache.
        var workerField = typeof(InventoryService).GetField("worker",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(workerField);
        Assert.Null(workerField.GetValue(service));
        Assert.Equal(0, cache.LoadCount);
        Assert.Null(service.GetSnapshot());
        Assert.Null(service.LastCacheFailure);
    }

    [Fact]
    public async Task CharacterCapturesWithoutACacheNeverReportAFullCacheQueue()
    {
        await using var service = new InventoryService();
        for (ulong id = 1; id <= 17; id++)
        {
            service.SetCharacter(id);
            Assert.True(service.AcceptLocation(id, Bag, "Inventory", [], Now));
            service.Pump();
            Assert.Null(service.LastCacheFailure);
        }
        Assert.Equal((ulong)17, service.GetSnapshot()!.ContentId);
    }

    private static InventoryLocationSnapshot At(InventorySnapshot snapshot, InventoryLocationKey key) => snapshot.Locations.Single(location => location.Key == key);
    internal static InventorySnapshot Saved(ulong contentId) => new(contentId, 2, true,
        [new(Bag, "Inventory", InventoryObservationState.Observed, Now, Now, [new(100, 2, 0)]),
         new(First, "First", InventoryObservationState.Observed, Now, Now, [new(200, 1, 1)])]);
    private static async Task PumpUntil(InventoryService service, Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) { timeout.Token.ThrowIfCancellationRequested(); service.Pump(); await Task.Delay(1, timeout.Token); }
    }

    private sealed class ControlledCache : IInventoryCache
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<InventorySnapshot?> Loaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int LoadCount;
        public Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref LoadCount);
            Started.TrySetResult();
            return contentId == 1 ? Loaded.Task : Task.FromResult<InventorySnapshot?>(null);
        }
        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingCache : IInventoryCache
    {
        public Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken) => Task.FromException<InventorySnapshot?>(new IOException());
        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken) => Task.FromException(new IOException());
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now = InventoryServiceTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RetryCache : IInventoryCache
    {
        public int Attempts;
        public InventorySnapshot? Saved;
        public Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken) => Task.FromResult<InventorySnapshot?>(null);
        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref Attempts) == 1) return Task.FromException(new IOException());
            Saved = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingWriteCache : IInventoryCache
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<InventorySnapshot> Writes { get; } = [];
        private int active;
        public int MaximumActive;
        public Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken) => Task.FromResult<InventorySnapshot?>(null);
        public async Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken)
        {
            MaximumActive = Math.Max(MaximumActive, Interlocked.Increment(ref active));
            Writes.Add(snapshot);
            Started.TrySetResult();
            await Release.Task;
            Interlocked.Decrement(ref active);
        }
    }
}

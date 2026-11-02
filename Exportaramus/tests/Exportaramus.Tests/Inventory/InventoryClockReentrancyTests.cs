using System.Reflection;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryClockReentrancyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalDuringTheClockCallbackCannotDispatchMoreCacheWork(bool pendingWrite)
    {
        var cache = new RecordingCache();
        var clock = new ReentrantClock();
        await using var service = new InventoryService(cache, clock);
        service.SetCharacter(1);
        if (pendingWrite)
        {
            service.Pump();
            await CurrentWorker(service);
            service.Pump();
            Assert.Equal(1, cache.LoadCalls);
            Assert.True(service.AcceptLocation(1, new(InventoryLocationKind.Inventory), "Inventory", [new(100, 1, 0)], Now));
        }
        Task? disposal = null;
        clock.Callback = () => disposal = service.DisposeAsync().AsTask();
        var failure = Record.Exception(service.Pump);
        await (disposal ?? Task.CompletedTask);
        // Observe the actual dispatched worker so an erroneous background dispatch cannot race the assertion.
        await CurrentWorker(service);
        Assert.Null(failure);
        Assert.NotNull(disposal);
        Assert.Null(service.GetSnapshot());
        Assert.Equal(pendingWrite ? 1 : 0, cache.LoadCalls);
        Assert.Equal(pendingWrite ? 1 : 0, cache.SaveCalls);
    }

    [Theory]
    [InlineData(false, "identity")]
    [InlineData(true, "identity")]
    [InlineData(false, "nested")]
    [InlineData(true, "nested")]
    [InlineData(false, "collected")]
    [InlineData(true, "collected")]
    public async Task AClockCallbackCannotDispatchAStaleOrDuplicateOperation(bool pendingWrite, string callbackKind)
    {
        var cache = new RecordingCache();
        var clock = new ReentrantClock();
        await using var service = new InventoryService(cache, clock);
        service.SetCharacter(1);
        if (pendingWrite)
        {
            service.Pump();
            await CurrentWorker(service);
            service.Pump();
            Assert.True(service.AcceptLocation(1, new(InventoryLocationKind.Inventory), "Inventory", [new(100, 1, 0)], Now));
        }
        clock.Callback = () =>
        {
            if (callbackKind == "identity") service.SetCharacter(2);
            else
            {
                service.Pump();
                if (callbackKind == "collected")
                {
                    CurrentWorker(service).GetAwaiter().GetResult();
                    service.Pump();
                }
            }
        };
        service.Pump();
        await CurrentWorker(service);
        if (callbackKind == "identity")
        {
            Assert.Equal(pendingWrite ? 1 : 0, cache.LoadCalls);
            Assert.Equal(0, cache.SaveCalls);
            Assert.Equal(2ul, service.GetSnapshot()!.ContentId);
            service.Pump();
            await CurrentWorker(service);
            Assert.Equal(2ul, cache.LastLoaded);
        }
        else
        {
            service.Pump();
            Assert.Equal(1, cache.LoadCalls);
            Assert.Equal(pendingWrite ? 1 : 0, cache.SaveCalls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RosterTimestampCallbacksCannotPublishIntoAChangedScope(bool dispose)
    {
        var clock = new ReentrantClock();
        await using var service = new InventoryService(timeProvider: clock);
        service.SetCharacter(1);
        clock.Callback = () => { if (dispose) service.DisposeAsync().GetAwaiter().GetResult(); else service.SetCharacter(2); };
        Assert.False(service.ObserveRoster(1, []));
        if (dispose) Assert.Null(service.GetSnapshot());
        else
        {
            Assert.Equal(2ul, service.GetSnapshot()!.ContentId);
            Assert.False(service.GetSnapshot()!.RetainerRosterKnown);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailureClockCallbacksCannotLoseOwnedWorkOrReviveDisposedWork(bool pendingWrite, bool dispose)
    {
        var cache = new RecordingCache { RestoreSecondCharacter = true };
        var clock = new ReentrantClock();
        await using var service = new InventoryService(cache, clock);
        service.SetCharacter(1);
        if (pendingWrite)
        {
            service.Pump();
            await CurrentWorker(service);
            service.Pump();
            Assert.True(service.AcceptLocation(1, new(InventoryLocationKind.Inventory), "Inventory", [new(100, 1, 0)], Now));
            cache.FailNextSave = true;
        }
        else cache.FailNextLoad = true;
        service.Pump();
        var failedWorker = CurrentWorker(service);
        await Assert.ThrowsAsync<IOException>(() => failedWorker);
        Task? disposal = null;
        clock.Callback = () =>
        {
            if (dispose) disposal = service.DisposeAsync().AsTask();
            else
            {
                service.SetCharacter(2);
                service.Pump();
                var nested = CurrentWorker(service);
                if (!ReferenceEquals(nested, failedWorker)) nested.GetAwaiter().GetResult();
            }
        };
        service.Pump();
        await (disposal ?? Task.CompletedTask);
        await CurrentWorker(service);
        if (dispose)
        {
            Assert.Null(service.GetSnapshot());
            Assert.Equal(1, cache.LoadCalls);
            Assert.Equal(pendingWrite ? 2 : 0, cache.SaveCalls);
        }
        else
        {
            service.Pump();
            Assert.Equal(2, cache.LoadCalls);
            Assert.Equal(2ul, service.GetSnapshot()!.ContentId);
            Assert.Contains(service.GetSnapshot()!.Locations, location => location.HasObservation);
        }
    }

    private static Task CurrentWorker(InventoryService service) =>
        (Task?)typeof(InventoryService).GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service) ?? Task.CompletedTask;

    private sealed class ReentrantClock : TimeProvider
    {
        public Action? Callback
        {
            get; set;
        }
        public override DateTimeOffset GetUtcNow()
        {
            var callback = Callback;
            Callback = null;
            callback?.Invoke();
            return Now;
        }
    }

    private sealed class RecordingCache : IInventoryCache
    {
        public int LoadCalls;
        public int SaveCalls;
        public ulong LastLoaded;
        public bool FailNextLoad;
        public bool FailNextSave;
        public bool RestoreSecondCharacter;
        public Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken)
        {
            LastLoaded = contentId;
            Interlocked.Increment(ref LoadCalls);
            if (FailNextLoad) { FailNextLoad = false; return Task.FromException<InventorySnapshot?>(new IOException("Controlled read failure.")); }
            return Task.FromResult<InventorySnapshot?>(RestoreSecondCharacter && contentId == 2 ? InventoryServiceTests.Saved(2) : null);
        }
        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            Interlocked.Increment(ref SaveCalls);
            if (FailNextSave) { FailNextSave = false; return Task.FromException(new IOException("Controlled write failure.")); }
            return Task.CompletedTask;
        }
    }
}

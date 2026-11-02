using System.Reflection;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryRemainingBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RosterClockCannotPublishAcrossAVisitToTheSameCharacter()
    {
        var clock = new Clock();
        await using var service = new InventoryService(timeProvider: clock);
        service.SetCharacter(1);
        clock.Callback = () => { service.SetCharacter(2); service.SetCharacter(1); };
        Assert.False(service.ObserveRoster(1, []));
        Assert.False(service.GetSnapshot()!.RetainerRosterKnown);
    }

    [Fact]
    public async Task IdlePumpDoesNotInvokeTheClock()
    {
        var clock = new Clock();
        await using var service = new InventoryService(new Cache(), clock);
        service.SetCharacter(1);
        service.Pump();
        await Worker(service);
        service.Pump();
        clock.Calls = 0;
        service.Pump();
        Assert.Equal(0, clock.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldReadFailuresCannotScheduleRetriesForANewVisit(bool changeDuringClock)
    {
        var cache = new Cache { HoldRead = true };
        var clock = new Clock();
        await using var service = new InventoryService(cache, clock);
        service.SetCharacter(1);
        service.Pump();
        var worker = Worker(service);
        cache.Read.SetException(new IOException("Read failed."));
        await Assert.ThrowsAsync<IOException>(() => worker);
        if (changeDuringClock) clock.Callback = () => { service.SetCharacter(2); service.SetCharacter(1); };
        else { service.SetCharacter(2); service.SetCharacter(1); }
        clock.Calls = 0;
        service.Pump();
        Assert.Null(service.LastCacheFailure);
        // A stale failure must not call the retry clock before the one fresh restore dispatch.
        Assert.Equal(changeDuringClock ? 2 : 1, clock.Calls);
        cache.HoldRead = false;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CacheSaveContinuationDoesNotCaptureACacheSuppliedContext(bool shutdown)
    {
        var cache = new Cache();
        var service = new InventoryService(cache);
        try
        {
            service.SetCharacter(1);
            service.Pump();
            await Worker(service);
            service.Pump();
            Assert.True(service.AcceptLocation(1, new(InventoryLocationKind.Inventory), "Inventory", [new(100, 1, 0)], Now));
            cache.HoldSave = true;
            Task operation;
            if (shutdown) operation = service.DisposeAsync().AsTask();
            else { service.Pump(); operation = Worker(service); }
            await Task.WhenAny(cache.SaveStarted.Task, operation).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(cache.SaveStarted.Task.IsCompletedSuccessfully);
            cache.Save.SetResult();
            await operation;
            Assert.Equal(0, cache.Context.Posts);
            if (!shutdown) { service.Pump(); await service.DisposeAsync(); }
        }
        finally
        {
            cache.HoldRead = false;
            cache.Read.TrySetResult(null);
            cache.Save.TrySetResult();
            await service.DisposeAsync();
        }
    }

    private static Task Worker(InventoryService service) => Assert.IsAssignableFrom<Task>(typeof(InventoryService).GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service));
    private sealed class Clock : TimeProvider
    {
        public int Calls;
        public Action? Callback;
        public override DateTimeOffset GetUtcNow()
        {
            Calls++; var action = Callback; Callback = null; action?.Invoke(); return Now;
        }
    }
    private sealed class Context : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref Posts); ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }
    private sealed class Cache : IInventoryCache
    {
        public bool HoldRead;
        public bool HoldSave;
        public TaskCompletionSource<InventorySnapshot?> Read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Save = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SaveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Context Context = new();
        public Task<InventorySnapshot?> LoadAsync(ulong id, CancellationToken token) => HoldRead ? Read.Task : Task.FromResult<InventorySnapshot?>(null);
        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken token)
        {
            if (!HoldSave) return Task.CompletedTask;
            SynchronizationContext.SetSynchronizationContext(Context);
            SaveStarted.TrySetResult();
            return Save.Task;
        }
    }
}

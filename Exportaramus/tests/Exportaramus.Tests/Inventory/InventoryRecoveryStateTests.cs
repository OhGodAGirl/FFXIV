using System.Reflection;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryRecoveryStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FailedReadSetsThePublicFailureState()
    {
        var cache = new HeldReadCache();
        await using var service = new InventoryService(cache);
        service.SetCharacter(1);
        service.Pump();
        await cache.Started.Task;
        cache.First.SetException(new IOException("The current read failed."));
        await Assert.ThrowsAsync<IOException>(() => Worker(service));
        service.Pump();
        Assert.Equal("InventoryCacheUnavailable", service.LastCacheFailure);
    }

    [Fact]
    public async Task RecoveredWriteClearsThePublicFailureState()
    {
        var cache = new RetryCache();
        var clock = new ManualClock();
        await using var service = new InventoryService(cache, clock);
        service.SetCharacter(1);
        Assert.True(service.AcceptLocation(1, new(InventoryLocationKind.Inventory), "Inventory", [], Now));
        service.Pump();
        await Worker(service);
        service.Pump();
        var failedWrite = Worker(service);
        await Assert.ThrowsAsync<IOException>(() => failedWrite);
        service.Pump();
        Assert.Equal("InventoryCacheUnavailable", service.LastCacheFailure);
        clock.Now = Now.AddSeconds(30);
        service.Pump();
        await Worker(service);
        service.Pump();
        Assert.Equal(2, cache.Attempts);
        Assert.Null(service.LastCacheFailure);
    }

    [Fact]
    public async Task FailedReadFromAnEarlierVisitDoesNotPoisonTheCurrentVisit()
    {
        var cache = new HeldReadCache();
        await using var service = new InventoryService(cache);
        service.SetCharacter(1);
        service.Pump();
        await cache.Started.Task;
        service.SetCharacter(2);
        service.SetCharacter(1);
        cache.First.SetException(new IOException("The earlier visit failed."));
        await Assert.ThrowsAsync<IOException>(() => Worker(service));
        service.Pump();
        Assert.Equal(1ul, service.GetSnapshot()!.ContentId);
        Assert.Null(service.LastCacheFailure);
    }

    // Await the actual owned task so assertions do not depend on a polling deadline.
    private static Task Worker(InventoryService service) => Assert.IsAssignableFrom<Task>(
        typeof(InventoryService).GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service));

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now = InventoryRecoveryStateTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RetryCache : IInventoryCache
    {
        public int Attempts;
        public Task<InventorySnapshot?> LoadAsync(ulong id, CancellationToken token) => Task.FromResult<InventorySnapshot?>(null);
        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken token) => Interlocked.Increment(ref Attempts) == 1
            ? Task.FromException(new IOException("The first write failed.")) : Task.CompletedTask;
    }

    private sealed class HeldReadCache : IInventoryCache
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<InventorySnapshot?> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int reads;
        public Task<InventorySnapshot?> LoadAsync(ulong id, CancellationToken token)
        {
            Started.TrySetResult();
            return Interlocked.Increment(ref reads) == 1 ? First.Task : Task.FromResult<InventorySnapshot?>(null);
        }
        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken token) => Task.CompletedTask;
    }
}

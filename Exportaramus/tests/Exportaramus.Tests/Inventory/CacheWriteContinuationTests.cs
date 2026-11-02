using System.Collections.Immutable;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class CacheWriteContinuationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(50000)]
    public async Task NewCacheWriteDoesNotDispatchToCallingContext(int count)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "cache-write-context-" + Guid.NewGuid().ToString("N"));
        try
        {
            var snapshot = InventoryServiceTests.Saved(1);
            var location = snapshot.Locations[0] with { Items = Enumerable.Range(1, count).Select(id => new InventoryItemQuantity((uint)id, 1, 0)).ToImmutableArray() };
            snapshot = snapshot with { Locations = snapshot.Locations.SetItem(0, location) };
            var cache = new InventoryFileCache(directory);
            var context = new CountingContext();
            var previous = SynchronizationContext.Current;
            Task operation;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                operation = cache.SaveAsync(snapshot, default);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            await operation;
            Assert.Equal(0, context.PostCount);
            Assert.NotNull(await cache.LoadAsync(1, default));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class CountingContext : SynchronizationContext
    {
        public int PostCount;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref PostCount);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }
}

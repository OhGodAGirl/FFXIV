using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class CacheContinuationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CacheIoDoesNotDispatchToTheCallingContext(bool save)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "cache-continuation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var original = new InventoryFileCache(directory);
            var snapshot = InventoryServiceTests.Saved(1);
            await original.SaveAsync(snapshot, default);
            var stream = new DeferredReadStream(await File.ReadAllBytesAsync(original.CharacterPath(1)));
            var cache = new InventoryFileCache(directory, _ => stream);
            var context = new CountingContext();
            Task operation;
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                operation = save ? cache.SaveAsync(snapshot, default) : cache.LoadAsync(1, default);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            Assert.False(operation.IsCompleted);
            stream.AllowRead.SetResult();
            await operation;
            Assert.Equal(0, context.PostCount);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class DeferredReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public TaskCompletionSource AllowRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            await AllowRead.Task.ConfigureAwait(false);
            return await base.ReadAsync(buffer, token).ConfigureAwait(false);
        }
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

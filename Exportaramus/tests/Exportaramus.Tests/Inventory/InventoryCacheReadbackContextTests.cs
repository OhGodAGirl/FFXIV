using System.Reflection;
using Exportaramus.Features.Inventory;
namespace Exportaramus.Tests.Inventory;

public sealed class InventoryCacheReadbackContextTests
{
    [Fact]
    public async Task ReadbackContinuationDoesNotDispatchToAnAmbientContext()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "cache-readback-" + Guid.NewGuid().ToString("N"));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new CountingContext();
        byte[]? bytes = null;
        Task? operation = null;
        var previous = SynchronizationContext.Current;
        try
        {
            var snapshot = InventoryServiceTests.Saved(1);
            Func<string, CancellationToken, Task<byte[]>> read = (path, token) =>
            {
                token.ThrowIfCancellationRequested();
                bytes = File.ReadAllBytes(path);
                SynchronizationContext.SetSynchronizationContext(context);
                reached.TrySetResult();
                return gate.Task;
            };
            var cache = (InventoryFileCache)Activator.CreateInstance(typeof(InventoryFileCache), BindingFlags.Instance | BindingFlags.NonPublic, null, [directory, null, null, null, read], null)!;
            try { operation = cache.SaveAsync(snapshot, default); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            await Task.WhenAny(reached.Task, operation);
            Assert.True(reached.Task.IsCompletedSuccessfully);
            Assert.False(operation.IsCompleted);
            var continuation = typeof(Task).GetField("m_continuationObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.NotNull(continuation);
            while (continuation.GetValue(gate.Task) is null && !operation.IsCompleted) await Task.Yield();
            Assert.False(operation.IsCompleted);
            Assert.NotNull(continuation.GetValue(gate.Task));
            gate.SetResult(bytes!);
            await operation;
            Assert.Equal(0, context.Posts);
            Assert.NotNull(await new InventoryFileCache(directory).LoadAsync(1, default));
        }
        finally
        {
            gate.TrySetResult(bytes ?? []);
            if (operation is not null) try { await operation; } catch { }
            SynchronizationContext.SetSynchronizationContext(previous);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private sealed class CountingContext : SynchronizationContext
    {
        private int posts;
        public int Posts => Volatile.Read(ref posts);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref posts);
            ThreadPool.UnsafeQueueUserWorkItem(_ => callback(state), null);
        }
    }
}

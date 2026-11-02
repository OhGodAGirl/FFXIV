using System.Reflection;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryShutdownContextTests
{
    [Fact]
    public async Task ShutdownDoesNotPostOwnedWorkToAnAmbientContext()
    {
        var context = new RecordingContext();
        var marker = new AsyncLocal<bool>(change =>
        {
            if (!change.ThreadContextChanged) return;
            if (change.CurrentValue) SynchronizationContext.SetSynchronizationContext(context);
            else if (ReferenceEquals(SynchronizationContext.Current, context)) SynchronizationContext.SetSynchronizationContext(null);
        });
        var cache = new PendingCache();
        var service = new InventoryService(cache);
        try
        {
            service.SetCharacter(1);
            service.Pump();
            var worker = Assert.IsAssignableFrom<Task>(typeof(InventoryService)
                .GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!cache.Started.Task.IsCompleted && !worker.IsCompleted)
                await Task.Delay(1, timeout.Token);
            Assert.True(cache.Started.Task.IsCompletedSuccessfully);
            Task shutdown;
            marker.Value = true;
            try { shutdown = service.DisposeAsync().AsTask(); }
            finally { marker.Value = false; }
            // Release only after the owned task has an actual await continuation.
            // This avoids assuming how quickly the thread-pool work starts.
            var continuation = typeof(Task).GetField("m_continuationObject", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(continuation);
            while (continuation.GetValue(worker) is null)
            {
                Assert.False(shutdown.IsCompleted);
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(1, timeout.Token);
            }
            cache.Completion.SetResult(null);
            await shutdown;
            Assert.Equal(0, context.PostCount);
        }
        finally
        {
            marker.Value = false;
            cache.Completion.TrySetResult(null);
            await service.DisposeAsync();
        }
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        private int posts;
        public int PostCount => Volatile.Read(ref posts);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref posts);
            ThreadPool.UnsafeQueueUserWorkItem(_ => callback(state), null);
        }
    }

    private sealed class PendingCache : IInventoryCache
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<InventorySnapshot?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return Completion.Task;
        }
        public Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

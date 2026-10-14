using System.Collections.Concurrent;
using System.Text;
using Exportaramus.Bootstrap;

namespace Exportaramus.Tests.Snapshots;

public sealed class MutationConfigurationLifecycleTests
{
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Enqueue((callback, state));
        public void Drain() { while (callbacks.TryDequeue(out var next)) next.Callback(next.State); }
    }

    private static Task DisposeOnContext(ConfigurationWriteCoordinator coordinator, QueuedContext context)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            return coordinator.DisposeAsync().AsTask();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static async Task DrainAndObserve(QueuedContext context, params Task[] tasks)
    {
        var all = Task.WhenAll(tasks);
        for (var index = 0; index < 500 && !all.IsCompleted; index++)
        {
            context.Drain();
            await Task.Delay(10);
        }
        context.Drain();
        await all.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterTemporaryWritePreventsAtomicPublication(bool canonicalExists)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "configuration-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "configuration.json");
        var previous = ConfigurationFile.Serialize(false, "Fixture.Configuration");
        var candidate = ConfigurationFile.Serialize(true, "Fixture.Configuration");
        string? writtenTemporary = null;
        using var cancellation = new CancellationTokenSource();
        try
        {
            if (canonicalExists) await ConfigurationFile.WriteAsync(path, previous);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConfigurationFile.WriteAsync(path, candidate,
                async (temporary, json, encoding, token) =>
                {
                    await File.WriteAllTextAsync(temporary, json, encoding, token).ConfigureAwait(false);
                    writtenTemporary = temporary;
                    Assert.Equal(Path.GetFullPath(directory), Path.GetDirectoryName(Path.GetFullPath(temporary)));
                    Assert.NotEqual(Path.GetFullPath(path), Path.GetFullPath(temporary));
                    Assert.Equal(Encoding.UTF8.GetBytes(candidate), await File.ReadAllBytesAsync(temporary, token).ConfigureAwait(false));
                    cancellation.Cancel();
                }, cancellation.Token));
            Assert.NotNull(writtenTemporary);
            Assert.False(File.Exists(writtenTemporary));
            if (canonicalExists)
            {
                Assert.Equal(Encoding.UTF8.GetBytes(previous), await File.ReadAllBytesAsync(path));
                Assert.Equal([path], Directory.GetFiles(directory));
            }
            else
            {
                Assert.False(File.Exists(path));
                Assert.Empty(Directory.GetFiles(directory));
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RepeatedDisposalReturnsImmediatelyWhileTheFirstDrainIsInFlight()
    {
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new ConcurrentQueue<bool>();
        var coordinator = new ConfigurationWriteCoordinator(value =>
        {
            writes.Enqueue(value);
            return writes.Count == 1 ? active.Task : Task.CompletedTask;
        });
        Task first = Task.CompletedTask;
        Task repeated = Task.CompletedTask;
        try
        {
            coordinator.Request(true); coordinator.Update(); coordinator.Request(false);
            first = coordinator.DisposeAsync().AsTask();
            Assert.False(first.IsCompleted);
            repeated = coordinator.DisposeAsync().AsTask();
            Assert.True(repeated.IsCompleted);
            Assert.Equal([true], writes.ToArray());
            active.TrySetResult();
            await Task.WhenAll(first, repeated).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal([true, false], writes.ToArray());
        }
        finally
        {
            active.TrySetResult();
            await Task.WhenAll(first, repeated).WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ActiveWriteCompletionFlushesLatestValueWithoutPumpingTheDisposalCaller()
    {
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new ConcurrentQueue<bool>();
        var coordinator = new ConfigurationWriteCoordinator(value =>
        {
            writes.Enqueue(value);
            return writes.Count == 1 ? active.Task : Task.CompletedTask;
        });
        var context = new QueuedContext();
        Task shutdown = Task.CompletedTask;
        try
        {
            coordinator.Request(true); coordinator.Update(); coordinator.Request(false);
            shutdown = DisposeOnContext(coordinator, context);
            Assert.False(shutdown.IsCompleted);
            active.TrySetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal([true, false], writes.ToArray());
        }
        finally
        {
            active.TrySetResult();
            await DrainAndObserve(context, active.Task, shutdown);
            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task PendingWriteCompletionFinishesDisposalWithoutPumpingItsCaller()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new ConcurrentQueue<bool>();
        var coordinator = new ConfigurationWriteCoordinator(value => { writes.Enqueue(value); return pending.Task; });
        var context = new QueuedContext();
        Task shutdown = Task.CompletedTask;
        try
        {
            coordinator.Request(false);
            shutdown = DisposeOnContext(coordinator, context);
            Assert.Equal([false], writes.ToArray());
            Assert.False(shutdown.IsCompleted);
            pending.TrySetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            pending.TrySetResult();
            await DrainAndObserve(context, pending.Task, shutdown);
            await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}

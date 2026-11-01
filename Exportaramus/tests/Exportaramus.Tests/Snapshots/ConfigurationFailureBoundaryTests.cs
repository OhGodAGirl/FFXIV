using System.Collections.Concurrent;
using Exportaramus.Bootstrap;

namespace Exportaramus.Tests.Snapshots;

public sealed class ConfigurationFailureBoundaryTests
{
    [Fact]
    public async Task DisposalRetainsNewerPreferenceWhenTheActiveWriteFails()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<bool>();
        var coordinator = new ConfigurationWriteCoordinator(value =>
        {
            writes.Add(value);
            return writes.Count == 1 ? completion.Task : Task.CompletedTask;
        });
        coordinator.Request(true);
        coordinator.Update();
        coordinator.Request(false);
        var disposal = coordinator.DisposeAsync().AsTask();
        completion.SetException(new IOException("The active write failed."));
        await disposal;
        Assert.Equal([true, false], writes);
    }

    [Fact]
    public async Task TemporaryWriteCompletionPublishesWithoutPumpingTheCaller()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "configuration-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "configuration.json");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new RecordingContext();
        var prior = SynchronizationContext.Current;
        Task write;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            write = ConfigurationFile.WriteAsync(path, "{}", (temporary, json, encoding, _) =>
            {
                File.WriteAllText(temporary, json, encoding);
                return completion.Task;
            });
        }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
        try
        {
            completion.SetResult();
            var completed = await Task.WhenAny(write, context.Posted.Task);
            Assert.Same(write, completed);
            await write;
            Assert.Equal("{}", await File.ReadAllTextAsync(path));
        }
        finally
        {
            context.Drain();
            await write;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            callbacks.Enqueue((callback, state));
            Posted.TrySetResult();
        }
        public void Drain()
        {
            while (callbacks.TryDequeue(out var item)) item.Callback(item.State);
        }
    }
}

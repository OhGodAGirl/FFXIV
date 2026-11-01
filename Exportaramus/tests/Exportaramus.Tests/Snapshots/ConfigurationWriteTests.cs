using Exportaramus.Bootstrap;
namespace Exportaramus.Tests.Snapshots;

public sealed class ConfigurationWriteTests
{
    [Fact]
    public void CleanupContinuesAfterOneHostResourceThrows()
    {
        var disposed = new List<int>();
        LifecycleCleanup.Run(() => disposed.Add(1), () => throw new InvalidOperationException(), () => disposed.Add(3));
        Assert.Equal([1, 3], disposed);
        LifecycleCleanup.Run();
    }
    [Fact]
    public async Task AtomicConfigurationContainsOnlyVersionAndReduceMotionAndRetainsPriorOnFailure()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "configuration-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "configuration.json");
        try
        {
            var first = ConfigurationFile.Serialize(true, "Exportaramus.Bootstrap.Configuration, Exportaramus");
            await ConfigurationFile.WriteAsync(path, first);
            Assert.Equal(first, await File.ReadAllTextAsync(path));
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes(first), await File.ReadAllBytesAsync(path));
            using var parsed = System.Text.Json.JsonDocument.Parse(first);
            Assert.Equal(["$type", "Version", "ReduceMotion"], parsed.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.True(parsed.RootElement.GetProperty("ReduceMotion").GetBoolean());
            var next = ConfigurationFile.Serialize(false, "Exportaramus.Bootstrap.Configuration, Exportaramus");
            await ConfigurationFile.WriteAsync(path, next);
            Assert.Equal(next, await File.ReadAllTextAsync(path));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConfigurationFile.WriteAsync(path, first, cancelled.Token));
            Assert.Equal(next, await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
            var invalid = Path.Combine(folder, "directory.json");
            Directory.CreateDirectory(invalid);
            await Assert.ThrowsAnyAsync<IOException>(() => ConfigurationFile.WriteAsync(invalid, first));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact]
    public async Task CoalescesPendingChangesAndDrainsLatestOnDispose()
    {
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<bool>();
        var coordinator = new ConfigurationWriteCoordinator(value => { writes.Add(value); return writes.Count == 1 ? active.Task : Task.CompletedTask; });
        Task? update = null;
        try
        {
            coordinator.Update();
            coordinator.Request(true);
            coordinator.Update();
            coordinator.Request(true);
            coordinator.Request(false);
            update = Task.Run(coordinator.Update);
            await update.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal([true], writes);
            var shutdown = coordinator.DisposeAsync();
            Assert.False(shutdown.IsCompleted);
            coordinator.Request(true);
            update = Task.Run(coordinator.Update);
            await update.WaitAsync(TimeSpan.FromSeconds(2));
            active.SetResult();
            await shutdown;
            Assert.Equal([true, false], writes);
            await coordinator.DisposeAsync();
        }
        finally
        {
            active.TrySetResult();
            if (update is not null) await update.WaitAsync(TimeSpan.FromSeconds(2));
            await coordinator.DisposeAsync();
        }
    }
    [Fact]
    public async Task ObservesFailedWriteOnceAndAllowsLaterRequest()
    {
        var writes = 0;
        var clock = new ManualClock();
        var coordinator = new ConfigurationWriteCoordinator(_ => ++writes == 1 ? Task.FromException(new IOException()) : Task.CompletedTask, clock);
        coordinator.Request(true);
        coordinator.Update();
        Assert.Throws<IOException>(coordinator.Update);
        coordinator.Update();
        coordinator.Request(false);
        clock.Now = clock.Now.AddSeconds(30);
        coordinator.Update();
        coordinator.Update();
        Assert.Equal(2, writes);
        await coordinator.DisposeAsync();
    }
    [Fact]
    public async Task DisposalObservesActiveAndPendingFailures()
    {
        var coordinator = new ConfigurationWriteCoordinator(_ => Task.FromException(new IOException()));
        coordinator.Request(true);
        coordinator.Update();
        coordinator.Request(false);
        await coordinator.DisposeAsync();
        var unused = new ConfigurationWriteCoordinator(_ => Task.CompletedTask);
        await unused.DisposeAsync();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPreferenceRetriesWithoutAnotherRequest(bool synchronousFailure)
    {
        var writes = new List<bool>();
        var clock = new ManualClock();
        await using var coordinator = new ConfigurationWriteCoordinator(value =>
        {
            writes.Add(value);
            if (writes.Count != 1) return Task.CompletedTask;
            if (synchronousFailure) throw new IOException();
            return Task.FromException(new IOException());
        }, clock);
        coordinator.Request(true);
        if (!synchronousFailure) coordinator.Update();
        Assert.Throws<IOException>(coordinator.Update);
        coordinator.Update();
        clock.Now = clock.Now.AddSeconds(29);
        coordinator.Update();
        Assert.Equal([true], writes);
        clock.Now = clock.Now.AddSeconds(1);
        coordinator.Update();
        coordinator.Update();
        Assert.Equal([true, true], writes);
    }
    [Fact]
    public async Task NewerPreferenceWinsOverFailedActiveWriteAndDisposalRetriesOnce()
    {
        var writes = new List<bool>();
        var completion = new TaskCompletionSource();
        var coordinator = new ConfigurationWriteCoordinator(value =>
        {
            writes.Add(value);
            return writes.Count == 1 ? completion.Task : Task.CompletedTask;
        });
        coordinator.Request(true);
        coordinator.Update();
        coordinator.Request(false);
        completion.SetException(new IOException());
        Assert.Throws<IOException>(coordinator.Update);
        await coordinator.DisposeAsync();
        Assert.Equal([true, false], writes);
    }
    [Fact]
    public async Task DisposalRetriesAnUncollectedFailedWriteWithoutAnotherRequest()
    {
        var writes = new List<bool>();
        var coordinator = new ConfigurationWriteCoordinator(value =>
        {
            writes.Add(value);
            return writes.Count == 1 ? Task.FromException(new IOException()) : Task.CompletedTask;
        });
        coordinator.Request(true);
        coordinator.Update();
        await coordinator.DisposeAsync();
        Assert.Equal([true, true], writes);
    }
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-31T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class CapabilityWorkerLifetimeTests
{
    [Fact]
    public async Task IncompleteCapabilityWorkerNeverBlocksAnUpdateAndIsQueuedOnlyOnce()
    {
        using var hold = new Fixture();
        hold.Update();
        Assert.Equal(1, hold.QueueCount);
        Assert.True(hold.Controller.Busy);
        Assert.Empty(hold.Controller.Publication.GetCapabilities());
        var update = Task.Run(hold.Update);
        try
        {
            var finished = await Task.WhenAny(update, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(update, finished);
            await update;
            Assert.Equal(1, hold.QueueCount);
            Assert.Empty(hold.Controller.Publication.GetCapabilities());
        }
        finally
        {
            hold.Complete();
            await update.WaitAsync(TimeSpan.FromSeconds(5));
        }
        hold.Update();
        Assert.True(CollectionDataSemanticValidator.ValidateCapabilities(hold.Controller.Publication.GetCapabilities()).Success);
        using var capabilities = System.Text.Json.JsonDocument.Parse(hold.Controller.Publication.GetCapabilities());
        Assert.False(capabilities.RootElement.GetProperty("reportedOwnershipEvidence").GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task DisposalObservesOwnedCapabilityCompletionIncludingItsFailure()
    {
        using var hold = new Fixture();
        hold.Update();
        hold.Controller.Dispose();
        try
        {
            Assert.False(hold.Controller.ShutdownTask.IsCompleted);
            Assert.NotEqual("Completed", hold.Controller.DiagnosticTasks.Single(task => task.Name == "ControllerShutdown").State);
            Assert.Equal("ProviderStopping", hold.Controller.Reports.TryEnqueue("123", 100, "Fixture", 1, SnapshotFixture.Now));
        }
        finally { hold.Fail(); }
        await hold.Controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(hold.Controller.Publication.GetCapabilities());
        Assert.Empty(hold.Controller.Publication.GetSnapshot());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(AppContext.BaseDirectory, "capability-worker", Guid.NewGuid().ToString("N"));
        private readonly TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<string>? work;
        public int QueueCount { get; private set; }
        public SnapshotController Controller { get; }
        public Fixture()
        {
            Controller = new(new Port(), new(root), null, prepareCapabilities: factory =>
            {
                QueueCount++; work = factory; return completion.Task;
            });
        }
        public void Update() => Controller.Update(0, "", true, false, 0, SnapshotFixture.Now);
        public void Complete() { if (!completion.Task.IsCompleted) completion.SetResult(work!()); }
        public void Fail() { if (!completion.Task.IsCompleted) completion.SetException(new IOException("Controlled owned worker failure.")); }
        public void Dispose()
        {
            Complete(); Controller.Dispose(); Controller.ShutdownTask.GetAwaiter().GetResult();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private sealed class Port : ISnapshotCapturePort
    {
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong id, long generation, DateTimeOffset now) => throw new InvalidOperationException();
        public bool Advance() => throw new InvalidOperationException();
        public RawCaptureBatch? TakeResult() => throw new InvalidOperationException();
        public void ResetSession() { }
        public void Dispose() { }
    }
}



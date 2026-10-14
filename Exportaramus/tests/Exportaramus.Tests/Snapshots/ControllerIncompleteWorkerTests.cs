using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class ControllerIncompleteWorkerTests
{
    [Fact]
    public async Task UpdateReturnsWithoutConsumingOrWaitingForAnIncompleteRestorationWorker()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "incomplete-controller", Guid.NewGuid().ToString("N"));
        var files = new HeldRead();
        var storage = new AtomicSnapshotStorage(directory, files);
        Directory.CreateDirectory(Path.GetDirectoryName(storage.CharacterPath(123))!);
        await File.WriteAllTextAsync(storage.CharacterPath(123), "fixture");
        var controller = new SnapshotController(new Port(), storage, null, prepareCapabilities: work => Task.FromResult(work()));
        Task? update = null;
        try
        {
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var updateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            update = Task.Factory.StartNew(() => { updateEntered.SetResult(); controller.Update(123, "Fixture", false, false, 1, SnapshotFixture.Now); },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await updateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var finished = await Task.WhenAny(update, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(update, finished);
            await update;

            Assert.False(files.Release.Task.IsCompleted);
            Assert.True(controller.Busy);
            Assert.Equal("Working", controller.DiagnosticOperation(1).ControllerState);
            Assert.False(controller.Reports.Available);
            Assert.Null(controller.Publication.Current);
            Assert.Empty(controller.Publication.GetSnapshot());
            Assert.Null(controller.Error);
        }
        finally
        {
            // Release the source before joining a potentially regressed synchronous Update.
            files.Release.TrySetResult();
            try
            {
                if (update is not null) await update.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                try { controller.Dispose(); }
                finally { await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5)); }
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now) => throw new InvalidOperationException("No capture is expected during restoration.");
        public bool Advance() => false;
        public RawCaptureBatch? TakeResult() => null;
        public void ResetSession() { }
        public void Dispose() { }
    }

    private sealed class HeldRead : IAtomicFileOperations
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream OpenRead(string path) => new HeldStream(this);
        public void Replace(string source, string destination, string? backup) => throw new InvalidOperationException();
        public void Move(string source, string destination) => throw new InvalidOperationException();
        public void Delete(string path) => File.Delete(path);
        private sealed class HeldStream(HeldRead owner) : MemoryStream
        {
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                owner.Entered.TrySetResult();
                await owner.Release.Task.ConfigureAwait(false);
                return 0;
            }
        }
    }
}

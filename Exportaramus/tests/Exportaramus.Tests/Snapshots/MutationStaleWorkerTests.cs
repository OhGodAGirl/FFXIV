using System.Diagnostics;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class MutationStaleWorkerTests
{
    [Fact]
    public async Task FaultedOldWorkerIsObservedAfterIdentityInvalidationWithoutChangingCurrentErrorOrPublication()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/stale-worker-proof", Guid.NewGuid().ToString("N"));
        var files = new DelayedFailureFiles(); var storage = new AtomicSnapshotStorage(root, files); var port = new Port();
        Directory.CreateDirectory(Path.GetDirectoryName(storage.CharacterPath(1))!);
        await File.WriteAllTextAsync(storage.CharacterPath(1), "old scope");
        var controller = new SnapshotController(port, storage, null, prepareCapabilities: work => Task.FromResult(work()));
        Task? switching = null;
        try
        {
            controller.Update(1, "First", false, false, 0, SnapshotFixture.Now);
            controller.Update(1, "First", false, false, 0, SnapshotFixture.Now);
            await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Working", controller.DiagnosticOperation(0).ControllerState);

            var updateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            switching = Task.Factory.StartNew(() => { updateEntered.SetResult(); controller.Update(2, "Second", false, false, 1, SnapshotFixture.Now); },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await updateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var finished = await Task.WhenAny(switching, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(switching, finished);
            await switching;
            Assert.Equal(2ul, controller.ActiveContentId);
            Assert.False(files.Release.Task.IsCompleted);
            files.Release.SetResult();
            await Until(() => controller.DiagnosticTasks.Single(task => task.Name == "CollectionOutput").State == "Faulted");
            var preparationsBeforeRejection = port.Preparations;

            controller.Update(2, "Second", false, false, 1, SnapshotFixture.Now);

            Assert.True(controller.DiagnosticOperation(1).StaleCompletionRejected);
            Assert.Equal(0, controller.DiagnosticOperation(1).RetryCount);
            Assert.Null(controller.Error);
            Assert.Null(controller.Publication.Current);
            Assert.Empty(controller.Publication.GetSnapshot());
            Assert.False(controller.Reports.Available);
            Assert.Equal(preparationsBeforeRejection, port.Preparations);
            Assert.Equal("NotStarted", controller.DiagnosticTasks.Single(task => task.Name == "CollectionOutput").State);
            await Until(() => controller.Reports.Available, () => controller.Update(2, "Second", false, false, 1, SnapshotFixture.Now));
            Assert.Null(controller.Error); Assert.Null(controller.Publication.Current); Assert.Equal(2ul, controller.ActiveContentId);
        }
        finally
        {
            files.Release.TrySetResult();
            try
            {
                if (switching is not null) await switching.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                try { controller.Dispose(); }
                finally { await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5)); }
                Directory.Delete(root, true);
            }
        }
    }

    private static async Task Until(Func<bool> predicate, Action? advance = null)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Fixture did not reach its explicitly controlled state.");
            advance?.Invoke(); await Task.Delay(1);
        }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public int Preparations { get; private set; }
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public bool PrepareCatalog() { Preparations++; return true; }
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now) => throw new InvalidOperationException("No capture is expected.");
        public bool Advance() => false;
        public RawCaptureBatch? TakeResult() => null;
        public void ResetSession() { }
        public void Dispose() { }
    }

    private sealed class DelayedFailureFiles : IAtomicFileOperations
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream OpenRead(string path) => new DelayedFailureStream(this);
        public void Replace(string source, string destination, string? backup) => throw new InvalidOperationException("No write is expected.");
        public void Move(string source, string destination) => throw new InvalidOperationException("No write is expected.");
        public void Delete(string path) => File.Delete(path);
    }

    private sealed class DelayedFailureStream(DelayedFailureFiles owner) : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            owner.Entered.TrySetResult();
            await owner.Release.Task.ConfigureAwait(false);
            throw new InvalidOperationException("Old worker fixture failure.");
        }
    }
}

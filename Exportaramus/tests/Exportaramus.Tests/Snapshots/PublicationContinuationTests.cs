using System.Collections.Concurrent;
using System.Text;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class PublicationContinuationTests
{
    [Fact]
    public async Task PublicationDoesNotRequirePumpingTheWorkerContext()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "publish-continuation", Guid.NewGuid().ToString("N"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new RecordingContext();
        var scheduler = new Scheduler(context);
        var storage = new AtomicSnapshotStorage(directory, new GatedFiles(gate.Task));
        var controller = new SnapshotController(new Port(), storage, null, prepareCapabilities: work => Task.FromResult(work()), background: scheduler);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!controller.Reports.Available || controller.Busy)
            {
                controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
                await Task.Delay(1, timeout.Token);
            }
            controller.RequestFull();
            controller.Update(123, "Fixture", false, false, 5, SnapshotFixture.Now.AddSeconds(5));
            controller.Update(123, "Fixture", false, false, 5, SnapshotFixture.Now.AddSeconds(5));
            var work = Assert.IsAssignableFrom<Task>(scheduler.Last);
            Assert.False(work.IsCompleted);
            gate.SetResult();
            Assert.Same(work, await Task.WhenAny(work, context.Posted.Task));
            await work;
            controller.Update(123, "Fixture", false, false, 5, SnapshotFixture.Now.AddSeconds(5));
            Assert.NotNull(controller.Publication.Current);
            Assert.Null(controller.Error);
        }
        finally
        {
            gate.TrySetResult();
            context.Drain();
            if (scheduler.Last is { } work) await work;
            controller.Dispose();
            await controller.ShutdownTask;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private sealed class Scheduler(RecordingContext context) : IBackgroundWorkScheduler
    {
        public Task? Last
        {
            get; private set;
        }
        public Task<T> Run<T>(Func<Task<T>> work, CancellationToken cancellationToken)
        {
            var prior = SynchronizationContext.Current;
            try { SynchronizationContext.SetSynchronizationContext(context); var task = work(); Last = task; return task; }
            finally { SynchronizationContext.SetSynchronizationContext(prior); }
        }
    }
    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> queue = new();
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            queue.Enqueue((callback, state)); Posted.TrySetResult();
        }
        public void Drain()
        {
            while (queue.TryDequeue(out var item)) item.Callback(item.State);
        }
    }
    private sealed class GatedFiles(Task gate) : IAtomicFileOperations
    {
        public Stream OpenRead(string path) => File.OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options) => new GatedStream(path, mode, bufferSize, options, gate);
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Delete(string path) => File.Delete(path);
    }
    private sealed class GatedStream(string path, FileMode mode, int bufferSize, FileOptions options, Task gate)
        : FileStream(path, mode, FileAccess.Write, FileShare.None, bufferSize, options)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await gate.ConfigureAwait(false); await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
    }
    private sealed class Port : ISnapshotCapturePort
    {
        public CollectionCatalog? Catalog => SnapshotFixture.Create().Catalog;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
        {
        }
        public bool Advance() => true;
        public RawCaptureBatch? TakeResult()
        {
            var state = SnapshotFixture.Create().State;
            return new(state.Unlocks, state.CurrentItemPresence, state.Quests);
        }
        public void ResetSession()
        {
        }
        public void Dispose()
        {
        }
    }
}

using System.Collections.Concurrent;
using System.Text;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class RestorationContinuationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestorationDoesNotRequirePumpingTheWorkerContext(bool evidence)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "restore-continuation", Guid.NewGuid().ToString("N"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new RecordingContext();
        var scheduler = new Scheduler(context);
        var storage = new AtomicSnapshotStorage(directory, new GatedFiles(gate.Task));
        var path = evidence ? storage.EvidencePath(123) : storage.CharacterPath(123);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, evidence ? "{\"contentId\":\"123\",\"items\":[]}" : CollectionDataSerializer.Compact(SnapshotFixture.Create(), 1));
        var controller = new SnapshotController(new Port(), storage, null, prepareCapabilities: work => Task.FromResult(work()), background: scheduler);
        try
        {
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            var work = Assert.IsAssignableFrom<Task>(scheduler.Last);
            Assert.False(work.IsCompleted);
            gate.SetResult();
            Assert.Same(work, await Task.WhenAny(work, context.Posted.Task));
            await work;
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            Assert.True(controller.Reports.Available);
        }
        finally
        {
            gate.TrySetResult();
            context.Drain();
            if (scheduler.Last is { } work) await work;
            controller.Dispose();
            await controller.ShutdownTask;
            Directory.Delete(directory, true);
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
        public Stream OpenRead(string path) => new GatedStream(File.ReadAllBytes(path), gate);
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Delete(string path) => File.Delete(path);
    }
    private sealed class GatedStream(byte[] bytes, Task gate) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await gate.ConfigureAwait(false); cancellationToken.ThrowIfCancellationRequested(); return Read(buffer.Span);
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
        public RawCaptureBatch? TakeResult() => null;
        public void ResetSession()
        {
        }
        public void Dispose()
        {
        }
    }
}

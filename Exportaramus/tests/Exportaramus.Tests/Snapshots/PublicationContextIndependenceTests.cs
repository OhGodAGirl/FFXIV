using System.Collections.Concurrent;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
namespace Exportaramus.Tests.Snapshots;

public sealed class PublicationContextIndependenceTests
{
    [Theory]
    [InlineData("storage")]
    [InlineData("full")]
    [InlineData("lightweight")]
    public async Task BackgroundPersistenceDoesNotRequireTheCallingContextToPump(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "exportaramus-context-" + Guid.NewGuid().ToString("N"));
        var files = new GatedFiles();
        var storage = new Exportaramus.Features.Snapshots.AtomicSnapshotStorage(root, files);
        var publisher = new CollectionDataPublisher(storage, null);
        var data = SnapshotFixture.Create();
        if (mode == "lightweight") data = data with { State = data.State with { CaptureType = CaptureType.Lightweight } };
        var publication = mode == "storage" ? null : publisher.Prepare(data, "Fixture", 1, null, false, false, default);
        var context = new HeldContext();
        var previous = SynchronizationContext.Current;
        Task operation;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            operation = publication is null
                ? storage.CommitAsync(1, "{}", null, null, true, false, SnapshotFixture.Now, default)
                : publisher.PersistAsync(publication, default);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        try
        {
            files.Release.TrySetResult();
            var first = await Task.WhenAny(operation, context.Posted.Task).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(operation, first);
            await operation;
        }
        finally
        {
            context.Drain();
            await operation.WaitAsync(TimeSpan.FromSeconds(10));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class HeldContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state) { callbacks.Enqueue((callback, state)); Posted.TrySetResult(); }
        public void Drain() { while (callbacks.TryDequeue(out var call)) call.Callback(call.State); }
    }

    private sealed class GatedFiles : Exportaramus.Features.Snapshots.IAtomicFileOperations
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream OpenRead(string path) => File.OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options) =>
            new GatedStream(path, mode, bufferSize, options, Release.Task);
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Delete(string path) => File.Delete(path);
    }

    private sealed class GatedStream(string path, FileMode mode, int bufferSize, FileOptions options, Task gate)
        : FileStream(path, mode, FileAccess.Write, FileShare.None, bufferSize, options)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        { await gate.ConfigureAwait(false); await base.WriteAsync(buffer, token).ConfigureAwait(false); }
    }
}

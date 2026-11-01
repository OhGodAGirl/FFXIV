using System.Collections.Concurrent;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class PublisherContinuationTests
{
    [Fact]
    public async Task PublicationCompletesWithoutPumpingTheCaller()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "publisher-context-tests", Guid.NewGuid().ToString("N"));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new RecordingContext();
        var prior = SynchronizationContext.Current;
        Task<AcceptedSnapshotBundle> write;
        var storage = new AtomicSnapshotStorage(directory, new GatedFiles(completion.Task));
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            write = new CollectionDataPublisher(storage, null).PrepareAndPersistAsync(
                SnapshotFixture.Create(), "Test", 1, null, false, false, default);
        }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
        try
        {
            completion.SetResult();
            var completed = await Task.WhenAny(write, context.Posted.Task);
            Assert.Same(write, completed);
            var bundle = await write;
            Assert.Equal(bundle.RichJson, await File.ReadAllTextAsync(storage.CanonicalPath));
        }
        finally
        {
            context.Drain();
            await write;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class GatedFiles(Task gate) : IAtomicFileOperations
    {
        public Stream OpenRead(string path) => File.OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options) =>
            options.HasFlag(FileOptions.Asynchronous)
                ? new GatedWriteStream(path, mode, bufferSize, options, gate)
                : new FileStream(path, mode, FileAccess.Write, FileShare.None, bufferSize, options);
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Delete(string path) => File.Delete(path);
    }

    private sealed class GatedWriteStream(string path, FileMode mode, int bufferSize, FileOptions options, Task gate)
        : FileStream(path, mode, FileAccess.Write, FileShare.None, bufferSize, options)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await gate.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
        }
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Flush();
            return Task.CompletedTask;
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

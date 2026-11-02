using System.Collections.Concurrent;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class AtomicStorageContextTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "atomic-context", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PendingStorageIoDoesNotRequireTheCallingContext(int operationKind)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new HeldContext();
        var storage = new AtomicSnapshotStorage(root, new Files(gate.Task, operationKind == 2));
        Directory.CreateDirectory(Path.GetDirectoryName(storage.CharacterPath(1))!);
        File.WriteAllText(storage.CharacterPath(1), "{}");
        var prior = SynchronizationContext.Current;
        Task operation;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            operation = operationKind != 0
                ? storage.CommitAsync(1, "{}", null, null, false, false, DateTimeOffset.UnixEpoch, default)
                : storage.ReadAsync(1, false, default);
        }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
        try
        {
            Assert.False(operation.IsCompleted);
            gate.SetResult();
            await Task.WhenAny(operation, context.Posted.Task);
            Assert.True(operation.IsCompleted, "Storage I/O must finish without posting continuation work to the caller's context.");
            await operation;
            Assert.False(context.Posted.Task.IsCompleted);
        }
        finally
        {
            context.Release();
            gate.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class HeldContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> pending = new();
        private volatile bool released;
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            pending.Enqueue((callback, state));
            Posted.TrySetResult();
            if (released) Drain();
        }
        public void Release()
        {
            released = true; Drain();
        }
        private void Drain()
        {
            while (pending.TryDequeue(out var work))
                ThreadPool.QueueUserWorkItem(_ => work.Callback(work.State));
        }
    }

    private sealed class Files(Task gate, bool gateFlush) : IAtomicFileOperations
    {
        public Stream OpenRead(string path) => new ReadStream(File.ReadAllBytes(path), gate);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options) =>
            options.HasFlag(FileOptions.Asynchronous) ? new WriteStream(path, mode, bufferSize, options, gate, gateFlush)
                : new FileStream(path, mode, FileAccess.Write, FileShare.None, bufferSize, options);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Delete(string path) => File.Delete(path);
    }
    private sealed class ReadStream(byte[] bytes, Task gate) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await gate.ConfigureAwait(false);
            return Read(buffer.Span);
        }
    }
    private sealed class WriteStream(string path, FileMode mode, int size, FileOptions options, Task gate, bool gateFlush)
        : FileStream(path, mode, FileAccess.Write, FileShare.None, size, options)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!gateFlush) await gate.ConfigureAwait(false);
            Write(buffer.Span);
        }
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (gateFlush) await gate.ConfigureAwait(false);
            Flush();
        }
    }
    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

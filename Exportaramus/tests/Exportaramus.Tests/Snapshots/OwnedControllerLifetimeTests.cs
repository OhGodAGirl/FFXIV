using System.Collections.Concurrent;
using Microsoft.Win32.SafeHandles;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class OwnedControllerLifetimeTests
{
    [Fact]
    public async Task DisposalCancelsAndClosesTheDepartingScopeAndWaitsForItsUncooperativeRead()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "owned-controller", Guid.NewGuid().ToString("N"));
        var files = new HeldRead();
        var storage = new AtomicSnapshotStorage(directory, files);
        Directory.CreateDirectory(Path.GetDirectoryName(storage.CharacterPath(123))!);
        File.WriteAllText(storage.CharacterPath(123), "fixture");
        var controller = new SnapshotController(new Port(), storage, null, prepareCapabilities: work => Task.FromResult(work()));
        try
        {
            // First queue completed fixture capabilities, then start restoration exactly once.
            // Do not pump Update against a deliberately held read during fixture setup.
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            controller.Dispose();
            Assert.True(files.Token.IsCancellationRequested);
            Assert.True(files.Handle!.IsClosed);
            Assert.False(controller.ShutdownTask.IsCompleted);
            Assert.False(files.Release.Task.IsCompleted);
        }
        finally
        {
            files.Release.TrySetResult();
            controller.Dispose();
            await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ShutdownCanFinishWhenTheDepartingFrameworkContextNoLongerPumps()
    {
        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new SnapshotController(new Port { ShutdownTask = shutdown.Task },
            new(Path.Combine(AppContext.BaseDirectory, "unused-controller", Guid.NewGuid().ToString("N"))), null);
        var context = new HeldContext();
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            controller.Dispose();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        shutdown.SetResult();
        try
        {
            var completed = await Task.WhenAny(controller.ShutdownTask, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(controller.ShutdownTask, completed);
            await controller.ShutdownTask;
        }
        finally
        {
            context.Drain();
            await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class HeldContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Enqueue((callback, state));
        public void Drain()
        {
            for (var i = 0; i < 16 && callbacks.TryDequeue(out var call); i++) call.Callback(call.State);
        }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public Task ShutdownTask { get; init; } = Task.CompletedTask;
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now) => throw new InvalidOperationException();
        public bool Advance() => false;
        public RawCaptureBatch? TakeResult() => null;
        public void ResetSession() { }
        public void Dispose() { }
    }

    private sealed class HeldRead : IAtomicFileOperations
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public SafeWaitHandle? Handle { get; private set; }
        public Stream OpenRead(string path) => new HeldStream(this);
        public void Replace(string source, string destination, string? backup) => throw new InvalidOperationException();
        public void Move(string source, string destination) => throw new InvalidOperationException();
        public void Delete(string path) => File.Delete(path);
        private sealed class HeldStream(HeldRead owner) : MemoryStream
        {
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
            {
                owner.Token = token; owner.Handle = token.WaitHandle.SafeWaitHandle; owner.Entered.TrySetResult();
                await owner.Release.Task.ConfigureAwait(false);
                return 0;
            }
        }
    }
}

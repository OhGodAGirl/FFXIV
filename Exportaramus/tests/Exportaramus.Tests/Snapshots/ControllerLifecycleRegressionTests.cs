using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class ControllerLifecycleRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithdrawalWithoutSubscribersAndRepeatedDisposalAreSafe(bool logoutFirst)
    {
        var directory = NewDirectory();
        var controller = new SnapshotController(new Port(), new(directory), null,
            prepareCapabilities: work => Task.FromResult(work()));
        try
        {
            // Establish an active character without starting an asynchronous restoration.
            // Neither withdrawal event has subscribers in this fixture.
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            Assert.Equal(123ul, controller.ActiveContentId);
            if (logoutFirst)
            {
                var logoutError = Record.Exception(() =>
                    controller.Update(0, "", true, false, 1, SnapshotFixture.Now.AddSeconds(1)));
                Assert.Null(logoutError);
                Assert.Equal(0ul, controller.ActiveContentId);
                Assert.Empty(controller.Publication.GetSnapshot());
            }

            Assert.Null(Record.Exception(controller.Dispose));
            await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Disposed", controller.DiagnosticOperation(2).ControllerState);
            Assert.Empty(controller.Publication.GetSnapshot());

            // This is a contract assertion in the test body, not an incidental cleanup call.
            Assert.Null(Record.Exception(controller.Dispose));
            Assert.True(controller.ShutdownTask.IsCompletedSuccessfully);
            Assert.Equal("Disposed", controller.DiagnosticOperation(2).ControllerState);
        }
        finally
        {
            await FinishWithoutReplacingBodyFailure(controller);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task PendingRestorationDoesNotRepeatCatalogPreparation()
    {
        var directory = NewDirectory();
        var files = new HeldRead();
        var storage = new AtomicSnapshotStorage(directory, files);
        Directory.CreateDirectory(Path.GetDirectoryName(storage.CharacterPath(123))!);
        await File.WriteAllTextAsync(storage.CharacterPath(123), "fixture");
        var port = new Port();
        var controller = new SnapshotController(port, storage, null,
            prepareCapabilities: work => Task.FromResult(work()));
        try
        {
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(controller.Busy);
            var preparations = port.CatalogPreparations;

            controller.Update(123, "Fixture", false, false, 1, SnapshotFixture.Now.AddSeconds(1));

            Assert.False(files.Release.Task.IsCompleted);
            Assert.Equal(preparations, port.CatalogPreparations);
            Assert.Equal("Working", controller.DiagnosticOperation(1).ControllerState);
            Assert.Null(controller.Publication.Current);
            Assert.Null(controller.Error);
        }
        finally
        {
            files.Release.TrySetResult();
            await FinishWithoutReplacingBodyFailure(controller);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static string NewDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "controller-lifecycle-regression", Guid.NewGuid().ToString("N"));

    private static async Task FinishWithoutReplacingBodyFailure(SnapshotController controller)
    {
        // A mutation that breaks Dispose must remain attributed to the body assertion.
        // The held read is released by its caller before reaching this cleanup helper.
        try { controller.Dispose(); } catch (Exception) { }
        try { await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public int CatalogPreparations { get; private set; }
        public bool PrepareCatalog() { CatalogPreparations++; return true; }
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now) =>
            throw new InvalidOperationException("No native capture is requested by this fixture.");
        public bool Advance() => throw new InvalidOperationException("No native capture is pending.");
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

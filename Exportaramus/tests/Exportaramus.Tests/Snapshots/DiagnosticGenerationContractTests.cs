using System.Diagnostics;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class DiagnosticGenerationContractTests
{
    [Fact]
    public async Task LogoutAfterFirstRestorationKeepsPublicGenerationNonnegativeBeforeAnyCapture()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "diagnostic-generation", Guid.NewGuid().ToString("N"));
        var port = new Port();
        var controller = new SnapshotController(port, new AtomicSnapshotStorage(root), null);
        long? withdrawalGeneration = null;
        controller.Withdrawn += _ => withdrawalGeneration = controller.DiagnosticOperation(1).CaptureGeneration;
        try
        {
            var clock = Stopwatch.StartNew();
            while (!controller.Reports.Available || controller.Busy)
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(5)) Assert.Fail("Initial restoration did not complete: " + controller.Error);
                controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
                await Task.Delay(1);
            }
            Assert.Equal(123ul, controller.ActiveContentId);
            Assert.Equal(0, port.Captures);
            Assert.Equal(0, controller.DiagnosticOperation(0).CaptureGeneration);
            Assert.Null(controller.Publication.Current);

            controller.Update(0, "", true, false, 1, SnapshotFixture.Now.AddSeconds(1));

            Assert.Equal(0ul, controller.ActiveContentId);
            Assert.Equal(0, port.Captures);
            Assert.NotNull(withdrawalGeneration);
            // Diagnostics schema operations.captureGeneration is an integer with minimum zero.
            Assert.InRange(withdrawalGeneration.Value, 0, long.MaxValue);
            Assert.InRange(controller.DiagnosticOperation(1).CaptureGeneration, 0, long.MaxValue);
            Assert.Null(controller.Publication.Current);
            Assert.False(controller.Reports.Available);
        }
        finally
        {
            controller.Dispose();
            await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public int Captures { get; private set; }
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now) => Captures++;
        public bool Advance() => throw new InvalidOperationException("No capture is expected.");
        public RawCaptureBatch? TakeResult() => throw new InvalidOperationException("No capture is expected.");
        public void ResetSession() { }
        public void Dispose() { }
    }
}

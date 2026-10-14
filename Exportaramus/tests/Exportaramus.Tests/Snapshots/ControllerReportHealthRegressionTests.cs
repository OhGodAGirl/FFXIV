using System.Diagnostics;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class ControllerReportHealthRegressionTests
{
    [Fact]
    public async Task CatalogFailureAfterRestorationDoesNotDisableHealthyReportIntake()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "controller-report-health", Guid.NewGuid().ToString("N"));
        var port = new Port();
        var controller = new SnapshotController(port, new(directory), null,
            prepareCapabilities: work => Task.FromResult(work()));
        try
        {
            var setup = Stopwatch.StartNew();
            while (!controller.Reports.Available || controller.Busy)
            {
                Assert.True(setup.Elapsed < TimeSpan.FromSeconds(5), "Healthy restoration did not complete: " + controller.Error);
                controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
                Assert.Null(controller.Error);
                await Task.Delay(1);
            }
            Assert.Equal(123ul, controller.ActiveContentId);
            Assert.Null(controller.Publication.Current);
            Assert.True(controller.Reports.Available);

            // Keep monotonic time at0, before any scheduled capture. Only the next
            // catalog preparation fails; report restoration and storage succeeded.
            port.FailNextCatalogPreparation = true;
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);

            Assert.Equal(1, port.CatalogFailures);
            Assert.Equal("Synchronization failed. Try Sync Now again.", controller.Error);
            Assert.True(controller.Reports.Available);
            Assert.Equal("Accepted", controller.Reports.TryEnqueue("123", 100, "Fixture", 0, SnapshotFixture.Now));
            Assert.Equal(1, controller.Reports.Count);
        }
        finally
        {
            controller.Dispose();
            await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public bool FailNextCatalogPreparation { get; set; }
        public int CatalogFailures { get; private set; }

        public bool PrepareCatalog()
        {
            if (FailNextCatalogPreparation)
            {
                FailNextCatalogPreparation = false;
                CatalogFailures++;
                throw new IOException("Controlled catalog preparation failure.");
            }
            return true;
        }

        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now) =>
            throw new InvalidOperationException("No capture is scheduled or requested by this fixture.");
        public bool Advance() => throw new InvalidOperationException("No capture is pending.");
        public RawCaptureBatch? TakeResult() => null;
        public void ResetSession() { }
        public void Dispose() { }
    }
}

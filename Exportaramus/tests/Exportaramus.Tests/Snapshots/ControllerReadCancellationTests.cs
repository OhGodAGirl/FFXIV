using System.Collections.Immutable;
using System.Diagnostics;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class ControllerReadCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringAnActiveReadEndsCaptureAndPreservesTheLastPublication(bool previouslyPublished)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "read-cancellation", Guid.NewGuid().ToString("N"));
        var port = new Port();
        var controller = new SnapshotController(port, new(root), null);
        var notices = new List<string>();
        controller.Changed += notices.Add;
        try
        {
            await Pump(controller, 0, () => controller.Reports.Available && !controller.Busy);
            if (previouslyPublished)
            {
                controller.RequestFull();
                await Pump(controller, 1, () => controller.Publication.Current is not null);
            }
            var previous = controller.Publication.Current;
            var noticeCount = notices.Count;
            port.CancelNextAdvance = true;
            controller.RequestFull();
            Update(controller, 2);
            Assert.Equal("Capturing", controller.DiagnosticOperation(2).ControllerState);

            Update(controller, 3);

            Assert.False(controller.Busy);
            Assert.Equal("Idle", controller.DiagnosticOperation(3).ControllerState);
            Assert.Null(controller.Error);
            Assert.Same(previous, controller.Publication.Current);
            Assert.Equal(noticeCount, notices.Count);
            Assert.Equal(previouslyPublished ? 1 : 0, port.CompletedReads);
            Assert.Equal(0, controller.DiagnosticOperation(3).RetryCount);
            controller.RequestPresence();
            await Pump(controller, 4, () => controller.Publication.Current?.Revision == (previous?.Revision ?? 0) + 1);
            Assert.Equal(CaptureType.Lightweight, controller.Publication.Current!.Data.State.CaptureType);
            Assert.False(controller.Busy);
            Assert.Null(controller.Error);
            Assert.Equal(previouslyPublished ? new[] { true, true, false } : new[] { true, false }, port.Begins);
        }
        finally
        {
            controller.Dispose();
            await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Update(SnapshotController controller, double now) =>
        controller.Update(123, "Fixture", false, false, now, SnapshotFixture.Now.AddSeconds(now));
    private static async Task Pump(SnapshotController controller, double now, Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) Assert.Fail("Controller did not reach the expected public state: " + controller.Error);
            Update(controller, now);
            await Task.Delay(1);
        }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public List<bool> Begins { get; } = [];
        public bool CancelNextAdvance { get; set; }
        public int CompletedReads { get; private set; }
        private RawCaptureBatch? result;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
        {
            Begins.Add(full);
            var empty = CollectionStateFactory.Empty(contentId, Guid.NewGuid(), now, generation);
            result = new(full ? empty.Unlocks : ImmutableDictionary<string, PermanentUnlockState>.Empty, empty.CurrentItemPresence, []);
        }
        public bool Advance()
        {
            if (CancelNextAdvance) { CancelNextAdvance = false; throw new OperationCanceledException("Controlled active-read cancellation."); }
            CompletedReads++;
            return true;
        }
        public RawCaptureBatch? TakeResult() => result;
        public void ResetSession() => result = null;
        public void Dispose() { }
    }
}


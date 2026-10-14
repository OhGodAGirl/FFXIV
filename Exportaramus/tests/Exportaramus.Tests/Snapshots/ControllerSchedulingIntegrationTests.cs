using System.Collections.Immutable;
using System.Diagnostics;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class ControllerSchedulingIntegrationTests
{
    [Fact]
    public async Task AutomaticDeadlinesStartAtAcceptanceAfterTheReadAndWorkerHaveCompleted()
    {
        await using var run = new Harness();
        await run.Restore(0);
        run.Controller.RequestFull();
        run.Update(5);
        Assert.True(run.Controller.Busy);
        Assert.Single(run.Port.Begins);
        run.Update(100);
        var wait = Stopwatch.StartNew();
        while (run.Controller.DiagnosticTasks.Single(task => task.Name == "CollectionOutput").State != "Completed")
        {
            if (wait.Elapsed > TimeSpan.FromSeconds(5)) Assert.Fail("Owned output worker did not complete.");
            await Task.Delay(1);
        }
        Assert.Null(run.Controller.Publication.Current);
        await run.Accept(200, 1, CaptureType.Full);

        run.AssertNoCapture(305, 1);
        run.AssertNoCapture(400, 1);
        run.AssertNoCapture(499.999, 1);
        await run.Accept(500, 2, CaptureType.Lightweight);
        run.Controller.RequestPresence(); await run.Accept(1800, 3, CaptureType.Lightweight);
        run.AssertNoCapture(1999.999, 3);
        await run.Accept(2000, 4, CaptureType.Full);
        Assert.Equal(new double[] { 5, 500, 1800, 2000 }, run.Port.Begins.Select(value => value.Time));
        Assert.Equal(new[] { true, false, false, true }, run.Port.Begins.Select(value => value.Full));
    }

    [Fact]
    public async Task AcceptedFullCaptureResetsBothAutomaticDeadlines()
    {
        await using var run = new Harness();
        await run.Restore(0);
        run.Controller.RequestFull(); await run.Accept(5, 1, CaptureType.Full);
        run.AssertNoCapture(15, 1);
        run.AssertNoCapture(304.999, 1);
        await run.Accept(305, 2, CaptureType.Lightweight);
        Assert.Equal(new[] { true, false }, run.Port.Begins.Select(value => value.Full));

        run.Controller.RequestPresence(); await run.Accept(1600, 3, CaptureType.Lightweight);
        run.AssertNoCapture(1804.999, 3);
        await run.Accept(1805, 4, CaptureType.Full);
        Assert.Equal(new[] { true, false, false, true }, run.Port.Begins.Select(value => value.Full));
        Assert.Equal(new double[] { 5, 305, 1600, 1805 }, run.Port.Begins.Select(value => value.Time));
    }

    [Fact]
    public async Task AcceptedLightweightCaptureMovesOnlyThePresenceDeadline()
    {
        await using var run = new Harness();
        await run.Restore(0);
        run.Controller.RequestFull(); await run.Accept(5, 1, CaptureType.Full);
        run.Controller.RequestPresence(); await run.Accept(100, 2, CaptureType.Lightweight);
        run.AssertNoCapture(305, 2);
        run.AssertNoCapture(399.999, 2);
        await run.Accept(400, 3, CaptureType.Lightweight);
        run.Controller.RequestPresence(); await run.Accept(1700, 4, CaptureType.Lightweight);

        run.AssertNoCapture(1804.999, 4);
        await run.Accept(1805, 5, CaptureType.Full);

        Assert.Equal(new[] { true, false, false, false, true }, run.Port.Begins.Select(value => value.Full));
        Assert.Equal(new double[] { 5, 100, 400, 1700, 1805 }, run.Port.Begins.Select(value => value.Time));
    }

    [Fact]
    public async Task AcceptedReportsMoveNeitherAutomaticDeadline()
    {
        await using var run = new Harness();
        await run.Restore(0);
        run.Controller.RequestFull(); await run.Accept(5, 1, CaptureType.Full);
        await run.Report(100, 2);
        Assert.Single(run.Port.Begins);
        run.AssertNoCapture(304.999, 1);
        await run.Accept(305, 3, CaptureType.Lightweight);
        run.Controller.RequestPresence(); await run.Accept(1700, 4, CaptureType.Lightweight);
        await run.Report(1750, 5);

        run.AssertNoCapture(1804.999, 3);
        await run.Accept(1805, 6, CaptureType.Full);

        Assert.Equal(new[] { true, false, false, true }, run.Port.Begins.Select(value => value.Full));
        Assert.Equal(new double[] { 5, 305, 1700, 1805 }, run.Port.Begins.Select(value => value.Time));
        Assert.Equal(6, run.Controller.Publication.Current!.Revision);
    }

    [Fact]
    public async Task RestorationDoesNotResetPresenceCadenceWhenTheInitialFullCaptureNeedsRetries()
    {
        await using var run = new Harness();
        await run.Storage.CommitAsync(123, CollectionDataSerializer.Compact(SnapshotFixture.Create(), 8), null, null,
            false, false, SnapshotFixture.Now, default);
        run.Update(0); // Establish login's original due times before asynchronous restoration completes.
        await run.Restore(10);
        Assert.Equal(CaptureType.Restored, run.Controller.Publication.Current!.Data.State.CaptureType);
        Assert.Equal(1, run.Controller.Publication.Current.Revision);
        Assert.Empty(run.Port.Begins);
        run.Port.FullFailures = 3;

        run.AssertNoCapture(14.999, 0);
        run.Update(15); Assert.Single(run.Port.Begins);
        run.AssertNoCapture(44.999, 1);
        run.Update(45); Assert.Equal(2, run.Port.Begins.Count);
        run.AssertNoCapture(164.999, 2);
        run.Update(165); Assert.Equal(3, run.Port.Begins.Count);
        run.AssertNoCapture(310, 3); // Treating restoration at10 as a capture incorrectly makes presence due here.
        run.AssertNoCapture(314.999, 3);
        await run.Accept(315, 2, CaptureType.Lightweight);

        Assert.Equal(new[] { true, true, true, false }, run.Port.Begins.Select(value => value.Full));
        Assert.Equal(new double[] { 15, 45, 165, 315 }, run.Port.Begins.Select(value => value.Time));
    }

    internal sealed class Harness : IAsyncDisposable
    {
        private readonly string root = Path.Combine(AppContext.BaseDirectory, "controller-schedule", Guid.NewGuid().ToString("N"));
        public Port Port { get; } = new();
        public AtomicSnapshotStorage Storage { get; }
        public SnapshotController Controller { get; }
        public Harness(Exportaramus.Features.Diagnostics.DiagnosticMeasurements? measurements = null) { Storage = new(root); Controller = new(Port, Storage, null, measurements); }
        public void Update(double now) => Controller.Update(123, "Fixture", false, false, now, SnapshotFixture.Now.AddSeconds(now));
        public async Task Restore(double now) => await Pump(now, () => Controller.Reports.Available && !Controller.Busy);
        public async Task Accept(double now, long revision, CaptureType type)
        {
            await Pump(now, () => Controller.Publication.Current?.Revision >= revision);
            Assert.Equal(revision, Controller.Publication.Current!.Revision);
            Assert.Equal(type, Controller.Publication.Current.Data.State.CaptureType);
            Assert.False(Controller.Busy);
            Assert.Null(Controller.Error);
        }
        public async Task Report(double now, long revision)
        {
            Assert.Equal("Accepted", Controller.Reports.TryEnqueue("123", 100, "Schedule fixture", now, SnapshotFixture.Now.AddSeconds(now)));
            await Accept(now, revision, CaptureType.Reported);
        }
        public void AssertNoCapture(double now, int count)
        {
            var revision = Controller.Publication.Current?.Revision;
            Update(now);
            Assert.Equal(count, Port.Begins.Count);
            Assert.Equal(revision, Controller.Publication.Current?.Revision);
            Assert.False(Controller.Busy);
        }
        private async Task Pump(double now, Func<bool> finished)
        {
            var clock = Stopwatch.StartNew();
            while (!finished())
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(5)) Assert.Fail("Controller did not reach its public completion state: " + Controller.Error);
                ControllerPumpStep.Advance(Controller, () => Update(now)); await Task.Delay(1);
            }
        }
        public async ValueTask DisposeAsync()
        {
            Controller.Dispose(); await Controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    internal sealed class Port : ISnapshotCapturePort
    {
        public List<(bool Full, double Time)> Begins { get; } = [];
        public int FullFailures { get; set; }
        public RawCaptureBatch? SuppliedBatch { get; set; }
        public bool CatalogReady { get; set; } = true;
        public bool AdvanceReady { get; set; } = true;
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        private RawCaptureBatch? result;
        public bool PrepareCatalog() => CatalogReady;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
        {
            Begins.Add((full, (now - SnapshotFixture.Now).TotalSeconds));
            if (full && FullFailures > 0) { FullFailures--; throw new IOException("Controlled full capture failure."); }
            var state = CollectionStateFactory.Empty(contentId, Guid.NewGuid(), now, generation);
            result = SuppliedBatch ?? new(full ? state.Unlocks : ImmutableDictionary<string, PermanentUnlockState>.Empty, state.CurrentItemPresence, []);
        }
        public bool Advance() => AdvanceReady;
        public RawCaptureBatch? TakeResult() => result;
        public void ResetSession() => result = null;
        public void Dispose() { }
    }
}

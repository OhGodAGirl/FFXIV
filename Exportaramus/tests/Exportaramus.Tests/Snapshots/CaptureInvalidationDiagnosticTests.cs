using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class CaptureInvalidationDiagnosticTests
{
    [Fact]
    public async Task InvalidatedCaptureRetainsItsFailureDiagnosticBeforeRedaction()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "targeted-scheduling-" + Guid.NewGuid().ToString("N"));
        var port = new Port();
        var controller = new SnapshotController(port, new(path), null);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!controller.Reports.Available || controller.Busy)
            {
                controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
                await Task.Delay(1, timeout.Token);
            }
            controller.RequestFull();
            controller.Update(123, "Fixture", false, false, 5, SnapshotFixture.Now.AddSeconds(5));
            while (controller.Busy)
            {
                await Task.Delay(1, timeout.Token);
                controller.Update(123, "Fixture", false, false, 5, SnapshotFixture.Now.AddSeconds(5));
            }
            Assert.NotNull(controller.Publication.Current);
            port.Invalidated = true;
            controller.RequestFull();
            controller.Update(123, "Fixture", false, false, 20, SnapshotFixture.Now.AddSeconds(20));
            var thread = Environment.CurrentManagedThreadId;
            var failures = new List<Exception>();
            EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> observe = (_, args) =>
            {
                if (Environment.CurrentManagedThreadId == thread && args.Exception is InvalidDataException)
                    failures.Add(args.Exception);
            };
            AppDomain.CurrentDomain.FirstChanceException += observe;
            try { controller.Update(123, "Fixture", false, false, 20.01, SnapshotFixture.Now.AddSeconds(20.01)); }
            finally { AppDomain.CurrentDomain.FirstChanceException -= observe; }
            Assert.Contains(failures, failure => failure.Message.Contains("Capture", StringComparison.Ordinal)
                && failure.Message.Contains("invalidated", StringComparison.Ordinal));
            Assert.NotNull(controller.Error);
        }
        finally
        {
            controller.Dispose();
            await controller.ShutdownTask;
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public bool Enabled
        {
            get; set;
        }
        public bool Invalidated;
        public List<ImmutableArray<string>> Targeted { get; } = [];
        public CollectionCatalog? Catalog => SnapshotFixture.Create().Catalog;
        public bool SupportsTargetedCapture => Enabled;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
        {
        }
        public void BeginTargeted(ImmutableArray<string> sources, ulong contentId, long generation, DateTimeOffset now) => Targeted.Add(sources);
        public bool Advance() => true;
        public RawCaptureBatch? TakeResult()
        {
            if (Invalidated) return null;
            var state = SnapshotFixture.Create().State;
            return new(state.Unlocks, state.CurrentItemPresence, state.Quests);
        }
        public void ResetSession()
        {
        }
        public void Dispose()
        {
        }
    }
}


using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class TargetedSchedulingTests
{
    [Fact]
    public async Task NonemptyHintStartsExactlyTheRequestedTargetedCapture()
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
            port.Enabled = true;
            controller.HintSource("orchestrions", 123, 20);
            controller.Update(123, "Fixture", false, false, 20.25, SnapshotFixture.Now.AddSeconds(20.25));
            Assert.Contains("orchestrions", Assert.Single(port.Targeted));
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
        public bool Enabled;
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

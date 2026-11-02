using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class ReentrantRestorationTests
{
    [Fact]
    public async Task ReentrantSessionResetRestoresTheNewCharacterBeforeStartingCapture()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "reentrant-restore-" + Guid.NewGuid().ToString("N"));
        var port = new Port();
        var controller = new SnapshotController(port, new(directory), null, prepareCapabilities: work => Task.FromResult(work()), background: new Scheduler());
        try
        {
            for (var i = 0; i < 4; i++) controller.Update(123, "Original", false, false, 0, SnapshotFixture.Now);
            Assert.True(controller.Reports.Available);
            Assert.Equal(0, port.BeginCount);
            var reentered = false;
            port.OnReset = () =>
            {
                if (reentered) return;
                reentered = true;
                controller.RequestFull();
                controller.Update(456, "Other", false, false, 1, SnapshotFixture.Now.AddSeconds(1));
                Assert.Equal(0, port.BeginCount);
            };
            controller.Update(456, "Other", false, false, 1, SnapshotFixture.Now.AddSeconds(1));
            Assert.True(reentered);
        }
        finally
        {
            port.OnReset = null;
            controller.Dispose();
            await controller.ShutdownTask;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private sealed class Scheduler : IBackgroundWorkScheduler
    {
        public Task<T> Run<T>(Func<Task<T>> work, CancellationToken cancellationToken) => Task.FromResult(Task.Run(work, cancellationToken).GetAwaiter().GetResult());
    }
    private sealed class Port : ISnapshotCapturePort
    {
        public bool Enabled = false;
        public int BeginCount;
        public Action? OnReset;
        public List<ImmutableArray<string>> Targeted { get; } = [];
        public CollectionCatalog? Catalog => SnapshotFixture.Create().Catalog;
        public bool SupportsTargetedCapture => Enabled;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
        {
            BeginCount++;
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
            OnReset?.Invoke();
        }
        public void Dispose()
        {
        }
    }
}

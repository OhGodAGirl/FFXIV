using System.Collections.Immutable;
using System.Diagnostics;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class AdapterControllerMutationTests
{
    [Fact]
    public async Task FullCaptureReplacesQuestProgressAndLightweightCaptureRetainsIt()
    {
        var root = Root(); var port = new Port(); var controller = new SnapshotController(port, new AtomicSnapshotStorage(root), null);
        try
        {
            await Pump(controller, () => controller.Reports.Available, 0);
            controller.RequestFull(); await Pump(controller, () => controller.Publication.Current is not null, 1);
            Assert.Equal(new QuestState(1, QuestStatus.InProgress, 7), Assert.Single(controller.Publication.Current!.Data.State.Quests));
            Assert.Equal(QuestStatus.InProgress, Assert.Single(controller.Publication.Current.Data.Collections["quests"].Items).Status);
            var revision = controller.Publication.Current.Revision;

            port.Sequence = 9; controller.RequestPresence();
            await Pump(controller, () => controller.Publication.Current!.Revision > revision, 2);
            Assert.Equal(CaptureType.Lightweight, controller.Publication.Current!.Data.State.CaptureType);
            Assert.Equal((byte)7, Assert.Single(controller.Publication.Current.Data.State.Quests).Sequence);
            revision = controller.Publication.Current.Revision;

            controller.RequestFull(); await Pump(controller, () => controller.Publication.Current!.Revision > revision, 3);
            Assert.Equal(new QuestState(1, QuestStatus.InProgress, 9), Assert.Single(controller.Publication.Current!.Data.State.Quests));
            Assert.Equal((byte)9, Assert.Single(controller.Publication.Current.Data.Collections["quests"].Items).Sequence);
        }
        finally { controller.Dispose(); await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5)); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CurrentTrustedSourcesRestoreTheirMappedOwnershipWithoutAReadAndRebaseTheSession()
    {
        var root = Root(); var fixture = SnapshotFixture.Create(); var storage = new AtomicSnapshotStorage(root);
        await storage.CommitAsync(123, CollectionDataSerializer.Compact(fixture, 81), null, null, false, false, SnapshotFixture.Now, default);
        var port = new Port { TrustedSourceKeys = ImmutableHashSet.Create("mounts", "inventory") };
        var controller = new SnapshotController(port, storage, null);
        try
        {
            await Pump(controller, () => controller.Publication.Current is not null, 0);
            var restored = controller.Publication.Current!.Data.State;
            Assert.Equal(new uint[] { 1 }, restored.Unlocks["mounts"].UnlockedIds);
            Assert.Equal(fixture.State.CurrentItemPresence["inventory"].Items.ToArray(), restored.CurrentItemPresence["inventory"].Items.ToArray());
            Assert.Equal(SourceStatus.Stale, restored.Unlocks["mounts"].Health.SourceStatus);
            Assert.Equal(ProvenanceType.Restored, restored.Unlocks["mounts"].Health.ProvenanceType);
            Assert.Equal(CapabilityState.Supported, restored.Unlocks["mounts"].Health.CapabilityState);
            Assert.Equal(SnapshotFixture.Now, restored.Unlocks["mounts"].Health.ObservedAtUtc);
            Assert.Equal(controller.DiagnosticOperation(0).CaptureGeneration, restored.Unlocks["mounts"].Health.CaptureGeneration);
            Assert.Equal(controller.Session, restored.ProviderSessionId);
            Assert.NotEqual(fixture.State.ProviderSessionId, restored.ProviderSessionId);
            Assert.Empty(restored.Unlocks["minions"].UnlockedIds);
            Assert.Empty(restored.Quests);
            Assert.Equal(0, port.Captures);
        }
        finally { controller.Dispose(); await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5)); Directory.Delete(root, true); }
    }

    private static string Root() => Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/adapter-controller-decisions", Guid.NewGuid().ToString("N"));

    private static async Task Pump(SnapshotController controller, Func<bool> completed, double monotonic)
    {
        var timer = Stopwatch.StartNew();
        while (!completed())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(5)) Assert.Fail("Controller did not complete: " + controller.Error);
            controller.Update(123, "Fixture", false, false, monotonic, SnapshotFixture.Now.AddSeconds(monotonic));
            await Task.Delay(1);
        }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public ImmutableHashSet<string> TrustedSourceKeys { get; init; } = [];
        public byte Sequence { get; set; } = 7;
        public int Captures { get; private set; }
        private RawCaptureBatch? result;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
        {
            Captures++;
            var state = CollectionStateFactory.Empty(contentId, Guid.NewGuid(), now, generation);
            var unlocks = full ? state.Unlocks.SetItem("quests", new([], CaptureReadPolicy.Health(DataType.PermanentUnlock, generation, now, ReasonCode.None)))
                : ImmutableDictionary<string, PermanentUnlockState>.Empty;
            result = new(unlocks, state.CurrentItemPresence, full ? [new(1, QuestStatus.InProgress, Sequence)] : []);
        }
        public bool Advance() => true;
        public RawCaptureBatch? TakeResult() => result;
        public void ResetSession() => result = null;
        public void Dispose() { }
    }
}

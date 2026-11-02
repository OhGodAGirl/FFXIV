using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class QuestInvalidationTests
{
    [Fact]
    public async Task UnusableQuestMappingClearsPreviouslyAcceptedQuestDetails()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "quest-invalidation-" + Guid.NewGuid().ToString("N"));
        var port = new Port();
        var scheduler = new Scheduler();
        var controller = new SnapshotController(port, new(directory), null, prepareCapabilities: work => Task.FromResult(work()), background: scheduler);
        try
        {
            for (var i = 0; i < 4; i++) controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            controller.RequestFull();
            for (var i = 0; i < 3; i++) controller.Update(123, "Fixture", false, false, 1, SnapshotFixture.Now.AddSeconds(1));
            Assert.Single(controller.Publication.Current!.Data.State.Quests);
            port.Unusable = true;
            controller.RequestFull();
            for (var i = 0; i < 3; i++) controller.Update(123, "Fixture", false, false, 2, SnapshotFixture.Now.AddSeconds(2));
            Assert.Null(scheduler.Failure);
            Assert.Null(controller.Error);
            Assert.False(controller.Publication.Current!.Data.State.Unlocks["quests"].Health.IsUsable);
            Assert.Empty(controller.Publication.Current.Data.State.Quests);
        }
        finally
        {
            controller.Dispose();
            await controller.ShutdownTask;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private sealed class Scheduler : IBackgroundWorkScheduler
    {
        public Exception? Failure;
        public Task<T> Run<T>(Func<Task<T>> work, CancellationToken cancellationToken)
        {
            try { return Task.FromResult(Task.Run(work, cancellationToken).GetAwaiter().GetResult()); } catch (Exception ex) { Failure = ex; throw; }
        }
    }
    private sealed class Port : ISnapshotCapturePort
    {
        public bool Enabled = false;
        public bool Unusable;
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
            var unlocks = state.Unlocks;
            if (Unusable) unlocks = unlocks.SetItem("quests", new([], SnapshotFixture.Health(DataType.PermanentUnlock, SourceStatus.Unavailable) with { CapabilityState = CapabilityState.UnavailablePendingValidation, ReasonCode = ReasonCode.MappingValidationFailed, Reason = "Quest mapping is invalid." }));
            return new(unlocks, state.CurrentItemPresence, state.Quests);
        }
        public void ResetSession()
        {
        }
        public void Dispose()
        {
        }
    }
}

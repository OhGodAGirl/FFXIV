using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class ReentrantCharacterNameTests
{
    [Fact]
    public async Task WithdrawalReturningToOriginalCharacterRetainsThatCharactersName()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "reentrant-name-" + Guid.NewGuid().ToString("N"));
        var controller = new SnapshotController(new Port(), new(directory), null, prepareCapabilities: work => Task.FromResult(work()), background: new Scheduler());
        try
        {
            for (var i = 0; i < 4; i++) controller.Update(123, "Original", false, false, 0, SnapshotFixture.Now);
            Assert.True(controller.Reports.Available);
            var reentered = false;
            controller.Withdrawn += _ =>
            {
                if (reentered) return;
                reentered = true;
                controller.Update(123, "Original", false, false, 1, SnapshotFixture.Now);
                controller.Update(123, "Original", false, false, 1, SnapshotFixture.Now);
                controller.RequestFull();
                controller.Update(123, "Original", false, false, 1, SnapshotFixture.Now);
            };
            controller.Update(456, "Other", false, false, 1, SnapshotFixture.Now);
            controller.Update(123, "Original", false, false, 1, SnapshotFixture.Now);
            Assert.True(reentered);
            Assert.Equal(123ul, controller.ActiveContentId);
            var bundle = Assert.IsType<Exportaramus.Features.CollectionData.Publication.AcceptedSnapshotBundle>(controller.Publication.Current);
            using var rich = JsonDocument.Parse(Assert.IsType<string>(bundle.RichJson));
            Assert.Equal("Original", rich.RootElement.GetProperty("character").GetProperty("displayName").GetString());
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
        public Task<T> Run<T>(Func<Task<T>> work, CancellationToken cancellationToken) => Task.FromResult(Task.Run(work, cancellationToken).GetAwaiter().GetResult());
    }
    private sealed class Port : ISnapshotCapturePort
    {
        public bool Enabled = false;
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

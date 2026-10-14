using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Capture;

public sealed class CollectionReadLifetimeRegressionTests
{
    [Fact]
    public void RetainedDisposedReadReleasesItsUnpublishedQuestRows()
    {
        var reader = new Reader();
        var definition = new CollectionDefinition("quests", "Quests", DataType.PermanentUnlock, true,
            [new(1, 1, "First", false), new(2, 2, "Second", false)]);
        var catalog = new CollectionCatalog("English", "fixture",
            ImmutableDictionary<string, CollectionDefinition>.Empty.Add("quests", definition), []);
        using var operation = new CollectionReadOperation(reader, catalog, true, 123, 1,
            DateTimeOffset.Parse("2026-09-07T00:00:00Z"));

        var pendingRow = ReadOneUnpublishedRow(operation, reader);
        operation.Dispose();

        // Match the repository's existing departing-cache lifetime test: collection
        // is explicit, while the disposed owner deliberately remains strongly rooted.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(pendingRow.TryGetTarget(out _));
        Assert.Null(operation.Result);
        GC.KeepAlive(operation);
        GC.KeepAlive(reader);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<QuestState> ReadOneUnpublishedRow(CollectionReadOperation operation, Reader reader)
    {
        Assert.False(operation.Advance(1));
        Assert.Equal(1, reader.QuestReads);
        Assert.Null(operation.Result);
        var row = Assert.IsType<WeakReference<QuestState>>(reader.PendingRow);
        Assert.True(row.TryGetTarget(out _));
        return row;
    }

    private sealed class Reader : ICollectionReader
    {
        public int QuestReads { get; private set; }
        public WeakReference<QuestState>? PendingRow { get; private set; }
        public bool IdentityMatches(ulong contentId) => contentId == 123;
        public bool IsAvailable(string key, string version) => true;
        public bool ReadUnlock(string key, CatalogItem row) => true;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public QuestState ReadQuest(uint id, bool completed)
        {
            QuestReads++;
            var row = new QuestState(id, QuestStatus.Completed, null);
            PendingRow = new(row);
            return row;
        }

        public CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog,
            ulong contentId, long generation, DateTimeOffset now) =>
            throw new InvalidOperationException("Only the first quest row is requested by this fixture.");
    }
}

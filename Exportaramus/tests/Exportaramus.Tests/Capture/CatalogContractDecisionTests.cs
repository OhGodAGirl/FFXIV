using System.Collections.Concurrent;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Capture;

public sealed class CatalogContractDecisionTests
{
    [Fact]
    public void UnnamedGameItemsRemainRepresentableWithFallbackDisplayMetadata()
    {
        var entry = CatalogRowPolicy.Project(new("@items", 99, null));
        Assert.Equal("@items", entry.Key);
        Assert.Equal(99u, entry.Item.Id); Assert.Equal(99u, entry.Item.SourceRowId);
        Assert.Equal("Entry 99", entry.Item.Name); Assert.True(entry.Item.IsFallbackName);
    }

    [Fact]
    public void BuiltCatalogExposesTheApprovedCollectionNamesKindsAndTotalAvailability()
    {
        var names = new Dictionary<string, string>
        {
            ["mounts"] = "Mounts",
            ["minions"] = "Minions",
            ["emotes"] = "Emotes",
            ["bardings"] = "Bardings",
            ["cards"] = "Triple Triad Cards",
            ["npcs"] = "Triple Triad NPCs",
            ["fashions"] = "Fashion Accessories",
            ["facewear"] = "Facewear",
            ["beasts"] = "Beasts",
            ["frames"] = "Portrait Frames",
            ["occult_records"] = "Occult Records",
            ["orchestrions"] = "Orchestrion Rolls",
            ["spells"] = "Blue Magic Spells",
            ["aetherytes"] = "Aetherytes",
            ["chocobo_routes"] = "Chocobo Routes",
            ["leves"] = "Levequests",
            ["hairstyles"] = "Hairstyles",
            ["field_records"] = "Field Records",
            ["survey_records"] = "Variant Dungeon Records",
            ["quests"] = "Quests",
            ["achievements"] = "Achievements",
            ["armoires"] = "Armoire",
            ["glamour_dresser"] = "Glamour Dresser",
            ["outfits"] = "Outfits",
            ["relics"] = "Relics"
        };
        var catalog = CollectionCatalogBuilderPolicy.Build(new("English", "build", CatalogRowPolicy.Definitions, [], []));

        Assert.Equal(names.Keys.Order(), catalog.Collections.Keys.Order());
        foreach (var expected in names)
        {
            var collection = catalog.Collections[expected.Key];
            Assert.Equal(expected.Key, collection.Key); Assert.Equal(expected.Value, collection.Label);
            Assert.Equal(expected.Key != "glamour_dresser", collection.TotalKnown);
            Assert.Empty(collection.Items);
        }
        Assert.Equal(new[] { "armoires", "glamour_dresser" }, catalog.Collections.Values.Where(value => value.OwnershipKind == DataType.CurrentItemPresence).Select(value => value.Key).Order());
        Assert.Equal(new[] { "outfits", "relics" }, catalog.Collections.Values.Where(value => value.OwnershipKind == DataType.Derived).Select(value => value.Key).Order());
        Assert.Equal(21, catalog.Collections.Values.Count(value => value.OwnershipKind == DataType.PermanentUnlock));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownObservesTheWorkerWithoutReenteringTheDisposedCallerContextAndReleasesItsWaitHandle(bool faulted)
    {
        var completion = new TaskCompletionSource<CollectionCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        var preparation = new CatalogPreparation(() => new Builder(), (_, owned) => { token = owned; return completion.Task; });
        Assert.False(preparation.Prepare("English", "build"));
        var handle = token.WaitHandle.SafeWaitHandle;
        var context = new QueuedContext();
        var prior = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            preparation.Dispose();
        }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }

        try
        {
            if (faulted) completion.SetException(new IOException("Worker fixture failure."));
            else completion.SetResult(CollectionCatalogBuilderPolicy.Build(new("English", "build", [], [], [])));
            await preparation.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0, context.PostCount);
            Assert.True(handle.IsClosed);
            Assert.True(token.IsCancellationRequested);
        }
        finally
        {
            completion.TrySetCanceled(); context.Drain(); preparation.Dispose();
            await preparation.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private sealed class Builder : ICatalogProjectionBuilder
    {
        public CatalogProjection? Projection { get; } = new("English", "build", [], [], []);
        public bool Advance(int maximumRows = 256) => true;
        public void Dispose() { }
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        private int posts;
        public int PostCount => Volatile.Read(ref posts);
        public override void Post(SendOrPostCallback callback, object? state) { Interlocked.Increment(ref posts); callbacks.Enqueue((callback, state)); }
        public void Drain() { while (callbacks.TryDequeue(out var pending)) pending.Callback(pending.State); }
    }
}

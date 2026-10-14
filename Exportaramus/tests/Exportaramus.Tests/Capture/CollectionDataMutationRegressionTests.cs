using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Capture;

public sealed class CollectionDataMutationRegressionTests
{
    [Fact]
    public void InvalidCompactAdmissionStopsBeforeAnyDownstreamPreparationMetrics()
    {
        var data = SnapshotFixture.Create();
        data = data with { State = data.State with { ContentId = 0 } };
        var measurements = new Exportaramus.Features.Diagnostics.DiagnosticMeasurements();
        var publisher = new CollectionDataPublisher(new Exportaramus.Features.Snapshots.AtomicSnapshotStorage(
            Path.Combine(AppContext.BaseDirectory, "invalid-compact-admission-unused")), null, measurements);
        Assert.Throws<InvalidDataException>(() => publisher.Prepare(data, "Character", 1, null, false, false, default));
        Assert.Equal(new[] { "CompactSerializer" }, measurements.Snapshot().Keys);
    }

    [Fact]
    public void FullDresserCapacityIsLegalAndEntriesAreCanonicalByPhysicalIndex()
    {
        var known = ImmutableHashSet.Create(100u);
        var entries = Enumerable.Range(0, 800).Reverse().Select(index => DresserCapturePolicy.Entry(index, 100, known));
        var result = DresserCapturePolicy.Complete(entries, 1, SnapshotFixture.Now);
        Assert.True(result.Health.IsUsable);
        Assert.Equal(Enumerable.Range(0, 800), result.DresserEntries!.Value.Select(entry => entry.EntryIndex));
        Assert.Single(result.Items);
    }

    [Fact]
    public void FacewearIncludesMaximumNativeIdAndTwoOwnedVariantsStillOwnOneStyle()
    {
        var entry = FacewearMappingPolicy.Project(ushort.MaxValue, 0);
        Assert.Equal(1u, entry.Id);
        Assert.Equal("Facewear 1", entry.Name);
        Assert.True(entry.IsFallbackName);
        var queried = new List<ushort>();
        Assert.True(FacewearMappingPolicy.Read(1, [new(1, 0), new(ushort.MaxValue, 0)], id => { queried.Add(id); return true; }));
        Assert.Equal(new ushort[] { 1, ushort.MaxValue }, queried);
    }

    [Fact]
    public void OutfitIdentityAndComponentsSurviveCanonicalReadAndTrustedRestoreOnlyOnDresser()
    {
        var data = WithDresser();
        var json = CollectionDataSerializer.CompactV11(data, 1);
        var saved = CollectionStateReader.Read(json, 123, Guid.NewGuid(), SnapshotFixture.Now);
        AssertComposition(saved);
        Assert.Equal(DataType.ReportedOwnership, saved.ReportedEvidence.Health.DataType);
        var restored = RestoredStatePolicy.Reconcile(data.Catalog, saved,
            CollectionKeys.Unlocks.Concat(CollectionKeys.Presence).ToImmutableHashSet(), [], true, SnapshotFixture.Now.AddMinutes(1), 0);
        AssertComposition(restored);
        Assert.Equal(SourceStatus.Stale, restored.CurrentItemPresence["glamourDresser"].Health.SourceStatus);
    }

    private static void AssertComposition(CollectionState state)
    {
        foreach (var pair in state.CurrentItemPresence)
        {
            if (pair.Key != "glamourDresser") { Assert.Null(pair.Value.DresserEntries); continue; }
            var entry = Assert.Single(pair.Value.DresserEntries!.Value);
            Assert.Equal(100u, entry.OutfitId);
            Assert.Equal(1_000_100u, entry.RawItemId);
            Assert.Equal(100u, entry.BaseItemId);
            Assert.Equal(7, entry.EntryIndex);
            Assert.Equal(new DresserEntryComponent(2, 2642), Assert.Single(entry.Components));
        }
    }

    private static CollectionData WithDresser()
    {
        var data = SnapshotFixture.Create();
        var dresser = DresserCapturePolicy.Complete([DresserCapturePolicy.Entry(7, 1_000_100, data.Catalog.KnownItemIds,
            [0, 0, 2642, 0, 0, 0, 0, 0, 0, 0, 0], [false, false, true, false, false, false, false, false, false, false, false])], 1, SnapshotFixture.Now);
        return CollectionDataAssembler.Assemble(data.Catalog, data.State with { CurrentItemPresence = data.State.CurrentItemPresence.SetItem("glamourDresser", dresser) });
    }

    [Fact]
    public void PartialOutfitSummaryRequiresEveryConstituent()
    {
        var data = SnapshotFixture.Create();
        data = data with
        {
            Collections = data.Collections.Remove("outfits"),
            State = data.State with
            {
                CurrentItemPresence = data.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key,
                    pair => pair.Value with { Items = [new(2642, pair.Value.SupportsQuantity ? 1u : null)] })
            }
        };
        var prepared = SnapshotBundlePreparation.Prepare(new(data, 1, "{}", null, "", ImmutableDictionary<string, string>.Empty,
            new(null, [], [], null), 0, null), null, true);
        Assert.Equal(0, prepared.ConfirmedPartialCounts["outfits"]);
    }

    [Fact]
    public void LegacyConsistencyCannotIncludeFacewearHealth()
    {
        var data = SnapshotFixture.Create();
        var legacy = CollectionDataSerializer.Compact(data, 1);
        var changed = data with { SourceHealth = data.SourceHealth.SetItem("facewear", data.SourceHealth["facewear"] with { CaptureGeneration = 99 }) };
        using var before = JsonDocument.Parse(legacy);
        using var after = JsonDocument.Parse(CollectionDataSerializer.Compact(changed, 1));
        Assert.Equal(before.RootElement.GetProperty("consistency").GetString(), after.RootElement.GetProperty("consistency").GetString());
    }

    [Fact]
    public void UnavailableCanonicalCapabilitiesUseTheEmptyWireSentinel()
    {
        var store = new CollectionDataPublicationStore();
        Assert.Equal("", store.GetCapabilitiesV11());
        store.SetCapabilities("legacy");
        Assert.Equal("", store.GetCapabilitiesV11());
    }

    [Fact]
    public void CaptureQueriesStableSourceOrderAndDisposedOperationDoesNoMoreWork()
    {
        var reader = new Reader();
        var definitions = new[] { "mounts", "facewear" }.Select(key => new CollectionDefinition(key, key, DataType.PermanentUnlock, true, [new(1, 1, key, false)]));
        var catalog = new CollectionCatalog("English", ReaderCompatibility.CurrentGameVersion, definitions.ToImmutableDictionary(row => row.Key), []);
        using (var operation = new CollectionReadOperation(reader, catalog, true, 123, 1, SnapshotFixture.Now))
        {
            var completed = false;
            var maximumSteps = 2 + 2 + CollectionReadOperation.PhysicalSources.Length + 1;
            for (var step = 0; step < maximumSteps && !completed; step++) completed = operation.Advance(1);
            Assert.True(completed);
            Assert.Equal(new[] { "facewear", "mounts" }, reader.Queries);
        }
        using var stopped = new CollectionReadOperation(reader, catalog, true, 123, 2, SnapshotFixture.Now);
        stopped.Dispose();
        var priorChecks = reader.IdentityChecks;
        Assert.True(stopped.Advance(1));
        Assert.Equal(priorChecks, reader.IdentityChecks);
        Assert.Null(stopped.Result);
    }

    private sealed class Reader : ICollectionReader
    {
        public int IdentityChecks;
        public List<string> Queries { get; } = [];
        public bool IdentityMatches(ulong id) { IdentityChecks++; return id == 123; }
        public bool IsAvailable(string key, string version) => true;
        public bool ReadUnlock(string key, CatalogItem row) { Queries.Add(key); return false; }
        public QuestState ReadQuest(uint id, bool complete) => throw new InvalidOperationException();
        public CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog, ulong id, long generation, DateTimeOffset now) => CaptureReadPolicy.Presence([], false, true, generation, now);
    }
}

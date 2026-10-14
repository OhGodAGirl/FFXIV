using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;
namespace Exportaramus.Tests.Snapshots;

public sealed class ExtendedPublicationBoundaryTests
{
    [Fact]
    public void InvalidCanonicalStateCannotPassLegacyOnlyValidationOrBeRestored()
    {
        var data = SnapshotFixture.Create();
        data = CollectionDataAssembler.Assemble(data.Catalog, data.State with { Unlocks = data.State.Unlocks.SetItem("facewear", data.State.Unlocks["facewear"] with { UnlockedIds = [0] }) });
        var publisher = new CollectionDataPublisher(new AtomicSnapshotStorage(Path.Combine(AppContext.BaseDirectory, "extended-invalid")), null);
        Assert.Throws<InvalidDataException>(() => publisher.Prepare(data, "Test", 1, null, false, false, default));
        Assert.Throws<InvalidDataException>(() => CollectionStateReader.Read(CollectionDataSerializer.CompactV11(data, 1), 123, Guid.NewGuid(), SnapshotFixture.Now));
    }
    [Fact]
    public void CanonicalOnlyOverflowKeepsTheWholePublicationUnaccepted()
    {
        var data = SnapshotFixture.Create();
        var source = data.State.Unlocks["facewear"];
        data = CollectionDataAssembler.Assemble(data.Catalog, data.State with { Unlocks = data.State.Unlocks.SetItem("facewear", source with { Health = source.Health with { Reason = new string('x', 2_100_000) } }) });
        Assert.True(Exportaramus.ConsumerKit.V1.CollectionDataSemanticValidator.ValidateSnapshot(CollectionDataSerializer.Compact(data, 1), "123").Success);
        var publisher = new CollectionDataPublisher(new AtomicSnapshotStorage(Path.Combine(AppContext.BaseDirectory, "extended-overflow")), null);
        Assert.Throws<OutputSizeLimitException>(() => publisher.Prepare(data, "Test", 1, null, false, false, default));
    }
    [Fact]
    public void LegacyNoticeFilteringHandlesFacewearOnlyAndWithdrawal()
    {
        var data = SnapshotFixture.Create();
        AcceptedSnapshotBundle Bundle(CollectionData value, long revision)
        {
            var prints = CollectionFingerprint.Sources(value);
            return SnapshotBundlePreparation.Prepare(new(value, revision, CollectionDataSerializer.Compact(value, revision), null, CollectionFingerprint.Combined(prints), prints,
                PreparedFFXIVCollectExport.Unavailable("test"), 0, null), null, true);
        }
        var previous = Bundle(data, 1);
        var next = Bundle(CollectionDataAssembler.Assemble(data.Catalog, data.State with { Unlocks = data.State.Unlocks.SetItem("facewear", data.State.Unlocks["facewear"] with { UnlockedIds = [] }) }), 2);
        var notices = new ChangedNoticeCoalescer(); notices.Add(next, previous, []); Assert.Null(notices.Take()); Assert.Null(notices.Take(true));
        var withdrawal = File.ReadAllText(Path.Combine(Exportaramus.Tests.Contracts.ConsumerContractTests.Root, "tests/fixtures/Contracts/1.1/fixtures/valid/snapshot-withdrawn-valid.json"));
        Assert.True(Exportaramus.ConsumerKit.V1.CollectionDataSemanticValidator.ValidateWithdrawn(CollectionDataLegacyProjection.Notice(withdrawal)).Success);
    }
    [Fact]
    public void MissingPresenceDoesNotInventPhysicalCompositionInFingerprint()
    {
        var data = SnapshotFixture.Create();
        var missing = data with { State = data.State with { CurrentItemPresence = data.State.CurrentItemPresence.Remove("glamourDresser") } };
        Assert.NotEqual(CollectionFingerprint.Sources(data)["glamourDresser"], CollectionFingerprint.Sources(missing)["glamourDresser"]);
    }
    [Theory]
    [InlineData(-1, 100)]
    [InlineData(0, 0)]
    [InlineData(0, 1000000)]
    [InlineData(0, 999)]
    public void InvalidPhysicalIdentityIsNeverAnEmptyRead(int index, uint raw)
    {
        Assert.Throws<SourceReadException>(() => DresserCapturePolicy.Entry(index, raw, new HashSet<uint> { 100 }));
    }
    [Fact]
    public void IncompleteDresserSlotMappingsAndExcessPhysicalEntriesAreRejected()
    {
        ImmutableArray<uint> slots = [100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        ImmutableArray<bool> unlocked = [true, false, false, false, false, false, false, false, false, false, false];
        var known = new HashSet<uint> { 100 };
        Assert.Throws<SourceReadException>(() => DresserCapturePolicy.Entry(0, 100, known, default, unlocked));
        Assert.Throws<SourceReadException>(() => DresserCapturePolicy.Entry(0, 100, known, slots, default));
        Assert.Throws<SourceReadException>(() => DresserCapturePolicy.Entry(0, 100, known, [], unlocked));
        Assert.Throws<SourceReadException>(() => DresserCapturePolicy.Entry(0, 100, known, slots, []));
        Assert.Throws<SourceReadException>(() => DresserCapturePolicy.Complete(Enumerable.Range(0, 801).Select(index => new DresserEntry(index, 100, 100, null, [new(0, 100)])), 1, SnapshotFixture.Now));
        Assert.Throws<SourceReadException>(() => FacewearMappingPolicy.Project(0, 0));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialOutfitCountNeverTreatsMissingConstituentsAsOwned(bool emptyCatalog)
    {
        var data = SnapshotFixture.Create();
        var definition = data.Catalog.Collections["outfits"];
        definition = definition with { Items = emptyCatalog ? [] : [definition.Items[0] with { RequiredItemIds = [] }] };
        data = data with { Collections = data.Collections.Remove("outfits"), Catalog = data.Catalog with { Collections = data.Catalog.Collections.SetItem("outfits", definition) } };
        var prints = CollectionFingerprint.Sources(data);
        var bundle = SnapshotBundlePreparation.Prepare(new(data, 1, CollectionDataSerializer.Compact(data, 1), null, CollectionFingerprint.Combined(prints), prints,
            PreparedFFXIVCollectExport.Unavailable("test"), 0, null), null, false);
        if (emptyCatalog) Assert.Empty(bundle.ConfirmedPartialCounts);
        else Assert.Equal(0, bundle.ConfirmedPartialCounts["outfits"]);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialOutfitCountRequiresAnInitializedCatalog(bool missingCatalog)
    {
        var data = SnapshotFixture.Create();
        var definitions = missingCatalog
            ? data.Catalog.Collections.Remove("outfits")
            : data.Catalog.Collections.SetItem("outfits", data.Catalog.Collections["outfits"] with { Items = default });
        data = data with { Collections = data.Collections.Remove("outfits"), Catalog = data.Catalog with { Collections = definitions } };
        var prints = CollectionFingerprint.Sources(data);
        var bundle = SnapshotBundlePreparation.Prepare(new(data, 1, CollectionDataSerializer.Compact(data, 1), null, CollectionFingerprint.Combined(prints), prints,
            PreparedFFXIVCollectExport.Unavailable("test"), 0, null), null, false);
        Assert.Empty(bundle.ConfirmedPartialCounts);
    }
    [Fact]
    public void IncompatibleRestoredDresserClearsBothAggregateAndComposition()
    {
        var data = SnapshotFixture.Create(); var dresser = data.State.CurrentItemPresence["glamourDresser"] with { DresserEntries = [new(0, 100, 100, null, [new(0, 100)])] };
        var state = data.State with { CurrentItemPresence = data.State.CurrentItemPresence.SetItem("glamourDresser", dresser) };
        var restored = RestoredStatePolicy.Reconcile(data.Catalog, state, ImmutableHashSet<string>.Empty, [], true, SnapshotFixture.Now, 2);
        Assert.Empty(restored.CurrentItemPresence["glamourDresser"].Items);
        Assert.Null(restored.CurrentItemPresence["glamourDresser"].DresserEntries);
    }
}

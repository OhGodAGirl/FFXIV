using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
namespace Exportaramus.Tests.Snapshots;

public sealed class RestorationAndPreparationTests
{
    [Fact]
    public void RestoreValidCompleteAndUnknownSourcesWithoutReconstructingEvidence()
    {
        var data = SnapshotFixture.Create();
        var restored = CollectionStateReader.Read(CollectionDataSerializer.Compact(data, 3), 123, Guid.NewGuid(), SnapshotFixture.Now);
        Assert.False(restored.Unlocks["facewear"].Health.IsUsable);
        Assert.All(restored.Unlocks.Where(pair => pair.Key is not ("facewear" or "beasts")).Select(pair => pair.Value), value => Assert.Equal(SourceStatus.Stale, value.Health.SourceStatus));
        Assert.Equal<PresenceItem>(data.State.CurrentItemPresence["inventory"].Items, restored.CurrentItemPresence["inventory"].Items);
        var unknown = CollectionStateFactory.Empty(123, Guid.NewGuid(), SnapshotFixture.Now);
        var empty = CollectionDataAssembler.Assemble(data.Catalog, unknown);
        var loaded = CollectionStateReader.Read(CollectionDataSerializer.Compact(empty, 1), 123, Guid.NewGuid(), SnapshotFixture.Now);
        Assert.All(loaded.Unlocks.Values, value => Assert.Empty(value.UnlockedIds));
        Assert.All(loaded.CurrentItemPresence.Values, value => Assert.Empty(value.Items));
        Assert.Throws<InvalidDataException>(() => CollectionStateReader.Read("{}", 123, Guid.NewGuid(), SnapshotFixture.Now));
    }
    [Fact]
    public void MissingEvidenceIsEmptyAndValidEvidenceIsSorted()
    {
        Assert.Empty(CollectionStateReader.ReadReports(null, 123));
        var json = Evidence([new(2, "Zulu", SnapshotFixture.Now), new(1, "é", SnapshotFixture.Now), new(1, "A", SnapshotFixture.Now)]);
        var records = CollectionStateReader.ReadReports(json, 123);
        Assert.Equal([1u, 1u, 2u], records.Select(value => value.ItemId));
        Assert.Equal(["A", "é", "Zulu"], records.Select(value => value.Reporter));
    }
    [Theory]
    [InlineData("{\"contentId\":\"124\",\"items\":[]}")]
    [InlineData("{\"contentId\":\"123\",\"contentId\":\"123\",\"items\":[]}")]
    [InlineData("{\"contentId\":\"123\",\"items\":[{\"itemId\":1,\"itemId\":1,\"reporter\":\"A\",\"reportedAtUtc\":\"2026-09-06T00:00:00Z\"}]}")]
    [InlineData("{\"contentId\":\"123\",\"items\":[{\"itemId\":0,\"reporter\":\"A\",\"reportedAtUtc\":\"2026-09-06T00:00:00Z\"}]}")]
    [InlineData("{\"contentId\":\"123\",\"items\":[{\"itemId\":1,\"reporter\":\" A\",\"reportedAtUtc\":\"2026-09-06T00:00:00Z\"}]}")]
    [InlineData("{\"contentId\":\"123\",\"items\":[{\"itemId\":1,\"reporter\":\"A\",\"reportedAtUtc\":\"2026-09-06T00:00:00+00:00\"}]}")]
    public void InvalidEvidenceNeverBecomesEmptyObservation(string json) => Assert.Throws<InvalidDataException>(() => CollectionStateReader.ReadReports(json, 123));
    [Fact]
    public void EvidenceRejectsDuplicateRecordsAndOversizedInputs()
    {
        var row = new ReportedEvidence(1, "A", SnapshotFixture.Now);
        Assert.Throws<InvalidDataException>(() => CollectionStateReader.ReadReports(Evidence([row, row]), 123));
        Assert.Throws<InvalidDataException>(() => CollectionStateReader.ReadReports(Evidence(Enumerable.Repeat(row, 10001)), 123));
        Assert.Throws<InvalidDataException>(() => CollectionStateReader.ReadReports(new string(' ', 4194305), 123));
        var records = Enumerable.Range(1, 10000).Select(i => new ReportedEvidence((uint)i, "A", SnapshotFixture.Now));
        Assert.Equal(10000, CollectionStateReader.ReadReports(Evidence(records), 123).Length);
    }
    [Fact]
    public void PreparedBundleCachesCountsDeltasCapabilitiesAndExactBytes()
    {
        var data = SnapshotFixture.Create();
        var first = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(data, 1), null, true);
        Assert.Empty(first.CollectionDeltas); Assert.Equal(Encoding.UTF8.GetByteCount(first.CompactJson), first.CompactBytes);
        Assert.Equal(Encoding.UTF8.GetByteCount(first.CapabilitiesJson), first.CapabilitiesBytes);
        var next = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(data, 2), first, false);
        Assert.All(next.CollectionDeltas.Values, delta => Assert.Equal(0, delta));
        Assert.Equal(next.CapabilitiesJson, next.UnavailableReportsCapabilitiesJson);
        var foreign = StateAndPublicationTests.Bundle(data with { State = data.State with { ContentId = 124 } }, 3);
        Assert.Empty(SnapshotBundlePreparation.Prepare(foreign, first, false).CollectionDeltas);
        var unprepared = StateAndPublicationTests.Bundle(data, 1);
        Assert.Empty(SnapshotBundlePreparation.Prepare(unprepared, unprepared, false).CollectionDeltas);
        var store = new CollectionDataPublicationStore(); store.Withdraw(); store.Accept(first); store.Withdraw();
        Assert.Equal(first.UnavailableReportsCapabilitiesJson, store.GetCapabilities()); Assert.Empty(store.GetSnapshot());
    }
    private static string Evidence(IEnumerable<ReportedEvidence> records) => JsonSerializer.Serialize(new
    {
        contentId = "123",
        items = records.Select(row => new
        { itemId = row.ItemId, reporter = row.Reporter, reportedAtUtc = row.ReportedAtUtc.UtcDateTime.ToString("O") })
    });
}

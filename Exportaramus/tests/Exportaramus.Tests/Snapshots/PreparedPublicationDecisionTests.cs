using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class PreparedPublicationDecisionTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "prepared-publication", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LightweightPublicationRetainsTheLastFullRichSnapshotAndHasNoPreparedRichBytes()
    {
        var storage = new AtomicSnapshotStorage(root);
        var publisher = new CollectionDataPublisher(storage, null);
        var data = SnapshotFixture.Create();
        var full = await publisher.PrepareAndPersistAsync(data, "Test", 1, null, true, false, default);
        var bytes = await File.ReadAllBytesAsync(storage.CanonicalPath);
        var next = CollectionDataAssembler.Assemble(data.Catalog, data.State with
        {
            CaptureType = CaptureType.Lightweight,
            CapturedAtUtc = SnapshotFixture.Now.AddMinutes(5),
            CurrentItemPresence = data.State.CurrentItemPresence.SetItem("inventory", data.State.CurrentItemPresence["inventory"] with { Items = [] })
        });

        var accepted = await publisher.PrepareAndPersistAsync(next, "Test", 2, full, false, false, default);

        Assert.Null(accepted.RichJson);
        Assert.Equal(0, accepted.RichBytes);
        Assert.Equal(0, accepted.ExportBytes);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(storage.CanonicalPath));
        Assert.Equal(full.HistoryCount, accepted.HistoryCount);
        Assert.Equal(accepted.CompactJsonV12, await storage.ReadAsync(123, false, default));
    }

    [Fact]
    public void PreparedDeltasUseThePriorAcceptedCountsAndReportAvailabilityCanBeWithdrawn()
    {
        var data = SnapshotFixture.Create();
        var first = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(data, 1), null, true);
        var next = CollectionDataAssembler.Assemble(data.Catalog, data.State with
        {
            Unlocks = data.State.Unlocks.SetItem("mounts", data.State.Unlocks["mounts"] with { UnlockedIds = [] })
        });

        var accepted = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(next, 2), first, true);

        Assert.Equal(-1, accepted.CollectionDeltas["mounts"]);
        Assert.Equal(0, accepted.CollectionDeltas["minions"]);
        Assert.Equal(1, first.CollectionOwnedCounts["mounts"]);
        Assert.Equal(0, accepted.CollectionOwnedCounts["mounts"]);
        using var available = JsonDocument.Parse(accepted.CapabilitiesJson);
        using var withdrawn = JsonDocument.Parse(accepted.UnavailableReportsCapabilitiesJson);
        Assert.True(available.RootElement.GetProperty("reportedOwnershipEvidence").GetProperty("available").GetBoolean());
        Assert.False(withdrawn.RootElement.GetProperty("reportedOwnershipEvidence").GetProperty("available").GetBoolean());
        var unavailable = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(next, 3), accepted, false);
        Assert.Equal(unavailable.CapabilitiesJson, unavailable.UnavailableReportsCapabilitiesJson);
        using var current = JsonDocument.Parse(unavailable.CapabilitiesJson);
        Assert.False(current.RootElement.GetProperty("reportedOwnershipEvidence").GetProperty("available").GetBoolean());
    }

    [Fact]
    public void CompactUnlocksAndRichQuestProgressRetainTheirActualValues()
    {
        var data = SnapshotFixture.Create();
        using var compact = JsonDocument.Parse(CollectionDataSerializer.Compact(data, 1));
        Assert.Equal(new uint[] { 1 }, compact.RootElement.GetProperty("unlocks").GetProperty("mounts").GetProperty("unlockedIds").EnumerateArray().Select(value => value.GetUInt32()));
        using var rich = JsonDocument.Parse(CollectionDataSerializer.Rich(data, "Test", SnapshotFixture.Now));
        Assert.Equal(255, rich.RootElement.GetProperty("collections").GetProperty("quests").GetProperty("items")[0].GetProperty("progress").GetProperty("sequence").GetInt32());
        Assert.Equal("Coherent", CollectionDataSerializer.Consistency(data));
        Assert.Equal("MixedObservationTimes", CollectionDataSerializer.Consistency(data with { State = data.State with { CaptureType = CaptureType.Lightweight } }));
        Assert.Equal("MixedObservationTimes", CollectionDataSerializer.Consistency(data with
        {
            SourceHealth = data.SourceHealth.SetItem("inventory", data.SourceHealth["inventory"] with { SourceStatus = SourceStatus.Stale })
        }));
    }

    [Fact]
    public void ActualCompactPublicationSortsReportedEvidenceByItemAndOrdinalLabel()
    {
        var data = SnapshotFixture.Create();
        data = CollectionDataAssembler.Assemble(data.Catalog, data.State with
        {
            ReportedEvidence = data.State.ReportedEvidence with
            {
                Items = [new(1675, "Z", SnapshotFixture.Now), new(100, "Z", SnapshotFixture.Now), new(100, "A", SnapshotFixture.Now)]
            }
        });
        using var compact = JsonDocument.Parse(CollectionDataSerializer.Compact(data, 1));
        var records = compact.RootElement.GetProperty("reportedOwnership").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new uint[] { 100, 100, 1675 }, records.Select(record => record.GetProperty("itemId").GetUInt32()));
        Assert.Equal(new[] { "A", "Z", "Z" }, records.Select(record => record.GetProperty("reporter").GetString()));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

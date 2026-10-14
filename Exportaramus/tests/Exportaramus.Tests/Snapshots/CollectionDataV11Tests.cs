using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using V10 = Exportaramus.ConsumerKit.V1.CollectionDataSemanticValidator;
using V11 = Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator;
namespace Exportaramus.Tests.Snapshots;

public sealed class CollectionDataV11Tests
{
    [Fact]
    public void PinnedStandaloneFixturesMatchDeclaredAcceptance()
    {
        var folder = Path.Combine(Exportaramus.Tests.Contracts.ConsumerContractTests.Root, "tests/fixtures/Contracts/1.1/fixtures");
        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            var payload = File.ReadAllText(path); var name = Path.GetFileName(path);
            var accepted = name.StartsWith("capabilities", StringComparison.Ordinal) ? V11.ValidateCapabilities(payload).Success :
                name.StartsWith("snapshot-changed", StringComparison.Ordinal) ? V11.ValidateChanged(payload).Success :
                name.StartsWith("snapshot-withdrawn", StringComparison.Ordinal) ? V11.ValidateWithdrawn(payload).Success :
                V11.ValidateSnapshot(payload, "12345678901234567").Success;
            Assert.True(accepted == (Path.GetFileName(Path.GetDirectoryName(path)) == "valid"), name);
        }
    }
    private static CollectionData Data()
    {
        var data = SnapshotFixture.Create();
        var dresser = data.State.CurrentItemPresence["glamourDresser"] with
        {
            Items = [new(100, null), new(2642, null)],
            DresserEntries = [new(0, 100, 100, null, [new(0, 100)]), new(4, 1_000_900, 900, 900, [new(2, 100), new(3, 2642)])],
        };
        return CollectionDataAssembler.Assemble(data.Catalog, data.State with
        {
            Unlocks = data.State.Unlocks.SetItem("facewear", data.State.Unlocks["facewear"] with { UnlockedIds = [59, 61] }),
            CurrentItemPresence = data.State.CurrentItemPresence.SetItem("glamourDresser", dresser),
        });
    }
    [Fact]
    public void ParallelSnapshotsPreservePinnedLegacyAndExposeCanonicalPermanentIdsAndPhysicalComposition()
    {
        var data = Data();
        var legacy = CollectionDataSerializer.Compact(data, 5);
        var canonical = CollectionDataSerializer.CompactV11(data, 5);
        Assert.True(V10.ValidateSnapshot(legacy, "123").Success);
        Assert.False(V10.ValidateSnapshot(canonical, "123").Success);
        var validated = V11.ValidateSnapshot(canonical, "123");
        Assert.True(validated.Success, validated.Code.ToString());
        Assert.Equal<uint>([59u, 61u], validated.Value!.Unlocks["facewear"].UnlockedIds);
        Assert.Equal("PermanentUnlock", validated.Value.SourceHealth["facewear"].DataType);
        Assert.Equal(2, validated.Value.CurrentItemPresence["glamourDresser"].DresserEntries!.Value.Length);
        Assert.DoesNotContain("facewear", legacy);
        Assert.DoesNotContain("dresserEntries", legacy);
        Assert.True(V10.ValidateRich(CollectionDataSerializer.Rich(data, "Test", SnapshotFixture.Now)).Success);
    }
    [Fact]
    public void CapabilitiesIdentifyPinnedVersionAndEachGateFamily()
    {
        var data = Data(); var legacy = CapabilityDocumentWriter.Build(data, true); var canonical = CapabilityDocumentWriter.BuildV11(data, true);
        Assert.True(V10.ValidateCapabilities(legacy).Success);
        var result = V11.ValidateCapabilities(canonical);
        Assert.True(result.Success, result.Code.ToString());
        Assert.Equal("Exportaramus.CollectionData.V1_1.GetSnapshot", result.Value!.Gates.GetSnapshot);
        Assert.True(V10.ValidateCapabilities(CollectionDataLegacyProjection.Capabilities(canonical)).Success);
        Assert.False(V10.ValidateCapabilities(canonical).Success);
    }
    [Theory]
    [InlineData(SourceStatus.Fresh, Completeness.Complete, true)]
    [InlineData(SourceStatus.Stale, Completeness.Complete, true)]
    [InlineData(SourceStatus.NeverObserved, Completeness.Unknown, false)]
    [InlineData(SourceStatus.Unavailable, Completeness.Unknown, false)]
    [InlineData(SourceStatus.Fresh, Completeness.Partial, false)]
    public void EmptyFacewearOnlyBecomesEvidenceWhenHealthIsUsable(SourceStatus status, Completeness completeness, bool included)
    {
        var data = Data(); var unlock = data.State.Unlocks["facewear"];
        var health = SnapshotFixture.Health(DataType.PermanentUnlock, status) with { Completeness = completeness };
        data = CollectionDataAssembler.Assemble(data.Catalog, data.State with { Unlocks = data.State.Unlocks.SetItem("facewear", unlock with { UnlockedIds = [], Health = health }) });
        var result = V11.ValidateSnapshot(CollectionDataSerializer.CompactV11(data, 1), "123");
        Assert.True(result.Success, result.Code.ToString());
        Assert.Equal(included, result.Value!.Unlocks.ContainsKey("facewear"));
        if (included) Assert.Empty(result.Value.Unlocks["facewear"].UnlockedIds);
    }
    [Fact]
    public void RestorationPreservesCanonicalEvidenceAsStaleAndLegacyHasUnknownCompositionAndFacewear()
    {
        var data = Data(); var session = Guid.NewGuid();
        var restored = CollectionStateReader.Read(CollectionDataSerializer.CompactV11(data, 3), 123, session, SnapshotFixture.Now);
        Assert.Equal<uint>([59u, 61u], restored.Unlocks["facewear"].UnlockedIds);
        Assert.Equal(SourceStatus.Stale, restored.Unlocks["facewear"].Health.SourceStatus);
        Assert.Equal(2, restored.CurrentItemPresence["glamourDresser"].DresserEntries!.Value.Length);
        Assert.Equal(ProvenanceType.Restored, restored.CurrentItemPresence["glamourDresser"].Health.ProvenanceType);
        var legacy = CollectionStateReader.Read(CollectionDataSerializer.Compact(data, 3), 123, session, SnapshotFixture.Now);
        Assert.False(legacy.Unlocks["facewear"].Health.IsUsable);
        Assert.Null(legacy.CurrentItemPresence["glamourDresser"].DresserEntries);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PhysicalMultiplicityAndPartitionPublishAndNotifyWithoutChangingAggregate(bool partition)
    {
        var data = Data(); var source = data.State.CurrentItemPresence["glamourDresser"];
        var entries = source.DresserEntries!.Value;
        var changedEntries = partition ? entries.SetItem(1, entries[1] with { Components = [new(3, 2642)] }) : entries.Add(new(5, 100, 100, null, [new(0, 100)]));
        var next = CollectionDataAssembler.Assemble(data.Catalog, data.State with
        {
            CurrentItemPresence = data.State.CurrentItemPresence.SetItem("glamourDresser", source with { DresserEntries = changedEntries }),
        });
        Assert.Equal(source.Items, next.State.CurrentItemPresence["glamourDresser"].Items);
        Assert.NotEqual(CollectionFingerprint.Sources(data)["glamourDresser"], CollectionFingerprint.Sources(next)["glamourDresser"]);
        Assert.NotEqual(CollectionFingerprint.Sources(data, false)["glamourDresser"], CollectionFingerprint.Sources(next, false)["glamourDresser"]);
        var storage = new Exportaramus.Features.Snapshots.AtomicSnapshotStorage(Path.Combine(Exportaramus.Tests.Contracts.ConsumerContractTests.Root, "development/physical-publication-proof"));
        var publisher = new CollectionDataPublisher(storage, null);
        var previous = publisher.Prepare(data, "Test", 1, null, false, false, default).Bundle;
        var prepared = publisher.Prepare(next, "Test", 2, previous, false, false, default);
        Assert.True(prepared.Changed);
        Assert.NotEqual(previous.CompactJsonV11, prepared.Bundle.CompactJsonV11);
        Assert.True(V11.ValidateSnapshot(prepared.Bundle.CompactJsonV11, "123").Success);
        var notices = new ChangedNoticeCoalescer(); notices.Add(prepared.Bundle, previous, []);
        var notice = V11.ValidateChanged(notices.Take(true)!);
        Assert.True(notice.Success, notice.Code.ToString());
        Assert.Contains("glamourDresser", notice.Value!.ChangedSources);
        Assert.Contains("CurrentItemPresence", notice.Value.ChangedDataTypes);
    }
    [Fact]
    public void FacewearOnlyChangeNotifiesOnlyCanonicalFamily()
    {
        var data = Data();
        var next = CollectionDataAssembler.Assemble(data.Catalog, data.State with { Unlocks = data.State.Unlocks.SetItem("facewear", data.State.Unlocks["facewear"] with { UnlockedIds = [59] }) });
        AcceptedSnapshotBundle Bundle(CollectionData value, long revision)
        {
            var prints = CollectionFingerprint.Sources(value);
            return SnapshotBundlePreparation.Prepare(new(value, revision, CollectionDataSerializer.Compact(value, revision), null, CollectionFingerprint.Combined(prints), prints,
                PreparedFFXIVCollectExport.Unavailable("test"), 0, null), null, true);
        }
        var coalescer = new ChangedNoticeCoalescer(); coalescer.Add(Bundle(next, 2), Bundle(data, 1), []);
        var notice = coalescer.Take(true)!;
        Assert.True(V11.ValidateChanged(notice).Success);
        Assert.Null(CollectionDataLegacyProjection.Notice(notice));
    }
    [Theory]
    [InlineData("duplicateEntry")]
    [InlineData("aggregateMismatch")]
    [InlineData("wrongBase")]
    [InlineData("wrongSlot")]
    [InlineData("wrongSource")]
    public void InvalidPhysicalEvidenceFailsClosed(string mutation)
    {
        var root = JsonNode.Parse(CollectionDataSerializer.CompactV11(Data(), 3))!;
        var dresser = root["currentItemPresence"]!["glamourDresser"]!; var entries = dresser["dresserEntries"]!.AsArray();
        switch (mutation)
        {
            case "duplicateEntry": entries[1]!["entryIndex"] = 0; break;
            case "aggregateMismatch": entries[1]!["components"]![1]!["itemId"] = 999; break;
            case "wrongBase": entries[1]!["baseItemId"] = 901; break;
            case "wrongSlot": entries[1]!["components"]![1]!["slotIndex"] = 11; break;
            case "wrongSource": root["currentItemPresence"]!["inventory"]!["dresserEntries"] = entries.DeepClone(); break;
        }
        Assert.False(V11.ValidateSnapshot(root.ToJsonString(), "123").Success);
    }
    [Fact]
    public void AcceptedPublicationWithdrawsBothVersionsTogether()
    {
        var data = Data(); var prints = CollectionFingerprint.Sources(data);
        var bundle = SnapshotBundlePreparation.Prepare(new(data, 3, CollectionDataSerializer.Compact(data, 3), null, CollectionFingerprint.Combined(prints), prints,
            PreparedFFXIVCollectExport.Unavailable("test"), 0, null), null, true);
        var store = new CollectionDataPublicationStore(); store.Accept(bundle); store.SetCapabilities(bundle.CapabilitiesJson, bundle.CapabilitiesJsonV11);
        Assert.True(V10.ValidateSnapshot(store.GetSnapshot(), "123").Success);
        Assert.True(V11.ValidateSnapshot(store.GetSnapshotV11(), "123").Success);
        store.Withdraw(); Assert.Empty(store.GetSnapshot()); Assert.Empty(store.GetSnapshotV11());
        Assert.False(V11.ValidateCapabilities(store.GetCapabilitiesV11()).Value!.ReportedOwnershipEvidence.Available);
    }
}

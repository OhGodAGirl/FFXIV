using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;
using V10 = Exportaramus.ConsumerKit.V1.CollectionDataSemanticValidator;
using V11 = Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator;
using V12 = Exportaramus.ConsumerKit.V1_2.CollectionDataSemanticValidator;

namespace Exportaramus.Tests.Snapshots;

public sealed class CollectionDataV12Tests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "beasts-provider", Guid.NewGuid().ToString("N"));

    private static CollectionData Data(ImmutableArray<uint> ids, CollectionSourceStatus? health = null)
    {
        var data = SnapshotFixture.Create();
        var definition = data.Catalog.Collections["beasts"] with
        {
            Items = [.. Enumerable.Range(1, 50).Select(id => new CatalogItem((uint)id, (uint)id, "Beast " + id, false, null, [], null))],
        };
        return CollectionDataAssembler.Assemble(data.Catalog with { Collections = data.Catalog.Collections.SetItem("beasts", definition) },
            data.State with { Unlocks = data.State.Unlocks.SetItem("beasts", new(ids, health ?? SnapshotFixture.Health(DataType.PermanentUnlock))) });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparedPayloadsAndCapabilitiesExposeBeastsOnlyOnOptedInVersion(bool empty)
    {
        var data = Data(empty ? [] : [1, 50]);
        var bundle = new CollectionDataPublisher(new(root), null).Prepare(data, "Test", 3, null, false, false, default).Bundle;
        Assert.True(V10.ValidateSnapshot(bundle.CompactJson, "123").Success);
        Assert.True(V11.ValidateSnapshot(bundle.CompactJsonV11, "123").Success);
        Assert.True(V12.ValidateSnapshot(bundle.CompactJsonV12, "123").Success);
        Assert.DoesNotContain("beasts", bundle.CompactJson);
        Assert.DoesNotContain("beasts", bundle.CompactJsonV11);
        Assert.Equal(empty ? 0 : 2, V12.ValidateSnapshot(bundle.CompactJsonV12, "123").Value!.Unlocks["beasts"].UnlockedIds.Length);
        Assert.True(V10.ValidateCapabilities(bundle.CapabilitiesJson).Success);
        Assert.True(V11.ValidateCapabilities(bundle.CapabilitiesJsonV11).Success);
        Assert.True(V12.ValidateCapabilities(bundle.CapabilitiesJsonV12).Success);
        Assert.Equal("Exportaramus.CollectionData.V1_2.GetSnapshot", V12.ValidateCapabilities(bundle.CapabilitiesJsonV12).Value!.Gates.GetSnapshot);
        Assert.DoesNotContain("beasts", bundle.CapabilitiesJson);
        Assert.DoesNotContain("beasts", bundle.CapabilitiesJsonV11);
        Assert.DoesNotContain("beasts", bundle.RichJson!);
        Assert.True(V10.ValidateRich(bundle.RichJson).Success);
        Assert.True(V12.ValidateRich(bundle.RichJsonV12).Success);
    }

    [Theory]
    [InlineData(SourceStatus.Fresh, Completeness.Complete, CapabilityState.Supported, true)]
    [InlineData(SourceStatus.Stale, Completeness.Complete, CapabilityState.Supported, true)]
    [InlineData(SourceStatus.Fresh, Completeness.Partial, CapabilityState.Supported, false)]
    [InlineData(SourceStatus.Unavailable, Completeness.Unknown, CapabilityState.UnavailablePendingValidation, false)]
    public void BeastsUnknownIsOmittedAndStalePreservesObservation(SourceStatus status, Completeness complete, CapabilityState capability, bool included)
    {
        var health = SnapshotFixture.Health(DataType.PermanentUnlock, status) with { Completeness = complete, CapabilityState = capability };
        if (status == SourceStatus.Stale) health = health with { ReasonCode = ReasonCode.CaptureFailed, Reason = "Native capture failed." };
        var result = V12.ValidateSnapshot(CollectionDataSerializer.CompactV12(Data([], health), 3), "123");
        Assert.True(result.Success, result.Code.ToString());
        Assert.Equal(included, result.Value!.Unlocks.ContainsKey("beasts"));
        if (included) Assert.Equal(SnapshotFixture.Now, DateTimeOffset.Parse(result.Value.SourceHealth["beasts"].ObservedAtUtc!));
    }

    [Fact]
    public void OldSnapshotsRestoreBeastsUnknownAndVersion12RestoresStaleInSameCharacter()
    {
        var data = Data([1, 50]);
        foreach (var json in new[] { CollectionDataSerializer.Compact(data, 3), CollectionDataSerializer.CompactV11(data, 3) })
        {
            var restored = CollectionStateReader.Read(json, 123, Guid.NewGuid(), SnapshotFixture.Now.AddHours(1));
            Assert.False(restored.Unlocks["beasts"].Health.IsUsable);
            Assert.Empty(restored.Unlocks["beasts"].UnlockedIds);
        }
        var current = CollectionStateReader.Read(CollectionDataSerializer.CompactV12(data, 3), 123, Guid.NewGuid(), SnapshotFixture.Now.AddHours(1));
        Assert.Equal<uint>([1, 50], current.Unlocks["beasts"].UnlockedIds);
        Assert.Equal(SourceStatus.Stale, current.Unlocks["beasts"].Health.SourceStatus);
        Assert.Equal(SnapshotFixture.Now, current.Unlocks["beasts"].Health.ObservedAtUtc);
        Assert.Equal(ReasonCode.RestoredFromDisk, current.Unlocks["beasts"].Health.ReasonCode);
        Assert.Throws<InvalidDataException>(() => CollectionStateReader.Read(CollectionDataSerializer.CompactV12(data, 3), 456, Guid.NewGuid(), SnapshotFixture.Now));
    }

    [Fact]
    public void BeastOnlyChangeIsSuppressedForBothOlderEventFamilies()
    {
        var publisher = new CollectionDataPublisher(new(root), null);
        var previous = publisher.Prepare(Data([1]), "Test", 1, null, false, false, default).Bundle;
        var next = publisher.Prepare(Data([1, 50]), "Test", 2, previous, false, false, default).Bundle;
        var notices = new ChangedNoticeCoalescer(); notices.Add(next, previous, ["beasts"]);
        var notice = notices.TakeV12()!;
        Assert.True(V12.ValidateChanged(notice).Success);
        Assert.Equal<string>(["beasts"], V12.ValidateChanged(notice).Value!.ChangedSources);
        Assert.Null(CollectionDataLegacyProjection.Notice(notice));
        Assert.Null(CollectionDataLegacyProjection.NoticeV11(notice));
        var store = new CollectionDataPublicationStore(); store.Accept(next);
        store.SetCapabilities(next.CapabilitiesJson, next.CapabilitiesJsonV11, next.CapabilitiesJsonV12);
        Assert.Equal(next.CompactJsonV12, store.GetSnapshotV12());
        store.Withdraw(); Assert.Empty(store.GetSnapshotV12());
        Assert.False(V12.ValidateCapabilities(store.GetCapabilitiesV12()).Value!.ReportedOwnershipEvidence.Available);
    }

    [Fact]
    public async Task PublisherPersistsVersionedRichAlongsideUnchangedLegacyRichAndCanonical12()
    {
        var storage = new AtomicSnapshotStorage(root);
        var bundle = await new CollectionDataPublisher(storage, null).PrepareAndPersistAsync(Data([1, 50]), "Test", 1, null, false, false, default);
        Assert.Equal(bundle.RichJson, await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal(bundle.RichJsonV12, await File.ReadAllTextAsync(storage.RichV12Path));
        Assert.Equal(bundle.CompactJsonV12, await storage.ReadAsync(123, false, default));
        Assert.True(V12.ValidateRich(await File.ReadAllTextAsync(storage.RichV12Path)).Success);
    }

    [Fact]
    public async Task SidecarFailureRollsBackBothOlderDestinationsInSameTransaction()
    {
        var storage = new AtomicSnapshotStorage(root);
        await storage.CommitAsync(123, "{\"old\":1}", "{\"old\":1}", null, false, false, SnapshotFixture.Now, default);
        Directory.CreateDirectory(storage.RichV12Path);
        await Assert.ThrowsAnyAsync<IOException>(() => storage.CommitAsync(123, "{}", "{}", null, false, false, SnapshotFixture.Now, default, "{}"));
        Assert.Equal("{\"old\":1}", await storage.ReadAsync(123, false, default));
        Assert.Equal("{\"old\":1}", await File.ReadAllTextAsync(storage.CanonicalPath));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void CollectBeastsNeedsValidatedContractCompleteObservationAndCatalog(bool validated, bool observed, bool included)
    {
        var data = observed ? Data([]) : Data([], CollectionStateFactory.Empty(123, Guid.NewGuid(), SnapshotFixture.Now).Unlocks["beasts"].Health);
        data = data with { Catalog = data.Catalog with { GameDataVersion = "2026.09.01.0000.0000" } };
        var contract = new FFXIVCollectContract(data.Catalog.GameDataVersion, data.Catalog.Collections.Where(pair => CollectionKeys.Collect.Contains(pair.Key)).ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Items.Select(item => item.Id).ToImmutableHashSet())) with
        { Beasts = new("2026.09.01.0000.0000", Enumerable.Range(1, 50).Select(id => (uint)id).ToImmutableHashSet(), validated) };
        var export = FFXIVCollectImportSerializer.Serialize(data, contract);
        Assert.Equal(included, JsonNode.Parse(export.Json!)!.AsObject().ContainsKey("beasts"));
        if (included) Assert.Empty(JsonNode.Parse(export.Json!)!["beasts"]!.AsArray());
    }

    [Fact]
    public void CollectOmitsStaleBeastsAndRejectsUnknownIdsAtPublication()
    {
        var health = SnapshotFixture.Health(DataType.PermanentUnlock, SourceStatus.Stale) with { ReasonCode = ReasonCode.CaptureFailed, Reason = "Native capture failed." };
        var data = Data([1], health); data = data with { Catalog = data.Catalog with { GameDataVersion = "2026.09.01.0000.0000" } };
        var contract = new FFXIVCollectContract(data.Catalog.GameDataVersion, data.Catalog.Collections.Where(pair => CollectionKeys.Collect.Contains(pair.Key)).ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Items.Select(item => item.Id).ToImmutableHashSet())) with
        { Beasts = new("2026.09.01.0000.0000", Enumerable.Range(1, 50).Select(id => (uint)id).ToImmutableHashSet(), true) };
        Assert.DoesNotContain("\"beasts\"", FFXIVCollectImportSerializer.Serialize(data, contract).Json!);
        Assert.Throws<InvalidDataException>(() => new CollectionDataPublisher(new(root), null).Prepare(Data([51]), "Test", 1, null, false, false, default));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

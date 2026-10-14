using System.Text;
using System.Text.Json;
using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;
namespace Exportaramus.Tests.Snapshots;

public sealed class PublicationTransactionTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "publication-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task TimestampRefreshPublishesNewBytesWithoutNewHistoryAndOptionalFailureReplacesCopyState()
    {
        var storage = new AtomicSnapshotStorage(root); var data = SnapshotFixture.Create();
        var contract = new FFXIVCollectContract(data.Catalog.GameDataVersion, data.Catalog.Collections.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Items.Select(item => item.Id).ToImmutableHashSet()));
        var publisher = new CollectionDataPublisher(storage, contract);
        var first = await publisher.PrepareAndPersistAsync(data, "Test", 1, null, true, false, default);
        Assert.True(first.Export.CanCopy); Assert.Equal(Encoding.UTF8.GetByteCount(first.Export.Json!), first.ExportBytes);
        Assert.Equal(Encoding.UTF8.GetByteCount(first.RichJson!), first.RichBytes); Assert.Equal(1, first.HistoryCount);
        var later = data with { State = data.State with { CapturedAtUtc = data.State.CapturedAtUtc.AddSeconds(1) } };
        var second = await publisher.PrepareAndPersistAsync(later, "Test", 2, first, true, false, default);
        Assert.Equal(first.Fingerprint, second.Fingerprint); Assert.Equal(1, second.HistoryCount); Assert.NotEqual(first.CompactJson, second.CompactJson);
        var changed = later with
        {
            State = later.State with
            {
                CapturedAtUtc = later.State.CapturedAtUtc.AddSeconds(1),
                CurrentItemPresence = later.State.CurrentItemPresence.SetItem("inventory",
            later.State.CurrentItemPresence["inventory"] with { Items = [] })
            }
        };
        changed = CollectionDataAssembler.Assemble(changed.Catalog, changed.State);
        var third = await new CollectionDataPublisher(storage, null).PrepareAndPersistAsync(changed, "Test", 3, second, true, false, default);
        Assert.NotEqual(second.Fingerprint, third.Fingerprint); Assert.Equal(2, third.HistoryCount); Assert.False(third.Export.CanCopy);
        Assert.Equal(third.CompactJsonV12, await storage.ReadAsync(123, false, default));
        var foreign = data with { State = data.State with { ContentId = 124, CapturedAtUtc = data.State.CapturedAtUtc.AddSeconds(4) } };
        var other = await publisher.PrepareAndPersistAsync(foreign, "Other", 4, third, true, false, default);
        Assert.Empty(other.CollectionDeltas); Assert.Equal(3, other.HistoryCount);
    }
    [Fact]
    public async Task InvalidAndCancelledCandidatesCannotReplaceAcceptedFiles()
    {
        var storage = new AtomicSnapshotStorage(root); var publisher = new CollectionDataPublisher(storage, null); var data = SnapshotFixture.Create();
        var accepted = await publisher.PrepareAndPersistAsync(data, "Test", 1, null, false, false, default);
        await Assert.ThrowsAsync<InvalidDataException>(() => publisher.PrepareAndPersistAsync(data, new string('X', 1000), 2, accepted, false, false, default));
        var invalid = data with { State = data.State with { CaptureGeneration = -1 } };
        await Assert.ThrowsAsync<InvalidDataException>(() => publisher.PrepareAndPersistAsync(invalid, "Test", 2, accepted, false, false, default));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PrepareAndPersistAsync(data, "Test", 2, accepted, false, false, cancel.Token));
        Assert.Equal(accepted.CompactJsonV12, await storage.ReadAsync(123, false, default));
        Assert.Equal(accepted.RichJson, await File.ReadAllTextAsync(storage.CanonicalPath));
    }
    [Fact]
    public void CoalescedNoticesClassifyChangedValuesCapabilitiesAndCharacterReplacement()
    {
        var data = SnapshotFixture.Create(); var first = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(data, 1), null, true);
        var health = data.State.Unlocks["mounts"].Health with { CapabilityState = CapabilityState.Degraded };
        var candidate = data with { State = data.State with { Unlocks = data.State.Unlocks.SetItem("mounts", data.State.Unlocks["mounts"] with { UnlockedIds = [], Health = health }) } };
        candidate = CollectionDataAssembler.Assemble(candidate.Catalog, candidate.State);
        var second = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(candidate, 2), first, true);
        var notices = new ChangedNoticeCoalescer(); notices.Add(first, null, []); notices.Add(second, first, ["mounts"]);
        using var parsed = JsonDocument.Parse(notices.Take()!);
        var types = parsed.RootElement.GetProperty("changedDataTypes").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Contains("Unlocks", types); Assert.Contains("SourceCapabilities", types);
        notices.Add(first, null, []); notices.Add(first, first, []);
        var foreign = first with { Data = data with { State = data.State with { ContentId = 124 } } };
        notices.Add(foreign, null, []);
        using var replacement = JsonDocument.Parse(notices.Take()!);
        Assert.Equal("124", replacement.RootElement.GetProperty("contentId").GetString());
        var mixed = data with { State = data.State with { CaptureGeneration = data.State.CaptureGeneration + 1 } };
        Assert.Equal("MixedObservationTimes", CollectionDataSerializer.Consistency(mixed));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

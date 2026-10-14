using System.Collections.Immutable;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class FingerprintEvidenceTests
{
    [Fact]
    public async Task DifferentValidOwnershipWitnessCreatesHistoryWithoutAnOwnedCountDelta()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "evidence-fingerprint-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var data = SnapshotFixture.Create(); var outfit = data.Collections["outfits"];
            var item = Assert.Single(outfit.Items);
            Assert.All(item.OwnershipEvidence.Facts, fact => Assert.Equal("inventory", fact.SourceKey));
            var otherWitness = item with
            {
                OwnershipEvidence = item.OwnershipEvidence with
                { Facts = item.OwnershipEvidence.Facts.Select(fact => fact with { SourceKey = "armoryChest" }).ToImmutableArray() }
            };
            var next = data with
            {
                Collections = data.Collections.SetItem("outfits", outfit with { Items = [otherWitness] }),
                State = data.State with { CapturedAtUtc = data.State.CapturedAtUtc.AddSeconds(1) }
            };
            Assert.True(CollectionDataSemanticValidator.ValidateRich(CollectionDataSerializer.Rich(data, "Test", data.State.CapturedAtUtc)).Success);
            Assert.True(CollectionDataSemanticValidator.ValidateRich(CollectionDataSerializer.Rich(next, "Test", next.State.CapturedAtUtc)).Success);
            Assert.Equal(data.State.CurrentItemPresence, next.State.CurrentItemPresence);
            Assert.NotEqual(CollectionFingerprint.Sources(data, false)["outfits"], CollectionFingerprint.Sources(next, false)["outfits"]);
            var publisher = new CollectionDataPublisher(new AtomicSnapshotStorage(root), null);
            var first = await publisher.PrepareAndPersistAsync(data, "Test", 1, null, false, false, default);
            var second = await publisher.PrepareAndPersistAsync(next, "Test", 2, first, false, false, default);
            Assert.NotEqual(first.Fingerprint, second.Fingerprint); Assert.Equal(2, second.HistoryCount);
            Assert.Equal(first.CollectionOwnedCounts["outfits"], second.CollectionOwnedCounts["outfits"]);
            Assert.Equal(0, second.CollectionDeltas.GetValueOrDefault("outfits"));
            Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json").Length);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void EvidenceFreshnessChangesHealthFingerprintWhileObservationMetadataDoesNotChangeValues()
    {
        var data = SnapshotFixture.Create(); var source = data.State.Unlocks["mounts"];
        var laterState = data.State with
        {
            CaptureGeneration = 2,
            CapturedAtUtc = data.State.CapturedAtUtc.AddMinutes(1),
            ProviderSessionId = Guid.NewGuid(),
            Unlocks = data.State.Unlocks.SetItem("mounts", source with
            { Health = source.Health with { ObservedAtUtc = data.State.CapturedAtUtc.AddSeconds(1), CaptureGeneration = 2, Reason = "A different player-facing explanation." } })
        };
        var later = CollectionDataAssembler.Assemble(data.Catalog, laterState);
        Assert.Equal(CollectionFingerprint.Combined(CollectionFingerprint.Sources(data)), CollectionFingerprint.Combined(CollectionFingerprint.Sources(later)));
        var staleState = laterState with
        {
            Unlocks = laterState.Unlocks.SetItem("mounts", source with
            { Health = source.Health with { SourceStatus = SourceStatus.Stale, ReasonCode = ReasonCode.NotLoadedThisSession } })
        };
        var stale = CollectionDataAssembler.Assemble(data.Catalog, staleState);
        Assert.Equal(CollectionFingerprint.Sources(data, false)["mounts"], CollectionFingerprint.Sources(stale, false)["mounts"]);
        Assert.NotEqual(CollectionFingerprint.Sources(data)["mounts"], CollectionFingerprint.Sources(stale)["mounts"]);
    }
}

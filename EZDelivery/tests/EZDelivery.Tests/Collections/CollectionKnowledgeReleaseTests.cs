using System.Collections.Immutable;
using Exportaramus.ConsumerKit.V1_1;
using EZDelivery.Features.Collections;

namespace EZDelivery.Tests.Collections;

public sealed partial class CollectionKnowledgeTests
{
    [Fact]
    public void BardingAcquisitionCannotExpireUntilUnlockObservationCatchesUp()
    {
        var data = Snapshot() with { ReportedOwnership = new([new(999, "2026-09-13T12:00:00Z", "EZDelivery")]) };
        data = data with { SourceHealth = data.SourceHealth.SetItem("bardings", data.SourceHealth["bardings"] with { ObservedAtUtc = "2026-09-12T00:00:00Z" }) };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Barding, 2).State);
        subject.Update(View(data with { Revision = 2, SourceHealth = Snapshot().SourceHealth }), Character);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Barding, 2).State);
    }

    [Theory]
    [InlineData("omitted")]
    [InlineData("health")]
    [InlineData("partial")]
    [InlineData("equal")]
    public void IncompleteOrEqualTimeUnlockEvidenceRetainsBardingAcquisition(string change)
    {
        var data = Snapshot() with { ReportedOwnership = new([new(999, "2026-09-13T12:00:00Z", "EZDelivery")]) };
        data = change switch
        {
            "omitted" => data with { Unlocks = data.Unlocks.Remove("bardings") },
            "health" => data with { SourceHealth = data.SourceHealth.Remove("bardings") },
            "partial" => data with { SourceHealth = data.SourceHealth.SetItem("bardings", data.SourceHealth["bardings"] with { Completeness = "Partial" }) },
            _ => data with { SourceHealth = data.SourceHealth.SetItem("bardings", data.SourceHealth["bardings"] with { ObservedAtUtc = "2026-09-13T12:00:00Z" }) },
        };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Barding, 2).State);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("armoryChest")]
    [InlineData("chocoboSaddlebag")]
    [InlineData("premiumSaddlebag")]
    [InlineData("retainers")]
    [InlineData("glamourDresser")]
    [InlineData("armoire")]
    public void EveryEquipmentSourceMustBeCompleteForAbsence(string source)
    {
        var original = Snapshot();
        var invalid = new[]
        {
            original with { SourceCapabilities = original.SourceCapabilities.Remove(source) },
            original with { SourceHealth = original.SourceHealth.Remove(source) },
            original with { SourceCapabilities = original.SourceCapabilities.SetItem(source, original.SourceCapabilities[source] with { CapabilityState = "Unsupported" }) },
            original with { CurrentItemPresence = original.CurrentItemPresence.Remove(source) },
            original with { SourceHealth = original.SourceHealth.SetItem(source, original.SourceHealth[source] with { ProvenanceType = "Unknown" }) },
            original with { SourceHealth = original.SourceHealth.SetItem(source, original.SourceHealth[source] with { SourceStatus = "Failed" }) },
        };
        foreach (var snapshot in invalid)
        {
            var subject = new CollectionKnowledge(); subject.Update(View(snapshot), Character);
            Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
        }
    }

    [Theory]
    [InlineData("Fresh", "Observed")]
    [InlineData("Fresh", "Restored")]
    [InlineData("Stale", "Observed")]
    [InlineData("Stale", "Restored")]
    public void CompleteSnapshotSemanticsApplyConsistentlyToEveryRequiredSource(string status, string provenance)
    {
        var data = Snapshot();
        data = data with { SourceHealth = data.SourceHealth.ToImmutableDictionary(x => x.Key, x => x.Value with { SourceStatus = status, ProvenanceType = provenance }) };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Furniture).State);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Barding, 2).State);
    }

    [Fact]
    public void MissingBardingUnlockSetCannotBecomeAnEmptySet()
    {
        var subject = new CollectionKnowledge();
        var data = Snapshot(); subject.Update(View(data with { Unlocks = data.Unlocks.Remove("bardings") }), Character);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Barding, 2).State);
        subject.Update(View(data with { Revision = 2, CurrentItemPresence = data.CurrentItemPresence.SetItem("inventory", new([new(999, 1)])), Unlocks = data.Unlocks.Remove("bardings") }), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Barding, 2).State);
    }

    [Fact]
    public void IdentityLossClearsBothAcquisitionAndSnapshotFacts()
    {
        var subject = new CollectionKnowledge(); var data = Snapshot();
        subject.Update(View(data), Character); subject.ConfirmAcquisition(999);
        subject.Update(View(data), null);
        Assert.False(subject.Available);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.ConfirmAcquisition(999);
        subject.Update(View(data), Character);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(0, CollectionKind.Equipment).State);
    }

    [Fact]
    public void ProviderSessionReplacementInvalidatesCachedPositivePresence()
    {
        var data = Snapshot(); var subject = new CollectionKnowledge();
        subject.Update(View(data with { CurrentItemPresence = data.CurrentItemPresence.SetItem("inventory", new([new(999, 1)])) }), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
        var generation = subject.Generation;
        subject.Update(View(data with { ProviderSessionId = "new-session" }), Character);
        Assert.True(subject.Generation > generation);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Theory]
    [InlineData("AnotherConsumer", "2026-09-13T00:00:00Z")]
    [InlineData("EZDelivery", "invalid-time")]
    public void AdvisoryPositiveEvidenceWithNoSafeExpiryStillSuppresses(string reporter, string timestamp)
    {
        var subject = new CollectionKnowledge();
        subject.Update(View(Snapshot() with { ReportedOwnership = new([new(999, timestamp, reporter)]) }), Character);
        subject.ConfirmDelivery(Character, 999, RemovedAt);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Fact]
    public void AcquisitionAndDeliveryGenerationOnlyTrackRelevantFacts()
    {
        var subject = new CollectionKnowledge(); subject.Update(View(Snapshot()), Character);
        var generation = subject.Generation;
        subject.ConfirmAcquisition(0); subject.ConfirmDelivery("other", 999, RemovedAt); subject.ConfirmDelivery(Character, 0, RemovedAt);
        Assert.Equal(generation, subject.Generation);
        subject.ConfirmAcquisition(999);
        Assert.Equal(generation + 1, subject.Generation);
        subject.ConfirmAcquisition(999);
        Assert.Equal(generation + 1, subject.Generation);
        subject.ConfirmDelivery(Character, 999, RemovedAt);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Fact]
    public void PartialDresserOutfitSuppressesOnlyReportedBaseAndPhysicalComponents()
    {
        var data = Snapshot();
        var dresser = new PresenceSource([])
        {
            DresserEntries = [new DresserEntry(0, 100, 100, 42, [new DresserEntryComponent(0, 200)])],
        };
        data = data with
        {
            CurrentItemPresence = data.CurrentItemPresence.SetItem("glamourDresser", dresser),
            SourceHealth = data.SourceHealth.SetItem("glamourDresser", data.SourceHealth["glamourDresser"] with { Completeness = "Partial" }),
        };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(100, CollectionKind.Equipment).State);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(200, CollectionKind.Equipment).State);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(42, CollectionKind.Equipment).State);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(201, CollectionKind.Equipment).State);
    }
}

using System.Collections.Immutable;
using Exportaramus.ConsumerKit.V1_1;
using EZDelivery.Features.Collections;

namespace EZDelivery.Tests.Collections;

public sealed partial class CollectionKnowledgeTests
{
    private const string Character = "123456789";
    private static readonly DateTimeOffset RemovedAt = DateTimeOffset.Parse("2026-09-15T00:00:00Z");

    [Fact]
    public void ConfirmedHandInClearsOwnPurchaseEvidenceButNotOtherStorageOrReports()
    {
        var data = Snapshot() with { ReportedOwnership = new([new(999, "2026-09-14T12:00:00Z", "EZDelivery")]) };
        var subject = new CollectionKnowledge();
        subject.Update(View(data), Character);
        subject.ConfirmAcquisition(999);
        subject.ConfirmDelivery("other", 999, RemovedAt);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.ConfirmDelivery(Character, 999, RemovedAt);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.Update(View(data with { Revision = 2, CurrentItemPresence = data.CurrentItemPresence.SetItem("retainers", new([new(999, 1)])) }), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.Update(View(data with { Revision = 3, ReportedOwnership = new([new(999, "2026-09-14T12:00:00Z", "AnotherConsumer")]) }), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Fact]
    public void HandInCannotOverrideIncompleteSourcesOrNewAcquisition()
    {
        var data = Snapshot() with { ReportedOwnership = new([new(999, "2026-09-14T12:00:00Z", "EZDelivery")]) };
        data = data with { CurrentItemPresence = data.CurrentItemPresence.Remove("retainers") };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        subject.ConfirmDelivery(Character, 999, RemovedAt);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.Update(View(data with { Revision = 2, ReportedOwnership = new([new(999, "2026-09-16T00:00:00Z", "EZDelivery")]) }), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.ConfirmAcquisition(999);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Fact]
    public void OnlyCompleteObservationsAfterOwnReportSupersedeHistoricalAcquisition()
    {
        var data = Snapshot() with { ReportedOwnership = new([new(999, "2026-09-13T12:00:00Z", "EZDelivery")]) };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.Update(View(data with { Revision = 2, SourceHealth = data.SourceHealth.SetItem("retainers", data.SourceHealth["retainers"] with { ObservedAtUtc = "2026-09-12T00:00:00Z" }) }), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Fact]
    public void ExplicitRecoveryIsLocalAndRetainsActualStorage()
    {
        var data = Snapshot() with { ReportedOwnership = new([new(999, "2026-09-14T12:00:00Z", "EZDelivery")]) };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        subject.ResetPurchaseTracking(RemovedAt);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.Update(View(data with { Revision = 2, CurrentItemPresence = data.CurrentItemPresence.SetItem("inventory", new([new(999, 1)])) }), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.Update(View(data), "another");
        subject.Update(View(data), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
    }
    private static CollectionDataSnapshot Snapshot()
    {
        string[] keys = ["inventory", "armoryChest", "chocoboSaddlebag", "premiumSaddlebag", "retainers", "glamourDresser", "armoire", "bardings"];
        return new("1.1", "Exportaramus", "test", "session", 1, Character, "2026-09-14T00:00:00Z", "Full", 1, "Coherent",
            ImmutableDictionary<string, UnlockSet>.Empty.Add("bardings", new([])),
            keys.Where(x => x != "bardings").ToImmutableDictionary(x => x, _ => new PresenceSource([])), new([]),
            keys.ToImmutableDictionary(x => x, x => new SourceCapability(x == "bardings" ? "PermanentUnlock" : "CurrentItemPresence", "Supported", false, "None", "")),
            keys.ToImmutableDictionary(x => x, x => new SourceHealth(x == "bardings" ? "PermanentUnlock" : "CurrentItemPresence", 1, "Fresh", "Complete", "Observed", "Supported", "None", "", "2026-09-14T00:00:00Z")));
    }
    private static CollectionDataClientView View(CollectionDataSnapshot snapshot, CollectionDataAvailability availability = CollectionDataAvailability.Ready)
        => new(availability, snapshot, snapshot.ProviderSessionId, snapshot.ContentId, snapshot.Revision, CollectionDataValidationCode.None);

    [Fact]
    public void CompleteEmptyIsMissingButOmittedSourceIsUnknown()
    {
        var subject = new CollectionKnowledge();
        var snapshot = Snapshot();
        subject.Update(View(snapshot), Character);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.Update(View(snapshot with { Revision = 2, CurrentItemPresence = snapshot.CurrentItemPresence.Remove("retainers") }), Character);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Theory]
    [InlineData("Fresh", "Partial", "Observed", "Supported")]
    [InlineData("Stale", "Partial", "Restored", "Supported")]
    [InlineData("Unavailable", "Complete", "Observed", "Supported")]
    [InlineData("Fresh", "Complete", "Observed", "Degraded")]
    public void IncompleteEvidenceCannotAuthorizeAbsence(string status, string completeness, string provenance, string capability)
    {
        var snapshot = Snapshot();
        var health = snapshot.SourceHealth["premiumSaddlebag"] with { SourceStatus = status, Completeness = completeness, ProvenanceType = provenance, CapabilityState = capability };
        var subject = new CollectionKnowledge();
        subject.Update(View(snapshot with { SourceHealth = snapshot.SourceHealth.SetItem("premiumSaddlebag", health) }), Character);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Theory]
    [InlineData("Stale", "Observed")]
    [InlineData("Stale", "Restored")]
    public void LastCompleteRetainerSnapshotCanEstablishSnapshotAbsence(string status, string provenance)
    {
        var snapshot = Snapshot();
        snapshot = snapshot with { SourceHealth = snapshot.SourceHealth.SetItem("retainers",
            snapshot.SourceHealth["retainers"] with { SourceStatus = status, ProvenanceType = provenance }) };
        var subject = new CollectionKnowledge();
        subject.Update(View(snapshot), Character);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
        Assert.Contains("last Exportaramus snapshot", subject.Status);
        snapshot = snapshot with { Revision = 2, CurrentItemPresence = snapshot.CurrentItemPresence.SetItem("retainers", new([new(999, 1)])) };
        subject.Update(View(snapshot), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Fact]
    public void PositiveStorageEvidenceSuppressesEvenWhenOtherSourcesAreIncomplete()
    {
        var snapshot = Snapshot();
        snapshot = snapshot with { CurrentItemPresence = ImmutableDictionary<string, PresenceSource>.Empty.Add("inventory", new([new(999, 1)])) };
        var subject = new CollectionKnowledge();
        subject.Update(View(snapshot), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Fact]
    public void BardingUsesUnlockIdNotItemId()
    {
        var snapshot = Snapshot();
        snapshot = snapshot with { Unlocks = snapshot.Unlocks.SetItem("bardings", new([2])) };
        var subject = new CollectionKnowledge();
        subject.Update(View(snapshot), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(6020, CollectionKind.Barding, 2).State);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(6021, CollectionKind.Barding, 3).State);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(6021, CollectionKind.Barding).State);
    }

    [Fact]
    public void RoutineRefreshRetainsFactsButWithdrawalAndCharacterChangeInvalidate()
    {
        var subject = new CollectionKnowledge();
        subject.Update(View(Snapshot(), CollectionDataAvailability.Refreshing), Character);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.Update(CollectionDataClientView.Empty(CollectionDataAvailability.Unavailable, CollectionDataValidationCode.NoPayload), Character);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.ConfirmAcquisition(999);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.Update(View(Snapshot()), "987654321");
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Fact]
    public void ReparsedUnchangedPollPreservesGeneration()
    {
        var subject = new CollectionKnowledge();
        subject.Update(View(Snapshot()), Character);
        var generation = subject.Generation;
        subject.Update(View(Snapshot()), Character);
        Assert.Equal(generation, subject.Generation);
        subject.Update(View(Snapshot() with { Revision = 2 }), Character);
        Assert.True(subject.Generation > generation);
    }

    [Fact]
    public void FurnitureUsesStorageAbsenceWithExplicitHousingDisclosure()
    {
        var subject = new CollectionKnowledge();
        subject.Update(View(Snapshot()), Character);
        var result = subject.Evaluate(999, CollectionKind.Furniture);
        Assert.Equal(OwnershipState.Missing, result.State);
        Assert.Contains("placed furnishings are not included", result.Reason);
        var snapshot = Snapshot();
        subject.Update(View(snapshot with { Revision = 2, CurrentItemPresence = snapshot.CurrentItemPresence.Remove("armoire").Remove("glamourDresser").Remove("armoryChest") }), Character);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Furniture).State);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.Update(View(snapshot with { Revision = 3, CurrentItemPresence = snapshot.CurrentItemPresence.Remove("retainers") }), Character);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Furniture).State);
    }
}

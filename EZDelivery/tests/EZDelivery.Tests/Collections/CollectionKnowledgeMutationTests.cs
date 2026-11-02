using Exportaramus.ConsumerKit.V1_1;
using EZDelivery.Features.Collections;

namespace EZDelivery.Tests.Collections;

public sealed partial class CollectionKnowledgeTests
{
    [Theory]
    [InlineData(CollectionDataAvailability.Unavailable)]
    [InlineData(CollectionDataAvailability.Rejected)]
    [InlineData(CollectionDataAvailability.Disposed)]
    public void ContradictoryUnavailableViewCannotAuthorizeSnapshotAbsence(CollectionDataAvailability availability)
    {
        // Fault injection: the pinned kit clears Snapshot for these states. Keep
        // the consumer's explicit availability guard tested independently too.
        var subject = new CollectionKnowledge();
        subject.Update(View(Snapshot(), availability), Character);
        Assert.False(subject.Available);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
        Assert.StartsWith("Exportaramus data unavailable", subject.Status);
    }

    [Fact]
    public void CollectionStatesAlwaysExplainTheirEvidence()
    {
        var subject = new CollectionKnowledge();
        Assert.Equal("Exportaramus data unavailable", subject.Status);
        Assert.Equal("Item mapping unavailable", subject.Evaluate(0, CollectionKind.Equipment).Reason);
        var data = Snapshot(); subject.Update(View(data), Character);
        subject.ConfirmAcquisition(999);
        Assert.Equal("Purchased this session", subject.Evaluate(999, CollectionKind.Equipment).Reason);
        subject.ConfirmDelivery(Character, 999, RemovedAt);
        Assert.Equal("Not present in the last complete Exportaramus snapshot", subject.Evaluate(999, CollectionKind.Equipment).Reason);
        subject.Update(View(data with { Revision = 2, CurrentItemPresence = data.CurrentItemPresence.SetItem("inventory", new([new(999, 1)])) }), Character);
        Assert.Equal("Present in Exportaramus storage or acquisition data", subject.Evaluate(999, CollectionKind.Equipment).Reason);
        subject.Update(View(data with { Revision = 3, ReportedOwnership = new([new(999, "2026-09-16T00:00:00Z", "EZDelivery")]) }), Character);
        Assert.Equal("Reported acquisition in Exportaramus", subject.Evaluate(999, CollectionKind.Equipment).Reason);
        subject.Update(View(data with { Revision = 4 }), Character);
        Assert.Equal("Barding unlock mapping unavailable", subject.Evaluate(999, CollectionKind.Barding).Reason);
        subject.Update(View(data with { Revision = 5, Unlocks = data.Unlocks.SetItem("bardings", new([2])) }), Character);
        Assert.Equal("Barding unlocked", subject.Evaluate(999, CollectionKind.Barding, 2).Reason);
        subject.Update(View(data with { Revision = 6, Unlocks = data.Unlocks.Remove("bardings") }), Character);
        Assert.Equal("Barding unlock data incomplete", subject.Evaluate(999, CollectionKind.Barding, 2).Reason);
        subject.Update(View(data with { Revision = 7, CurrentItemPresence = data.CurrentItemPresence.Remove("retainers") }), Character);
        Assert.Equal("Exportaramus snapshot is incomplete for retainers; capture this source in Exportaramus", subject.Evaluate(999, CollectionKind.Equipment).Reason);
    }

    [Fact]
    public void DeliveryAndTrackingResetAdvanceProjectionGeneration()
    {
        var subject = new CollectionKnowledge(); subject.Update(View(Snapshot()), Character);
        var generation = subject.Generation;
        subject.ConfirmDelivery(Character, 999, RemovedAt);
        Assert.Equal(generation + 1, subject.Generation);
        subject.ResetPurchaseTracking(RemovedAt);
        Assert.Equal(generation + 2, subject.Generation);
    }

    [Fact]
    public void IdentityChangeWithoutSnapshotStillInvalidatesProjectionGeneration()
    {
        var subject = new CollectionKnowledge();
        var unavailable = CollectionDataClientView.Empty(CollectionDataAvailability.Unavailable, CollectionDataValidationCode.NoPayload);
        var generation = subject.Generation;
        subject.Update(unavailable, Character);
        Assert.Equal(generation + 1, subject.Generation);
        subject.Update(unavailable, "another");
        Assert.Equal(generation + 2, subject.Generation);
    }

    [Fact]
    public void CollectionHealthCopyDistinguishesFreshRestoredAndUnavailableViews()
    {
        var subject = new CollectionKnowledge(); var data = Snapshot();
        subject.Update(View(data), Character); Assert.Equal("Collection data from Exportaramus", subject.Status);
        subject.Update(View(data with { Revision = 2, SourceHealth = data.SourceHealth.SetItem("retainers", data.SourceHealth["retainers"] with { ProvenanceType = "Restored" }) }), Character);
        Assert.Equal("Using last Exportaramus snapshot; storage may have changed since capture", subject.Status);
        subject.Update(CollectionDataClientView.Empty(CollectionDataAvailability.Unavailable, CollectionDataValidationCode.NoPayload), Character);
        Assert.Equal("Exportaramus data unavailable (NoPayload)", subject.Status);
    }

    [Fact]
    public void DeliveryTimestampCannotMoveBackwardAndOnlyStrictlyNewerDeliveryOverridesReport()
    {
        var data = Snapshot() with { ReportedOwnership = new([new(999, RemovedAt.ToString("O"), "EZDelivery")]) };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        subject.ConfirmDelivery(Character, 999, RemovedAt);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.ConfirmDelivery(Character, 999, RemovedAt.AddSeconds(1));
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.ConfirmDelivery(Character, 999, RemovedAt.AddSeconds(-1));
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Theory]
    [InlineData(CollectionKind.Equipment, OwnershipState.Present)]
    [InlineData(CollectionKind.Furniture, OwnershipState.Missing)]
    [InlineData(CollectionKind.Barding, OwnershipState.Missing)]
    public void AdvisoryExpiryRequiresOnlyApplicableSources(CollectionKind kind, OwnershipState expected)
    {
        var data = Snapshot() with { ReportedOwnership = new([new(999, "2026-09-13T00:00:00Z", "EZDelivery")]) };
        data = data with { CurrentItemPresence = data.CurrentItemPresence.Remove("armoryChest").Remove("glamourDresser").Remove("armoire") };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        Assert.Equal(expected, subject.Evaluate(999, kind, 2).State);
    }
}

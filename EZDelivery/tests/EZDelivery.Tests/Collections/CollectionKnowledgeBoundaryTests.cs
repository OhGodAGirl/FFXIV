using Exportaramus.ConsumerKit.V1_1;
using EZDelivery.Features.Collections;

namespace EZDelivery.Tests.Collections;

public sealed partial class CollectionKnowledgeTests
{
    [Fact]
    public void ReadyViewWithoutSnapshotCannotEstablishAbsence()
    {
        var subject = new CollectionKnowledge(); subject.Update(View(Snapshot()), Character);
        subject.Update(CollectionDataClientView.Empty(CollectionDataAvailability.Ready, CollectionDataValidationCode.NoPayload), Character);
        Assert.False(subject.Available);
        Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Fact]
    public void OutOfOrderDeliveryNoticesCannotRestoreOlderAcquisitionEvidence()
    {
        var data = Snapshot() with { ReportedOwnership = new([new(999, "2026-09-14T12:00:00Z", "EZDelivery")]) };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        subject.ConfirmDelivery(Character, 999, RemovedAt.AddDays(1));
        subject.ConfirmDelivery(Character, 999, RemovedAt.AddDays(-2));
        subject.ConfirmDelivery(Character, 999, RemovedAt.AddDays(1));
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
        subject.ConfirmDelivery(Character, 999, RemovedAt.AddDays(2));
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
    }

    [Fact]
    public void TrackingResetRemovesSessionAcquisitionsWhileRetainingStorageEvidence()
    {
        var data = Snapshot() with { ReportedOwnership = new([new(999, "2026-09-14T12:00:00Z", "EZDelivery")]) };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        subject.ConfirmAcquisition(999); subject.ConfirmAcquisition(1000);
        subject.ResetPurchaseTracking(RemovedAt);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
        Assert.Equal(OwnershipState.Missing, subject.Evaluate(1000, CollectionKind.Equipment).State);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("partial")]
    [InlineData("timestamp")]
    public void UnsafeStorageEvidenceCannotExpireAdvisoryAcquisition(string invalid)
    {
        var data = Snapshot() with { ReportedOwnership = new([new(999, "2026-09-13T12:00:00Z", "EZDelivery")]) };
        data = invalid switch
        {
            "missing" => data with { CurrentItemPresence = data.CurrentItemPresence.Remove("retainers") },
            "partial" => data with { SourceHealth = data.SourceHealth.SetItem("retainers", data.SourceHealth["retainers"] with { Completeness = "Partial" }) },
            _ => data with { SourceHealth = data.SourceHealth.SetItem("retainers", data.SourceHealth["retainers"] with { ObservedAtUtc = "invalid" }) },
        };
        var subject = new CollectionKnowledge(); subject.Update(View(data), Character);
        Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
    }
}

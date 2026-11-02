using Exportaramus.ConsumerKit.V1_1;
using EZDelivery.Features.Collections;

namespace EZDelivery.Tests.Collections;

public sealed partial class CollectionKnowledgeTests
{
    [Fact]
    public void RepeatedProviderWithdrawalAndSessionReplacementCannotReusePriorPresence()
    {
        var subject = new CollectionKnowledge();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var snapshot = Snapshot() with { ProviderSessionId = $"session-{iteration}",
                CurrentItemPresence = Snapshot().CurrentItemPresence.SetItem("inventory", new([new(999, 1)])) };
            subject.Update(View(snapshot), Character);
            Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
            subject.Update(CollectionDataClientView.Empty(CollectionDataAvailability.Unavailable, CollectionDataValidationCode.NoPayload), Character);
            Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
            subject.Update(View(Snapshot() with { ProviderSessionId = $"empty-{iteration}" }), Character);
            Assert.Equal(OwnershipState.Missing, subject.Evaluate(999, CollectionKind.Equipment).State);
            subject.ConfirmAcquisition(999);
            subject.Update(View(snapshot), "other-character");
            Assert.Equal(OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
        }
    }

    [Fact]
    public void EveryGeneratedMissingSourceCombinationRemainsUnknownUnlessPositiveEvidenceExists()
    {
        string[] sources = ["inventory", "armoryChest", "chocoboSaddlebag", "premiumSaddlebag", "retainers", "glamourDresser", "armoire"];
        for (var mask = 0; mask < 128; mask++)
        {
            var snapshot = Snapshot();
            for (var index = 0; index < sources.Length; index++)
                if ((mask & (1 << index)) != 0) snapshot = snapshot with { CurrentItemPresence = snapshot.CurrentItemPresence.Remove(sources[index]) };
            var subject = new CollectionKnowledge();
            subject.Update(View(snapshot), Character);
            Assert.Equal(mask == 0 ? OwnershipState.Missing : OwnershipState.Unknown, subject.Evaluate(999, CollectionKind.Equipment).State);
            subject.ConfirmAcquisition(999);
            Assert.Equal(OwnershipState.Present, subject.Evaluate(999, CollectionKind.Equipment).State);
        }
    }
}

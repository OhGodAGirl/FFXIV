using System.Collections.Immutable;
using Exportaramus.ConsumerKit.V1_2;

namespace CollectionShopper;

internal sealed record CollectionDataOwnershipState(
    bool WasChecked,
    CollectionDataValidationCode Failure,
    CollectionDataSnapshot? Snapshot,
    ImmutableArray<uint> TrustedOwnedItemIds,
    ImmutableArray<CollectionDataOwnershipSourceState> Sources,
    string StatusDetail)
{
    internal ImmutableDictionary<uint, DateTimeOffset> AcknowledgedAcquisitions { get; init; }
        = ImmutableDictionary<uint, DateTimeOffset>.Empty;
    internal ImmutableHashSet<uint> LegacyPurchaseArchive { get; init; } = ImmutableHashSet<uint>.Empty;
    internal bool CanInferMissingItemIds => Enum.GetValues<ShoppingCategory>().All(CompleteFor);
    internal bool CompleteFor(ShoppingCategory category) => CollectionDataOwnershipPolicy.CompleteFor(this, category);
    internal ItemOwnership Classify(ShoppingCategory category, uint itemId, uint unlockId, bool mappingValid = true)
        => CollectionDataOwnershipPolicy.Classify(this, category, itemId, unlockId, mappingValid);
}

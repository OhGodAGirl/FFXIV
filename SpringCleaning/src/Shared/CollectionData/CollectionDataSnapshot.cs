#nullable enable

using System;
using System.Collections.Immutable;

namespace SpringCleaning.Shared.CollectionData;

internal sealed record CollectionDataSnapshot(
    string ProviderInternalName,
    string ProviderVersion,
    ulong ContentId,
    ulong Revision,
    DateTimeOffset CapturedAtUtc,
    CollectionSourceSnapshot Inventory,
    CollectionSourceSnapshot ArmoryChest,
    CollectionSourceSnapshot ChocoboSaddlebag,
    CollectionSourceSnapshot PremiumSaddlebag,
    CollectionSourceSnapshot Retainers,
    CollectionSourceSnapshot Armoire,
    CollectionSourceSnapshot GlamourDresser)
{
    internal string ProviderSessionId { get; init; } = string.Empty;

    internal string ContentHash { get; init; } = string.Empty;

    internal string InventoryHash { get; init; } = string.Empty;

    internal ulong InventoryRevision { get; init; }

    internal ImmutableArray<CollectionDresserEntry>? DresserEntries { get; init; }

    internal SpringCleaning.Shared.Inventory.InventorySnapshot? InventoryObservation { get; init; }

    internal CollectionSourceSnapshot GetSource(CollectionSourceName source) => source switch
    {
        CollectionSourceName.Inventory => Inventory,
        CollectionSourceName.ArmoryChest => ArmoryChest,
        CollectionSourceName.ChocoboSaddlebag => ChocoboSaddlebag,
        CollectionSourceName.PremiumSaddlebag => PremiumSaddlebag,
        CollectionSourceName.Retainers => Retainers,
        CollectionSourceName.Armoire => Armoire,
        CollectionSourceName.GlamourDresser => GlamourDresser,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}

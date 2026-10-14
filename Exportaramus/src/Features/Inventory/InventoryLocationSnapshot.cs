using System.Collections.Immutable;

namespace Exportaramus.Features.Inventory;

public sealed record InventoryLocationSnapshot(
    InventoryLocationKey Key,
    string Name,
    InventoryObservationState State,
    DateTimeOffset? ObservedAtUtc,
    DateTimeOffset? AttemptedAtUtc,
    ImmutableArray<InventoryItemQuantity> Items)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool RestoredFromCache { get; init; }
    public bool HasObservation => ObservedAtUtc.HasValue;
    public bool SupportsQuantity => Key.Kind is not (InventoryLocationKind.Armoire or InventoryLocationKind.GlamourDresser);
}


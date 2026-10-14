using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Exportaramus.Features.Inventory;

public sealed record InventorySnapshot(
    ulong ContentId,
    long Revision,
    bool RetainerRosterKnown,
    ImmutableArray<InventoryLocationSnapshot> Locations)
{
    [JsonIgnore]
    public DateTimeOffset? RetainerRosterObservedAtUtc { get; init; }
}

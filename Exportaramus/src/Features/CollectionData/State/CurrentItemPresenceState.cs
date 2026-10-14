using System.Collections.Immutable;
namespace Exportaramus.Features.CollectionData.State;

public sealed record CurrentItemPresenceState(ImmutableArray<PresenceItem> Items, bool SupportsQuantity, CollectionSourceStatus Health)
{
    public ImmutableArray<DresserEntry>? DresserEntries { get; init; }
}



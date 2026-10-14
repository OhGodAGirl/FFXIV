using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Catalog;
namespace Exportaramus.Features.CollectionData.Publication;

public static class FFXIVCollectIdMapper
{
    // Catalog IDs have already applied the authoritative stable-ID transform, including Cabinet + 1.
    public static bool Covers(CollectionDefinition catalog, ImmutableHashSet<uint> recognized) => recognized.IsSubsetOf(catalog.Items.Select(item => item.Id));
    public static ImmutableArray<uint> Map(CollectionResult collection, ImmutableHashSet<uint> recognized) =>
        [.. collection.Items.Where(item => item.Owned && recognized.Contains(item.Id)).Select(item => item.Id).Distinct().Order()];
}

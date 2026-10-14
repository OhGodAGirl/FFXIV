using System.Collections.Immutable;

namespace Exportaramus.Features.CollectionData.Catalog;

public static class CollectionCatalogBuilderPolicy
{
    public static CollectionCatalog Build(CatalogProjection projection)
    {
        var rows = projection.Rows.ToLookup(r => r.Key, StringComparer.Ordinal);
        return new CollectionCatalog(projection.ClientLanguage, projection.GameDataVersion,
            projection.Definitions.ToImmutableDictionary(d => d.Key, d => d with
            {
                Items = rows[d.Key].Select(r => r.Item).Where(r => r.Id != 0).DistinctBy(r => r.Id).OrderBy(r => r.Id).ToImmutableArray(),
            }, StringComparer.Ordinal), projection.KnownItemIds.Where(id => id != 0).ToImmutableHashSet());
    }
}

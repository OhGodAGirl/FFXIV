using System.Collections.Immutable;

namespace Exportaramus.Features.CollectionData.Catalog;

public sealed record CatalogProjection(string ClientLanguage, string GameDataVersion,
    ImmutableArray<CollectionDefinition> Definitions, ImmutableArray<(string Key, CatalogItem Item)> Rows,
    ImmutableArray<uint> KnownItemIds);

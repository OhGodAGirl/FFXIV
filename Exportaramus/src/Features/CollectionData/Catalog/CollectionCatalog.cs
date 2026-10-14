using System.Collections.Immutable;

namespace Exportaramus.Features.CollectionData.Catalog;

public sealed record CollectionCatalog(string ClientLanguage, string GameDataVersion,
    ImmutableDictionary<string, CollectionDefinition> Collections,
    ImmutableHashSet<uint> KnownItemIds);

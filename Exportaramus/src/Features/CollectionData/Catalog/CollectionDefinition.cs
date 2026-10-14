using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Catalog;

public sealed record CollectionDefinition(string Key, string Label, DataType OwnershipKind,
    bool TotalKnown, ImmutableArray<CatalogItem> Items);

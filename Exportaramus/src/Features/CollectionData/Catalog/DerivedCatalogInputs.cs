using System.Collections.Immutable;

namespace Exportaramus.Features.CollectionData.Catalog;

public sealed record DerivedCatalogInputs(ImmutableArray<CatalogItem> Outfits, ImmutableArray<CatalogItem> Relics);

using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;
namespace Exportaramus.Features.CollectionData;

public sealed record CollectionData(CollectionCatalog Catalog, CollectionState State,
    ImmutableDictionary<string, CollectionResult> Collections,
    ImmutableDictionary<string, CollectionSourceStatus> SourceHealth);

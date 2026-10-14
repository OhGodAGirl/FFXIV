using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;
namespace Exportaramus.Features.CollectionData;

public sealed record CollectionResult(string Label, DataType OwnershipKind, bool TotalKnown,
    CollectionSourceStatus Health, ImmutableArray<CollectionItem> Items);

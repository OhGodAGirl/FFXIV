using System.Collections.Immutable;

namespace Exportaramus.Features.CollectionData.Catalog;

public sealed record CatalogItem(uint Id, uint SourceRowId, string Name, bool IsFallbackName,
    uint? ItemId = null, ImmutableArray<uint> RequiredItemIds = default, uint? AchievementId = null);

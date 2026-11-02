using BetterGlamorPlates.Shared.CollectionData;

namespace BetterGlamorPlates.Features.GlamourPlates;

/// <summary>
/// Projects the canonical Exportaramus snapshot into one bounded
/// render list. Current and retained last-known observations can
/// contribute items; unavailable or never-observed sources remain unknown.
/// </summary>
internal static class CollectionViewPlanner
{
    internal const int MaximumVisibleItems = 2_000;

    internal static IReadOnlyList<CollectionItem> Create(
        CollectionDataSnapshot? snapshot,
        PlateSlot slot,
        string? filter,
        Func<uint, ItemDisplayFacts?> getItemFacts)
        => Create(snapshot, slot, filter, CollectionSourceFilter.All, getItemFacts);

    internal static IReadOnlyList<CollectionItem> Create(
        CollectionDataSnapshot? snapshot,
        PlateSlot slot,
        string? filter,
        CollectionSourceFilter sourceFilter,
        Func<uint, ItemDisplayFacts?> getItemFacts)
    {
        ArgumentNullException.ThrowIfNull(getItemFacts);
        if (snapshot is null)
        {
            return [];
        }

        var normalizedFilter = filter?.Trim() ?? string.Empty;
        var ownership = CreateOwnershipIndex(snapshot);
        var result = new List<CollectionItem>(Math.Min(ownership.Count, MaximumVisibleItems));
        foreach (var (itemId, evidence) in ownership)
        {
            if (!Includes(sourceFilter, evidence.Source))
            {
                continue;
            }

            var facts = getItemFacts(itemId);
            if (facts is null ||
                !PlateRules.MatchesSlot(facts.SlotMask, slot) ||
                (!string.IsNullOrEmpty(normalizedFilter) &&
                 !facts.Name.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(new CollectionItem(
                itemId,
                facts.Name,
                facts.IconId,
                evidence.Source,
                evidence.Freshness,
                CollectionProviderPolicy.DescribeFreshness(evidence.Freshness),
                facts.SlotMask));
        }

        result.Sort(static (left, right) =>
        {
            var nameOrder = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return nameOrder != 0 ? nameOrder : left.ItemId.CompareTo(right.ItemId);
        });
        if (result.Count > MaximumVisibleItems)
            result.RemoveRange(MaximumVisibleItems, result.Count - MaximumVisibleItems);
        return result.ToArray();
    }

    private static bool Includes(CollectionSourceFilter filter, CollectionSource source)
        => (filter & (source == CollectionSource.Dresser
            ? CollectionSourceFilter.Dresser : CollectionSourceFilter.Armoire)) != 0;

    internal static int CountKnownOwnedItems(CollectionDataSnapshot? snapshot)
        => snapshot is null ? 0 : CreateOwnershipIndex(snapshot).Count;

    private static Dictionary<uint, OwnershipEvidence> CreateOwnershipIndex(
        CollectionDataSnapshot snapshot)
    {
        var ownership = new Dictionary<uint, OwnershipEvidence>();
        AddSource(ownership, snapshot.Armoire, CollectionSource.Armoire);
        AddSource(ownership, snapshot.GlamourDresser, CollectionSource.Dresser);
        return ownership;
    }

    private static void AddSource(
        IDictionary<uint, OwnershipEvidence> ownership,
        CollectionSourceSnapshot source,
        CollectionSource collectionSource)
    {
        var freshness = source.Status switch
        {
            CollectionSourceStatus.Fresh => CollectionFreshness.Fresh,
            CollectionSourceStatus.Stale => CollectionFreshness.LastKnown,
            _ => (CollectionFreshness?)null,
        };
        if (freshness is null)
        {
            return;
        }

        foreach (var itemId in source.ItemIds)
        {
            var candidate = new OwnershipEvidence(collectionSource, freshness.Value);
            if (!ownership.TryGetValue(itemId, out var existing) ||
                IsPreferred(candidate, existing))
            {
                ownership[itemId] = candidate;
            }
        }
    }

    private static bool IsPreferred(OwnershipEvidence candidate, OwnershipEvidence existing)
        => candidate.Freshness <= existing.Freshness;

    private readonly record struct OwnershipEvidence(
        CollectionSource Source,
        CollectionFreshness Freshness);
}


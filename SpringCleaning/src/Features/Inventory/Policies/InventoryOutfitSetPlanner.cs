namespace SpringCleaning;

internal static class InventoryOutfitSetPlanner
{
    internal static IReadOnlyList<(uint SetId, bool IsHighQuality, InventoryDresserComponent[] Components)> BuildBatches(
        IReadOnlyList<InventoryDresserCandidate> candidates,
        IReadOnlyCollection<(uint SetId, bool IsHighQuality)> existingSets)
    {
        // An appearance is stored once, but different pieces of an outfit can have different qualities.
        // Quality partitions must exist before set preference and membership ranking are calculated.
        return candidates.Where(candidate => candidate.OutfitSetIds.Count > 0)
            .DistinctBy(candidate => candidate.Component.ItemId)
            .GroupBy(candidate => candidate.Component.IsHighQuality)
            .SelectMany(quality =>
            {
                var existing = existingSets.Where(set => set.IsHighQuality == quality.Key)
                    .Select(set => set.SetId).ToHashSet();
                var counts = quality.SelectMany(candidate => candidate.OutfitSetIds.Distinct())
                    .GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
                return quality.GroupBy(candidate => ChooseSet(candidate.OutfitSetIds, counts, existing))
                    .Select(group => (SetId: group.Key, IsHighQuality: quality.Key,
                        Components: group.Select(candidate => candidate.Component).ToArray()));
            })
            .OrderBy(batch => batch.SetId).ThenBy(batch => batch.IsHighQuality).ToArray();
    }

    public static bool ShouldSkipCompleteDuplicate(
        bool skipDuplicateOutfits,
        bool outfitAlreadyExists,
        int candidatePieceCount,
        int completeOutfitPieceCount) =>
        skipDuplicateOutfits
        && outfitAlreadyExists
        && completeOutfitPieceCount > 0
        && candidatePieceCount >= completeOutfitPieceCount;

    public static uint ChooseSet(
        IReadOnlyCollection<uint> candidateSetIds,
        IReadOnlyDictionary<uint, int> membershipCounts,
        IReadOnlySet<uint> existingSetIds)
    {
        return candidateSetIds
            .OrderByDescending(existingSetIds.Contains)
            .ThenByDescending(setId => membershipCounts.GetValueOrDefault(setId))
            .ThenBy(setId => setId)
            .First();
    }
}

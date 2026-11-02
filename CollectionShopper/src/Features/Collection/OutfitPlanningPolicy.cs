namespace CollectionShopper;

internal static class OutfitPlanningPolicy
{
    public static IReadOnlyList<OutfitPlan> BuildPlans(
        IEnumerable<Outfit> outfits,
        IReadOnlySet<uint> locallyOwnedItemIds,
        IReadOnlySet<uint> completedItemIds,
        Func<uint, bool> isMarketEligible)
    {
        ArgumentNullException.ThrowIfNull(outfits);
        ArgumentNullException.ThrowIfNull(locallyOwnedItemIds);
        ArgumentNullException.ThrowIfNull(completedItemIds);
        ArgumentNullException.ThrowIfNull(isMarketEligible);

        return outfits
            .Select(outfit => BuildPlan(
                outfit,
                locallyOwnedItemIds,
                completedItemIds,
                isMarketEligible))
            .Where(plan => plan is not null)
            .Select(plan => plan!)
            .OrderBy(plan => plan.OwnedPieceCount == 0)
            .ThenBy(plan => plan.RemainingEligiblePieceCount)
            .ThenByDescending(plan => plan.OwnedPieceCount)
            .ThenBy(plan => plan.Outfit.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static OutfitPlan? BuildPlan(
        Outfit outfit,
        IReadOnlySet<uint> locallyOwnedItemIds,
        IReadOnlySet<uint> completedItemIds,
        Func<uint, bool> isMarketEligible)
    {
        var pieces = outfit.Items
            .Where(piece => piece.Id != 0)
            .DistinctBy(piece => piece.Id)
            .ToArray();
        if (pieces.Length == 0)
            return null;

        var ownedCount = pieces.Count(piece => IsOwned(
            piece.Id,
            locallyOwnedItemIds,
            completedItemIds));
        if (ownedCount == pieces.Length)
            return null;

        var eligibleMissingPieces = pieces
            .Where(piece => !IsOwned(piece.Id, locallyOwnedItemIds, completedItemIds)
                && isMarketEligible(piece.Id))
            .ToArray();
        if (eligibleMissingPieces.Length == 0)
            return null;

        return new OutfitPlan(
            outfit,
            eligibleMissingPieces,
            ownedCount,
            eligibleMissingPieces.Length);
    }

    private static bool IsOwned(
        uint itemId,
        IReadOnlySet<uint> locallyOwnedItemIds,
        IReadOnlySet<uint> completedItemIds)
        => locallyOwnedItemIds.Contains(itemId) || completedItemIds.Contains(itemId);

}

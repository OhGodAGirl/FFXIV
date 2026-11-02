namespace CollectionShopper;

/// <summary>Replans retained metadata without repeating catalog or price requests.</summary>
internal static class ShoppingCatalogProjection
{
    internal static ShoppingCandidate[] BuildCandidates(
        IReadOnlyList<Outfit> outfits,
        IReadOnlyList<ShoppingCandidate> catalog,
        IReadOnlySet<uint> owned,
        IReadOnlySet<uint> completed,
        IReadOnlyList<ShoppingCandidate> displayed)
    {
        var outfitMetadata = catalog.Where(candidate => candidate.Category == ShoppingCategory.Outfit)
            .DistinctBy(candidate => (candidate.CollectionId, candidate.ItemId))
            .ToDictionary(candidate => (candidate.CollectionId, candidate.ItemId));
        var eligibleIds = outfitMetadata.Values.Select(candidate => candidate.ItemId).ToHashSet();
        var plans = OutfitPlanningPolicy.BuildPlans(outfits, owned, completed, eligibleIds.Contains);
        var planPositions = plans.Select((plan, priority) => (plan, priority))
            .DistinctBy(entry => entry.plan.Outfit.Id).ToDictionary(entry => entry.plan.Outfit.Id);
        var displayedPriorities = displayed.Where(candidate => candidate.Category == ShoppingCategory.Outfit)
            .DistinctBy(candidate => candidate.CollectionId)
            .ToDictionary(candidate => candidate.CollectionId, candidate => candidate.Priority);
        var nextPriority = displayed.Select(candidate => candidate.Priority).DefaultIfEmpty(-1).Max() + 1;
        var current = plans.SelectMany((plan, priority) => plan.EligibleMissingPieces.Select(piece =>
            outfitMetadata[(plan.Outfit.Id, piece.Id)] with
            {
                Priority = displayedPriorities.GetValueOrDefault(plan.Outfit.Id, nextPriority + priority),
                RemainingPieces = plan.RemainingEligiblePieceCount,
            }))
            .Concat(catalog.Where(candidate => candidate.Category != ShoppingCategory.Outfit)
                .Select(candidate => candidate with { Priority = plans.Count + candidate.Priority }));

        // Ownership-only changes preserve rows already displayed, including an active
        // purchase and owned rows revealed by the player's filters. Newly missing
        // outfit pieces are added from retained metadata; authority is classified below.
        var retained = displayed.Select(candidate =>
        {
            if (candidate.Category != ShoppingCategory.Outfit)
                return candidate;
            return planPositions.TryGetValue(candidate.CollectionId, out var entry)
                ? candidate with { RemainingPieces = entry.plan.RemainingEligiblePieceCount }
                : candidate;
        });
        return retained.Concat(current).DistinctBy(candidate => candidate.ItemId).ToArray();
    }

    internal static ShoppingItem[] Project(
        IReadOnlyList<ShoppingCandidate> candidates,
        CollectionDataOwnershipState ownership,
        IReadOnlyDictionary<uint, MarketPriceQuote> prices)
        => candidates.Select(candidate =>
        {
            var classification = ownership.Classify(
                candidate.Category, candidate.ItemId, candidate.CollectionId, candidate.MappingValid);
            return new ShoppingItem(
                candidate.ItemId,
                candidate.ItemName,
                candidate.CollectionId,
                candidate.CollectionName,
                candidate.CollectionIconPath,
                candidate.Patch,
                candidate.Source,
                candidate.IconId,
                classification.Known,
                classification.SuppressPurchase,
                prices.GetValueOrDefault(candidate.ItemId),
                candidate.Category,
                candidate.Priority,
                candidate.RemainingPieces,
                candidate.MappingValid);
        }).ToArray();
}

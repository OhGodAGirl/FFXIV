namespace BetterGlamorPlates.Features.GlamourPlates;

internal static class CollectionSourceResolver
{
    internal static NativeCollectionItem? ResolveClosest(IEnumerable<NativeCollectionItem> collection, SavedPlateItem desired)
    {
        NativeCollectionItem? best = null;
        var bestChanges = int.MaxValue;
        foreach (var item in collection)
        {
            if (item.ItemId != desired.ItemId) continue;
            var changes = (item.Stain0Id == desired.Stain0Id ? 0 : 1) + (item.Stain1Id == desired.Stain1Id ? 0 : 1);
            if (changes < bestChanges || (changes == bestChanges && best is not null
                && (item.Source < best.Source || (item.Source == best.Source && item.SourceId < best.SourceId))))
            {
                best = item;
                bestChanges = changes;
            }
        }
        return best;
    }

    internal static NativeCollectionItem? ResolveExact(
        IEnumerable<NativeCollectionItem> collection,
        SavedPlateItem item)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(item);

        var dresser = collection.FirstOrDefault(candidate =>
            candidate.Source == CollectionSource.Dresser
            && candidate.ItemId == item.ItemId
            && candidate.Stain0Id == item.Stain0Id
            && candidate.Stain1Id == item.Stain1Id);
        if (dresser is not null)
            return dresser;

        // Armoire items are undyed. Never substitute one for a saved dyed item:
        // doing so would silently change the requested appearance.
        if (item.Stain0Id != 0 || item.Stain1Id != 0)
            return null;

        return collection.FirstOrDefault(candidate =>
            candidate.Source == CollectionSource.Armoire
            && candidate.ItemId == item.ItemId);
    }
}

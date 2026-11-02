namespace BetterGlamorPlates.Features.GlamourPlates;

/// <summary>Projects copied action-time source facts without reading native memory.</summary>
internal static class NativeCollectionProjection
{
    internal static void AddDresser(List<NativeCollectionItem> result, uint rawId, uint sourceId,
        byte stain0, byte stain1, IReadOnlyList<(uint ItemId, bool Unlocked)>? setItems)
    {
        if (rawId == 0) return;
        if (setItems is not null)
        {
            foreach (var (itemId, unlocked) in setItems)
                if (itemId != 0 && unlocked)
                    result.Add(new(itemId, CollectionSource.Dresser, sourceId, 0, 0));
            return;
        }
        result.Add(new(rawId % 1_000_000, CollectionSource.Dresser, sourceId, stain0, stain1));
    }

    internal static void AddArmoire(List<NativeCollectionItem> result, uint row, uint item, bool owned)
    {
        if (row == 0 || item == 0 || !owned) return;
        result.Add(new(item, CollectionSource.Armoire, row, 0, 0));
    }

    internal static NativeCollectionSnapshot Complete(IEnumerable<NativeCollectionItem> items,
        bool dresserLoaded, bool armoireLoaded, DateTime captured)
        => new(items.OrderBy(item => item.Source).ThenBy(item => item.SourceId).ThenBy(item => item.ItemId).ToArray(),
            dresserLoaded, armoireLoaded, captured);
}

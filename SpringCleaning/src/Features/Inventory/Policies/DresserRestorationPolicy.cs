using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal static class DresserRestorationPolicy
{
    internal static uint[] Components(DresserAlertEntry entry) => entry.IsOutfit
        ? entry.Components.Select(component => component.ItemId).ToArray() : [entry.ItemId];

    internal static bool CanPlan(CollectionDataSnapshot? snapshot, DresserAlertEntry entry)
    {
        if (!DresserCompositionPolicy.IsReady(snapshot))
            return false;
        var components = Components(entry);
        if (components.Length == 0 || components.Any(id =>
                CollectionOwnershipPolicy.GetFact(snapshot, CollectionSourceName.Armoire, id) != CollectionOwnershipFact.Owned))
            return false;
        return snapshot!.DresserEntries!.Value.Any(current => current.EntryIndex == entry.PrismBoxIndex
            && current.RawItemId == entry.RawItemId
            && (entry.IsOutfit
                ? current.OutfitId is not null && current.Components.SequenceEqual(entry.Components)
                : current.OutfitId is null && current.BaseItemId == entry.ItemId));
    }
}

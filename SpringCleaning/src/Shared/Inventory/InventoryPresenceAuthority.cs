namespace SpringCleaning.Shared.Inventory;

// CollectionData groups equipped items with the Armoury Chest. Inventory V1
// exposes them separately; reconciliation preserves that provider grouping,
// while action locators continue to use only their explicitly selected source.
internal static class InventoryPresenceAuthority
{
    internal static bool IsReady(InventorySnapshot? snapshot, string character,
        string collectionSource)
    {
        if (snapshot is null || snapshot.Character != character)
            return false;

        var primaryKind = collectionSource == "inventory" ? "Inventory" : "ArmoryChest";
        var primary = snapshot.Locations.FirstOrDefault(location => location.Kind == primaryKind);
        if (primary is null || primary.State != "Observed")
            return false;

        if (collectionSource == "armoryChest")
        {
            var equipped = snapshot.Locations.FirstOrDefault(location => location.Kind == "Equipped");
            if (equipped is null || equipped.State != "Observed")
                return false;
        }

        return true;
    }

    // The two provider feeds publish independently. A removed item must disappear
    // from this projection without making every remaining carried item unavailable.
    // Keep canonical scope (which excludes crystals) and current observed presence.
    internal static IEnumerable<uint> CurrentIds(InventorySnapshot snapshot,
        string collectionSource, IEnumerable<uint> canonicalIds)
    {
        var kinds = collectionSource == "inventory"
            ? new[] { "Inventory" } : new[] { "ArmoryChest", "Equipped" };
        var current = snapshot.Locations.Where(location => kinds.Contains(location.Kind))
            .SelectMany(location => location.Items).Select(item => item.ItemId).ToHashSet();
        return canonicalIds.Where(current.Contains);
    }
}

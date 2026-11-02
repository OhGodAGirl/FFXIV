namespace SpringCleaning.Shared.Inventory;

internal static class InventoryCopyAuthority
{
    internal static bool Matches(InventorySnapshot? snapshot, string source, uint itemId, uint? normal, uint? highQuality)
    {
        if (snapshot is null || normal is null || highQuality is null)
            return false;
        var location = snapshot.Locations.FirstOrDefault(value => value.Kind == source);
        if (location?.State != "Observed")
            return false;
        var item = location.Items.FirstOrDefault(value => value.ItemId == itemId);
        return item is not null && item.Normal == normal && item.HighQuality == highQuality;
    }
}

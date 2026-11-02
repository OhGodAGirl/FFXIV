namespace CollectionShopper;

internal static class MarketBoardContext
{
    internal static bool IsBrowsing(Func<string, bool> isAddonVisible)
    {
        ArgumentNullException.ThrowIfNull(isAddonVisible);
        var itemSearchVisible = isAddonVisible("ItemSearch");
        var itemSearchResultVisible = isAddonVisible("ItemSearchResult");
        var retainerSellVisible = isAddonVisible("RetainerSell");
        return IsBrowsing(itemSearchVisible, itemSearchResultVisible, retainerSellVisible);
    }

    internal static bool IsBrowsing(
        bool itemSearchVisible,
        bool itemSearchResultVisible,
        bool retainerSellVisible)
        => !retainerSellVisible && (itemSearchVisible || itemSearchResultVisible);

}

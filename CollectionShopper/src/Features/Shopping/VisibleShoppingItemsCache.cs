namespace CollectionShopper;

/// <summary>Caches the displayed projection until a visible input or ownership revision changes.</summary>
internal sealed class VisibleShoppingItemsCache
{
    private Key? previous;
    private ShoppingItem[] items = [];

    internal ShoppingItem[] Get(Key key, IEnumerable<ShoppingItem> source, IReadOnlySet<uint> pending)
    {
        if (previous == key)
            return items;

        items = ShoppingProjection.BuildVisible(source, key.Page, key.Filter, key.HideLocallyOwned,
            true, pending, key.CurrentItemId, key.AllowHighQuality);
        previous = key;
        return items;
    }

    internal readonly record struct Key(int ItemsRevision, int CompletionRevision, ShopperPage Page,
        string Filter, bool HideLocallyOwned, bool AllowHighQuality, uint CurrentItemId);
}

namespace CollectionShopper;

/// <summary>Stops delivering a drained batch when a callback tears down the adapter.</summary>
internal static class ShoppingEventDelivery
{
    internal static void Dispatch(ShoppingEventQueue queue, Func<bool> disposed,
        Action overflowed, Action<ShoppingOfferings> offerings, Action<ShoppingPurchase> purchased, Action updated)
    {
        if (disposed())
            return;
        var batch = queue.TakePending(out var overflow);
        if (overflow)
            overflowed();
        foreach (var marketEvent in batch)
        {
            if (disposed())
                return;
            if (marketEvent.Offerings is { } offering)
                offerings(offering);
            if (marketEvent.Purchase is { } purchase)
                purchased(purchase);
        }
        if (!disposed())
            updated();
    }
}

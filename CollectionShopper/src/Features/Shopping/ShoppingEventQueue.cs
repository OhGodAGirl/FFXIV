namespace CollectionShopper;

/// <summary>Transfers bounded immutable packet snapshots to the framework thread in arrival order.</summary>
internal sealed class ShoppingEventQueue : IDisposable
{
    internal const int MaximumPendingEvents = 64;
    private readonly object gate = new();
    private readonly Queue<ShoppingMarketEvent> pending = new();
    private bool overflowed;
    private bool disposed;

    internal void Enqueue(ShoppingOfferings offerings)
        => Enqueue(new ShoppingMarketEvent(
            offerings with { ItemIds = offerings.ItemIds.ToArray() }, null));

    internal void Enqueue(ShoppingPurchase purchase)
        => Enqueue(new ShoppingMarketEvent(null, purchase));

    internal ShoppingMarketEvent[] TakePending(out bool overflow)
    {
        lock (gate)
        {
            overflow = overflowed;
            overflowed = false;
            var batch = pending.ToArray();
            pending.Clear();
            return batch;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            pending.Clear();
            overflowed = false;
        }
    }

    private void Enqueue(ShoppingMarketEvent marketEvent)
    {
        lock (gate)
        {
            if (disposed || overflowed)
                return;
            if (pending.Count >= MaximumPendingEvents)
            {
                // Losing a purchase confirmation must stop the run, never silently advance it.
                pending.Clear();
                overflowed = true;
                return;
            }
            pending.Enqueue(marketEvent);
        }
    }
}

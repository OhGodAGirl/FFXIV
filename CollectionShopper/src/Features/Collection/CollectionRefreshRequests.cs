namespace CollectionShopper;

/// <summary>Keeps requests pending until the preceding refresh has fully settled.</summary>
internal sealed class CollectionRefreshRequests : IDisposable
{
    private PendingRequests? pending = new();

    internal void RequestCatalog(bool operationBusy)
    {
        if (pending is { } current && !(operationBusy && current.CurrentCatalogActive))
            current.CatalogRequested = true;
    }

    internal void RequestPrices()
    {
        if (pending is { } current)
            current.PricesRequested = true;
    }

    internal void Invalidate(bool requestCatalog)
    {
        if (pending is { } current)
        {
            current.CurrentCatalogActive = false;
            current.CatalogRequested = requestCatalog;
            current.PricesRequested = false;
        }
    }

    internal bool TryTakeCatalog(bool collectionLoaded, bool operationBusy)
    {
        if (pending is not { } current || operationBusy
            || !(current.CatalogRequested || current.PricesRequested && !collectionLoaded))
            return false;
        current.CatalogRequested = false;
        current.PricesRequested = false;
        current.CurrentCatalogActive = true;
        return true;
    }

    internal bool TryTakePrices(bool operationBusy)
    {
        if (pending is not { } current || operationBusy || !current.PricesRequested)
            return false;
        current.PricesRequested = false;
        current.CurrentCatalogActive = false;
        return true;
    }

    public void Dispose() => pending = null;

    private sealed class PendingRequests
    {
        internal bool CurrentCatalogActive;
        internal bool CatalogRequested;
        internal bool PricesRequested;
    }
}

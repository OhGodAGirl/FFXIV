namespace CollectionShopper;

/// <summary>
/// Decides whether a requested Exportaramus ownership refresh may start.
/// Public-catalog readiness is deliberately absent: ownership must refresh as
/// soon as the character and provider are available.
/// </summary>
internal static class CollectionOwnershipRefreshPolicy
{
    internal static bool CanStart(bool disposed, bool refreshInProgress)
        => !disposed && !refreshInProgress;

    /// <summary>
    /// The first accepted ownership snapshot must also build the shopping catalog
    /// when automatic refresh is enabled. Later notifications can update the
    /// existing item projection without repeating catalog and price I/O.
    /// </summary>
    internal static bool RequiresCatalogRefresh(bool collectionLoaded, bool automaticallyRefresh)
        => !collectionLoaded && automaticallyRefresh;

}

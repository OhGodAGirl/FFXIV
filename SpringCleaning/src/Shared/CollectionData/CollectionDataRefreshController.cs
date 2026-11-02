namespace SpringCleaning.Shared.CollectionData;

internal sealed class CollectionDataRefreshController : IDisposable
{
    private ulong activeContentId;
    private ulong refreshingContentId;
    private CollectionDataSnapshot? latestSnapshot;
    private CollectionDataFailure lastFailure = CollectionDataFailure.NoCharacter;
    private bool refreshRequested;
    private bool refreshInProgress;
    private bool disposed;

    internal CollectionDataStatusSnapshot Snapshot { get; private set; } =
                CollectionDataStatusPolicy.Create(
                    null,
                    CollectionDataFailure.NoCharacter,
                    refreshRequested: false,
                    refreshInProgress: false);

    internal CollectionDataSnapshot? LatestSnapshot => latestSnapshot;

    internal CollectionDataObservation Observe(ulong contentId, bool providerRequested, bool invalidated = true)
    {
        if (disposed)
            return default;

        var clearTransport = default(bool);
        if (activeContentId != contentId)
        {
            if (activeContentId != 0)
                clearTransport = true;
            if (latestSnapshot is not null)
                clearTransport = true;
            activeContentId = contentId;
            latestSnapshot = null;
            refreshRequested = contentId != 0;
            lastFailure = contentId == 0
                ? CollectionDataFailure.NoCharacter
                : CollectionDataFailure.None;
        }

        if (contentId == 0)
        {
            refreshRequested = default;
            Publish();
            return new CollectionDataObservation(clearTransport, null);
        }

        if (providerRequested)
        {
            refreshRequested = true;
            if (invalidated)
                latestSnapshot = null;
        }

        var refreshRequest = TryStartRefresh();
        Publish();
        return new CollectionDataObservation(clearTransport, refreshRequest);
    }

    internal CollectionDataRefreshRequest? Complete(
        ulong refreshedContentId,
        CollectionDataParseResult result,
        CollectionDataSnapshot? latestFromTransport)
    {
        if (!refreshInProgress || refreshingContentId != refreshedContentId)
            return null;

        refreshingContentId = 0;
        refreshInProgress = false;
        if (activeContentId == refreshedContentId)
        {
            if (result.Accepted && !refreshRequested)
            {
                latestSnapshot = result.Snapshot;
            }
            else
                latestSnapshot = null;

            lastFailure = result.Failure;
        }

        var refreshRequest = TryStartRefresh();
        Publish();
        return refreshRequest;
    }

    public void Dispose()
    {
        disposed = true;
        activeContentId = 0;
        refreshingContentId = 0;
        latestSnapshot = null;
        refreshRequested = false;
        refreshInProgress = false;
        lastFailure = CollectionDataFailure.Disposed;
        Publish();
    }

    private CollectionDataRefreshRequest? TryStartRefresh()
    {
        if (!refreshRequested || refreshInProgress || activeContentId == 0)
            return null;

        refreshRequested = false;
        refreshInProgress = true;
        refreshingContentId = activeContentId;
        return new CollectionDataRefreshRequest(activeContentId);
    }

    private void Publish()
    {
        Snapshot = CollectionDataStatusPolicy.Create(
            latestSnapshot,
            lastFailure,
            refreshRequested,
            refreshInProgress);
    }
}

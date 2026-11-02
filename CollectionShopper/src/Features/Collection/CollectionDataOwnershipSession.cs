namespace CollectionShopper;

internal sealed class CollectionDataOwnershipSession : IDisposable
{
    private ulong activeContentId;
    private int version;
    private bool disposed;

    internal CollectionDataOwnershipState State { get; private set; } = CollectionDataOwnershipPolicy.NotChecked;

    internal int Begin(ulong contentId)
    {
        if (disposed)
            return version;

        if (activeContentId != contentId)
        {
            activeContentId = contentId;
            State = CollectionDataOwnershipPolicy.NotChecked;
            version++;
        }

        return version;
    }

    internal bool TryPublish(
        ulong contentId,
        int publishVersion,
        CollectionDataOwnershipState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (disposed)
            return false;
        if (contentId == 0)
            return false;
        if (activeContentId != contentId)
            return false;
        if (version != publishVersion)
            return false;

        State = state;
        return true;
    }

    internal bool IsCurrent(ulong contentId, int publishVersion)
        => !disposed && contentId == activeContentId && publishVersion == version;

    internal void Invalidate(ulong nextContentId = 0)
    {
        if (disposed)
            return;

        activeContentId = nextContentId;
        State = CollectionDataOwnershipPolicy.NotChecked;
        version++;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        activeContentId = 0;
        State = CollectionDataOwnershipPolicy.NotChecked;
        version++;
    }

}

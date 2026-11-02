using System.Threading;
using Exportaramus.ConsumerKit.V1_2;
using SpringCleaning.Shared.Inventory;

namespace SpringCleaning.Shared.CollectionData;

// Refresh runs in the bootstrap's single owned worker. Event callbacks only
// invalidate publication; no parser or provider call runs in Draw or callbacks.
internal sealed class CollectionDataClient : IDisposable
{
    private readonly object stateGate = new();
    private readonly ICollectionDataTransport transport;
    private CollectionDataSnapshot? latest;
    private CollectionDataSnapshot? ordering;
    private ulong activeContentId;
    private long generation;
    private long presentationEpoch;
    private int disposed;
    private int refreshInProgress;
    private int refreshRequested = 1;
    private DateTimeOffset nextDiscovery;

    internal CollectionDataClient(ICollectionDataTransport transport)
    {
        this.transport = transport;
        transport.SnapshotChanged += OnSnapshotChanged;
        transport.SnapshotWithdrawn += OnSnapshotWithdrawn;
    }

    internal CollectionDataSnapshot? Latest
    {
        get { lock (stateGate) return latest; }
    }

    // Changed notices may briefly retain display models; withdrawal/failure never does.
    internal long PresentationEpoch
    {
        get { lock (stateGate) return presentationEpoch; }
    }

    internal bool TryTakeRefreshRequest(DateTimeOffset nowUtc)
    {
        if (Volatile.Read(ref disposed) != 0)
            return false;
        if (Interlocked.Exchange(ref refreshRequested, 0) == 0 && nowUtc < nextDiscovery)
            return false;
        nextDiscovery = nowUtc.AddSeconds(5);
        return true;
    }

    internal CollectionDataParseResult Refresh(ulong expectedContentId, DateTimeOffset nowUtc)
    {
        if (Volatile.Read(ref disposed) != 0)
            return CollectionDataParseResult.Reject(CollectionDataFailure.Disposed);
        if (expectedContentId == 0)
        {
            ClearCharacter();
            return CollectionDataParseResult.Reject(CollectionDataFailure.NoCharacter);
        }
        if (Interlocked.CompareExchange(ref refreshInProgress, 1, 0) != 0)
            return CollectionDataParseResult.Reject(CollectionDataFailure.Busy);
        long scope;
        CollectionDataSnapshot? previous;
        lock (stateGate)
        {
            if (activeContentId != expectedContentId)
            {
                activeContentId = expectedContentId;
                presentationEpoch++;
                latest = ordering = null;
            }
            scope = generation;
            previous = ordering;
        }
        try
        {
            var before = CollectionDataSemanticValidator.ValidateCapabilities(transport.ReadCapabilities());
            if (!before.Success)
                return Reject(scope, CollectionDataFailure.UnsupportedProtocol);
            var payload = transport.ReadSnapshot();
            InventorySnapshot? inventory = null;
            try
            {
                InventoryContract.ValidateCapabilities(transport.ReadInventoryCapabilities());
                inventory = InventoryContract.ReadSnapshot(transport.ReadInventorySnapshot(), expectedContentId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch
            {
                // Dresser/Armoire evidence can remain usable when carried locations
                // fail. The projection explicitly marks those locations unavailable.
            }
            var parsed = CollectionDataSnapshotParser.Parse(payload, expectedContentId, nowUtc, previous, inventory);
            if (!parsed.Accepted)
                return Reject(scope, parsed.Failure);
            var after = CollectionDataSemanticValidator.ValidateCapabilities(transport.ReadCapabilities());
            if (!after.Success || before.Value!.ProviderSessionId != after.Value!.ProviderSessionId
                || before.Value.GameDataVersion != after.Value.GameDataVersion
                || parsed.Snapshot!.ProviderSessionId != after.Value.ProviderSessionId)
                return Reject(scope, CollectionDataFailure.StaleCompletion);
            lock (stateGate)
            {
                if (Volatile.Read(ref disposed) != 0 || generation != scope || activeContentId != expectedContentId)
                    return CollectionDataParseResult.Reject(CollectionDataFailure.StaleCompletion);
                if (ordering is not null && ordering.ProviderSessionId != parsed.Snapshot.ProviderSessionId)
                    presentationEpoch++;
                latest = ordering = parsed.Snapshot;
            }
            return parsed;
        }
        catch
        {
            return Reject(scope, CollectionDataFailure.ProviderUnavailable);
        }
        finally
        {
            Volatile.Write(ref refreshInProgress, 0);
        }
    }

    internal void ClearCharacter()
    {
        lock (stateGate)
        {
            activeContentId = 0;
            latest = ordering = null;
            generation++;
            presentationEpoch++;
        }
        Interlocked.Exchange(ref refreshRequested, 1);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        transport.SnapshotChanged -= OnSnapshotChanged;
        transport.SnapshotWithdrawn -= OnSnapshotWithdrawn;
        ClearCharacter();
        transport.Dispose();
    }

    private CollectionDataParseResult Reject(long scope, CollectionDataFailure reason)
    {
        lock (stateGate)
        {
            if (scope == generation)
            {
                latest = ordering = null;
                presentationEpoch++;
            }
        }
        return CollectionDataParseResult.Reject(reason);
    }

    private void OnSnapshotChanged() => Invalidate(false);

    private void OnSnapshotWithdrawn() => Invalidate(true);

    private void Invalidate(bool resetOrdering)
    {
        lock (stateGate)
        {
            if (Volatile.Read(ref disposed) != 0)
                return;
            latest = null;
            if (resetOrdering)
            {
                ordering = null;
                presentationEpoch++;
            }
            generation++;
        }
        Interlocked.Exchange(ref refreshRequested, 1);
    }
}

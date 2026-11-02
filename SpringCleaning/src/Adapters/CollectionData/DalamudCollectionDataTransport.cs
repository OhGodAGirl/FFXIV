using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Exportaramus.ConsumerKit.V1_2;
using SpringCleaning.Shared.Inventory;

namespace SpringCleaning.Shared.CollectionData;

// Gate acquisition/subscriptions belong to this plugin. Reads and disposal are
// serialized by its owned refresh lifetime; event payloads are never retained.
internal sealed class DalamudCollectionDataTransport : ICollectionDataTransport
{
    private readonly ICallGateSubscriber<string> capabilities;
    private readonly ICallGateSubscriber<string> snapshot;
    private readonly ICallGateSubscriber<string> inventoryCapabilities;
    private readonly ICallGateSubscriber<string> inventorySnapshot;
    private readonly (ICallGateSubscriber<string, object?> Gate, Action<string> Handler)[] subscriptions;
    private int entered;
    private int disposed;

    internal DalamudCollectionDataTransport(IDalamudPluginInterface plugin)
    {
        capabilities = plugin.GetIpcSubscriber<string>(CollectionDataGates.GetCapabilities);
        snapshot = plugin.GetIpcSubscriber<string>(CollectionDataGates.GetSnapshot);
        inventoryCapabilities = plugin.GetIpcSubscriber<string>("Exportaramus.Inventory.V1.GetCapabilities");
        inventorySnapshot = plugin.GetIpcSubscriber<string>("Exportaramus.Inventory.V1.GetSnapshot");
        subscriptions =
        [
            (plugin.GetIpcSubscriber<string, object?>(CollectionDataGates.SnapshotChanged), payload => Changed(payload, CollectionDataWireContract.MaximumNoticeBytes)),
            (plugin.GetIpcSubscriber<string, object?>(CollectionDataGates.SnapshotWithdrawn), payload => Withdrawn(payload, CollectionDataWireContract.MaximumNoticeBytes)),
            (plugin.GetIpcSubscriber<string, object?>("Exportaramus.Inventory.V1.SnapshotChanged"), payload => Changed(payload, InventoryContract.ByteLimit)),
            (plugin.GetIpcSubscriber<string, object?>("Exportaramus.Inventory.V1.SnapshotWithdrawn"), payload => Withdrawn(payload, InventoryContract.ByteLimit)),
        ];
    }

    public event Action? SnapshotChanged;

    public event Action? SnapshotWithdrawn;

    public string ReadCapabilities()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        try
        {
            while (entered < subscriptions.Length)
            {
                var subscription = subscriptions[entered];
                subscription.Gate.Subscribe(subscription.Handler);
                entered++;
            }
        }
        catch (Exception failure)
        {
            try { Detach(); }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
        }
        return capabilities.InvokeFunc();
    }

    public string ReadSnapshot() => snapshot.InvokeFunc();

    public string ReadInventoryCapabilities() => inventoryCapabilities.InvokeFunc();

    public string ReadInventorySnapshot() => inventorySnapshot.InvokeFunc();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
            Detach();
    }

    private void Changed(string payload, int limit)
    {
        if (Volatile.Read(ref disposed) == 0 && payload is not null && payload.Length <= limit)
            SnapshotChanged?.Invoke();
    }

    private void Withdrawn(string payload, int limit)
    {
        if (Volatile.Read(ref disposed) == 0 && payload is not null && payload.Length <= limit)
            SnapshotWithdrawn?.Invoke();
    }

    private void Detach()
    {
        List<Exception>? errors = null;
        while (entered > 0)
        {
            var subscription = subscriptions[--entered];
            try { subscription.Gate.Unsubscribe(subscription.Handler); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors is not null)
            throw new AggregateException("Exportaramus notice subscriptions could not be removed.", errors);
    }
}

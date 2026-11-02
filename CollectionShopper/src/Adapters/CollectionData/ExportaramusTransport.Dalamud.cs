using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Exportaramus.ConsumerKit.V1_2;

namespace CollectionShopper;

internal sealed class ExportaramusTransport : ICollectionDataTransport
{
    private readonly ICallGateSubscriber<string> capabilities;
    private readonly ICallGateSubscriber<string> snapshot;
    private readonly ICallGateSubscriber<string, object?> changed;
    private readonly ICallGateSubscriber<string, object?> withdrawn;
    private readonly ICallGateSubscriber<string, uint, string, string> report;
    private readonly Action<string> changedHandler;
    private readonly Action<string> withdrawnHandler;
    private int disposed;

    internal ExportaramusTransport(IDalamudPluginInterface pluginInterface,
        Action<string> changedHandler, Action<string> withdrawnHandler)
    {
        this.changedHandler = changedHandler;
        this.withdrawnHandler = withdrawnHandler;
        capabilities = pluginInterface.GetIpcSubscriber<string>(CollectionDataGates.GetCapabilities);
        snapshot = pluginInterface.GetIpcSubscriber<string>(CollectionDataGates.GetSnapshot);
        changed = pluginInterface.GetIpcSubscriber<string, object?>(CollectionDataGates.SnapshotChanged);
        withdrawn = pluginInterface.GetIpcSubscriber<string, object?>(CollectionDataGates.SnapshotWithdrawn);
        report = pluginInterface.GetIpcSubscriber<string, uint, string, string>(CollectionDataGates.ReportOwnershipEvidence);
        changed.Subscribe(changedHandler);
        try
        {
            withdrawn.Subscribe(withdrawnHandler);
        }
        catch
        {
            changed.Unsubscribe(changedHandler);
            throw;
        }
    }

    public string ReadCapabilities() => capabilities.InvokeFunc();
    public string ReadSnapshot() => snapshot.InvokeFunc();
    public string Report(string contentId, uint itemId, string reporter) => report.InvokeFunc(contentId, itemId, reporter);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        Exception? failure = null;
        try { changed.Unsubscribe(changedHandler); }
        catch (Exception exception) { failure = exception; }
        try { withdrawn.Unsubscribe(withdrawnHandler); }
        catch (Exception exception) { failure ??= exception; }
        if (failure is not null)
            throw new InvalidOperationException("Exportaramus subscriptions could not be removed.", failure);
    }

}

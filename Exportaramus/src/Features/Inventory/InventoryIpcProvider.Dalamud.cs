using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Exportaramus.Features.Inventory;

public sealed class InventoryIpcProvider : IDisposable
{
    public const string Prefix = "Exportaramus.Inventory.V1.";
    private readonly ICallGateProvider<string> capabilities;
    private readonly ICallGateProvider<string> snapshot;
    private readonly ICallGateProvider<uint, string> item;
    private readonly ICallGateProvider<string, object?> changed;
    private readonly ICallGateProvider<string, object?> withdrawn;
    private InventorySnapshot? current;
    private string prepared = InventoryQuery.Snapshot(null);
    private bool disposed;
    public InventoryIpcProvider(IDalamudPluginInterface plugin)
    {
        capabilities = plugin.GetIpcProvider<string>(Prefix + "GetCapabilities");
        snapshot = plugin.GetIpcProvider<string>(Prefix + "GetSnapshot");
        item = plugin.GetIpcProvider<uint, string>(Prefix + "GetItem");
        changed = plugin.GetIpcProvider<string, object?>(Prefix + "SnapshotChanged");
        withdrawn = plugin.GetIpcProvider<string, object?>(Prefix + "SnapshotWithdrawn");
        try
        {
            capabilities.RegisterFunc(() => InventoryQuery.Capabilities);
            snapshot.RegisterFunc(() => Volatile.Read(ref prepared));
            item.RegisterFunc(id => InventoryQuery.Item(Volatile.Read(ref current), id));
        }
        catch { Dispose(); throw; }
    }
    public void Update(InventorySnapshot? value)
    {
        if (disposed || ReferenceEquals(value, current)) return;
        var json = InventoryQuery.Snapshot(value);
        Volatile.Write(ref current, value); Volatile.Write(ref prepared, json);
        try { if (value is null) withdrawn.SendMessage(json); else changed.SendMessage(json); } catch (Exception) { }
    }
    public void Dispose()
    {
        if (disposed) return;
        Update(null); disposed = true;
        try { capabilities.UnregisterFunc(); } finally { try { snapshot.UnregisterFunc(); } finally { item.UnregisterFunc(); } }
    }
}

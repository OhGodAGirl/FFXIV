using System.Collections.Immutable;

namespace Exportaramus.Features.Inventory;

public interface IRetainerGuidePort
{
    bool IsSafe(ulong contentId);
    bool ListReady { get; }
    bool InventoryVisible { get; }
    bool OpenBell();
    bool TryReadRoster(out ImmutableArray<InventoryRetainer> roster);
    bool SelectRetainer(ulong retainerId);
    bool OpenInventory(ulong retainerId);
    bool CaptureInventory(ulong contentId, ulong retainerId);
    bool CloseInventory(ulong retainerId);
    bool QuitRetainer(ulong retainerId);
    bool CloseOverviewAndInventory();
}

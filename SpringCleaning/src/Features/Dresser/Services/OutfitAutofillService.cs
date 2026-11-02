using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed class OutfitAutofillService : IDisposable
{
    private static readonly TimeSpan RefreshSuppressionInterval = TimeSpan.FromMilliseconds(500);
    private readonly IAddonLifecycle addonLifecycle;
    private readonly CollectionDataRefreshController collectionData;
    private readonly MigrationController controller;
    private readonly IPluginLog log;
    private DateTime ignoreRefreshUntil;
    private bool disposed;

    public OutfitAutofillService(
                IAddonLifecycle addonLifecycle,
                CollectionDataRefreshController collectionData,
                MigrationController controller,
                IPluginLog log)
    {
        this.addonLifecycle = addonLifecycle;
        this.collectionData = collectionData;
        this.controller = controller;
        this.log = log;
        addonLifecycle.RegisterListener(
            AddonEvent.PostRefresh,
            "MiragePrismPrismSetConvert",
            OnPostRefresh);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        addonLifecycle.UnregisterListener(
            AddonEvent.PostRefresh,
            "MiragePrismPrismSetConvert",
            OnPostRefresh);
    }

    private unsafe void OnPostRefresh(AddonEvent _, AddonArgs args)
    {
        if (disposed
            || DateTime.UtcNow < ignoreRefreshUntil
            || args is not AddonRefreshArgs refreshArgs)
            return;
        if (controller.IsActive)
            return;

        var values = (AtkValue*)refreshArgs.AtkValues;
        if (values == null || refreshArgs.AtkValueCount == 0 || (values[0].UInt & 4) == 0)
            return;

        var agent = AgentMiragePrismPrismSetConvert.Instance();
        if (agent == null || agent->Data == null)
            return;

        var changed = false;
        foreach (ref var setItem in agent->Data->Items)
        {
            if (setItem.ItemId == 0
                || setItem.InventoryType != InventoryType.Invalid
                || CollectionOwnershipPolicy.GetFact(collectionData.LatestSnapshot,
                    CollectionSourceName.Inventory, setItem.ItemId) != CollectionOwnershipFact.Owned
                || CollectionOwnershipPolicy.GetFact(
                    collectionData.LatestSnapshot,
                    CollectionSourceName.GlamourDresser,
                    setItem.ItemId) != CollectionOwnershipFact.Missing)
            {
                continue;
            }

            if (TryFindInventorySlot(setItem.ItemId, out var container, out var slot))
            {
                setItem.InventoryType = container;
                setItem.Slot = slot;
                changed = true;
            }
        }

        if (!changed)
            return;

        ignoreRefreshUntil = DateTime.UtcNow.Add(RefreshSuppressionInterval);
        agent->ValidateItems();
        log.Debug("Auto-selected missing inventory pieces for outfit conversion.");
    }

    private static unsafe bool TryFindInventorySlot(
        uint rawItemId,
        out InventoryType inventoryType,
        out uint slotIndex)
    {
        var inventory = InventoryManager.Instance();
        if (inventory != null)
        {
            for (var containerIndex = 0; containerIndex < 4; containerIndex++)
            {
                var containerType = (InventoryType)containerIndex;
                var container = inventory->GetInventoryContainer(containerType);
                if (container == null || !container->IsLoaded)
                    continue;

                for (var index = 0; index < container->Size; index++)
                {
                    var item = container->GetInventorySlot(index);
                    if (item != null && !item->IsEmpty() && item->GetItemId() == rawItemId
                        && DresserScanner.IsInGearset(item) == false)
                    {
                        inventoryType = containerType;
                        slotIndex = (uint)index;
                        return true;
                    }
                }
            }
        }

        inventoryType = InventoryType.Invalid;
        slotIndex = 0;
        return false;
    }
}

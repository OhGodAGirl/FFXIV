using BetterGlamorPlates.Features.Diagnostics;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMiragePrismMiragePlateData;

namespace BetterGlamorPlates.Features.GlamourPlates;

internal sealed partial class NativePlateEditor
{
    private static readonly InventoryType[] DyeInventories =
        [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];

    private unsafe PlateDyePlan PrepareDyes(PlateLoadPlan plan, AgentMiragePrismMiragePlate* agent)
    {
        // Existing pending inventory allocations belong to native Save. Do not reuse them.
        var pending = NativeDyeStatePolicy.RejectPending(index => HasPendingDye(agent->Data->CurrentItems[index]), diagnostics);
        if (pending is not null) return pending;

        var dyeItems = DyePreparation.ReagentMap(plan, id => dataManager.GetExcelSheet<Stain>().TryGetRow(id, out var dye)
            ? dye.Item.Select(item => item.RowId).ToArray() : []);
        var inventoryFacts = new List<DyeInventoryFact>(140);
        var inventory = InventoryManager.Instance();
        if (inventory != null && dyeItems.Count > 0)
            foreach (var type in DyeInventories)
            {
                var container = inventory->GetInventoryContainer(type);
                if (container == null || !DyePreparation.CanReadInventory(container->IsLoaded, container->Size)) continue;
                for (var index = 0; index < container->Size; index++)
                {
                    var item = container->GetInventorySlot(index);
                    if (item != null) inventoryFacts.Add(new((uint)type, (ushort)index, item->ItemId, item->Quantity));
                }
            }
        var supplies = DyePreparation.Supplies(dyeItems, inventoryFacts);
        var result = PlateDyePlanner.Create(plan, supplies, itemId => Get(itemId)?.DyeCount ?? 0);
        foreach (var failure in result.Failures)
        {
            diagnostics.Check(failure.Kind == PlateApplyFailureKind.UnsupportedDye
                ? DiagnosticKey.DyeChannelSupported : DiagnosticKey.DyeAvailable, 1, 0, (int?)failure.Slot);
            diagnostics.Check(failure.DyeChannel == 0 ? DiagnosticKey.Dye0 : DiagnosticKey.Dye1,
                failure.DyeId, null, (int?)failure.Slot);
        }
        return result;
    }

    private unsafe bool ApplyDyes(AgentMiragePrismMiragePlate* agent, PlateLoadStep step, PlateDyePlan plan)
        => PlateDyeExecution.Apply(step, plan, new DyePort(this, agent), diagnostics);

    private sealed unsafe class DyePort(NativePlateEditor owner, AgentMiragePrismMiragePlate* agent) : IPlateDyePort
    {
        private readonly AgentMiragePrismMiragePlateData* data = agent->Data;
        private readonly uint plateIndex = agent->Data->SelectedMiragePlateIndex;
        private InventoryItem* dyeItem;
        public bool ContextMatches() => owner.ObserveContext(agent, data, plateIndex);
        public uint ReadItemId(PlateSlot slot) => data->CurrentItems[(int)slot].ItemId;
        public NativeDyeSupplyState? ReadSupply(DyeSupply supply)
        {
            var inventory = InventoryManager.Instance();
            var container = inventory == null ? null : inventory->GetInventoryContainer((InventoryType)supply.Container);
            if (container == null || !container->IsLoaded || supply.InventorySlot >= container->Size) return null;
            dyeItem = container->GetInventorySlot(supply.InventorySlot);
            return dyeItem == null ? null : new(true, container->Size, dyeItem->ItemId, dyeItem->Quantity);
        }
        public void Apply(PlateDyeChange change)
        {
            ref var current = ref data->CurrentItems[(int)change.Slot];
            // SetSelectedItemData defers these capability bits; SetItemStain reads them synchronously.
            var channels = owner.Get(current.ItemId)?.DyeCount ?? 0;
            current.Flags = (ItemFlag)NativeDyeStatePolicy.ChannelFlags((uint)current.Flags, channels,
                (uint)ItemFlag.HasStain0, (uint)ItemFlag.HasStain1);
            var supply = change.Supply;
            agent->SetItemStain((uint)change.Slot, change.DyeId, dyeItem, supply.ItemId, change.Channel);
        }
        public NativeDyeReadback ReadDye(PlateDyeChange change)
        {
            var observed = data->CurrentItems[(int)change.Slot];
            var channel = change.Channel;
            return new(observed.PendingStainIds[channel], observed.IsPendingStainFromInventoryItem[channel],
                observed.IsPendingStainFromItemId[channel], observed.PendingStainItemIds[channel],
                (uint)observed.PendingStainInventoryTypes[channel], observed.PendingStainInventorySlots[channel]);
        }
    }
}

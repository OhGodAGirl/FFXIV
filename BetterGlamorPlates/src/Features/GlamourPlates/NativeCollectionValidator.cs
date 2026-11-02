using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace BetterGlamorPlates.Features.GlamourPlates;

/// <summary>
/// Captures the minimum live Dresser and Armoire source details required to
/// validate one deliberate plate-load action. The result is never retained or
/// used as collection ownership state.
/// </summary>
internal sealed class NativeCollectionValidator : INativeCollectionValidator
{
    private readonly IDataManager dataManager;

    internal NativeCollectionValidator(IDataManager dataManager)
    {
        this.dataManager = dataManager;
    }

    public unsafe NativeCollectionSnapshot Capture(DateTime capturedAtUtc)
    {
        var items = new List<NativeCollectionItem>();
        var manager = MirageManager.Instance();
        var uiState = UIState.Instance();
        var dresserLoaded = manager != null && manager->PrismBoxLoaded;
        var armoireLoaded = uiState != null && uiState->Cabinet.IsCabinetLoaded();

        if (dresserLoaded)
            ScanDresser(manager, items);
        if (armoireLoaded)
            ScanArmoire(uiState, items);

        return NativeCollectionProjection.Complete(items, dresserLoaded, armoireLoaded, capturedAtUtc);
    }

    private unsafe void ScanDresser(MirageManager* manager, List<NativeCollectionItem> result)
    {
        var setSheet = dataManager.GetExcelSheet<MirageStoreSetItem>();

        for (var index = 0; index < manager->PrismBoxItemIds.Length; index++)
        {
            var rawItemId = manager->PrismBoxItemIds[index];
            if (rawItemId == 0)
                continue;

            var itemId = rawItemId % 1_000_000;
            if (setSheet.TryGetRow(itemId, out var set))
            {
                Span<uint> setItemIds =
                [
                    set.MainHand.RowId,
                    set.OffHand.RowId,
                    set.Head.RowId,
                    set.Body.RowId,
                    set.Hands.RowId,
                    set.Legs.RowId,
                    set.Feet.RowId,
                    set.Earrings.RowId,
                    set.Necklace.RowId,
                    set.Bracelets.RowId,
                    set.Ring.RowId,
                ];
                var components = new (uint ItemId, bool Unlocked)[setItemIds.Length];
                for (var setSlot = 0; setSlot < setItemIds.Length; setSlot++)
                    components[setSlot] = (setItemIds[setSlot], setItemIds[setSlot] != 0 && manager->IsSetSlotUnlocked((uint)index, setSlot));
                NativeCollectionProjection.AddDresser(result, rawItemId, (uint)index, 0, 0, components);

                continue;
            }

            NativeCollectionProjection.AddDresser(result, rawItemId, (uint)index,
                manager->PrismBoxStain0Ids[index], manager->PrismBoxStain1Ids[index], null);
        }
    }

    private unsafe void ScanArmoire(UIState* uiState, List<NativeCollectionItem> result)
    {
        foreach (var cabinetRow in dataManager.GetExcelSheet<Lumina.Excel.Sheets.Cabinet>())
        {
            if (cabinetRow.RowId == 0
                || cabinetRow.Item.RowId == 0
                || !uiState->Cabinet.IsItemInCabinet(cabinetRow.RowId))
            {
                continue;
            }

            NativeCollectionProjection.AddArmoire(result, cabinetRow.RowId, cabinetRow.Item.RowId, true);
        }
    }
}



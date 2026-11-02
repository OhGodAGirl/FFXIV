using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Features.Main;

internal abstract record MainCommand
{
    internal sealed record CloseEditor : MainCommand;
    internal sealed record RefreshCollection : MainCommand;

    internal sealed record CreatePlate : MainCommand;
    internal sealed record SaveOutfit(Guid PlateId) : MainCommand;
    internal sealed record DiscardOutfit(Guid PlateId) : MainCommand;
    internal sealed record BeginDelete(Guid PlateId) : MainCommand;
    internal sealed record ConfirmDelete(Guid PlateId) : MainCommand;
    internal sealed record DeletePlate(Guid PlateId) : MainCommand;

    internal sealed record CaptureCurrentPlate : MainCommand;

    internal sealed record SelectPlate(Guid PlateId) : MainCommand;

    internal sealed record SetPlateSearch(string Search) : MainCommand;

    internal sealed record RenameActive(string Name) : MainCommand;

    internal sealed record DuplicateActive : MainCommand;
    internal sealed record RenamePlate(Guid PlateId, string Name) : MainCommand;
    internal sealed record DuplicatePlate(Guid PlateId) : MainCommand;
    internal sealed record AssignIcon(Guid PlateId, uint IconId) : MainCommand;

    internal sealed record ArmOrDelete(Guid PlateId) : MainCommand;

    internal sealed record DisarmDelete : MainCommand;

    internal sealed record LoadActive(PlateDyeMode? DyeMode = null, Guid? PlateId = null, long? Revision = null) : MainCommand;
    internal sealed record PreviewActive(PlateDyeMode DyeMode = PlateDyeMode.Saved) : MainCommand;
    internal sealed record UseStoredDyes : MainCommand;
    internal sealed record SetDye(Guid PlateId, PlateSlot Slot, uint ExpectedItemId, byte Channel, byte DyeId) : MainCommand;
    internal sealed record ClearOutfitItems(Guid PlateId, long Revision) : MainCommand;
    internal sealed record ClearOutfitDyes(Guid PlateId, long Revision) : MainCommand;
    internal sealed record ClearItemDyes(Guid PlateId, PlateSlot Slot, uint ExpectedItemId) : MainCommand;
    internal sealed record RestoreSlotDyes(Guid PlateId, PlateSlot Slot, uint ExpectedItemId) : MainCommand;

    internal sealed record SelectSlot(PlateSlot Slot) : MainCommand;

    internal sealed record ClearSlot(PlateSlot Slot, Guid? PlateId = null, uint? ExpectedItemId = null) : MainCommand;

    internal sealed record SetFilter(string Filter) : MainCommand;

    internal sealed record ToggleCollectionSource(CollectionSource Source) : MainCommand;

    internal sealed record AddItem(PlateSlot Slot, CollectionItem Item) : MainCommand;
}


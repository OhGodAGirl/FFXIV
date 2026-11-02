using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Features.Main;

/// <summary>Owns mutually exclusive drawer targets and binds actions to the displayed outfit.</summary>
internal sealed class DrawerSession
{
    internal bool DyeOpen;
    internal bool IconOpen;
    internal MainCommand.SetDye? DyeTarget { get; private set; }
    internal SavedPlateSummary? IconTarget { get; private set; }
    internal bool SpecialDyes { get; set; }

    internal bool OpenDye(MainViewState view, SavedSlotView slot, byte channel, IReadOnlyList<DyeOption> dyes)
    {
        if (view.SelectedPlateId is not { } plate || !slot.HasItem || channel >= slot.DyeCount)
            return false;
        var id = channel == 0 ? slot.Stain0Id : slot.Stain1Id;
        DyeTarget = new(plate, slot.Slot, slot.ItemId, channel, id);
        SpecialDyes = dyes.FirstOrDefault(dye => dye.Id == id)?.SpecialReagent ?? false;
        DyeOpen = true;
        IconOpen = false;
        return true;
    }

    internal void OpenIcon(SavedPlateSummary plate)
    {
        IconTarget = plate;
        IconOpen = true;
        DyeOpen = false;
    }

    internal SavedSlotView? CurrentDyeSlot(MainViewState view)
    {
        if (!DyeOpen || DyeTarget is not { } target) return null;
        var slot = view.Slots.FirstOrDefault(value => value.Slot == target.Slot);
        if (target.PlateId != view.SelectedPlateId || slot is null
            || target.ExpectedItemId != slot.ItemId || target.Channel >= slot.DyeCount)
        {
            DyeOpen = false;
            return null;
        }
        return slot;
    }

    internal bool HasCurrentIconTarget(MainViewState view)
    {
        if (!IconOpen) return false;
        if (IconTarget is null || !view.SavedPlates.Any(plate => plate.Id == IconTarget.Id))
            IconOpen = false;
        return IconOpen;
    }

    internal MainCommand? SelectDye(MainViewState view, byte dye)
        => CurrentDyeSlot(view) is null ? null : DyeTarget! with { DyeId = dye };

    internal MainCommand? ClearBoth(MainViewState view)
        => CurrentDyeSlot(view) is null ? null
            : new MainCommand.ClearItemDyes(DyeTarget!.PlateId, DyeTarget.Slot, DyeTarget.ExpectedItemId);

    internal MainCommand? ClearAll(MainViewState view)
        => CurrentDyeSlot(view) is null ? null
            : new MainCommand.ClearOutfitDyes(DyeTarget!.PlateId, view.OutfitRevision);

    internal MainCommand? AssignIcon(MainViewState view, uint icon)
        => HasCurrentIconTarget(view) ? new MainCommand.AssignIcon(IconTarget!.Id, icon) : null;

    internal void Close()
    {
        DyeOpen = false;
        IconOpen = false;
    }

    internal static DyeOption[] FilterDyes(IReadOnlyList<DyeOption> dyes, bool special, string search)
        => dyes.Where(dye => dye.SpecialReagent == special
            && dye.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
}

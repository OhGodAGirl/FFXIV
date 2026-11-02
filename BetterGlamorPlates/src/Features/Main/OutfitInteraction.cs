namespace BetterGlamorPlates.Features.Main;

/// <summary>Converts enabled outfit actions into commands bound to the displayed revision.</summary>
internal static class OutfitInteraction
{
    internal static MainCommand? ClearItems(MainViewState view, bool loadQueued)
        => view.ActivePlate is null || !view.CanLoad || loadQueued || view.SelectedPlateId is not { } plate
            ? null : new MainCommand.ClearOutfitItems(plate, view.OutfitRevision);

    internal static MainCommand? Save(MainViewState view)
        => view.HasUnsavedChanges && view.SelectedPlateId is { } plate ? new MainCommand.SaveOutfit(plate) : null;

    internal static MainCommand? Reset(MainViewState view)
        => view.HasUnsavedChanges && view.SelectedPlateId is { } plate ? new MainCommand.DiscardOutfit(plate) : null;

    internal static MainCommand? Load(MainViewState view, bool queued)
        => view.CanLoad && !queued
            ? new MainCommand.LoadActive(view.PreviewDyeMode, view.SelectedPlateId, view.OutfitRevision) : null;

    internal static MainCommand? ClearSlot(MainViewState view, SavedSlotView slot)
        => slot.HasItem ? new MainCommand.ClearSlot(slot.Slot, view.SelectedPlateId, slot.ItemId) : null;

    internal static string? Feedback(MainViewState view, bool queued)
        => queued ? "Preparing the current plate…" : view.OperationFeedback
            ?? (view.CollectionNeedsAttention ? view.SharedCollectionStatus : null);
}

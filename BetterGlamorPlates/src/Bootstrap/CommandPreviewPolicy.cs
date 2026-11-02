using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Bootstrap;

internal static class CommandPreviewPolicy
{
    internal static bool ShouldPreview(MainCommand command, bool editorOpen, bool dyeWindowOpen,
        Guid? previousSelection, Guid? currentSelection)
        => editorOpen && !dyeWindowOpen
            && (previousSelection != currentSelection || command is MainCommand.AddItem or MainCommand.SelectSlot
                or MainCommand.SelectPlate or MainCommand.ClearSlot or MainCommand.PreviewActive
                or MainCommand.UseStoredDyes or MainCommand.CaptureCurrentPlate or MainCommand.CreatePlate
                or MainCommand.SetDye or MainCommand.ClearOutfitDyes or MainCommand.ClearItemDyes
                or MainCommand.RestoreSlotDyes or MainCommand.DuplicatePlate or MainCommand.DiscardOutfit);

    internal static bool ReportSuccess(MainCommand command)
        => command is not (MainCommand.UseStoredDyes or MainCommand.SetDye or MainCommand.ClearOutfitDyes
            or MainCommand.ClearItemDyes or MainCommand.RestoreSlotDyes);
}

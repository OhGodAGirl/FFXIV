namespace SpringCleaning;

internal static class OutfitPieceSelectionPolicy
{
    internal static bool Matches(InventoryDresserOutfit outfit, IReadOnlyList<OutfitPieceSelection> selected) =>
        outfit.Components.Count != 0
        && outfit.Components.All(component => component.IsHighQuality == outfit.Components[0].IsHighQuality)
        && selected.Count == outfit.Components.Count
        && selected.Select(piece => piece.ItemId).Distinct().Count() == selected.Count
        && outfit.Components.All(component => selected.Any(piece =>
            piece.ItemId == component.ItemId
            && piece.Container == component.InventoryTypeId
            && piece.Slot == component.SlotIndex));
}

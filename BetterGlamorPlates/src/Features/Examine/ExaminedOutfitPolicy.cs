using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Features.Examine;

internal sealed record ExaminedAppearance(byte SlotId, uint ItemId, uint GlamourId,
    byte Stain0, byte Stain1, byte GlamourStain0, byte GlamourStain1);

/// <summary>Copies displayed appearances and dyes into a new, independently owned local outfit.</summary>
internal static class ExaminedOutfitPolicy
{
    // Examine's thirteen visible equipment entries omit the retired waist slot.
    internal static int ToInventorySlot(int examineSlot) => examineSlot < 5 ? examineSlot : examineSlot + 1;

    internal static bool MatchesEquipment(uint inventoryItem, uint inventoryGlamour, uint displayedItem, uint displayedGlamour)
        => inventoryItem % 1_000_000 == displayedItem % 1_000_000
            && (displayedGlamour == 0 || (displayedItem != 0 && inventoryGlamour == displayedGlamour));

    internal static SavedPlate? Create(string name, IReadOnlyList<ExaminedAppearance> appearances,
        DateTime timestamp, Guid id)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || appearances.Count > 14)
            return null;
        var occupied = appearances.Where(item => item.SlotId < 14 && item.ItemId != 0).ToArray();
        if (occupied.Select(item => item.SlotId).Distinct().Count() != occupied.Length)
            return null;
        var plate = PlateLibrary.Create($"{name.Trim()}'s Glamour", timestamp, id);
        for (var index = 0; index < PlateRules.SlotCount; index++)
        {
            var nativeSlot = OutfitPreviewPolicy.ToPreviewSlot((PlateSlot)index);
            var item = occupied.FirstOrDefault(item => item.SlotId == nativeSlot);
            if (item is null)
                continue;
            var glamoured = item.GlamourId != 0;
            plate.Items[index] = new SavedPlateItem
            {
                ItemId = (glamoured ? item.GlamourId : item.ItemId) % 1_000_000,
                Stain0Id = glamoured ? item.GlamourStain0 : item.Stain0,
                Stain1Id = glamoured ? item.GlamourStain1 : item.Stain1,
            };
        }
        return plate.Items.Any(item => item is not null) ? plate : null;
    }
}

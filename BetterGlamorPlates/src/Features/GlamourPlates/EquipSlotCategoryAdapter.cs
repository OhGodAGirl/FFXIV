using Lumina.Excel.Sheets;

namespace BetterGlamorPlates.Features.GlamourPlates;

/// <summary>
/// Converts Lumina's patch-facing equipment row into the stable slot policy
/// model used by the plugin.
/// </summary>
internal static class EquipSlotCategoryAdapter
{
    internal static EquipSlotMask ToMask(EquipSlotCategory category)
        => new(
            category.MainHand > 0,
            category.OffHand > 0,
            category.Head > 0,
            category.Body > 0,
            category.Gloves > 0,
            category.Legs > 0,
            category.Feet > 0,
            category.Ears > 0,
            category.Neck > 0,
            category.Wrists > 0,
            category.FingerR > 0,
            category.FingerL > 0);
}

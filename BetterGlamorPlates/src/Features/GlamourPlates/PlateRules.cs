namespace BetterGlamorPlates.Features.GlamourPlates;

internal readonly record struct EquipSlotMask(
    bool MainHand,
    bool OffHand,
    bool Head,
    bool Body,
    bool Hands,
    bool Legs,
    bool Feet,
    bool Ears,
    bool Neck,
    bool Wrists,
    bool RightRing,
    bool LeftRing);

internal static class PlateRules
{
    internal const int SlotCount = 12;

    internal static string GetSlotName(PlateSlot slot) => slot switch
    {
        PlateSlot.MainHand => "Main Hand",
        PlateSlot.OffHand => "Off Hand",
        PlateSlot.Head => "Head",
        PlateSlot.Body => "Body",
        PlateSlot.Hands => "Hands",
        PlateSlot.Legs => "Legs",
        PlateSlot.Feet => "Feet",
        PlateSlot.Ears => "Ears",
        PlateSlot.Neck => "Neck",
        PlateSlot.Wrists => "Wrists",
        PlateSlot.RightRing => "Right Ring",
        PlateSlot.LeftRing => "Left Ring",
        _ => "Unknown",
    };

    internal static bool MatchesSlot(EquipSlotMask mask, PlateSlot slot) => slot switch
    {
        PlateSlot.MainHand => mask.MainHand,
        PlateSlot.OffHand => mask.OffHand,
        PlateSlot.Head => mask.Head,
        PlateSlot.Body => mask.Body,
        PlateSlot.Hands => mask.Hands,
        PlateSlot.Legs => mask.Legs,
        PlateSlot.Feet => mask.Feet,
        PlateSlot.Ears => mask.Ears,
        PlateSlot.Neck => mask.Neck,
        PlateSlot.Wrists => mask.Wrists,
        PlateSlot.RightRing => mask.RightRing,
        PlateSlot.LeftRing => mask.LeftRing,
        _ => false,
    };
}

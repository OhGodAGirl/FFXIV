using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Features.Main;

internal sealed record AccessoriesView(IReadOnlyList<SavedSlotView> Previews, int AdditionalCount,
    SharedOwnershipState Ownership, string Label);

/// <summary>Projects five independent slots into a visual summary without changing saved intent.</summary>
internal static class EquipmentSummaryPolicy
{
    internal static bool IsAccessory(PlateSlot slot) => slot is >= PlateSlot.Ears and <= PlateSlot.LeftRing;

    internal static AccessoriesView Create(IReadOnlyList<SavedSlotView> slots)
    {
        var accessories = slots.Where(slot => IsAccessory(slot.Slot)).OrderBy(slot => slot.Slot).ToArray();
        var occupied = accessories.Where(slot => slot.HasItem).ToArray();
        var state = SharedOwnershipState.Empty;
        foreach (var slot in occupied)
        {
            if (Priority(slot.SharedOwnership) > Priority(state))
                state = slot.SharedOwnership;
        }
        return new AccessoriesView(occupied.Take(2).ToArray(), Math.Max(0, occupied.Length - 2),
            state, CollectionProviderPolicy.DescribeSlotOwnership(state));
    }

    private static int Priority(SharedOwnershipState state) => state switch
    {
        SharedOwnershipState.NotObserved => 4,
        SharedOwnershipState.Unknown => 3,
        SharedOwnershipState.LastKnownMatch => 2,
        SharedOwnershipState.FreshMatch => 1,
        _ => 0,
    };
}

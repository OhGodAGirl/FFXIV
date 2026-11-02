using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Preserves independent accessory identity and truthful summary state.</summary>
public sealed class EquipmentSummaryPolicyTests
{
    /// <summary>Preview order follows anatomy rather than input order and reports overflow.</summary>
    [Fact]
    public void FiveAccessoriesProduceTwoOrderedPreviewsAndThreeMore()
    {
        var slots = Enumerable.Range(7, 5).Reverse().Select(index => Slot((PlateSlot)index, SharedOwnershipState.FreshMatch)).ToArray();
        var result = EquipmentSummaryPolicy.Create(slots);
        Assert.Equal(new[] { PlateSlot.Ears, PlateSlot.Neck }, result.Previews.Select(slot => slot.Slot));
        Assert.Equal(3, result.AdditionalCount);
        Assert.Equal("Collected", result.Label);
    }

    /// <summary>A higher-priority problem cannot disappear behind other occupied slots.</summary>
    [Theory]
    [InlineData((int)SharedOwnershipState.NotObserved, (int)SharedOwnershipState.Unknown, "Not collected")]
    [InlineData((int)SharedOwnershipState.Unknown, (int)SharedOwnershipState.LastKnownMatch, "Unknown")]
    [InlineData((int)SharedOwnershipState.LastKnownMatch, (int)SharedOwnershipState.FreshMatch, "Stale")]
    public void AggregateRetainsWorstState(int first, int second, string expected)
    {
        var result = EquipmentSummaryPolicy.Create([Slot(PlateSlot.Ears, (SharedOwnershipState)first),
            Slot(PlateSlot.RightRing, (SharedOwnershipState)second)]);
        Assert.Equal(expected, result.Label);
    }

    /// <summary>Armor does not enter Accessories and empty accessories remain empty.</summary>
    [Fact]
    public void EmptyIgnoresNonAccessorySlots()
    {
        var result = EquipmentSummaryPolicy.Create([Slot(PlateSlot.Body, SharedOwnershipState.Unknown)]);
        Assert.Empty(result.Previews);
        Assert.Equal(0, result.AdditionalCount);
        Assert.Equal("Empty", result.Label);
        Assert.False(EquipmentSummaryPolicy.IsAccessory((PlateSlot)12));
    }

    private static SavedSlotView Slot(PlateSlot slot, SharedOwnershipState state)
        => new(slot, slot.ToString(), "Example", true, 1, state, state.ToString());
}

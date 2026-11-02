namespace BetterGlamorPlates.Tests;

using BetterGlamorPlates.Features.GlamourPlates;

/// <summary>
/// Verifies stable plate-slot decisions independently from Lumina rows.
/// </summary>
public sealed class PlateRulesTests
{
    /// <summary>
    /// Verifies every native slot has the intended player-facing name.
    /// </summary>
    [Theory]
    [InlineData((int)PlateSlot.MainHand, "Main Hand")]
    [InlineData((int)PlateSlot.OffHand, "Off Hand")]
    [InlineData((int)PlateSlot.Head, "Head")]
    [InlineData((int)PlateSlot.Body, "Body")]
    [InlineData((int)PlateSlot.Hands, "Hands")]
    [InlineData((int)PlateSlot.Legs, "Legs")]
    [InlineData((int)PlateSlot.Feet, "Feet")]
    [InlineData((int)PlateSlot.Ears, "Ears")]
    [InlineData((int)PlateSlot.Neck, "Neck")]
    [InlineData((int)PlateSlot.Wrists, "Wrists")]
    [InlineData((int)PlateSlot.RightRing, "Right Ring")]
    [InlineData((int)PlateSlot.LeftRing, "Left Ring")]
    [InlineData(99, "Unknown")]
    public void GetSlotNameMapsEverySlot(int slotValue, string expected)
    {
        // Arrange and Act
        var name = PlateRules.GetSlotName((PlateSlot)slotValue);

        // Assert
        Assert.Equal(expected, name);
    }

    /// <summary>
    /// Verifies every slot reads only its corresponding compatibility flag.
    /// </summary>
    [Theory]
    [MemberData(nameof(SlotMasks))]
    public void MatchesSlotReadsCorrespondingMask(int slotValue, object matchingValue)
    {
        // Arrange
        var slot = (PlateSlot)slotValue;
        var matching = (EquipSlotMask)matchingValue;
        var empty = default(EquipSlotMask);

        // Act
        var accepted = PlateRules.MatchesSlot(matching, slot);
        var rejected = PlateRules.MatchesSlot(empty, slot);

        // Assert
        Assert.True(accepted);
        Assert.False(rejected);
    }

    /// <summary>
    /// Verifies unknown serialized slot values fail closed.
    /// </summary>
    [Fact]
    public void MatchesSlotRejectsUnknownSlot()
    {
        // Arrange
        var all = new EquipSlotMask(
            true, true, true, true, true, true,
            true, true, true, true, true, true);

        // Act
        var accepted = PlateRules.MatchesSlot(all, (PlateSlot)99);

        // Assert
        Assert.False(accepted);
    }

    /// <summary>
    /// Gets one positive compatibility mask for every supported plate slot.
    /// </summary>
    public static IEnumerable<object[]> SlotMasks
    {
        get
        {
            yield return [(int)PlateSlot.MainHand, Mask(mainHand: true)];
            yield return [(int)PlateSlot.OffHand, Mask(offHand: true)];
            yield return [(int)PlateSlot.Head, Mask(head: true)];
            yield return [(int)PlateSlot.Body, Mask(body: true)];
            yield return [(int)PlateSlot.Hands, Mask(hands: true)];
            yield return [(int)PlateSlot.Legs, Mask(legs: true)];
            yield return [(int)PlateSlot.Feet, Mask(feet: true)];
            yield return [(int)PlateSlot.Ears, Mask(ears: true)];
            yield return [(int)PlateSlot.Neck, Mask(neck: true)];
            yield return [(int)PlateSlot.Wrists, Mask(wrists: true)];
            yield return [(int)PlateSlot.RightRing, Mask(rightRing: true)];
            yield return [(int)PlateSlot.LeftRing, Mask(leftRing: true)];
        }
    }

    private static EquipSlotMask Mask(
        bool mainHand = false,
        bool offHand = false,
        bool head = false,
        bool body = false,
        bool hands = false,
        bool legs = false,
        bool feet = false,
        bool ears = false,
        bool neck = false,
        bool wrists = false,
        bool rightRing = false,
        bool leftRing = false)
        => new(
            mainHand,
            offHand,
            head,
            body,
            hands,
            legs,
            feet,
            ears,
            neck,
            wrists,
            rightRing,
            leftRing);
}

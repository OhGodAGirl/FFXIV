using BetterGlamorPlates.Features.Examine;
using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests.Features.Examine;

/// <summary>Verifies Examine saves the visible glamour, both dyes and the character's full name.</summary>
public sealed class ExaminedOutfitPolicyTests
{
    private static readonly int[] ExpectedInventorySlots = [0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13];
    /// <summary>Glamour overrides must replace underlying gear and carry their own two dye channels.</summary>
    [Theory]
    [InlineData("Redna Liorasch")]
    [InlineData("Amorette Nightwish")]
    [InlineData("Ucarai Feannag")]
    public void SavesCharacterNameAndDisplayedGlamourDyes(string playerName)
    {
        var id = Guid.NewGuid();
        var plate = ExaminedOutfitPolicy.Create(playerName,
            [new(3, 100, 200, 1, 2, 7, 9)], TestDomainValues.Timestamp, id);
        Assert.NotNull(plate);
        Assert.Equal($"{playerName}'s Glamour", plate.Name);
        Assert.Equal(id, plate.Id);
        var body = plate.Items[(int)PlateSlot.Body]!;
        Assert.Equal(200u, body.ItemId);
        Assert.Equal(7, body.Stain0Id);
        Assert.Equal(9, body.Stain1Id);
    }

    /// <summary>Non-glamoured equipment retains its own dyes and HQ IDs normalize to base IDs.</summary>
    [Fact]
    public void UnglamouredItemsPreserveDyesAndRingSides()
    {
        var plate = ExaminedOutfitPolicy.Create("Redna Liorasch",
            [new(11, 1000100, 0, 4, 5, 0, 0), new(12, 200, 0, 6, 7, 0, 0)],
            TestDomainValues.Timestamp, Guid.NewGuid())!;
        Assert.Equal(100u, plate.Items[(int)PlateSlot.RightRing]!.ItemId);
        Assert.Equal(4, plate.Items[(int)PlateSlot.RightRing]!.Stain0Id);
        Assert.Equal(200u, plate.Items[(int)PlateSlot.LeftRing]!.ItemId);
        Assert.Equal(7, plate.Items[(int)PlateSlot.LeftRing]!.Stain1Id);
        Assert.Null(plate.Items[(int)PlateSlot.Body]);
    }

    /// <summary>Only glamour plate slots are imported; waist and soul crystal are not outfit equipment.</summary>
    [Fact]
    public void IgnoresNonPlateSlots()
        => Assert.Null(ExaminedOutfitPolicy.Create("Redna Liorasch",
            [new(5, 100, 0, 0, 0, 0, 0), new(13, 200, 0, 0, 0, 0, 0)],
            TestDomainValues.Timestamp, Guid.NewGuid()));

    /// <summary>Ambiguous or empty capture cannot produce a misleading saved outfit.</summary>
    [Fact]
    public void RejectsDuplicateSlotsAndMissingNames()
    {
        ExaminedAppearance[] items = [new(3, 100, 0, 0, 0, 0, 0), new(3, 200, 0, 0, 0, 0, 0)];
        Assert.Null(ExaminedOutfitPolicy.Create("Redna Liorasch", items, TestDomainValues.Timestamp, Guid.NewGuid()));
        Assert.Null(ExaminedOutfitPolicy.Create("", [items[0]], TestDomainValues.Timestamp, Guid.NewGuid()));
    }

    /// <summary>Rejects stale equipment while respecting a glamour suppressed by the Examine display.</summary>
    [Theory]
    [InlineData(1000100u, 200u, 100u, 200u, true)]
    [InlineData(100u, 200u, 100u, 0u, true)]
    [InlineData(100u, 200u, 101u, 200u, false)]
    [InlineData(100u, 201u, 100u, 200u, false)]
    [InlineData(0u, 0u, 100u, 0u, false)]
    [InlineData(0u, 200u, 0u, 200u, false)]
    [InlineData(0u, 0u, 0u, 0u, true)]
    public void MatchesDisplayedEquipment(uint item, uint glamour, uint displayed, uint displayedGlamour, bool expected)
        => Assert.Equal(expected, ExaminedOutfitPolicy.MatchesEquipment(item, glamour, displayed, displayedGlamour));

    /// <summary>The native Examine order skips waist but keeps both ring sides and the soul crystal.</summary>
    [Fact]
    public void MapsExamineEquipmentOrder()
        => Assert.Equal(ExpectedInventorySlots,
            Enumerable.Range(0, 13).Select(ExaminedOutfitPolicy.ToInventorySlot));
}



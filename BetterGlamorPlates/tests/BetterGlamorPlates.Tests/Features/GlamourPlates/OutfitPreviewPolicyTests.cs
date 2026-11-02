using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

/// <summary>Locks the native preview slot mapping independently from plate slot indices.</summary>
public sealed class OutfitPreviewPolicyTests
{
    /// <summary>The default preview clears both channels without changing the supplied saved colour.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(255)]
    public void PreviewModesChooseSavedOrDefaultColour(byte colour)
    {
        Assert.Equal(colour, OutfitPreviewPolicy.PreviewDye(colour, PlateDyeMode.Saved));
        Assert.Equal(0, OutfitPreviewPolicy.PreviewDye(colour, PlateDyeMode.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutfitPreviewPolicy.PreviewDye(colour, (PlateDyeMode)99));
    }

    /// <summary>Waist and soul crystal are never used for a glamour outfit.</summary>
    [Fact]
    public void MapsEveryPlateSlotWithoutWaistOrSoulCrystal()
    {
        byte[] expected = [0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12];
        var actual = Enum.GetValues<PlateSlot>().Select(OutfitPreviewPolicy.ToPreviewSlot).ToArray();
        Assert.Equal(expected, actual);
    }

    /// <summary>Invalid slot IDs cannot reach native memory.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    public void RejectsInvalidSlot(int slot)
        => Assert.Throws<ArgumentOutOfRangeException>(() => OutfitPreviewPolicy.ToPreviewSlot((PlateSlot)slot));
}

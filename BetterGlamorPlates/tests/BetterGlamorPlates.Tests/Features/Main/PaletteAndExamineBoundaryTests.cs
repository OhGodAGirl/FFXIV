using System.Numerics;
using BetterGlamorPlates.Features.Examine;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Colour-family and native button boundary regressions.</summary>
public sealed class PaletteAndExamineBoundaryTests
{
    /// <summary>Identical colours retain a stable order independent of input order.</summary>
    [Fact]
    public void SameColourUsesDyeIdentityAsTieBreaker()
    {
        DyeOption[] dyes = [new(9, "Second", 0x769abc), new(3, "First", 0x769abc)];
        Assert.Equal(new byte[] { 3, 9 }, DyePaletteOrder.Sort(dyes).Select(dye => dye.Id));
    }

    /// <summary>Muted intermediate hues appear in spectrum order, including red wraparound.</summary>
    [Fact]
    public void MutedColoursFollowTheFullSpectrum()
    {
        uint[] colours = [0xc06060, 0xc09060, 0xc0c060, 0x90c060, 0x60c060, 0x60c090,
            0x60c0c0, 0x6090c0, 0x6060c0, 0x9060c0, 0xc060c0, 0xc06090];
        DyeOption[] dyes = colours.Select((colour, index) => new DyeOption((byte)(index + 1), "Shade", colour)).Reverse().ToArray();
        Assert.Equal(Enumerable.Range(1, 12).Select(value => (byte)value), DyePaletteOrder.Sort(dyes).Select(dye => dye.Id));
    }

    /// <summary>Lightness groups nearby hues before hue fine ordering.</summary>
    [Fact]
    public void NearbyRedShadesPutLighterShadeFirst()
    {
        DyeOption[] dyes = [new(1, "Dark red", 0x802020), new(2, "Light warm red", 0xe09080)];
        Assert.Equal(new byte[] { 2, 1 }, DyePaletteOrder.Sort(dyes).Select(dye => dye.Id));
    }

    /// <summary>Alpha bits do not change the RGB colour family.</summary>
    [Fact]
    public void IgnoresHighByteWhenOrderingColours()
    {
        DyeOption[] dyes = [new(2, "Red", 0xffff0000), new(1, "Grey", 0x00808080)];
        Assert.Equal(new byte[] { 1, 2 }, DyePaletteOrder.Sort(dyes).Select(dye => dye.Id));
    }

    /// <summary>The smaller native axis determines button scale.</summary>
    [Theory]
    [InlineData(620, 480, .5f)]
    [InlineData(310, 960, .5f)]
    public void UsesSmallerAxisForScale(float width, float height, float expected)
        => Assert.Equal(new Vector2(64 * expected), ExamineTabPlacement.ButtonSize(new(0, 0, width, height)));

    /// <summary>A zero-area occluding window covers no button pixels.</summary>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    public void ZeroAreaWindowDoesNotOcclude(float width, float height)
        => Assert.False(ExamineTabPlacement.IsCovered(1, 2, 2, 0, new(0, 0, 100, 100), new(50, 50, width, height)));

    /// <summary>A button exactly fitting a viewport edge remains visible.</summary>
    [Theory]
    [InlineData(47, 0, 1000, 1000)]
    [InlineData(0, 824, 1000, 1000)]
    [InlineData(0, 0, 111, 1000)]
    [InlineData(0, 0, 1000, 888)]
    public void AllowsExactViewportEdges(float x, float y, float width, float height)
        => Assert.Equal(new Vector2(47, 824), ExamineTabPlacement.Calculate(new(0, 0, 620, 960), new(x, y, width, height)));

    /// <summary>A clipped bottom is rejected rather than clamped onto another control.</summary>
    [Fact]
    public void RejectsPartiallyClippedBottom()
        => Assert.Null(ExamineTabPlacement.Calculate(new(0, 0, 620, 960), new(0, 0, 1000, 887)));
    /// <summary>Different hue families take priority over lightness for muted colours.</summary>
    [Theory]
    [InlineData(0x802020u, 0xe0c080u)]
    [InlineData(0x208020u, 0x80e0c0u)]
    [InlineData(0x202080u, 0xc080e0u)]
    public void FamilyPrecedesLightness(uint darkEarlierFamily, uint lightLaterFamily)
    {
        DyeOption[] dyes = [new(1, "Earlier family", darkEarlierFamily), new(2, "Later family", lightLaterFamily)];
        Assert.Equal(new byte[] { 1, 2 }, DyePaletteOrder.Sort(dyes).Select(dye => dye.Id));
    }
    /// <summary>Rejects extreme geometry whose coordinate arithmetic overflows.</summary>
    [Theory]
    [InlineData(-2, 2, 4, 1, -2, 1, 2, 2)]
    [InlineData(1, 0, 4, 2, 1, 0, 1, 2)]
    [InlineData(-2, 1, 4, 4, -2, 1, 1, 4)]
    [InlineData(2, -4, 1, 4, 2, -2, 1, 2)]
    [InlineData(-2, 1, 2, 2, -4, 2, 4, 1)]
    [InlineData(1, 0, 1, 1, -1, -1, 4, 4)]
    [InlineData(-2, -2, 2, 4, -2, 1, 1, 4)]
    public void RejectsOverflowingGeometry(int x, int y, int width, int height,
        int viewportX, int viewportY, int viewportWidth, int viewportHeight)
    {
        const float unit = float.MaxValue / 4;
        Assert.Null(ExamineTabPlacement.Calculate(
            new(x * unit, y * unit, width * unit, height * unit),
            new(viewportX * unit, viewportY * unit, viewportWidth * unit, viewportHeight * unit)));
    }

    /// <summary>Zero-area viewports remain invalid even when a tiny button underflows to zero.</summary>
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    public void RejectsZeroViewportWithSubnormalGeometry(float width, float height)
        => Assert.Null(ExamineTabPlacement.Calculate(new(0, -float.Epsilon, float.Epsilon, float.Epsilon),
            new(0, 0, width, height)));
    /// <summary>The viewport minimum width is independent of coordinate precision.</summary>
    [Fact]
    public void RejectsMinimumViewportAtLargeScreenOrigin()
        => Assert.Null(CompanionPlacementPolicy.Calculate(new(0, 0, 620, 960),
            new(134217744f, 0, 8, 1000)));
}




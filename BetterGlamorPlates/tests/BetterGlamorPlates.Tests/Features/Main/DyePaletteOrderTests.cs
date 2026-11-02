using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;
namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Verifies colour ordering and reserved drawer bounds.</summary>
public sealed class DyePaletteOrderTests
{
    /// <summary>Neutral shades precede the hue spectrum.</summary>
    [Fact]
    public void SortsNeutralsThenSpectrumWithoutChangingDyeIdentity()
    {
        DyeOption[] input = [new(7,"Blue",0x0000ff),new(3,"Black",0),new(2,"Grey",0x888888),
            new(6,"Green",0x00ff00),new(4,"Red",0xff0000),new(1,"White",0xffffff),new(5,"Yellow",0xffff00)];
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, DyePaletteOrder.Sort(input).Select(dye => dye.Id));
    }

    /// <summary>Both native-window sides retain space for the full drawer.</summary>
    [Theory]
    [InlineData(20f, 160f)]
    [InlineData(20f, 436f)]
    [InlineData(1200f, 160f)]
    [InlineData(1200f, 436f)]
    public void DrawerStaysBesideCompanionInsideViewport(float nativeX, float drawerWidth)
    {
        var viewport = new WindowBounds(0, 0, 1920, 1080);
        var fit = CompanionPlacementPolicy.Calculate(new(nativeX, 50, 580, 860), viewport, drawerWidth)!.Value;
        Assert.True(fit.Position.X >= 4);
        Assert.True(fit.Position.X + fit.Size.X + drawerWidth * fit.Scale <= 1916.01f);
    }
}


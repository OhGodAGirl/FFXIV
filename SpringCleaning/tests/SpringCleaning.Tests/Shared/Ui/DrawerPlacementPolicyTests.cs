using System.Numerics;

namespace SpringCleaning.Tests.Ui;

public sealed class DrawerPlacementPolicyTests
{
    [Fact]
    public void ScaleAffectsSpacingWidthAndMinimumHeight()
    {
        var layout = DrawerPlacementPolicy.Plan(new(100, 50), new(900, 200), Vector2.Zero,
            new(3000, 1500), new(950, 200), 2);
        Assert.Equal(new Vector2(1016, 50), layout.Position);
        Assert.Equal(new Vector2(720, 520), layout.Size);
        Assert.Equal(new Vector2(1752, 50), layout.CreationPosition);
        Assert.True(layout.FitsBeside);
        layout = DrawerPlacementPolicy.Plan(Vector2.Zero, new(900, 800), Vector2.Zero,
            new(2100, 1200), new(950, 800), 2);
        Assert.Equal(16, layout.Position.Y);
    }

    [Theory]
    [InlineData(300, 200, 284, 184)]
    [InlineData(0, 0, 1, 1)]
    public void SmallOrTemporarilyEmptyViewportCannotProduceInvalidBounds(float width, float height, float expectedWidth, float expectedHeight)
    {
        var layout = DrawerPlacementPolicy.Plan(new(100, 50), new(900, 1000), Vector2.Zero,
            new(width, height), new(950, 1000), 1);
        Assert.Equal(new Vector2(8, 8), layout.Position);
        Assert.Equal(new Vector2(expectedWidth, expectedHeight), layout.Size);
    }

    [Theory]
    [InlineData(-100, -42)]
    [InlineData(1000, 142)]
    public void DockedHeightClampsAgainstBothViewportEdges(float y, float expectedY)
    {
        var layout = DrawerPlacementPolicy.Plan(new(100, y), new(900, 800), new(-100, -50),
            new(3000, 1000), new(950, 800), 1);
        Assert.Equal(expectedY, layout.Position.Y);
    }

    [Theory]
    [InlineData(584, true)]
    [InlineData(583, false)]
    public void NativeCreationFitIncludesItsExactRightMargin(float width, bool fits)
    {
        var layout = DrawerPlacementPolicy.Plan(new(0, 10), new(100, 300), Vector2.Zero,
            new(width, 310), new(100, 300), 1);
        Assert.Equal(fits, layout.FitsBeside);
    }

    [Theory]
    [InlineData(-100, 8)]
    [InlineData(1000, 132)]
    public void FallbackClampsBothHorizontalEdges(float x, float expectedX)
    {
        var layout = DrawerPlacementPolicy.Plan(new(x, 50), new(900, 800), Vector2.Zero,
            new(500, 1000), new(950, 800), 1);
        Assert.Equal(expectedX, layout.Position.X);
    }

    [Theory]
    [InlineData(1076, 808)]
    [InlineData(1075, 8)]
    public void LowerFallbackRequiresItsWholeMinimumHeightAndMargin(float height, float expectedY)
    {
        var layout = DrawerPlacementPolicy.Plan(Vector2.Zero, new(900, 800), Vector2.Zero,
            new(2100, height), new(950, 800), 1);
        Assert.Equal(expectedY, layout.Position.Y);
    }
    [Fact]
    public void RememberedPlacementStaysInsideTheCurrentViewport()
    {
        Assert.Equal(new Vector2(-992, 58), DrawerPlacementPolicy.ClampPosition(
            new(-3000, -100), new(360, 650), new(-1000, 50), new(2000, 1000)));
        Assert.Equal(new Vector2(632, 392), DrawerPlacementPolicy.ClampPosition(
            new(3000, 3000), new(360, 650), new(-1000, 50), new(2000, 1000)));
        Assert.Equal(new Vector2(20, 80), DrawerPlacementPolicy.ClampPosition(
            new(20, 80), new(360, 650), Vector2.Zero, new(2000, 1000)));
    }
    [Fact]
    public void NonFiniteScaleFallsBackToNativeScale()
    {
        var result = DrawerPlacementPolicy.Plan(Vector2.Zero, new(900, 800), Vector2.Zero,
            new(3000, 1100), new(950, 800), float.NaN);
        Assert.Equal(360, result.Size.X);
    }
    [Fact]
    public void WideLayoutKeepsNativeSizesAndInsertsDrawerBetweenThem()
    {
        var result = DrawerPlacementPolicy.Plan(new(100, 50), new(900, 800), Vector2.Zero,
            new(3000, 1100), new(950, 800), 1);
        Assert.True(result.FitsBeside);
        Assert.Equal(new Vector2(1008, 50), result.Position);
        Assert.Equal(new Vector2(360, 800), result.Size);
        Assert.Equal(new Vector2(1376, 50), result.CreationPosition);
    }

    [Fact]
    public void NarrowLayoutDoesNotPretendNativePairFits()
    {
        var result = DrawerPlacementPolicy.Plan(new(100, 50), new(900, 800), Vector2.Zero,
            new(2100, 1037), new(950, 800), 1);
        Assert.False(result.FitsBeside);
        Assert.Equal(108, result.Position.X);
        Assert.True(result.Position.Y + result.Size.Y <= 1037);
    }

    [Fact]
    public void AvailableSpaceBelowTheNativePairTakesPrecedenceOverOverlay()
    {
        var result = DrawerPlacementPolicy.Plan(new(100, 20), new(900, 500), Vector2.Zero,
            new(2100, 1100), new(950, 500), 1);
        Assert.False(result.FitsBeside);
        Assert.Equal(528, result.Position.Y);
        Assert.Equal(564, result.Size.Y);
    }
}

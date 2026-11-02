namespace BetterGlamorPlates.Tests.Features.Main;

using BetterGlamorPlates.Features.Main;

/// <summary>Locks the generated mockup's reference landmarks and responsive floor.</summary>
public sealed class MainLayoutPolicyTests
{
    /// <summary>Collection tiles fill the viewport width and fit three complete rows.</summary>
    [Theory]
    [InlineData(680f, 300f, 8f)]
    [InlineData(960f, 400f, 12f)]
    [InlineData(1200f, 550f, 16f)]
    public void CollectionGridFillsWidth(float width, float height, float gap)
    {
        var grid = MainLayoutPolicy.CollectionGrid(width, height, gap);
        Assert.Equal(width, grid.Columns * grid.TileSize + (grid.Columns - 1) * gap, 2);
        Assert.True(3 * grid.TileSize + 2 * gap <= height + 0.01f);
        Assert.True(grid.TileSize > 70);
        Assert.Equal(height, 2 * grid.RowPitch + grid.TileSize, 2);
        Assert.True(3 * grid.RowPitch >= height);
    }

    /// <summary>Verifies the 1000 by 860 reference produces its 274px rail and ten columns.</summary>
    [Fact]
    public void ReferenceWindowProducesReferenceLandmarks()
    {
        var result = MainLayoutPolicy.Calculate(
            MainLayoutPolicy.ReferenceWidth - 20f,
            MainLayoutPolicy.ReferenceHeight - 64f);

        Assert.Equal(314f, result.RailWidth);
        Assert.Equal(740f, result.MainWidth);
        Assert.Equal(366f, result.PlatePanelHeight);
        Assert.Equal(350f, result.CollectionPanelHeight);
        Assert.Equal(9, result.CollectionColumns);
    }

    /// <summary>Verifies the minimum window retains useful rail, grid, and collection dimensions.</summary>
    [Fact]
    public void MinimumWindowRetainsUsableResponsiveLayout()
    {
        var result = MainLayoutPolicy.Calculate(
            MainLayoutPolicy.MinimumWidth - 24f,
            MainLayoutPolicy.MinimumHeight - 48f);

        Assert.InRange(result.RailWidth, 224f, 274f);
        Assert.True(result.MainWidth >= 550f);
        Assert.True(result.PlatePanelHeight >= 306f);
        Assert.True(result.CollectionPanelHeight >= 180f);
        Assert.True(result.CollectionColumns >= 7);
    }

    /// <summary>Verifies invalid geometry is normalized to the supported minimum.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidDimensionsRemainFiniteAndBounded(float value)
    {
        var result = MainLayoutPolicy.Calculate(value, value);

        Assert.True(float.IsFinite(result.RailWidth));
        Assert.True(float.IsFinite(result.MainWidth));
        Assert.True(float.IsFinite(result.PlatePanelHeight));
        Assert.True(float.IsFinite(result.CollectionPanelHeight));
        Assert.InRange(result.CollectionColumns, 7, 9);
    }

    /// <summary>Verifies grid boundary math used by row clipping.</summary>
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(9, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(1999, 10, 200)]
    [InlineData(2000, 10, 200)]
    public void RowCountUsesCeilingDivision(int items, int columns, int expected)
        => Assert.Equal(expected, MainLayoutPolicy.CalculateRowCount(items, columns));

    /// <summary>Verifies clipped coordinates map to stable item indices.</summary>
    [Theory]
    [InlineData(0, 0, 8, 0)]
    [InlineData(1, 0, 9, 9)]
    [InlineData(1, 9, 10, 19)]
    [InlineData(-1, 0, 10, -1)]
    [InlineData(0, 0, 0, -1)]
    public void ItemIndexMappingIsStable(int row, int column, int columns, int expected)
        => Assert.Equal(expected, MainLayoutPolicy.CalculateItemIndex(row, column, columns));
}

using System.Numerics;

namespace CollectionShopper.Tests;

public sealed class UiLayoutPolicyTests
{
    [Theory]
    [InlineData(16f, 1920f, 12f, 416f)]
    [InlineData(16f, 300f, 12f, 256f)]
    [InlineData(16f, 45f, 12f, 1f)]
    [InlineData(16f, 20f, 12f, 1f)]
    [InlineData(16f, 460f, 12f, 416f)]
    public void TooltipRespectsFontAndViewportBounds(float font, float width, float padding, float expected)
    {
        // Arrange: measured font, viewport, and style padding.
        // Act
        var actual = UiLayoutPolicy.TooltipWidth(font, width, padding);
        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(100f, 0f, 1f, 52f, 249.5f)]
    [InlineData(50f, 0f, 1f, 2f, 249.5f)]
    [InlineData(49f, 0f, 1f, 44f, 249.5f)]
    [InlineData(100f, 20f, 2f, 44f, 199f)]
    [InlineData(200f, 0f, 2f, 104f, 199f)]
    public void RailCentersAndFallsInsideOnlyWhenOutsideViewport(float frameX, float viewportLeft,
        float scale, float expectedX, float expectedY)
    {
        // Arrange
        var market = new Vector2(40f, 90f);
        var frame = new Vector2(frameX, 100f);
        // Act
        var actual = MarketBoardTabPresentation.RailPosition(market, frame, 400f, viewportLeft, 2, scale);
        // Assert
        Assert.Equal(new Vector2(expectedX, expectedY), actual);
    }

    [Fact]
    public void MovingMarketAndViewportTogetherMovesRailBySameDistance()
    {
        // Arrange
        var movement = new Vector2(43f, -19f);
        var initial = MarketBoardTabPresentation.RailPosition(new(100f, 100f), new(100f, 100f), 700f, 0f, 10, 1.5f);
        // Act
        var moved = MarketBoardTabPresentation.RailPosition(new Vector2(100f, 100f) + movement,
            new Vector2(100f, 100f) + movement, 700f, movement.X, 10, 1.5f);
        // Assert
        Assert.Equal(initial + movement, moved);
    }
}

namespace CollectionShopper.Tests;

/// <summary>
/// Verifies price estimates follow the live world where the player is currently shopping.
/// </summary>
public sealed class MarketWorldPolicyTests
{
    [Theory]
    [InlineData("Alpha", "Alpha", true)]
    [InlineData("Alpha", "Beta", false)]
    [InlineData("Alpha", null, false)]
    [InlineData(null, null, false)]
    [InlineData(" ", " ", false)]
    public void DelayedPriceResultsRequireTheRequestedWorld(string? requested, string? current, bool expected)
        => Assert.Equal(expected, MarketWorldPolicy.Matches(requested, current));

    /// <summary>
    /// Verifies a non-empty live current world is selected.
    /// </summary>
    [Fact]
    public void CurrentWorldIsSelected()
    {
        // Arrange

        // Act
        var market = MarketWorldPolicy.Select("Current World");

        // Assert
        Assert.Equal("Current World", market);
    }

    /// <summary>
    /// Verifies price lookup is skipped without live current-world data.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoMarketIsSelectedWithoutCurrentWorld(string? currentWorld)
    {
        var market = MarketWorldPolicy.Select(currentWorld);

        Assert.Null(market);
    }
}

namespace CollectionShopper.Tests;

/// <summary>
/// Verifies the bookmark rail appears only while browsing the public Market Board.
/// </summary>
public sealed class MarketBoardContextTests
{
    /// <summary>
    /// Verifies the addon visibility boundary is required explicitly.
    /// </summary>
    [Fact]
    public void VisibilityReaderRejectsMissingBoundary()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            MarketBoardContext.IsBrowsing(null!));

        Assert.Equal("isAddonVisible", exception.ParamName);
    }

    /// <summary>
    /// Verifies every runtime gate reads the complete canonical addon set.
    /// </summary>
    [Fact]
    public void VisibilityReaderUsesCanonicalAddonNames()
    {
        // Arrange
        var requested = new List<string>();

        // Act
        var browsing = MarketBoardContext.IsBrowsing(name =>
        {
            requested.Add(name);
            return name == "ItemSearch";
        });

        // Assert
        Assert.True(browsing);
        Assert.Equal(["ItemSearch", "ItemSearchResult", "RetainerSell"], requested);
    }

    /// <summary>
    /// Verifies retainer price comparison suppresses the rail even though it reuses
    /// the public Market Board results addon.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, true, false)]
    public void BrowsingRequiresMarketBoardWithoutRetainerPricing(
        bool itemSearchVisible,
        bool itemSearchResultVisible,
        bool retainerSellVisible,
        bool expected)
    {
        // Arrange

        // Act
        var browsing = MarketBoardContext.IsBrowsing(
            itemSearchVisible,
            itemSearchResultVisible,
            retainerSellVisible);

        // Assert
        Assert.Equal(expected, browsing);
    }
}

namespace CollectionShopper.Tests;

public sealed class MarketPricePublicationTests
{
    [Theory]
    [InlineData("Mateus", "Mateus", false)]
    [InlineData("Mateus", "Brynhildr", true)]
    [InlineData("Mateus", null, true)]
    [InlineData(null, null, true)]
    public void PublicationRejectsPricesAfterTravelOrLogout(string? requested, string? current, bool refresh)
    {
        // Arrange
        var prices = new Dictionary<uint, MarketPriceQuote> { [1] = new(new(1, 42, 1, "Mateus", false), null) };
        // Act
        var publication = MarketPricePublication.Validate(requested, current, prices);
        // Assert
        Assert.Equal(refresh, publication.RequestRefresh);
        Assert.Equal(refresh ? null : requested, publication.Market);
        if (refresh) Assert.Empty(publication.Prices);
        else Assert.Same(prices, publication.Prices);
    }

    [Theory]
    [InlineData("Mateus", true)]
    [InlineData("Brynhildr", false)]
    [InlineData(null, false)]
    public void PartialFailureRetainsPreviousQuotesOnlyForTheRequestedWorld(string? previousMarket, bool retained)
    {
        // Arrange
        var quote = new MarketPriceQuote(new(1, 42, 1, "Mateus", false), null);
        var previous = new Dictionary<uint, MarketPriceQuote> { [1] = quote, [2] = quote };
        var result = new MarketPriceRefreshResult(new Dictionary<uint, MarketPriceQuote> { [3] = quote }, new uint[] { 1 });
        // Act
        var merged = MarketPricePublication.Merge(result, "Mateus", previousMarket, previous);
        // Assert
        Assert.Equal(retained, merged.ContainsKey(1));
        Assert.False(merged.ContainsKey(2));
        Assert.Same(quote, merged[3]);
        Assert.Equal(2, previous.Count);
    }
}

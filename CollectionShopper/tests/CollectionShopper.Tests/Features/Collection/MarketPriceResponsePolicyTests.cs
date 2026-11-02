using System.Text.Json;

namespace CollectionShopper.Tests;

public sealed class MarketPriceResponsePolicyTests
{
    [Fact]
    public void AddAggregatedMinimumsRequiresDestinationAndMarket()
    {
        using var document = JsonDocument.Parse("{}");

        var missingDestination = Assert.Throws<ArgumentNullException>(() =>
            MarketPriceResponsePolicy.AddAggregatedMinimums(
                null!,
                "Example",
                document.RootElement));
        var missingMarket = Assert.Throws<ArgumentNullException>(() =>
            MarketPriceResponsePolicy.AddAggregatedMinimums(
                new Dictionary<uint, MarketPriceQuote>(),
                null!,
                document.RootElement));
        var blankMarket = Assert.Throws<ArgumentException>(() =>
            MarketPriceResponsePolicy.AddAggregatedMinimums(
                new Dictionary<uint, MarketPriceQuote>(),
                " ",
                document.RootElement));

        Assert.Equal("prices", missingDestination.ParamName);
        Assert.Equal("market", missingMarket.ParamName);
        Assert.Equal("market", blankMarket.ParamName);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"itemId\":0,\"nq\":{\"minListing\":{\"world\":{\"price\":1}}}}")]
    [InlineData("{\"itemId\":null,\"nq\":{\"minListing\":{\"world\":{\"price\":1}}}}")]
    public void AddAggregatedMinimumsRejectsInvalidItemIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        var prices = new Dictionary<uint, MarketPriceQuote>();

        MarketPriceResponsePolicy.AddAggregatedMinimums(prices, "Example", document.RootElement);

        Assert.Empty(prices);
    }

    [Fact]
    public void AddAggregatedMinimumsMapsBothQualityMinimums()
    {
        using var document = JsonDocument.Parse("""
            {
              "itemId": 42,
              "nq": { "minListing": { "world": { "price": 200 } } },
              "hq": { "minListing": { "world": { "price": 150 } } }
            }
            """);
        var prices = new Dictionary<uint, MarketPriceQuote>();

        MarketPriceResponsePolicy.AddAggregatedMinimums(prices, "Example", document.RootElement);

        var quote = Assert.Single(prices).Value;
        Assert.Equal(new MarketPrice(42, 200, 1, "Example", false), quote.NormalQuality);
        Assert.Equal(new MarketPrice(42, 150, 1, "Example", true), quote.HighQuality);
    }

    [Fact]
    public void AddCheapestListingRejectsMissingDestination()
    {
        using var document = JsonDocument.Parse("{}");

        var exception = Assert.Throws<ArgumentNullException>(() =>
            MarketPriceResponsePolicy.AddCheapestListing(null!, 42, document.RootElement));

        Assert.Equal("prices", exception.ParamName);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"listings\":null}")]
    [InlineData("{\"listings\":{}}")]
    public void AddCheapestListingIgnoresMalformedContainers(string json)
    {
        using var document = JsonDocument.Parse(json);
        var prices = new Dictionary<uint, MarketPriceQuote>();

        MarketPriceResponsePolicy.AddCheapestListing(prices, 42, document.RootElement);

        Assert.Empty(prices);
    }

    [Fact]
    public void AddCheapestListingMapsExactSafeQuoteAndIgnoresMalformedEntries()
    {
        using var document = JsonDocument.Parse("""
            {
              "listings": [
                null,
                {},
                { "pricePerUnit": 0, "quantity": 1 },
                { "pricePerUnit": 1, "quantity": 2 },
                { "pricePerUnit": 2, "quantity": 1, "hq": null },
                { "pricePerUnit": 300, "quantity": 1, "worldName": 7, "hq": false },
                { "pricePerUnit": 200, "quantity": 1, "worldName": "NQ World", "hq": false },
                { "pricePerUnit": 150, "quantity": 1, "worldName": "HQ World", "hq": true }
              ]
            }
            """);
        var prices = new Dictionary<uint, MarketPriceQuote>();

        MarketPriceResponsePolicy.AddCheapestListing(prices, 42, document.RootElement);

        var quote = Assert.Single(prices).Value;
        Assert.Equal(new MarketPrice(42, 200, 1, "NQ World", false), quote.NormalQuality);
        Assert.Equal(new MarketPrice(42, 150, 1, "HQ World", true), quote.HighQuality);
    }

    [Fact]
    public void GeneratedListingsSelectIndependentQualityMinimumsDeterministically()
    {
        var random = new Random(20260901);
        for (var iteration = 0; iteration < 256; iteration++)
        {
            var listings = Enumerable.Range(0, 32)
                .Select(index => new GeneratedListing(
                    (uint)random.Next(1, 100_000),
                    (uint)(random.Next(4) == 0 ? 2 : 1),
                    index % 2 == 0,
                    $"World {index}"))
                .ToArray();
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { listings }));
            var first = new Dictionary<uint, MarketPriceQuote>();
            var second = new Dictionary<uint, MarketPriceQuote>();

            MarketPriceResponsePolicy.AddCheapestListing(first, 42, document.RootElement);
            MarketPriceResponsePolicy.AddCheapestListing(second, 42, document.RootElement);

            Assert.Equal(first, second);
            var expectedNq = listings.Where(item => item.quantity == 1 && !item.hq).Min(item => item.pricePerUnit);
            var expectedHq = listings.Where(item => item.quantity == 1 && item.hq).Min(item => item.pricePerUnit);
            var quote = Assert.Single(first).Value;
            Assert.Equal(expectedNq, quote.NormalQuality?.PricePerUnit);
            Assert.Equal(expectedHq, quote.HighQuality?.PricePerUnit);
        }
    }

    [Fact]
    public void MissingQualityFlagMeansNormalQuality()
    {
        using var document = JsonDocument.Parse("""{"listings":[{"pricePerUnit":100,"quantity":1,"worldName":"First"}]}""");
        var prices = new Dictionary<uint, MarketPriceQuote>();

        MarketPriceResponsePolicy.AddCheapestListing(prices, 42, document.RootElement);

        var quote = Assert.Single(prices).Value;
        Assert.Equal(new MarketPrice(42, 100, 1, "First", false), quote.NormalQuality);
        Assert.Null(quote.HighQuality);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualPricesRetainTheFirstWorld(bool highQuality)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            listings = new[]
            {
                new { pricePerUnit = 100, quantity = 1, worldName = "First", hq = highQuality },
                new { pricePerUnit = 100, quantity = 1, worldName = "Second", hq = highQuality },
            },
        }));
        var prices = new Dictionary<uint, MarketPriceQuote>();

        MarketPriceResponsePolicy.AddCheapestListing(prices, 42, document.RootElement);

        var quote = Assert.Single(prices).Value;
        Assert.Equal(new MarketPrice(42, 100, 1, "First", highQuality), highQuality ? quote.HighQuality : quote.NormalQuality);
        Assert.Null(highQuality ? quote.NormalQuality : quote.HighQuality);
    }

    private sealed record GeneratedListing(
        uint pricePerUnit,
        uint quantity,
        bool hq,
        string worldName);
}
